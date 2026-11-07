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

/// <summary>Coordena consultas pontuais e cadências passivas; nunca gera carga.</summary>
public sealed class PassiveSessionRunner(ISessionRepository repository, IPassiveCollectionSource source, ISessionClock? clock = null)
{
    private readonly ISessionClock clock = clock ?? new SessionClock();

    public async Task<DiagnosticSession> RunAsync(Guid id, DiagnosticOptions options, Func<DiagnosticSession, Task> publish,
        CancellationToken cancellationToken, Func<bool>? disconnected = null)
    {
        options.Validate();
        var start = clock.Elapsed;
        using var own = Process.GetCurrentProcess();
        var session = new DiagnosticSession
        {
            Id = id, Options = options, IsDemo = false, ComputerName = "Identificando computador", StartedAt = clock.UtcNow,
            IntegrityNote = "Coleta passiva. Estresse desativado. Ausência de achados não comprova integridade física.",
            EngineProcessId = own.Id, EngineProcessStartedAt = new DateTimeOffset(own.StartTime.ToUniversalTime(), TimeSpan.Zero),
            Modules = options.Modules.Select(x => new ModuleRun(x, ModuleState.Pending, "Não executado", "Aguardando validação.")).ToArray()
        }.TransitionTo(SessionState.Validating, "Validando seleção, inventário e acesso ao banco.", clock.UtcNow);
        var accumulator = new MetricAccumulator();
        var window = new List<Measurement>();
        var degraded = new HashSet<ModuleId>();
        var previous = new Dictionary<ModuleId, TimeSpan>();
        async Task SaveAndPublish(CollectionBatch? batch = null)
        {
            session = session with { EffectiveDuration = clock.Elapsed - start };
            await repository.SaveBatchAsync(session, batch ?? new([], [], []));
            await publish(session);
        }
        async Task Collect(ModuleId module)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool gap = previous.TryGetValue(module, out var lastSample) && clock.Elapsed - lastSample > TimeSpan.FromSeconds(15);
            var batch = new CollectionBatch([], [], []);
            try
            {
                batch = await source.SampleAsync(options with { Modules = [module] }, clock.UtcNow,
                    module == ModuleId.Processes, gap, cancellationToken);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or NotSupportedException)
            {
                batch = batch with { Issues = [new(clock.UtcNow, module, "Falha isolada de coleta: " + ex.GetType().Name + ". Demais módulos independentes preservados.")] };
                session = session with { Modules = session.Modules.Select(x => x.Id == module ? x with { State = ModuleState.Error, Summary = "Falha do coletor; não comprova defeito do computador." } : x).ToArray() };
                degraded.Add(module);
            }
            if (gap)
            {
                accumulator.ResetBaselines();
                batch = batch with { Issues = [.. batch.Issues, new(clock.UtcNow, null, "Lacuna de amostragem superior a 15 s, possível suspensão/atraso; médias não interpoladas nesse intervalo.")] };
            }
            foreach (var reading in batch.Measurements) accumulator.Add(reading);
            window.AddRange(batch.Measurements);
            window = window.GroupBy(x => (x.Module, x.Metric, x.Component)).SelectMany(x => x.TakeLast(32)).OrderBy(x => x.Timestamp).TakeLast(512).ToList();
            var records = session.Records.Concat(batch.Records).GroupBy(x => (x.Module, x.Key)).Select(x => x.Last())
                .GroupBy(x => x.Module).SelectMany(x => x.OrderByDescending(r => r.Severity == FindingSeverity.Attention).ThenByDescending(r => r.Timestamp).Take(64)).Take(192).ToArray();
            foreach (var assessment in batch.Assessments.Where(x => x.State is ModuleState.Partial or ModuleState.Unavailable or ModuleState.Error)) degraded.Add(assessment.Module);
            foreach (var item in batch.Measurements.Where(x => x.Quality is DataQuality.Unavailable or DataQuality.Error)) degraded.Add(item.Module);
            session = session with
            {
                Measurements = window.ToArray(), Records = records, Statistics = accumulator.Snapshot(),
                Issues = session.Issues.Concat(batch.Issues).TakeLast(64).ToArray(),
                Modules = session.Modules.Select(run =>
                {
                    var assessment = batch.Assessments.LastOrDefault(x => x.Module == run.Id);
                    int readings = batch.Measurements.Count(x => x.Module == run.Id && x.Quality == DataQuality.Valid) +
                        batch.Records.Count(x => x.Module == run.Id && x.Quality == DataQuality.Valid) +
                        (run.Id == ModuleId.Processes ? batch.Processes.Count(x => !x.Ended) : 0);
                    return run with
                    {
                        ReadingCount = run.ReadingCount + readings,
                        State = assessment is null ? run.State : assessment.State == ModuleState.Unavailable && readings > 0 ? ModuleState.Partial : assessment.State,
                        Source = assessment?.Source ?? run.Source, Summary = assessment?.Summary ?? run.Summary
                    };
                }).ToArray()
            };
            // Confirma dados antes de publicar rankings. Só snapshots limitados atravessam o canal.
            await repository.SaveBatchAsync(session with { EffectiveDuration = clock.Elapsed - start }, batch);
            if (batch.Processes.Length > 0) session = session with { Processes = await repository.ReadProcessRankingsAsync(id) };
            await SaveAndPublish();
            previous[module] = clock.Elapsed;
        }
        await SaveAndPublish();
        try
        {
            var inventory = await source.ReadInventoryAsync(cancellationToken);
            if (options.Modules.Contains(ModuleId.Storage) && options.DiskIds.Any(x => !inventory.Disks.Any(d => d.Id == x)))
                throw new ArgumentException("O disco selecionado não está disponível no inventário atual. Revise a seleção no Início.");
            session = session with
            {
                Inventory = inventory, ComputerName = inventory.ComputerName,
                Modules = options.Modules.Select(x => new ModuleRun(x, ModuleState.Running, "APIs Windows de leitura / v0.3", "Consulta passiva em andamento.", StartedAt: clock.UtcNow)).ToArray()
            };
            cancellationToken.ThrowIfCancellationRequested();
            session = session.TransitionTo(SessionState.Running, "Consultas pontuais e monitoramento passivo dos módulos selecionados. Sem estresse.", clock.UtcNow);
            await SaveAndPublish();
            foreach (var module in options.Modules) await Collect(module);
            // Ausências verificadas na primeira consulta não impõem espera artificial quando não resta fonte contínua.
            var continuous = session.Modules.Where(x => ModuleCatalog.All.Single(m => m.Id == x.Id).Continuous &&
                (x.State != ModuleState.Unavailable || x.ReadingCount > 0 || x.Id == ModuleId.Storage && inventory.Disks.Length > 0)).Select(x => x.Id).ToArray();
            TimeSpan nextFast = clock.Elapsed + TimeSpan.FromSeconds(2), nextSlow = clock.Elapsed + TimeSpan.FromSeconds(5);
            bool hasFast = continuous.Any(x => x is ModuleId.Cpu or ModuleId.Memory or ModuleId.Storage);
            bool hasSlow = continuous.Any(x => x is not (ModuleId.Cpu or ModuleId.Memory or ModuleId.Storage));
            var untilEnd = start + TimeSpan.FromMinutes(options.MonitoringMinutes);
            while (continuous.Length > 0 && clock.Elapsed < untilEnd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = hasFast && hasSlow ? (nextFast < nextSlow ? nextFast : nextSlow) : hasFast ? nextFast : nextSlow;
                if (next > untilEnd) next = untilEnd;
                var delay = next - clock.Elapsed;
                if (delay > TimeSpan.Zero) await clock.DelayAsync(delay, cancellationToken);
                if (clock.Elapsed >= untilEnd) break;
                bool fastDue = hasFast && clock.Elapsed >= nextFast, slowDue = hasSlow && clock.Elapsed >= nextSlow;
                foreach (var module in continuous.Where(x => x is ModuleId.Cpu or ModuleId.Memory or ModuleId.Storage ? fastDue : slowDue)) await Collect(module);
                if (fastDue) nextFast = clock.Elapsed + TimeSpan.FromSeconds(2);
                if (slowDue) nextSlow = clock.Elapsed + TimeSpan.FromSeconds(5);
            }
            session = session.TransitionTo(SessionState.Finalizing, "Consolidando estatísticas e limitações das fontes.", clock.UtcNow);
            await SaveAndPublish();
            cancellationToken.ThrowIfCancellationRequested();
            session = session.TransitionTo(SessionState.Completed, "Coleta passiva finalizada; consulte dados e limitações por módulo.", clock.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bool interrupted = disconnected?.Invoke() == true;
            session = session.TransitionTo(interrupted ? SessionState.Interrupted : SessionState.Cancelled,
                interrupted ? "Comunicação interrompida; dados confirmados preservados." : "Cancelamento passivo solicitado; dados parciais preservados.", clock.UtcNow);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.Data.Common.DbException or UnauthorizedAccessException)
        {
            session = session.TransitionTo(SessionState.Error, "Falha de validação, persistência ou coleta: " + ex.GetType().Name, clock.UtcNow) with
            { Issues = [.. session.Issues.TakeLast(63), new(clock.UtcNow, null, "A sessão não pôde continuar. Verifique banco, seleção e limitações das fontes.")] };
        }
        session = FinalizeSession(session, degraded, clock.UtcNow);
        await SaveAndPublish();
        return session;
    }

    private static DiagnosticSession FinalizeSession(DiagnosticSession session, HashSet<ModuleId> degraded, DateTimeOffset timestamp)
    {
        var modules = session.Modules.Select(x =>
        {
            bool valid = x.ReadingCount > 0 || x.Id == ModuleId.Storage && session.Inventory?.Disks.Length > 0;
            bool failed = degraded.Contains(x.Id) || x.State == ModuleState.Error || session.Issues.Any(i => i.Module == x.Id);
            bool punctualCompleted = !ModuleCatalog.All.Single(m => m.Id == x.Id).Continuous && x.State == ModuleState.Completed && !failed;
            var state = valid ? punctualCompleted || session.State == SessionState.Completed && !failed ? ModuleState.Completed : ModuleState.Partial :
                x.State == ModuleState.Error ? ModuleState.Error : x.State == ModuleState.Unavailable || session.State == SessionState.Completed ? ModuleState.Unavailable : ModuleState.Cancelled;
            string summary = x.Id switch
            {
                ModuleId.Cpu => "CPU por deltas de GetSystemTimes; tempo ocupado normalizado, pode diferir da utilidade mostrada no Gerenciador de Tarefas.",
                ModuleId.Memory => "RAM física e comprometimento são conceitos distintos. Cache não é diagnóstico de defeito; valores em GiB.",
                ModuleId.Storage => "Tráfego em MB/s por disco físico; indicadores de confiabilidade quando expostos. SMART bruto e integridade física não determinados.",
                ModuleId.Processes => "CPU acumulada na sessão; RAM working set com média ponderada. Processos encerrados preservados; processos restritos podem faltar.",
                _ => x.Summary
            };
            return x with { State = state, Summary = summary, EndedAt = timestamp };
        }).ToArray();
        var findings = modules.Where(x => x.State is ModuleState.Unavailable or ModuleState.Error or ModuleState.Partial)
            .Select(x => new Finding(x.Id, FindingSeverity.Unavailable, x.State == ModuleState.Unavailable ? "Dados indisponíveis." : "Dados parciais ou limitação da coleta.",
                $"{x.Source}; {x.ReadingCount} leituras; {timestamp:O}", "Ausência de leitura ou falha de coleta não demonstra defeito físico.",
                "Revisar limitações e permissões; validar com fontes equivalentes antes de recomendar intervenção.", true))
            .Concat(session.Records.Where(x => x.Severity == FindingSeverity.Attention).Take(64).Select(x => new Finding(x.Module, FindingSeverity.Attention,
                x.Title, $"{x.Source}; {x.Timestamp:O}; {x.Detail}", "Ocorrência observada; a causa não foi determinada apenas por este registro.",
                "Correlacionar com sintomas, período e outras evidências antes de decidir uma intervenção."))).ToArray();
        return session with { Modules = modules, Findings = findings };
    }
}
