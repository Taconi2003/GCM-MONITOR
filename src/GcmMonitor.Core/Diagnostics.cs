namespace GcmMonitor.Core;

public enum SessionState { NotStarted, Validating, Running, Finalizing, Completed, Cancelled, Interrupted, Error }
public enum ModuleState { Pending, Running, Completed, Partial, Error, Unavailable, Cancelled }
public enum DataQuality { Valid, Unavailable, Error, Insufficient }
public enum FindingSeverity { Normal, Attention, Failure, Unavailable }
public enum ModuleId { Memory = 1, Cpu, Storage, WindowsEvents, Battery, WindowsUpdate, Sensors, Processes, Devices, Wifi, Power }

public sealed record ModuleDefinition(ModuleId Id, string Code, string Name, string Icon, bool Continuous);

public static class ModuleCatalog
{
    public static IReadOnlyList<ModuleDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new ModuleDefinition(ModuleId.Memory, "M01", "Memória RAM e paginação", "\uE950", true),
        new ModuleDefinition(ModuleId.Cpu, "M02", "Processador (CPU)", "\uE9D9", true),
        new ModuleDefinition(ModuleId.Storage, "M03", "Armazenamento e SMART", "\uEDA2", true),
        new ModuleDefinition(ModuleId.WindowsEvents, "M04", "Eventos relevantes do Windows", "\uE9CE", false),
        new ModuleDefinition(ModuleId.Battery, "M05", "Bateria", "\uE850", true),
        new ModuleDefinition(ModuleId.WindowsUpdate, "M06", "Windows Update", "\uE895", false),
        new ModuleDefinition(ModuleId.Sensors, "M07", "Temperaturas e ventoinhas", "\uE9CA", true),
        new ModuleDefinition(ModuleId.Processes, "M08", "Processos Top 10", "\uE9D9", true),
        new ModuleDefinition(ModuleId.Devices, "M09", "Dispositivos e drivers", "\uE772", false),
        new ModuleDefinition(ModuleId.Wifi, "M10", "Wi-Fi", "\uE701", true),
        new ModuleDefinition(ModuleId.Power, "M11", "Estado de energia", "\uE945", true)
    });
}

public static class DiagnosticDurations
{
    public static IReadOnlyList<int> Minutes { get; } = Array.AsReadOnly(new[] { 5, 30, 60, 240 });
}

public sealed record DiagnosticOptions
{
    public ModuleId[] Modules { get; init; } = ModuleCatalog.All.Select(x => x.Id).ToArray();
    public int MonitoringMinutes { get; init; } = 30;
    public bool StressEnabled { get; init; } = false;
    public int StressMinutes { get; init; } = 5;
    public string[] DiskIds { get; init; } = ["demo-disk-0", "demo-disk-1"];
    public bool DetailedBattery { get; init; }
    public bool DetailedWifi { get; init; }

    public void Validate()
    {
        if (Modules is null || Modules.Length == 0 || Modules.Distinct().Count() != Modules.Length || Modules.Any(x => !Enum.IsDefined(x)))
            throw new ArgumentException("Selecione ao menos um módulo válido, sem duplicatas.");
        if (!DiagnosticDurations.Minutes.Contains(MonitoringMinutes) || !DiagnosticDurations.Minutes.Contains(StressMinutes))
            throw new ArgumentException("As durações permitidas são 5 min, 30 min, 1 h e 4 h.");
        if (StressEnabled && !StressReleasePolicy.IsReleased) throw new ArgumentException(string.Join(" ", StressReleasePolicy.Evaluate(true, DateTimeOffset.UtcNow).Reasons));
        if (DetailedBattery && !Modules.Contains(ModuleId.Battery) || DetailedWifi && !Modules.Contains(ModuleId.Wifi))
            throw new ArgumentException("A análise detalhada requer o respectivo módulo.");
        if (DiskIds is null || DiskIds.Distinct().Count() != DiskIds.Length || Modules.Contains(ModuleId.Storage) &&
            (DiskIds.Length == 0 || DiskIds.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Selecione ao menos um disco físico válido, sem duplicatas.");
    }
}

public sealed record Measurement(ModuleId Module, string Metric, string Component, double? Value,
    string Unit, DateTimeOffset Timestamp, string Source, DataQuality Quality, bool IsDemo, string? Reason = null)
{
    public void Validate()
    {
        if (Quality == DataQuality.Valid && (Value is null || !double.IsFinite(Value.Value)))
            throw new ArgumentException("Leitura válida requer valor finito.");
        if (Quality != DataQuality.Valid && Value is not null)
            throw new ArgumentException("Leitura indisponível não pode produzir valor fictício.");
        if (string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(Unit))
            throw new ArgumentException("Fonte e unidade são obrigatórias.");
    }
}

public sealed record ModuleRun(ModuleId Id, ModuleState State, string Source, string Summary, int ReadingCount = 0,
    DateTimeOffset? StartedAt = null, DateTimeOffset? EndedAt = null, string? Limitation = null);
public sealed record Finding(ModuleId Module, FindingSeverity Severity, string Observation, string Evidence,
    string Interpretation, string Recommendation, bool IsCollectorIssue = false);
public sealed record SessionTransition(SessionState From, SessionState To, DateTimeOffset Timestamp, string Reason);

public sealed record DiagnosticSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ComputerName { get; init; } = "DEMO-GCM01";
    public string AppVersion { get; init; } = ProductVersion.Value;
    public bool IsDemo { get; init; } = true;
    public DiagnosticOptions Options { get; init; } = new();
    public SessionState State { get; init; } = SessionState.NotStarted;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; init; }
    public TimeSpan EffectiveDuration { get; init; }
    public ModuleRun[] Modules { get; init; } = [];
    public Measurement[] Measurements { get; init; } = [];
    public Finding[] Findings { get; init; } = [];
    public SessionTransition[] Transitions { get; init; } = [];
    public string Notes { get; init; } = "";
    public string IntegrityNote { get; init; } = "Dados sintéticos; nenhuma medição deste computador.";
    public ComputerInventory? Inventory { get; init; }
    public MetricStatistics[] Statistics { get; init; } = [];
    public ProcessAggregate[] Processes { get; init; } = [];
    public CollectionIssue[] Issues { get; init; } = [];
    public DiagnosticRecord[] Records { get; init; } = [];
    public StressAssessment? Stress { get; init; }
    public ThermalTrace? ThermalRehearsal { get; init; }
    public int? EngineProcessId { get; init; }
    public DateTimeOffset? EngineProcessStartedAt { get; init; }
    public bool IsTerminal => State is SessionState.Completed or SessionState.Cancelled or SessionState.Interrupted or SessionState.Error;

    public DiagnosticSession TransitionTo(SessionState next, string reason, DateTimeOffset timestamp)
    {
        bool allowed = State switch
        {
            SessionState.NotStarted => next == SessionState.Validating,
            SessionState.Validating => next is SessionState.Running or SessionState.Cancelled or SessionState.Error or SessionState.Interrupted,
            SessionState.Running => next is SessionState.Finalizing or SessionState.Cancelled or SessionState.Error or SessionState.Interrupted,
            SessionState.Finalizing => next is SessionState.Completed or SessionState.Cancelled or SessionState.Error or SessionState.Interrupted,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"Transição inválida: {State} → {next}.");
        var terminal = next is SessionState.Completed or SessionState.Cancelled or SessionState.Interrupted or SessionState.Error;
        return this with
        {
            State = next,
            EndedAt = terminal ? timestamp : null,
            Transitions = [.. Transitions, new(State, next, timestamp, reason)]
        };
    }
}
