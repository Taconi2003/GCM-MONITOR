using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GcmMonitor.Core;
using static GcmMonitor.Collector.DiagnosticValue;

namespace GcmMonitor.Collector;

/// <summary>Somente consultas. Não instala/abre drivers, não muda energia e não inicia buscas de rede/updates.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAdvancedSource(IWindowsDiagnosticReader? reader = null, IWindowsPowerWifi? native = null,
    IWindowsHistoryReader? history = null)
{
    private readonly IWindowsDiagnosticReader reader = reader ?? new WindowsDiagnosticReader();
    private readonly IWindowsPowerWifi native = native ?? new WindowsPowerWifi();
    private readonly IWindowsHistoryReader history = history ?? new WindowsHistoryReader();
    private readonly HashSet<ModuleId> queried = [];
    private readonly Dictionary<string, int> wifiStates = [];
    private readonly Dictionary<string, int> reconnects = [];
    private readonly Dictionary<(string Metric, string Component), string> previousSensors = [];
    private PowerReading? previousPower;
    private sealed record BatteryMetadata(string Instance, double? Tag, string Component, bool AbsoluteUnits);
    private readonly List<BatteryMetadata> batteryMetadata = [];
    private readonly Dictionary<ModuleId, Measurement[]> previousReadings = [];

    public CollectionBatch Sample(DiagnosticOptions options, DateTimeOffset timestamp, CancellationToken ct)
    {
        var measurements = new List<Measurement>(); var records = new List<DiagnosticRecord>();
        var assessments = new List<ModuleAssessment>();
        void Reading(ModuleId module, string metric, string component, double? value, string unit, string source, string? reason = null) =>
            measurements.Add(new(module, metric, component, value, unit, timestamp, source, value.HasValue ? DataQuality.Valid : DataQuality.Unavailable, false,
                value.HasValue ? null : reason ?? "Fonte não expõe leitura válida. Ausência não equivale a defeito."));
        void Record(ModuleId module, string key, string title, string detail, string source, DataQuality quality = DataQuality.Valid) =>
            records.Add(new(module, key, Clean(title, 256), Clean(detail, 2048), timestamp, source, quality));
        IReadOnlyList<Dictionary<string, object?>> Query(string scope, string query) => reader.Query(scope, query, ct);
        foreach (var module in options.Modules)
        {
            ct.ThrowIfCancellationRequested();
            if (module is ModuleId.Cpu or ModuleId.Memory or ModuleId.Processes) continue;
            bool punctual = module is ModuleId.Devices or ModuleId.WindowsEvents or ModuleId.WindowsUpdate or ModuleId.Storage;
            if (punctual && !queried.Add(module)) continue;
            int recordStart = records.Count, measurementStart = measurements.Count;
            try
            {
                switch (module)
                {
                    case ModuleId.WindowsEvents:
                        records.AddRange(history.ReadEvents(timestamp, ct));
                        break;
                    case ModuleId.WindowsUpdate:
                        records.AddRange(history.ReadUpdates(timestamp, ct));
                        break;
                    case ModuleId.Devices:
                    {
                        var pnp = Query(@"root\cimv2", "SELECT DeviceID, Name, ConfigManagerErrorCode FROM Win32_PnPEntity");
                        var drivers = Query(@"root\cimv2", "SELECT DeviceID, DeviceName, Manufacturer, DriverVersion, DriverDate FROM Win32_PnPSignedDriver");
                        var grouped = drivers.GroupBy(x => Identity(x, "DeviceID"), StringComparer.OrdinalIgnoreCase).Where(x => x.Key.Length > 0 && x.Count() == 1).ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
                        int index = 0;
                        foreach (var item in pnp.OrderByDescending(x => Number(x, "ConfigManagerErrorCode") is > 0).Take(512))
                        {
                            grouped.TryGetValue(Identity(item, "DeviceID"), out var driver);
                            var code = Number(item, "ConfigManagerErrorCode");
                            Record(module, "pnp:" + index++, Text(item, "Name"),
                                $"Código PnP: {code?.ToString() ?? "não disponível"}; driver {Text(driver ?? [], "DriverVersion")}; fabricante {Text(driver ?? [], "Manufacturer")}; data {Text(driver ?? [], "DriverDate")}. " +
                                "Somente versão instalada; não consulta disponibilidade online nem executa reparos.", "WMI / Win32_PnPEntity + Win32_PnPSignedDriver",
                                code is null || driver is null ? DataQuality.Unavailable : DataQuality.Valid);
                            if (code is > 0) records[^1] = records[^1] with { Severity = FindingSeverity.Attention };
                        }
                        Record(module, "scope", "Dispositivos e drivers consultados", $"{Math.Min(pnp.Count, 512)} dispositivos listados. IDs PnP usados somente para associação em memória; não armazenados.", "WMI / PnP");
                        if (pnp.Count > 512 || drivers.Count > 4096) Record(module, "limit", "Consulta limitada", "Lista excedeu o limite de coleta. Problemas PnP priorizados; resultado parcial.", "WMI / PnP", DataQuality.Unavailable);
                        break;
                    }
                    case ModuleId.Power:
                    {
                        var power = native.ReadPower();
                        Reading(module, "Alimentação AC", "Sistema", power.Ac <= 1 ? power.Ac : null, "0=bateria; 1=AC", "GetSystemPowerStatus");
                        Record(module, "state", "Alimentação e plano de energia",
                            $"Alimentação: {(power.Ac == 1 ? "AC conectado" : power.Ac == 0 ? "bateria" : "desconhecida")}; plano ativo: {power.Scheme?.ToString() ?? "não disponível"}. Nenhuma política alterada.",
                            "GetSystemPowerStatus / PowerGetActiveScheme", power.Scheme is null || power.Ac > 1 ? DataQuality.Unavailable : DataQuality.Valid);
                        if (previousPower is { } old && (old.Ac != power.Ac || old.Scheme != power.Scheme))
                            Record(module, "change:" + timestamp.UtcTicks, "Mudança de alimentação/plano observada",
                                $"AC: {old.Ac} → {power.Ac}; plano: {old.Scheme} → {power.Scheme}. Detecção por amostragem, sem alteração do sistema.", "GetSystemPowerStatus / PowerGetActiveScheme");
                        previousPower = power;
                        break;
                    }
                    case ModuleId.Wifi:
                    {
                        var adapters = native.ReadWifi();
                        foreach (var key in wifiStates.Keys.Where(x => !adapters.Any(a => a.Key == x)).ToArray()) wifiStates[key] = 4;
                        if (adapters.Length == 0) Record(module, "absent", "Wi-Fi indisponível", "Nenhum adaptador WLAN exposto. Nenhuma varredura ou medição ativa executada.", "Native Wi-Fi / WlanEnumInterfaces", DataQuality.Unavailable);
                        foreach (var adapter in adapters)
                        {
                            if (wifiStates.TryGetValue(adapter.Key, out int old) && old != 1 && adapter.State == 1) reconnects[adapter.Key] = reconnects.GetValueOrDefault(adapter.Key) + 1;
                            wifiStates[adapter.Key] = adapter.State;
                            string key = "wifi:" + adapter.Key;
                            Record(module, key, adapter.Adapter,
                                $"Estado WLAN {adapter.State} ({(adapter.State == 1 ? "conectado" : "não conectado")}); canal {adapter.Channel?.ToString() ?? "não disponível"}; banda não determinada apenas pelo canal; reconexões observadas {reconnects.GetValueOrDefault(adapter.Key)}. " +
                                (adapter.Limitation ?? "SSID, BSSID e perfil não armazenados."), "Native Wi-Fi / consulta local",
                                adapter.Limitation is not null || adapter.State != 1 ? DataQuality.Unavailable : DataQuality.Valid);
                            Reading(module, "Sinal Wi-Fi", adapter.Adapter, adapter.Signal, "%", "WlanQueryInterface / wlanSignalQuality");
                            Reading(module, "Enlace Wi-Fi recepção", adapter.Adapter, adapter.ReceiveKbps / 1000.0, "Mbps", "WlanQueryInterface / ulRxRate (kbit/s)");
                            Reading(module, "Enlace Wi-Fi transmissão", adapter.Adapter, adapter.TransmitKbps / 1000.0, "Mbps", "WlanQueryInterface / ulTxRate (kbit/s)");
                            if (options.DetailedWifi) Reading(module, "Reconexões observadas", adapter.Adapter, reconnects.GetValueOrDefault(adapter.Key), "ocorrências", "Native Wi-Fi / transições amostradas a cada 5 s");
                        }
                        Record(module, "active", "Latência/perda não executadas", "Coleta passiva. Análise detalhada acrescenta reconexões observadas; não autoriza ping, tráfego de teste ou busca de redes. Transições entre amostras podem não ser observadas.", "Política de coleta passiva", adapters.Length > 0 ? DataQuality.Valid : DataQuality.Unavailable);
                        if (adapters.Length > 0) Record(module, "band", "Banda Wi-Fi não determinada",
                            "A consulta disponível informa canal, sinal e enlace. Canal isolado é ambíguo entre bandas; frequência não foi inferida. API adicional ainda requer validação.", "Native Wi-Fi / limites da consulta", DataQuality.Unavailable);
                        break;
                    }
                    case ModuleId.Battery:
                    {
                        var batteries = Query(@"root\cimv2", "SELECT Name, EstimatedChargeRemaining, EstimatedRunTime, DesignCapacity, FullChargeCapacity FROM Win32_Battery");
                        if (batteries.Count == 0) Record(module, "absent", "Bateria indisponível", "Nenhuma bateria exposta pelo Windows. Ausência em desktop não é falha.", "WMI / Win32_Battery", DataQuality.Unavailable);
                        int i = 0;
                        foreach (var battery in batteries.Take(8))
                        {
                            string component = "Bateria " + i++ + " · " + Text(battery, "Name");
                            var charge = Number(battery, "EstimatedChargeRemaining");
                            Reading(module, "Carga da bateria", component, charge is >= 0 and <= 100 ? charge : null, "%", "WMI / Win32_Battery.EstimatedChargeRemaining");
                            var design = Number(battery, "DesignCapacity"); var full = Number(battery, "FullChargeCapacity");
                            if (options.DetailedBattery)
                            {
                                Reading(module, "Capacidade de projeto", component, design is > 0 ? design : null, "mWh", "WMI / Win32_Battery.DesignCapacity (0 significa não suportado)");
                                Reading(module, "Capacidade de carga completa", component, full is > 0 ? full : null, "mWh", "WMI / Win32_Battery.FullChargeCapacity (0 significa não suportado)");
                                Reading(module, "Desgaste estimado", component, DiagnosticMath.BatteryWear(design, full), "%", "100 × (1 − FullChargeCapacity / DesignCapacity)", "Capacidades ausentes, zero ou inconsistentes; desgaste não calculado.");
                                var runtime = Number(battery, "EstimatedRunTime");
                                var power = native.ReadPower();
                                Reading(module, "Autonomia estimada pelo Windows", component, power.Ac == 0 && runtime is >= 0 and < 71582788 ? runtime : null, "min", "Win32_Battery.EstimatedRunTime / consumo presente", "Sem descarga observada ou estimativa desconhecida. Não é previsão exata.");
                            }
                            Record(module, "battery:" + (i - 1), component, "Carga amostrada; capacidades dependem do firmware. Sem descarga forçada, calibração ou alteração de energia.", "WMI / Win32_Battery");
                        }
                        if (options.DetailedBattery && queried.Add(ModuleId.Battery))
                        {
                            try
                            {
                                var cycles = Query(@"root\wmi", "SELECT InstanceName, Tag, CycleCount FROM BatteryCycleCount WHERE Active=True");
                                int j = 0;
                                foreach (var cycle in cycles.Take(8)) Record(module, "cycles:" + j++, "Ciclos · bateria do provedor root/WMI",
                                    "Ciclos reportados: " + (Number(cycle, "CycleCount")?.ToString() ?? "não disponível") + ". Instância do provedor; associação com Win32_Battery não presumida.", "WMI / BatteryCycleCount",
                                    Number(cycle, "CycleCount") is > 0 and < uint.MaxValue ? DataQuality.Valid : DataQuality.Unavailable);
                                if (cycles.Count == 0) Record(module, "cycles", "Ciclos indisponíveis", "Firmware não expôs ciclos.", "WMI / BatteryCycleCount", DataQuality.Unavailable);
                            }
                            catch (Exception ex) when (Unavailable(ex)) { Record(module, "cycles", "Ciclos indisponíveis", "Provedor ausente ou acesso restrito.", "WMI / BatteryCycleCount", DataQuality.Unavailable); }
                            try
                            {
                                var metadata = Query(@"root\wmi", "SELECT InstanceName, Tag, Capabilities, DesignedCapacity FROM BatteryStaticData WHERE Active=True");
                                var fullCharges = Query(@"root\wmi", "SELECT InstanceName, Tag, FullChargedCapacity FROM BatteryFullChargedCapacity WHERE Active=True");
                                int j = 0;
                                foreach (var item in metadata.Take(8))
                                {
                                    string identity = Identity(item, "InstanceName"), component = "Bateria root/WMI " + j++;
                                    var tag = Number(item, "Tag"); var caps = Number(item, "Capabilities");
                                    bool absolute = caps is >= 0 and <= uint.MaxValue && ((uint)caps.Value & 0x40000000) == 0;
                                    var matches = fullCharges.Where(x => Identity(x, "InstanceName") == identity && tag.HasValue && Number(x, "Tag") == tag).ToArray();
                                    var design = Number(item, "DesignedCapacity");
                                    var full = matches.Length == 1 ? Number(matches[0], "FullChargedCapacity") : null;
                                    if (design is <= 0 or >= uint.MaxValue) design = null;
                                    if (full is <= 0 or >= uint.MaxValue) full = null;
                                    Reading(module, "Capacidade de projeto WMI", component, absolute ? design : null, "mWh", "BatteryStaticData.DesignedCapacity / unidades absolutas");
                                    Reading(module, "Capacidade de carga completa WMI", component, absolute ? full : null, "mWh", "BatteryFullChargedCapacity / InstanceName+Tag");
                                    Reading(module, "Desgaste estimado WMI", component, absolute ? DiagnosticMath.BatteryWear(design, full) : null, "%", "100 × (1 − capacidade completa / projeto) / root/WMI");
                                    batteryMetadata.Add(new(identity, tag, component, absolute));
                                }
                                if (metadata.Count == 0) Record(module, "capacity", "Capacidades detalhadas indisponíveis", "Provedor não expôs metadados ativos. Zero e unidades relativas não convertidos para mWh.", "WMI / BatteryStaticData", DataQuality.Unavailable);
                            }
                            catch (Exception ex) when (Unavailable(ex)) { Record(module, "capacity", "Capacidades detalhadas indisponíveis", "Metadados de capacidade não acessíveis; carga básica preservada.", "WMI / BatteryStaticData", DataQuality.Unavailable); }
                        }
                        if (options.DetailedBattery && batteryMetadata.Count > 0)
                        {
                            try
                            {
                                var statuses = Query(@"root\wmi", "SELECT InstanceName, Tag, Discharging, PowerOnline, RemainingCapacity, DischargeRate FROM BatteryStatus WHERE Active=True");
                                foreach (var metadata in batteryMetadata)
                                {
                                    var matches = statuses.Where(x => Identity(x, "InstanceName") == metadata.Instance && metadata.Tag.HasValue && Number(x, "Tag") == metadata.Tag).ToArray();
                                    var status = matches.Length == 1 ? matches[0] : null;
                                    bool discharging = status?.GetValueOrDefault("Discharging") is true && status.GetValueOrDefault("PowerOnline") is false;
                                    var remaining = status is null || !metadata.AbsoluteUnits ? null : Number(status, "RemainingCapacity");
                                    var rate = status is null || !metadata.AbsoluteUnits ? null : Number(status, "DischargeRate");
                                    if (remaining is < 0 or >= uint.MaxValue) remaining = null;
                                    Reading(module, "Capacidade restante WMI", metadata.Component, remaining, "mWh", "BatteryStatus / InstanceName+Tag / unidades absolutas");
                                    Reading(module, "Consumo em descarga", metadata.Component, discharging && rate > 0 ? rate : null, "mW", "BatteryStatus.DischargeRate");
                                    Reading(module, "Autonomia por consumo observado", metadata.Component, discharging ? DiagnosticMath.EstimatedBatteryMinutes(remaining, rate) : null, "min", "60 × RemainingCapacity(mWh) / DischargeRate(mW)", "Sem descarga, unidades absolutas ou consumo válido. Estimativa variável, não previsão exata.");
                                }
                            }
                            catch (Exception ex) when (Unavailable(ex))
                            {
                                foreach (var metadata in batteryMetadata)
                                {
                                    Reading(module, "Capacidade restante WMI", metadata.Component, null, "mWh", "WMI / BatteryStatus", "Fonte de consumo indisponível.");
                                    Reading(module, "Consumo em descarga", metadata.Component, null, "mW", "WMI / BatteryStatus", "Fonte de consumo indisponível.");
                                    Reading(module, "Autonomia por consumo observado", metadata.Component, null, "min", "WMI / BatteryStatus", "Fonte de consumo indisponível.");
                                }
                            }
                        }
                        break;
                    }
                    case ModuleId.Storage:
                    {
                        var disks = Query(@"root\Microsoft\Windows\Storage", "SELECT Number, Size FROM MSFT_Disk");
                        foreach (var id in options.DiskIds)
                        {
                            var matches = disks.Where(x => "disk:" + Number(x, "Number") == id).ToArray();
                            var disk = matches.Length == 1 ? matches[0] : null;
                            var path = disk?.GetValueOrDefault("__RELPATH") as string;
                            IReadOnlyList<Dictionary<string, object?>> counters = path is null ? [] : Query(@"root\Microsoft\Windows\Storage",
                                "ASSOCIATORS OF {" + path + "} WHERE AssocClass=MSFT_DiskToStorageReliabilityCounter");
                            var counter = counters.Count == 1 ? counters[0] : null;
                            foreach (var (property, metric, unit) in new[] { ("Temperature", "Temperatura do disco", "°C"), ("Wear", "Desgaste reportado pelo disco", "%"),
                                ("PowerOnHours", "Horas de funcionamento do disco", "h"), ("ReadErrorsUncorrected", "Erros de leitura não corrigidos", "ocorrências"), ("WriteErrorsUncorrected", "Erros de gravação não corrigidos", "ocorrências") })
                            {
                                var value = counter is null ? null : Number(counter, property);
                                if (value < 0 || property == "Temperature" && value == 0 || property == "Wear" && value > 100) value = null;
                                Reading(module, metric, id, value, unit, "MSFT_DiskToStorageReliabilityCounter / " + property, "Indicador não exposto ou associação inequívoca ausente. Não comprova integridade.");
                            }
                            Record(module, "reliability:" + id, "SMART/confiabilidade · " + id,
                                "Somente indicadores expostos pelo provedor Storage e associação MSFT_DiskToStorageReliabilityCounter. SMART bruto/limiares específicos de fabricante não consultados; não há conclusão de integridade.", "WMI / Storage Reliability", DataQuality.Unavailable);
                        }
                        break;
                    }
                    case ModuleId.Sensors:
                    {
                        var current = new Dictionary<(string Metric, string Component), string>();
                        try
                        {
                            var hardware = Query(@"root\LibreHardwareMonitor", "SELECT Identifier, Name, HardwareType FROM Hardware");
                            var sensors = Query(@"root\LibreHardwareMonitor", "SELECT Identifier, Parent, Name, SensorType, Value FROM Sensor");
                            var parents = hardware.GroupBy(x => Identity(x, "Identifier")).Where(x => x.Key.Length > 0 && x.Count() == 1).ToDictionary(x => x.Key, x => x.Single());
                            foreach (var sensor in sensors.Take(64))
                            {
                                if (!parents.TryGetValue(Identity(sensor, "Parent"), out var parent)) continue;
                                string kind = Text(sensor, "SensorType"), type = Text(parent, "HardwareType");
                                bool gpu = type.StartsWith("Gpu", StringComparison.Ordinal);
                                if (kind is not ("Temperature" or "Fan") && !(gpu && kind == "Load")) continue;
                                string metric = kind == "Temperature" ? "Temperatura · " + Text(sensor, "Name") : kind == "Fan" ? "Ventoinha · " + Text(sensor, "Name") : "GPU · " + Text(sensor, "Name");
                                string unit = kind == "Temperature" ? "°C" : kind == "Fan" ? "RPM" : "%";
                                string component = Clean(Text(parent, "Name") + " · " + Text(sensor, "Identifier"), 256);
                                var value = Number(sensor, "Value");
                                // O contrato WMI v0.9.4 do provedor converte null em 0: zero é ambíguo,
                                // inclusive em ventoinhas. Não o apresentar como uma leitura comprovada.
                                if (value == 0) value = null;
                                if (kind == "Load" && value is < 0 or > 100 || kind == "Fan" && value < 0) value = null;
                                Reading(module, metric, component, value, unit, "WMI / LibreHardwareMonitor (provedor externo existente)", "Valor ausente ou zero ambíguo no contrato WMI externo; não comprova sensor a zero nem ventoinha parada.");
                                current[(metric, component)] = unit;
                            }
                            Record(module, "external", "Sensores do provedor externo",
                                $"{current.Count} sensores vinculados ao hardware por Parent/Identifier. Fonte externa sem garantia de frescor do firmware; timestamp é o recebimento. GCM não inicia nem instala o provedor. Não habilita proteção térmica para estresse.", "WMI / root/LibreHardwareMonitor",
                                current.Count > 0 ? DataQuality.Valid : DataQuality.Unavailable);
                            if (sensors.Count > 64) Record(module, "limit", "Sensores limitados", "Mais de 64 sensores expostos; coleta parcial.", "WMI / LibreHardwareMonitor", DataQuality.Unavailable);
                        }
                        catch (Exception ex) when (Unavailable(ex))
                        { Record(module, "external", "Temperaturas/RPM do provedor indisponíveis", "Provedor LibreHardwareMonitor WMI não acessível. Nenhum driver instalado e nenhuma temperatura/RPM inventados. GPU por PDH é uma fonte independente.", "WMI / root/LibreHardwareMonitor", DataQuality.Unavailable); }
                        try
                        {
                            var zones = Query(@"root\wmi", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                            int i = 0;
                            foreach (var zone in zones.Take(16))
                            {
                                var value = Number(zone, "CurrentTemperature");
                                string component = "Zona ACPI " + i++ + " (não identificada como CPU/GPU)";
                                Reading(module, "Temperatura da zona ACPI", component, value is > 0 ? value / 10 - 273.15 : null, "°C", "WMI / MSAcpi_ThermalZoneTemperature / décimos K");
                                current[("Temperatura da zona ACPI", component)] = "°C";
                            }
                        }
                        catch (Exception ex) when (Unavailable(ex)) { /* Zonas ACPI são opcionais; não substituem temperatura CPU/GPU. */ }
                        foreach (var old in previousSensors.Where(x => !current.ContainsKey(x.Key)))
                            Reading(module, old.Key.Metric, old.Key.Component, null, old.Value, "WMI / sensor anterior indisponível", "Sensor não retornou na consulta atual; série preservada sem ponto fictício.");
                        previousSensors.Clear(); foreach (var sensor in current) previousSensors[sensor.Key] = sensor.Value;
                        if (current.Count == 0) Reading(module, "Sensores térmicos e RPM", "Hardware", null, "°C/RPM", "WMI / capacidades disponíveis", "Nenhum sensor acessível nesta execução.");
                        break;
                    }
                }
                var ownRecords = records.Skip(recordStart).ToArray(); var ownMeasurements = measurements.Skip(measurementStart).ToArray();
                bool valid = ownRecords.Any(x => x.Quality == DataQuality.Valid) || ownMeasurements.Any(x => x.Quality == DataQuality.Valid);
                bool unavailable = ownRecords.Any(x => x.Quality != DataQuality.Valid) || ownMeasurements.Any(x => x.Quality == DataQuality.Unavailable);
                assessments.Add(new(module, !valid ? ModuleState.Unavailable : unavailable ? ModuleState.Partial : punctual ? ModuleState.Completed : ModuleState.Running,
                    "APIs Windows de leitura / v0.3", !valid ? "Fonte ausente, sem suporte ou sem permissão; nenhum valor substituído por zero." :
                    "Consulta observacional; consulte registros, fontes e limitações. Ausência de achados não comprova integridade."));
            }
            catch (Exception ex) when (Unavailable(ex))
            {
                Record(module, "unavailable", "Consulta indisponível", "Fonte não acessível: " + ex.GetType().Name + ". Não significa falha física.", "APIs Windows / v0.3", DataQuality.Unavailable);
                assessments.Add(new(module, ModuleState.Unavailable, "APIs Windows / v0.3", "Fonte não acessível ou permissão insuficiente; demais módulos continuam."));
            }
        }
        // Uma falha da API/remoção de adaptador precisa interromper também a integração das séries anteriores.
        foreach (var module in options.Modules.Where(x => x is ModuleId.Battery or ModuleId.Wifi or ModuleId.Power or ModuleId.Sensors))
        {
            var current = measurements.Where(x => x.Module == module).ToArray();
            foreach (var old in previousReadings.GetValueOrDefault(module) ?? [])
                if (!current.Any(x => x.Metric == old.Metric && x.Component == old.Component))
                    Reading(module, old.Metric, old.Component, null, old.Unit, old.Source, "Série anterior não retornou; lacuna explícita, sem interpolação de leitura ausente.");
            previousReadings[module] = current.Where(x => x.Quality == DataQuality.Valid).ToArray();
        }
        foreach (var record in records) record.Validate();
        return new(measurements.ToArray(), [], []) { Records = records.ToArray(), Assessments = assessments.ToArray() };
    }

    private static bool Unavailable(Exception ex) => ex is ManagementException or COMException or UnauthorizedAccessException or Win32Exception or InvalidDataException or NotSupportedException;
}
