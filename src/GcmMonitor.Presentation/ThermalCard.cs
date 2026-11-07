using GcmMonitor.Core;

namespace GcmMonitor.Presentation;

public sealed record ThermalCard(string Phase, string Sensor, string Baseline, string Load, string Recovery,
    string Cooling, string Limitations, string Notices, string Alert, bool Critical, bool AwaitingChoice,
    double[] Values, string[] Labels, int LoadMarker, int RecoveryMarker)
{
    public static ThermalCard? From(DiagnosticSession? session)
    {
        if (session is not { IsDemo: true, ThermalRehearsal: { } trace }) return null;
        var review = ThermalRecoveryAnalyzer.Analyze(trace, StressPlan.Create(session.Options.MonitoringMinutes, session.Options.StressMinutes));
        var points = ThermalRecoveryAnalyzer.TemperatureSeries(trace);
        var notice = trace.Notices.LastOrDefault();
        return new(trace.Phase switch
        {
            StressPhase.Baseline => "Referência inicial", StressPhase.Active => "Carga fictícia",
            StressPhase.Recovery => "Observação da recuperação", StressPhase.Stopped => "Observação concluída",
            _ => "Observação interrompida"
        }, trace.Sensor + " · " + trace.Source,
            $"Inicial {Number(review.Baseline.First, "°C")} · média {Number(review.Baseline.Average, "°C")}\n{Context(review.Baseline)}",
            $"Média {Number(review.Load.Average, "°C")} · pico {Number(review.Load.Peak, "°C")}\n{Context(review.Load)}",
            $"No fim da carga {Number(review.AtLoadEnd, "°C")} · última {Number(review.Recovery.Last, "°C")} · {review.ObservedRecovery:mm\\:ss}\n{Context(review.Recovery)}",
            $"Queda {Number(review.Drop, "°C")} · taxa {Number(review.CoolingPerMinute, "°C/min")} · recuperação {Number(review.RecoveredPercent, "%")} · diferença da referência {Number(review.DifferenceFromBaseline, "°C")}",
            string.Join(" ", review.Limitations.Append("Critérios de classificação térmica pendentes.")),
            string.Join("\n", trace.Notices.Select(n => $"{n.At:HH:mm:ss} (simulado) · {n.Message}" +
                (n.Choice is { } choice ? $" Escolha {ChoiceLabel(choice)} às {n.ChoiceRecordedAt?.ToLocalTime():HH:mm:ss}." : ""))
                .Append(trace.StopReason).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()),
            notice is null ? "" : notice.Level == ThermalAlertLevel.Critical ? "Crítico · parada obrigatória simulada" : "Atenção · temperatura elevada na simulação",
            notice?.Level == ThermalAlertLevel.Critical,
            trace.Phase == StressPhase.Active && notice is { Level: ThermalAlertLevel.Attention, Choice: null },
            points.Select(p => p.Celsius ?? double.NaN).ToArray(),
            points.Select(p => p.ObservedAt.ToLocalTime().ToString("HH:mm:ss")).ToArray(),
            Array.FindIndex(points, p => p.ObservedAt >= trace.LoadStartedAt),
            Array.FindIndex(points, p => p.ObservedAt >= trace.LoadEndedAt));
    }

    private static string Number(double? value, string unit) => value is { } n ? $"{n:0.#} {unit}" : "—";
    private static string Context(ThermalPhaseStatistics stats) => stats.Samples == 0 ? "Sem amostras válidas" :
        $"CPU {Number(stats.CpuAverage, "%")} · {Number(stats.FrequencyAverage, "MHz")} · {Number(stats.FanAverage, "RPM")} · {stats.Power}";
    private static string ChoiceLabel(ThermalChoice choice) => choice == ThermalChoice.Stop ? "Parar estresse" : "Continuar monitorando";
}
