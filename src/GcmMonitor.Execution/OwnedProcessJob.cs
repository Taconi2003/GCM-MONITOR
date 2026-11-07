using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GcmMonitor.Execution;

/// <summary>Um único filho próprio, associado atomicamente ao Job na criação, antes de executar código.</summary>
[SupportedOSPlatform("windows")]
public sealed class OwnedProcessJob : IDisposable
{
    private readonly SafeJobHandle job;
    private readonly SafeProcessHandle process;
    private readonly SafeKernelHandle primaryThread;
    private bool resumed, disposed;
    public int ProcessId { get; }
    public DateTimeOffset StartedAt { get; }
    public bool HasExited => WaitForSingleObject(process, 0) switch
    { 0 => true, 258 => false, _ => throw new Win32Exception(Marshal.GetLastWin32Error()) };

    private OwnedProcessJob(SafeJobHandle job, ProcessInformation created, DateTimeOffset startedAt)
    {
        ProcessId = checked((int)created.ProcessId);
        this.job = job;
        process = new(created.Process, true); primaryThread = new(created.Thread);
        StartedAt = startedAt;
    }

    public static OwnedProcessJob CreateSuspended(string executable, IReadOnlyList<string> arguments)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("Executável próprio não encontrado.", executable);
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        IntPtr attributes = IntPtr.Zero, jobList = IntPtr.Zero;
        bool initialized = false;
        ProcessInformation created = default;
        try
        {
            var limits = new ExtendedLimits { Basic = new() { Flags = 0x2000 | 0x8, ActiveProcesses = 1 } };
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) throw new Win32Exception(Marshal.GetLastWin32Error());
            nuint size = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            if (size is 0 or > 65536) throw new Win32Exception(Marshal.GetLastWin32Error());
            attributes = Marshal.AllocHGlobal(checked((int)size));
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            jobList = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(jobList, job.DangerousGetHandle());
            // PROC_THREAD_ATTRIBUTE_JOB_LIST elimina a janela CreateProcess → AssignProcessToJobObject.
            if (!UpdateProcThreadAttribute(attributes, 0, (nuint)0x0002000D, jobList, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx { Info = new() { Size = (uint)Marshal.SizeOf<StartupInfoEx>(), Flags = 1, ShowWindow = 0 }, Attributes = attributes };
            var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(QuoteArgument)));
            if (command.Length >= 32767) throw new ArgumentException("Linha de execução própria acima do limite.");
            if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08080004, IntPtr.Zero,
                Path.GetDirectoryName(executable)!, ref startup, out created)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!IsProcessInJob(created.Process, job, out bool assigned) || !assigned)
                throw new IOException("Não foi possível comprovar a contenção do processo próprio.");
            if (!GetProcessTimes(created.Process, out var creation, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var owned = new OwnedProcessJob(job, created, DateTimeOffset.FromFileTime(creation));
            created = default; return owned;
        }
        catch
        {
            // O Job existe antes do filho e já o contém desde a criação; fechar encerra até um filho suspenso.
            job.Dispose();
            if (created.Process != IntPtr.Zero) { WaitForSingleObjectRaw(created.Process, 5000); CloseHandle(created.Process); }
            if (created.Thread != IntPtr.Zero) CloseHandle(created.Thread);
            throw;
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (jobList != IntPtr.Zero) Marshal.FreeHGlobal(jobList);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
        }
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (resumed) throw new InvalidOperationException("O processo próprio já foi iniciado.");
        if (ResumeThread(primaryThread) != 1) throw new Win32Exception(Marshal.GetLastWin32Error());
        resumed = true; primaryThread.Dispose();
    }

    public Task StopAsync() => Task.Run(() =>
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!HasExited && !TerminateJobObject(job, 0xE001)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (WaitForSingleObject(process, 5000) != 0) throw new IOException("Encerramento do processo próprio não confirmado em cinco segundos.");
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        job.Dispose(); // KILL_ON_JOB_CLOSE; handle não herdável e sem nome público.
        try { if (WaitForSingleObject(process, 5000) != 0) throw new IOException("Fechamento do Job não confirmou encerramento do processo próprio."); }
        finally { primaryThread.Dispose(); process.Dispose(); }
    }

    public static string QuoteArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0')) throw new ArgumentException("Argumento contém caractere nulo.");
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char item in value)
        {
            if (item == '\\') { slashes++; continue; }
            result.Append('\\', item == '"' ? slashes * 2 + 1 : slashes); result.Append(item); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    { protected override bool ReleaseHandle() => CloseHandle(handle); }
    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    { public SafeKernelHandle(IntPtr value) : base(true) => SetHandle(value); protected override bool ReleaseHandle() => CloseHandle(handle); }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessTime, JobTime; public uint Flags; public nuint MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public uint Size; public string? Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XChars, YChars, Fill, Flags; public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeJobHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeJobHandle job, int infoClass, ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessInJob(IntPtr process, SafeJobHandle job, out bool result);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeKernelHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)] private static extern uint WaitForSingleObjectRaw(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
