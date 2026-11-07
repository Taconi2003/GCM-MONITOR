using System.IO.Pipes;
using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Persistence;
using GcmMonitor.Engine;
using GcmMonitor.Execution;

// Motor local: modo passivo real por padrão. Demonstração reservada aos testes, explicitamente identificada.
if (!OperatingSystem.IsWindows()) return 2;
if (args.Length != 6 || args[0] != "--pipe" || args[2] != "--session" || args[4] != "--database" ||
    !Guid.TryParse(args[3], out var sessionId) || !(args[1].StartsWith("gcm-session-", StringComparison.Ordinal) || args[1].StartsWith("gcm-demo-", StringComparison.Ordinal)))
{
    Console.Error.WriteLine("Motor de sessão: inicialização restrita ao controlador GCM Monitor v0.4.");
    return 2;
}

var repository = new SqliteSessionRepository(args[5]);
DiagnosticSession? session = null;
using var lifetime = new CancellationTokenSource(TimeSpan.FromHours(4.1));
using var cancel = new CancellationTokenSource();
using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
Task? commandLoop = null;
bool disconnected = false;
try
{
    await pipe.ConnectAsync(8000, lifetime.Token);
    var channel = new EngineChannel(pipe);
    var start = await channel.ReadAsync<StartSessionMessage>(lifetime.Token);
    EngineProtocol.Validate(start.Version, start.SessionId, sessionId);
    if (start.Options is null) throw new InvalidDataException("Opções ausentes.");
    if (!Enum.IsDefined(start.Mode)) throw new InvalidDataException("Modo de coleta inválido.");
    start.Options.Validate();
    if (start.UseCpuThermalBridge && (start.Mode != CollectionMode.Passive || !start.Options.Modules.Contains(ModuleId.Sensors)))
        throw new InvalidDataException("Auxiliar térmico aceito somente para coleta passiva de sensores selecionados.");
    await repository.InitializeAsync(lifetime.Token);
    long sequence = 0;
    async Task SendAsync()
    {
        await repository.SaveAsync(session!, lifetime.Token);
        await channel.WriteAsync(new EngineMessage(EngineProtocol.Version, sessionId, ++sequence, session!), lifetime.Token);
    }

    commandLoop = Task.Run(async () =>
    {
        try
        {
            var command = await channel.ReadAsync<EngineCommand>(lifetime.Token);
            EngineProtocol.Validate(command.Version, command.SessionId, sessionId);
            if (command.Command != "cancel") throw new InvalidDataException("Comando não permitido.");
            cancel.Cancel();
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
        { if (!lifetime.IsCancellationRequested) disconnected = true; cancel.Cancel(); }
    });

    if (start.Mode == CollectionMode.Passive)
    {
        string dotnetPath = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        await using var thermal = start.UseCpuThermalBridge ? new CpuThermalBridge(sessionId,
            Path.Combine(AppContext.BaseDirectory, "thermal", "GcmMonitor.SensorProbe.dll"), dotnetPath) : null;
        await using var source = new WindowsPassiveSource(thermal);
        using var runLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token, lifetime.Token);
        session = await new PassiveSessionRunner(repository, source).RunAsync(sessionId, start.Options, async snapshot =>
        {
            session = snapshot;
            await channel.WriteAsync(new EngineMessage(EngineProtocol.Version, sessionId, ++sequence, snapshot), lifetime.Token);
        }, runLifetime.Token, () => disconnected);
        return session.State == SessionState.Error ? 1 : 0;
    }

    session = new DiagnosticSession { Id = sessionId, Options = start.Options };
    session = session.TransitionTo(SessionState.Validating, "Validando opções demonstrativas; estresse bloqueado.", DateTimeOffset.UtcNow);
    await SendAsync();

    var collectors = start.Options.Modules.Select(x => new DemoCollector(x)).ToArray();
    var runs = new List<ModuleRun>();
    foreach (var collector in collectors) runs.Add(await collector.CheckCapabilityAsync(lifetime.Token));
    session = session.TransitionTo(SessionState.Running, "Reprodução de dados sintéticos; relógio acelerado.", DateTimeOffset.UtcNow) with { Modules = runs.ToArray() };
    await SendAsync();
    bool continuous = ModuleCatalog.All.Any(x => start.Options.Modules.Contains(x.Id) && x.Continuous &&
        runs.Any(r => r.Id == x.Id && r.State != ModuleState.Unavailable));
    int steps = continuous ? 20 : 1;
    for (int i = 1; i <= steps && !cancel.IsCancellationRequested; i++)
    {
        var samples = session.Measurements.ToList();
        var elapsed = continuous ? TimeSpan.FromMinutes(start.Options.MonitoringMinutes * (double)i / steps) : TimeSpan.Zero;
        foreach (var collector in collectors)
        {
            var collected = await collector.CollectAsync(session.StartedAt + elapsed, i, lifetime.Token);
            samples.AddRange(collected.Where(x => x.Module != ModuleId.Storage || start.Options.DiskIds.Contains(x.Component)));
        }
        session = session with
        {
            EffectiveDuration = elapsed,
            Measurements = samples.ToArray(),
            Modules = runs.Select(x => x.State == ModuleState.Unavailable ? x : x with
            {
                State = continuous && ModuleCatalog.All.Single(m => m.Id == x.Id).Continuous ? ModuleState.Running : ModuleState.Completed,
                ReadingCount = samples.Count(m => m.Module == x.Id),
                Summary = DemoFixtures.SummaryFor(x.Id)
            }).ToArray()
        };
        await SendAsync();
        if (continuous)
        {
            try { await Task.Delay(500, cancel.Token); } catch (OperationCanceledException) { break; }
        }
    }

    if (cancel.IsCancellationRequested)
        session = session.TransitionTo(SessionState.Cancelled, "Demonstração cancelada pelo controlador ou canal fechado.", DateTimeOffset.UtcNow);
    else
    {
        session = session.TransitionTo(SessionState.Finalizing, "Consolidando exemplos ilustrativos.", DateTimeOffset.UtcNow);
        await SendAsync();
        await Task.Delay(200, lifetime.Token);
        session = session.TransitionTo(SessionState.Completed, "Reprodução demonstrativa concluída; não é diagnóstico real.", DateTimeOffset.UtcNow);
    }
    session = session with
    {
        Modules = session.Modules.Select(x => x.State == ModuleState.Running ? x with
        { State = session.State == SessionState.Completed ? ModuleState.Completed : ModuleState.Partial } :
        x.State == ModuleState.Pending ? x with { State = ModuleState.Cancelled } : x).ToArray(),
        Findings = DemoFixtures.FindingsFor(start.Options.Modules)
    };
    await SendAsync();
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or System.Text.Json.JsonException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
{
    if (session is { IsTerminal: false })
    {
        try
        {
            session = session.TransitionTo(SessionState.Interrupted, "Demonstração interrompida: falha de canal, persistência ou tempo-limite.", DateTimeOffset.UtcNow);
            await repository.SaveAsync(session);
        }
        catch (Exception saveError) when (saveError is IOException or Microsoft.Data.Sqlite.SqliteException) { /* Falha de gravação: processo encerra. */ }
    }
    Console.Error.WriteLine("Motor de sessão interrompido; nenhuma carga de estresse foi executada.");
    return 1;
}
finally
{
    lifetime.Cancel();
    pipe.Dispose();
    if (commandLoop is not null) await commandLoop;
}
