using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using GcmMonitor.Core;

namespace GcmMonitor.Execution;

/// <summary>Receptor sem elevação. Cria somente o auxiliar configurado pelo próprio motor.</summary>
[SupportedOSPlatform("windows")]
public sealed class CpuThermalBridge(Guid sessionId, string helperPath, string dotnetPath) : ICpuThermalFeed
{
    private readonly CpuThermalBuffer buffer = new(sessionId);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private NamedPipeServerStream? pipe;
    private EngineChannel? channel;
    private Process? ownedHelper;
    private Task? pump;
    private bool disposed;

    public async Task<CpuThermalSnapshot> ReadAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        pump ??= RunAsync(); // O motor serializa amostras; uma tentativa por sessão, sem novos prompts após falha.
        await ready.Task.WaitAsync(ct);
        return buffer.Read(clock.Elapsed);
    }

    private async Task RunAsync()
    {
        Task? heartbeat = null;
        try
        {
            if (!File.Exists(helperPath) || !File.Exists(dotnetPath)) throw new FileNotFoundException();
            string pipeName = "gcm-thermal-" + Guid.NewGuid().ToString("N");
            pipe = ThermalPipe.CreateServer(pipeName);
            using var parent = Process.GetCurrentProcess();
            var arguments = new[] { helperPath, "--bridge", pipeName, sessionId.ToString(), nonce,
                parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            var launch = new ProcessStartInfo(dotnetPath)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(helperPath)!,
                Arguments = string.Join(" ", arguments.Select(OwnedProcessJob.QuoteArgument))
            };
            ownedHelper = await Task.Run(() => Process.Start(launch) ?? throw new IOException("Auxiliar não iniciado."));
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, lifetime.Token))
            {
                await pipe.WaitForConnectionAsync(linked.Token);
                LocalPipePeer.ValidateClient(pipe.SafePipeHandle, ownedHelper.Id, new(ownedHelper.StartTime.ToUniversalTime(), TimeSpan.Zero));
                channel = new EngineChannel(pipe, CpuThermalProtocol.MaximumBytes, CpuThermalProtocol.Json);
                var hello = await channel.ReadAsync<CpuThermalHello>(linked.Token);
                CpuThermalProtocol.ValidateIdentity(hello.Version, hello.SessionId, sessionId, hello.Nonce, nonce);
                if (!hello.Elevated || hello.LibraryVersion != "0.9.6.0" || hello.DriverVersion != "2.2.0.0")
                    throw new InvalidDataException("Privilégio ou versão da fonte térmica não revisados.");
                await channel.WriteAsync(new CpuThermalControl(CpuThermalProtocol.Version, sessionId, nonce, 1, "start"), linked.Token);
            }
            heartbeat = HeartbeatAsync();
            while (!lifetime.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(CpuThermalProtocol.HeartbeatTimeout);
                var frame = await channel.ReadAsync<CpuThermalFrame>(timeout.Token);
                buffer.Accept(frame, clock.Elapsed, DateTimeOffset.UtcNow);
                ready.TrySetResult();
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or Win32Exception or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            buffer.Fail(ex is Win32Exception { NativeErrorCode: 1223 }
                ? "Permissão de administrador recusada; coleta direta não iniciada." :
                "Canal térmico encerrado ou inválido (" + ex.GetType().Name + "). Não foi reutilizada a leitura anterior.");
        }
        finally
        {
            buffer.Fail("Canal térmico encerrado; sem leitura atual disponível.");
            lifetime.Cancel(); pipe?.Dispose(); ready.TrySetResult();
            if (heartbeat is not null)
                try { await heartbeat; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            long sequence = 1;
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await channel!.WriteAsync(new CpuThermalControl(CpuThermalProtocol.Version, sessionId, nonce, ++sequence, "heartbeat"), timeout.Token);
            }
        }
        finally { lifetime.Cancel(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; buffer.Fail("Coleta térmica encerrada.");
        lifetime.Cancel(); pipe?.Dispose();
        // Se o UAC ainda estiver aberto, o callback encerra o canal após a resposta do usuário.
        if (pump is not null) await pump;
        if (ownedHelper is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try { await ownedHelper.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { throw new IOException("Saída do auxiliar térmico não confirmada no prazo."); }
            finally { ownedHelper.Dispose(); }
        }
        lifetime.Dispose();
    }
}
