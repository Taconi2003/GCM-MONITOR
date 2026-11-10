using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GcmMonitor.Core;
using GcmMonitor.Execution;

namespace GcmMonitor.StressSupervisor;

/// <summary>Observação local temporária de suspensão e alimentação, somente leitura.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSafetyObservation : IAsyncDisposable
{
    private const uint DeviceNotifyCallback = 2;
    private const uint PowerSettingChanged = 0x8013;
    private static readonly Guid AcDcPowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private static readonly ConcurrentDictionary<nint, WindowsSafetyObservation> Registrations = new();
    private static readonly NotificationCallback Callback = OnNotification;
    private static long nextCookie;

    private readonly CancellationTokenSource shutdown = new();
    private readonly TaskCompletionSource<WindowsSafetySignal> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource initialPower = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new();
    private readonly byte baseline;
    private readonly nint cookie;
    private readonly nint suspendHandle;
    private readonly nint powerHandle;
    private readonly Task polling;
    private OwnedProcessJob? worker;
    public Task<WindowsSafetySignal> Signal => signal.Task;
    public Task InitialPower => initialPower.Task;
    public WindowsSafetySignal? CurrentSignal
    {
        get
        {
            lock (gate) return signal.Task.IsCompletedSuccessfully ? signal.Task.Result : null;
        }
    }

    public WindowsSafetyObservation()
    {
        baseline = ReadAc() ?? throw new InvalidOperationException("Alimentação inicial indisponível.");
        cookie = (nint)Interlocked.Increment(ref nextCookie);
        Registrations[cookie] = this;
        var parameters = new SubscribeParameters { Callback = Marshal.GetFunctionPointerForDelegate(Callback), Context = cookie };
        try
        {
            uint error = PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, ref parameters, out suspendHandle);
            if (error != 0) throw new Win32Exception((int)error, "Notificações de suspensão indisponíveis.");
            error = PowerSettingRegisterNotification(in AcDcPowerSource, DeviceNotifyCallback, ref parameters, out powerHandle);
            if (error != 0) throw new Win32Exception((int)error, "Notificações de alimentação indisponíveis.");
        }
        catch
        {
            ReleaseRegistrations();
            throw;
        }
        polling = PollAcAsync(shutdown.Token);
    }

    private async Task PollAcAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            do
            {
                byte? ac = ReadAc();
                if (!ac.HasValue) ObserveSignal(SafetyStopReason.EnvironmentUnavailable, false);
                else if (ac.Value != baseline) ObserveSignal(SafetyStopReason.PowerChanged, false);
            } while (!signal.Task.IsCompleted && await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static byte? ReadAc() => GetSystemPowerStatus(out var status) && status.Ac <= 1 ? status.Ac : null;

    public void ResumeUnlessSignaled(OwnedProcessJob candidate)
    {
        lock (gate)
        {
            if (signal.Task.IsCompleted) return;
            worker = candidate;
            candidate.Resume();
        }
    }

    public void DetachWorker()
    {
        lock (gate) worker = null;
    }

    private void ObserveSignal(SafetyStopReason reason, bool callback, WindowsSuspensionNotification? suspension = null)
    {
        var timing = new WindowsStopTiming(DateTimeOffset.UtcNow);
        lock (gate)
        {
            if (signal.Task.IsCompleted) return;
            if (worker is not null)
            {
                // O primeiro sinal ambiental já pede a parada, mesmo quando antecede um aviso de suspensão.
                timing = timing with { DirectStopRequestedAtUtc = DateTimeOffset.UtcNow };
                try
                {
                    timing = timing with { DirectStopAccepted = worker.RequestStop() };
                }
                catch (Win32Exception)
                {
                    // A rotina principal repete a solicitação e o fechamento do Job continua sendo a última proteção.
                    timing = timing with { DirectStopAccepted = false };
                }
            }
            signal.TrySetResult(new(reason, callback, suspension, timing));
        }
    }

    private static uint OnNotification(nint context, uint type, nint setting)
    {
        const uint suspend = 0x0004, resumeSuspend = 0x0007, resumeAutomatic = 0x0012;
        if (!Registrations.TryGetValue(context, out var observer)) return 0;
        if (type is suspend or resumeSuspend or resumeAutomatic)
        {
            var notification = type == suspend ? WindowsSuspensionNotification.Entering : WindowsSuspensionNotification.Resuming;
            observer.ObserveSignal(SafetyStopReason.Suspended, true, notification);
        }
        if (type == PowerSettingChanged) observer.ObservePower(setting);
        return 0;
    }

    private void ObservePower(nint setting)
    {
        if (setting == 0 || Marshal.PtrToStructure<Guid>(setting) != AcDcPowerSource || Marshal.ReadInt32(setting, 16) != sizeof(uint))
        {
            ObserveSignal(SafetyStopReason.EnvironmentUnavailable, true);
            return;
        }
        // SYSTEM_POWER_CONDITION usa a ordem inversa de SYSTEM_POWER_STATUS para AC/bateria.
        switch (Marshal.ReadInt32(setting, 20))
        {
            case 0 when baseline == 1:
            case 1 when baseline == 0:
                initialPower.TrySetResult();
                break;
            case 0 or 1 or 2:
                ObserveSignal(SafetyStopReason.PowerChanged, true);
                break;
            default:
                ObserveSignal(SafetyStopReason.EnvironmentUnavailable, true);
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();
        try { await polling; }
        finally { ReleaseRegistrations(); }
    }

    private void ReleaseRegistrations()
    {
        Registrations.TryRemove(cookie, out _);
        uint powerError = powerHandle == 0 ? 0 : PowerSettingUnregisterNotification(powerHandle);
        if (powerError != 0)
            Console.Error.WriteLine("Falha ao remover observação temporária de alimentação: " + powerError);
        uint suspendError = suspendHandle == 0 ? 0 : PowerUnregisterSuspendResumeNotification(suspendHandle);
        if (suspendError != 0)
            Console.Error.WriteLine("Falha ao remover observação temporária de suspensão: " + suspendError);
        shutdown.Dispose();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint NotificationCallback(nint context, uint type, nint setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct SubscribeParameters { public nint Callback, Context; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus { public byte Ac, BatteryFlag, BatteryPercent, Saver; public uint Lifetime, FullLifetime; }

    [DllImport("powrprof.dll")]
    private static extern uint PowerRegisterSuspendResumeNotification(uint flags, ref SubscribeParameters recipient, out nint registrationHandle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerUnregisterSuspendResumeNotification(nint registrationHandle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingRegisterNotification(in Guid setting, uint flags, ref SubscribeParameters recipient, out nint registrationHandle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingUnregisterNotification(nint registrationHandle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}

internal readonly record struct WindowsSafetySignal(SafetyStopReason Reason, bool Callback,
    WindowsSuspensionNotification? Suspension, WindowsStopTiming Timing);
