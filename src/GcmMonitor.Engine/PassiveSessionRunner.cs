using System.Diagnostics;
using GcmMonitor.Core;

namespace GcmMonitor.Engine;

public interface ISessionClock
{
    DateTimeOffset UtcNow { get; }
    TimeSpan Elapsed { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
public sealed class SessionClock : ISessionClock
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeSpan Elapsed => stopwatch.Elapsed;
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

/// <summary>Coordenação passiva. Nenhuma dependência de WPF e nenhum mecanismo de geração de carga.</summary>
public sealed class PassiveSessionRunner(ISessionRepository repository, IPassiveCollectionSource source, ISessionClock? clock = null)
{
    private readonly ISessionClock clock = clock ?? new SessionClock();
    private static readonly ModuleId[] Supported = [ModuleId.Cpu, ModuleId.Memory, ModuleId.Storage, ModuleId.Processes];

    public async Task<DiagnosticSession> RunAsync(Guid id, DiagnosticOptions options, Func<DiagnosticSession, Task> publish,
        CancellationToken cancellationToken, Func<bool>? disconnected = null)
    {
        options.Validate();
        var start = clock.Elapsed;
        using var own = Process.GetCurrentProcess();
        var session = new DiagnosticSession
        {
            Id = id,
            Options = options,
            IsDemo = false,
            ComputerName = "Identificando computador",
            StartedAt = clock.UtcNow,
            IntegrityNote = "Coleta passiva. Estresse desativado. Ausência de achados não comprova integridade física.",
            EngineProcessId = own.Id,
            EngineProcessStartedAt = new DateTimeOffset(own.StartTime.ToUniversalTime(), TimeSpan.Zero),
            Modules = options.Modules.Select(x => new ModuleRun(x, ModuleState.Pending, "Não executado", "Aguardando validação.")).ToArray()
        }.TransitionTo(SessionState.Validating, "Validando seleção, inventário e acesso ao banco.", clock.UtcNow);
        var accumulator = new MetricAccumulator();
        var window = new List<Measurement>();
        async Task SaveAndPublish(CollectionBatch? batch = null)
        {
            session = session with { EffectiveDuration = clock.Elapsed - start };
            await repository.SaveBatchAsync(session, batch ?? new([], [], []));
            await publish(session);
        }
        await SaveAndPublish();
        try
        {
            var inventory = await source.ReadInventoryAsync(cancellationToken);
            if (options.Modules.Contains(ModuleId.Storage) && options.DiskIds.Any(x => !inventory.Disks.Any(d => d.Id == x)))
                throw new ArgumentException("O disco selecionado não está disponível no inventário atual. Revise a seleção no Início.");
            session = session with
            {
                Inventory = inventory,
                ComputerName = inventory.ComputerName,
                Modules = options.Modules.Select(x => new ModuleRun(x, Supported.Contains(x) ? ModuleState.Running : ModuleState.Unavailable,
                    Supported.Contains(x) ? "APIs Windows de leitura / v0.2" : "Não executado",
                    Supported.Contains(x) ? "Coleta passiva em andamento." : "Diagnóstico avançado previsto para v0.3; não foi executado.",
                    StartedAt: clock.UtcNow,
                    Limitation: x == ModuleId.Storage ? "SMART, temperatura e desgaste não coletados; integridade não avaliada." : null)).ToArray()
            };
            cancellationToken.ThrowIfCancellationRequested();
            session = session.TransitionTo(SessionState.Running, "Coletando CPU, RAM, discos e processos selecionados. Sem estresse.", clock.UtcNow);
            await SaveAndPublish();
            TimeSpan nextFast = clock.Elapsed, nextProcesses = clock.Elapsed, previous = clock.Elapsed;
            bool hasFast = options.Modules.Any(x => x is ModuleId.Cpu or ModuleId.Memory or ModuleId.Storage);
            bool hasProcesses = options.Modules.Contains(ModuleId.Processes);
            while ((hasFast || hasProcesses) && clock.Elapsed - start < TimeSpan.FromMinutes(options.MonitoringMinutes))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = clock.Elapsed; bool gap = now - previous > TimeSpan.FromSeconds(15);
                bool fastDue = hasFast && now >= nextFast, processDue = hasProcesses && now >= nextProcesses;
                var due = options.Modules.Where(x => Supported.Contains(x) && (x == ModuleId.Processes ? processDue : fastDue)).ToArray();
                if (due.Length > 0)
                {
                    var batch = new CollectionBatch([], [], []);
                    foreach (var module in due)
                    {
                        try
                        {
                            var collected = await source.SampleAsync(options with { Modules = [module] }, clock.UtcNow, module == ModuleId.Processes, gap, cancellationToken);
                            batch = new([.. batch.Measurements, .. collected.Measurements], [.. batch.Processes, .. collected.Processes], [.. batch.Issues, .. collected.Issues]);
                        }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
                        {
                            batch = batch with { Issues = [.. batch.Issues, new(clock.UtcNow, module, "Falha isolada de coleta: " + ex.GetType().Name + ". Os demais módulos continuam quando disponíveis.")] };
                            session = session with { Modules = session.Modules.Select(x => x.Id == module ? x with { State = ModuleState.Error, Summary = "Falha de coletor; não comprova defeito do computador." } : x).ToArray() };
                        }
                    }
                    if (gap)
                    {
                        accumulator.ResetBaselines();
                        batch = batch with { Issues = [.. batch.Issues, new(clock.UtcNow, null, "Lacuna de amostragem superior a 15 s, possível suspensão/atraso; médias não interpoladas nesse intervalo.")] };
                    }
                    foreach (var measurement in batch.Measurements) accumulator.Add(measurement);
                    window.AddRange(batch.Measurements);
                    window = window.GroupBy(x => (x.Module, x.Metric, x.Component)).SelectMany(x => x.TakeLast(32)).OrderBy(x => x.Timestamp).TakeLast(512).ToList();
                    session = session with
                    {
                        Measurements = window.ToArray(),
                        Statistics = accumulator.Snapshot(),
                        Issues = session.Issues.Concat(batch.Issues).TakeLast(64).ToArray(),
                        Modules = session.Modules.Select(x => x with
                        {
                            ReadingCount = x.ReadingCount + batch.Measurements.Count(m => m.Module == x.Id && m.Quality == DataQuality.Valid) + (x.Id == ModuleId.Processes ? batch.Processes.Count(p => !p.Ended) : 0)
                        }).ToArray()
                    };
                    // Confirma amostras e agregados antes de consultar rankings; a UI não depende de amostras apenas em memória.
                    await repository.SaveBatchAsync(session with { EffectiveDuration = clock.Elapsed - start }, batch);
                    if (batch.Processes.Length > 0) session = session with { Processes = await repository.ReadProcessRankingsAsync(id) };
                    await SaveAndPublish();
                    previous = now;
                    if (fastDue) nextFast = clock.Elapsed + TimeSpan.FromSeconds(2);
                    if (processDue) nextProcesses = clock.Elapsed + TimeSpan.FromSeconds(5);
                }
                var next = hasFast && hasProcesses ? (nextFast < nextProcesses ? nextFast : nextProcesses) : hasFast ? nextFast : nextProcesses;
                var untilEnd = start + TimeSpan.FromMinutes(options.MonitoringMinutes);
                if (next > untilEnd) next = untilEnd;
                var delay = next - clock.Elapsed;
                if (delay > TimeSpan.Zero) await clock.DelayAsync(delay, cancellationToken);
            }
            session = session.TransitionTo(SessionState.Finalizing, "Consolidando estatísticas e limitações das fontes.", clock.UtcNow);
            await SaveAndPublish();
            cancellationToken.ThrowIfCancellationRequested();
            session = session.TransitionTo(SessionState.Completed, "Coleta passiva finalizada; verifique limitações e dados indisponíveis.", clock.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bool interrupted = disconnected?.Invoke() == true;
            session = session.TransitionTo(interrupted ? SessionState.Interrupted : SessionState.Cancelled,
                interrupted ? "Comunicação com o controlador interrompida; dados confirmados preservados." : "Cancelamento passivo solicitado; dados parciais preservados.", clock.UtcNow);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.Data.Common.DbException or UnauthorizedAccessException)
        {
            session = session.TransitionTo(SessionState.Error, "Falha de validação, persistência ou coleta: " + ex.GetType().Name, clock.UtcNow) with
            { Issues = [.. session.Issues.TakeLast(63), new(clock.UtcNow, null, "A sessão não pôde continuar. Verifique acesso ao banco, discos selecionados e limitações de coleta.")] };
        }
        session = FinalizeSession(session, clock.UtcNow);
        await SaveAndPublish();
        return session;
    }

    private static DiagnosticSession FinalizeSession(DiagnosticSession session, DateTimeOffset timestamp)
    {
        var modules = session.Modules.Select(x =>
        {
            if (x.State == ModuleState.Unavailable) return x with { EndedAt = timestamp };
            bool valid = x.ReadingCount > 0 || x.Id == ModuleId.Storage && session.Inventory?.Disks.Length > 0;
            bool failed = x.State == ModuleState.Error || session.Issues.Any(i => i.Module == x.Id) ||
                session.Measurements.Any(m => m.Module == x.Id && m.Quality is DataQuality.Error or DataQuality.Unavailable) ||
                x.Id == ModuleId.Storage; // Inventário/tráfego disponíveis; SMART permanece fora desta entrega.
            var state = valid ? session.State == SessionState.Completed && !failed ? ModuleState.Completed : ModuleState.Partial :
                x.State == ModuleState.Error ? ModuleState.Error : session.State != SessionState.Completed ? ModuleState.Cancelled : ModuleState.Unavailable;
            string summary = x.Id switch
            {
                ModuleId.Cpu => "CPU por deltas de GetSystemTimes; tempo ocupado normalizado, pode diferir da utilidade mostrada no Gerenciador de Tarefas.",
                ModuleId.Memory => "RAM física e comprometimento são conceitos distintos. Cache não é diagnóstico de defeito; valores em GiB.",
                ModuleId.Storage => "Inventário e tráfego por disco físico, em MB/s decimais. SMART não consultado; integridade não avaliada.",
                ModuleId.Processes => "CPU acumulada durante a sessão; RAM working set com média ponderada no tempo. Processos encerrados preservados; processos restritos podem faltar.",
                _ => x.Summary
            };
            return x with { State = state, Summary = summary, EndedAt = timestamp };
        }).ToArray();
        var findings = modules.Where(x => x.State is ModuleState.Unavailable or ModuleState.Error or ModuleState.Partial)
            .Select(x => new Finding(x.Id, FindingSeverity.Unavailable, x.State == ModuleState.Unavailable ? "Dados indisponíveis ou módulo ainda não implementado." : "Dados parciais ou falha do coletor.",
                $"{x.Source}; {x.ReadingCount} leituras; {timestamp:O}", "Ausência de leitura ou falha de coleta não demonstra defeito físico.",
                "Revisar limitações e permissões; validar com fontes equivalentes antes de recomendar intervenção.", true)).ToArray();
        return session with { Modules = modules, Findings = findings };
    }
}
