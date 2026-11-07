namespace GcmMonitor.Core;

/// <summary>Valida continuidade de uma série sintética na prova ociosa.</summary>
public sealed class IdleThermalWatch
{
    private readonly Guid sessionId;
    private readonly Dictionary<string, CpuThermalSensor> baseline;
    private CpuThermalFrame previous;
    private bool lost;
    public TimeSpan Deadline { get; private set; } = CpuThermalProtocol.MaximumAge;

    public IdleThermalWatch(Guid sessionId, CpuThermalFrame first)
    {
        CpuThermalProtocol.Validate(first, sessionId, 0, 0);
        if (first.Status != CpuThermalStatus.Reading || first.Sensors.Any(x => x.Celsius is null) ||
            !first.Sensors.Any(x => x.Role == CpuThermalRole.Package) || !first.Sensors.Any(x => x.Role == CpuThermalRole.Core))
            throw new InvalidDataException("A prova térmica ociosa requer fixture inicial com núcleo e pacote disponíveis.");
        this.sessionId = sessionId;
        baseline = first.Sensors.ToDictionary(x => x.SensorId, StringComparer.Ordinal);
        previous = first;
    }

    public bool Observe(CpuThermalFrame next, TimeSpan elapsed)
    {
        if (lost || elapsed < Deadline - CpuThermalProtocol.MaximumAge || elapsed >= Deadline) return MarkLost();
        try { CpuThermalProtocol.Validate(next, sessionId, previous.Sequence, previous.ReadCompletedMilliseconds); }
        catch (InvalidDataException) { lost = true; throw; }
        if (next.Status != CpuThermalStatus.Reading || next.Sensors.Length != baseline.Count || next.Sensors.Any(sensor =>
            sensor.Celsius is null || !baseline.TryGetValue(sensor.SensorId, out var expected) ||
            sensor with { Celsius = expected.Celsius } != expected)) return MarkLost();
        previous = next;
        Deadline = elapsed + CpuThermalProtocol.MaximumAge;
        return true;
    }

    private bool MarkLost() { lost = true; return false; }
}
