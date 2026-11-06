using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GcmMonitor.Collector;

/// <summary>PDH com caminhos ingleses independentes do idioma instalado. Apenas leitura.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPerformanceQuery : IDisposable
{
    private IntPtr query;
    private readonly Dictionary<string, IntPtr> counters = [];
    public WindowsPerformanceQuery(params string[] paths)
    {
        Check(PdhOpenQuery(null, UIntPtr.Zero, out query));
        try
        {
            foreach (var path in paths)
            { Check(PdhAddEnglishCounter(query, path, UIntPtr.Zero, out var counter)); counters.Add(path, counter); }
        }
        catch { Dispose(); throw; }
    }
    public Dictionary<string, Dictionary<string, double>> Sample()
    {
        Check(PdhCollectQueryData(query));
        var result = new Dictionary<string, Dictionary<string, double>>();
        foreach (var (path, counter) in counters)
        {
            if (!path.Contains('*'))
            {
                uint singleStatus = PdhGetFormattedCounterValue(counter, 0x200, out _, out var value);
                result[path] = singleStatus == 0 && value.Status is 0 or 1 && double.IsFinite(value.DoubleValue) && value.DoubleValue >= 0
                    ? new() { ["_Total"] = value.DoubleValue } : [];
                continue;
            }
            uint bytes = 0, count = 0;
            uint status = PdhGetFormattedCounterArray(counter, 0x200, ref bytes, ref count, IntPtr.Zero);
            if (status != 0x800007D2 || bytes is 0 or > 1024 * 1024) { result[path] = []; continue; }
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                status = PdhGetFormattedCounterArray(counter, 0x200, ref bytes, ref count, buffer);
                var values = new Dictionary<string, double>();
                if (status == 0)
                {
                    int size = Marshal.SizeOf<FormattedCounterItem>();
                    if ((ulong)count * (ulong)size > bytes) throw new InvalidDataException("Buffer PDH inconsistente.");
                    for (int i = 0; i < count; i++)
                    {
                        var item = Marshal.PtrToStructure<FormattedCounterItem>(buffer + i * size);
                        string? name = Marshal.PtrToStringUni(item.Name);
                        if (name is not null && item.Value.Status is 0 or 1 && double.IsFinite(item.Value.DoubleValue) && item.Value.DoubleValue >= 0)
                            values[name] = item.Value.DoubleValue;
                    }
                }
                result[path] = values;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return result;
    }
    private static void Check(uint status) { if (status != 0) throw new Win32Exception(unchecked((int)status), "Contador de desempenho indisponível (PDH)."); }
    public void Dispose() { if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FormattedCounterValue
    { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double DoubleValue; }
    [StructLayout(LayoutKind.Sequential)] private struct FormattedCounterItem { public IntPtr Name; public FormattedCounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")] private static extern uint PdhOpenQuery(string? source, UIntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out FormattedCounterValue value);
}
