using System.Diagnostics;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform.WorldExecution;
using LegendsOfTheDungeon.Runtime.Abstractions;
using LegendsOfTheDungeon.Runtime.World;

namespace LegendsOfTheDungeon.Platform.WorldScheduling;

/// <summary>Tick 执行耗时来自单调计时器，不进入确定性领域 Hash。</summary>
public interface IWorldTickExecutionMeter
{
    /// <summary>获取仅用于耗时测量的单调时间戳。</summary>
    long Timestamp();

    /// <summary>从指定时间戳计算正向经过时间。</summary>
    /// <param name="started">本计时器产生的起点时间戳。</param>
    TimeSpan Elapsed(long started);
}

/// <summary>生产平台使用的 Stopwatch 单调耗时实现。</summary>
public sealed class StopwatchWorldTickMeter : IWorldTickExecutionMeter
{
    /// <inheritdoc />
    public long Timestamp() => Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public TimeSpan Elapsed(long started) => Stopwatch.GetElapsedTime(started);
}

/// <summary>可信组合根所绑定的 P17 世界、P18 技术执行器和 P19 确定性 Pipeline。</summary>
public sealed class WorldTickBinding
{
    internal WorldTickScheduler.TickAction? Pending;

    /// <summary>绑定世界、运行它的执行器及唯一 Tick Pipeline；三者的 World/Epoch 必须一致。</summary>
    /// <param name="executor">不得交给未授权网络代码的 P18 执行器。</param>
    /// <param name="pipeline">属于同世界的确定性 Tick 阶段。</param>
    public WorldTickBinding(WorldExecutor executor, WorldTickPipeline pipeline)
    {
        Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        if (executor.World != pipeline.World.World || executor.Epoch != pipeline.World.Epoch)
        {
            throw new ArgumentException("WorldExecutor and WorldTickPipeline must have matching World/Epoch.");
        }

        Producer = executor.CreateTrustedProducer();
    }

    /// <summary>绑定的世界执行器。</summary>
    public WorldExecutor Executor { get; }

    /// <summary>属于该世界的固定 Tick Pipeline。</summary>
    public WorldTickPipeline Pipeline { get; }

    internal TrustedWorldProducer Producer { get; }

    /// <summary>绑定世界 ID。</summary>
    public WorldId World => Pipeline.World.World;
}

/// <summary>多世界同一轮有界处理的状态。</summary>
public enum WorldTickScheduleStatus
{
    /// <summary>本轮完成，可能仍有下一轮需处理的积压 Tick。</summary>
    Completed,
    /// <summary>重复调用同一调度器时被拒绝，未处理额外工作。</summary>
    Busy
}

/// <summary>单世界本轮的队列与执行状态。</summary>
public enum WorldTickLaneStatus
{
    /// <summary>本轮可处理的 Tick 正常完成。</summary>
    Completed,
    /// <summary>调度预算已耗尽或 P18 邮箱未消费到 Tick，工作尚在队列中。</summary>
    Deferred,
    /// <summary>P18 拒绝本轮接纳（邮箱满）。</summary>
    Backpressured,
    /// <summary>该世界已有另一执行线程，本轮没有获得执行租约。</summary>
    Busy,
    /// <summary>世界不在 Running 或接纳端已关闭。</summary>
    Closed,
    /// <summary>世界执行器或生命周期进入不可恢复故障。</summary>
    Faulted
}

/// <summary>不包含可变世界对象的单世界 Tick 测量与确定性摘要。</summary>
public sealed record WorldTickLaneReport
{
    internal WorldTickLaneReport(WorldId world, WorldTickLaneStatus status,
        int completedTicks, ulong remainingDueTicks, int slowTicks,
        double maximumDurationMilliseconds, Tick lastTick, string stateHash)
    {
        World = world;
        Status = status;
        CompletedTicks = completedTicks;
        RemainingDueTicks = remainingDueTicks;
        SlowTicks = slowTicks;
        MaximumDurationMilliseconds = maximumDurationMilliseconds;
        LastTick = lastTick;
        StateHash = stateHash;
    }

    /// <summary>对应世界。</summary>
    public WorldId World { get; }
    /// <summary>本轮处理结果；接纳不等于提交。</summary>
    public WorldTickLaneStatus Status { get; }
    /// <summary>确实完成全部 Phase 并提交状态摘要的 Tick 数。</summary>
    public int CompletedTicks { get; }
    /// <summary>当前逻辑时钟已经到期、但仍未完成的 Tick 个数；不是丢弃的 Tick。</summary>
    public ulong RemainingDueTicks { get; }
    /// <summary>本轮实际 Tick 处理超过 TickDuration 的次数。</summary>
    public int SlowTicks { get; }
    /// <summary>本轮单个 Tick 实际执行最大耗时；不进入确定性状态 Hash。</summary>
    public double MaximumDurationMilliseconds { get; }
    /// <summary>最后一次真实提交的 Tick。</summary>
    public Tick LastTick { get; }
    /// <summary>稳定 SHA256 状态摘要；首次成功提交前为空。</summary>
    public string StateHash { get; }
}

/// <summary>按 WorldId 稳定排序的轮次报告，不保留可变 Entity/World 引用。</summary>
public sealed record WorldTickScheduleReport
{
    internal WorldTickScheduleReport(WorldTickScheduleStatus status, IReadOnlyList<WorldTickLaneReport> worlds)
    {
        Status = status;
        Worlds = worlds;
    }

    /// <summary>成功开始或被另一轮拒绝。</summary>
    public WorldTickScheduleStatus Status { get; }
    /// <summary>各世界本轮执行结果。</summary>
    public IReadOnlyList<WorldTickLaneReport> Worlds { get; }
}

/// <summary>
/// 有界多世界 Tick 调度，只有可信 Host 保管。最多 64 World、8 Worker，每世界每轮最多 8 次 Tick；
/// 同世界 Tick 永远通过其 P18 WorldExecutor 同步执行，不能在 Worker 直接修改 WorldInstance。
/// </summary>
public sealed class WorldTickScheduler
{
    private readonly WorldTickBinding[] _bindings;
    private readonly IWorldTickExecutionMeter _meter;
    private readonly int _workers;
    private readonly int _maximumCatchup;
    private readonly int _batchBudget;
    private int _running;

    /// <summary>建立固定世界列表、有限并行度和追赶预算；不得在运行时交换世界归属。</summary>
    /// <param name="bindings">每个 WorldId 恰好一个有效绑定。</param>
    /// <param name="maximumWorkers">同轮最多同时处理的世界数，1–8。</param>
    /// <param name="maximumCatchupTicks">每世界每轮最多执行 Tick 数，1–8。</param>
    /// <param name="maximumMessagesPerBatch">每个 P18 RunBatch 调用的工作预算，1–1024。</param>
    /// <param name="meter">可选单调耗时计量实现；省略时使用 Stopwatch。</param>
    public WorldTickScheduler(IEnumerable<WorldTickBinding> bindings,
        int maximumWorkers = 2, int maximumCatchupTicks = 4,
        int maximumMessagesPerBatch = 128, IWorldTickExecutionMeter? meter = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (maximumWorkers is < 1 or > 8 ||
            maximumCatchupTicks is < 1 or > 8 ||
            maximumMessagesPerBatch is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWorkers),
                "Worker/catch-up/batch limits must be positive and within hard bounds.");
        }

        _bindings = bindings.ToArray();
        if (_bindings.Length is < 1 or > 64 || _bindings.Any(x => x is null))
        {
            throw new ArgumentException("Scheduler requires between one and 64 non-null world bindings.", nameof(bindings));
        }

        _bindings = _bindings.OrderBy(x => x.World.Value).ToArray();
        for (var i = 1; i < _bindings.Length; i++)
        {
            if (_bindings[i - 1].World == _bindings[i].World)
            {
                throw new ArgumentException("WorldId may be registered only once per scheduler.", nameof(bindings));
            }
        }

        _workers = maximumWorkers;
        _maximumCatchup = maximumCatchupTicks;
        _batchBudget = maximumMessagesPerBatch;
        _meter = meter ?? new StopwatchWorldTickMeter();
    }

    /// <summary>按 WorldId 稳定枚举到期世界；有限 Worker 处理后按 WorldId 返回报告。</summary>
    /// <param name="cancellationToken">只取消尚未开始的调度工作，不取消世界同步租约中已开始的 Tick。</param>
    /// <returns>每世界确定性摘要和独立的慢 Tick 测量结果。</returns>
    public async Task<WorldTickScheduleReport> RunDueAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return new WorldTickScheduleReport(WorldTickScheduleStatus.Busy,
                Array.Empty<WorldTickLaneReport>());
        }

        try
        {
            var reports = new WorldTickLaneReport[_bindings.Length];
            await Parallel.ForEachAsync(Enumerable.Range(0, _bindings.Length),
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = _workers
                }, async (index, token) =>
                {
                    // 使 Worker 任务在专用异步调度上被领取，而不允许同一世界重入。
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    reports[index] = RunLane(_bindings[index]);
                }).ConfigureAwait(false);
            return new WorldTickScheduleReport(WorldTickScheduleStatus.Completed,
                Array.AsReadOnly(reports));
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private WorldTickLaneReport RunLane(WorldTickBinding binding)
    {
        var pipeline = binding.Pipeline;
        var completed = 0;
        var slow = 0;
        var maximumMilliseconds = 0.0;
        var status = WorldTickLaneStatus.Completed;
        for (var i = 0; i < _maximumCatchup; i++)
        {
            if (binding.Executor.IsFaulted || pipeline.World.State == WorldLifecycle.Faulted)
            {
                status = WorldTickLaneStatus.Faulted;
                break;
            }

            if (pipeline.World.State != WorldLifecycle.Running ||
                binding.Executor.Epoch != pipeline.World.Epoch)
            {
                status = WorldTickLaneStatus.Closed;
                break;
            }

            var pending = binding.Pending;
            if (pending is null)
            {
                if (pipeline.Clock.CurrentTick <= pipeline.LastCommittedTick)
                {
                    break;
                }

                var next = pipeline.LastCommittedTick.Add(1);
                pending = new TickAction(pipeline, next, _meter);
                var offer = binding.Producer.Offer(pending);
                if (offer != WorldAdmissionStatus.Accepted)
                {
                    status = offer == WorldAdmissionStatus.Busy
                        ? WorldTickLaneStatus.Backpressured : WorldTickLaneStatus.Closed;
                    break;
                }

                binding.Pending = pending;
            }

            var run = binding.Executor.RunBatch(_batchBudget);
            if (pending.Completed)
            {
                completed++;
                maximumMilliseconds = Math.Max(maximumMilliseconds, pending.Elapsed.TotalMilliseconds);
                if (pending.Elapsed > pipeline.Clock.TickDuration.ToTimeSpan())
                {
                    slow++;
                }

                binding.Pending = null;
            }

            if (run.Status == WorldRunStatus.Faulted || binding.Executor.IsFaulted)
            {
                status = WorldTickLaneStatus.Faulted;
                break;
            }

            if (run.Status == WorldRunStatus.Busy)
            {
                status = WorldTickLaneStatus.Busy;
                break;
            }

            if (run.Status == WorldRunStatus.Closed && !pending.Completed)
            {
                status = WorldTickLaneStatus.Closed;
                break;
            }

            if (!pending.Completed)
            {
                // Tick 已排队但 P18 本轮先消费了旧工作，下一轮只继续消费，不能再次 Offer 同一 Tick。
                status = WorldTickLaneStatus.Deferred;
                break;
            }
        }

        var last = pipeline.LastCommittedTick;
        var now = pipeline.Clock.CurrentTick;
        var lag = now.Value > last.Value ? now.Value - last.Value : 0UL;
        if (status == WorldTickLaneStatus.Completed && lag > 0)
        {
            status = WorldTickLaneStatus.Deferred;
        }

        return new WorldTickLaneReport(binding.World, status, completed, lag,
            slow, maximumMilliseconds, last, pipeline.LastStateHash);
    }

    internal sealed class TickAction : IWorldAction
    {
        private readonly WorldTickPipeline _pipeline;
        private readonly Tick _tick;
        private readonly IWorldTickExecutionMeter _meter;
        private int _completed;

        internal TickAction(WorldTickPipeline pipeline, Tick tick, IWorldTickExecutionMeter meter)
        {
            _pipeline = pipeline;
            _tick = tick;
            _meter = meter;
        }

        internal bool Completed => Volatile.Read(ref _completed) != 0;
        internal TimeSpan Elapsed { get; private set; }

        /// <inheritdoc />
        public void Execute(IWorldWriteContext context)
        {
            var start = _meter.Timestamp();
            try
            {
                _pipeline.Execute(context, _tick);
            }
            finally
            {
                Elapsed = _meter.Elapsed(start);
            }

            Volatile.Write(ref _completed, 1);
        }
    }
}
