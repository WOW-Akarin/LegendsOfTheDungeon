using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Runtime.Abstractions;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>固定的四阶段逻辑 Tick 顺序；顺序属于协议，不依赖字典枚举或注册线程。</summary>
public enum WorldTickPhase
{
    /// <summary>输入接纳、顺序应用。</summary>
    Input = 0,
    /// <summary>到期的逻辑计时器。</summary>
    Timers = 1,
    /// <summary>游戏模拟状态转换。</summary>
    Simulation = 2,
    /// <summary>单个 Tick 的最终提交阶段。</summary>
    Commit = 3
}

/// <summary>绑定到一个固定阶段的同步世界工作单元，不允许异步持有世界写入租约。</summary>
public interface IWorldTickStep
{
    /// <summary>阶段内唯一且稳定的序号名称，采用 Ordinal 排序，与注册顺序无关。</summary>
    string Key { get; }

    /// <summary>执行所在固定阶段。</summary>
    WorldTickPhase Phase { get; }

    /// <summary>在 P18 真实同步租约中执行当前 Tick 的领域操作。</summary>
    /// <param name="context">有效世界同步写入租约。</param>
    /// <param name="world">本步骤只应修改的世界实例。</param>
    /// <param name="tick">单调逻辑 Tick 编号。</param>
    void Execute(IWorldWriteContext context, WorldInstance world, Tick tick);
}

/// <summary>单次完成的 Tick 记录，Hash 仅覆盖确定性领域状态，不含真实性能时钟或线程号。</summary>
public readonly record struct WorldTickCommit(Tick Tick, string StateHash);

/// <summary>
/// 一个世界的固定阶段执行器。所有 Tick 调用都必须由该 World 对应的真实 P18 同步 Scope 签发。
/// 步骤按 Phase/Key 固定排序，禁止动态注册或把非确定性时间混入领域状态。
/// </summary>
public sealed class WorldTickPipeline
{
    private static readonly WorldTickPhase[] Phases =
    [
        WorldTickPhase.Input, WorldTickPhase.Timers,
        WorldTickPhase.Simulation, WorldTickPhase.Commit
    ];

    private readonly object _gate = new();
    private readonly IWorldTickStep[] _steps;
    private Tick _lastTick;
    private string _lastHash = string.Empty;

    /// <summary>创建独立的世界 Tick Pipeline，第一次完成的逻辑 Tick 必须是 1。</summary>
    /// <param name="world">已构造、随后由 P17 激活的世界实例。</param>
    /// <param name="clock">稳定 TickDuration 的逻辑时钟；由可信调度层推进。</param>
    /// <param name="steps">固定步骤列表；按 Phase/Key 排序，重复键拒绝。</param>
    public WorldTickPipeline(WorldInstance world, IClock clock, IEnumerable<IWorldTickStep> steps)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(steps);
        if (clock.TickDuration.IsZero)
        {
            throw new ArgumentException("TickDuration must be positive.", nameof(clock));
        }

        _steps = steps.ToArray();
        if (_steps.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), "Maximum 128 phase steps per world.");
        }

        if (_steps.Any(s => s is null || string.IsNullOrWhiteSpace(s.Key) ||
                            s.Key.Length > 96 || !Enum.IsDefined(s.Phase)))
        {
            throw new ArgumentException("Tick steps require a valid phase and stable non-empty Key.", nameof(steps));
        }

        _steps = _steps.OrderBy(s => s.Phase).ThenBy(s => s.Key, StringComparer.Ordinal).ToArray();
        for (var i = 1; i < _steps.Length; i++)
        {
            if (_steps[i - 1].Phase == _steps[i].Phase &&
                StringComparer.Ordinal.Equals(_steps[i - 1].Key, _steps[i].Key))
            {
                throw new ArgumentException("Duplicate (phase, key) is forbidden.", nameof(steps));
            }
        }
    }

    /// <summary>由本 Pipeline 独占修改的 P17 世界。</summary>
    public WorldInstance World { get; }

    /// <summary>只读取逻辑 Tick/时长/单调时间，核心不修改墙钟。</summary>
    public IClock Clock { get; }

    /// <summary>上次完整提交的逻辑 Tick，初始为 0。</summary>
    public Tick LastCommittedTick
    {
        get
        {
            lock (_gate)
            {
                return _lastTick;
            }
        }
    }

    /// <summary>最后一次完整提交的 SHA256 十六进制摘要；首次完成前为空。</summary>
    public string LastStateHash
    {
        get
        {
            lock (_gate)
            {
                return _lastHash;
            }
        }
    }

    /// <summary>提供只读的稳定步骤顺序（Phase、Key）；不是可变内部数组。</summary>
    public IReadOnlyList<(WorldTickPhase Phase, string Key)> OrderedSteps =>
        Array.AsReadOnly(_steps.Select(s => (s.Phase, s.Key)).ToArray());

    /// <summary>只在真实同步世界租约中按顺序执行全部 Tick Phase，并在成功后提交快照 Hash。</summary>
    /// <param name="context">P18 WorldExecutor 在 IWorldAction.Execute 内签发的同步上下文。</param>
    /// <param name="tick">严格连续的下一个正整数 Tick。</param>
    /// <returns>已提交 Tick 与稳定状态摘要。</returns>
    public WorldTickCommit Execute(IWorldWriteContext context, Tick tick)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DemandWorld(World.World, World.Epoch);
        lock (_gate)
        {
            if (World.State != WorldLifecycle.Running)
            {
                throw new InvalidOperationException("Tick requires a running WorldInstance.");
            }

            try
            {
                // 重复/跳号/提前 Tick 同样属于不可恢复的同步世界执行故障；
                // 必须与 Phase 异常走同一 Faulted 路径，不能只把 P18 置为故障。
                if (tick.Value != _lastTick.Add(1).Value || Clock.CurrentTick < tick)
                {
                    throw new InvalidOperationException("Tick must be contiguous and due on the logical clock.");
                }

                foreach (var phase in Phases)
                {
                    foreach (var step in _steps)
                    {
                        if (step.Phase == phase)
                        {
                            step.Execute(context, World, tick);
                        }
                    }
                }

                var digest = WorldStateDigest.Calculate(World.SnapshotForTick(context), World.World, World.Epoch, tick);
                _lastHash = digest;
                _lastTick = tick;
                return new WorldTickCommit(tick, digest);
            }
            catch
            {
                // 阶段可能已写部分状态；不伪装事务回滚，也不允许后续 Tick 继续处理。
                World.Fault(context);
                throw;
            }
        }
    }
}

/// <summary>稳定的字节序列化：SHA256，固定字段顺序、little-endian、按 EntityId 升序。</summary>
internal static class WorldStateDigest
{
    internal static string Calculate(
        IReadOnlyList<WorldEntitySnapshot> snapshots, WorldId world, WorldEpoch epoch, Tick tick)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(digest, 4); // P25: add source-sensitive attribute/resource ledger to deterministic digest
        Add(digest, world.Value);
        Add(digest, epoch.Value);
        Add(digest, tick.Value);
        Add(digest, (ulong)snapshots.Count);

        foreach (var item in snapshots)
        {
            Add(digest, item.Identity.World.Value);
            Add(digest, item.Identity.Epoch.Value);
            Add(digest, item.Identity.Id.Value);
            Add(digest, item.Identity.Generation.Value);
            Add(digest, (ulong)item.Lifecycle);
            Add(digest, (ulong)item.Life);
            AddDouble(digest, item.Transform.Position.X);
            AddDouble(digest, item.Transform.Position.Y);
            AddDouble(digest, item.Transform.Position.Z);
            AddDouble(digest, item.Transform.Yaw.Radians);
            Add(digest, item.Target.HasValue ? 1UL : 0UL);
            if (item.Target is { } target)
            {
                Add(digest, target.World.Value);
                Add(digest, target.Epoch.Value);
                Add(digest, target.Id.Value);
                Add(digest, target.Generation.Value);
            }
            Add(digest, (ulong)item.Control);
            Add(digest, item.ControlSequence);
            Add(digest, item.TagSequence);
            Add(digest, (ulong)item.ControlGrants.Count);
            foreach (var control in item.ControlGrants)
            {
                Add(digest, control.Sequence);
                Add(digest, control.Source.Value);
                Add(digest, (ulong)control.Flag);
            }

            Add(digest, (ulong)item.TagGrants.Count);
            foreach (var grant in item.TagGrants)
            {
                Add(digest, grant.Sequence);
                Add(digest, grant.Source.Value);
                AddText(digest, grant.Tag);
            }

            Add(digest, (ulong)item.PermanentTags.Count);
            foreach (var tag in item.PermanentTags)
            {
                AddText(digest, tag);
            }

            Add(digest, (ulong)item.Tags.Count);
            foreach (var tag in item.Tags)
            {
                AddText(digest, tag);
            }

            AddRelated(digest, item.Relationships.Owner);
            AddRelated(digest, item.Relationships.Creator);
            AddRelated(digest, item.Relationships.Controller);
            AddRelated(digest, item.Relationships.Summoner);
            var attributes = item.Attributes;
            Add(digest, attributes.LastModifierSequence);
            Add(digest, (ulong)attributes.Stats.Count);
            foreach (var stat in attributes.Stats)
            {
                AddText(digest, stat.Key.Value);
                AddDecimal(digest, stat.BaseValue);
                AddDecimal(digest, stat.EffectiveValue);
                Add(digest, stat.IsDerived ? 1UL : 0UL);
            }

            Add(digest, (ulong)attributes.Derived.Count);
            foreach (var rule in attributes.Derived)
            {
                AddText(digest, rule.Key.Value);
                AddText(digest, rule.Source.Value);
                AddDecimal(digest, rule.Factor);
                AddDecimal(digest, rule.Offset);
            }

            Add(digest, (ulong)attributes.Modifiers.Count);
            foreach (var mod in attributes.Modifiers)
            {
                Add(digest, mod.Handle.Sequence);
                Add(digest, mod.Source.Value);
                AddText(digest, mod.Key.Value);
                Add(digest, (ulong)mod.Operation);
                AddDecimal(digest, mod.Amount);
                Add(digest, unchecked((ulong)mod.Priority));
            }

            Add(digest, (ulong)attributes.Resources.Count);
            foreach (var resource in attributes.Resources)
            {
                AddText(digest, resource.Key.Value);
                AddText(digest, resource.MaximumStat.Value);
                AddDecimal(digest, resource.Current);
                AddDecimal(digest, resource.Maximum);
            }
        }

        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static void AddText(IncrementalHash digest, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Add(digest, (ulong)bytes.Length);
        digest.AppendData(bytes);
    }

    private static void AddRelated(IncrementalHash digest, EntityIdentity? identity)
    {
        Add(digest, identity.HasValue ? 1UL : 0UL);
        if (identity is { } value)
        {
            Add(digest, value.World.Value);
            Add(digest, value.Epoch.Value);
            Add(digest, value.Id.Value);
            Add(digest, value.Generation.Value);
        }
    }

    private static void AddDecimal(IncrementalHash digest, decimal value) =>
        AddText(digest, value.ToString("G29", CultureInfo.InvariantCulture));

    private static void AddDouble(IncrementalHash digest, double value) =>
        Add(digest, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

    private static void Add(IncrementalHash digest, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        digest.AppendData(bytes);
    }
}
