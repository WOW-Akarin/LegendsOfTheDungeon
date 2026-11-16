using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>每个世界内唯一的非零持久 Spawn 键；不同于运行期 EntityId。</summary>
public readonly record struct WorldSpawnKey
{
    /// <summary>创建有效的 Spawn 键。</summary>
    /// <param name="value">非零稳定键。</param>
    public WorldSpawnKey(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    /// <summary>非零 Spawn 编号。</summary>
    public ulong Value { get; }

    /// <summary>是否为未初始化的 default 键。</summary>
    public bool IsEmpty => Value == 0;
}

/// <summary>静态定义与一次性动态定义使用不同退休规则。</summary>
public enum WorldSpawnKind
{
    /// <summary>Cell 激活后可以生成，退休后按时钟重新生成。</summary>
    Static,
    /// <summary>经可信业务明确生成一次，退休后不会由计时器重新生成。</summary>
    Dynamic
}

/// <summary>Spawn 的唯一领域配置，不储存任何可写实体引用。</summary>
public readonly record struct WorldSpawnDefinition
{
    /// <summary>定义不可变 Spawn 键、EntityId、Transform 和重生延迟。</summary>
    /// <param name="key">此世界内的唯一持久键。</param>
    /// <param name="entityId">独占的实体数值 ID。</param>
    /// <param name="transform">初始 P24 Transform。</param>
    /// <param name="respawnDelay">单调逻辑重生等待时长，向上对齐到 Tick。</param>
    /// <param name="kind">静态或动态。</param>
    public WorldSpawnDefinition(WorldSpawnKey key, EntityId entityId, Transform transform,
        Duration respawnDelay, WorldSpawnKind kind = WorldSpawnKind.Static)
    {
        if (key.IsEmpty || entityId.IsEmpty || !Enum.IsDefined(kind))
        {
            throw new ArgumentException("Spawn definition requires valid key, EntityId and kind.");
        }

        Key = key;
        EntityId = entityId;
        Transform = transform;
        RespawnDelay = respawnDelay;
        Kind = kind;
    }

    /// <summary>不可变 Spawn 键。</summary>
    public WorldSpawnKey Key { get; }

    /// <summary>该 Spawn 键独占的运行期 EntityId，Generation 随退休递增。</summary>
    public EntityId EntityId { get; }

    /// <summary>每次重生的 canonical 初始坐标和角度。</summary>
    public Transform Transform { get; }

    /// <summary>静态点的逻辑重生延迟。</summary>
    public Duration RespawnDelay { get; }

    /// <summary>静态或一次性动态生成。</summary>
    public WorldSpawnKind Kind { get; }
}

/// <summary>一个 Spawn 点的确定性生命周期。</summary>
public enum WorldSpawnStatus
{
    /// <summary>尚未生成，等待显式激活 Cell 和下一 Timers 阶段。</summary>
    Registered,
    /// <summary>对应完整实体代际正在 WorldInstance 中活动。</summary>
    Active,
    /// <summary>静态实体已退休，按到期 Tick 等待重生及关联父节点。</summary>
    Waiting,
    /// <summary>崩溃恢复中未能核对已确认的实例，拒绝自行生成副本。</summary>
    RecoveryHold,
    /// <summary>一次性动态实体已永久退休。</summary>
    Retired
}

/// <summary>Spawn API 的明确结果；拒绝与幂等不能被宣称为新生成。</summary>
public enum WorldSpawnResult
{
    /// <summary>新增一个定义。</summary>
    Registered,
    /// <summary>生成一个真实活动实体。</summary>
    Spawned,
    /// <summary>退休当前确认的实例。</summary>
    Despawned,
    /// <summary>关联关系修改。</summary>
    LinkChanged,
    /// <summary>无状态变化。</summary>
    AlreadyCompleted,
    /// <summary>未注册 Spawn。</summary>
    NotFound,
    /// <summary>唯一键或实体 ID 已被其它定义占用。</summary>
    Conflict,
    /// <summary>世界不在允许 Spawn 写入的生命周期。</summary>
    InvalidState,
    /// <summary>Cell 未显式激活或已经卸载。</summary>
    GridUnavailable,
    /// <summary>超出登记预算或世界预算。</summary>
    CapacityExceeded,
    /// <summary>静态 Spawn 关联关系会形成环。</summary>
    LinkCycle,
    /// <summary>恢复状态或 P17 实体所有权未核对，不允许自动重生。</summary>
    RecoveryPending
}

/// <summary>只含值类型的 Spawn 当前状态，不共享内部记录引用。</summary>
/// <param name="Definition">初始 Spawn 定义。</param>
/// <param name="Cell">派生的空间 Cell。</param>
/// <param name="Status">当前生命周期。</param>
/// <param name="LastGeneration">该 Spawn 键已成功确认的最大代际，初始为 0。</param>
/// <param name="Current">当前确认实例身份，非 Active 状态为 null。</param>
/// <param name="DueTick">等待重生的绝对逻辑 Tick；其它状态为 null。</param>
/// <param name="LinkedTo">依赖其活跃状态的父 Spawn 键。</param>
public readonly record struct WorldSpawnEntrySnapshot(
    WorldSpawnDefinition Definition, SpatialCellKey Cell, WorldSpawnStatus Status,
    ulong LastGeneration, EntityIdentity? Current, Tick? DueTick, WorldSpawnKey? LinkedTo);

/// <summary>外部持久化边界的按值快照；实际落盘由 P11/P60 Adapter 负责，不暗示已经完成持久事务。</summary>
public sealed class WorldSpawnCheckpoint
{
    internal WorldSpawnCheckpoint(WorldId world, WorldEpoch epoch, Tick lastTick,
        ulong version, IReadOnlyList<WorldSpawnEntrySnapshot> entries)
    {
        World = world;
        Epoch = epoch;
        LastTick = lastTick;
        Version = version;
        Entries = entries;
    }

    /// <summary>该快照归属的 World。</summary>
    public WorldId World { get; }

    /// <summary>该快照归属的世界生命周期代际。</summary>
    public WorldEpoch Epoch { get; }

    /// <summary>该注册表最后处理完毕的逻辑 Tick。</summary>
    public Tick LastTick { get; }

    /// <summary>每次实际状态修改递增的快照版本。</summary>
    public ulong Version { get; }

    /// <summary>键升序、不可变的全量 Spawn 定义和状态。</summary>
    public IReadOnlyList<WorldSpawnEntrySnapshot> Entries { get; }
}

/// <summary>一次成功生成的 Spawn 键与完整实体代际，供逻辑 Tick 记录。</summary>
/// <param name="Key">来源 Spawn 键。</param>
/// <param name="Identity">新生成的完整身份。</param>
public readonly record struct WorldSpawnActivation(WorldSpawnKey Key, EntityIdentity Identity);

/// <summary>
/// P22 单世界有界 Spawn/Respawn 编排；P17 是真实实体唯一拥有者，P20 是唯一 Grid/Transform 真相。
/// 所有写入要求 P18 当期 World 同步租约，绝不由系统 Timer、后台线程或网络输入自行修改实体。
/// </summary>
public sealed class WorldSpawnRegistry : IWorldTickStep
{
    private const int MaximumSpawns = 4096;
    private readonly object _gate = new();
    private readonly WorldInstance _world;
    private readonly IClock _clock;
    private readonly Dictionary<WorldSpawnKey, Record> _records = [];
    private readonly Dictionary<EntityId, WorldSpawnKey> _entityKeys = [];
    private Tick _lastTick;
    private ulong _version;

    /// <summary>创建世界私有 Spawn 注册表，逻辑时钟必须由外部可信 Tick 调度器推进。</summary>
    /// <param name="world">P17 世界注册和唯一实体拥有者。</param>
    /// <param name="clock">确定性时钟（支持 FakeClock）。</param>
    public WorldSpawnRegistry(WorldInstance world, IClock clock)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (clock.TickDuration.IsZero)
        {
            throw new ArgumentException("Spawn logic requires positive TickDuration.", nameof(clock));
        }
    }

    /// <inheritdoc/>
    public string Key => "world.spawn-respawn";

    /// <inheritdoc/>
    public WorldTickPhase Phase => WorldTickPhase.Timers;

    /// <summary>只读世界标识。</summary>
    public WorldId World => _world.World;

    /// <summary>只读世界运行代际。</summary>
    public WorldEpoch Epoch => _world.Epoch;

    /// <summary>当前定义数量，不重复统计运行期代际。</summary>
    public int Count
    {
        get { lock (_gate) { return _records.Count; } }
    }

    /// <summary>已确认 Registry 状态的版本，仅供持久快照比较。</summary>
    public ulong Version
    {
        get { lock (_gate) { return _version; } }
    }

    /// <summary>最后处理完成的逻辑 Tick。</summary>
    public Tick LastProcessedTick
    {
        get { lock (_gate) { return _lastTick; } }
    }

    /// <summary>复制某键完整的只读 Spawn 状态，不暴露可写对象。</summary>
    /// <param name="key">唯一 Spawn 键。</param>
    /// <param name="snapshot">对应状态。</param>
    /// <returns>是否存在该键。</returns>
    public bool TryGet(WorldSpawnKey key, out WorldSpawnEntrySnapshot snapshot)
    {
        lock (_gate)
        {
            if (_records.TryGetValue(key, out var record))
            {
                snapshot = record.Snapshot();
                return true;
            }

            snapshot = default;
            return false;
        }
    }

    /// <summary>静态 Spawn 仅注册定义，真实生成在显式加载 Cell 后由 Timers 阶段执行。</summary>
    /// <param name="context">真实 P18 世界租约。</param>
    /// <param name="definition">Kind 为 Static 的不可变定义。</param>
    /// <returns>新增、幂等或明确拒绝。</returns>
    public WorldSpawnResult RegisterStatic(IWorldWriteContext context, WorldSpawnDefinition definition)
    {
        Demand(context);
        if (definition.Kind != WorldSpawnKind.Static)
        {
            throw new ArgumentException("RegisterStatic requires a static definition.", nameof(definition));
        }

        lock (_gate)
        {
            return Register(definition);
        }
    }

    /// <summary>可信业务创建一次性动态实体；目标 Cell 必须已经 Active。</summary>
    /// <param name="context">真实 P18 世界租约。</param>
    /// <param name="definition">Kind 为 Dynamic 的不可变定义。</param>
    /// <returns>生成成功、冲突或 Cell 未激活。</returns>
    public WorldSpawnResult SpawnDynamic(IWorldWriteContext context, WorldSpawnDefinition definition)
    {
        Demand(context);
        if (definition.Kind != WorldSpawnKind.Dynamic)
        {
            throw new ArgumentException("SpawnDynamic requires a dynamic definition.", nameof(definition));
        }

        lock (_gate)
        {
            if (_records.TryGetValue(definition.Key, out var existing))
            {
                return existing.Definition == definition
                    ? WorldSpawnResult.AlreadyCompleted : WorldSpawnResult.Conflict;
            }

            if (_entityKeys.ContainsKey(definition.EntityId))
            {
                return WorldSpawnResult.Conflict;
            }

            if (_records.Count >= MaximumSpawns)
            {
                return WorldSpawnResult.CapacityExceeded;
            }

            if (!_world.TryLocateCell(definition.Transform.Position, out var cell))
            {
                return WorldSpawnResult.GridUnavailable;
            }

            if (!CellActive(cell))
            {
                return WorldSpawnResult.GridUnavailable;
            }

            var candidate = new Record(definition, cell);
            var result = Spawn(context, candidate);
            if (result != WorldSpawnResult.Spawned)
            {
                return result;
            }

            _records.Add(definition.Key, candidate);
            _entityKeys.Add(definition.EntityId, definition.Key);
            Changed();
            return result;
        }
    }

    /// <summary>在可信同步 Scope 中显式激活 Cell；静态 Spawn 由下一次 Timers 阶段生成。</summary>
    /// <param name="context">本世界 P18 租约。</param>
    /// <param name="cell">待激活 Cell。</param>
    /// <returns>P20 原始 Cell 生命周期结果。</returns>
    public SpatialCellResult LoadGrid(IWorldWriteContext context, SpatialCellKey cell)
    {
        Demand(context);
        lock (_gate)
        {
            return _world.ActivateCell(context, cell);
        }
    }

    /// <summary>先确保 Cell 内没有未受本 Registry 管理的实体，再退休当前受管实体并卸载。</summary>
    /// <param name="context">本世界 P18 租约。</param>
    /// <param name="cell">目标 Cell。</param>
    /// <returns>成功、占用或原始 P20 Cell 状态。</returns>
    public SpatialCellResult UnloadGrid(IWorldWriteContext context, SpatialCellKey cell)
    {
        Demand(context);
        lock (_gate)
        {
            if (!_world.TryGetCell(cell, out var snapshot))
            {
                return SpatialCellResult.NotFound;
            }

            if (snapshot.Lifecycle == SpatialCellLifecycle.Unloaded)
            {
                return SpatialCellResult.AlreadyCompleted;
            }

            var owned = _records.Values.Where(r => r.Cell == cell &&
                r.Status == WorldSpawnStatus.Active)
                .OrderBy(r => r.Definition.Key.Value).ToArray();
            // Foreign entities or untracked recovery records must block the unload before ANY retirement.
            if (snapshot.Count != owned.Length ||
                owned.Any(r => !CanonicalActive(r)))
            {
                return SpatialCellResult.Occupied;
            }

            foreach (var record in owned)
            {
                var result = Retire(context, record);
                if (result != WorldSpawnResult.Despawned)
                {
                    _world.Fault(context);
                    throw new InvalidOperationException("Grid unload unexpectedly lost a confirmed spawn.");
                }
            }

            var unload = _world.UnloadCell(context, cell);
            if (unload != SpatialCellResult.Unloaded)
            {
                _world.Fault(context);
                throw new InvalidOperationException("Grid changed while unloading owned spawns.");
            }

            return unload;
        }
    }

    /// <summary>将一项静态 Spawn 关联到另一静态 Spawn；子节点必须等待父节点当前代际 Active。</summary>
    /// <param name="context">P18 世界租约。</param>
    /// <param name="child">子 Spawn 键。</param>
    /// <param name="parent">父 Spawn 键，null 为取消关联。</param>
    /// <returns>关联成功、重复、找不到节点或环拒绝。</returns>
    public WorldSpawnResult SetLinkedRespawn(IWorldWriteContext context, WorldSpawnKey child,
        WorldSpawnKey? parent)
    {
        Demand(context);
        lock (_gate)
        {
            if (!_records.TryGetValue(child, out var source) ||
                source.Definition.Kind != WorldSpawnKind.Static)
            {
                return WorldSpawnResult.NotFound;
            }

            if (source.LinkedTo == parent)
            {
                return WorldSpawnResult.AlreadyCompleted;
            }

            if (parent is { } selected)
            {
                if (!_records.TryGetValue(selected, out var target) ||
                    target.Definition.Kind != WorldSpawnKind.Static)
                {
                    return WorldSpawnResult.NotFound;
                }

                var cursor = (WorldSpawnKey?)selected;
                var visited = new HashSet<WorldSpawnKey>();
                while (cursor is { } current)
                {
                    if (current == child || !visited.Add(current))
                    {
                        return WorldSpawnResult.LinkCycle;
                    }

                    cursor = _records[current].LinkedTo;
                }
            }

            source.LinkedTo = parent;
            Changed();
            return WorldSpawnResult.LinkChanged;
        }
    }

    /// <summary>退休一个当前确认的实体；静态点启动 LogicalTimer、动态点永久 Retired。</summary>
    /// <param name="context">P18 世界租约。</param>
    /// <param name="key">注册 Spawn 键。</param>
    /// <returns>退休或幂等结果。</returns>
    public WorldSpawnResult Despawn(IWorldWriteContext context, WorldSpawnKey key)
    {
        Demand(context);
        lock (_gate)
        {
            if (!_records.TryGetValue(key, out var record))
            {
                return WorldSpawnResult.NotFound;
            }

            if (record.Status == WorldSpawnStatus.RecoveryHold)
            {
                return WorldSpawnResult.RecoveryPending;
            }

            return record.Status == WorldSpawnStatus.Active
                ? Retire(context, record) : WorldSpawnResult.AlreadyCompleted;
        }
    }

    /// <summary>以 P19 Timers Phase 按稳定依赖深度和 SpawnKey 顺序执行到期生成。</summary>
    /// <param name="context">P18 当前世界租约。</param>
    /// <param name="world">必须与注册表绑定的 P17 世界是同一对象。</param>
    /// <param name="tick">P19 当前连续逻辑 Tick。</param>
    public void Execute(IWorldWriteContext context, WorldInstance world, Tick tick)
    {
        if (!ReferenceEquals(world, _world))
        {
            throw new ArgumentException("Spawn registry belongs to another WorldInstance.", nameof(world));
        }

        Advance(context, tick);
    }

    /// <summary>允许直接测试同一 Timers 规则，每个逻辑 Tick 最多处理一次。</summary>
    /// <param name="context">真实 P18 世界租约。</param>
    /// <param name="tick">必须处于时钟已推进的未来 Tick。</param>
    /// <returns>本次真正生成的实体，稳定按依赖深度/SpawnKey 顺序。</returns>
    public IReadOnlyList<WorldSpawnActivation> Advance(IWorldWriteContext context, Tick tick)
    {
        Demand(context);
        lock (_gate)
        {
            if (tick == _lastTick)
            {
                return Array.AsReadOnly(Array.Empty<WorldSpawnActivation>());
            }

            // Never accept historical or skipped ticks, even through the direct test/host API.
            // Otherwise a caller could jump over an observed lifecycle transition or due timer.
            if (tick < _lastTick || tick != _lastTick.Add(1))
            {
                throw new InvalidOperationException("Spawn Timers requires exactly the next logical Tick.");
            }

            if (tick > _clock.CurrentTick)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Tick cannot exceed the logical clock.");
            }

            var result = new List<WorldSpawnActivation>();
            foreach (var record in _records.Values.OrderBy(Depth)
                         .ThenBy(r => r.Definition.Key.Value))
            {
                if (record.Status == WorldSpawnStatus.Active)
                {
                    if (!CanonicalActive(record))
                    {
                        if (_world.TryGetSnapshot(record.Definition.EntityId, out var old) &&
                            (old.Identity != record.Current ||
                             old.Lifecycle is WorldEntityLifecycle.Active or WorldEntityLifecycle.Attached))
                        {
                            record.Status = WorldSpawnStatus.RecoveryHold;
                            record.Current = null;
                            record.DueTick = null;
                            Changed();
                        }
                        else
                        {
                            AfterRetirement(record);
                        }
                    }

                    continue;
                }

                if (record.Status == WorldSpawnStatus.RecoveryHold)
                {
                    // Recovery reconciliation NEVER creates a new instance by inference.
                    continue;
                }

                if (record.Definition.Kind == WorldSpawnKind.Dynamic ||
                    record.Status == WorldSpawnStatus.Retired ||
                    record.DueTick is { } due && tick < due ||
                    !CellActive(record.Cell) ||
                    record.LinkedTo is { } parent &&
                    (!_records.TryGetValue(parent, out var upstream) ||
                     upstream.Status != WorldSpawnStatus.Active || !CanonicalActive(upstream)))
                {
                    continue;
                }

                if (Spawn(context, record) == WorldSpawnResult.Spawned)
                {
                    result.Add(new WorldSpawnActivation(record.Definition.Key,
                        record.Current!.Value));
                }
            }

            _lastTick = tick;
            Changed();
            return Array.AsReadOnly(result.ToArray());
        }
    }

    /// <summary>在同 World Scope 冻结可持久化的完整注册表状态；存储确认由外部 Adapter 负责。</summary>
    /// <param name="context">P18 当前世界租约。</param>
    /// <returns>按 SpawnKey 排序且与 Registry 分离的不可变快照。</returns>
    public WorldSpawnCheckpoint Capture(IWorldWriteContext context)
    {
        Demand(context);
        lock (_gate)
        {
            return new WorldSpawnCheckpoint(World, Epoch, _lastTick, _version,
                Array.AsReadOnly(_records.Values.OrderBy(r => r.Definition.Key.Value)
                    .Select(r => r.Snapshot()).ToArray()));
        }
    }

    /// <summary>
    /// 恢复已被持久化层确认的 Spawn 账本。已确认 Active 若不在 P17 注册表中精确存在，
    /// 必须进入 RecoveryHold；绝不会在恢复时按同 EntityId 自行创建不受确认的新副本。
    /// </summary>
    /// <param name="context">当前 P18 世界写租约。</param>
    /// <param name="world">已单独恢复或加载的 P17 世界。</param>
    /// <param name="clock">逻辑 FakeClock/调度时钟。</param>
    /// <param name="checkpoint">外部确认过的完整账本。</param>
    /// <returns>新的世界私有 Registry，未自动生成任何实体。</returns>
    public static WorldSpawnRegistry Restore(IWorldWriteContext context, WorldInstance world,
        IClock clock, WorldSpawnCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(context);
        context.DemandWorld(world.World, world.Epoch);
        if (world.State != WorldLifecycle.Running ||
            checkpoint.World != world.World || checkpoint.Epoch != world.Epoch ||
            clock.CurrentTick < checkpoint.LastTick ||
            checkpoint.Entries.Count > MaximumSpawns)
        {
            throw new InvalidOperationException("Checkpoint world/epoch/clock or capacity mismatch.");
        }

        var restored = new WorldSpawnRegistry(world, clock);
        foreach (var snapshot in checkpoint.Entries)
        {
            var definition = snapshot.Definition;
            if (definition.Key.IsEmpty || definition.EntityId.IsEmpty ||
                !world.TryLocateCell(definition.Transform.Position, out var cell) ||
                cell != snapshot.Cell ||
                !Enum.IsDefined(snapshot.Status) ||
                snapshot.Status == WorldSpawnStatus.Active && snapshot.Current is null ||
                snapshot.Status == WorldSpawnStatus.Waiting && snapshot.DueTick is null ||
                snapshot.Status != WorldSpawnStatus.Waiting && snapshot.DueTick is not null ||
                snapshot.Status != WorldSpawnStatus.Active && snapshot.Current is not null ||
                snapshot.Current is { } current &&
                (current.World != world.World || current.Epoch != world.Epoch ||
                 current.Id != definition.EntityId ||
                 current.Generation.Value != snapshot.LastGeneration) ||
                snapshot.Status == WorldSpawnStatus.Active && snapshot.LastGeneration == 0 ||
                !restored._entityKeys.TryAdd(definition.EntityId, definition.Key))
            {
                throw new InvalidOperationException("Checkpoint contains invalid or duplicate Spawn identity.");
            }

            var record = new Record(definition, cell)
            {
                Status = snapshot.Status,
                LastGeneration = snapshot.LastGeneration,
                Current = snapshot.Current,
                DueTick = snapshot.DueTick,
                LinkedTo = snapshot.LinkedTo
            };
            if (!restored._records.TryAdd(definition.Key, record))
            {
                throw new InvalidOperationException("Checkpoint contains duplicate Spawn keys.");
            }

            if (record.Status == WorldSpawnStatus.Active && !restored.CanonicalActive(record) ||
                record.Status != WorldSpawnStatus.Active &&
                    world.TryGetSnapshot(definition.EntityId, out var known) &&
                    known.Lifecycle is WorldEntityLifecycle.Active or WorldEntityLifecycle.Attached)
            {
                record.Status = WorldSpawnStatus.RecoveryHold;
                record.Current = null;
                record.DueTick = null;
            }
        }

        foreach (var record in restored._records.Values)
        {
            if (record.LinkedTo is { } parent && (parent == record.Definition.Key ||
                !restored._records.TryGetValue(parent, out var upstream) ||
                upstream.Definition.Kind != WorldSpawnKind.Static ||
                record.Definition.Kind != WorldSpawnKind.Static))
            {
                throw new InvalidOperationException("Checkpoint contains invalid linked respawn.");
            }

            restored.Depth(record); // This detects cycles before the recovered registry is published.
        }

        restored._lastTick = checkpoint.LastTick;
        restored._version = checkpoint.Version;
        return restored;
    }

    /// <summary>仅在已真实恢复 exact Identity 且 Active 时解除 RecoveryHold；否则保留防重闸。</summary>
    /// <param name="context">P18 当前世界租约。</param>
    /// <param name="key">待核对的唯一 Spawn 键。</param>
    /// <param name="identity">已被其它恢复机制确切加载的完整实体身份。</param>
    /// <returns>解除恢复闸或保持等待。</returns>
    public WorldSpawnResult ConfirmRecovered(IWorldWriteContext context, WorldSpawnKey key,
        EntityIdentity identity)
    {
        Demand(context);
        lock (_gate)
        {
            if (!_records.TryGetValue(key, out var record))
            {
                return WorldSpawnResult.NotFound;
            }

            if (record.Status != WorldSpawnStatus.RecoveryHold ||
                identity.World != World || identity.Epoch != Epoch ||
                identity.Id != record.Definition.EntityId ||
                identity.Generation.Value != record.LastGeneration ||
                !_world.TryGetSnapshot(identity.Id, out var snapshot) ||
                snapshot.Identity != identity ||
                snapshot.Lifecycle != WorldEntityLifecycle.Active)
            {
                return WorldSpawnResult.RecoveryPending;
            }

            record.Status = WorldSpawnStatus.Active;
            record.Current = identity;
            record.DueTick = null;
            Changed();
            return WorldSpawnResult.AlreadyCompleted;
        }
    }

    private WorldSpawnResult Register(WorldSpawnDefinition definition)
    {
        if (_world.State != WorldLifecycle.Running)
        {
            return WorldSpawnResult.InvalidState;
        }

        if (_records.TryGetValue(definition.Key, out var existing))
        {
            return existing.Definition == definition
                ? WorldSpawnResult.AlreadyCompleted : WorldSpawnResult.Conflict;
        }

        if (_entityKeys.ContainsKey(definition.EntityId))
        {
            return WorldSpawnResult.Conflict;
        }

        if (_records.Count >= MaximumSpawns)
        {
            return WorldSpawnResult.CapacityExceeded;
        }

        if (!_world.TryLocateCell(definition.Transform.Position, out var cell))
        {
            return WorldSpawnResult.GridUnavailable;
        }

        // A preexisting EntityId cannot be assigned to a newly registered Spawn without
        // reconciling the confirmed owner; including old tombstones prevents generation theft.
        if (_world.TryGetSnapshot(definition.EntityId, out _))
        {
            return WorldSpawnResult.Conflict;
        }

        var record = new Record(definition, cell);
        _records.Add(definition.Key, record);
        _entityKeys.Add(definition.EntityId, definition.Key);
        Changed();
        return WorldSpawnResult.Registered;
    }

    private WorldSpawnResult Spawn(IWorldWriteContext context, Record record)
    {
        if (_world.State != WorldLifecycle.Running)
        {
            return WorldSpawnResult.InvalidState;
        }

        if (!CellActive(record.Cell))
        {
            return WorldSpawnResult.GridUnavailable;
        }

        var nextGeneration = checked(record.LastGeneration + 1);
        var identity = new EntityIdentity(World, Epoch, record.Definition.EntityId,
            new EntityGeneration(nextGeneration));
        if (_world.TryGetSnapshot(identity.Id, out var old))
        {
            if (old.Lifecycle is WorldEntityLifecycle.Active or WorldEntityLifecycle.Attached ||
                old.Identity.Generation.Value >= nextGeneration)
            {
                record.Status = WorldSpawnStatus.RecoveryHold;
                record.Current = null;
                record.DueTick = null;
                Changed();
                return WorldSpawnResult.RecoveryPending;
            }
        }

        var result = _world.Spawn(context, new WorldEntitySeed(identity, record.Definition.Transform));
        if (result == WorldEntityResult.Spawned)
        {
            record.LastGeneration = nextGeneration;
            record.Current = identity;
            record.DueTick = null;
            record.Status = WorldSpawnStatus.Active;
            Changed();
            return WorldSpawnResult.Spawned;
        }

        if (result is WorldEntityResult.CellUnloaded or WorldEntityResult.SpatialOutOfBounds)
        {
            return WorldSpawnResult.GridUnavailable;
        }

        if (result == WorldEntityResult.CapacityExceeded)
        {
            return WorldSpawnResult.CapacityExceeded;
        }

        record.Status = WorldSpawnStatus.RecoveryHold;
        record.DueTick = null;
        record.Current = null;
        Changed();
        return WorldSpawnResult.RecoveryPending;
    }

    private WorldSpawnResult Retire(IWorldWriteContext context, Record record)
    {
        if (!CanonicalActive(record))
        {
            record.Status = WorldSpawnStatus.RecoveryHold;
            record.Current = null;
            record.DueTick = null;
            Changed();
            return WorldSpawnResult.RecoveryPending;
        }

        var removed = _world.Despawn(context, record.Current!.Value);
        if (removed != WorldEntityResult.Despawned)
        {
            throw new InvalidOperationException("Confirmed World Spawn did not retire successfully.");
        }

        AfterRetirement(record);
        return WorldSpawnResult.Despawned;
    }

    private void AfterRetirement(Record record)
    {
        record.Current = null;
        if (record.Definition.Kind == WorldSpawnKind.Dynamic)
        {
            record.Status = WorldSpawnStatus.Retired;
            record.DueTick = null;
        }
        else
        {
            record.Status = WorldSpawnStatus.Waiting;
            record.DueTick = LogicalTimer.Start(_clock, record.Definition.RespawnDelay).DueTick;
        }

        Changed();
    }

    private bool CanonicalActive(Record record) =>
        record.Current is { } expected &&
        _world.TryGetSnapshot(expected.Id, out var snapshot) &&
        snapshot.Identity == expected && snapshot.Lifecycle == WorldEntityLifecycle.Active;

    private bool CellActive(SpatialCellKey cell) =>
        _world.TryGetCell(cell, out var current) &&
        current.Lifecycle == SpatialCellLifecycle.Active;

    private int Depth(Record record)
    {
        var visited = new HashSet<WorldSpawnKey>();
        var depth = 0;
        var cursor = record.LinkedTo;
        while (cursor is { } key)
        {
            if (!visited.Add(key) || !_records.TryGetValue(key, out var parent))
            {
                throw new InvalidOperationException("Linked spawn contains cycle or missing parent.");
            }

            depth++;
            cursor = parent.LinkedTo;
        }

        return depth;
    }

    private void Demand(IWorldWriteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DemandWorld(World, Epoch);
    }

    private void Changed() => _version = checked(_version + 1);

    private sealed class Record(WorldSpawnDefinition definition, SpatialCellKey cell)
    {
        internal WorldSpawnDefinition Definition { get; } = definition;
        internal SpatialCellKey Cell { get; } = cell;
        internal WorldSpawnStatus Status { get; set; } = WorldSpawnStatus.Registered;
        internal ulong LastGeneration { get; set; }
        internal EntityIdentity? Current { get; set; }
        internal Tick? DueTick { get; set; }
        internal WorldSpawnKey? LinkedTo { get; set; }

        internal WorldSpawnEntrySnapshot Snapshot() =>
            new(Definition, Cell, Status, LastGeneration, Current, DueTick, LinkedTo);
    }
}
