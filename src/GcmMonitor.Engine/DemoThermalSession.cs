using GcmMonitor.Core;

namespace GcmMonitor.Engine;

// Relógio sintético, sem chamada a hardware, supervisor ativo ou gerador de carga.
internal sealed class DemoThermalSession
{
    private const string Sensor = "CPU Package fictício";
    private const string Source = "Simulação térmica GCM";
    private TaskCompletionSource<ThermalChoice>? attention;
    private int active;
    private int stopRequested;

    public void Resolve(ThermalChoice choice)
    {
        if (Volatile.Read(ref attention)?.TrySetResult(choice) == true) return;
        if (choice == ThermalChoice.Stop && Volatile.Read(ref active) != 0) Volatile.Write(ref stopRequested, 1);
    }

    public async Task RunAsync(Guid id, DiagnosticOptions options, Func<DiagnosticSession, Task> publish,
        CancellationToken ct, Func<bool> disconnected)
    {
        var plan = StressPlan.Create(options.MonitoringMinutes, options.StressMinutes);
        var started = DateTimeOffset.UtcNow;
        var points = new List<ThermalPoint>();
        var notices = new List<ThermalNotice>();
        var alert = new ThermalAlertRehearsal(82, 93);
        var observed = started;
        DateTimeOffset? loadStart = null, loadEnd = null;
        string? reason = null;
        var session = new DiagnosticSession
        {
            Id = id, Options = options, StartedAt = started, Stress = StressReleasePolicy.Evaluate(false, started),
            IntegrityNote = "Simulação térmica com relógio acelerado. Sem carga física."
        }.TransitionTo(SessionState.Validating, plan.RequiresExtension
            ? "Simulação térmica e extensão da janela fictícia autorizadas."
            : "Simulação térmica autorizada.", started);
        await publish(session);
        session = session.TransitionTo(SessionState.Running, "Registrando fases com amostras fictícias.", started);

        Task PublishAsync(StressPhase phase) => publish(session with
        {
            EffectiveDuration = observed - started,
            Modules = options.Modules.Select(module => new ModuleRun(module,
                session.State == SessionState.Completed ? ModuleState.Completed : session.IsTerminal ? ModuleState.Partial : ModuleState.Running,
                Source, "Observação sintética.", points.Count)).ToArray(),
            Measurements = points.TakeLast(32).SelectMany(p => new[]
            {
                new Measurement(ModuleId.Sensors, "Temperatura CPU", Sensor, p.Celsius, "°C", p.ObservedAt,
                    Source, DataQuality.Valid, true),
                new Measurement(ModuleId.Cpu, "CPU", Sensor, p.CpuPercent, "%", p.ObservedAt, Source, DataQuality.Valid, true)
            }).ToArray(),
            ThermalRehearsal = new(phase, Sensor, Source, started, loadStart, loadEnd, observed, true, true,
                points.ToArray(), notices.ToArray(), reason)
        });

        ThermalPoint AddPoint(double temperature, double usage, double frequency, double fan)
        {
            var point = new ThermalPoint(observed, observed, temperature, usage, frequency, fan, true);
            points.Add(point);
            return point;
        }

        try
        {
            for (int seconds = 0; seconds < plan.Before.TotalSeconds; seconds += 5)
            {
                observed = started.AddSeconds(seconds);
                AddPoint(47 + seconds % 3, 9, 1300, 1500);
                await PublishAsync(StressPhase.Baseline);
                await Task.Delay(140, ct);
            }

            loadStart = started + plan.Before;
            Volatile.Write(ref active, 1);
            double[] temperatures = [66, 76, 84, 87, 91, 94];
            for (int i = 0; i < temperatures.Length; i++)
            {
                observed = loadStart.Value.AddSeconds(i * 5);
                var notice = alert.Observe(AddPoint(temperatures[i], 91, 3100, 2600 + i * 100));
                if (notice is not null) notices.Add(notice);
                if (!alert.MustStop && Volatile.Read(ref stopRequested) != 0)
                {
                    reason = "Parada solicitada pelo técnico na simulação.";
                    break;
                }
                if (notice is { Level: ThermalAlertLevel.Attention })
                {
                    var reply = new TaskCompletionSource<ThermalChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Volatile.Write(ref attention, reply);
                    try
                    {
                        await PublishAsync(StressPhase.Active);
                        ThermalChoice choice;
                        try { choice = await reply.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
                        catch (TimeoutException)
                        {
                            reason = "Prazo de resposta da simulação expirado. Parada aplicada por segurança.";
                            break;
                        }
                        notices[^1] = alert.Resolve(choice, DateTimeOffset.UtcNow)!;
                        if (choice == ThermalChoice.Stop) reason = "Parada solicitada pelo técnico na simulação.";
                    }
                    finally { Volatile.Write(ref attention, null); }
                }
                if (alert.MustStop)
                {
                    reason ??= notice?.Message;
                    break;
                }
                await PublishAsync(StressPhase.Active);
                await Task.Delay(140, ct);
            }

            Volatile.Write(ref active, 0);
            loadEnd = observed.AddSeconds(5);
            double endTemperature = points[^1].Celsius!.Value;
            for (int seconds = 0; seconds <= plan.After.TotalSeconds; seconds += 5)
            {
                observed = loadEnd.Value.AddSeconds(seconds);
                AddPoint(49 + (endTemperature - 49) * Math.Exp(-seconds / 23.0), 11, 1500, 2100);
                await PublishAsync(StressPhase.Recovery);
                await Task.Delay(140, ct);
            }

            session = session.TransitionTo(SessionState.Finalizing, "Consolidando a recuperação térmica simulada.", observed)
                .TransitionTo(SessionState.Completed, "Simulação térmica concluída.", observed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            reason ??= "Observação simulada encerrada antes da conclusão.";
            session = session.TransitionTo(disconnected() ? SessionState.Interrupted : SessionState.Cancelled, reason, observed);
        }
        finally { Volatile.Write(ref active, 0); }
        await PublishAsync(session.State == SessionState.Completed ? StressPhase.Stopped : StressPhase.Cancelled);
    }
}
