using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>明确的世界生命周期；不可恢复错误进入 Faulted，不能伪装停止成功。</summary>
public enum WorldLifecycle
{
    /// <summary>构造完成但未加载。</summary>
    Created,
    /// <summary>准备基础资源。</summary>
    Loading,
    /// <summary>世界允许实体命令。</summary>
    Running,
    /// <summary>不再允许创建或新目标，允许移除和停机清理。</summary>
    Draining,
    /// <summary>全部实体已退休并解除世界归属。</summary>
    Stopped,
    /// <summary>不可恢复世界错误，等待可信 Host 恢复。</summary>
    Faulted
}

/// <summary>注册实体的最小生命周期；Removing 为同步清理过程中的隔离态。</summary>
public enum WorldEntityLifecycle
{
    /// <summary>尚未注册的不可变创建意图。</summary>
    Created,
    /// <summary>已绑定世界和 Generation，但尚未激活。</summary>
    Attached,
    /// <summary>正常存活的注册实体。</summary>
    Active,
    /// <summary>正在同步撤销关联资源。</summary>
    Removing,
    /// <summary>已释放世界归属但留有代际墓碑。</summary>
    Detached,
    /// <summary>显式销毁，旧代际不得再使用。</summary>
    Disposed
}

/// <summary>世界和实体命令的明确结果，不会将重复或非法状态宣称为成功。</summary>
public enum WorldEntityResult
{
    /// <summary>实体已附加。</summary>
    Added,
    /// <summary>实体已激活。</summary>
    Activated,
    /// <summary>实体创建并激活。</summary>
    Spawned,
    /// <summary>实体已从世界移除。</summary>
    Removed,
    /// <summary>实体已销毁。</summary>
    Despawned,
    /// <summary>实体重定位。</summary>
    Relocated,
    /// <summary>实体目标已更新或清空。</summary>
    TargetChanged,
    /// <summary>生命状态发生改变。</summary>
    LifeChanged,
    /// <summary>已登记清理钩子。</summary>
    CleanupRegistered,
    /// <summary>同一标识在当前世界已注册。</summary>
    AlreadyExists,
    /// <summary>生命周期动作已经完成，不重复执行副作用。</summary>
    AlreadyCompleted,
    /// <summary>目标实体或本实体不存在。</summary>
    NotFound,
    /// <summary>当前世界或实体不允许该转换。</summary>
    InvalidState,
    /// <summary>WorldEpoch、Generation 或快照已过期。</summary>
    Stale,
    /// <summary>实体属于另一个世界或 ID 被别的世界占用。</summary>
    WorldConflict,
    /// <summary>目标不存在、已失效或不处于 Alive+Active。</summary>
    TargetUnavailable,
    /// <summary>已达到有界实体/回调预算。</summary>
    CapacityExceeded,
    /// <summary>当前版本明确不支持跨世界转移。</summary>
    TransferNotSupported,
    /// <summary>坐标落在可表示的 Grid 边界之外。</summary>
    SpatialOutOfBounds,
    /// <summary>目标 Cell 已显式卸载，必须重新激活。</summary>
    CellUnloaded,
    /// <summary>新控制来源已授予。</summary>
    ControlGranted,
    /// <summary>指定控制来源已撤销。</summary>
    ControlRevoked,
    /// <summary>临时标签来源已授予。</summary>
    TagGranted,
    /// <summary>临时标签来源已撤销。</summary>
    TagRevoked,
    /// <summary>永久标签状态已修改。</summary>
    TagChanged,
    /// <summary>一个关系角色已更新。</summary>
    RelationChanged,
    /// <summary>关系目标不存在、过期、已退休或属于其它世界。</summary>
    RelationUnavailable,
    /// <summary>关系会造成跨角色循环或自指。</summary>
    RelationCycle,
    /// <summary>基础或派生属性已更新。</summary>
    AttributeChanged,
    /// <summary>临时属性修饰成功授予。</summary>
    ModifierAdded,
    /// <summary>精确来源属性修饰已撤销。</summary>
    ModifierRemoved,
    /// <summary>资源当前值确实改变。</summary>
    ResourceChanged,
    /// <summary>资源超出当前最大值或小于零。</summary>
    ResourceOutOfRange,
    /// <summary>P27 移动主体成功绑定或轮换会话。</summary>
    MovementBound,
    /// <summary>P27 移动输入已接受（包括原地静止）。</summary>
    MovementAccepted,
    /// <summary>移动尚未建立可信主体绑定。</summary>
    MovementUnbound,
    /// <summary>输入的主体键或会话版本与真实绑定不符。</summary>
    MovementSubjectMismatch,
    /// <summary>输入序号或逻辑 Tick 已被消费或倒退。</summary>
    MovementOutOfOrder,
    /// <summary>输入跨越过多逻辑 Tick，必须可信重新建立基准。</summary>
    MovementTickGapExceeded,
    /// <summary>速度属性未定义或超过当前 P27 上限。</summary>
    MovementInvalidSpeed,
    /// <summary>位置位移超出速度与逻辑 Tick 预算。</summary>
    MovementSpeedExceeded,
    /// <summary>移动被眩晕、定身或恐惧等控制禁止。</summary>
    MovementControlled
}

/// <summary>注册前的不可变实体创建意图，不向调用方交付实体可变能力。</summary>
public readonly record struct WorldEntitySeed
{
    /// <summary>创建完整的实体注册意图。</summary>
    /// <param name="identity">世界与实体代际的真实四元组。</param>
    /// <param name="transform">初始的有限变换。</param>
    public WorldEntitySeed(EntityIdentity identity, Transform transform)
    {
        if (identity.IsEmpty)
        {
            throw new ArgumentException("Seed identity must be complete.", nameof(identity));
        }

        Identity = identity;
        Transform = transform;
    }

    /// <summary>注册时的固定实体归属。</summary>
    public EntityIdentity Identity { get; }

    /// <summary>注册时的固定位置。</summary>
    public Transform Transform { get; }
}

/// <summary>读取方唯一可获得的实体快照，不含任何可变领域对象引用。</summary>
public readonly record struct WorldEntitySnapshot
{
    internal WorldEntitySnapshot(EntityIdentity identity, WorldEntityLifecycle lifecycle,
        Transform transform, LifeState life, EntityIdentity? target,
        EntityControlEffects control, IReadOnlyList<string> tags, EntityRelations relationships,
        IReadOnlyList<EntityControlGrantSnapshot> controlGrants,
        IReadOnlyList<EntityTagGrantSnapshot> tagGrants,
        IReadOnlyList<string> permanentTags, ulong controlSequence, ulong tagSequence,
        EntityAttributeSnapshot attributes, WorldMovementSnapshot movement,
        WorldMotionSnapshot motion)
    {
        Identity = identity;
        Lifecycle = lifecycle;
        Transform = transform;
        Life = life;
        Target = target;
        Control = control;
        Tags = tags;
        Relationships = relationships;
        ControlGrants = controlGrants;
        TagGrants = tagGrants;
        PermanentTags = permanentTags;
        ControlSequence = controlSequence;
        TagSequence = tagSequence;
        Attributes = attributes;
        Movement = movement;
        Motion = motion;
    }

    /// <summary>包含代际的实体身份。</summary>
    public EntityIdentity Identity { get; }

    /// <summary>当前生命周期快照。</summary>
    public WorldEntityLifecycle Lifecycle { get; }

    /// <summary>实体位置的按值快照。</summary>
    public Transform Transform { get; }

    /// <summary>当前生命状态。</summary>
    public LifeState Life { get; }

    /// <summary>只包含不可变身份的目标信息。</summary>
    public EntityIdentity? Target { get; }

    /// <summary>按来源合并后的不可变控制位集合。</summary>
    public EntityControlEffects Control { get; }

    /// <summary>按值冻结、Ordinal 升序的有效标签，不是任何内部集合。</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>四类区分角色的关系身份快照。</summary>
    public EntityRelations Relationships { get; }

    /// <summary>用于确定性状态摘要的不可变控制来源明细。</summary>
    public IReadOnlyList<EntityControlGrantSnapshot> ControlGrants { get; }

    /// <summary>用于确定性状态摘要的不可变临时标签来源明细。</summary>
    public IReadOnlyList<EntityTagGrantSnapshot> TagGrants { get; }

    /// <summary>永久标签来源快照；不能与有效标签混为一个多源集合。</summary>
    public IReadOnlyList<string> PermanentTags { get; }

    /// <summary>此代际控制来源单调递增的最大序号。</summary>
    public ulong ControlSequence { get; }

    /// <summary>此代际标签来源单调递增的最大序号。</summary>
    public ulong TagSequence { get; }

    /// <summary>当前基础/派生属性、来源修饰及资源的不可变快照。</summary>
    public EntityAttributeSnapshot Attributes { get; }

    /// <summary>当前移动输入会话及基准，位置仍由 Transform 唯一持有。</summary>
    public WorldMovementSnapshot Movement { get; }

    /// <summary>P28 不保存当前位置副本的服务器运动状态、请求与轨迹版本快照。</summary>
    public WorldMotionSnapshot Motion { get; }
}

/// <summary>可选同步清理回调；供未来 Timer、订阅和异步请求撤销器接入。</summary>
public interface IWorldEntityCleanup
{
    /// <summary>每代实体成功进入 Removing 时至多调用一次；失败使整个执行器 Faulted。</summary>
    /// <param name="identity">正在移除的实体身份。</param>
    void OnRemoving(EntityIdentity identity);
}

/// <summary>
/// 一个 WorldId/Epoch 内唯一的可变实体 Registry；只通过 P18 同步租约进行生命周期变化。
/// 对外只有快照与明确定义的命令，不暴露 EntityCapabilities 或可变集合。
/// </summary>
public sealed partial class WorldInstance
{
    private const int MaximumEntities = 10_000;
    private const int MaximumCleanupHooks = 16;
    private readonly object _gate = new();
    private readonly Dictionary<EntityId, Entry> _entries = [];
    private readonly WorldOwnershipRegistry _ownership;
    private readonly WorldSpatialIndex _spatial;
    private WorldLifecycle _state = WorldLifecycle.Created;

    /// <summary>在共享协调器中为唯一 WorldId 预注册此世界；激活仍必须通过执行器租约。</summary>
    /// <param name="world">非空世界标识。</param>
    /// <param name="epoch">非空世界运行世代。</param>
    /// <param name="ownership">同进程全部世界共用的归属协调器。</param>
    /// <param name="cellSize">三维 Grid Cell 边长（米），合法范围 1–4096。</param>
    public WorldInstance(WorldId world, WorldEpoch epoch, WorldOwnershipRegistry ownership,
        double cellSize = 32)
    {
        if (world.IsEmpty || epoch.IsEmpty)
        {
            throw new ArgumentException("World and Epoch must be non-empty.");
        }

        ArgumentNullException.ThrowIfNull(ownership);
        World = world;
        Epoch = epoch;
        _ownership = ownership;
        _spatial = new WorldSpatialIndex(cellSize);
        _ownership.RegisterWorld(this);
    }

    /// <summary>此世界不可变 ID。</summary>
    public WorldId World { get; }

    /// <summary>此实例不可变 Epoch；重开世界必须在执行器推进 Epoch 后新建实例。</summary>
    public WorldEpoch Epoch { get; }

    /// <summary>当前世界生命周期。</summary>
    public WorldLifecycle State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>当前活动或附加实体数量，不计已退休墓碑。</summary>
    public int LiveCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values.Count(entry => IsLive(entry.State));
            }
        }
    }

    /// <summary>按值查询当前 EntityId 的最近代际和生命周期，包含已退休墓碑。</summary>
    /// <param name="id">实体 ID。</param>
    /// <param name="snapshot">复制的只读快照。</param>
    /// <returns>Registry 中是否还保有该 ID 的代际记录。</returns>
    public bool TryGetSnapshot(EntityId id, out WorldEntitySnapshot snapshot)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                snapshot = Snapshot(entry);
                return true;
            }
        }

        snapshot = default;
        return false;
    }

    /// <summary>空间索引 Cell 的固定边长，单位米。</summary>
    public double CellSize => _spatial.CellSize;

    /// <summary>Spatial 中附加/活跃实体计数；不包含已移除实体。</summary>
    public int SpatialIndexedCount
    {
        get
        {
            lock (_gate)
            {
                return _spatial.IndexedCount;
            }
        }
    }

    /// <summary>已创建的 Grid Cell 数量（含显式 Unloaded 的空 Cell）。</summary>
    public int SpatialCellCount
    {
        get
        {
            lock (_gate)
            {
                return _spatial.CellCount;
            }
        }
    }

    /// <summary>仅计算位置的 Cell 坐标，不改变任何世界或 Cell 状态。</summary>
    /// <param name="position">有限世界位置。</param>
    /// <param name="cell">成功时为 floor 计算的整数 Cell 坐标。</param>
    /// <returns>位置可由当前 Grid 表示则为 true。</returns>
    public bool TryLocateCell(Position position, out SpatialCellKey cell) =>
        _spatial.TryCell(position, out cell);

    /// <summary>获取 Cell 生命周期与实体数量的按值快照。</summary>
    /// <param name="cell">待查 Cell。</param>
    /// <param name="snapshot">结果快照。</param>
    /// <returns>该 Cell 是否曾被创建。</returns>
    public bool TryGetCell(SpatialCellKey cell, out SpatialCellSnapshot snapshot)
    {
        lock (_gate)
        {
            return _spatial.TrySnapshot(cell, out snapshot);
        }
    }

    /// <summary>显式激活空 Cell；此前 Unloaded 的 Cell 只有此入口可重新进入。</summary>
    /// <param name="context">P18 当前世界真实同步租约。</param>
    /// <param name="cell">目标 Cell。</param>
    /// <returns>成功、重复或容量/生命周期拒绝。</returns>
    public SpatialCellResult ActivateCell(IWorldWriteContext context, SpatialCellKey cell)
    {
        Demand(context);
        lock (_gate)
        {
            return _state == WorldLifecycle.Running
                ? _spatial.Activate(cell) : SpatialCellResult.InvalidState;
        }
    }

    /// <summary>只允许空 Cell 卸载；有成员时拒绝并保持索引不变。</summary>
    /// <param name="context">P18 当前世界真实同步租约。</param>
    /// <param name="cell">目标 Cell。</param>
    /// <returns>成功、重复、占用或生命周期拒绝。</returns>
    public SpatialCellResult UnloadCell(IWorldWriteContext context, SpatialCellKey cell)
    {
        Demand(context);
        lock (_gate)
        {
            return _state == WorldLifecycle.Running
                ? _spatial.Unload(cell) : SpatialCellResult.InvalidState;
        }
    }

    /// <summary>按 Cell 筛选并精确欧氏距离判断，返回 Active 实体的 EntityId 升序快照。</summary>
    /// <param name="center">查询中心。</param>
    /// <param name="radius">非负米单位闭区间半径。</param>
    /// <returns>不可变、已排序的实体快照；不返回内部能力引用。</returns>
    public IReadOnlyList<WorldEntitySnapshot> QueryNearby(Position center, Distance radius)
    {
        lock (_gate)
        {
            DemandSpatialReadable();
            var result = new List<WorldEntitySnapshot>();
            foreach (var id in _spatial.Candidates(center, radius))
            {
                var entry = _entries[id];
                if (entry.State == WorldEntityLifecycle.Active &&
                    IsWithin(entry.Capabilities.Transform.Value.Position, center, radius.Meters))
                {
                    result.Add(Snapshot(entry));
                }
            }

            return result.AsReadOnly();
        }
    }

    /// <summary>按 Grid Cell 粗筛后精确检测三轴闭区间，返回 Active 实体升序快照。</summary>
    /// <param name="bounds">合法闭区间 Bounds。</param>
    /// <returns>与暴力扫描语义相同的不可变、已排序实体快照。</returns>
    public IReadOnlyList<WorldEntitySnapshot> QueryRange(Bounds bounds)
    {
        lock (_gate)
        {
            DemandSpatialReadable();
            var result = new List<WorldEntitySnapshot>();
            foreach (var id in _spatial.Candidates(bounds))
            {
                var entry = _entries[id];
                if (entry.State == WorldEntityLifecycle.Active &&
                    bounds.Contains(entry.Capabilities.Transform.Value.Position))
                {
                    result.Add(Snapshot(entry));
                }
            }

            return result.AsReadOnly();
        }
    }

    /// <summary>从 P24 唯一 Transform 重建可丢弃的派生索引，不改变任何实体位置或代际。</summary>
    /// <param name="context">P18 当前世界真实同步租约。</param>
    /// <returns>是否在 Running/Draining 下完成同步重建。</returns>
    public bool RebuildSpatialIndex(IWorldWriteContext context)
    {
        Demand(context);
        lock (_gate)
        {
            if (_state is not (WorldLifecycle.Running or WorldLifecycle.Draining))
            {
                return false;
            }

            try
            {
                _spatial.Rebuild(_entries.Values.Where(entry => IsLive(entry.State))
                    .Select(entry => (entry.Identity, entry.Capabilities.Transform.Value.Position)));
                return true;
            }
            catch
            {
                _state = WorldLifecycle.Faulted;
                throw;
            }
        }
    }

    // Fail-closed：不可恢复异常可能留下仅部分更新的派生索引。
    // 允许 Host 检查单实体 canonical 快照，但禁止将损坏的 Spatial 查询误报为可信结果。
    private void DemandSpatialReadable()
    {
        if (_state == WorldLifecycle.Faulted)
        {
            throw new InvalidOperationException("Spatial query is unavailable for a faulted world.");
        }
    }

    private static WorldEntitySnapshot Snapshot(Entry entry) =>
        new(entry.Identity, entry.State, entry.Capabilities.Transform.Value,
            entry.Capabilities.Life.State, entry.Capabilities.Target.State.Selected,
            entry.Capabilities.Control.Effective, entry.Capabilities.Tags.ActiveTags,
            entry.Capabilities.Relationships.State,
            entry.Capabilities.Control.ActiveGrants, entry.Capabilities.Tags.ActiveGrants,
            entry.Capabilities.Tags.PermanentTags,
            entry.Capabilities.Control.LastSequence, entry.Capabilities.Tags.LastSequence,
            entry.Capabilities.Attributes.Capture(),
            MovementSnapshot(entry), entry.Motion.Capture());

    private static WorldMovementSnapshot MovementSnapshot(Entry entry)
    {
        WorldSpeedModel.TrySpeed(entry.Capabilities, out var speed);
        return entry.Movement.Capture(speed);
    }

    private static bool IsWithin(Position point, Position center, double radius) =>
        WorldSpatialIndex.IsWithin(point, center, radius);

    /// <summary>只在有效 P18 Scope 内复制完整代际墓碑并按 EntityId 固定排序。</summary>
    internal IReadOnlyList<WorldEntitySnapshot> SnapshotForTick(IWorldWriteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DemandWorld(World, Epoch);
        lock (_gate)
        {
            var snapshots = _entries.Values.OrderBy(entry => entry.Identity.Id.Value)
                .Select(Snapshot).ToArray();
            return Array.AsReadOnly(snapshots);
        }
    }

    /// <summary>Created → Loading；在空世界时也必须拥有真实同步世界租约。</summary>
    /// <param name="context">P18 有效世界写入上下文。</param>
    /// <returns>是否成功转换。</returns>
    public bool BeginLoading(IWorldWriteContext context) =>
        Transition(context, WorldLifecycle.Created, WorldLifecycle.Loading);

    /// <summary>Loading → Running。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <returns>是否成功转换。</returns>
    public bool Start(IWorldWriteContext context) =>
        Transition(context, WorldLifecycle.Loading, WorldLifecycle.Running);

    /// <summary>Running → Draining，停止接纳新的创建和玩法命令。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <returns>是否成功转换。</returns>
    public bool BeginDrain(IWorldWriteContext context) =>
        Transition(context, WorldLifecycle.Running, WorldLifecycle.Draining);

    /// <summary>由可信世界命令显式进入 Faulted；不能再在未知部分状态上继续处理命令。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <returns>是否首次进入 Faulted。</returns>
    public bool Fault(IWorldWriteContext context)
    {
        Demand(context);
        lock (_gate)
        {
            if (_state is WorldLifecycle.Stopped or WorldLifecycle.Faulted)
            {
                return false;
            }

            _state = WorldLifecycle.Faulted;
            return true;
        }
    }

    /// <summary>Draining → Stopped；依稳定 ID 顺序清理全部实体与归属，释放 WorldId。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <returns>是否完成正常停止。</returns>
    public bool Stop(IWorldWriteContext context)
    {
        Demand(context);
        lock (_gate)
        {
            if (_state != WorldLifecycle.Draining)
            {
                return false;
            }

            foreach (var entry in _entries.Values
                         .OrderBy(item => item.Identity.Id.Value))
            {
                if (IsLive(entry.State))
                {
                    Detach(context, entry, dispose: true);
                }
                else if (entry.State == WorldEntityLifecycle.Detached)
                {
                    entry.State = WorldEntityLifecycle.Disposed;
                }
            }

            _spatial.Clear();
            _ownership.UnregisterWorld(this);
            _state = WorldLifecycle.Stopped;
            return true;
        }
    }

    /// <summary>将不可变 Seed 注册为 Attached，先占用唯一全局归属，再绑定 P18 活跃代际。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="seed">尚未进入世界的实体创建意图。</param>
    /// <returns>Added 或可明确恢复的拒绝原因。</returns>
    public WorldEntityResult Add(IWorldWriteContext context, WorldEntitySeed seed)
    {
        Demand(context);
        if (seed.Identity.IsEmpty)
        {
            throw new ArgumentException("Seed identity must be complete.", nameof(seed));
        }

        if (seed.Identity.World != World || seed.Identity.Epoch != Epoch)
        {
            return WorldEntityResult.WorldConflict;
        }

        lock (_gate)
        {
            if (_state != WorldLifecycle.Running)
            {
                return WorldEntityResult.InvalidState;
            }

            if (_entries.TryGetValue(seed.Identity.Id, out var existing))
            {
                if (IsLive(existing.State) || existing.State == WorldEntityLifecycle.Removing)
                {
                    return WorldEntityResult.AlreadyExists;
                }

                if (seed.Identity.Generation.Value <= existing.Identity.Generation.Value)
                {
                    return WorldEntityResult.Stale;
                }
            }
            else if (_entries.Count >= MaximumEntities)
            {
                return WorldEntityResult.CapacityExceeded;
            }

            var spatialStatus = _spatial.ValidateDestination(seed.Transform.Position, out var startCell);
            if (spatialStatus != WorldEntityResult.Added)
            {
                return spatialStatus;
            }

            if (!_ownership.TryClaim(this, seed.Identity))
            {
                return WorldEntityResult.WorldConflict;
            }

            try
            {
                // 只有真实 WorldExecutor 的当期同步租约才能 Bind；失败时必须回滚归属预留。
                context.BindEntity(seed.Identity.Id, seed.Identity.Generation);
            }
            catch
            {
                _ownership.Release(this, seed.Identity);
                throw;
            }

            try
            {
                _entries[seed.Identity.Id] = new Entry(seed);
                _spatial.Add(seed.Identity, startCell);
                return WorldEntityResult.Added;
            }
            catch
            {
                // 技术代际已绑定，不能简单释放全局 Claim；统一故障封闭等待可信恢复。
                _state = WorldLifecycle.Faulted;
                throw;
            }
        }
    }

    /// <summary>Attached → Active；重复激活返回 AlreadyCompleted。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">精确 EntityId/Generation/World/Epoch。</param>
    /// <returns>明确的生命周期结果。</returns>
    public WorldEntityResult Activate(IWorldWriteContext context, EntityIdentity identity)
    {
        Demand(context);
        lock (_gate)
        {
            var result = Find(identity, out var entry);
            if (result != WorldEntityResult.Added)
            {
                return result;
            }

            if (_state != WorldLifecycle.Running)
            {
                return WorldEntityResult.InvalidState;
            }

            if (entry!.State == WorldEntityLifecycle.Active)
            {
                return WorldEntityResult.AlreadyCompleted;
            }

            if (entry.State != WorldEntityLifecycle.Attached)
            {
                return WorldEntityResult.InvalidState;
            }

            context.Demand(identity.World, identity.Epoch, identity.Id, identity.Generation);
            entry.State = WorldEntityLifecycle.Active;
            return WorldEntityResult.Activated;
        }
    }

    /// <summary>在一条同步世界命令中完成 Created → Attached → Active。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="seed">完整创建意图。</param>
    /// <returns>Spawned 或详细拒绝状态。</returns>
    public WorldEntityResult Spawn(IWorldWriteContext context, WorldEntitySeed seed)
    {
        var result = Add(context, seed);
        if (result != WorldEntityResult.Added)
        {
            return result;
        }

        var activated = Activate(context, seed.Identity);
        if (activated != WorldEntityResult.Activated)
        {
            throw new InvalidOperationException("Newly attached entity could not be activated.");
        }

        return WorldEntityResult.Spawned;
    }

    /// <summary>Attached/Active → Removing → Detached，并撤销旧回执和注册的清理钩子。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">实体精确代际。</param>
    /// <returns>Removed、AlreadyCompleted 或明确拒绝。</returns>
    public WorldEntityResult Remove(IWorldWriteContext context, EntityIdentity identity) =>
        RemoveCore(context, identity, dispose: false);

    /// <summary>Attached/Active → Removing → Disposed；Detached → Disposed，重复销毁幂等。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">实体精确代际。</param>
    /// <returns>Despawned、AlreadyCompleted 或明确拒绝。</returns>
    public WorldEntityResult Despawn(IWorldWriteContext context, EntityIdentity identity) =>
        RemoveCore(context, identity, dispose: true);

    /// <summary>统一的原子重定位入口：先准备 Spatial 目标 Cell，再同步提交索引和 P24 唯一 Transform。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">活动实体精确身份。</param>
    /// <param name="transform">新的有限位置与方向。</param>
    /// <returns>重定位或拒绝状态。</returns>
    public WorldEntityResult Relocate(IWorldWriteContext context, EntityIdentity identity, Transform transform) =>
        RelocateCore(context, identity, transform, invalidateMovement: true);

    private WorldEntityResult RelocateCore(IWorldWriteContext context, EntityIdentity identity,
        Transform transform, bool invalidateMovement)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            var before = entry!.Capabilities.Transform.Value;
            if (!_spatial.TryCell(before.Position, out var originalCell))
            {
                _state = WorldLifecycle.Faulted;
                throw new InvalidOperationException("Canonical Transform is outside Spatial Grid.");
            }

            var spatialStatus = _spatial.ValidateDestination(transform.Position, out var destination);
            if (spatialStatus != WorldEntityResult.Added)
            {
                return spatialStatus;
            }

            // 已通过同代际与真实租约校验，所有写入在同一 _gate 下完成，
            // 读者无法看到索引先于 Transform 更新的中间状态。
            try
            {
                context.Demand(identity.World, identity.Epoch, identity.Id, identity.Generation);
                _spatial.Move(identity, originalCell, destination);
                entry.Capabilities.Transform.Relocate(context, transform);
                if (invalidateMovement)
                {
                    entry.Movement.Invalidate();
                    entry.Motion.Cancel();
                }
                return WorldEntityResult.Relocated;
            }
            catch
            {
                // 不声称异常时自动回滚已部分提交的内存对象；世界必须停止工作并重建索引。
                _state = WorldLifecycle.Faulted;
                throw;
            }
        }
    }

    /// <summary>使用实际注册表校验目标 World/Epoch/Generation、Active 与 Alive，不保留目标引用。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">来源实体精确身份。</param>
    /// <param name="target">目标身份，null 清除目标。</param>
    /// <returns>目标更新或拒绝状态。</returns>
    public WorldEntityResult SelectTarget(IWorldWriteContext context, EntityIdentity identity,
        EntityIdentity? target)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            if (target is { } selected)
            {
                if (selected.World != World || selected.Epoch != Epoch ||
                    selected.Id == identity.Id)
                {
                    return WorldEntityResult.TargetUnavailable;
                }

                var targetStatus = FindActive(selected, out var targetEntry);
                if (targetStatus != WorldEntityResult.Added ||
                    targetEntry!.Capabilities.Life.State != LifeState.Alive)
                {
                    return WorldEntityResult.TargetUnavailable;
                }

                if (entry!.Capabilities.Life.State != LifeState.Alive)
                {
                    return WorldEntityResult.InvalidState;
                }
            }

            entry!.Capabilities.Target.Select(context, target);
            return WorldEntityResult.TargetChanged;
        }
    }

    /// <summary>在真实 P18 租约中按来源授予单项控制；同来源同标志幂等。</summary>
    /// <param name="context">真实同步世界写入上下文。</param>
    /// <param name="identity">受控制实体的完整身份。</param>
    /// <param name="source">非零业务来源。</param>
    /// <param name="flag">单项受支持控制标志。</param>
    /// <param name="handle">成功授予或重复时返回可精确撤销的句柄。</param>
    /// <returns>明确的授予、重复或配额状态。</returns>
    public WorldEntityResult GrantControl(IWorldWriteContext context, EntityIdentity identity,
        EntityEffectSource source, EntityControlEffects flag, out EntityEffectHandle handle)
    {
        Demand(context);
        lock (_gate)
        {
            handle = default;
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            return entry!.Capabilities.Control.Grant(context, source, flag, out handle) switch
            {
                EntityEffectGrantResult.Granted => WorldEntityResult.ControlGranted,
                EntityEffectGrantResult.AlreadyExists => WorldEntityResult.AlreadyExists,
                EntityEffectGrantResult.CapacityExceeded => WorldEntityResult.CapacityExceeded,
                _ => throw new InvalidOperationException("Unknown control grant result.")
            };
        }
    }

    /// <summary>只撤销当前代际的一个控制来源，其它同类来源继续生效。</summary>
    /// <param name="context">真实同步世界写入上下文。</param>
    /// <param name="identity">被控制实体身份。</param>
    /// <param name="handle">控制类型且完整代际匹配的来源句柄。</param>
    /// <returns>实际撤销或幂等结果。</returns>
    public WorldEntityResult RevokeControl(IWorldWriteContext context, EntityIdentity identity,
        EntityEffectHandle handle)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            return handle.Identity != identity ? WorldEntityResult.Stale
                : entry!.Capabilities.Control.Revoke(context, handle)
                    ? WorldEntityResult.ControlRevoked : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>授予临时来源标签；不同来源可共同维持一个有效 Tag。</summary>
    /// <param name="context">真实同步世界写入上下文。</param>
    /// <param name="identity">被标记实体。</param>
    /// <param name="source">非零标签来源。</param>
    /// <param name="tag">规范化 ASCII 小写标签。</param>
    /// <param name="handle">本次标签授予的撤销句柄。</param>
    /// <returns>授予、重复或配额状态。</returns>
    public WorldEntityResult GrantTag(IWorldWriteContext context, EntityIdentity identity,
        EntityEffectSource source, string tag, out EntityEffectHandle handle)
    {
        Demand(context);
        lock (_gate)
        {
            handle = default;
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            return entry!.Capabilities.Tags.Grant(context, source, tag, out handle) switch
            {
                EntityEffectGrantResult.Granted => WorldEntityResult.TagGranted,
                EntityEffectGrantResult.AlreadyExists => WorldEntityResult.AlreadyExists,
                EntityEffectGrantResult.CapacityExceeded => WorldEntityResult.CapacityExceeded,
                _ => throw new InvalidOperationException("Unknown tag grant result.")
            };
        }
    }

    /// <summary>按完整句柄撤销临时 Tag，不影响其它来源或永久标签。</summary>
    /// <param name="context">真实同步世界写入上下文。</param>
    /// <param name="identity">被标记实体。</param>
    /// <param name="handle">来自当前代际 TagCapability 的句柄。</param>
    /// <returns>撤销或幂等结果。</returns>
    public WorldEntityResult RevokeTag(IWorldWriteContext context, EntityIdentity identity,
        EntityEffectHandle handle)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            return handle.Identity != identity ? WorldEntityResult.Stale
                : entry!.Capabilities.Tags.Revoke(context, handle)
                    ? WorldEntityResult.TagRevoked : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>启停永久 Tag；临时 Tag 撤销不会移除其它来源或永久 Tag。</summary>
    /// <param name="context">真实同步世界写入上下文。</param>
    /// <param name="identity">被标记实体。</param>
    /// <param name="tag">规范化 Tag。</param>
    /// <param name="enabled">启用/关闭永久来源。</param>
    /// <returns>状态变更或幂等。</returns>
    public WorldEntityResult SetPermanentTag(IWorldWriteContext context, EntityIdentity identity,
        string tag, bool enabled)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            if (enabled && entry!.Capabilities.Tags.SourceCount >= 64 &&
                !entry.Capabilities.Tags.HasPermanent(tag))
            {
                return WorldEntityResult.CapacityExceeded;
            }

            return entry!.Capabilities.Tags.SetPermanent(context, tag, enabled)
                ? WorldEntityResult.TagChanged : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>按有效 Tag 过滤正在活动的实体，返回按 EntityId 升序的不可变快照。</summary>
    /// <param name="tag">必须是规范化的 Tag。</param>
    /// <returns>不泄露内部可变标签集合的只读实体快照。</returns>
    public IReadOnlyList<WorldEntitySnapshot> QueryByTag(string tag)
    {
        EntityTagCapability.ValidateTag(tag);
        lock (_gate)
        {
            if (_state == WorldLifecycle.Faulted)
            {
                throw new InvalidOperationException("Cannot query potentially partial relationship state.");
            }

            return Array.AsReadOnly(_entries.Values
                .Where(entry => entry.State == WorldEntityLifecycle.Active &&
                                entry.Capabilities.Tags.Contains(tag))
                .OrderBy(entry => entry.Identity.Id.Value).Select(Snapshot).ToArray());
        }
    }

    /// <summary>更新 Owner/Creator/Controller/Summoner 其中一个关系，验证活动代际与跨类型图无环。</summary>
    /// <param name="context">真实同步世界写入上下文。</param>
    /// <param name="identity">关系的起点实体。</param>
    /// <param name="kind">区别四类关系的明确角色。</param>
    /// <param name="related">当前世界中另一活动代际，或 null 清空。</param>
    /// <returns>成功、重复、不可用关系或循环拒绝。</returns>
    public WorldEntityResult SetRelation(IWorldWriteContext context, EntityIdentity identity,
        EntityRelationKind kind, EntityIdentity? related)
    {
        Demand(context);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            if (entry!.Capabilities.Relationships.State.Get(kind) == related)
            {
                return WorldEntityResult.AlreadyCompleted;
            }

            if (related is { } target)
            {
                if (target.IsEmpty || target.World != World || target.Epoch != Epoch)
                {
                    return WorldEntityResult.RelationUnavailable;
                }

                if (target.Id == identity.Id || HasRelationshipPath(target, identity))
                {
                    return WorldEntityResult.RelationCycle;
                }

                if (FindActive(target, out _) != WorldEntityResult.Added)
                {
                    return WorldEntityResult.RelationUnavailable;
                }
            }

            return entry.Capabilities.Relationships.Set(context, kind, related)
                ? WorldEntityResult.RelationChanged : WorldEntityResult.AlreadyCompleted;
        }
    }

    private bool HasRelationshipPath(EntityIdentity start, EntityIdentity target)
    {
        var pending = new Stack<EntityIdentity>();
        var visited = new HashSet<EntityIdentity>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current == target)
            {
                return true;
            }

            if (!visited.Add(current) || !_entries.TryGetValue(current.Id, out var entry) ||
                entry.Identity != current || !IsLive(entry.State))
            {
                continue;
            }

            foreach (var kind in Enum.GetValues<EntityRelationKind>())
            {
                if (entry.Capabilities.Relationships.State.Get(kind) is { } next)
                {
                    pending.Push(next);
                }
            }
        }

        return false;
    }

    private void ClearInboundRelations(IWorldWriteContext context, EntityIdentity retired)
    {
        foreach (var entry in _entries.Values)
        {
            if (IsLive(entry.State) && entry.Identity != retired)
            {
                entry.Capabilities.Relationships.ClearReference(context, retired);
            }
        }
    }

    /// <summary>活动实体死亡时同步清除自身及世界内其它实体指向它的目标。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">当前活动实体。</param>
    /// <returns>生命状态更新或重复结果。</returns>
    public WorldEntityResult MarkDead(IWorldWriteContext context, EntityIdentity identity)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            if (!entry!.Capabilities.MarkDead(context))
            {
                return WorldEntityResult.AlreadyCompleted;
            }

            entry.Movement.Invalidate();
            entry.Motion.Cancel();
            StopInboundMotions(identity);
            ClearInboundTargets(context, identity);
            return WorldEntityResult.LifeChanged;
        }
    }

    /// <summary>活动实体最小复活状态切换；Health 恢复规则属于 P32。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">当前活动实体。</param>
    /// <returns>生命状态更新或重复结果。</returns>
    public WorldEntityResult Revive(IWorldWriteContext context, EntityIdentity identity)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            return entry!.Capabilities.Revive(context)
                ? WorldEntityResult.LifeChanged
                : WorldEntityResult.AlreadyCompleted;
        }
    }

    /// <summary>每个活动代际最多登记 16 个同步清理回调；重复实例只登记一次。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">当前已附加或活动实体。</param>
    /// <param name="cleanup">世界移除前的同步清理器。</param>
    /// <returns>登记结果。</returns>
    public WorldEntityResult RegisterCleanup(IWorldWriteContext context,
        EntityIdentity identity, IWorldEntityCleanup cleanup)
    {
        Demand(context);
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (_gate)
        {
            var status = Find(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            if (!IsLive(entry!.State))
            {
                return WorldEntityResult.InvalidState;
            }

            if (entry.Cleanup.Any(item => ReferenceEquals(item, cleanup)))
            {
                return WorldEntityResult.AlreadyExists;
            }

            if (entry.Cleanup.Count >= MaximumCleanupHooks)
            {
                return WorldEntityResult.CapacityExceeded;
            }

            context.Demand(identity.World, identity.Epoch, identity.Id, identity.Generation);
            entry.Cleanup.Add(cleanup);
            return WorldEntityResult.CleanupRegistered;
        }
    }

    /// <summary>P17 未实现跨世界转移；明确拒绝，禁止通过复制实体绕过唯一归属。</summary>
    /// <param name="context">同步世界写入上下文。</param>
    /// <param name="identity">活动实体。</param>
    /// <param name="destination">目标世界。</param>
    /// <returns>有效跨世界请求始终返回 TransferNotSupported。</returns>
    public WorldEntityResult Transfer(IWorldWriteContext context,
        EntityIdentity identity, WorldId destination)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(identity, out _);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            return destination.IsEmpty || destination == World
                ? WorldEntityResult.InvalidState
                : WorldEntityResult.TransferNotSupported;
        }
    }

    private void Demand(IWorldWriteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DemandWorld(World, Epoch);
    }

    private bool Transition(IWorldWriteContext context, WorldLifecycle from, WorldLifecycle to)
    {
        Demand(context);
        lock (_gate)
        {
            if (_state != from)
            {
                return false;
            }

            _state = to;
            return true;
        }
    }

    private WorldEntityResult RemoveCore(IWorldWriteContext context,
        EntityIdentity identity, bool dispose)
    {
        Demand(context);
        lock (_gate)
        {
            if (_state is not (WorldLifecycle.Running or WorldLifecycle.Draining))
            {
                return WorldEntityResult.InvalidState;
            }

            var status = Find(identity, out var entry);
            if (status != WorldEntityResult.Added)
            {
                return status;
            }

            if (entry!.State == WorldEntityLifecycle.Disposed)
            {
                return WorldEntityResult.AlreadyCompleted;
            }

            if (entry.State == WorldEntityLifecycle.Detached)
            {
                if (dispose)
                {
                    entry.State = WorldEntityLifecycle.Disposed;
                    return WorldEntityResult.Despawned;
                }

                return WorldEntityResult.AlreadyCompleted;
            }

            if (!IsLive(entry.State))
            {
                return WorldEntityResult.InvalidState;
            }

            Detach(context, entry, dispose);
            return dispose ? WorldEntityResult.Despawned : WorldEntityResult.Removed;
        }
    }

    private void Detach(IWorldWriteContext context, Entry entry, bool dispose)
    {
        // 先隔离再同步撤销；失败时保留 Claim/代际，标记 WorldInstance 故障；
        // P18 还会使真实 WorldExecutor Faulted，不允许在部分清理状态继续运行。
        entry.State = WorldEntityLifecycle.Removing;
        try
        {
            foreach (var cleanup in entry.Cleanup)
            {
                cleanup.OnRemoving(entry.Identity);
            }

            // 提前清空自己的目标，否则稳定 ID 次序停机时先销毁的实体墓碑可能保留旧目标。
            entry.Capabilities.Target.Select(context, null);
            entry.Motion.Cancel();
            StopInboundMotions(entry.Identity);
            ClearInboundTargets(context, entry.Identity);
            ClearInboundRelations(context, entry.Identity);
            entry.Capabilities.Relationships.Clear(context);
            entry.Capabilities.Control.Clear(context);
            entry.Capabilities.Tags.Clear(context);
            _spatial.Remove(entry.Identity, entry.Capabilities.Transform.Value.Position);
            context.RetireEntity(entry.Identity.Id, entry.Identity.Generation);
            _ownership.Release(this, entry.Identity);
            entry.Cleanup.Clear();
            entry.State = dispose ? WorldEntityLifecycle.Disposed : WorldEntityLifecycle.Detached;
        }
        catch
        {
            // 不能伪装回滚：已清理的资源可能无法恢复；所有权保留供 Host 后续诊断。
            _state = WorldLifecycle.Faulted;
            throw;
        }
    }

    private void ClearInboundTargets(IWorldWriteContext context, EntityIdentity removed)
    {
        foreach (var entry in _entries.Values)
        {
            if (IsLive(entry.State) &&
                entry.Identity != removed &&
                entry.Capabilities.Target.State.Selected == removed)
            {
                entry.Capabilities.Target.Select(context, null);
            }
        }
    }

    private WorldEntityResult Find(EntityIdentity identity, out Entry? entry)
    {
        entry = null;
        if (identity.IsEmpty || identity.World != World || identity.Epoch != Epoch)
        {
            return WorldEntityResult.Stale;
        }

        if (!_entries.TryGetValue(identity.Id, out var existing))
        {
            return WorldEntityResult.NotFound;
        }

        if (existing.Identity != identity)
        {
            return WorldEntityResult.Stale;
        }

        entry = existing;
        return WorldEntityResult.Added;
    }

    private WorldEntityResult FindActive(EntityIdentity identity, out Entry? entry)
    {
        var result = Find(identity, out entry);
        if (result != WorldEntityResult.Added)
        {
            return result;
        }

        return _state == WorldLifecycle.Running && entry!.State == WorldEntityLifecycle.Active
            ? WorldEntityResult.Added
            : WorldEntityResult.InvalidState;
    }

    private static bool IsLive(WorldEntityLifecycle state) =>
        state is WorldEntityLifecycle.Attached or WorldEntityLifecycle.Active;

    private sealed class Entry
    {
        internal Entry(WorldEntitySeed seed)
        {
            Identity = seed.Identity;
            Capabilities = new EntityCapabilities(seed.Identity, seed.Transform);
            Movement = new WorldMovementLedger();
            Motion = new WorldMotionLedger(seed.Transform.Position);
            State = WorldEntityLifecycle.Attached;
        }

        internal EntityIdentity Identity { get; }
        internal EntityCapabilities Capabilities { get; }
        internal WorldMovementLedger Movement { get; }
        internal WorldMotionLedger Motion { get; }
        internal WorldEntityLifecycle State { get; set; }
        internal List<IWorldEntityCleanup> Cleanup { get; } = [];
    }
}
