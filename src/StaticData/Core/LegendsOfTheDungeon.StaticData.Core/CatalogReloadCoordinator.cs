using LegendsOfTheDungeon.StaticData.Abstractions;

namespace LegendsOfTheDungeon.StaticData.Core;

/// <summary>活动定义的受控切换政策，由未来 Cast/Item 等领域状态所有者选择。</summary>
public enum ActiveDefinitionPolicy
{
    /// <summary>定义被修改或删除时拒绝替换；允许只增不改的数据包。</summary>
    RejectChange,
    /// <summary>允许新包替换，但必须保留该实例绑定的旧冻结版本。</summary>
    PinOldVersion,
    /// <summary>没有领域所有者实际迁移凭证时拒绝改变或删除，不能伪装迁移成功。</summary>
    RequireMigration
}

/// <summary>安全的整包替换拒绝代码；消息不包含用户数据或原始定义值。</summary>
public enum CatalogReloadFault
{
    /// <summary>版本不前进。</summary>
    InvalidVersion,
    /// <summary>已有一个待提交候选。</summary>
    AlreadyStaged,
    /// <summary>没有候选或票据已过期。</summary>
    StaleTicket,
    /// <summary>本逻辑 Tick 仍有正在读取的视图。</summary>
    TickActive,
    /// <summary>Tick 编号不合法或边界未完成。</summary>
    InvalidBoundary,
    /// <summary>活动实例按 RejectChange 政策不能修改或删除。</summary>
    ActiveDefinition,
    /// <summary>活动实例需要真实领域迁移，本层拒绝假装完成。</summary>
    MigrationRequired,
    /// <summary>有限历史无法安全腾出空间保留仍在使用的旧版本。</summary>
    RetentionFull,
    /// <summary>活动定义使用标识已存在或预算超限。</summary>
    InvalidBinding
}

/// <summary>固定类型的 Reload 错误，不包含定义值、实体信息或回调原始异常。</summary>
public sealed class CatalogReloadException : Exception
{
    /// <summary>从固定错误代码构造安全异常。</summary>
    /// <param name="fault">安全拒绝类别。</param>
    public CatalogReloadException(CatalogReloadFault fault)
        : base($"catalog_reload.{fault}")
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault));
        }

        Fault = fault;
    }

    /// <summary>固定的拒绝原因。</summary>
    public CatalogReloadFault Fault { get; }
}

/// <summary>已经完成整包校验的单次 Reload 票据，不作为游戏状态授权凭证。</summary>
public sealed class CatalogReloadTicket
{
    internal CatalogReloadTicket(long sequence, CatalogPackageSnapshot candidate)
    {
        Sequence = sequence;
        Candidate = candidate;
    }

    /// <summary>单调候选序号。</summary>
    public long Sequence { get; }
    /// <summary>完整、不可变的待替换包。</summary>
    public CatalogPackageSnapshot Candidate { get; }
}

/// <summary>一次 Tick 的固定整包快照，释放前无法在此协调器提交新包。</summary>
public sealed class CatalogTickView : IDisposable
{
    private readonly Action _release;
    private int _disposed;

    internal CatalogTickView(long tick, CatalogPackageSnapshot package, Action release)
    {
        Tick = tick;
        Package = package;
        _release = release;
    }

    /// <summary>已锁定的逻辑 Tick 编号。</summary>
    public long Tick { get; }
    /// <summary>该 Tick 期间始终相同的整包快照。</summary>
    public CatalogPackageSnapshot Package { get; }

    /// <summary>释放本次读取视图，仅最后一个视图释放后才结束该 Tick。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _release();
        }
    }
}

/// <summary>活动定义注册句柄；Dispose 只释放版本 pin，不修改领域实例。</summary>
public sealed class ActiveDefinitionHandle : IDisposable
{
    private readonly Action<Guid> _release;
    private int _disposed;

    internal ActiveDefinitionHandle(Guid usageId, Action<Guid> release)
    {
        UsageId = usageId;
        _release = release;
    }

    /// <summary>调用方传入且非空的技术关联标识；不作为指标标签。</summary>
    public Guid UsageId { get; }

    /// <summary>显式结束定义的活动使用，使旧版本可在之后安全被回收。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _release(UsageId);
        }
    }
}

/// <summary>每个执行域独立的 P16.R 整包切换协调器；只在已结束 Tick 边界原子换引用。</summary>
public sealed class CatalogReloadCoordinator
{
    /// <summary>历史总容量上限（包括当前版本和被活动对象 pin 的旧版本）。</summary>
    public const int MaximumRetainedPackages = 16;
    /// <summary>可跟踪活动定义使用的固定上限。</summary>
    public const int MaximumActiveDefinitions = 4096;

    private readonly object _gate = new();
    private readonly int _maximumHistory;
    private readonly List<CatalogPackageSnapshot> _history;
    private readonly Dictionary<Guid, ActiveUsage> _active = [];
    private CatalogPackageSnapshot _current;
    private CatalogReloadTicket? _staged;
    private long _sequence;
    private long _activeTick;
    private long _completedTick;
    private long _lastCommitTick;
    private int _activeViews;

    /// <summary>以完整已验证的冻结包启动，每个 World 执行域拥有自己的协调器。</summary>
    /// <param name="initial">已冻结 P16.F 包。</param>
    /// <param name="maximumHistory">2–16 个不同版本（含当前和 pin）的上限。</param>
    public CatalogReloadCoordinator(
        CatalogPackageSnapshot initial, int maximumHistory = 8)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumHistory, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumHistory, MaximumRetainedPackages);
        _current = initial;
        _maximumHistory = maximumHistory;
        _history = [initial];
    }

    /// <summary>当前整包只读快照；跨多个读取操作的一致性应使用 CaptureTick。</summary>
    public CatalogPackageSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>进入正数且严格递增的逻辑 Tick；同 Tick 可有多个视图，但不能混合旧包/新包。</summary>
    /// <param name="tick">由可信 World Owner 提供的逻辑 Tick。</param>
    /// <returns>使用 using 范围释放的固定整包。</returns>
    public CatalogTickView CaptureTick(long tick)
    {
        if (tick <= 0)
        {
            throw new CatalogReloadException(CatalogReloadFault.InvalidBoundary);
        }

        lock (_gate)
        {
            if (_activeViews == 0)
            {
                if (tick <= _completedTick)
                {
                    throw new CatalogReloadException(CatalogReloadFault.InvalidBoundary);
                }

                _activeTick = tick;
            }
            else if (_activeTick != tick)
            {
                throw new CatalogReloadException(CatalogReloadFault.TickActive);
            }

            _activeViews++;
            return new CatalogTickView(tick, _current, EndTickView);
        }
    }

    /// <summary>预先登记活动定义，数据变更时执行拒绝、旧版 pin 或显式迁移策略。</summary>
    /// <param name="usageId">非空唯一关联标识，由领域生成并管理。</param>
    /// <param name="catalog">当前目录名。</param>
    /// <param name="recordId">当前冻结定义 ID（不放入错误字符串或 Metric 标签）。</param>
    /// <param name="policy">活动定义的版本切换策略。</param>
    /// <returns>实例终结时必须 Dispose 的版本注册句柄。</returns>
    public ActiveDefinitionHandle RegisterActiveDefinition(
        Guid usageId, string catalog, string recordId, ActiveDefinitionPolicy policy)
    {
        if (usageId == Guid.Empty || !StaticIdentifier.IsValid(catalog)
            || string.IsNullOrEmpty(recordId) || !Enum.IsDefined(policy))
        {
            throw new CatalogReloadException(CatalogReloadFault.InvalidBinding);
        }

        lock (_gate)
        {
            if (_active.Count >= MaximumActiveDefinitions || _active.ContainsKey(usageId)
                || _current.Find(catalog)?.Find(recordId) is null)
            {
                throw new CatalogReloadException(CatalogReloadFault.InvalidBinding);
            }

            _active.Add(usageId, new ActiveUsage(_current, catalog, recordId, policy));
            return new ActiveDefinitionHandle(usageId, ReleaseActiveDefinition);
        }
    }

    /// <summary>允许在后台加载构造候选，但验证/发布票据串行化且无外部 I/O。</summary>
    /// <param name="loadCandidate">返回完整 P16.F 冻结包的可信加载回调。</param>
    /// <param name="cancellationToken">加载取消信号。</param>
    /// <returns>校验过但尚未影响当前状态的唯一票据。</returns>
    public async Task<CatalogReloadTicket> PrepareAsync(
        Func<CancellationToken, Task<CatalogPackageSnapshot>> loadCandidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loadCandidate);
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = await loadCandidate(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Stage(candidate);
    }

    /// <summary>准备完整新版本，所有检查在写入 pending 之前完成。</summary>
    /// <param name="candidate">完整已校验整包。</param>
    /// <returns>不更改当前可见包的待提交票据。</returns>
    public CatalogReloadTicket Stage(CatalogPackageSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            if (_staged is not null)
            {
                throw new CatalogReloadException(CatalogReloadFault.AlreadyStaged);
            }

            ValidateTransition(candidate);
            if (_sequence == long.MaxValue)
            {
                throw new CatalogReloadException(CatalogReloadFault.StaleTicket);
            }

            _staged = new CatalogReloadTicket(++_sequence, candidate);
            return _staged;
        }
    }

    /// <summary>仅在已完全结束的逻辑 Tick 边界，将整个包一次性原子替换。</summary>
    /// <param name="completedTick">必须与最近完全关闭的 Tick 相同，且每个边界只允许一次提交。</param>
    /// <param name="ticket">本协调器当前唯一的待提交票据。</param>
    public void CommitAtBoundary(long completedTick, CatalogReloadTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (_gate)
        {
            if (!ReferenceEquals(ticket, _staged))
            {
                throw new CatalogReloadException(CatalogReloadFault.StaleTicket);
            }

            if (_activeViews != 0)
            {
                throw new CatalogReloadException(CatalogReloadFault.TickActive);
            }

            if (completedTick <= 0 || completedTick != _completedTick ||
                completedTick == _lastCommitTick)
            {
                throw new CatalogReloadException(CatalogReloadFault.InvalidBoundary);
            }

            // Stage 之后新增/释放 ActiveDefinition 也必须再次核对，否则会遗失 pin。
            ValidateTransition(ticket.Candidate);
            var next = PlannedHistory(ticket.Candidate);
            _history.Clear();
            _history.AddRange(next);
            _current = ticket.Candidate;
            _staged = null;
            _lastCommitTick = completedTick;
        }
    }

    /// <summary>丢弃唯一待发布版本；不会影响运行中的原包。</summary>
    /// <param name="ticket">必须是本协调器原票据。</param>
    public void Abandon(CatalogReloadTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (_gate)
        {
            if (!ReferenceEquals(ticket, _staged))
            {
                throw new CatalogReloadException(CatalogReloadFault.StaleTicket);
            }

            _staged = null;
        }
    }

    /// <summary>取最近有界历史快照；旧包可以在内存保留期内回放，不承诺持久归档。</summary>
    /// <param name="version">正整数包版本。</param>
    /// <param name="hash">可验证小写内容 Hash。</param>
    /// <returns>匹配则返回不可变包，已合法驱逐则返回 null。</returns>
    public CatalogPackageSnapshot? FindRetained(int version, string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        lock (_gate)
        {
            return _history.FirstOrDefault(
                x => x.Version == version &&
                    string.Equals(x.ContentHash, hash, StringComparison.Ordinal));
        }
    }

    /// <summary>有限的版本历史只读快照，包含当前版本。</summary>
    public IReadOnlyList<CatalogPackageSnapshot> RetainedSnapshots()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_history.ToArray());
        }
    }

    private void EndTickView()
    {
        lock (_gate)
        {
            if (--_activeViews == 0)
            {
                _completedTick = _activeTick;
                _activeTick = 0;
            }
        }
    }

    private void ReleaseActiveDefinition(Guid id)
    {
        lock (_gate)
        {
            _active.Remove(id);
        }
    }

    private void ValidateTransition(CatalogPackageSnapshot candidate)
    {
        if (candidate.Version <= _current.Version)
        {
            throw new CatalogReloadException(CatalogReloadFault.InvalidVersion);
        }

        foreach (var use in _active.Values)
        {
            var prior = use.Origin.Find(use.Catalog)?.Find(use.RecordId);
            var updated = candidate.Find(use.Catalog)?.Find(use.RecordId);
            if (SameDefinition(prior, updated))
            {
                continue;
            }

            if (use.Policy == ActiveDefinitionPolicy.RejectChange)
            {
                throw new CatalogReloadException(CatalogReloadFault.ActiveDefinition);
            }

            if (use.Policy == ActiveDefinitionPolicy.RequireMigration)
            {
                // 世界实例需要真实的领域迁移能力，基础库不可冒充成功。
                throw new CatalogReloadException(CatalogReloadFault.MigrationRequired);
            }
        }

        _ = PlannedHistory(candidate);
    }

    private List<CatalogPackageSnapshot> PlannedHistory(CatalogPackageSnapshot candidate)
    {
        var proposed = new List<CatalogPackageSnapshot>(_history) { candidate };
        while (proposed.Count > _maximumHistory)
        {
            var evict = proposed.FindIndex(x => !ReferenceEquals(x, candidate) &&
                !ReferenceEquals(x, _current) &&
                !_active.Values.Any(use => ReferenceEquals(use.Origin, x)));
            if (evict < 0)
            {
                // 当前版本可以在切换后驱逐，但前提是没有任何活动实例绑定。
                evict = proposed.FindIndex(x => !ReferenceEquals(x, candidate) &&
                    !_active.Values.Any(use => ReferenceEquals(use.Origin, x)));
            }

            if (evict < 0)
            {
                throw new CatalogReloadException(CatalogReloadFault.RetentionFull);
            }

            proposed.RemoveAt(evict);
        }

        return proposed;
    }

    private static bool SameDefinition(StaticDataRow? old, StaticDataRow? updated)
    {
        if (old is null || updated is null || old.Fields.Count != updated.Fields.Count)
        {
            return false;
        }

        foreach (var field in old.Fields)
        {
            if (!updated.Fields.TryGetValue(field.Key, out var value) ||
                value.Type != field.Value.Type ||
                !string.Equals(value.CanonicalText, field.Value.CanonicalText,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record ActiveUsage(
        CatalogPackageSnapshot Origin,
        string Catalog,
        string RecordId,
        ActiveDefinitionPolicy Policy);
}
