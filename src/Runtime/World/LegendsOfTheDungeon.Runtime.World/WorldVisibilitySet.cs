using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>距离/标签/生命/控制过滤的有界、不可变配置，所有标签采用 P26 规范。</summary>
public sealed class WorldVisibilityFilter
{
    /// <summary>构建只读过滤条件，自动按 Ordinal 排序、去重标签。</summary>
    /// <param name="radius">闭区间欧氏距离半径，单位米；最大 4096。</param>
    /// <param name="requiredTags">必须全部拥有的标签，可为空。</param>
    /// <param name="excludedTags">任意一个存在即不可见的标签，可为空。</param>
    /// <param name="requireAlive">只允许 Alive 目标；false 可包含已死亡但仍 Active 的实体。</param>
    /// <param name="excludedControls">存在任意这些控制状态则不可见。</param>
    /// <param name="maximumVisible">一次刷新最多接纳实体数量，1–1024。</param>
    public WorldVisibilityFilter(Distance radius, IEnumerable<string>? requiredTags = null,
        IEnumerable<string>? excludedTags = null, bool requireAlive = true,
        EntityControlEffects excludedControls = EntityControlEffects.None,
        int maximumVisible = 1024)
    {
        if (radius.Meters > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Visibility radius exceeds the 4096m budget.");
        }

        if (maximumVisible is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumVisible));
        }

        const EntityControlEffects allowed = EntityControlEffects.Stunned |
            EntityControlEffects.Rooted | EntityControlEffects.Silenced | EntityControlEffects.Feared;
        if ((excludedControls & ~allowed) != EntityControlEffects.None)
        {
            throw new ArgumentOutOfRangeException(nameof(excludedControls));
        }

        Radius = radius;
        RequireAlive = requireAlive;
        ExcludedControls = excludedControls;
        MaximumVisible = maximumVisible;
        RequiredTags = Normalize(requiredTags);
        ExcludedTags = Normalize(excludedTags);
        if (RequiredTags.Intersect(ExcludedTags, StringComparer.Ordinal).Any())
        {
            throw new ArgumentException("A tag cannot be simultaneously required and excluded.");
        }
    }

    /// <summary>最大查询距离。</summary>
    public Distance Radius { get; }

    /// <summary>必须同时存在的规范标签，不可变。</summary>
    public IReadOnlyList<string> RequiredTags { get; }

    /// <summary>有任一存在就排除的规范标签，不可变。</summary>
    public IReadOnlyList<string> ExcludedTags { get; }

    /// <summary>是否拒绝已死亡实体。</summary>
    public bool RequireAlive { get; }

    /// <summary>排除的控制状态位。</summary>
    public EntityControlEffects ExcludedControls { get; }

    /// <summary>可见集合容量上限。</summary>
    public int MaximumVisible { get; }

    internal bool Accept(WorldEntitySnapshot entity)
    {
        if (RequireAlive && entity.Life != LifeState.Alive)
        {
            return false;
        }

        if ((entity.Control & ExcludedControls) != EntityControlEffects.None)
        {
            return false;
        }

        return RequiredTags.All(tag => entity.Tags.Contains(tag, StringComparer.Ordinal)) &&
               !ExcludedTags.Any(tag => entity.Tags.Contains(tag, StringComparer.Ordinal));
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<string> Normalize(IEnumerable<string>? source)
    {
        var tags = source?.ToArray() ?? [];
        if (tags.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Maximum 64 visibility filter tags.");
        }

        foreach (var tag in tags)
        {
            EntityTagCapability.ValidateTag(tag);
        }

        return Array.AsReadOnly(tags.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray());
    }
}

/// <summary>供 P21 观察者使用的按值实体视图，不泄露 EntityCapabilities 或内部标签集合。</summary>
public readonly record struct WorldVisibilityMember
{
    internal WorldVisibilityMember(WorldEntitySnapshot entity)
    {
        Identity = entity.Identity;
        Transform = entity.Transform;
        Life = entity.Life;
        Control = entity.Control;
        Tags = Array.AsReadOnly(entity.Tags.ToArray());
    }

    /// <summary>目标完整代际，禁止仅通过 EntityId 追踪。</summary>
    public EntityIdentity Identity { get; }

    /// <summary>所见位置的唯一 Transform 按值快照。</summary>
    public Transform Transform { get; }

    /// <summary>所见目标的 Life 状态。</summary>
    public LifeState Life { get; }

    /// <summary>所见目标的有效控制状态。</summary>
    public EntityControlEffects Control { get; }

    /// <summary>有效标签的不可变快照。</summary>
    public IReadOnlyList<string> Tags { get; }
}

/// <summary>某观察者一次成功重计算后的完整只读视图，带 World/Epoch/本 Set 快照版本。</summary>
public sealed class WorldVisibilityView
{
    internal WorldVisibilityView(EntityIdentity observer, ulong version, bool observerActive,
        IReadOnlyList<WorldVisibilityMember> members)
    {
        Observer = observer;
        World = observer.World;
        Epoch = observer.Epoch;
        SnapshotVersion = version;
        ObserverActive = observerActive;
        Members = members;
    }

    /// <summary>观察者所属的 World。</summary>
    public WorldId World { get; }

    /// <summary>世界的不可变运行代际。</summary>
    public WorldEpoch Epoch { get; }

    /// <summary>固定观察者身份（含实体代际）。</summary>
    public EntityIdentity Observer { get; }

    /// <summary>每个 VisibilitySet 单独递增的成功重计算版本；0 表示未计算。</summary>
    public ulong SnapshotVersion { get; }

    /// <summary>观察者是否仍是本世界 Active 且 Alive 的当前代际。</summary>
    public bool ObserverActive { get; }

    /// <summary>按 EntityId 升序冻结的可见实体；不含观察者本人。</summary>
    public IReadOnlyList<WorldVisibilityMember> Members { get; }
}

/// <summary>一次版本转移的不可变差量，精确区分进入实体与离开身份。</summary>
public sealed class WorldVisibilityDelta
{
    internal WorldVisibilityDelta(WorldVisibilityView view, ulong previousVersion,
        IReadOnlyList<WorldVisibilityMember> entered, IReadOnlyList<EntityIdentity> left)
    {
        View = view;
        World = view.World;
        Epoch = view.Epoch;
        Observer = view.Observer;
        PreviousVersion = previousVersion;
        SnapshotVersion = view.SnapshotVersion;
        Entered = entered;
        Left = left;
    }

    /// <summary>世界标识。</summary>
    public WorldId World { get; }

    /// <summary>世界运行代际。</summary>
    public WorldEpoch Epoch { get; }

    /// <summary>观察者完整代际。</summary>
    public EntityIdentity Observer { get; }

    /// <summary>上一快照版本。</summary>
    public ulong PreviousVersion { get; }

    /// <summary>本次提交的快照版本。</summary>
    public ulong SnapshotVersion { get; }

    /// <summary>本次进入的实体视图，按 EntityId 升序。</summary>
    public IReadOnlyList<WorldVisibilityMember> Entered { get; }

    /// <summary>本次离开的完整实体身份，按 EntityId 升序。</summary>
    public IReadOnlyList<EntityIdentity> Left { get; }

    /// <summary>最新完整视图，包含移动但未发生 Enter/Leave 的成员新坐标。</summary>
    public WorldVisibilityView View { get; }
}

/// <summary>
/// 仅持有单个观察者的派生、版本化 VisibilitySet；真实实体状态始终由 P17/P20/P26 拥有。
/// 外部可读取上次已提交视图，但只有 P18 同步 World Scope 可以刷新和发布 Delta。
/// </summary>
public sealed class WorldVisibilitySet
{
    private readonly object _gate = new();
    private readonly WorldInstance _world;
    private readonly WorldVisibilityFilter _filter;
    private WorldVisibilityView _current;

    /// <summary>绑定固定 World/Observer 与不可变过滤条件，不声称构造时观察者已经存在。</summary>
    /// <param name="world">拥有 P17/P20/P26 实体状态的唯一世界。</param>
    /// <param name="observer">当前世界的完整观察者代际。</param>
    /// <param name="filter">冻结的查询规则。</param>
    public WorldVisibilitySet(WorldInstance world, EntityIdentity observer,
        WorldVisibilityFilter filter)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(filter);
        if (observer.IsEmpty || observer.World != world.World || observer.Epoch != world.Epoch)
        {
            throw new ArgumentException("Observer must match World/Epoch and have complete identity.", nameof(observer));
        }

        _world = world;
        _filter = filter;
        _current = new WorldVisibilityView(observer, 0, false,
            Array.AsReadOnly(Array.Empty<WorldVisibilityMember>()));
    }

    /// <summary>固定观察者完整代际。</summary>
    public EntityIdentity Observer => _current.Observer;

    /// <summary>只读过滤配置；没有动态绕过 P26 状态验证的写入入口。</summary>
    public WorldVisibilityFilter Filter => _filter;

    /// <summary>上一次提交的冻结视图；刷新前它可能是旧版本，不保证实时状态。</summary>
    public WorldVisibilityView Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// 在 P18 真实同步 World Scope 中从 P20 空间范围和 P26 状态重建最新视图，
    /// 精确计算 Enter/Leave；重复刷新相同成员绝不重复 Enter。
    /// </summary>
    /// <param name="context">本世界当期真实的 P18 同步写租约。</param>
    /// <returns>带连续版本的 Enter/Leave 以及完整只读视图。</returns>
    public WorldVisibilityDelta Refresh(IWorldWriteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DemandWorld(_world.World, _world.Epoch);
        lock (_gate)
        {
            if (_world.State == WorldLifecycle.Faulted)
            {
                throw new InvalidOperationException("Faulted world cannot issue trustworthy visibility deltas.");
            }

            var prior = _current;
            if (prior.SnapshotVersion == ulong.MaxValue)
            {
                throw new InvalidOperationException("Visibility snapshot version exhausted.");
            }

            WorldEntitySnapshot observer = default;
            var observerActive = _world.State == WorldLifecycle.Running &&
                _world.TryGetSnapshot(prior.Observer.Id, out observer) &&
                observer.Identity == prior.Observer &&
                observer.Lifecycle == WorldEntityLifecycle.Active &&
                observer.Life == LifeState.Alive;

            var members = new List<WorldVisibilityMember>();
            if (observerActive)
            {
                foreach (var candidate in _world.QueryNearby(observer.Transform.Position, _filter.Radius))
                {
                    if (candidate.Identity == prior.Observer || !_filter.Accept(candidate))
                    {
                        continue;
                    }

                    if (members.Count >= _filter.MaximumVisible)
                    {
                        throw new InvalidOperationException("Visibility capacity exceeded; no partial view published.");
                    }

                    members.Add(new WorldVisibilityMember(candidate));
                }
            }

            // QueryNearby already uses ordinal EntityId ordering, but sorting here freezes the P21 contract.
            members.Sort(static (a, b) => a.Identity.Id.Value.CompareTo(b.Identity.Id.Value));
            var priorIds = new HashSet<EntityIdentity>(prior.Members.Select(member => member.Identity));
            var latestIds = new HashSet<EntityIdentity>(members.Select(member => member.Identity));
            var entered = members.Where(member => !priorIds.Contains(member.Identity)).ToArray();
            var left = prior.Members.Where(member => !latestIds.Contains(member.Identity))
                .Select(member => member.Identity)
                .OrderBy(identity => identity.Id.Value)
                .ThenBy(identity => identity.Generation.Value)
                .ToArray();

            var next = new WorldVisibilityView(prior.Observer, prior.SnapshotVersion + 1,
                observerActive, Array.AsReadOnly(members.ToArray()));
            var delta = new WorldVisibilityDelta(next, prior.SnapshotVersion,
                Array.AsReadOnly(entered), Array.AsReadOnly(left));
            _current = next;
            return delta;
        }
    }
}
