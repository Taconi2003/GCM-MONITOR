using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GcmMonitor.Core;

public enum CpuThermalStatus { Reading, PermissionRequired, UnsupportedHardware, SourceFailed }
public enum CpuThermalRole { Core, Package }
public sealed record CpuThermalSensor(string HardwareId, string SensorId, string Name, CpuThermalRole Role,
    double? Celsius, double? TjMaxCelsius);
public sealed record CpuThermalFrame(int Version, Guid SessionId, long Sequence, double ReadStartedMilliseconds,
    double ReadCompletedMilliseconds, CpuThermalStatus Status, CpuThermalSensor[] Sensors, bool SafetyQualified = false);
public sealed record CpuThermalHello(int Version, Guid SessionId, string Nonce, string LibraryVersion,
    string DriverVersion, bool Elevated);
public sealed record CpuThermalControl(int Version, Guid SessionId, string Nonce, long Sequence, string Command);
public sealed record CpuThermalSnapshot(CpuThermalFrame? Frame, DateTimeOffset ReceivedAt, bool Available, string Reason);
public interface ICpuThermalFeed : IAsyncDisposable
{
    Task<CpuThermalSnapshot> ReadAsync(CancellationToken ct);
}

/// <summary>Contrato de telemetria passiva. Não aprova proteção térmica ou carga.</summary>
public static class CpuThermalProtocol
{
    public const int Version = 1;
    public const int MaximumBytes = 32768;
    public const int MaximumSensors = 64;
    public static TimeSpan MaximumAge => TimeSpan.FromSeconds(6);
    public static TimeSpan HeartbeatTimeout => TimeSpan.FromSeconds(8);
    public static JsonSerializerOptions Json { get; } = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false };

    public static CpuThermalRole? ClassifyIntel(string name) => name == "CPU Package" ? CpuThermalRole.Package :
        Regex.IsMatch(name, @"\A(?:P-|E-)?Core #[1-9][0-9]*\z", RegexOptions.CultureInvariant) ? CpuThermalRole.Core : null;

    public static void ValidateIdentity(int version, Guid session, Guid expected, string? nonce, string expectedNonce)
    {
        if (version != Version || session == Guid.Empty || session != expected || nonce != expectedNonce || nonce?.Length != 64 || !nonce.All(Uri.IsHexDigit))
            throw new InvalidDataException("Identidade do canal térmico inválida.");
    }
    public static void Validate(CpuThermalFrame frame, Guid session, long previousSequence, double previousCompleted)
    {
        if (frame.Version != Version || frame.SessionId != session || session == Guid.Empty || frame.Sequence != previousSequence + 1 ||
            frame.SafetyQualified || !Enum.IsDefined(frame.Status) || frame.Sensors is null || frame.Sensors.Length > MaximumSensors ||
            !double.IsFinite(frame.ReadStartedMilliseconds) || !double.IsFinite(frame.ReadCompletedMilliseconds) ||
            frame.ReadStartedMilliseconds < previousCompleted || frame.ReadStartedMilliseconds < 0 ||
            frame.ReadCompletedMilliseconds < frame.ReadStartedMilliseconds ||
            previousSequence > 0 && frame.ReadCompletedMilliseconds <= previousCompleted ||
            frame.ReadCompletedMilliseconds - frame.ReadStartedMilliseconds > MaximumAge.TotalMilliseconds ||
            frame.Status != CpuThermalStatus.Reading && frame.Sensors.Length != 0)
            throw new InvalidDataException("Amostra térmica inválida, atrasada ou fora de sequência.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sensor in frame.Sensors)
        {
            if (sensor is null || sensor.HardwareId is null || sensor.SensorId is null || sensor.Name is null ||
                sensor.Name.Length > 80 || !Regex.IsMatch(sensor.HardwareId, @"\A/intelcpu/[0-9]{1,4}\z", RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(sensor.SensorId, @"\A" + Regex.Escape(sensor.HardwareId) + @"/temperature/[0-9]{1,6}\z", RegexOptions.CultureInvariant) ||
                !ids.Add(sensor.SensorId) || ClassifyIntel(sensor.Name) != sensor.Role ||
                sensor.TjMaxCelsius is { } tj && (!double.IsFinite(tj) || tj <= 0) ||
                sensor.Celsius is { } value && (!double.IsFinite(value) || sensor.TjMaxCelsius is null || value > sensor.TjMaxCelsius))
                throw new InvalidDataException("Sensor térmico ambíguo ou inconsistente.");
        }
    }
}

/// <summary>Idade medida no receptor por relógio monotônico; não mede frescor do firmware.</summary>
public sealed class CpuThermalBuffer(Guid session)
{
    private readonly object gate = new();
    private CpuThermalFrame? frame;
    private TimeSpan receivedElapsed;
    private DateTimeOffset receivedAt;
    private string? fault;
    public void Accept(CpuThermalFrame next, TimeSpan elapsed, DateTimeOffset now)
    {
        lock (gate)
        {
            if (fault is not null || elapsed < receivedElapsed) throw new InvalidDataException("Fonte térmica encerrada ou relógio inválido.");
            try { CpuThermalProtocol.Validate(next, session, frame?.Sequence ?? 0, frame?.ReadCompletedMilliseconds ?? 0); }
            catch (InvalidDataException) { fault = "Amostra térmica rejeitada; fonte encerrada."; throw; }
            // Não compartilhar arrays mutáveis com o produtor.
            frame = next with { Sensors = next.Sensors.ToArray() }; receivedElapsed = elapsed; receivedAt = now;
        }
    }
    public void Fail(string reason) { lock (gate) fault ??= reason; }
    public CpuThermalSnapshot Read(TimeSpan elapsed)
    {
        lock (gate)
        {
            string reason = fault ?? (frame is null ? "Aguardando a primeira leitura do auxiliar." :
                elapsed < receivedElapsed || elapsed - receivedElapsed > CpuThermalProtocol.MaximumAge ? "Leitura recebida há mais de 6 s ou relógio inválido." :
                frame.Status != CpuThermalStatus.Reading ? "Fonte térmica indisponível: " + frame.Status : "");
            return new(frame is null ? null : frame with { Sensors = frame.Sensors.ToArray() }, receivedAt, reason.Length == 0, reason);
        }
    }
}
