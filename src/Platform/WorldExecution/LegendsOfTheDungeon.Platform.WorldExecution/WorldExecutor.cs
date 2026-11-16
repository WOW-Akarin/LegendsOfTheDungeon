using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.Concurrency;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Security.Contracts;

namespace LegendsOfTheDungeon.Platform.WorldExecution;

/// <summary>外部命令或可信 Worker 回执被接纳的状态；Accepted 不代表领域提交。</summary>
public enum WorldAdmissionStatus
{
    /// <summary>已进入对应有界车道。</summary>
    Accepted,
    /// <summary>本车道积压已满，未接纳。</summary>
    Busy,
    /// <summary>世界已故障或关闭，未接纳。</summary>
    Closed,
    /// <summary>生产身份或执行时安全策略拒绝。</summary>
    Denied,
    /// <summary>回执 Epoch、Generation、请求序号或票据已过期。</summary>
    Stale
}

/// <summary>执行器单轮消费状态；非成功状态不能代表完成所有已接纳消息。</summary>
public enum WorldRunStatus
{
    /// <summary>同步执行器在预算内完成一轮消费。</summary>
    Completed,
    /// <summary>同世界正由别的线程或嵌套代码执行。</summary>
    Busy,
    /// <summary>上一条同步命令产生不可预期异常，世界必须由未来 Host 恢复。</summary>
    Faulted,
    /// <summary>已关闭且无可处理消息。</summary>
    Closed
}

/// <summary>世界写入租约、版本和生命周期拒绝的安全类别。</summary>
public enum WorldAccessFault
{
    /// <summary>当前不在有效世界独占执行上下文中。</summary>
    LeaseRequired,
    /// <summary>实体 WorldId、Epoch 或 Generation 已失效。</summary>
    InvalidGeneration,
    /// <summary>实体绑定重复或代际不能前进。</summary>
    InvalidBinding,
    /// <summary>世界内实体或回执票据超过有界预算。</summary>
    LimitExceeded
}

/// <summary>不包含原始消息、玩家数据、数据库异常或堆栈的写入拒绝错误。</summary>
public sealed class WorldAccessException : Exception
{
    /// <summary>使用固定拒绝枚举构造安全错误。</summary>
    /// <param name="fault">错误类别。</param>
    public WorldAccessException(WorldAccessFault fault)
        : base($"world_access.{fault}")
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault));
        }

        Fault = fault;
    }

    /// <summary>稳定的安全拒绝类型。</summary>
    public WorldAccessFault Fault { get; }
}

/// <summary>固定预算单轮同步执行报告，不隐瞒未处理工作或世界故障。</summary>
public sealed class WorldRunReport
{
    internal WorldRunReport(WorldRunStatus status, int executed, int stale, int denied, int remaining)
    {
        Status = status;
        Executed = executed;
        Stale = stale;
        Denied = denied;
        Remaining = remaining;
    }

    /// <summary>执行器退出本次消费的类别。</summary>
    public WorldRunStatus Status { get; }
    /// <summary>完整执行的同步回调数量。</summary>
    public int Executed { get; }
    /// <summary>丢弃的旧回执/旧 Epoch 消息数量。</summary>
    public int Stale { get; }
    /// <summary>执行前二次授权失败丢弃的消息数量。</summary>
    public int Denied { get; }
    /// <summary>离开时仍留在有界 Mailbox 的数量。</summary>
    public int Remaining { get; }
}

/// <summary>由可信组合根保管的生产者能力；不向不可信 Socket/HTTP 入口提供。</summary>
public sealed class TrustedWorldProducer
{
    private readonly WorldExecutor _owner;

    internal TrustedWorldProducer(WorldExecutor owner)
    {
        _owner = owner;
    }

    /// <summary>仅可信系统控制/内部调度才能投递同步命令；不直接执行世界写入。</summary>
    /// <param name="action">同步世界命令。</param>
    /// <returns>有界普通车道接纳状态。</returns>
    public WorldAdmissionStatus Offer(IWorldAction action) =>
        _owner.OfferTrusted(action);

    /// <summary>可信 DB/Network Worker 只能回送原世界签发的 opaque 票据和不可变结果命令。</summary>
    /// <param name="ticket">不可伪造的已签发回执票据。</param>
    /// <param name="action">由同步世界执行器处理的回执命令。</param>
    /// <returns>有限关键回执车道的接纳/超期/关闭结果。</returns>
    public WorldAdmissionStatus OfferReply(IWorldReplyTicket ticket, IWorldAction action) =>
        _owner.OfferReply(ticket, action);
}

/// <summary>
/// 逐世界唯一同步写执行器，复用 P12 双车道 Mailbox；不会在任何后台线程签发可异步传播的写入权。
/// </summary>
public sealed class WorldExecutor
{
    private const string OperationCode = "world.execute";
    private const int MaximumTrackedEntities = 10_000;
    private readonly object _stateGate = new();
    private readonly BoundedMailbox<Envelope> _mailbox;
    private readonly ICommandAuthorizationPort _authorizer;
    private readonly int _maximumTickets;
    private readonly Dictionary<EntityId, EntityVersion> _entities = [];
    private readonly Dictionary<long, ReplyTicket> _tickets = [];
    private readonly TrustedWorldProducer _trusted;
    private readonly WorldId _world;
    private WorldEpoch _epoch;
    private long _nextSequence;
    private int _running;
    private int _closed;
    private int _faulted;
    private volatile Scope? _currentScope;

    // Ambient 仅是附加匹配，不独自授予写权；还需 active scope、同步线程、独占运行状态和 Epoch。
    private static readonly AsyncLocal<Scope?> Ambient = new();

    /// <summary>构造每世界独有的限额执行器；生产入口若未注入可信授权规则则默认拒绝。</summary>
    /// <param name="world">非空 World 强类型 ID。</param>
    /// <param name="epoch">非空初始世界运行 Epoch。</param>
    /// <param name="authorizer">P60.F 生产命令授权端口，默认实现拒绝一切。</param>
    /// <param name="normalCapacity">普通命令独立最大队列数。</param>
    /// <param name="criticalCapacity">关键 DB/Worker 回执专属最大队列数。</param>
    /// <param name="maximumCriticalBurst">关键消息优先处理次数的有界上限。</param>
    /// <param name="maximumTickets">等待外部回执的票据最多 1–4096 条。</param>
    public WorldExecutor(
        WorldId world, WorldEpoch epoch,
        ICommandAuthorizationPort authorizer,
        int normalCapacity = 256, int criticalCapacity = 64,
        int maximumCriticalBurst = 8, int maximumTickets = 4096)
    {
        if (world.IsEmpty || epoch.IsEmpty)
        {
            throw new ArgumentException("World 与 Epoch 必须为非空强类型 ID。");
        }

        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTickets, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTickets, 4096);
        _world = world;
        _epoch = epoch;
        _authorizer = authorizer;
        _maximumTickets = maximumTickets;
        _mailbox = new BoundedMailbox<Envelope>(
            normalCapacity, criticalCapacity, maximumCriticalBurst);
        _trusted = new TrustedWorldProducer(this);
    }

    /// <summary>此实例唯一所有的世界。</summary>
    public WorldId World => _world;

    /// <summary>当前世界 Epoch，只能在执行器未运行时被组合根单调推进。</summary>
    public WorldEpoch Epoch
    {
        get
        {
            lock (_stateGate)
            {
                return _epoch;
            }
        }
    }

    /// <summary>执行器是否已故障；故障后不得在未知部分领域状态上继续运行。</summary>
    public bool IsFaulted => Volatile.Read(ref _faulted) != 0;

    /// <summary>取得只供可信组合根/内部 Worker 保管的投递能力，不向网络用户泄露。</summary>
    public TrustedWorldProducer CreateTrustedProducer() => _trusted;

    /// <summary>不可信外部入口须同时具备生产身份和命令授权；接纳与执行前均重复检查。</summary>
    /// <param name="identity">由 P60.F 可信 Adapter 绑定的生产身份。</param>
    /// <param name="action">已解析且固定预算的同步命令，不包含原始网络 Payload。</param>
    /// <returns>未通过验证时 Denied，不调用业务世界代码。</returns>
    public WorldAdmissionStatus OfferExternal(BoundIdentity? identity, IWorldAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsAuthorized(identity))
        {
            return WorldAdmissionStatus.Denied;
        }

        lock (_stateGate)
        {
            return OfferUnderLock(new Envelope(action, _epoch, identity, null), MailboxLane.Normal);
        }
    }

    /// <summary>仅可信 Host 可推进世界运行世代，同时使旧消息、旧实体代际与回执票据全部失效。</summary>
    /// <param name="newEpoch">必须严格大于当前 Epoch。</param>
    public void AdvanceEpoch(WorldEpoch newEpoch)
    {
        if (newEpoch.IsEmpty)
        {
            throw new ArgumentException("世界 Epoch 不得为空。", nameof(newEpoch));
        }

        lock (_stateGate)
        {
            if (Volatile.Read(ref _running) != 0)
            {
                throw new WorldAccessException(WorldAccessFault.LeaseRequired);
            }

            if (newEpoch.Value <= _epoch.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(newEpoch));
            }

            _epoch = newEpoch;
            _entities.Clear();
            _tickets.Clear();
        }
    }

    /// <summary>只同步消费不超过预算的命令；故障时封闭接纳，不掩盖可能已经提交的写操作。</summary>
    /// <param name="maximumItems">本轮最多执行或拒绝的消息数，必须为正。</param>
    /// <returns>实际执行、过期、拒绝和队列剩余数量。</returns>
    public WorldRunReport RunBatch(int maximumItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return Report(WorldRunStatus.Busy, 0, 0, 0);
        }

        var executed = 0;
        var stale = 0;
        var denied = 0;
        try
        {
            if (Volatile.Read(ref _faulted) != 0)
            {
                return Report(WorldRunStatus.Faulted, 0, 0, 0);
            }

            for (var i = 0; i < maximumItems; i++)
            {
                // 每次只 Drain(1)，即使中途执行失败，剩余已接纳消息仍保留队列中。
                var batch = _mailbox.Drain(1);
                if (batch.Count == 0)
                {
                    break;
                }

                var envelope = batch[0].Value;
                if (!ValidateEnvelope(envelope))
                {
                    if (envelope.Identity is not null)
                    {
                        denied++;
                    }
                    else
                    {
                        stale++;
                    }

                    continue;
                }

                var scope = new Scope(this, envelope.Epoch, Environment.CurrentManagedThreadId);
                var before = Ambient.Value;
                _currentScope = scope;
                Ambient.Value = scope;
                try
                {
                    envelope.Action.Execute(new ExecutionContext(this, scope));
                    executed++;
                }
                catch (Exception)
                {
                    // 领域写可能已经部分发生；不能继续执行队列或伪装成可回滚结果。
                    Volatile.Write(ref _faulted, 1);
                    Complete();
                    return Report(WorldRunStatus.Faulted, executed, stale, denied);
                }
                finally
                {
                    scope.Close();
                    _currentScope = null;
                    Ambient.Value = before;
                }
            }

            var status = Volatile.Read(ref _closed) != 0 && _mailbox.Count == 0
                ? WorldRunStatus.Closed : WorldRunStatus.Completed;
            return Report(status, executed, stale, denied);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>组合根关闭世界外部接纳，已接纳消息仍可按预算尝试排空；故障除外。</summary>
    public void Complete()
    {
        Volatile.Write(ref _closed, 1);
        _mailbox.Complete();
    }

    private WorldRunReport Report(
        WorldRunStatus status, int executed, int stale, int denied) =>
        new(status, executed, stale, denied, _mailbox.Count);

    internal WorldAdmissionStatus OfferTrusted(IWorldAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_stateGate)
        {
            return OfferUnderLock(new Envelope(action, _epoch, null, null), MailboxLane.Normal);
        }
    }

    internal WorldAdmissionStatus OfferReply(IWorldReplyTicket ticket, IWorldAction action)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(action);
        lock (_stateGate)
        {
            if (Volatile.Read(ref _closed) != 0 || Volatile.Read(ref _faulted) != 0)
            {
                return WorldAdmissionStatus.Closed;
            }

            if (!IsCurrentTicket(ticket, requireQueued: false))
            {
                return WorldAdmissionStatus.Stale;
            }

            var actual = (ReplyTicket)ticket;
            var admission = OfferUnderLock(
                new Envelope(action, _epoch, null, actual), MailboxLane.Critical);
            if (admission == WorldAdmissionStatus.Accepted)
            {
                actual.Enqueued = true;
            }

            return admission;
        }
    }

    private WorldAdmissionStatus OfferUnderLock(Envelope envelope, MailboxLane lane)
    {
        if (Volatile.Read(ref _closed) != 0 || Volatile.Read(ref _faulted) != 0)
        {
            return WorldAdmissionStatus.Closed;
        }

        return _mailbox.Offer(envelope, lane) switch
        {
            MailboxOfferResult.Accepted => WorldAdmissionStatus.Accepted,
            MailboxOfferResult.Busy => WorldAdmissionStatus.Busy,
            _ => WorldAdmissionStatus.Closed
        };
    }

    private bool IsAuthorized(BoundIdentity? identity)
    {
        if (ProductionIdentityGuard.RequireProduction(identity).IsFailure)
        {
            return false;
        }

        try
        {
            return _authorizer.Authorize(identity, OperationCode).IsSuccess;
        }
        catch (Exception)
        {
            // 任何安全适配器异常都必须按拒绝处理；不进入可变世界。
            return false;
        }
    }

    private bool ValidateEnvelope(Envelope message)
    {
        lock (_stateGate)
        {
            if (message.Epoch != _epoch)
            {
                if (message.Ticket is not null)
                {
                    _tickets.Remove(message.Ticket.RequestSequence);
                }

                return false;
            }

            if (message.Ticket is not null)
            {
                var valid = IsCurrentTicket(message.Ticket, requireQueued: true);
                _tickets.Remove(message.Ticket.RequestSequence);
                return valid;
            }
        }

        return message.Identity is null || IsAuthorized(message.Identity);
    }

    private bool IsCurrentTicket(IWorldReplyTicket candidate, bool requireQueued)
    {
        return candidate is ReplyTicket ticket
            && ReferenceEquals(ticket.Owner, this)
            && ticket.World == _world && ticket.Epoch == _epoch
            && _tickets.TryGetValue(ticket.RequestSequence, out var registered)
            && ReferenceEquals(ticket, registered)
            && ticket.Enqueued == requireQueued
            && _entities.TryGetValue(ticket.Entity, out var entity)
            && entity.Alive && entity.Generation == ticket.Generation
            && entity.LatestRequest == ticket.RequestSequence;
    }

    private void DemandScope(Scope scope)
    {
        if (!ReferenceEquals(_currentScope, scope)
            || !ReferenceEquals(Ambient.Value, scope)
            || Volatile.Read(ref scope.Active) == 0
            || Environment.CurrentManagedThreadId != scope.Thread
            || Volatile.Read(ref _running) == 0
            || Volatile.Read(ref _faulted) != 0
            || scope.Epoch != _epoch)
        {
            throw new WorldAccessException(WorldAccessFault.LeaseRequired);
        }
    }

    private void DemandEntity(Scope scope, EntityId entity, EntityGeneration generation)
    {
        DemandScope(scope);
        if (entity.IsEmpty || generation.IsEmpty)
        {
            throw new WorldAccessException(WorldAccessFault.InvalidGeneration);
        }

        lock (_stateGate)
        {
            if (!_entities.TryGetValue(entity, out var current)
                || !current.Alive || current.Generation != generation)
            {
                throw new WorldAccessException(WorldAccessFault.InvalidGeneration);
            }
        }
    }

    private void Bind(Scope scope, EntityId entity, EntityGeneration generation)
    {
        DemandScope(scope);
        if (entity.IsEmpty || generation.IsEmpty)
        {
            throw new WorldAccessException(WorldAccessFault.InvalidBinding);
        }

        lock (_stateGate)
        {
            if (_entities.TryGetValue(entity, out var previous))
            {
                if (previous.Alive || generation.Value <= previous.Generation.Value)
                {
                    throw new WorldAccessException(WorldAccessFault.InvalidBinding);
                }
            }
            else if (_entities.Count >= MaximumTrackedEntities)
            {
                throw new WorldAccessException(WorldAccessFault.LimitExceeded);
            }

            _entities[entity] = new EntityVersion(generation, true, 0);
        }
    }

    private void Retire(Scope scope, EntityId entity, EntityGeneration generation)
    {
        DemandEntity(scope, entity, generation);
        lock (_stateGate)
        {
            var current = _entities[entity];
            _entities[entity] = current with { Alive = false };
            foreach (var id in _tickets.Values.Where(t => t.Entity == entity)
                         .Select(t => t.RequestSequence).ToArray())
            {
                _tickets.Remove(id);
            }
        }
    }

    private ReplyTicket Issue(Scope scope, EntityId entity, EntityGeneration generation)
    {
        DemandEntity(scope, entity, generation);
        lock (_stateGate)
        {
            var current = _entities[entity];
            var replacingOwnTicket = current.LatestRequest != 0
                && _tickets.ContainsKey(current.LatestRequest);
            if (_nextSequence == long.MaxValue ||
                _tickets.Count >= _maximumTickets && !replacingOwnTicket)
            {
                throw new WorldAccessException(WorldAccessFault.LimitExceeded);
            }

            if (replacingOwnTicket)
            {
                _tickets.Remove(current.LatestRequest);
            }

            var sequence = ++_nextSequence;
            var ticket = new ReplyTicket(this, _world, _epoch, entity, generation, sequence);
            _tickets.Add(sequence, ticket);
            _entities[entity] = current with { LatestRequest = sequence };
            return ticket;
        }
    }

    private sealed record EntityVersion(
        EntityGeneration Generation, bool Alive, long LatestRequest);

    private sealed record Envelope(
        IWorldAction Action, WorldEpoch Epoch, BoundIdentity? Identity, ReplyTicket? Ticket);

    private sealed class ReplyTicket : IWorldReplyTicket
    {
        internal ReplyTicket(
            WorldExecutor owner, WorldId world, WorldEpoch epoch,
            EntityId entity, EntityGeneration generation, long requestSequence)
        {
            Owner = owner;
            World = world;
            Epoch = epoch;
            Entity = entity;
            Generation = generation;
            RequestSequence = requestSequence;
        }

        internal WorldExecutor Owner { get; }
        internal bool Enqueued { get; set; }
        public WorldId World { get; }
        public WorldEpoch Epoch { get; }
        public EntityId Entity { get; }
        public EntityGeneration Generation { get; }
        public long RequestSequence { get; }
    }

    private sealed class Scope
    {
        internal Scope(WorldExecutor owner, WorldEpoch epoch, int thread)
        {
            Owner = owner;
            Epoch = epoch;
            Thread = thread;
            Active = 1;
        }

        internal WorldExecutor Owner { get; }
        internal WorldEpoch Epoch { get; }
        internal int Thread { get; }
        internal int Active;

        internal void Close() => Volatile.Write(ref Active, 0);
    }

    private sealed class ExecutionContext : IWorldWriteContext
    {
        private readonly WorldExecutor _owner;
        private readonly Scope _scope;

        internal ExecutionContext(WorldExecutor owner, Scope scope)
        {
            _owner = owner;
            _scope = scope;
        }

        public WorldId World => _owner._world;
        public WorldEpoch Epoch => _scope.Epoch;
        public void Demand(EntityId entity, EntityGeneration generation) =>
            _owner.DemandEntity(_scope, entity, generation);
        public void BindEntity(EntityId entity, EntityGeneration generation) =>
            _owner.Bind(_scope, entity, generation);
        public void RetireEntity(EntityId entity, EntityGeneration generation) =>
            _owner.Retire(_scope, entity, generation);
        public IWorldReplyTicket IssueReplyTicket(EntityId entity, EntityGeneration generation) =>
            _owner.Issue(_scope, entity, generation);
    }
}
