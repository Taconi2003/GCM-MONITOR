using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Engine;

namespace GcmMonitor.UnitTests;

public sealed class TelemetryTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CpuDeltaSubtractsIdleAndIncludesKernelOnlyOnce()
    {
        Assert.Equal(60, CpuUsage.Calculate(new(100, 200, 100), new(140, 270, 130)));
        Assert.Null(CpuUsage.Calculate(new(100, 200, 100), new(99, 270, 130)));
        Assert.Null(CpuUsage.Calculate(new(100, 200, 100), new(100, 200, 100)));
    }
    [Fact]
    public void WeightedMetricMeanUsesActualIntervalsAndExcludesGaps()
    {
        var stats = new MetricAccumulator();
        Measurement Point(double? value, int seconds) => new(ModuleId.Cpu, "CPU", "total", value, "%", Epoch.AddSeconds(seconds), "fixture", value.HasValue ? DataQuality.Valid : DataQuality.Unavailable, true);
        stats.Add(Point(10, 0)); stats.Add(Point(30, 2)); stats.Add(Point(50, 10));
        var first = stats.Snapshot().Single(); Assert.Equal(36, first.TimeWeightedMean); Assert.Equal(10, first.ObservedSeconds); Assert.Equal(50, first.Maximum);
        stats.Add(Point(null, 12)); stats.Add(Point(90, 14)); stats.Add(Point(80, 40));
        Assert.Equal(10, stats.Snapshot().Single().ObservedSeconds);
    }
    [Fact]
    public void ProcessCpuAndWorkingSetAreTimeWeightedAndPidReuseDoesNotMerge()
    {
        var accumulator = new ProcessAccumulator(4);
        ProcessObservation Observe(int seconds, double cpu, long memory, DateTimeOffset? start = null) => new(42, start ?? Epoch, "fixture.exe", TimeSpan.FromSeconds(cpu), memory, Epoch.AddSeconds(seconds));
        accumulator.Add([Observe(0, 100, 100)]);
        var p = accumulator.Add([Observe(2, 102, 300)]).Single();
        Assert.Equal(2, p.CpuSeconds); Assert.Equal(25, p.CpuMeanPercent(4)); Assert.Equal(200, p.WorkingSetMeanBytes);
        var reused = accumulator.Add([Observe(3, 0, 500, Epoch.AddSeconds(3))]);
        Assert.Equal(2, reused.Length); Assert.Contains(reused, x => x.Ended && x.CpuSeconds == 2);
        Assert.Contains(reused, x => !x.Ended && x.CpuSeconds == 0 && x.StartedAt == Epoch.AddSeconds(3));
    }
    [Fact]
    public void MissingProcessCountersDoNotFillInZerosOrIntegrateUnobservedTime()
    {
        var accumulator = new ProcessAccumulator(2);
        accumulator.Add([new(1, Epoch, "fixture", TimeSpan.FromSeconds(1), 100, Epoch)]);
        accumulator.Add([new(1, Epoch, "fixture", null, null, Epoch.AddSeconds(5))]);
        var item = accumulator.Add([new(1, Epoch, "fixture", TimeSpan.FromSeconds(4), 200, Epoch.AddSeconds(10))]).Single();
        Assert.Null(item.WorkingSetMeanBytes); Assert.Null(item.CpuMeanPercent(2));
    }
    [Fact]
    public async Task PassiveSessionRunsApprovedDurationWithoutAcceleratingRealClockContract()
    {
        var clock = new FastClock(); var repository = new MemoryRepository(); var source = new FakeSource();
        var snapshots = new List<DiagnosticSession>();
        var last = await new PassiveSessionRunner(repository, source, clock).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Cpu, ModuleId.Memory], MonitoringMinutes = 5 }, s => { snapshots.Add(s); return Task.CompletedTask; }, default);
        Assert.Equal(TimeSpan.FromMinutes(5), last.EffectiveDuration); Assert.Equal(SessionState.Completed, last.State);
        Assert.False(last.IsDemo); Assert.All(last.Measurements, x => Assert.False(x.IsDemo)); Assert.True(last.Measurements.Length <= 512);
        Assert.Equal(150, last.Statistics.Single(x => x.Module == ModuleId.Cpu).ValidSamples);
        Assert.Contains(snapshots, x => x.State == SessionState.Finalizing);
    }
    [Fact]
    public async Task IsolatedCollectorFailureDoesNotCancelOtherModules()
    {
        var last = await new PassiveSessionRunner(new MemoryRepository(), new FakeSource { FailCpu = true }, new FastClock()).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Cpu, ModuleId.Memory], MonitoringMinutes = 5 }, _ => Task.CompletedTask, default);
        Assert.Equal(SessionState.Completed, last.State);
        Assert.Equal(ModuleState.Error, last.Modules.Single(x => x.Id == ModuleId.Cpu).State);
        Assert.Equal(ModuleState.Completed, last.Modules.Single(x => x.Id == ModuleId.Memory).State);
        Assert.All(last.Findings, x => Assert.True(x.IsCollectorIssue));
    }
    [Fact]
    public async Task UnsupportedModulesFinishImmediatelyAndAreNotHealthy()
    {
        var last = await new PassiveSessionRunner(new MemoryRepository(), new FakeSource { SensorsUnavailable = true }, new FastClock()).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Sensors], MonitoringMinutes = 240 }, _ => Task.CompletedTask, default);
        Assert.Equal(TimeSpan.Zero, last.EffectiveDuration); Assert.Equal(ModuleState.Unavailable, last.Modules.Single().State);
        Assert.Empty(last.Measurements); Assert.DoesNotContain(last.Findings, x => x.Severity == FindingSeverity.Normal);
    }
    [Fact]
    public async Task CancellationPreservesPartialResultsWithoutFinishingChosenDuration()
    {
        using var cancel = new CancellationTokenSource(); var clock = new FastClock();
        var last = await new PassiveSessionRunner(new MemoryRepository(), new FakeSource(), clock).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Memory], MonitoringMinutes = 30 }, s => { if (s.Measurements.Length >= 3) cancel.Cancel(); return Task.CompletedTask; }, cancel.Token);
        Assert.Equal(SessionState.Cancelled, last.State); Assert.True(last.EffectiveDuration.TotalMinutes < 30);
        Assert.Equal(ModuleState.Partial, last.Modules.Single().State); Assert.False(last.Options.StressEnabled);
    }
    [Fact]
    public async Task BrokenPersistenceStopsSessionRatherThanContinuingUnrecordedCollection()
    {
        var source = new FakeSource();
        await Assert.ThrowsAsync<IOException>(() => new PassiveSessionRunner(new FailingRepository(), source, new FastClock()).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Cpu] }, _ => Task.CompletedTask, default));
        Assert.Equal(0, source.Calls);
    }
    [Fact]
    public async Task PersistenceFailureAfterCollectionStartsStopsFurtherSampling()
    {
        var source = new FakeSource();
        await Assert.ThrowsAsync<IOException>(() => new PassiveSessionRunner(new FailingRepository(6), source, new FastClock()).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Cpu] }, _ => Task.CompletedTask, default));
        Assert.InRange(source.Calls, 1, 4);
    }
    [Fact]
    public async Task DelayedSamplingRecordsGapWithoutInterpolatingMissingInterval()
    {
        var last = await new PassiveSessionRunner(new MemoryRepository(), new FakeSource(), new FastClock(true)).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Memory], MonitoringMinutes = 5 }, _ => Task.CompletedTask, default);
        Assert.Contains(last.Issues, x => x.Message.Contains("Lacuna"));
        Assert.True(last.Statistics.Single().ObservedSeconds < 280);
    }

    private sealed class FastClock(bool oversleep = false) : ISessionClock
    {
        public TimeSpan Elapsed { get; private set; }
        public DateTimeOffset UtcNow => Epoch + Elapsed;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Elapsed += delay; if (oversleep) { Elapsed += TimeSpan.FromSeconds(20); oversleep = false; } return Task.CompletedTask; }
    }
    private sealed class FakeSource : IPassiveCollectionSource
    {
        public bool FailCpu { get; init; }
        public bool SensorsUnavailable { get; init; }
        public int Calls { get; private set; }
        public Task<ComputerInventory> ReadInventoryAsync(CancellationToken cancellationToken) => Task.FromResult(
            new ComputerInventory("FIXTURE", "Windows fixture", "test", "x64", Epoch, "CPU fixture", 2, 4, 16L * 1024 * 1024 * 1024, [], [], [], Epoch));
        public Task<CollectionBatch> SampleAsync(DiagnosticOptions options, DateTimeOffset timestamp, bool includeProcesses, bool resetBaselines, CancellationToken cancellationToken)
        {
            Calls++;
            if (SensorsUnavailable && options.Modules.Contains(ModuleId.Sensors)) return Task.FromResult(new CollectionBatch([], [], [])
            { Assessments = [new(ModuleId.Sensors, ModuleState.Unavailable, "fixture", "Sem sensor na fixture.")] });
            if (FailCpu && options.Modules.Contains(ModuleId.Cpu)) throw new System.ComponentModel.Win32Exception(5);
            return Task.FromResult(new CollectionBatch(options.Modules.Select(x => new Measurement(x, x.ToString(), "fixture", 20, "%", timestamp, "fixture source", DataQuality.Valid, false)).ToArray(), [], []));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FailingRepository(int failAt = 1) : ISessionRepository
    {
        private int writes;
        public Task SaveBatchAsync(DiagnosticSession session, CollectionBatch batch, CancellationToken cancellationToken = default) => SaveAsync(session, cancellationToken);
        public Task<ProcessAggregate[]> ReadProcessRankingsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<ProcessAggregate>());
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(DiagnosticSession session, CancellationToken cancellationToken = default)
        { if (++writes >= failAt) throw new IOException("Simulated full disk"); return Task.CompletedTask; }
        public Task<DiagnosticSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DiagnosticSession?>(null);
        public Task<IReadOnlyList<DiagnosticSession>> ListAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DiagnosticSession>>([]);
        public Task SaveNotesAsync(Guid id, string notes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> ClearTerminalAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
