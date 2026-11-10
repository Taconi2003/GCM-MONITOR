using System.Diagnostics;

namespace GcmMonitor.Core;

/// <summary>Valida continuidade de uma série sintética na prova ociosa.</summary>
public sealed class IdleThermalWatch
{
    private readonly CpuThermalBuffer buffer;
    private readonly Dictionary<string, CpuThermalSensor> baseline;
    private long queryStarted;
    private bool lost;

    public IdleThermalWatch(Guid sessionId, CpuThermalTransfer initial, long received)
    {
        buffer = new(sessionId);
        buffer.Accept(initial, received, default);
        queryStarted = initial.QueryStarted;
        if (DeadlineFrom(received) <= TimeSpan.Zero) throw new InvalidDataException("A fixture térmica inicial já expirou.");
        var first = initial.Frame;
        if (first.Status != CpuThermalStatus.Reading || first.Sensors.Any(x => x.Celsius is null) ||
            !first.Sensors.Any(x => x.Role == CpuThermalRole.Package) || !first.Sensors.Any(x => x.Role == CpuThermalRole.Core))
            throw new InvalidDataException("A prova térmica ociosa requer fixture inicial com núcleo e pacote disponíveis.");
        baseline = first.Sensors.ToDictionary(x => x.SensorId, StringComparer.Ordinal);
    }

    public TimeSpan DeadlineFrom(long origin) => CpuThermalProtocol.MaximumAge - Stopwatch.GetElapsedTime(queryStarted, origin);

    public bool Observe(CpuThermalTransfer transfer, long received)
    {
        if (lost || DeadlineFrom(received) <= TimeSpan.Zero) return MarkLost();
        try { buffer.Accept(transfer, received, default); }
        catch (InvalidDataException) { lost = true; throw; }
        var next = transfer.Frame;
        if (next.Status != CpuThermalStatus.Reading || next.Sensors.Length != baseline.Count || next.Sensors.Any(sensor =>
            sensor.Celsius is null || !baseline.TryGetValue(sensor.SensorId, out var expected) ||
            sensor with { Celsius = expected.Celsius } != expected)) return MarkLost();
        queryStarted = transfer.QueryStarted;
        return true;
    }

    private bool MarkLost() { lost = true; return false; }
}
