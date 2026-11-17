using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;
using LegendsOfTheDungeon.Security.Contracts;

RunCheck("real-world-modifier-source-revoke-and-recalculation", SourceIsolation);
RunCheck("max-stat-reduction-resource-clamp-and-death-guard", ResourceLifecycle);
RunCheck("expired-lease-and-generation-handle-rejection", StaleGeneration);
RunCheck("overflow-rejected-without-state-or-sequence-change", AtomicOverflow);
RunCheck("p19-deterministic-source-sensitive-attribute-replay", Replay);
Console.WriteLine("P25_ATTRIBUTES_INTEGRATION_PASS");

static void RunCheck(string name, Action check)
{
    check();
    Console.WriteLine("PASS " + name);
}

static void Require(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
}

static EntityIdentity Identity(ulong generation = 1) =>
    new(new WorldId(19), new WorldEpoch(7), new EntityId(1), new EntityGeneration(generation));

static (WorldInstance World, WorldExecutor Executor, EntityIdentity Entity) Create()
{
    var world = new WorldInstance(new WorldId(19), new WorldEpoch(7), new WorldOwnershipRegistry());
    var executor = new WorldExecutor(world.World, world.Epoch,
        new DenyByDefaultCommandAuthorizer(), 128, 16, 2);
    Run(executor, ctx =>
    {
        Require(world.BeginLoading(ctx) && world.Start(ctx), "world startup rejected");
        Require(world.Spawn(ctx, new WorldEntitySeed(Identity(),
            new Transform(new Position(1, 2, 3), new Angle(0)))) == WorldEntityResult.Spawned,
            "actual WorldInstance entity spawn failed");
    });
    return (world, executor, Identity());
}

static void Run(WorldExecutor executor, Action<IWorldWriteContext> action)
{
    Require(executor.CreateTrustedProducer().Offer(new Work(action)) ==
        WorldAdmissionStatus.Accepted, "trusted queue rejected");
    Require(executor.RunBatch(1).Status == WorldRunStatus.Completed,
        "real WorldExecutor callback did not complete");
}

static EntityAttributeSnapshot Read(WorldInstance world, EntityIdentity identity)
{
    Require(world.TryGetAttributes(identity, out var result) && result is not null,
        "canonical attribute snapshot unavailable");
    return result!;
}

static decimal Stat(EntityAttributeSnapshot snapshot, string key) =>
    snapshot.Stats.Single(v => v.Key == new AttributeKey(key)).EffectiveValue;

static AttributeResourceSnapshot Resource(EntityAttributeSnapshot snapshot) =>
    snapshot.Resources.Single(v => v.Key == new AttributeKey("energy"));

static void SourceIsolation()
{
    var (world, executor, entity) = Create();
    var baseKey = new AttributeKey("power");
    var derived = new AttributeKey("damage");
    AttributeModifierHandle a = default;
    AttributeModifierHandle b = default;
    Run(executor, ctx =>
    {
        Require(world.SetBaseAttribute(ctx, entity, baseKey, 100) == WorldEntityResult.AttributeChanged,
            "base set failed");
        Require(world.DefineDerivedAttribute(ctx, entity, derived, baseKey, 2, 5) ==
            WorldEntityResult.AttributeChanged, "derived definition failed");
        Require(world.GrantAttributeModifier(ctx, entity, new AttributeModifierSource(1),
            baseKey, AttributeModifierOperation.Flat, 10, 0, out a) ==
            WorldEntityResult.ModifierAdded, "source A grant failed");
        Require(world.GrantAttributeModifier(ctx, entity, new AttributeModifierSource(2),
            baseKey, AttributeModifierOperation.Percent, 50, 10, out b) ==
            WorldEntityResult.ModifierAdded, "source B grant failed");
    });
    var saved = Read(world, entity);
    Require(Stat(saved, "power") == 165 && Stat(saved, "damage") == 335,
        "derived recompute incorrect");
    Run(executor, ctx =>
    {
        Require(world.RevokeAttributeModifier(ctx, entity, a) == WorldEntityResult.ModifierRemoved,
            "precise revocation failed");
        Require(world.RevokeAttributeModifier(ctx, entity, a) == WorldEntityResult.AlreadyCompleted,
            "duplicate revocation not idempotent");
    });
    Require(Stat(Read(world, entity), "power") == 150 &&
        Stat(Read(world, entity), "damage") == 305 &&
        Stat(saved, "power") == 165 && b.Sequence == 2,
        "other source accidentally lost or snapshot mutated");
}

static void ResourceLifecycle()
{
    var (world, executor, entity) = Create();
    var maxKey = new AttributeKey("energy.max");
    var energy = new AttributeKey("energy");
    AttributeModifierHandle reduce = default;
    Run(executor, ctx =>
    {
        world.SetBaseAttribute(ctx, entity, maxKey, 100);
        world.DefineEntityResource(ctx, entity, energy, maxKey, 80);
        world.GrantAttributeModifier(ctx, entity, new AttributeModifierSource(1),
            maxKey, AttributeModifierOperation.Flat, -60, 0, out reduce);
    });
    Require(Resource(Read(world, entity)) is { Maximum: 40m, Current: 40m },
        "maximum shrink did not clamp resource");
    Run(executor, ctx =>
    {
        world.RevokeAttributeModifier(ctx, entity, reduce);
        Require(world.ChangeEntityResource(ctx, entity, energy, 61) ==
            WorldEntityResult.ResourceOutOfRange, "excess refill unexpectedly succeeded");
        Require(world.MarkDead(ctx, entity) == WorldEntityResult.LifeChanged,
            "canonical entity death rejected");
        Require(world.ChangeEntityResource(ctx, entity, energy, -5) ==
            WorldEntityResult.InvalidState, "dead entity consumed resource");
        Require(world.Revive(ctx, entity) == WorldEntityResult.LifeChanged,
            "canonical entity revive rejected");
    });
    Require(Resource(Read(world, entity)) is { Maximum: 100m, Current: 40m },
        "maximum restoration incorrectly healed resource");
}

static void StaleGeneration()
{
    var (world, executor, old) = Create();
    AttributeModifierHandle handle = default;
    IWorldWriteContext? expired = null;
    Run(executor, ctx =>
    {
        world.SetBaseAttribute(ctx, old, new AttributeKey("power"), 10);
        world.GrantAttributeModifier(ctx, old, new AttributeModifierSource(1),
            new AttributeKey("power"), AttributeModifierOperation.Flat, 4, 0, out handle);
        Require(world.Remove(ctx, old) == WorldEntityResult.Removed, "old generation removal failed");
        Require(world.Spawn(ctx, new WorldEntitySeed(Identity(2),
            new Transform(new Position(5, 2, 3), new Angle(0)))) == WorldEntityResult.Spawned,
            "new generation spawn failed");
        world.SetBaseAttribute(ctx, Identity(2), new AttributeKey("power"), 20);
        Require(world.RevokeAttributeModifier(ctx, Identity(2), handle) == WorldEntityResult.Stale,
            "stale modifier handle revoked next generation");
        expired = ctx;
    });
    Require(Stat(Read(world, Identity(2)), "power") == 20, "new entity overwritten");
    try
    {
        world.SetBaseAttribute(expired!, Identity(2), new AttributeKey("power"), 99);
        throw new InvalidOperationException("expired lease unexpectedly succeeded");
    }
    catch (WorldAccessException ex) when (ex.Fault == WorldAccessFault.LeaseRequired)
    {
        // 同步 Scope 之外写入被真实 P18 Guard 拒绝。
    }
}

static void AtomicOverflow()
{
    var (world, executor, entity) = Create();
    Run(executor, ctx =>
    {
        world.SetBaseAttribute(ctx, entity, new AttributeKey("power"), 1_000_000_000);
        try
        {
            world.GrantAttributeModifier(ctx, entity, new AttributeModifierSource(1),
                new AttributeKey("power"), AttributeModifierOperation.Flat, 1, 0, out _);
            throw new InvalidOperationException("overflow unexpectedly succeeded");
        }
        catch (OverflowException)
        {
            // Precommit 校验拒绝，不能消耗 Source 句柄。
        }
    });
    var snapshot = Read(world, entity);
    Require(snapshot.LastModifierSequence == 0 && snapshot.Modifiers.Count == 0 &&
        Stat(snapshot, "power") == 1_000_000_000, "failed modifier mutated state");
}

static void Replay()
{
    static string Hash(ulong source)
    {
        var (world, executor, entity) = Create();
        Run(executor, ctx =>
        {
            world.SetBaseAttribute(ctx, entity, new AttributeKey("energy.max"), 100);
            world.DefineEntityResource(ctx, entity, new AttributeKey("energy"),
                new AttributeKey("energy.max"), 80);
            world.GrantAttributeModifier(ctx, entity, new AttributeModifierSource(source),
                new AttributeKey("energy.max"), AttributeModifierOperation.Flat, -20, 0, out _);
        });
        var clock = new FakeClock(new Duration(50),
            new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
        var pipeline = new WorldTickPipeline(world, clock, []);
        clock.AdvanceTicks(1);
        Run(executor, ctx => pipeline.Execute(ctx, new Tick(1)));
        return pipeline.LastStateHash;
    }

    Require(Hash(1) == Hash(1), "same sequence hash differs");
    Require(Hash(1) != Hash(2), "different source ledger produced same replay hash");
}

sealed class Work(Action<IWorldWriteContext> action) : IWorldAction
{
    public void Execute(IWorldWriteContext context) => action(context);
}
