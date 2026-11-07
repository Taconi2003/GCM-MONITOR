using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace GcmMonitor.Collector;

public interface IWindowsDiagnosticReader
{
    IReadOnlyList<Dictionary<string, object?>> Query(string scope, string query, CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDiagnosticReader : IWindowsDiagnosticReader
{
    public IReadOnlyList<Dictionary<string, object?>> Query(string scope, string query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), Rewindable = false, ReturnImmediately = false });
        using var objects = searcher.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementBaseObject item in objects)
        {
            using (item)
            {
                ct.ThrowIfCancellationRequested();
                var row = item.Properties.Cast<PropertyData>().ToDictionary(x => x.Name, x => (object?)x.Value, StringComparer.OrdinalIgnoreCase);
                row["__RELPATH"] = item.SystemProperties["__RELPATH"]?.Value;
                rows.Add(row);
            }
            // O último registro permite sinalizar truncamento sem enumerar indefinidamente o provedor.
            if (rows.Count == 4097) break;
        }
        return rows;
    }
}

public static class DiagnosticValue
{
    public static string Identity(IReadOnlyDictionary<string, object?> row, string name) => row.GetValueOrDefault(name)?.ToString() ?? "";
    public static string Text(IReadOnlyDictionary<string, object?> row, string name) =>
        Clean(row.GetValueOrDefault(name)?.ToString() ?? "Não disponível", 180);
    public static string Clean(string value, int maximum = 180) =>
        new(value.Where(c => !char.IsControl(c)).Take(maximum).ToArray());
    public static double? Number(IReadOnlyDictionary<string, object?> row, string name)
    {
        try
        {
            var raw = row.GetValueOrDefault(name);
            if (raw is null) return null;
            double number = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return double.IsFinite(number) ? number : null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }
}
