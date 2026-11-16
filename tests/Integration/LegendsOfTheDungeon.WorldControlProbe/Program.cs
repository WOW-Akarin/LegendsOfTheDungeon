using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

RunCheck("two-source-stun-and-temporary-tag-lease", ControlAndTags);
RunCheck("independent-four-relations-cycle-rejection-and-death-cleanup", RelationsAndDeath);
RunCheck("old-generation-handle-and-captured-lease-fail-closed", StaleAndGuards);
RunCheck("world-tick-control-tag-relation-replay-hash", Replay);
Console.WriteLine("P26_WORLD_CONTROL_INTEGRATION_PASS");

static void RunCheck(string name, Action check)
{
    check();
    Console.WriteLine("PASS " + name);
}

static void Require(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
}

static (WorldInstance World, WorldExecutor Executor) Create()
{
    var world = new WorldInstance(new WorldId(1), new WorldEpoch(1),
        new WorldOwnershipRegistry());
    var executor = new WorldExecutor(world.World, world.Epoch,
        new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
    Run(executor, ctx =>
    {
        Require(world.BeginLoading(ctx) && world.Start(ctx), "failed starting world");
    });
    return (world, executor);
}

static EntityIdentity Id(ulong value, ulong generation = 1) =>
    new(new WorldId(1), new WorldEpoch(1), new EntityId(value), new EntityGeneration(generation));

static void Spawn(WorldInstance world, IWorldWriteContext context, EntityIdentity entity) =>
    Require(world.Spawn(context, new WorldEntitySeed(entity,
        new Transform(new Position((double)entity.Id.Value, 0, 0), new Angle(0)))) ==
        WorldEntityResult.Spawned, "failed to spawn");

static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
{
    Require(executor.CreateTrustedProducer().Offer(new Work(action)) ==
        WorldAdmissionStatus.Accepted, "world producer failed to offer");
    Require(executor.RunBatch(1).Status == WorldRunStatus.Completed,
        "real WorldExecutor did not complete one callback");
}

static void ControlAndTags()
{
    var (world, executor) = Create();
    var entity = Id(1);
    var first = new EntityEffectSource(1);
    var second = new EntityEffectSource(2);
    EntityEffectHandle a = default;
    EntityEffectHandle b = default;
    EntityEffectHandle tag = default;
    Run(executor, ctx =>
    {
        Spawn(world, ctx, entity);
        Require(world.GrantControl(ctx, entity, first, EntityControlFlag.Stunned, out a) ==
            WorldEntityResult.ControlGranted, "first stun rejected");
        Require(world.GrantControl(ctx, entity, second, EntityControlFlag.Stunned, out b) ==
            WorldEntityResult.ControlGranted, "second stun rejected");
        Require(world.GrantTag(ctx, entity, first, "npc.elite", out tag) ==
            WorldEntityResult.TagGranted, "temporary tag rejected");
        Require(world.SetPermanentTag(ctx, entity, "npc", true) ==
            WorldEntityResult.TagChanged, "permanent tag rejected");
    });
    Require(world.TryGetSnapshot(entity.Id, out var snapshot) &&
        snapshot.Control == EntityControlFlag.Stunned &&
        snapshot.Tags.SequenceEqual(["npc", "npc.elite"]), "control/tag snapshot invalid");
    Run(executor, ctx =>
    {
        Require(world.RevokeControl(ctx, entity, a) == WorldEntityResult.ControlRevoked,
            "first stun revoke rejected");
        Require(world.RevokeTag(ctx, entity, tag) == WorldEntityResult.TagRevoked,
            "temporary tag revoke rejected");
    });
    Require(world.TryGetSnapshot(entity.Id, out snapshot) &&
        snapshot.Control == EntityControlFlag.Stunned && snapshot.Tags.SequenceEqual(["npc"]),
        "one stun source or permanent tag was mistakenly removed");
    Run(executor, ctx => Require(world.RevokeControl(ctx, entity, b) ==
        WorldEntityResult.ControlRevoked, "second stun revoke rejected"));
    Require(world.TryGetSnapshot(entity.Id, out snapshot) &&
        snapshot.Control == EntityControlFlag.None && snapshot.Tags.SequenceEqual(["npc"]),
        "last stun source cleanup failed");
}

static void RelationsAndDeath()
{
    var (world, executor) = Create();
    var child = Id(1);
    var owner = Id(2);
    var creator = Id(3);
    var controller = Id(4);
    var summoner = Id(5);
    Run(executor, ctx =>
    {
        foreach (var id in new[] { child, owner, creator, controller, summoner })
        {
            Spawn(world, ctx, id);
        }
        Require(world.SetRelation(ctx, child, EntityRelationKind.Owner, owner) ==
            WorldEntityResult.RelationChanged, "owner relation rejected");
        Require(world.SetRelation(ctx, child, EntityRelationKind.Creator, creator) ==
            WorldEntityResult.RelationChanged, "creator relation rejected");
        Require(world.SetRelation(ctx, child, EntityRelationKind.Controller, controller) ==
            WorldEntityResult.RelationChanged, "controller relation rejected");
        Require(world.SetRelation(ctx, child, EntityRelationKind.Summoner, summoner) ==
            WorldEntityResult.RelationChanged, "summoner relation rejected");
        Require(world.SetRelation(ctx, owner, EntityRelationKind.Creator, child) ==
            WorldEntityResult.RelationCycle, "cross-role owner-creator cycle permitted");
    });
    Require(world.TryGetSnapshot(child.Id, out var relations) &&
        relations.Relationships == new EntityRelations(owner, creator, controller, summoner),
        "relation roles were merged");
    Run(executor, ctx =>
    {
        Require(world.Despawn(ctx, owner) == WorldEntityResult.Despawned,
            "owner could not be retired");
        Require(world.Despawn(ctx, controller) == WorldEntityResult.Despawned,
            "controller could not be retired");
    });
    Require(world.TryGetSnapshot(child.Id, out relations) &&
        relations.Relationships == new EntityRelations(null, creator, null, summoner),
        "destroyed target left dangling relations");
}

static void StaleAndGuards()
{
    var (world, executor) = Create();
    var old = Id(1);
    var current = Id(1, 2);
    EntityEffectHandle expired = default;
    IWorldWriteContext? captured = null;
    Run(executor, ctx =>
    {
        Spawn(world, ctx, old);
        Require(world.GrantControl(ctx, old, new EntityEffectSource(1),
            EntityControlFlag.Rooted, out expired) == WorldEntityResult.ControlGranted,
            "first generation failed grant");
        Require(world.Remove(ctx, old) == WorldEntityResult.Removed, "failed retirement");
        Spawn(world, ctx, current);
        Require(world.GrantControl(ctx, current, new EntityEffectSource(2),
            EntityControlFlag.Rooted, out _) == WorldEntityResult.ControlGranted,
            "new generation grant rejected");
        Require(world.RevokeControl(ctx, current, expired) == WorldEntityResult.Stale,
            "old generation handle revoked fresh control");
        captured = ctx;
    });
    Require(world.TryGetSnapshot(current.Id, out var state) &&
        state.Control == EntityControlFlag.Rooted, "new root was revoked");
    try
    {
        world.GrantTag(captured!, current, new EntityEffectSource(8), "rooted", out _);
        throw new InvalidOperationException("expired lease unexpectedly mutated world");
    }
    catch (WorldAccessException ex) when (ex.Fault == WorldAccessFault.LeaseRequired)
    {
        // Expected: captured P18 lease no longer has world write capability.
    }
}

static void Replay()
{
    static IReadOnlyList<string> Sequence()
    {
        var (world, executor) = Create();
        var entity = Id(1);
        var owner = Id(2);
        Run(executor, ctx =>
        {
            Spawn(world, ctx, entity);
            Spawn(world, ctx, owner);
        });
        var clock = new FakeClock(new Duration(50),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var pipeline = new WorldTickPipeline(world, clock, []);
        clock.AdvanceTicks(4);
        var hashes = new List<string>();
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
        hashes.Add(pipeline.LastStateHash);
        Run(executor, ctx => Require(world.GrantControl(ctx, entity, new EntityEffectSource(1),
            EntityControlFlag.Stunned, out _) == WorldEntityResult.ControlGranted, "stun failed"));
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(2)));
        hashes.Add(pipeline.LastStateHash);
        Run(executor, ctx => Require(world.SetPermanentTag(ctx, entity, "npc", true) ==
            WorldEntityResult.TagChanged, "tag failed"));
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(3)));
        hashes.Add(pipeline.LastStateHash);
        Run(executor, ctx => Require(world.SetRelation(ctx, entity, EntityRelationKind.Creator, owner) ==
            WorldEntityResult.RelationChanged, "creator failed"));
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(4)));
        hashes.Add(pipeline.LastStateHash);
        return hashes;
    }
    var a = Sequence();
    var b = Sequence();
    Require(a.SequenceEqual(b) && a.Distinct().Count() == 4 &&
        a.All(hash => hash.Length == 64), "control/tag/relationship digest replay diverged");
}

internal sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => action(context);
}
