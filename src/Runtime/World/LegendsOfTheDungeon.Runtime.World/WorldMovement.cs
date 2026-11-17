using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Security.Contracts;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>最近一次已接受的移动输入状态；这里不拥有第二份 Position 真相。</summary>
public enum WorldMovementMode
{
    /// <summary>最近输入只有朝向变化或静止。</summary>
    Idle,
    /// <summary>最近输入改变了世界位置。</summary>
    Moving
}

/// <summary>移动意图是不可变的候选位置，不能直接写实体 Transform。</summary>
public readonly record struct WorldMovementIntent
{
    /// <summary>创建含完整实体代际、主体、会话版本和输入序号的意图。</summary>
    /// <param name="identity">期望移动的完整实体身份。</param>
    /// <param name="subject">P60.F 已绑定的主体对象，不能由客户端提供字符串冒充。</param>
    /// <param name="sessionVersion">可信会话绑定时下发的非零版本。</param>
    /// <param name="sequence">该会话单调增加的非零输入序号。</param>
    /// <param name="tick">业务提交对应的逻辑 Tick。</param>
    /// <param name="destination">期望到达的有限位置及规范化 Yaw。</param>
    public WorldMovementIntent(EntityIdentity identity, BoundIdentity subject, ulong sessionVersion,
        ulong sequence, Tick tick, Transform destination)
    {
        if (identity.IsEmpty) throw new ArgumentException("Movement identity must be complete.", nameof(identity));
        ArgumentNullException.ThrowIfNull(subject);
        ValidateSubject(subject.SubjectKey);
        ArgumentOutOfRangeException.ThrowIfZero(sessionVersion);
        ArgumentOutOfRangeException.ThrowIfZero(sequence);
        Identity = identity;
        Subject = subject;
        SessionVersion = sessionVersion;
        Sequence = sequence;
        Tick = tick;
        Destination = destination;
    }

    /// <summary>目标实体完整代际。</summary>
    public EntityIdentity Identity { get; }
    /// <summary>已绑定 P60.F 主体对象，不包含密码/Token，生产签发需通过可信 Adapter。</summary>
    public BoundIdentity Subject { get; }
    /// <summary>会话版本；重新绑定后旧会话不可继续提交。</summary>
    public ulong SessionVersion { get; }
    /// <summary>会话内输入序号。</summary>
    public ulong Sequence { get; }
    /// <summary>该输入的逻辑时间。</summary>
    public Tick Tick { get; }
    /// <summary>待验证的目标 Transform，不授予直接改写权。</summary>
    public Transform Destination { get; }

    internal static void ValidateSubject(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 64 ||
            key.Any(c => !((c >= 'a' && c <= 'z') ||
                            (c >= '0' && c <= '9') || c is '.' or '_' or '-')))
        {
            throw new ArgumentException("Movement subject must be a normalized identity key.", nameof(key));
        }
    }
}

/// <summary>按值复制的移动会话和已接受输入基准；Position 仍只来自 P24 Transform。</summary>
/// <param name="Bound">实体是否绑定可信移动主体。</param>
/// <param name="SubjectKey">绑定的 P60.F 规范化主体键；未绑定为空。</param>
/// <param name="SubjectOrigin">绑定身份来源（Production 或 LocalTest）；未绑定为空。</param>
/// <param name="SessionVersion">绑定会话递增版本。</param>
/// <param name="LastSequence">最后成功的非零输入序号。</param>
/// <param name="LastTick">最后成功输入的逻辑 Tick；初始为配置基准 Tick。</param>
/// <param name="TickDuration">每个逻辑 Tick 的固定毫秒数。</param>
/// <param name="Mode">最近一次成功输入的移动模式。</param>
/// <param name="EffectiveSpeed">当前 P25 属性计算的米/秒速度，非法/缺失时为零。</param>
public readonly record struct WorldMovementSnapshot(
    bool Bound, string? SubjectKey, IdentityOrigin? SubjectOrigin, ulong SessionVersion, ulong LastSequence, Tick LastTick,
    Duration TickDuration, WorldMovementMode Mode, decimal EffectiveSpeed);

/// <summary>移动能力使用 P25 中唯一的最大速度属性和固定确定性预算。</summary>
public static class WorldSpeedModel
{
    /// <summary>P25 属性键 movement.speed（米/秒），可叠加/撤销 Flat 与 Percent Modifier。</summary>
    public static AttributeKey SpeedStat => new("movement.speed");

    /// <summary>单次移动输入最多跨过四个 Tick，禁止在长时间无输入后凭空瞬移。</summary>
    public const ulong MaximumTickGap = 4;

    /// <summary>基础移动输入允许的速度上限（米/秒），更复杂例外归 P29。</summary>
    public const decimal MaximumMetersPerSecond = 100m;

    internal static bool TrySpeed(EntityCapabilities capabilities, out decimal speed)
    {
        speed = 0;
        var stats = capabilities.Attributes.Capture().Stats;
        foreach (var stat in stats)
        {
            if (stat.Key == SpeedStat)
            {
                if (stat.EffectiveValue > MaximumMetersPerSecond) return false;
                speed = stat.EffectiveValue;
                return true;
            }
        }

        return false;
    }
}

/// <summary>只存移动输入进度，不保留位置；P17 活跃代际离开时实例随 Entry 退休。</summary>
internal sealed class WorldMovementLedger
{
    private ulong _version;
    private BoundIdentity? _subject;
    private ulong _sequence;
    private Tick _lastTick;
    private Duration _tickDuration;
    private WorldMovementMode _mode;

    internal bool IsBound => _subject is not null;
    internal BoundIdentity? Subject => _subject;
    internal ulong SessionVersion => _version;
    internal ulong Sequence => _sequence;
    internal Tick LastTick => _lastTick;
    internal Duration TickDuration => _tickDuration;

    internal WorldMovementSnapshot Capture(decimal speed) => new(
        IsBound, _subject?.SubjectKey, _subject?.Origin, _version, _sequence, _lastTick, _tickDuration, _mode, speed);

    internal ulong Bind(BoundIdentity subject, Tick baseline, Duration tickDuration)
    {
        // 同一已注册代际重新绑定只推进 SessionVersion，旧包不会覆盖新会话。
        var next = checked(_version + 1);
        _subject = subject;
        _version = next;
        _sequence = 0;
        _lastTick = baseline;
        _tickDuration = tickDuration;
        _mode = WorldMovementMode.Idle;
        return next;
    }

    internal void Invalidate()
    {
        _subject = null;
        _mode = WorldMovementMode.Idle;
    }

    internal void Accept(ulong sequence, Tick tick, bool moved)
    {
        _sequence = sequence;
        _lastTick = tick;
        _mode = moved ? WorldMovementMode.Moving : WorldMovementMode.Idle;
    }
}

/// <summary>P27 仅通过 P17/P18/P20 真正的 WorldInstance 更新移动；不公开底层 Position 写权限。</summary>
public sealed partial class WorldInstance
{
    /// <summary>由可信世界 Host 绑定已认证主体与当前代际，重新绑定使旧会话包失效。</summary>
    /// <param name="context">P18 当前同步世界租约；不能来自异步客户端。</param>
    /// <param name="identity">要配置的已注册活动实体。</param>
    /// <param name="subject">可信 P60.F 已绑定身份；不接受未验证的主体字符串。</param>
    /// <param name="baselineTick">本会话提交的逻辑起点。</param>
    /// <param name="tickDuration">固定 Tick 时长，1–1000ms。</param>
    /// <param name="sessionVersion">成功时的非零会话版本。</param>
    /// <returns>成功、代际过期、缺少 Speed Stat 或不活跃。</returns>
    public WorldEntityResult BindMovementSubject(IWorldWriteContext context, EntityIdentity identity,
        BoundIdentity subject, Tick baselineTick, Duration tickDuration, out ulong sessionVersion)
    {
        Demand(context);
        ArgumentNullException.ThrowIfNull(subject);
        WorldMovementIntent.ValidateSubject(subject.SubjectKey);
        if (tickDuration.Milliseconds is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(tickDuration));
        lock (_gate)
        {
            sessionVersion = 0;
            var status = FindActive(identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            context.Demand(identity.World, identity.Epoch, identity.Id, identity.Generation);
            if (!WorldSpeedModel.TrySpeed(entry!.Capabilities, out _))
                return WorldEntityResult.MovementInvalidSpeed;
            sessionVersion = entry.Movement.Bind(subject, baselineTick, tickDuration);
            entry.Motion.Cancel();
            return WorldEntityResult.MovementBound;
        }
    }

    /// <summary>读取当前代际移动会话、输入进度及 P25 实时速度，不返回可变状态。</summary>
    /// <param name="identity">完整实体代际。</param>
    /// <param name="snapshot">按值复制的移动基准。</param>
    /// <returns>是否在活跃实体注册表找到。</returns>
    public bool TryGetMovement(EntityIdentity identity, out WorldMovementSnapshot snapshot)
    {
        lock (_gate)
        {
            if (FindActive(identity, out var entry) != WorldEntityResult.Added)
            {
                snapshot = default;
                return false;
            }

            WorldSpeedModel.TrySpeed(entry!.Capabilities, out var speed);
            snapshot = entry.Movement.Capture(speed);
            return true;
        }
    }

    /// <summary>校验完整主体/会话/顺序/时间/速度/控制与空间后，使用 P20 唯一重定位入口提交。</summary>
    /// <param name="context">当前真实世界同步租约。</param>
    /// <param name="intent">来自可信 P60.F 入口构造的不可变候选；客户端不能直接获取世界写租约。</param>
    /// <returns>可追踪的接受或拒绝结果，拒绝不消费序号/基准。</returns>
    public WorldEntityResult SubmitMovement(IWorldWriteContext context, WorldMovementIntent intent)
    {
        Demand(context);
        lock (_gate)
        {
            var status = FindActive(intent.Identity, out var entry);
            if (status != WorldEntityResult.Added) return status;
            context.Demand(intent.Identity.World, intent.Identity.Epoch,
                intent.Identity.Id, intent.Identity.Generation);
            var ledger = entry!.Movement;
            if (!ledger.IsBound) return WorldEntityResult.MovementUnbound;
            if (intent.Subject is not { } actor || ledger.Subject is not { } boundSubject ||
                !string.Equals(actor.SubjectKey, boundSubject.SubjectKey, StringComparison.Ordinal) ||
                actor.Origin != boundSubject.Origin || intent.SessionVersion != ledger.SessionVersion)
                return WorldEntityResult.MovementSubjectMismatch;
            if (intent.Sequence <= ledger.Sequence || intent.Tick <= ledger.LastTick)
                return WorldEntityResult.MovementOutOfOrder;
            var elapsedTicks = intent.Tick.Value - ledger.LastTick.Value;
            if (elapsedTicks > WorldSpeedModel.MaximumTickGap)
                return WorldEntityResult.MovementTickGapExceeded;
            if (entry.Capabilities.Life.State != LifeState.Alive)
                return WorldEntityResult.InvalidState;
            if (!WorldSpeedModel.TrySpeed(entry.Capabilities, out var speed))
                return WorldEntityResult.MovementInvalidSpeed;

            var before = entry.Capabilities.Transform.Value;
            var changedPosition = intent.Destination.Position != before.Position;
            var control = entry.Capabilities.Control.Effective;
            if ((control & EntityControlEffects.Stunned) != 0 ||
                (changedPosition && (control & (EntityControlEffects.Rooted | EntityControlEffects.Feared)) != 0))
                return WorldEntityResult.MovementControlled;

            if (changedPosition)
            {
                // P27 只做基本位移预算；路径穿障、异常跳变和玩法例外由 P28/P29 另验。
                var elapsedSeconds = elapsedTicks * (ledger.TickDuration.Milliseconds / 1000d);
                var limit = (double)speed * elapsedSeconds;
                // 先做分轴范围检查，防止大坐标相减或欧氏距离平方溢出。
                var from = before.Position;
                var to = intent.Destination.Position;
                if (!WithinAxis(from.X, to.X, limit) ||
                    !WithinAxis(from.Y, to.Y, limit) ||
                    !WithinAxis(from.Z, to.Z, limit))
                    return WorldEntityResult.MovementSpeedExceeded;
                var dx = (to.X - from.X) / (limit == 0 ? 1 : limit);
                var dy = (to.Y - from.Y) / (limit == 0 ? 1 : limit);
                var dz = (to.Z - from.Z) / (limit == 0 ? 1 : limit);
                if (dx * dx + dy * dy + dz * dz > 1 + 1e-12)
                    return WorldEntityResult.MovementSpeedExceeded;
            }

            if (intent.Destination == before)
            {
                ledger.Accept(intent.Sequence, intent.Tick, false);
                return WorldEntityResult.MovementAccepted;
            }

            var relocation = RelocateCore(context, intent.Identity, intent.Destination, invalidateMovement: false);
            if (relocation != WorldEntityResult.Relocated) return relocation;
            ledger.Accept(intent.Sequence, intent.Tick, changedPosition);
            return WorldEntityResult.MovementAccepted;
        }
    }

    private static bool WithinAxis(double from, double to, double limit) =>
        double.IsFinite(from - to) && Math.Abs(from - to) <= limit;
}
