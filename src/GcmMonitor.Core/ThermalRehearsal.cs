namespace GcmMonitor.Core;

public enum ThermalAlertLevel { Attention, Critical }
public enum ThermalChoice { Continue, Stop }

public sealed record ThermalPoint(DateTimeOffset ObservedAt, DateTimeOffset? CapturedAt, double? Celsius,
    double? CpuPercent = null, double? FrequencyMhz = null, double? FanRpm = null, bool? AcPower = null)
{
    public bool HasFreshTemperature => Celsius is { } temperature && double.IsFinite(temperature) &&
        temperature > -273.15 && CapturedAt is { } captured && captured <= ObservedAt &&
        ObservedAt - captured <= CpuThermalProtocol.MaximumAge;
}

public sealed record ThermalNotice(ThermalAlertLevel Level, DateTimeOffset At, string Message,
    ThermalChoice? Choice = null, DateTimeOffset? ChoiceRecordedAt = null);

/// <summary>Os limites de demonstração nunca qualificam proteção para um equipamento real.</summary>
public sealed class ThermalAlertRehearsal(double attentionCelsius, double criticalCelsius)
{
    private readonly double criticalLimit = double.IsFinite(attentionCelsius) && double.IsFinite(criticalCelsius) && criticalCelsius > attentionCelsius
        ? criticalCelsius : throw new ArgumentException("Os limites do cenário sintético são inválidos.");
    private DateTimeOffset? lastCapture;
    private bool inAttention;
    public ThermalNotice? Notice { get; private set; }
    public bool MustStop { get; private set; }

    public ThermalNotice? Observe(ThermalPoint point, bool supervisionValid = true)
    {
        if (MustStop) return null;
        if (!supervisionValid) return Critical(point.ObservedAt, "Supervisão indisponível.");
        if (!point.HasFreshTemperature || point.CapturedAt <= lastCapture)
            return Critical(point.ObservedAt, "Sensor essencial ausente, inválido ou desatualizado.");
        lastCapture = point.CapturedAt;
        if (point.Celsius >= criticalLimit) return Critical(point.ObservedAt, "Condição térmica crítica simulada.");
        if (point.Celsius < attentionCelsius && Notice is not { Level: ThermalAlertLevel.Attention, Choice: null })
            inAttention = false;
        if (point.Celsius < attentionCelsius || inAttention) return null;
        inAttention = true;
        return Notice = new(ThermalAlertLevel.Attention, point.ObservedAt, "Temperatura elevada na simulação.");
    }

    public ThermalNotice? Resolve(ThermalChoice choice, DateTimeOffset recordedAt)
    {
        if (MustStop || !Enum.IsDefined(choice) || Notice is not { Level: ThermalAlertLevel.Attention, Choice: null } notice)
            return null;
        MustStop = choice == ThermalChoice.Stop;
        return Notice = notice with { Choice = choice, ChoiceRecordedAt = recordedAt };
    }

    private ThermalNotice Critical(DateTimeOffset at, string message)
    {
        MustStop = true;
        return Notice = new(ThermalAlertLevel.Critical, at, message);
    }
}

/// <summary>Registro sintético de fases confirmadas pelo executor de demonstração.</summary>
public sealed record ThermalTrace(StressPhase Phase, string Sensor, string Source, DateTimeOffset StartedAt,
    DateTimeOffset? LoadStartedAt, DateTimeOffset? LoadEndedAt, DateTimeOffset ObservedUntil,
    bool ComparableBaseline, bool ComparableRecovery, ThermalPoint[] Points, ThermalNotice[] Notices, string? StopReason = null);

public sealed record ThermalPhaseStatistics(int Samples, double? First, double? Average, double? Peak, double? Last,
    double? CpuAverage, double? FrequencyAverage, double? FanAverage, string Power);

public sealed record ThermalReview(ThermalPhaseStatistics Baseline, ThermalPhaseStatistics Load, ThermalPhaseStatistics Recovery,
    double? AtLoadEnd, double? Drop, double? CoolingPerMinute, double? RecoveredPercent, double? DifferenceFromBaseline,
    TimeSpan ObservedRecovery, string[] Limitations);

public static class ThermalRecoveryAnalyzer
{
    public static ThermalPoint[] TemperatureSeries(ThermalTrace trace)
    {
        var series = trace.Points.Where(p => p.ObservedAt >= trace.StartedAt && p.ObservedAt <= trace.ObservedUntil)
            .OrderBy(p => p.ObservedAt).ToArray();
        DateTimeOffset? previousCapture = null;
        for (int i = 0; i < series.Length; i++)
        {
            var point = series[i];
            var phaseStart = point.ObservedAt >= trace.LoadEndedAt ? trace.LoadEndedAt :
                point.ObservedAt >= trace.LoadStartedAt ? trace.LoadStartedAt : trace.StartedAt;
            if (!point.HasFreshTemperature || point.CapturedAt <= previousCapture || point.CapturedAt < phaseStart)
                series[i] = point with { Celsius = null };
            else
                previousCapture = point.CapturedAt;
        }
        return series;
    }

    public static ThermalReview Analyze(ThermalTrace trace, StressPlan plan)
    {
        plan.Validate();
        var limitations = new List<string>();
        var ordered = TemperatureSeries(trace);
        var valid = ordered.Where(p => p.HasFreshTemperature).ToArray();
        if (ordered.Length != valid.Length) limitations.Add("Há lacunas, leituras inválidas, capturas repetidas ou de outra fase.");
        var baseline = valid.Where(p => trace.LoadStartedAt is null || p.ObservedAt < trace.LoadStartedAt).ToArray();
        var load = valid.Where(p => p.ObservedAt >= trace.LoadStartedAt &&
            (trace.LoadEndedAt is null || p.ObservedAt < trace.LoadEndedAt)).ToArray();
        var recovery = valid.Where(p => p.ObservedAt >= trace.LoadEndedAt).ToArray();
        var beforeStats = Summarize(baseline);
        var loadStats = Summarize(load);
        var afterStats = Summarize(recovery);
        var observation = trace.LoadEndedAt is { } ended && trace.ObservedUntil >= ended ? trace.ObservedUntil - ended : TimeSpan.Zero;
        bool validBaseline = trace.ComparableBaseline && trace.LoadStartedAt - trace.StartedAt >= plan.Before &&
            baseline.Length >= 2 && Covers(baseline, trace.StartedAt, trace.LoadStartedAt!.Value);
        bool validRecovery = trace.ComparableRecovery && observation >= plan.After &&
            recovery.Length >= 2 && Covers(recovery, trace.LoadEndedAt!.Value, trace.ObservedUntil);
        if (!validBaseline) limitations.Add("Referência inicial insuficiente ou sem condições comparáveis.");
        if (!validRecovery) limitations.Add("Recuperação incompleta ou sem condições comparáveis.");
        double? atLoadEnd = trace.LoadEndedAt is { } end && recovery.FirstOrDefault() is { } first &&
            first.ObservedAt - end <= CpuThermalProtocol.MaximumAge ? first.Celsius : null;
        if (atLoadEnd is null) limitations.Add("Temperatura no fim da carga simulada indisponível.");
        double? drop = atLoadEnd - afterStats.Last;
        var span = recovery.Length >= 2 ? recovery[^1].ObservedAt - recovery[0].ObservedAt : TimeSpan.Zero;
        double? difference = validBaseline && validRecovery ? afterStats.Last - beforeStats.Average : null;
        double? increase = atLoadEnd - beforeStats.Average;
        double? recovered = validBaseline && validRecovery && increase > 0 ? 100 * drop / increase : null;
        if (validBaseline && validRecovery && recovered is null) limitations.Add("Aumento inicial insuficiente para calcular percentual.");
        return new(beforeStats, loadStats, afterStats, atLoadEnd, drop,
            span > TimeSpan.Zero ? drop / span.TotalMinutes : null, recovered, difference, observation, limitations.ToArray());
    }

    private static bool Covers(ThermalPoint[] points, DateTimeOffset start, DateTimeOffset end) =>
        points[0].ObservedAt - start <= CpuThermalProtocol.MaximumAge &&
        end - points[^1].ObservedAt <= CpuThermalProtocol.MaximumAge &&
        points.Zip(points.Skip(1)).All(pair => pair.Second.ObservedAt - pair.First.ObservedAt <= CpuThermalProtocol.MaximumAge);

    private static ThermalPhaseStatistics Summarize(ThermalPoint[] points)
    {
        static double? Average(IEnumerable<double?> source) => source.Where(v => v is { } n && double.IsFinite(n)).Average();
        var power = points.Select(p => p.AcPower).OfType<bool>().Distinct().ToArray();
        return new(points.Length, points.FirstOrDefault()?.Celsius, Average(points.Select(p => p.Celsius)),
            points.Select(p => p.Celsius).Max(), points.LastOrDefault()?.Celsius,
            Average(points.Select(p => p.CpuPercent)), Average(points.Select(p => p.FrequencyMhz)),
            Average(points.Select(p => p.FanRpm)), power.Length == 0 ? "Indisponível" :
                power.Length > 1 ? "Variou" : power[0] ? "Tomada" : "Bateria");
    }
}
