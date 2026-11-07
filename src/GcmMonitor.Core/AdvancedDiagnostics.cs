namespace GcmMonitor.Core;

/// <summary>Evidência textual limitada, sem mensagens integrais de eventos ou dados pessoais de rede.</summary>
public sealed record DiagnosticRecord(ModuleId Module, string Key, string Title, string Detail,
    DateTimeOffset Timestamp, string Source, DataQuality Quality, bool IsDemo = false)
{
    public FindingSeverity? Severity { get; init; }
    public void Validate()
    {
        if (!Enum.IsDefined(Module) || !Enum.IsDefined(Quality) || string.IsNullOrWhiteSpace(Key) ||
            Key.Length > 256 || string.IsNullOrWhiteSpace(Source) || Source.Length > 256 ||
            Title.Length > 256 || Detail.Length > 2048)
            throw new ArgumentException("Registro de diagnóstico inválido ou acima do limite.");
    }
}

public sealed record ModuleAssessment(ModuleId Module, ModuleState State, string Source, string Summary);
public sealed record GpuInfo(string Name, string DriverVersion, string Limitation);

public static class DiagnosticMath
{
    public static double? BatteryWear(double? design, double? full) =>
        design is > 0 && full is > 0 && double.IsFinite(design.Value) && double.IsFinite(full.Value) && full <= design
            ? (1 - full / design) * 100 : null;
    public static double? EstimatedBatteryMinutes(double? remainingMwh, double? dischargeMw) =>
        remainingMwh is >= 0 && remainingMwh < uint.MaxValue && dischargeMw is > 0 &&
        double.IsFinite(remainingMwh.Value) && double.IsFinite(dischargeMw.Value) ? remainingMwh / dischargeMw * 60 : null;
}
