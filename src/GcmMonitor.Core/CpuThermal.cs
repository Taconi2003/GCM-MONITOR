using System.Diagnostics;
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
public sealed record CpuThermalTransfer(CpuThermalFrame Frame, long QueryStarted, long QueryCompleted);
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
    public const int BridgeVersion = 2;
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
        if (version != BridgeVersion || session == Guid.Empty || session != expected || nonce != expectedNonce || nonce?.Length != 64 || !nonce.All(Uri.IsHexDigit))
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
        var packages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sensor in frame.Sensors)
        {
            if (sensor is null || sensor.HardwareId is null || sensor.SensorId is null || sensor.Name is null ||
                sensor.Name.Length > 80 || !Regex.IsMatch(sensor.HardwareId, @"\A/intelcpu/[0-9]{1,4}\z", RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(sensor.SensorId, @"\A" + Regex.Escape(sensor.HardwareId) + @"/temperature/[0-9]{1,6}\z", RegexOptions.CultureInvariant) ||
                !ids.Add(sensor.SensorId) || ClassifyIntel(sensor.Name) != sensor.Role ||
                sensor.Role == CpuThermalRole.Package && !packages.Add(sensor.HardwareId) ||
                sensor.TjMaxCelsius is { } tj && (!double.IsFinite(tj) || tj <= 0) ||
                sensor.Celsius is { } value && (!double.IsFinite(value) || value <= -273.15 || sensor.TjMaxCelsius is null || value > sensor.TjMaxCelsius))
                throw new InvalidDataException("Sensor térmico ambíguo ou inconsistente.");
        }
    }
}

/// <summary>Idade desde o início da consulta no mesmo relógio monotônico do Windows.</summary>
public sealed class CpuThermalBuffer(Guid session)
{
    private readonly object gate = new();
    private CpuThermalFrame? frame;
    private long queryStarted, queryCompleted, receivedTimestamp;
    private DateTimeOffset receivedAt;
    private string? fault;
    public void Accept(CpuThermalTransfer transfer, long timestamp, DateTimeOffset now)
    {
        lock (gate)
        {
            if (fault is not null) throw new InvalidDataException("Fonte térmica encerrada.");
            try
            {
                if (transfer.Frame is null || transfer.QueryStarted <= 0 || transfer.QueryStarted < queryCompleted ||
                    transfer.QueryCompleted < transfer.QueryStarted || timestamp < transfer.QueryCompleted || timestamp < receivedTimestamp ||
                    Stopwatch.GetElapsedTime(transfer.QueryStarted, timestamp) > CpuThermalProtocol.MaximumAge)
                    throw new InvalidDataException("Consulta térmica atrasada ou relógio inválido.");
                CpuThermalProtocol.Validate(transfer.Frame, session, frame?.Sequence ?? 0, frame?.ReadCompletedMilliseconds ?? 0);
            }
            catch (InvalidDataException ex) { fault = ex.Message + " Fonte encerrada."; throw; }
            // Não compartilhar arrays mutáveis com o produtor.
            frame = transfer.Frame with { Sensors = transfer.Frame.Sensors.ToArray() };
            queryStarted = transfer.QueryStarted;
            queryCompleted = transfer.QueryCompleted;
            receivedTimestamp = timestamp;
            receivedAt = now;
        }
    }
    public void Fail(string reason) { lock (gate) fault ??= reason; }
    public CpuThermalSnapshot Read(long timestamp)
    {
        lock (gate)
        {
            string reason = fault ?? (frame is null ? "Aguardando a primeira leitura do auxiliar." :
                timestamp < receivedTimestamp || Stopwatch.GetElapsedTime(queryStarted, timestamp) > CpuThermalProtocol.MaximumAge ?
                    "Consulta do auxiliar há mais de 6 s ou relógio inválido." :
                frame.Status != CpuThermalStatus.Reading ? "Fonte térmica indisponível: " + frame.Status : "");
            return new(frame is null ? null : frame with { Sensors = frame.Sensors.ToArray() }, receivedAt, reason.Length == 0, reason);
        }
    }
}
