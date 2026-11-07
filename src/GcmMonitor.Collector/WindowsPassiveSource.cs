using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using GcmMonitor.Core;

namespace GcmMonitor.Collector;

[SupportedOSPlatform("windows")]
public sealed class WindowsPassiveSource : IPassiveCollectionSource
{
    public const string DiskReadCounter = @"\PhysicalDisk(*)\Disk Read Bytes/sec";
    public const string DiskWriteCounter = @"\PhysicalDisk(*)\Disk Write Bytes/sec";
    private CpuTimes? previousCpu;
    private WindowsPerformanceQuery? diskQuery;
    private WindowsPerformanceQuery? memoryQuery;
    private WindowsPerformanceQuery? gpuQuery;
    private readonly HashSet<string> gpuComponents = [];
    private ProcessAccumulator? processAccumulator;
    private readonly Dictionary<int, ProcessObservation> knownProcesses = [];
    private readonly WindowsAdvancedSource advanced = new();
    public Task<ComputerInventory> ReadInventoryAsync(CancellationToken cancellationToken) => new WindowsInventoryProvider().ReadAsync(cancellationToken);

    public Task<CollectionBatch> SampleAsync(DiagnosticOptions options, DateTimeOffset timestamp, bool includeProcesses,
        bool resetBaselines, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var measurements = new List<Measurement>(); var issues = new List<CollectionIssue>();
        void Reading(ModuleId module, string metric, string component, double? value, string unit, string source, string? reason = null,
            DataQuality? quality = null) => measurements.Add(new(module, metric, component, value, unit, timestamp, source,
                quality ?? (value.HasValue ? DataQuality.Valid : DataQuality.Unavailable), false, reason));
        if (options.Modules.Contains(ModuleId.Cpu))
        {
            try
            {
                if (WindowsNativeMetrics.LogicalProcessors > 64) Reading(ModuleId.Cpu, "CPU", "CPU total", null, "%", "GetSystemTimes", "Mais de 64 processadores lógicos: agregação de grupos ainda não validada.");
                else
                {
                    var current = WindowsNativeMetrics.ReadCpu();
                    var value = !resetBaselines && previousCpu is { } previous ? CpuUsage.Calculate(previous, current) : null;
                    previousCpu = current;
                    Reading(ModuleId.Cpu, "CPU", "CPU total", value, "%", "GetSystemTimes / deltas kernel+user-idle", value is null ? "Aguardando intervalo válido entre amostras." : null,
                        value is null ? DataQuality.Insufficient : DataQuality.Valid);
                }
            }
            catch (Win32Exception) { Reading(ModuleId.Cpu, "CPU", "CPU total", null, "%", "GetSystemTimes", "API indisponível."); }
        }
        if (options.Modules.Contains(ModuleId.Memory))
        {
            try
            {
                var memory = WindowsNativeMetrics.ReadMemory(); const double gib = 1024 * 1024 * 1024;
                Reading(ModuleId.Memory, "RAM", "Memória física", (memory.TotalPhysical - memory.AvailablePhysical) / (double)memory.TotalPhysical * 100, "%", "GlobalMemoryStatusEx / física ocupada");
                Reading(ModuleId.Memory, "RAM ocupada", "Memória física", (memory.TotalPhysical - memory.AvailablePhysical) / gib, "GiB", "GlobalMemoryStatusEx");
                Reading(ModuleId.Memory, "RAM disponível", "Memória física", memory.AvailablePhysical / gib, "GiB", "GlobalMemoryStatusEx");
                Reading(ModuleId.Memory, "Comprometida", "Comprometimento do sistema", memory.CommitTotal / gib, "GiB", "GetPerformanceInfo / CommitTotal × PageSize");
                Reading(ModuleId.Memory, "Limite de comprometimento", "Comprometimento do sistema", memory.CommitLimit / gib, "GiB", "GetPerformanceInfo / CommitLimit × PageSize");
                Reading(ModuleId.Memory, "Cache do sistema", "SystemCache", memory.SystemCache / gib, "GiB", "GetPerformanceInfo / SystemCache × PageSize");
            }
            catch (Win32Exception) { Reading(ModuleId.Memory, "RAM", "Memória física", null, "%", "GlobalMemoryStatusEx / GetPerformanceInfo", "API de memória indisponível."); }
            try
            {
                bool baseline = memoryQuery is null || resetBaselines;
                memoryQuery ??= new(@"\Memory\Pages Input/sec", @"\Memory\Pages Output/sec");
                var rates = memoryQuery.Sample();
                foreach (var (path, values) in rates)
                    Reading(ModuleId.Memory, path.EndsWith("Input/sec", StringComparison.Ordinal) ? "Paginação: entrada" : "Paginação: saída",
                        "Paginação do sistema", baseline ? null : values.Values.Select(x => (double?)x).SingleOrDefault(), "páginas/s", "PDH / " + path,
                        baseline || values.Count == 0 ? "Contador aguardando intervalo válido ou indisponível." : null,
                        baseline ? DataQuality.Insufficient : null);
            }
            catch (Win32Exception)
            {
                memoryQuery?.Dispose(); memoryQuery = null;
                issues.Add(new(timestamp, ModuleId.Memory, "Contadores de paginação indisponíveis; dados de RAM preservados."));
            }
        }
        if (options.Modules.Contains(ModuleId.Storage))
        {
            try
            {
                bool baseline = diskQuery is null || resetBaselines;
                diskQuery ??= new(DiskReadCounter, DiskWriteCounter);
                var values = diskQuery.Sample();
                foreach (var id in options.DiskIds)
                {
                    string number = id.StartsWith("disk:", StringComparison.Ordinal) ? id[5..] : "";
                    double? Find(string path) => values[path].Where(x => x.Key.Split(' ')[0] == number).Select(x => (double?)x.Value).SingleOrDefault();
                    double? read = baseline ? null : Find(DiskReadCounter), write = baseline ? null : Find(DiskWriteCounter);
                    Reading(ModuleId.Storage, "Leitura do disco", id, read / 1_000_000, "MB/s", "PDH / PhysicalDisk / Disk Read Bytes/sec", read is null ? "Contador sem duas amostras válidas." : null, baseline ? DataQuality.Insufficient : null);
                    Reading(ModuleId.Storage, "Gravação do disco", id, write / 1_000_000, "MB/s", "PDH / PhysicalDisk / Disk Write Bytes/sec", write is null ? "Contador sem duas amostras válidas." : null, baseline ? DataQuality.Insufficient : null);
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidDataException)
            {
                diskQuery?.Dispose(); diskQuery = null;
                foreach (var id in options.DiskIds) Reading(ModuleId.Storage, "Atividade do disco", id, null, "MB/s", "PDH / PhysicalDisk", "Contador físico indisponível; inventário/espaço preservados.");
            }
        }
        var processes = Array.Empty<ProcessAggregate>();
        if (includeProcesses && options.Modules.Contains(ModuleId.Processes))
        {
            var observations = new List<ProcessObservation>(); var alive = new HashSet<int>(); int restricted = 0;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    cancellationToken.ThrowIfCancellationRequested(); alive.Add(process.Id);
                    try
                    {
                        var start = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
                        TimeSpan? cpu = null; long? ram = null;
                        try { cpu = process.TotalProcessorTime; } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { restricted++; }
                        try { ram = process.WorkingSet64; } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { restricted++; }
                        var observation = new ProcessObservation(process.Id, start, process.ProcessName, cpu, ram, timestamp);
                        observations.Add(observation); knownProcesses[process.Id] = observation;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        restricted++;
                        if (knownProcesses.TryGetValue(process.Id, out var known))
                            observations.Add(known with { CpuTime = null, WorkingSetBytes = null, Timestamp = timestamp });
                    }
                }
            }
            foreach (var pid in knownProcesses.Keys.Where(x => !alive.Contains(x)).ToArray()) knownProcesses.Remove(pid);
            processAccumulator ??= new(WindowsNativeMetrics.LogicalProcessors);
            processes = processAccumulator.Add(observations, resetBaselines);
            if (restricted > 0) issues.Add(new(timestamp, ModuleId.Processes, $"{restricted} leituras de processos indisponíveis por encerramento ou restrição de acesso. Sem leitura de conteúdo dos processos."));
        }
        var extra = advanced.Sample(options, timestamp, cancellationToken);
        if (options.Modules.Contains(ModuleId.Sensors))
        {
            const string path = @"\GPU Engine(*)\Utilization Percentage";
            var current = new HashSet<string>();
            try
            {
                bool baseline = gpuQuery is null || resetBaselines;
                gpuQuery ??= new(path);
                foreach (var engine in GpuEngineUsage.Aggregate(gpuQuery.Sample()[path]))
                {
                    current.Add(engine.Component);
                    Reading(ModuleId.Sensors, "GPU por engine", engine.Component, baseline ? null : engine.Percent, "%",
                        "PDH / GPU Engine / soma por LUID+físico+engine (modelo não correlacionado)",
                        "Contador sem intervalo válido ou agregado fora de 0–100%. Não soma engines diferentes.", baseline ? DataQuality.Insufficient : null);
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidDataException)
            { gpuQuery?.Dispose(); gpuQuery = null; }
            foreach (var component in gpuComponents.Where(x => !current.Contains(x)))
                Reading(ModuleId.Sensors, "GPU por engine", component, null, "%", "PDH / GPU Engine", "Engine anteriormente observado indisponível; não substituído por zero.");
            gpuComponents.Clear(); foreach (var component in current) gpuComponents.Add(component);
            if (gpuQuery is not null) extra = extra with
            {
                Assessments = extra.Assessments.Select(x => x.Module == ModuleId.Sensors && x.State == ModuleState.Unavailable
                    ? x with { State = ModuleState.Partial, Summary = "GPU por PDH disponível para amostragem; temperaturas e RPM dependem de outras fontes." } : x).ToArray()
            };
            if (current.Count == 0) Reading(ModuleId.Sensors, "GPU por engine", "GPU / PDH", null, "%", "PDH / GPU Engine", "Fonte WDDM não exposta nesta execução.");
        }
        return new CollectionBatch([.. measurements, .. extra.Measurements], processes, [.. issues, .. extra.Issues])
        { Records = extra.Records, Assessments = extra.Assessments };
    }, cancellationToken);

    public ValueTask DisposeAsync() { diskQuery?.Dispose(); memoryQuery?.Dispose(); gpuQuery?.Dispose(); return ValueTask.CompletedTask; }
}
