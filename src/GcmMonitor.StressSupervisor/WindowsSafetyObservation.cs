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
    private static readonly ConcurrentDictionary<nint, TaskCompletionSource<SafetyStopReason>> Registrations = new();
    private static readonly NotificationCallback Callback = OnNotification;
    private static long nextCookie;

    private readonly CancellationTokenSource shutdown = new();
    private readonly TaskCompletionSource<SafetyStopReason> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly nint cookie;
    private readonly nint handle;
    private readonly Task polling;
    public Task<SafetyStopReason> Signal => signal.Task;

    public WindowsSafetyObservation()
    {
        byte baseline = ReadAc() ?? throw new InvalidOperationException("Alimentação inicial indisponível.");
        cookie = (nint)Interlocked.Increment(ref nextCookie);
        Registrations[cookie] = signal;
        var parameters = new SubscribeParameters { Callback = Marshal.GetFunctionPointerForDelegate(Callback), Context = cookie };
        try
        {
            uint error = PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, ref parameters, out handle);
            if (error != 0) throw new Win32Exception((int)error, "Notificações de suspensão indisponíveis.");
        }
        catch
        {
            Registrations.TryRemove(cookie, out _);
            shutdown.Dispose();
            throw;
        }
        polling = PollAcAsync(baseline, shutdown.Token);
    }

    private async Task PollAcAsync(byte baseline, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            do
            {
                byte? ac = ReadAc();
                if (!ac.HasValue) signal.TrySetResult(SafetyStopReason.EnvironmentUnavailable);
                else if (ac.Value != baseline) signal.TrySetResult(SafetyStopReason.PowerChanged);
            } while (!signal.Task.IsCompleted && await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static byte? ReadAc() => GetSystemPowerStatus(out var status) && status.Ac <= 1 ? status.Ac : null;

    private static uint OnNotification(nint context, uint type, nint setting)
    {
        const uint suspend = 0x0004, resumeSuspend = 0x0007, resumeAutomatic = 0x0012;
        if (type is suspend or resumeSuspend or resumeAutomatic && Registrations.TryGetValue(context, out var pending))
            pending.TrySetResult(SafetyStopReason.Suspended);
        return 0;
    }

    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();
        try { await polling; }
        finally
        {
            Registrations.TryRemove(cookie, out _);
            uint error = PowerUnregisterSuspendResumeNotification(handle);
            if (error != 0) Console.Error.WriteLine("Falha ao remover observação temporária de suspensão: " + error);
            shutdown.Dispose();
        }
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

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
