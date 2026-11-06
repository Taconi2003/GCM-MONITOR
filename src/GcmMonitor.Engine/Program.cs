using System.IO.Pipes;
using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Persistence;

// Executável exclusivo de demonstração. Não recebe comandos de sistema nem carrega coletores reais.
if (args.Length != 6 || args[0] != "--pipe" || args[2] != "--session" || args[4] != "--database" ||
    !Guid.TryParse(args[3], out var sessionId) || !args[1].StartsWith("gcm-demo-", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Motor demonstrativo: inicialização restrita ao controlador GCM Monitor v0.1.");
    return 2;
}

var repository = new SqliteSessionRepository(args[5]);
DiagnosticSession? session = null;
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
using var cancel = new CancellationTokenSource();
using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
Task? commandLoop = null;
try
{
    await pipe.ConnectAsync(8000, lifetime.Token);
    var channel = new EngineChannel(pipe);
    var start = await channel.ReadAsync<StartSessionMessage>(lifetime.Token);
    EngineProtocol.Validate(start.Version, start.SessionId, sessionId);
    if (start.Options is null) throw new InvalidDataException("Opções ausentes.");
    start.Options.Validate();
    await repository.InitializeAsync(lifetime.Token);
    session = new DiagnosticSession { Id = sessionId, Options = start.Options };
    long sequence = 0;
    async Task SendAsync()
    {
        await repository.SaveAsync(session!, lifetime.Token);
        await channel.WriteAsync(new EngineMessage(EngineProtocol.Version, sessionId, ++sequence, session!), lifetime.Token);
    }

    session = session.TransitionTo(SessionState.Validating, "Validando opções demonstrativas; estresse bloqueado.", DateTimeOffset.UtcNow);
    await SendAsync();
    commandLoop = Task.Run(async () =>
    {
        try
        {
            var command = await channel.ReadAsync<EngineCommand>(lifetime.Token);
            EngineProtocol.Validate(command.Version, command.SessionId, sessionId);
            if (command.Command != "cancel") throw new InvalidDataException("Comando não permitido.");
            cancel.Cancel();
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException)
        { cancel.Cancel(); }
    });

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
    Console.Error.WriteLine("Motor demonstrativo interrompido; nenhuma carga de estresse foi executada.");
    return 1;
}
finally
{
    lifetime.Cancel();
    pipe.Dispose();
    if (commandLoop is not null) await commandLoop;
}
