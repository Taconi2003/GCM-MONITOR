using GcmMonitor.Core;

namespace GcmMonitor.Collector;

/// <summary>Converte somente leituras passivas do auxiliar. Nunca consulta nem autoriza estresse.</summary>
public sealed class CpuThermalCollection(ICpuThermalFeed feed)
{
    public const string Source = "LibreHardwareMonitorLib 0.9.6 / PawnIO 2.2.0 / CPU direta; horário de recebimento";
    private readonly Dictionary<string, CpuThermalSensor> known = [];
    private long consumedSequence;
    public async Task<CollectionBatch> SampleAsync(DateTimeOffset timestamp, CancellationToken ct)
    {
        var snapshot = await feed.ReadAsync(ct);
        var frame = snapshot.Frame;
        bool fresh = snapshot.Available && frame is not null && frame.Sequence > consumedSequence;
        var current = frame?.Sensors ?? [];
        foreach (var sensor in current)
            if (known.ContainsKey(sensor.SensorId) || known.Count < CpuThermalProtocol.MaximumSensors) known[sensor.SensorId] = sensor;
        // Leitura repetida não cria novo ponto. A série antiga permanece no histórico com uma lacuna.
        string reason = !snapshot.Available ? snapshot.Reason : "Nenhuma amostra nova do auxiliar; não foi repetido o valor anterior.";
        var readings = known.Values.Select(sensor =>
        {
            var reading = fresh ? current.SingleOrDefault(x => x.SensorId == sensor.SensorId) : null;
            double? value = reading?.Celsius;
            return new Measurement(ModuleId.Sensors, "Temperatura CPU · " + sensor.Name,
                sensor.HardwareId + " · " + sensor.SensorId, value, "°C", fresh ? snapshot.ReceivedAt : timestamp, Source,
                value.HasValue ? DataQuality.Valid : DataQuality.Unavailable, false,
                value.HasValue ? null : fresh ? "Sensor ausente ou sem leitura direta válida nesta atualização." : reason);
        }).ToArray();
        if (fresh) consumedSequence = frame!.Sequence;
        bool available = readings.Any(x => x.Quality == DataQuality.Valid);
        return new(readings, [], [])
        {
            Records = [new(ModuleId.Sensors, "cpu-direct", "Leitura direta de temperatura CPU",
                available ? "Núcleos/pacote expostos pelo auxiliar temporário. Timestamp do receptor; frescor do firmware e precisão ainda não qualificados. TjMax não é limiar de proteção; estresse bloqueado." :
                    "Leitura direta indisponível. " + (fresh ? "Nenhum valor de núcleo/pacote disponível." : reason),
                timestamp, Source, available ? DataQuality.Valid : DataQuality.Unavailable)],
            Assessments = [new(ModuleId.Sensors, available ? ModuleState.Partial : ModuleState.Unavailable, Source,
                available ? "Temperaturas CPU disponíveis para observação passiva; demais sensores por capacidade. Proteção térmica não validada." : "Fonte direta de CPU indisponível; outras fontes independentes preservadas.")]
        };
    }
}
