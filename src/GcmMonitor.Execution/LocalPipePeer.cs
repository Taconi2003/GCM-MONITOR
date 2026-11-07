using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace GcmMonitor.Execution;

[SupportedOSPlatform("windows")]
public static class LocalPipePeer
{
    public static void ValidateServer(SafePipeHandle pipe, int expectedPid, DateTimeOffset expectedStart)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var pid) || pid != expectedPid) throw new InvalidDataException("Servidor do canal não corresponde ao controlador esperado.");
        ValidateProcess(expectedPid, expectedStart);
    }
    public static void ValidateClient(SafePipeHandle pipe, int expectedPid, DateTimeOffset expectedStart)
    {
        if (!GetNamedPipeClientProcessId(pipe, out var pid) || pid != expectedPid) throw new InvalidDataException("Cliente do canal não corresponde ao processo próprio esperado.");
        ValidateProcess(expectedPid, expectedStart);
    }
    private static void ValidateProcess(int pid, DateTimeOffset expectedStart)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != expectedStart.UtcDateTime)
                throw new InvalidDataException("Identidade do processo local não confirmada.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        { throw new InvalidDataException("Identidade do processo local não verificável.", ex); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
}
