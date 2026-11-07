namespace GcmMonitor.Core;

public enum CollectionMode { Passive, Demonstration }
public sealed record VolumeInfo(string Name, long? CapacityBytes, long? FreeBytes, string[] DiskIds);
public sealed record PhysicalDiskInfo(string Id, int Number, string Model, string BusType, string MediaType,
    long? CapacityBytes, VolumeInfo[] Volumes, string Limitation);
public sealed record ComputerInventory(string ComputerName, string WindowsEdition, string WindowsVersion,
    string Architecture, DateTimeOffset? LastBoot, string CpuName, int? Cores, int LogicalProcessors,
    long? PhysicalMemoryBytes, long[] MemoryModulesBytes, PhysicalDiskInfo[] Disks, string[] Limitations,
    DateTimeOffset Timestamp, bool IsDemo = false)
{
    public GpuInfo[] Gpus { get; init; } = [];
}
public sealed record ProcessObservation(int Pid, DateTimeOffset StartedAt, string Name, TimeSpan? CpuTime,
    long? WorkingSetBytes, DateTimeOffset Timestamp);
public sealed record ProcessAggregate(int Pid, DateTimeOffset StartedAt, string Name, double CpuSeconds,
    double CpuObservedSeconds, double CpuPeakPercent, double WorkingSetByteSeconds,
    double MemoryObservedSeconds, long WorkingSetPeakBytes, DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt, bool Ended, int Samples)
{
    public double? CpuMeanPercent(int logicalProcessors) => CpuObservedSeconds > 0
        ? CpuSeconds / CpuObservedSeconds / logicalProcessors * 100 : null;
    public double? WorkingSetMeanBytes => MemoryObservedSeconds > 0 ? WorkingSetByteSeconds / MemoryObservedSeconds : null;
}
public sealed record MetricStatistics(ModuleId Module, string Metric, string Component, string Unit,
    double Minimum, double Maximum, double WeightedSum, double ObservedSeconds, int ValidSamples,
    DateTimeOffset FirstSample, DateTimeOffset LastSample)
{
    public double? TimeWeightedMean => ObservedSeconds > 0 ? WeightedSum / ObservedSeconds : null;
}
public sealed record CollectionIssue(DateTimeOffset Timestamp, ModuleId? Module, string Message);
public sealed record CollectionBatch(Measurement[] Measurements, ProcessAggregate[] Processes, CollectionIssue[] Issues)
{
    public DiagnosticRecord[] Records { get; init; } = [];
    public ModuleAssessment[] Assessments { get; init; } = [];
}

public interface IInventoryProvider
{
    Task<ComputerInventory> ReadAsync(CancellationToken cancellationToken = default);
}
public interface IPassiveCollectionSource : IAsyncDisposable
{
    Task<ComputerInventory> ReadInventoryAsync(CancellationToken cancellationToken);
    Task<CollectionBatch> SampleAsync(DiagnosticOptions options, DateTimeOffset timestamp, bool includeProcesses,
        bool resetBaselines, CancellationToken cancellationToken);
}

/// <summary>Média trapezoidal ponderada no tempo; lacunas não são interpoladas.</summary>
public sealed class MetricAccumulator
{
    private readonly Dictionary<(ModuleId, string, string), MetricStatistics> statistics = [];
    private readonly Dictionary<(ModuleId, string, string), Measurement> previous = [];

    public void Add(Measurement reading)
    {
        reading.Validate();
        var key = (reading.Module, reading.Metric, reading.Component);
        if (reading.Quality != DataQuality.Valid) { previous.Remove(key); return; }
        if (!statistics.ContainsKey(key) && statistics.Count >= 4096) throw new InvalidOperationException("Limite de séries distintas atingido; coleta interrompida sem descartar silenciosamente estatísticas.");
        if (!statistics.TryGetValue(key, out var item))
            item = new(reading.Module, reading.Metric, reading.Component, reading.Unit, reading.Value!.Value,
                reading.Value.Value, 0, 0, 0, reading.Timestamp, reading.Timestamp);
        double seconds = 0, area = 0;
        if (previous.TryGetValue(key, out var last))
        {
            var delta = (reading.Timestamp - last.Timestamp).TotalSeconds;
            if (delta > 0 && delta <= 15) { seconds = delta; area = (last.Value!.Value + reading.Value!.Value) / 2 * delta; }
        }
        statistics[key] = item with
        {
            Minimum = Math.Min(item.Minimum, reading.Value!.Value),
            Maximum = Math.Max(item.Maximum, reading.Value.Value),
            WeightedSum = item.WeightedSum + area,
            ObservedSeconds = item.ObservedSeconds + seconds,
            ValidSamples = item.ValidSamples + 1,
            LastSample = reading.Timestamp
        };
        previous[key] = reading;
    }
    public MetricStatistics[] Snapshot() => statistics.Values.ToArray();
    public void ResetBaselines() => previous.Clear();
}

/// <summary>Mantém somente processos observados; os encerrados saem da memória após serem persistidos.</summary>
public sealed class ProcessAccumulator(int logicalProcessors)
{
    private readonly Dictionary<(int, DateTimeOffset), (ProcessObservation Previous, ProcessAggregate Aggregate)> active = [];

    public ProcessAggregate[] Add(IReadOnlyList<ProcessObservation> observations, bool resetBaselines = false)
    {
        if (logicalProcessors < 1) throw new ArgumentOutOfRangeException(nameof(logicalProcessors));
        var changed = new List<ProcessAggregate>();
        var seen = new HashSet<(int, DateTimeOffset)>();
        foreach (var observation in observations)
        {
            var key = (observation.Pid, observation.StartedAt); seen.Add(key);
            var aggregate = active.TryGetValue(key, out var value) ? value.Aggregate :
                new ProcessAggregate(observation.Pid, observation.StartedAt, observation.Name, 0, 0, 0, 0, 0,
                    observation.WorkingSetBytes ?? 0, observation.Timestamp, observation.Timestamp, false, 0);
            double seconds = value.Previous is not null ? (observation.Timestamp - value.Previous.Timestamp).TotalSeconds : 0;
            double cpu = 0, cpuSeconds = 0, memorySeconds = 0, memoryArea = 0;
            if (!resetBaselines && value.Previous is not null && seconds is > 0 and <= 15)
            {
                if (observation.CpuTime is { } currentCpu && value.Previous.CpuTime is { } previousCpu && currentCpu >= previousCpu)
                { cpu = (currentCpu - previousCpu).TotalSeconds; cpuSeconds = seconds; }
                if (observation.WorkingSetBytes is { } currentRam && value.Previous.WorkingSetBytes is { } previousRam)
                { memorySeconds = seconds; memoryArea = (currentRam / 2.0 + previousRam / 2.0) * seconds; }
            }
            aggregate = aggregate with
            {
                CpuSeconds = aggregate.CpuSeconds + cpu,
                CpuObservedSeconds = aggregate.CpuObservedSeconds + cpuSeconds,
                CpuPeakPercent = Math.Max(aggregate.CpuPeakPercent, cpuSeconds > 0 ? cpu / cpuSeconds / logicalProcessors * 100 : 0),
                WorkingSetByteSeconds = aggregate.WorkingSetByteSeconds + memoryArea,
                MemoryObservedSeconds = aggregate.MemoryObservedSeconds + memorySeconds,
                WorkingSetPeakBytes = Math.Max(aggregate.WorkingSetPeakBytes, observation.WorkingSetBytes ?? 0),
                LastObservedAt = observation.Timestamp,
                Samples = aggregate.Samples + 1,
                Ended = false
            };
            if (active.Count >= 16384 && !active.ContainsKey(key)) throw new InvalidOperationException("Limite de processos simultâneos atingido.");
            active[key] = (observation, aggregate); changed.Add(aggregate);
        }
        foreach (var missing in active.Keys.Where(x => !seen.Contains(x)).ToArray())
        { changed.Add(active[missing].Aggregate with { Ended = true }); active.Remove(missing); }
        return changed.ToArray();
    }
}
