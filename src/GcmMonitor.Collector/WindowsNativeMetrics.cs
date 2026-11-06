using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GcmMonitor.Collector;

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
public readonly record struct MemoryStatus(ulong TotalPhysical, ulong AvailablePhysical, ulong CommitTotal, ulong CommitLimit, ulong CommitPeak, ulong SystemCache);

public static class CpuUsage
{
    public static double? Calculate(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User) return null;
        double total = current.Kernel - previous.Kernel + (double)(current.User - previous.User);
        double idle = current.Idle - previous.Idle;
        return total > 0 && idle <= total ? (total - idle) / total * 100 : null;
    }
}

[SupportedOSPlatform("windows")]
public static class WindowsNativeMetrics
{
    public static int LogicalProcessors => checked((int)GetActiveProcessorCount(ushort.MaxValue));
    public static CpuTimes ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(idle, kernel, user);
    }
    public static MemoryStatus ReadMemory()
    {
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var performance = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!GetPerformanceInfo(ref performance, performance.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        ulong page = performance.PageSize.ToUInt64();
        return new(memory.TotalPhysical, memory.AvailablePhysical,
            performance.CommitTotal.ToUInt64() * page, performance.CommitLimit.ToUInt64() * page,
            performance.CommitPeak.ToUInt64() * page, performance.SystemCache.ToUInt64() * page);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInformation information, uint size);
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort groupNumber);
}
