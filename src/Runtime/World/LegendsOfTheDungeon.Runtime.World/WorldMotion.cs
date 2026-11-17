using System.Buffers.Binary;
using System.Security.Cryptography;
using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>P28 通用服务器运动类型；Jump/Knockback 是受信任的有界服务器轨迹。</summary>
public enum WorldMotionKind
{
    /// <summary>指定位置导航。</summary>
    Point = 1,
    /// <summary>由固定种子确定目标的随机漫游。</summary>
    Random,
    /// <summary>追逐当前仍有效的目标实体。</summary>
    Chase,
    /// <summary>跟随当前仍有效的目标实体。</summary>
    Follow,
    /// <summary>远离当前仍有效的目标实体。</summary>
    Flee,
    /// <summary>返回实体出生时的静态 Home 锚点。</summary>
    Home,
    /// <summary>依次经过一组固定航点。</summary>
    Waypoint,
    /// <summary>沿已验证路径段做确定性平滑插值。</summary>
    Spline,
    /// <summary>由可信服务器推动的有限 Tick 抛物线。</summary>
    Jump,
    /// <summary>由可信服务器推动的有限 Tick 线性击退。</summary>
    Knockback
}

/// <summary>每代实体运动会话的状态。</summary>
public enum WorldMotionState
{
    /// <summary>尚无运动命令。</summary>
    Idle,
    /// <summary>已生成请求版本，等待 P23 外部只读导航回执。</summary>
    AwaitingPath,
    /// <summary>路径或受信任服务器轨迹正在推进。</summary>
    Active,
    /// <summary>运动目标已达到。</summary>
    Completed,
    /// <summary>显式取消、手工重定位或控制权切换。</summary>
    Cancelled,
    /// <summary>P23 返回无法通行或未知环境。</summary>
    NoPath,
    /// <summary>超过了固定逻辑截止 Tick。</summary>
    TimedOut,
    /// <summary>目标实体死亡、销毁、失活或 Generation 变化。</summary>
    TargetLost,
    /// <summary>移动时空间索引拒绝，或 P23 预算/取消失败。</summary>
    Blocked
}

/// <summary>P28 命令/导航/逐 Tick 推进的稳定结果，不混淆开始、等待和完成。</summary>
public enum WorldMotionResult
{
    /// <summary>接受新运动并签发版本。</summary>
    Started,
    /// <summary>接受来自正确版本的 P23 路径。</summary>
    PathAccepted,
    /// <summary>运动产生了一个有效同步位移。</summary>
    Advanced,
    /// <summary>运动到达最终终点。</summary>
    Completed,
    /// <summary>等待异步只读导航结果。</summary>
    WaitingForPath,
    /// <summary>无移动或被控制暂停，本 Tick 未改变位置。</summary>
    Paused,
    /// <summary>显式取消当前运动。</summary>
    Cancelled,
    /// <summary>低优先级无法抢占高优先级模式。</summary>
    PriorityRejected,
    /// <summary>旧运动版本、旧请求、过期实体或 Tick。</summary>
    Stale,
    /// <summary>没有可推进的活动运动。</summary>
    NotActive,
    /// <summary>世界生命周期/实体生命状态不支持本次操作。</summary>
    InvalidState,
    /// <summary>P25 速度缺失、无效或超过输入基础上限。</summary>
    InvalidSpeed,
    /// <summary>目标不存在或已退休。</summary>
    TargetLost,
    /// <summary>路径不存在，不能退化为穿墙直线。</summary>
    NoPath,
    /// <summary>逻辑截止 Tick 已过。</summary>
    TimedOut,
    /// <summary>空间目标不可表示、已卸载或查询失败。</summary>
    Blocked,
    /// <summary>导航回执的起点/终点/版本/节点数不合法。</summary>
    InvalidPath
}

/// <summary>不可变服务器运动命令；可选路径点在构造时防御性复制，不暴露原数组。</summary>
public sealed class WorldMotionCommand
{
    /// <summary>创建有界的服务器运动意图。</summary>
    /// <param name="kind">支持的运动类型。</param>
    /// <param name="priority">0–100；优先级更低的模式无法抢占当前活动模式。</param>
    /// <param name="goal">Point/Jump/Knockback 的目的地，或可忽略的默认值。</param>
    /// <param name="target">Chase/Follow/Flee 的完整目标实体代际。</param>
    /// <param name="points">Waypoint/Spline 的 1–32 个指定点；构造时复制。</param>
    /// <param name="seed">Random 非零确定性种子。</param>
    /// <param name="durationTicks">Jump/Knockback 的整数持续 Tick，1–20。</param>
    /// <param name="deadline">可选的绝对逻辑截止 Tick。</param>
    public WorldMotionCommand(WorldMotionKind kind, int priority = 10, Position goal = default,
        EntityIdentity? target = null, IEnumerable<Position>? points = null, ulong seed = 0,
        uint durationTicks = 0, Tick? deadline = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (priority is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(priority));
        var copy = points?.ToArray() ?? [];
        if (kind is WorldMotionKind.Waypoint or WorldMotionKind.Spline)
        {
            if (copy.Length is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(points));
        }
        else if (copy.Length != 0)
        {
            throw new ArgumentException("Only Waypoint/Spline accept multiple destinations.", nameof(points));
        }

        if ((kind is WorldMotionKind.Chase or WorldMotionKind.Follow or WorldMotionKind.Flee) !=
            (target is { IsEmpty: false }))
        {
            throw new ArgumentException("Only target motion requires a complete entity identity.", nameof(target));
        }

        if ((kind == WorldMotionKind.Random) != (seed != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(seed));
        }

        if (kind is WorldMotionKind.Jump or WorldMotionKind.Knockback)
        {
            if (durationTicks is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(durationTicks));
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(durationTicks, 0u);
        }

        Kind = kind;
        Priority = priority;
        Goal = goal;
        Target = target;
        Points = Array.AsReadOnly(copy);
        Seed = seed;
        DurationTicks = durationTicks;
        Deadline = deadline;
    }

    /// <summary>运动类型。</summary>
    public WorldMotionKind Kind { get; }
    /// <summary>同一实体可比较的确定性优先级。</summary>
    public int Priority { get; }
    /// <summary>单目的地或可信服务器轨迹终点。</summary>
    public Position Goal { get; }
    /// <summary>跟随、追逐或逃跑的完整目标标识。</summary>
    public EntityIdentity? Target { get; }
    /// <summary>防御性冻结的多个航点。</summary>
    public IReadOnlyList<Position> Points { get; }
    /// <summary>随机游走固定种子。</summary>
    public ulong Seed { get; }
    /// <summary>受信任运动轨迹持续的逻辑 Tick 数。</summary>
    public uint DurationTicks { get; }
    /// <summary>不依赖墙钟的截止 Tick。</summary>
    public Tick? Deadline { get; }
}

/// <summary>带实体代际、运动与请求版本、环境版本及目标锚点的不可变 P23 路径工作票据。</summary>
/// <param name="Identity">需接收结果的完整实体身份。</param>
/// <param name="MotionVersion">会话代号；抢占与取消使其失效。</param>
/// <param name="Request">P23 只读导航请求，包含递增 RequestVersion。</param>
/// <param name="EnvironmentVersion">请求时的环境快照版本。</param>
/// <param name="Target">动态目标的完整标识，静态运动为空。</param>
public readonly record struct WorldMotionPathRequest(
    EntityIdentity Identity, long MotionVersion, NavigationRequest Request,
    long EnvironmentVersion, EntityIdentity? Target);

/// <summary>P28 领域快照，不保存当前位置副本；路径内容由不可变哈希摘要表示。</summary>
/// <param name="Kind">当前运动类型；未初始化时为默认值。</param>
/// <param name="State">当前状态。</param>
/// <param name="Priority">当前优先级。</param>
/// <param name="MotionVersion">单调的运动代号。</param>
/// <param name="RequestVersion">单调的 P23 导航请求代号。</param>
/// <param name="EnvironmentVersion">当前请求所使用的环境快照版本。</param>
/// <param name="LastTick">最后已处理的逻辑 Tick。</param>
/// <param name="Deadline">可选绝对截止 Tick。</param>
/// <param name="Goal">当前目标位置（不是自身位置）。</param>
/// <param name="Target">目标实体完整身份。</param>
/// <param name="LegIndex">已执行的多航点序号。</param>
/// <param name="PathIndex">当前路径段序号。</param>
/// <param name="PathCount">接受的导航路径点数。</param>
/// <param name="SegmentProgress">当前路径段进度 [0,1]。</param>
/// <param name="PathDigest">防御性冻结路径的 SHA256 摘要。</param>
/// <param name="NavigationStatus">最近 P23 导航结果。</param>
/// <param name="Home">出生点静态锚点，非当前 Position。</param>
/// <param name="TargetAnchor">上一次路径计算使用的目标位置，不是本实体 Position。</param>
/// <param name="BallisticStart">受信任 Jump/Knockback 的起跳位置。</param>
/// <param name="BallisticProgress">服务器轨迹已执行 Tick 数。</param>
/// <param name="TrajectoryTicks">服务器轨迹总持续 Tick 数。</param>
/// <param name="CommandDigest">冻结命令原始航点/Seed 的确定性摘要。</param>
public readonly record struct WorldMotionSnapshot(
    WorldMotionKind Kind, WorldMotionState State, int Priority, long MotionVersion,
    long RequestVersion, long EnvironmentVersion, Tick LastTick, Tick? Deadline,
    Position Goal, EntityIdentity? Target, int LegIndex, int PathIndex, int PathCount,
    double SegmentProgress, string? PathDigest, EnvironmentQueryStatus NavigationStatus,
    Position Home, Position TargetAnchor, Position BallisticStart, uint BallisticProgress,
    uint TrajectoryTicks, string? CommandDigest);

/// <summary>拥有每代运动版本、导航路径和超时状态；当前位置仍只能从 Entity.Transform 读取。</summary>
internal sealed class WorldMotionLedger
{
    private readonly Position _home;
    private WorldMotionCommand? _command;
    private List<Position> _path = [];
    private WorldMotionPathRequest? _pending;
    private WorldMotionKind _kind;
    private WorldMotionState _state;
    private int _priority;
    private long _version;
    private long _requestVersion;
    private long _environmentVersion;
    private Tick _lastTick;
    private Tick? _deadline;
    private Position _goal;
    private Position _targetAnchor;
    private EntityIdentity? _target;
    private int _legIndex;
    private int _pathIndex;
    private double _progress;
    private string _digest = string.Empty;
    private string _commandDigest = string.Empty;
    private EnvironmentQueryStatus _navigationStatus;
    private Position _ballisticStart;
    private uint _ballisticProgress;

    internal WorldMotionLedger(Position home) => _home = home;
    internal WorldMotionState State => _state;
    internal long Version => _version;
    internal long EnvironmentVersion => _environmentVersion;
    internal WorldMotionCommand? Command => _command;
    internal Tick? Deadline => _deadline;
    internal Tick LastTick => _lastTick;
    internal EntityIdentity? Target => _target;
    internal Position TargetAnchor => _targetAnchor;
    internal Position Goal => _goal;
    internal int PathIndex => _pathIndex;
    internal double SegmentProgress => _progress;
    internal IReadOnlyList<Position> Path => _path;
    internal uint BallisticProgress => _ballisticProgress;
    internal Position BallisticStart => _ballisticStart;
    internal bool CanAdvance => _state is WorldMotionState.Active or WorldMotionState.AwaitingPath;
    internal bool IsBusy => CanAdvance;
    internal bool IsPending => _state == WorldMotionState.AwaitingPath && _pending.HasValue;
    internal WorldMotionPathRequest? Pending => _pending;
    internal Position Home => _home;

    internal WorldMotionSnapshot Capture() => new(
        _kind, _state, _priority, _version, _requestVersion, _environmentVersion,
        _lastTick, _deadline, _goal, _target, _legIndex, _pathIndex, _path.Count,
        _progress, _digest, _navigationStatus, _home, _targetAnchor, _ballisticStart,
        _ballisticProgress, _command?.DurationTicks ?? 0, _commandDigest);

    internal bool CanReplace(int priority) => !IsBusy || priority >= _priority;

    internal void Start(WorldMotionCommand command, Tick tick, long environmentVersion,
        Position start, Position goal, Position targetAnchor)
    {
        var next = checked(_version + 1);
        _command = command;
        _commandDigest = DigestCommand(command);
        _kind = command.Kind;
        _state = command.Kind is WorldMotionKind.Jump or WorldMotionKind.Knockback
            ? WorldMotionState.Active : WorldMotionState.AwaitingPath;
        _priority = command.Priority;
        _version = next;
        _lastTick = tick;
        _deadline = command.Deadline;
        _environmentVersion = environmentVersion;
        _goal = goal;
        _targetAnchor = targetAnchor;
        _target = command.Target;
        _legIndex = 0;
        _path = [];
        _pathIndex = 0;
        _progress = 0;
        _digest = string.Empty;
        _pending = null;
        _navigationStatus = EnvironmentQueryStatus.Unknown;
        _ballisticStart = start;
        _ballisticProgress = 0;
    }

    internal WorldMotionPathRequest Request(EntityIdentity identity, Position start, Tick tick)
    {
        var version = checked(_requestVersion + 1);
        _requestVersion = version;
        _pending = new WorldMotionPathRequest(identity, _version,
            new NavigationRequest(start, _goal, version, CurrentTick: checked((long)tick.Value),
                DeadlineTick: _deadline.HasValue ? checked((long)_deadline.Value.Value) : null),
            _environmentVersion, _target);
        _state = WorldMotionState.AwaitingPath;
        _path = [];
        _pathIndex = 0;
        _progress = 0;
        _digest = string.Empty;
        return _pending.Value;
    }

    internal bool Matches(WorldMotionPathRequest request) =>
        IsPending && _pending == request &&
        request.MotionVersion == _version &&
        request.Request.RequestVersion == _requestVersion &&
        request.EnvironmentVersion == _environmentVersion;

    internal void Accept(NavigationPathResult result)
    {
        _path = result.Waypoints.ToList();
        _pathIndex = 1;
        _progress = 0;
        _digest = DigestPath(_path);
        _navigationStatus = result.Status;
        _pending = null;
        _state = WorldMotionState.Active;
    }

    internal void Step(int index, double progress, Tick tick)
    {
        _pathIndex = index;
        _progress = progress;
        _lastTick = tick;
    }

    internal void BallisticStep(Tick tick)
    {
        _lastTick = tick;
        _ballisticProgress++;
    }

    internal void Observe(Tick tick) => _lastTick = tick;

    internal bool NextLeg(EntityIdentity identity, Position start, Tick tick, out WorldMotionPathRequest request)
    {
        var points = _command?.Points;
        if (points is not null && (_kind is WorldMotionKind.Waypoint or WorldMotionKind.Spline) &&
            _legIndex + 1 < points.Count)
        {
            _legIndex++;
            _goal = points[_legIndex];
            request = Request(identity, start, tick);
            return true;
        }

        Complete();
        request = default;
        return false;
    }

    internal void RefreshGoal(Position goal, Position targetAnchor)
    {
        _goal = goal;
        _targetAnchor = targetAnchor;
    }

    internal void Complete()
    {
        _state = WorldMotionState.Completed;
        _pending = null;
    }

    internal void Fail(WorldMotionState state, EnvironmentQueryStatus reason = EnvironmentQueryStatus.Unknown)
    {
        _state = state;
        _navigationStatus = reason;
        _pending = null;
        _path = [];
        _pathIndex = 0;
        _progress = 0;
        _digest = string.Empty;
    }

    internal void Cancel()
    {
        if (_state == WorldMotionState.Idle || _state == WorldMotionState.Cancelled) return;
        _version = checked(_version + 1);
        Fail(WorldMotionState.Cancelled);
    }

    private static string DigestCommand(WorldMotionCommand command)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddWord(digest, (ulong)command.Kind);
        AddWord(digest, (ulong)command.Priority);
        AddWord(digest, command.Seed);
        AddWord(digest, command.DurationTicks);
        AddWord(digest, (ulong)command.Points.Count);
        foreach (var point in command.Points)
        {
            AddWord(digest, unchecked((ulong)BitConverter.DoubleToInt64Bits(point.X)));
            AddWord(digest, unchecked((ulong)BitConverter.DoubleToInt64Bits(point.Y)));
            AddWord(digest, unchecked((ulong)BitConverter.DoubleToInt64Bits(point.Z)));
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static void AddWord(IncrementalHash hash, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static string DigestPath(IReadOnlyList<Position> points)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[24];
        foreach (var point in points)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes[..8], BitConverter.DoubleToInt64Bits(point.X));
            BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(8, 8), BitConverter.DoubleToInt64Bits(point.Y));
            BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(16, 8), BitConverter.DoubleToInt64Bits(point.Z));
            digest.AppendData(bytes);
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }
}

/// <summary>把服务器 Motion 接入 P19 固定 Simulation 阶段；不在 Tick 内同步调用可能耗时的寻路器。</summary>
public sealed class WorldMotionTickStep : IWorldTickStep
{
    private readonly Duration _duration;

    /// <summary>创建固定 TickDuration 驱动；逻辑 Tick 时长需与实际 Pipeline 时钟相同。</summary>
    /// <param name="tickDuration">固定每 Tick 1–1000ms。</param>
    public WorldMotionTickStep(Duration tickDuration)
    {
        if (tickDuration.Milliseconds is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(tickDuration));
        _duration = tickDuration;
    }

    /// <inheritdoc />
    public string Key => "p28.server.motion";

    /// <inheritdoc />
    public WorldTickPhase Phase => WorldTickPhase.Simulation;

    /// <inheritdoc />
    public void Execute(IWorldWriteContext context, WorldInstance world, Tick tick) =>
        world.AdvanceAllMotions(context, tick, _duration);
}

/// <summary>P28 服务器运动状态机与 P23 路径回执；所有世界内状态更新必须由 P18 同步租约发起。</summary>
public sealed partial class WorldInstance
{
    /// <summary>可信世界逻辑启动 Motion；客户端没有此方法所需的真实 P18 写入租约。</summary>
    /// <param name="context">真实同步世界执行上下文。</param>
    /// <param name="identity">活动实体的完整代际。</param>
    /// <param name="command">不可变服务器运动命令。</param>
    /// <param name="tick">此时观测的逻辑 Tick。</param>
    /// <param name="environmentVersion">可信 P23 环境快照版本（正整数）。</param>
    /// <param name="request">如需 P23 导航，返回精确版本化请求；Jump/Knockback 不需要。</param>
    /// <returns>开始、优先级拒绝或实体无效等稳定结果。</returns>
    public WorldMotionResult StartMotion(IWorldWriteContext context, EntityIdentity identity,
        WorldMotionCommand command, Tick tick, long environmentVersion,
        out WorldMotionPathRequest? request)
    {
        Demand(context);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(environmentVersion);
        if (tick.Value > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(tick));
        lock (_gate)
        {
            request = null;
            if (FindActive(identity, out var entry) != WorldEntityResult.Added)
                return WorldMotionResult.Stale;
            context.Demand(identity.World, identity.Epoch, identity.Id, identity.Generation);
            if (entry!.Capabilities.Life.State != LifeState.Alive)
                return WorldMotionResult.InvalidState;
            if (!WorldSpeedModel.TrySpeed(entry.Capabilities, out _))
                return WorldMotionResult.InvalidSpeed;
            if (!entry.Motion.CanReplace(command.Priority))
                return WorldMotionResult.PriorityRejected;
            if (command.Deadline is { } deadline && deadline <= tick)
                return WorldMotionResult.TimedOut;

            var position = entry.Capabilities.Transform.Value.Position;
            Position goal;
            Position anchor = default;
            if (command.Target is { } target)
            {
                if (FindActive(target, out var targetEntry) != WorldEntityResult.Added ||
                    targetEntry!.Capabilities.Life.State != LifeState.Alive || target == identity)
                    return WorldMotionResult.TargetLost;
                anchor = targetEntry.Capabilities.Transform.Value.Position;
                goal = CalculateDynamicGoal(command.Kind, position, anchor);
            }
            else
            {
                goal = command.Kind switch
                {
                    WorldMotionKind.Home => entry.Motion.Home,
                    WorldMotionKind.Waypoint or WorldMotionKind.Spline => command.Points[0],
                    WorldMotionKind.Random => RandomGoal(position, command.Seed, identity.Id.Value),
                    _ => command.Goal
                };
            }

            if (command.Kind is WorldMotionKind.Jump or WorldMotionKind.Knockback &&
                (!position.IsWithin(goal, new Distance(30))))
                return WorldMotionResult.Blocked;

            entry.Motion.Start(command, tick, environmentVersion, position, goal, anchor);
            // 服务器 Motion 拥有本代际路径期间不允许沿用客户端 P27 会话提交位置。
            entry.Movement.Invalidate();
            if (entry.Motion.State == WorldMotionState.AwaitingPath)
                request = entry.Motion.Request(identity, position, tick);
            return WorldMotionResult.Started;
        }
    }

    /// <summary>读取已签发的待处理 P23 请求，无需暴露内部可变路径列表。</summary>
    /// <param name="identity">完整实体代际。</param>
    /// <param name="request">当前仍有效的请求；旧版本不在此处返回。</param>
    /// <returns>是否有待完成的版本化路径请求。</returns>
    public bool TryGetPendingMotionPath(EntityIdentity identity, out WorldMotionPathRequest request)
    {
        lock (_gate)
        {
            request = default;
            if (FindActive(identity, out var entry) != WorldEntityResult.Added ||
                entry!.Motion.Pending is not { } pending)
                return false;
            request = pending;
            return true;
        }
    }

    /// <summary>只读查看运动状态，不暴露内部可变路径点或实体对象。</summary>
    /// <param name="identity">完整实体代际。</param>
    /// <param name="snapshot">防御性按值状态快照。</param>
    /// <returns>实体当前是否活动。</returns>
    public bool TryGetMotion(EntityIdentity identity, out WorldMotionSnapshot snapshot)
    {
        lock (_gate)
        {
            snapshot = default;
            if (FindActive(identity, out var entry) != WorldEntityResult.Added) return false;
            snapshot = entry!.Motion.Capture();
            return true;
        }
    }

    /// <summary>P23 Worker 计算路径后只能在 P18 当前代际/请求/环境版本核验通过时提交。</summary>
    /// <param name="context">当前同步世界写入租约。</param>
    /// <param name="request">从真实 StartMotion/路径重规划签发的请求值。</param>
    /// <param name="path">不可变 P23 导航结果，包含原始版本。</param>
    /// <returns>路径接受、旧请求拒绝、无路、超时或预算耗尽。</returns>
    public WorldMotionResult ApplyMotionPath(IWorldWriteContext context,
        WorldMotionPathRequest request, NavigationPathResult path)
    {
        Demand(context);
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            if (FindActive(request.Identity, out var entry) != WorldEntityResult.Added)
                return WorldMotionResult.Stale;
            context.Demand(request.Identity.World, request.Identity.Epoch,
                request.Identity.Id, request.Identity.Generation);
            var motion = entry!.Motion;
            if (!motion.Matches(request) ||
                path.RequestVersion != request.Request.RequestVersion ||
                path.EnvironmentVersion != request.EnvironmentVersion ||
                entry.Capabilities.Transform.Value.Position != request.Request.Start)
                return WorldMotionResult.Stale;
            if (motion.Deadline is { } deadline && motion.LastTick >= deadline)
            {
                motion.Fail(WorldMotionState.TimedOut, EnvironmentQueryStatus.TimedOut);
                return WorldMotionResult.TimedOut;
            }

            if (motion.Target is { } target &&
                (FindActive(target, out var other) != WorldEntityResult.Added ||
                 other!.Capabilities.Life.State != LifeState.Alive))
            {
                motion.Fail(WorldMotionState.TargetLost);
                return WorldMotionResult.TargetLost;
            }

            if (path.Status != EnvironmentQueryStatus.Success)
            {
                var status = path.Status == EnvironmentQueryStatus.TimedOut
                    ? WorldMotionState.TimedOut :
                    path.Status == EnvironmentQueryStatus.NoPath ||
                    path.Status is EnvironmentQueryStatus.Blocked or EnvironmentQueryStatus.OutsideBounds
                    ? WorldMotionState.NoPath : WorldMotionState.Blocked;
                motion.Fail(status, path.Status);
                return status switch
                {
                    WorldMotionState.TimedOut => WorldMotionResult.TimedOut,
                    WorldMotionState.NoPath => WorldMotionResult.NoPath,
                    _ => WorldMotionResult.Blocked
                };
            }

            if (path.Waypoints.Count is < 2 or > 4096 ||
                path.Waypoints[0] != request.Request.Start ||
                path.Waypoints[^1] != request.Request.Goal ||
                path.Waypoints.Any(p => !_spatial.TryCell(p, out _)))
                return WorldMotionResult.InvalidPath;
            motion.Accept(path);
            return WorldMotionResult.PathAccepted;
        }
    }

    /// <summary>只取消精确版本的当前活动运动，令所有旧 P23 结果过期。</summary>
    /// <param name="context">当前真实同步世界租约。</param>
    /// <param name="identity">完整活动代际。</param>
    /// <param name="expectedVersion">取消者观察到的运动版本。</param>
    /// <returns>取消成功或旧版本拒绝。</returns>
    public WorldMotionResult CancelMotion(IWorldWriteContext context,
        EntityIdentity identity, long expectedVersion)
    {
        Demand(context);
        lock (_gate)
        {
            if (FindActive(identity, out var entry) != WorldEntityResult.Added)
                return WorldMotionResult.Stale;
            context.Demand(identity.World, identity.Epoch, identity.Id, identity.Generation);
            var motion = entry!.Motion;
            if (!motion.IsBusy || motion.Version != expectedVersion)
                return WorldMotionResult.Stale;
            motion.Cancel();
            return WorldMotionResult.Cancelled;
        }
    }

    /// <summary>在当前真实世界 Tick 上推进单个实体运动；只经 P20 唯一 RelocateCore 写 Transform/Spatial。</summary>
    /// <param name="context">当前世界同步租约。</param>
    /// <param name="identity">完整实体代际。</param>
    /// <param name="tick">单调逻辑 Tick。</param>
    /// <param name="duration">本 Tick 固定 1–1000ms 时长。</param>
    /// <returns>推进、等待路径、超时、终结或拒绝状态。</returns>
    public WorldMotionResult AdvanceMotion(IWorldWriteContext context, EntityIdentity identity,
        Tick tick, Duration duration)
    {
        Demand(context);
        if (duration.Milliseconds is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (tick.Value > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(tick));
        lock (_gate)
        {
            if (FindActive(identity, out var entry) != WorldEntityResult.Added)
                return WorldMotionResult.Stale;
            context.Demand(identity.World, identity.Epoch, identity.Id, identity.Generation);
            var motion = entry!.Motion;
            if (!motion.CanAdvance) return WorldMotionResult.NotActive;
            if (tick <= motion.LastTick) return WorldMotionResult.Stale;
            if (entry.Capabilities.Life.State != LifeState.Alive)
            {
                motion.Fail(WorldMotionState.TargetLost);
                return WorldMotionResult.InvalidState;
            }
            if (motion.Deadline is { } deadline && tick >= deadline)
            {
                motion.Fail(WorldMotionState.TimedOut, EnvironmentQueryStatus.TimedOut);
                return WorldMotionResult.TimedOut;
            }

            var before = entry.Capabilities.Transform.Value;
            if (motion.Target is { } target)
            {
                if (FindActive(target, out var other) != WorldEntityResult.Added ||
                    other!.Capabilities.Life.State != LifeState.Alive)
                {
                    motion.Fail(WorldMotionState.TargetLost);
                    return WorldMotionResult.TargetLost;
                }
                var anchor = other.Capabilities.Transform.Value.Position;
                if (!anchor.IsWithin(motion.TargetAnchor, new Distance(.5)))
                {
                    motion.RefreshGoal(CalculateDynamicGoal(motion.Command!.Kind, before.Position, anchor), anchor);
                    motion.Request(identity, before.Position, tick);
                    motion.Observe(tick);
                    return WorldMotionResult.WaitingForPath;
                }
                if (motion.Command!.Kind == WorldMotionKind.Follow &&
                    before.Position.IsWithin(anchor, new Distance(1.5)))
                {
                    motion.Complete();
                    motion.Observe(tick);
                    return WorldMotionResult.Completed;
                }
            }

            if (motion.State == WorldMotionState.AwaitingPath)
            {
                motion.Observe(tick);
                return WorldMotionResult.WaitingForPath;
            }

            var control = entry.Capabilities.Control.Effective;
            if ((control & (EntityControlEffects.Stunned | EntityControlEffects.Rooted |
                            EntityControlEffects.Feared)) != 0)
            {
                motion.Observe(tick);
                return WorldMotionResult.Paused;
            }

            if (motion.Command!.Kind is WorldMotionKind.Jump or WorldMotionKind.Knockback)
            {
                var next = motion.BallisticProgress + 1;
                var fraction = (double)next / motion.Command.DurationTicks;
                var ballisticDestination = Interpolate(motion.BallisticStart, motion.Goal, fraction);
                if (motion.Command.Kind == WorldMotionKind.Jump)
                    ballisticDestination = new Position(ballisticDestination.X, ballisticDestination.Y,
                        ballisticDestination.Z + 4 * 1.5 * fraction * (1 - fraction));
                var result = ApplyServerTransform(context, identity, before, ballisticDestination);
                if (result != WorldMotionResult.Advanced) return result;
                motion.BallisticStep(tick);
                if (next == motion.Command.DurationTicks)
                {
                    motion.Complete();
                    return WorldMotionResult.Completed;
                }
                return WorldMotionResult.Advanced;
            }

            if (!WorldSpeedModel.TrySpeed(entry.Capabilities, out var effective))
            {
                motion.Fail(WorldMotionState.Blocked);
                return WorldMotionResult.InvalidSpeed;
            }

            var path = motion.Path;
            var index = motion.PathIndex;
            var progress = motion.SegmentProgress;
            if (index < 1 || index >= path.Count)
            {
                motion.Fail(WorldMotionState.Blocked);
                return WorldMotionResult.InvalidPath;
            }
            var source = path[index - 1];
            var destinationPoint = path[index];
            var distance = source.DistanceTo(destinationPoint).Meters;
            var budget = (double)effective * duration.Milliseconds / 1000;
            var fractionAdvance = distance <= 1e-12 ? 1 : budget / distance;
            var nextProgress = Math.Min(1d, progress + fractionAdvance);
            var interpolated = motion.Command.Kind == WorldMotionKind.Spline
                ? nextProgress * nextProgress * (3 - 2 * nextProgress)
                : nextProgress;
            var destination = Interpolate(source, destinationPoint, interpolated);
            var relocated = ApplyServerTransform(context, identity, before, destination);
            if (relocated != WorldMotionResult.Advanced) return relocated;
            if (nextProgress >= 1)
            {
                index++;
                if (index >= path.Count)
                {
                    if (motion.NextLeg(identity, destination, tick, out _))
                    {
                        motion.Observe(tick);
                        return WorldMotionResult.WaitingForPath;
                    }
                    motion.Observe(tick);
                    return WorldMotionResult.Completed;
                }
                nextProgress = 0;
            }
            motion.Step(index, nextProgress, tick);
            return budget == 0 ? WorldMotionResult.Paused : WorldMotionResult.Advanced;
        }
    }

    /// <summary>按实体 ID 升序推进当前世界已运行的运动，用于 P19 固定 Simulation Tick Step。</summary>
    /// <param name="context">真实同步世界租约。</param>
    /// <param name="tick">新一轮逻辑 Tick。</param>
    /// <param name="duration">此 Pipeline 每 Tick 的固定持续时间。</param>
    public void AdvanceAllMotions(IWorldWriteContext context, Tick tick, Duration duration)
    {
        Demand(context);
        lock (_gate)
        {
            var active = _entries.Values.Where(e => e.State == WorldEntityLifecycle.Active &&
                    e.Motion.CanAdvance)
                .OrderBy(e => e.Identity.Id.Value)
                .Select(e => e.Identity).ToArray();
            foreach (var identity in active)
                AdvanceMotion(context, identity, tick, duration);
        }
    }

    private WorldMotionResult ApplyServerTransform(IWorldWriteContext context,
        EntityIdentity identity, Transform current, Position destination)
        => ApplyServerTransform(context, identity, current, destination, true);

    private WorldMotionResult ApplyServerTransform(IWorldWriteContext context,
        EntityIdentity identity, Transform current, Position destination, bool updateYaw)
    {
        var x = destination.X - current.Position.X;
        var y = destination.Y - current.Position.Y;
        var yaw = updateYaw && (x != 0 || y != 0) ? new Angle(Math.Atan2(y, x)) : current.Yaw;
        if (current.Position == destination) return WorldMotionResult.Advanced;
        var relocated = RelocateCore(context, identity, new Transform(destination, yaw),
            invalidateMovement: false);
        if (relocated == WorldEntityResult.Relocated) return WorldMotionResult.Advanced;
        if (FindActive(identity, out var entry) == WorldEntityResult.Added)
            entry!.Motion.Fail(WorldMotionState.Blocked);
        return WorldMotionResult.Blocked;
    }

    private static Position Interpolate(Position start, Position goal, double fraction) =>
        new(start.X + (goal.X - start.X) * fraction,
            start.Y + (goal.Y - start.Y) * fraction,
            start.Z + (goal.Z - start.Z) * fraction);

    private static Position CalculateDynamicGoal(WorldMotionKind kind, Position self, Position target)
    {
        if (kind != WorldMotionKind.Flee) return target;
        var dx = self.X - target.X;
        var dy = self.Y - target.Y;
        var magnitude = Math.Sqrt(dx * dx + dy * dy);
        if (magnitude <= 1e-12) return new Position(self.X + 3, self.Y, self.Z);
        return new Position(self.X + 3 * dx / magnitude, self.Y + 3 * dy / magnitude, self.Z);
    }

    private static Position RandomGoal(Position from, ulong seed, ulong entityId)
    {
        var value = unchecked((seed ^ (entityId * 0x9E3779B97F4A7C15UL)) * 0xBF58476D1CE4E5B9UL);
        var dx = (int)(value % 7UL) - 3;
        var dy = (int)((value >> 12) % 7UL) - 3;
        if (dx == 0 && dy == 0) dx = 1;
        return new Position(from.X + dx, from.Y + dy, from.Z);
    }

    private void StopInboundMotions(EntityIdentity retired)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Motion.Target == retired && entry.Motion.CanAdvance)
                entry.Motion.Fail(WorldMotionState.TargetLost);
        }
    }
}
