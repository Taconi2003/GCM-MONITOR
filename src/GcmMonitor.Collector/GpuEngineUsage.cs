using System.Text.RegularExpressions;

namespace GcmMonitor.Collector;

public sealed record GpuEngineReading(string Component, double? Percent);
public static partial class GpuEngineUsage
{
    [GeneratedRegex(@"^pid_\d+_luid_(0x[0-9a-f]+)_(0x[0-9a-f]+)_phys_(\d+)_eng_(\d+)_engtype_(.+)$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex InstancePattern();

    public static GpuEngineReading[] Aggregate(IReadOnlyDictionary<string, double> values) => values
        .Select(x => (Match: InstancePattern().Match(x.Key), x.Value)).Where(x => x.Match.Success)
        .GroupBy(x => $"LUID {x.Match.Groups[1].Value}:{x.Match.Groups[2].Value} · físico {x.Match.Groups[3].Value} · engine {x.Match.Groups[4].Value} {x.Match.Groups[5].Value}")
        .Take(64).Select(group =>
        {
            double sum = group.Sum(x => x.Value);
            return new GpuEngineReading(group.Key, group.All(x => double.IsFinite(x.Value) && x.Value >= 0) && sum <= 100 ? sum : null);
        }).ToArray();
}
