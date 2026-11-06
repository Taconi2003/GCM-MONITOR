using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using GcmMonitor.Core;

namespace GcmMonitor.App;

/// <summary>Canal local restrito ao usuário; um processo próprio por sessão demonstrativa.</summary>
public sealed class DemoEngineClient(string enginePath, string databasePath) : IDiagnosticEngineClient
{
    private Process? process;
    private EngineChannel? channel;
    private Guid sessionId;
    private bool running;
    private bool cancelRequested;
    private int cancelSent;

    public async IAsyncEnumerable<DiagnosticSession> RunAsync(DiagnosticOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (running) throw new InvalidOperationException("Já existe uma sessão demonstrativa ativa.");
        running = true; cancelRequested = false; cancelSent = 0; sessionId = Guid.NewGuid();
        var pipeName = "gcm-demo-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            var info = new ProcessStartInfo(enginePath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(enginePath)! };
            foreach (var argument in new[] { "--pipe", pipeName, "--session", sessionId.ToString(), "--database", databasePath }) info.ArgumentList.Add(argument);
            process = Process.Start(info) ?? throw new IOException("Não foi possível iniciar o motor demonstrativo.");
            using (var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectionTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await server.WaitForConnectionAsync(connectionTimeout.Token);
            }
            channel = new EngineChannel(server);
            await channel.WriteAsync(new StartSessionMessage(EngineProtocol.Version, sessionId, options), cancellationToken);
            if (cancelRequested) await CancelAsync(cancellationToken);
            long lastSequence = 0;
            while (true)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var message = await channel.ReadAsync<EngineMessage>(timeout.Token);
                EngineProtocol.Validate(message.Version, message.SessionId, sessionId);
                if (message.Sequence != lastSequence + 1 || message.Session is null || message.Session.Id != sessionId || !message.Session.IsDemo ||
                    message.Session.Options is null || message.Session.Options.StressEnabled || !Enum.IsDefined(message.Session.State))
                    throw new InvalidDataException("Snapshot de sessão demonstrativa inválido.");
                message.Session.Options.Validate();
                foreach (var measurement in message.Session.Measurements) measurement.Validate();
                lastSequence = message.Sequence;
                yield return message.Session;
                if (message.Session.IsTerminal) break;
            }
        }
        finally
        {
            channel = null;
            server.Dispose();
            await StopOwnedProcessAsync();
            running = false;
        }
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        cancelRequested = true;
        if (channel is null) return;
        // Cancelamento idempotente: o motor aceita uma única solicitação de parada por sessão.
        if (Interlocked.CompareExchange(ref cancelSent, 1, 0) != 0) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await channel.WriteAsync(new EngineCommand(EngineProtocol.Version, sessionId, "cancel"), timeout.Token);
    }

    private async Task StopOwnedProcessAsync()
    {
        var ownedProcess = Interlocked.Exchange(ref process, null);
        if (ownedProcess is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await ownedProcess.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!ownedProcess.HasExited) ownedProcess.Kill(entireProcessTree: true);
                await ownedProcess.WaitForExitAsync();
            }
        }
        finally { ownedProcess.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        try { await CancelAsync(); } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        await StopOwnedProcessAsync();
    }
}
