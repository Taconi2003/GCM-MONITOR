using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GcmMonitor.Core;

namespace GcmMonitor.Collector;

[SupportedOSPlatform("windows")]
public sealed class WindowsInventoryProvider : IInventoryProvider
{
    public Task<ComputerInventory> ReadAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Read(cancellationToken), cancellationToken);

    public static ComputerInventory Read(CancellationToken ct)
    {
        var limitations = new List<string>();
        List<Dictionary<string, object?>> Query(string scope, string query)
        {
            ct.ThrowIfCancellationRequested();
            try { return WmiRead(scope, query); }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or System.Runtime.InteropServices.ExternalException)
            { limitations.Add("Consulta de inventário indisponível: " + query.Split("FROM", StringSplitOptions.None)[^1].Trim()); return []; }
        }
        static string Text(Dictionary<string, object?> row, string key, string fallback = "Não disponível") => row.GetValueOrDefault(key)?.ToString()?.Trim() is { Length: > 0 } value ? value : fallback;
        static long? Number(Dictionary<string, object?> row, string key) => row.GetValueOrDefault(key) is { } value ? Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) : null;
        var os = Query("root\\cimv2", "SELECT Caption, Version, OSArchitecture, LastBootUpTime FROM Win32_OperatingSystem").FirstOrDefault() ?? [];
        var cpus = Query("root\\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
        var memory = Query("root\\cimv2", "SELECT Capacity FROM Win32_PhysicalMemory").Select(x => Number(x, "Capacity")).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        long? totalRam = null;
        try { totalRam = checked((long)WindowsNativeMetrics.ReadMemory().TotalPhysical); }
        catch (System.ComponentModel.Win32Exception) { limitations.Add("Memória física indisponível."); }
        DateTimeOffset? boot = null;
        try { if (os.GetValueOrDefault("LastBootUpTime") is string bootTime) boot = new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(bootTime)); }
        catch (ArgumentException) { limitations.Add("Último boot indisponível."); }
        var drives = Query("root\\cimv2", "SELECT Index, Model, Size, InterfaceType, DeviceID FROM Win32_DiskDrive");
        var partitions = Query("root\\cimv2", "SELECT DiskIndex, DeviceID FROM Win32_DiskPartition");
        var links = Query("root\\cimv2", "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
        var logical = Query("root\\cimv2", "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");
        var storageDisks = Query("root\\Microsoft\\Windows\\Storage", "SELECT Number, Size, BusType FROM MSFT_Disk");
        var physical = Query("root\\Microsoft\\Windows\\Storage", "SELECT DeviceId, FriendlyName, Size, MediaType FROM MSFT_PhysicalDisk");
        var volumes = new List<VolumeInfo>();
        foreach (var volume in logical)
        {
            var name = Text(volume, "DeviceID");
            var diskIds = new HashSet<string>();
            foreach (var link in links)
            {
                string dependent = ReferenceId(Text(link, "Dependent")), antecedent = ReferenceId(Text(link, "Antecedent"));
                if (dependent != name) continue;
                foreach (var partition in partitions.Where(x => Text(x, "DeviceID") == antecedent))
                    if (Number(partition, "DiskIndex") is { } diskNumber) diskIds.Add($"disk:{diskNumber}");
            }
            volumes.Add(new(name, Number(volume, "Size"), Number(volume, "FreeSpace"), diskIds.ToArray()));
        }
        var disks = drives.Where(x => Number(x, "Index") is not null).Select(disk =>
        {
            int index = checked((int)Number(disk, "Index")!.Value); string id = $"disk:{index}";
            var extra = storageDisks.FirstOrDefault(x => Number(x, "Number") == index && Number(x, "Size") == Number(disk, "Size"));
            var mediaMatches = physical.Where(x => Text(x, "DeviceId") == index.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                Number(x, "Size") == Number(disk, "Size") && string.Equals(Text(x, "FriendlyName"), Text(disk, "Model"), StringComparison.OrdinalIgnoreCase)).ToArray();
            string bus = extra is null ? Text(disk, "InterfaceType") : Number(extra, "BusType") switch
            { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 1 => "SCSI", 15 => "Virtual", _ => "Não disponível" };
            string type = mediaMatches.Length != 1 ? "Não disponível" : Number(mediaMatches[0], "MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Não disponível" };
            var mapped = volumes.Where(x => x.DiskIds.Contains(id)).ToArray();
            return new PhysicalDiskInfo(id, index, Text(disk, "Model"), bus, type, Number(disk, "Size"), mapped,
                "Indicadores de confiabilidade dependem da consulta ao módulo de armazenamento. Espaço livre pertence aos volumes, não ao disco físico." +
                (mapped.Any(x => x.DiskIds.Length > 1) ? " Volume associado a mais de um disco; espaço não deve ser somado." : ""));
        }).ToArray();
        limitations.AddRange(volumes.Where(x => x.DiskIds.Length == 0).Select(x => $"Volume {x.Name}: associação física não disponível."));
        var gpus = Query("root\\cimv2", "SELECT Name, DriverVersion FROM Win32_VideoController")
            .Take(16).Select(x => new GpuInfo(Text(x, "Name"), Text(x, "DriverVersion"),
                "Tipo integrada/dedicada e VRAM não determinados por Win32_VideoController. Métricas dependem do módulo de sensores.")).ToArray();
        return new ComputerInventory(Environment.MachineName, Text(os, "Caption"), Text(os, "Version", Environment.OSVersion.VersionString),
            RuntimeInformation.OSArchitecture.ToString(), boot, string.Join(" / ", cpus.Select(x => Text(x, "Name"))),
            cpus.Count > 0 ? checked((int)cpus.Sum(x => Number(x, "NumberOfCores") ?? 0)) : null,
            WindowsNativeMetrics.LogicalProcessors, totalRam, memory, disks, limitations.ToArray(), DateTimeOffset.UtcNow) { Gpus = gpus };
    }

    private static string ReferenceId(string path)
    {
        try { return new ManagementPath(path).RelativePath.Split('=', 2)[1].Trim('"').Replace("\\\\", "\\", StringComparison.Ordinal); }
        catch (Exception ex) when (ex is ManagementException or ArgumentException or IndexOutOfRangeException) { return ""; }
    }
    private static List<Dictionary<string, object?>> WmiRead(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), Rewindable = false, ReturnImmediately = false });
        using var objects = searcher.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementBaseObject item in objects)
        {
            using (item) rows.Add(item.Properties.Cast<PropertyData>().ToDictionary(x => x.Name, x => (object?)x.Value, StringComparer.OrdinalIgnoreCase));
        }
        return rows;
    }
}
