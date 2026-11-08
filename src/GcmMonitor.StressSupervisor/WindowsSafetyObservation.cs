using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GcmMonitor.Core;

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
    private readonly TaskCompletionSource<(SafetyStopReason Reason, bool Callback)> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource initialPower = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte baseline;
    private readonly nint cookie;
    private readonly nint suspendHandle;
    private readonly nint powerHandle;
    private readonly Task polling;
    public Task<(SafetyStopReason Reason, bool Callback)> Signal => signal.Task;
    public Task InitialPower => initialPower.Task;

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
                if (!ac.HasValue) signal.TrySetResult((SafetyStopReason.EnvironmentUnavailable, false));
                else if (ac.Value != baseline) signal.TrySetResult((SafetyStopReason.PowerChanged, false));
            } while (!signal.Task.IsCompleted && await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static byte? ReadAc() => GetSystemPowerStatus(out var status) && status.Ac <= 1 ? status.Ac : null;

    private static uint OnNotification(nint context, uint type, nint setting)
    {
        const uint suspend = 0x0004, resumeSuspend = 0x0007, resumeAutomatic = 0x0012;
        if (!Registrations.TryGetValue(context, out var observer)) return 0;
        if (type is suspend or resumeSuspend or resumeAutomatic) observer.signal.TrySetResult((SafetyStopReason.Suspended, true));
        if (type == PowerSettingChanged) observer.ObservePower(setting);
        return 0;
    }

    private void ObservePower(nint setting)
    {
        if (setting == 0 || Marshal.PtrToStructure<Guid>(setting) != AcDcPowerSource || Marshal.ReadInt32(setting, 16) != sizeof(uint))
        {
            signal.TrySetResult((SafetyStopReason.EnvironmentUnavailable, true));
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
                signal.TrySetResult((SafetyStopReason.PowerChanged, true));
                break;
            default:
                signal.TrySetResult((SafetyStopReason.EnvironmentUnavailable, true));
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
