using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using GcmMonitor.Core;
using GcmMonitor.Execution;
using GcmMonitor.StressSupervisor;

if (!OperatingSystem.IsWindows()) return 2;
// Filho de prova deliberadamente ocioso; não contém gerador de carga ou acesso a sensores/drivers.
if (args is ["--idle-probe"])
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}
if (args.Length != 9 || args[0] != "--safety-probe" || args[1] != "--pipe" || args[3] != "--session" ||
    args[5] != "--controller" || args[7] != "--controller-start" ||
    !args[2].StartsWith("gcm-safety-probe-", StringComparison.Ordinal) || !Guid.TryParseExact(args[2][17..], "N", out _) ||
    !Guid.TryParse(args[4], out var sessionId) || sessionId == Guid.Empty ||
    !int.TryParse(args[6], NumberStyles.None, CultureInfo.InvariantCulture, out int controllerPid) || controllerPid <= 0 ||
    !long.TryParse(args[8], NumberStyles.None, CultureInfo.InvariantCulture, out long controllerStart))
{
    Console.Error.WriteLine(string.Join(" ", StressReleasePolicy.Evaluate(true, DateTimeOffset.UtcNow).Reasons));
    return 3;
}

using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(25));
using var pipe = new NamedPipeClientStream(".", args[2], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
OwnedProcessJob? worker = null;
WindowsSafetyObservation? environment = null;
bool windowsSignalObserved = false;
SafetyStopReason? reason = null;
long sequence = 0;
EngineChannel? channel = null;
Task? runningNotice = null;
double? workerExitMilliseconds = null;
try
{
    await pipe.ConnectAsync(5000, lifetime.Token);
    LocalPipePeer.ValidateServer(pipe.SafePipeHandle, controllerPid, DateTimeOffset.FromFileTime(controllerStart));
    channel = new(pipe, SafetyProbeProtocol.MaximumBytes, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
    SafetyProbeStart start;
    using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
    {
        handshake.CancelAfter(TimeSpan.FromSeconds(3));
        start = await channel.ReadAsync<SafetyProbeStart>(handshake.Token);
    }
    SafetyProbeProtocol.Validate(start.Version, start.SessionId, sessionId, start.Nonce, start.Nonce);
    if (start.Purpose != SafetyProbeProtocol.Purpose || start.DurationMilliseconds is < 100 or > 15000 || !start.Nonce.All(Uri.IsHexDigit))
        throw new InvalidDataException("Prova aceita somente contenção ociosa, com duração interna limitada.");
    var thermal = start.InitialThermalFixture is { } initial ? new IdleThermalWatch(sessionId, initial) : null;
    if (start.ObserveWindows)
    {
        try { environment = new WindowsSafetyObservation(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
        { reason = SafetyStopReason.EnvironmentUnavailable; throw new InvalidOperationException("Observação do Windows indisponível.", ex); }
    }
    SafetyStopReason? WindowsSignal()
    {
        if (environment?.Signal.IsCompletedSuccessfully != true) return null;
        windowsSignalObserved = true;
        return environment.Signal.Result;
    }
    reason = WindowsSignal();
    if (reason is null)
    {
        worker = OwnedProcessJob.CreateSuspended(Environment.ProcessPath!, ["--idle-probe"]);
        reason = WindowsSignal();
        if (reason is null) worker.Resume();
    }
    var elapsed = Stopwatch.StartNew();
    var lastHeartbeat = elapsed.Elapsed;
    var duration = TimeSpan.FromMilliseconds(start.DurationMilliseconds);
    (TimeSpan Due, SafetyStopReason Reason) NextDeadline()
    {
        var heartbeatDue = lastHeartbeat + SafetyProbeProtocol.HeartbeatTimeout;
        var deadline = duration <= heartbeatDue ? (duration, SafetyStopReason.DurationCompleted) : (heartbeatDue, SafetyStopReason.HeartbeatLost);
        return thermal is not null && thermal.Deadline <= deadline.Item1 ? (thermal.Deadline, SafetyStopReason.SensorLost) : deadline;
    }
    async Task Send(string state, SafetyStopReason? stopped = null)
    {
        using var writeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await channel.WriteAsync(new SafetyProbeEvent(SafetyProbeProtocol.Version, sessionId, ++sequence, state,
            worker?.ProcessId, worker?.StartedAt, stopped, WindowsObservationEnabled: environment is not null,
            WindowsSignalObserved: windowsSignalObserved, WorkerExitMilliseconds: workerExitMilliseconds), writeTimeout.Token);
    }
    // A espera de leitura pelo controlador não pode bloquear os prazos do filho.
    if (reason is null) runningNotice = Send("IdleProbeRunning");
    long lastCommand = 0;
    while (reason is null)
    {
        if ((reason = WindowsSignal()) is not null) break;
        if (worker!.HasExited) { reason = SafetyStopReason.WorkerExited; break; }
        var deadline = NextDeadline();
        var remaining = deadline.Due - elapsed.Elapsed;
        if (remaining <= TimeSpan.Zero) { reason = deadline.Reason; break; }
        using var receive = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        receive.CancelAfter(remaining);
        try
        {
            var pending = channel.ReadAsync<SafetyProbeCommand>(receive.Token);
            if (environment is not null && await Task.WhenAny(pending, environment.Signal) == environment.Signal)
            {
                reason = WindowsSignal();
                await receive.CancelAsync();
                try { await pending; } catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException) { }
                break;
            }
            var command = await pending;
            if ((reason = WindowsSignal()) is not null) break;
            // A renovação só vale se a mensagem inteira chegou antes do prazo.
            if (elapsed.Elapsed >= deadline.Due) { reason = deadline.Reason; break; }
            SafetyProbeProtocol.Validate(command.Version, command.SessionId, sessionId, command.Nonce, start.Nonce);
            if (command.Sequence != lastCommand + 1) throw new InvalidDataException("Sequência de comando inválida.");
            lastCommand = command.Sequence;
            if (command.Command == "thermal-fixture")
            {
                if (thermal is null || command.ThermalFixture is null) throw new InvalidDataException("Fixture térmica não negociada.");
                if (!thermal.Observe(command.ThermalFixture, elapsed.Elapsed)) reason = SafetyStopReason.SensorLost;
                continue;
            }
            if (command.ThermalFixture is not null) throw new InvalidDataException("Payload térmico fora da operação permitida.");
            reason = command.Command switch
            {
                "heartbeat" => null,
                "stop" => SafetyStopReason.UserStopped,
                "cancel" => SafetyStopReason.Cancelled,
                "sensor-lost" => SafetyStopReason.SensorLost,
                "suspend" => SafetyStopReason.Suspended,
                "power-changed" => SafetyStopReason.PowerChanged,
                "persistence-failed" => SafetyStopReason.PersistenceFailed,
                _ => throw new InvalidDataException("Comando não permitido na prova de contenção.")
            };
            if (command.Command == "heartbeat") lastHeartbeat = elapsed.Elapsed;
        }
        catch (OperationCanceledException) when (receive.IsCancellationRequested)
        {
            reason = WindowsSignal() ?? (worker.HasExited ? SafetyStopReason.WorkerExited :
                lifetime.IsCancellationRequested ? SafetyStopReason.SupervisorFailed :
                deadline.Reason);
        }
    }
    if (worker is not null)
    {
        await worker.StopAsync();
        workerExitMilliseconds = elapsed.Elapsed.TotalMilliseconds;
    }
    if (runningNotice is not null) await runningNotice;
    await Send("IdleProbeStopped", reason);
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or OperationCanceledException or
    System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
{
    reason ??= ex is InvalidDataException or JsonException ? SafetyStopReason.InvalidProtocol :
        ex is IOException ? SafetyStopReason.ControllerDisconnected : SafetyStopReason.SupervisorFailed;
    if (worker is not null) await worker.StopAsync();
    if (runningNotice is not null)
    {
        try { await runningNotice; }
        catch (Exception writeError) when (writeError is IOException or OperationCanceledException or ObjectDisposedException)
        { channel = null; }
    }
    if (channel is not null && pipe.IsConnected)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await channel.WriteAsync(new SafetyProbeEvent(SafetyProbeProtocol.Version, sessionId, ++sequence,
                "IdleProbeStopped", worker?.ProcessId, worker?.StartedAt, reason, WindowsObservationEnabled: environment is not null,
                WindowsSignalObserved: windowsSignalObserved), timeout.Token);
        }
        catch (Exception writeError) when (writeError is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    Console.Error.WriteLine("Prova ociosa encerrada: " + reason + ". Nenhuma carga de CPU executada.");
    return 2;
}
finally
{
    try { worker?.Dispose(); }
    finally { if (environment is not null) await environment.DisposeAsync(); }
}
