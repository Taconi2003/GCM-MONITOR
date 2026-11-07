using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GcmMonitor.Collector;

public sealed record PowerReading(byte Ac, byte BatteryFlag, byte Charge, uint LifetimeSeconds, Guid? Scheme);
public sealed record WifiReading(string Key, string Adapter, int State, uint? Signal, uint? ReceiveKbps,
    uint? TransmitKbps, uint? Channel, string? Limitation);
public interface IWindowsPowerWifi
{
    PowerReading ReadPower();
    WifiReading[] ReadWifi();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPowerWifi : IWindowsPowerWifi
{
    public PowerReading ReadPower()
    {
        if (!GetSystemPowerStatus(out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Guid? scheme = null;
        uint result = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (pointer != IntPtr.Zero)
        {
            try { if (result == 0) scheme = Marshal.PtrToStructure<Guid>(pointer); }
            finally { LocalFree(pointer); }
        }
        return new(status.Ac, status.Flag, status.Charge, status.Lifetime, scheme);
    }

    public WifiReading[] ReadWifi()
    {
        uint result = WlanOpenHandle(2, IntPtr.Zero, out _, out var client);
        if (result != 0) throw new Win32Exception((int)result);
        try
        {
            result = WlanEnumInterfaces(client, IntPtr.Zero, out var list);
            if (result != 0) throw new Win32Exception((int)result);
            try
            {
                int count = Marshal.ReadInt32(list);
                if (count is < 0 or > 64) throw new InvalidDataException("Lista WLAN acima do limite.");
                var readings = new List<WifiReading>();
                for (int i = 0; i < count; i++)
                {
                    var entry = IntPtr.Add(list, 8 + i * 532);
                    var id = Marshal.PtrToStructure<Guid>(entry);
                    string name = DiagnosticValue.Clean(Marshal.PtrToStringUni(IntPtr.Add(entry, 16), 256)?.TrimEnd('\0') ?? "Adaptador Wi-Fi");
                    int state = Marshal.ReadInt32(entry, 528);
                    uint? signal = null, rx = null, tx = null, channel = null; string? limitation = null;
                    if (state == 1)
                    {
                        result = WlanQueryInterface(client, ref id, 7, IntPtr.Zero, out var size, out var data, out _);
                        try
                        {
                            if (result == 0 && size >= 604)
                            {
                                // WLAN_CONNECTION_ATTRIBUTES: associação inicia em 520; somente qualidade e taxas.
                                // Não copia SSID, BSSID, perfil nem material de autenticação para objetos gerenciados.
                                signal = unchecked((uint)Marshal.ReadInt32(data, 576));
                                rx = unchecked((uint)Marshal.ReadInt32(data, 580));
                                tx = unchecked((uint)Marshal.ReadInt32(data, 584));
                                if (signal > 100) signal = null;
                            }
                            else limitation = result == 5 ? "Acesso à conexão negado pelo Windows/permissão de localização. Nenhuma permissão foi alterada." : "Detalhes da conexão indisponíveis (código " + result + ").";
                        }
                        finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
                        result = WlanQueryInterface(client, ref id, 8, IntPtr.Zero, out size, out data, out _);
                        try { if (result == 0 && size >= 4) channel = unchecked((uint)Marshal.ReadInt32(data)); }
                        finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
                    }
                    readings.Add(new(id.ToString("N"), name, state, signal, rx, tx, channel, limitation));
                }
                return readings.ToArray();
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(client, IntPtr.Zero); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus { public byte Ac, Flag, Charge, Saver; public uint Lifetime, FullLifetime; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr client);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr client, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr client, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr client, ref Guid id, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int type);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr pointer);
}
