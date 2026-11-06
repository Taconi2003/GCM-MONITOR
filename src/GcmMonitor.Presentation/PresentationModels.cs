using CommunityToolkit.Mvvm.ComponentModel;
using GcmMonitor.Core;

namespace GcmMonitor.Presentation;

public enum Screen { Home, Monitoring, Results, History, Settings }
public sealed record NavigationItem(Screen Screen, string Title, string Icon)
{
    public override string ToString() => Title;
}
public sealed record DurationOption(int Minutes, string Label)
{
    public override string ToString() => Label;
}
public sealed record SummaryCard(string Icon, string Title, string Value, string Detail)
{
    public bool IsStorage => Title == "Armazenamento";
    public VolumeUsage[] Volumes { get; init; } = [];
}
public sealed record VolumeUsage(string Label, double PercentUsed);

public sealed partial class ModuleSelection(ModuleDefinition definition) : ObservableObject
{
    public ModuleDefinition Definition { get; } = definition;
    [ObservableProperty] private bool isSelected = true;
    [ObservableProperty] private bool isAvailable = true;
    public string Availability => IsAvailable ? "Disponível" : "Indisponível na v0.2 · previsto para v0.3";
    partial void OnIsAvailableChanged(bool value) => OnPropertyChanged(nameof(Availability));
}

public sealed partial class DiskSelection(string id, string label) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    [ObservableProperty] private bool isSelected = true;
}

public sealed record ModuleResult(string Code, string Name, string State, string Summary, string Evidence, string Interpretation, string Recommendation, string Color, string Statistics = "");
public sealed record MetricCard(string Name, string Value, string Unit, string Component, string Source, string[] Labels, double[] Values, bool IsDemo = true)
{
    public string AxisLabel => IsDemo ? "tempo simulado" : "horário local";
}
public sealed record ProcessRow(string Identity, string Values);
public sealed record HistoryRow(DiagnosticSession Session)
{
    public string Date => Session.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string Computer => Session.ComputerName + (Session.IsDemo ? " · demonstração" : "");
    public string Duration => $"{Session.EffectiveDuration.TotalMinutes:0.#} min" + (Session.IsDemo ? " (simulados)" : "");
    public string State => Labels.Session(Session.State);
}

public static class Labels
{
    public static string Session(SessionState state) => state switch
    {
        SessionState.NotStarted => "Não iniciada",
        SessionState.Validating => "Validando",
        SessionState.Running => "Em execução",
        SessionState.Finalizing => "Finalizando",
        SessionState.Completed => "Concluída",
        SessionState.Cancelled => "Cancelada",
        SessionState.Interrupted => "Interrompida",
        SessionState.Error => "Erro",
        _ => "Indisponível"
    };
    public static string Module(ModuleState state) => state switch
    {
        ModuleState.Pending => "Pendente",
        ModuleState.Running => "Executando",
        ModuleState.Completed => "Concluído",
        ModuleState.Partial => "Parcial",
        ModuleState.Error => "Erro",
        ModuleState.Unavailable => "Indisponível",
        ModuleState.Cancelled => "Cancelado",
        _ => "Indisponível"
    };
}

public interface IUserDialogs
{
    bool Confirm(string title, string message);
    string? ChooseReportFolder(string current);
}
