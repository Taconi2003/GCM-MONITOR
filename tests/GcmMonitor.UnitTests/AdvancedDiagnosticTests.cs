using System.Runtime.Versioning;
using System.Text.Json;
using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Engine;

namespace GcmMonitor.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class AdvancedDiagnosticTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static Dictionary<string, object?> Row(params (string Name, object? Value)[] values) => values.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
    private static DiagnosticOptions Options(ModuleId module, bool detailed = false) => new()
    { Modules = [module], DiskIds = ["disk:0"], DetailedBattery = detailed && module == ModuleId.Battery, DetailedWifi = detailed && module == ModuleId.Wifi };

    [Theory]
    [InlineData(50000, 40000, 20)]
    [InlineData(50000, 50000, 0)]
    public void BatteryWearUsesDesignAndFullChargeCapacities(double design, double full, double expected) =>
        Assert.Equal(expected, DiagnosticMath.BatteryWear(design, full)!.Value, 8);
    [Theory]
    [InlineData(0, 40000)]
    [InlineData(50000, 0)]
    [InlineData(50000, 60000)]
    public void MissingOrInconsistentBatteryCapacityDoesNotProduceHealthValue(double design, double full) => Assert.Null(DiagnosticMath.BatteryWear(design, full));

    [Fact]
    public void BatteryRuntimeUsesObservedDischargeAndRejectsUnknownValues()
    {
        Assert.Equal(120, DiagnosticMath.EstimatedBatteryMinutes(30000, 15000));
        Assert.Null(DiagnosticMath.EstimatedBatteryMinutes(30000, 0));
        Assert.Null(DiagnosticMath.EstimatedBatteryMinutes(uint.MaxValue, 15000));
    }

    [Fact]
    public void DetailedBatteryJoinsCapacityByFullInstanceAndTagAndRejectsRelativeUnits()
    {
        bool relative = false;
        var reader = new Reader((_, query) => query.Contains("FROM BatteryStaticData") ?
            [Row(("InstanceName", "private"), ("Tag", 1), ("Capabilities", relative ? 0x40000000u : 0u), ("DesignedCapacity", 50000))] :
            query.Contains("FROM BatteryFullChargedCapacity") ? [Row(("InstanceName", "private"), ("Tag", 1), ("FullChargedCapacity", 40000)), Row(("InstanceName", "private"), ("Tag", 2), ("FullChargedCapacity", 90000))] :
            query.Contains("FROM BatteryStatus") ? [Row(("InstanceName", "private"), ("Tag", 1), ("Discharging", true), ("PowerOnline", false), ("RemainingCapacity", 30000), ("DischargeRate", 15000))] : []);
        var batch = new WindowsAdvancedSource(reader, new Native(), new History()).Sample(Options(ModuleId.Battery, true), Time, default);
        Assert.Equal(20, batch.Measurements.Single(x => x.Metric == "Desgaste estimado WMI").Value!.Value, 8);
        Assert.Equal(120, batch.Measurements.Single(x => x.Metric == "Autonomia por consumo observado").Value);
        relative = true;
        var blocked = new WindowsAdvancedSource(reader, new Native(), new History()).Sample(Options(ModuleId.Battery, true), Time, default);
        Assert.All(blocked.Measurements.Where(x => x.Component == "Bateria root/WMI 0"), x => Assert.Null(x.Value));
    }

    [Fact]
    public void DetailedBatteryUsesActualUnitsAndDoesNotEstimateRuntimeWhileCharging()
    {
        var reader = new Reader((_, query) => query.Contains("Win32_Battery") ?
            [Row(("Name", "fixture"), ("EstimatedChargeRemaining", 50), ("EstimatedRunTime", 80), ("DesignCapacity", 50000), ("FullChargeCapacity", 40000))] : []);
        var source = new WindowsAdvancedSource(reader, new Native(), new History());
        var batch = source.Sample(Options(ModuleId.Battery, true), Time, default);
        Assert.Equal(20, batch.Measurements.Single(x => x.Metric == "Desgaste estimado").Value!.Value, 8);
        Assert.Equal("mWh", batch.Measurements.Single(x => x.Metric == "Capacidade de projeto").Unit);
        Assert.Null(batch.Measurements.Single(x => x.Metric.Contains("Autonomia")).Value);
        Assert.Contains(batch.Records, x => x.Key == "cycles" && x.Quality == DataQuality.Unavailable);
        Assert.All(batch.Measurements, x => x.Validate());
    }

    [Fact]
    public void BatteryAbsenceIsUnavailableWithoutSyntheticZeroCharge()
    {
        var batch = new WindowsAdvancedSource(new Reader(), new Native(), new History()).Sample(Options(ModuleId.Battery), Time, default);
        Assert.Equal(ModuleState.Unavailable, batch.Assessments.Single().State);
        Assert.Empty(batch.Measurements); Assert.Contains(batch.Records, x => x.Key == "absent");
    }

    [Fact]
    public void SensorIdentityComesFromParentAndAcpiZoneIsNeverRelabelledAsCpu()
    {
        var reader = SensorReader(); var source = new WindowsAdvancedSource(reader, new Native(), new History());
        var batch = source.Sample(Options(ModuleId.Sensors), Time, default);
        Assert.Contains(batch.Measurements, x => x.Component.StartsWith("Fixture GPU") && x.Value == 55 && x.Unit == "°C");
        Assert.Contains(batch.Measurements, x => x.Component.StartsWith("Fixture CPU") && x.Value == 44 && x.Unit == "°C");
        Assert.Contains(batch.Measurements, x => x.Value is null && x.Unit == "RPM" && x.Quality == DataQuality.Unavailable);
        var zone = batch.Measurements.Single(x => x.Metric.Contains("ACPI"));
        Assert.Contains("não identificada como CPU/GPU", zone.Component); Assert.Equal(26.85, zone.Value!.Value, 8);
        Assert.DoesNotContain(batch.Measurements, x => x.Component.Contains("orphan"));
        reader.Failure = true;
        var lost = source.Sample(Options(ModuleId.Sensors), Time.AddSeconds(5), default);
        Assert.Contains(lost.Measurements, x => x.Component.StartsWith("Fixture GPU") && x.Value is null);
        Assert.All(lost.Measurements, x => Assert.Null(x.Value));
    }

    [Fact]
    public void SmartCounterRequiresAssociationAndTreatsUnexposedTemperatureAsUnavailable()
    {
        var reader = new Reader((_, query) => query.Contains("FROM MSFT_Disk") ? [Row(("Number", 0), ("__RELPATH", "MSFT_Disk.ObjectId=\"fixture\""))] :
            query.StartsWith("ASSOCIATORS", StringComparison.Ordinal) ? [Row(("Temperature", 0), ("Wear", 0), ("PowerOnHours", 300), ("ReadErrorsUncorrected", 2))] : []);
        var source = new WindowsAdvancedSource(reader, new Native(), new History());
        var batch = source.Sample(Options(ModuleId.Storage), Time, default);
        Assert.Null(batch.Measurements.Single(x => x.Unit == "°C").Value);
        Assert.Equal(0, batch.Measurements.Single(x => x.Unit == "%").Value);
        Assert.Contains(reader.Queries, x => x.Contains("AssocClass=MSFT_DiskToStorageReliabilityCounter"));
        Assert.All(batch.Measurements, x => Assert.Equal("disk:0", x.Component));
        Assert.Equal(ModuleState.Partial, batch.Assessments.Single().State);
        Assert.Empty(source.Sample(Options(ModuleId.Storage), Time.AddSeconds(2), default).Measurements);
    }

    [Fact]
    public void PnpDriversAreJoinedByDeviceIdentityAndPrivateIdsAreNotPersisted()
    {
        var reader = new Reader((_, query) => query.Contains("Win32_PnPEntity") ?
            [Row(("DeviceID", "PRIVATE-ID-B"), ("Name", "Fixture B"), ("ConfigManagerErrorCode", 28)), Row(("DeviceID", "PRIVATE-ID-A"), ("Name", "Fixture A"), ("ConfigManagerErrorCode", 0))] :
            [Row(("DeviceID", "PRIVATE-ID-A"), ("DriverVersion", "1.0")), Row(("DeviceID", "PRIVATE-ID-B"), ("DriverVersion", "2.0"))]);
        var batch = new WindowsAdvancedSource(reader, new Native(), new History()).Sample(Options(ModuleId.Devices), Time, default);
        Assert.Contains("2.0", batch.Records.Single(x => x.Title == "Fixture B").Detail);
        Assert.Equal(FindingSeverity.Attention, batch.Records.Single(x => x.Title == "Fixture B").Severity);
        Assert.DoesNotContain("PRIVATE-ID", JsonSerializer.Serialize(batch.Records));
    }

    [Fact]
    public void LongPnpIdsAreNeverTruncatedBeforeDriverAssociation()
    {
        string prefix = new('x', 200);
        var reader = new Reader((_, query) => query.Contains("Win32_PnPEntity") ?
            [Row(("DeviceID", prefix + "B"), ("Name", "Fixture B"), ("ConfigManagerErrorCode", 0))] :
            [Row(("DeviceID", prefix + "A"), ("DriverVersion", "WRONG")), Row(("DeviceID", prefix + "B"), ("DriverVersion", "RIGHT"))]);
        var batch = new WindowsAdvancedSource(reader, new Native(), new History()).Sample(Options(ModuleId.Devices), Time, default);
        Assert.Contains("RIGHT", batch.Records.Single(x => x.Title == "Fixture B").Detail);
    }

    [Fact]
    public void WifiCountsOnlyObservedReconnectsAndNeverInventsSignalWhenAccessIsDenied()
    {
        var native = new Native { Wifi = [new("fixture", "Fixture WLAN", 4, null, null, null, null, "Acesso negado")] };
        var source = new WindowsAdvancedSource(new Reader(), native, new History());
        var first = source.Sample(Options(ModuleId.Wifi, true), Time, default);
        Assert.Null(first.Measurements.Single(x => x.Metric == "Sinal Wi-Fi").Value);
        Assert.Equal(0, first.Measurements.Single(x => x.Unit == "ocorrências").Value);
        native.Wifi = [new("fixture", "Fixture WLAN", 1, 80, 144000, 72000, 6, null)];
        var second = source.Sample(Options(ModuleId.Wifi, true), Time.AddSeconds(5), default);
        Assert.Equal(1, second.Measurements.Single(x => x.Unit == "ocorrências").Value);
        Assert.Equal(144, second.Measurements.Single(x => x.Metric == "Enlace Wi-Fi recepção").Value);
        Assert.Contains(second.Records, x => x.Key == "active" && x.Detail.Contains("não autoriza ping"));
    }

    [Fact]
    public void WifiAbsenceIsUnavailableAndAdapterRemovalBreaksPreviousSignalSeries()
    {
        var native = new Native(); var source = new WindowsAdvancedSource(new Reader(), native, new History());
        Assert.Equal(ModuleState.Unavailable, source.Sample(Options(ModuleId.Wifi), Time, default).Assessments.Single().State);
        native.Wifi = [new("fixture", "Fixture WLAN", 1, 80, 144000, 72000, 6, null)];
        source.Sample(Options(ModuleId.Wifi), Time.AddSeconds(5), default);
        native.Wifi = [];
        var removed = source.Sample(Options(ModuleId.Wifi), Time.AddSeconds(10), default);
        Assert.Contains(removed.Measurements, x => x.Metric == "Sinal Wi-Fi" && x.Value is null);
    }

    [Fact]
    public void PowerChangesAreRecordsAndDoNotBecomeHardwareFailureFindings()
    {
        var native = new Native(); var source = new WindowsAdvancedSource(new Reader(), native, new History());
        source.Sample(Options(ModuleId.Power), Time, default);
        native.Power = native.Power with { Ac = 0 };
        var changed = source.Sample(Options(ModuleId.Power), Time.AddSeconds(5), default);
        Assert.Contains(changed.Records, x => x.Key.StartsWith("change:", StringComparison.Ordinal));
        Assert.All(changed.Records, x => Assert.Null(x.Severity));
    }

    [Fact]
    public void EventSelectionUsesProviderAndIdTogetherAndBoundsItsHistoricalWindow()
    {
        Assert.True(WindowsEventSelection.Relevant("Microsoft-Windows-Kernel-Power", 41));
        Assert.False(WindowsEventSelection.Relevant("UnknownProvider", 41));
        Assert.False(WindowsEventSelection.Relevant("Application Error", 1001));
        Assert.Contains("604800000", WindowsEventSelection.Query("System"));
        Assert.DoesNotContain("Security", WindowsEventSelection.Query("System"));
    }

    [Fact]
    public void GpuEnginesAreGroupedByAdapterAndEngineWithoutSummingDifferentEngines()
    {
        var result = GpuEngineUsage.Aggregate(new Dictionary<string, double>
        {
            ["pid_1_luid_0x0000_0x0001_phys_0_eng_0_engtype_3D"] = 20,
            ["pid_2_luid_0x0000_0x0001_phys_0_eng_0_engtype_3D"] = 30,
            ["pid_1_luid_0x0000_0x0001_phys_0_eng_1_engtype_Copy"] = 80,
            ["pid_1_luid_0x0000_0x0002_phys_0_eng_0_engtype_3D"] = 10,
            ["malformed"] = 99
        });
        Assert.Equal(3, result.Length); Assert.Contains(result, x => x.Percent == 50); Assert.Contains(result, x => x.Percent == 80);
        var invalid = GpuEngineUsage.Aggregate(new Dictionary<string, double> { ["pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D"] = 101 });
        Assert.Null(invalid.Single().Percent);
    }

    [Fact]
    public async Task PunctualOnlySessionCompletesWithoutWaitingChosenFourHoursAndDoesNotRepeatQueries()
    {
        var source = new PunctualSource(); var clock = new Clock();
        var session = await new PassiveSessionRunner(new MemoryRepository(), source, clock).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Devices, ModuleId.WindowsEvents, ModuleId.WindowsUpdate], MonitoringMinutes = 240 }, _ => Task.CompletedTask, default);
        Assert.Equal(3, source.Calls); Assert.Equal(TimeSpan.Zero, session.EffectiveDuration);
        Assert.Equal(SessionState.Completed, session.State); Assert.All(session.Modules, x => Assert.Equal(ModuleState.Completed, x.State));
        Assert.Equal(3, session.Records.Length);
    }

    [Fact]
    public async Task CancellingContinuousMonitoringKeepsAlreadyCompletedPunctualModuleCompleted()
    {
        using var cancelled = new CancellationTokenSource();
        var session = await new PassiveSessionRunner(new MemoryRepository(), new PunctualSource(), new Clock()).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Devices, ModuleId.Memory] }, snapshot =>
            {
                if (snapshot.Modules.Any(x => x.Id == ModuleId.Memory && x.ReadingCount > 0)) cancelled.Cancel();
                return Task.CompletedTask;
            }, cancelled.Token);
        Assert.Equal(SessionState.Cancelled, session.State);
        Assert.Equal(ModuleState.Completed, session.Modules.Single(x => x.Id == ModuleId.Devices).State);
        Assert.Equal(ModuleState.Partial, session.Modules.Single(x => x.Id == ModuleId.Memory).State);
    }

    [Fact]
    public async Task UnavailableSmartAndFirstPdhBaselineDoNotDisableContinuousDiskActivity()
    {
        var source = new DiskBaselineSource();
        var session = await new PassiveSessionRunner(new MemoryRepository(), source, new AdvancingClock()).RunAsync(Guid.NewGuid(),
            new() { Modules = [ModuleId.Storage], DiskIds = ["disk:0"], MonitoringMinutes = 5 }, _ => Task.CompletedTask, default);
        Assert.Equal(150, source.Calls); Assert.Equal(TimeSpan.FromMinutes(5), session.EffectiveDuration);
        Assert.Equal(149, session.Statistics.Single().ValidSamples);
        Assert.Equal(ModuleState.Partial, session.Modules.Single().State);
    }

    [Fact]
    public void DiagnosticRecordRejectsOversizedPayloads()
    {
        var record = new DiagnosticRecord(ModuleId.Devices, "fixture", "fixture", new string('x', 2049), Time, "fixture", DataQuality.Valid);
        Assert.Throws<ArgumentException>(record.Validate);
    }

    private static Reader SensorReader() => new((_, query) => query.Contains("FROM Hardware") ?
        [Row(("Identifier", "/gpu/0"), ("Name", "Fixture GPU"), ("HardwareType", "GpuNvidia")), Row(("Identifier", "/cpu/0"), ("Name", "Fixture CPU"), ("HardwareType", "Cpu"))] :
        query.Contains("FROM Sensor") ?
        [Row(("Identifier", "/gpu/0/temperature/0"), ("Parent", "/gpu/0"), ("Name", "GPU Core"), ("SensorType", "Temperature"), ("Value", 55)),
         Row(("Identifier", "/cpu/0/temperature/0"), ("Parent", "/cpu/0"), ("Name", "CPU Package"), ("SensorType", "Temperature"), ("Value", 44)),
         Row(("Identifier", "/gpu/0/fan/0"), ("Parent", "/gpu/0"), ("Name", "GPU Fan"), ("SensorType", "Fan"), ("Value", 0)),
         Row(("Identifier", "orphan"), ("Parent", "missing"), ("Name", "unknown"), ("SensorType", "Temperature"), ("Value", 99))] :
        query.Contains("ThermalZone") ? [Row(("CurrentTemperature", 3000))] : []);
    private sealed class Reader(Func<string, string, IReadOnlyList<Dictionary<string, object?>>>? read = null) : IWindowsDiagnosticReader
    {
        public List<string> Queries { get; } = [];
        public bool Failure { get; set; }
        public IReadOnlyList<Dictionary<string, object?>> Query(string scope, string query, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Queries.Add(query); if (Failure) throw new UnauthorizedAccessException(); return read?.Invoke(scope, query) ?? []; }
    }
    private sealed class Native : IWindowsPowerWifi
    {
        public PowerReading Power { get; set; } = new(1, 128, 255, uint.MaxValue, Guid.Empty);
        public WifiReading[] Wifi { get; set; } = [];
        public PowerReading ReadPower() => Power;
        public WifiReading[] ReadWifi() => Wifi;
    }
    private sealed class History : IWindowsHistoryReader
    {
        public DiagnosticRecord[] ReadEvents(DateTimeOffset time, CancellationToken ct) => [];
        public DiagnosticRecord[] ReadUpdates(DateTimeOffset time, CancellationToken ct) => [];
    }
    private sealed class Clock : ISessionClock
    {
        public DateTimeOffset UtcNow => Time;
        public TimeSpan Elapsed => TimeSpan.Zero;
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => throw new InvalidOperationException("A sessão pontual não deve esperar.");
    }
    private sealed class PunctualSource : IPassiveCollectionSource
    {
        public int Calls { get; private set; }
        public Task<ComputerInventory> ReadInventoryAsync(CancellationToken ct) => new FixtureInventory().ReadAsync(ct);
        public Task<CollectionBatch> SampleAsync(DiagnosticOptions options, DateTimeOffset timestamp, bool includeProcesses, bool resetBaselines, CancellationToken ct)
        {
            Calls++; var module = options.Modules.Single();
            return Task.FromResult(new CollectionBatch([], [], [])
            { Records = [new(module, "fixture", "fixture", "fixture", timestamp, "fixture", DataQuality.Valid)], Assessments = [new(module, ModuleState.Completed, "fixture", "Consulta concluída.")] });
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class AdvancingClock : ISessionClock
    {
        public DateTimeOffset UtcNow => Time + Elapsed;
        public TimeSpan Elapsed { get; private set; }
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Elapsed += delay; return Task.CompletedTask; }
    }
    private sealed class DiskBaselineSource : IPassiveCollectionSource
    {
        public int Calls { get; private set; }
        public Task<ComputerInventory> ReadInventoryAsync(CancellationToken ct) => new FixtureInventory().ReadAsync(ct);
        public Task<CollectionBatch> SampleAsync(DiagnosticOptions options, DateTimeOffset timestamp, bool includeProcesses, bool resetBaselines, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new CollectionBatch([new(ModuleId.Storage, "Leitura do disco", "disk:0", Calls == 1 ? null : 1,
                "MB/s", timestamp, "fixture / PDH", Calls == 1 ? DataQuality.Insufficient : DataQuality.Valid, false)], [], [])
            { Assessments = Calls == 1 ? [new(ModuleId.Storage, ModuleState.Unavailable, "fixture / SMART", "Sem SMART; PDH aguarda segunda amostra.")] : [] });
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
