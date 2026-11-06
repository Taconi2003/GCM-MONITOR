using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using GcmMonitor.Core;

namespace GcmMonitor.App;

/// <summary>Canal local restrito ao usuário; um processo próprio por sessão passiva.</summary>
public sealed class SessionEngineClient(string enginePath, string databasePath, CollectionMode mode = CollectionMode.Passive) : IDiagnosticEngineClient
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
        if (running) throw new InvalidOperationException("Já existe uma sessão ativa.");
        running = true; cancelRequested = false; cancelSent = 0; sessionId = Guid.NewGuid();
        var pipeName = "gcm-session-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            var info = new ProcessStartInfo(enginePath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(enginePath)! };
            foreach (var argument in new[] { "--pipe", pipeName, "--session", sessionId.ToString(), "--database", databasePath }) info.ArgumentList.Add(argument);
            process = Process.Start(info) ?? throw new IOException("Não foi possível iniciar o motor de sessão.");
            int expectedPid = process.Id;
            using (var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectionTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await server.WaitForConnectionAsync(connectionTimeout.Token);
            }
            if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out uint clientPid) || clientPid != expectedPid)
                throw new InvalidDataException("A identidade do cliente do canal não corresponde ao motor iniciado.");
            channel = new EngineChannel(server);
            await channel.WriteAsync(new StartSessionMessage(EngineProtocol.Version, sessionId, options, mode), cancellationToken);
            if (cancelRequested) await CancelAsync(cancellationToken);
            long lastSequence = 0;
            while (true)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var message = await channel.ReadAsync<EngineMessage>(timeout.Token);
                EngineProtocol.Validate(message.Version, message.SessionId, sessionId);
                if (message.Sequence != lastSequence + 1 || message.Session is null || message.Session.Id != sessionId || message.Session.IsDemo != (mode == CollectionMode.Demonstration) ||
                    message.Session.Options is null || message.Session.Options.StressEnabled || !Enum.IsDefined(message.Session.State))
                    throw new InvalidDataException("Snapshot de sessão inválido.");
                message.Session.Options.Validate();
                foreach (var measurement in message.Session.Measurements)
                {
                    measurement.Validate();
                    if (measurement.IsDemo != message.Session.IsDemo) throw new InvalidDataException("Origem de medição inconsistente.");
                }
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

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
}
