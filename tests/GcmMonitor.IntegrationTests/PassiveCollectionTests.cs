using System.Diagnostics;
using System.Text.Json;
using GcmMonitor.App;
using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Persistence;
using Microsoft.Data.Sqlite;

namespace GcmMonitor.IntegrationTests;

public sealed class PassiveCollectionTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(folder, "passive.db");

    [Fact]
    public async Task WindowsInventoryContainsPhysicalDiskMappingsAndNoSyntheticHardware()
    {
        var inventory = await new WindowsInventoryProvider().ReadAsync();
        Assert.False(inventory.IsDemo); Assert.True(inventory.LogicalProcessors > 0);
        Assert.True(inventory.PhysicalMemoryBytes > 0); Assert.False(string.IsNullOrWhiteSpace(inventory.WindowsVersion));
        Assert.Equal(inventory.Disks.Length, inventory.Disks.Select(x => x.Id).Distinct().Count());
        Assert.All(inventory.Disks, disk => Assert.All(disk.Volumes, volume =>
        {
            Assert.Contains(disk.Id, volume.DiskIds);
            Assert.True(volume.FreeBytes is null || volume.CapacityBytes is null || volume.FreeBytes <= volume.CapacityBytes);
        }));
    }
    [Fact]
    public async Task NativeCpuMemoryDiskAndProcessSamplesAreRealAndHaveValidUnits()
    {
        var inventory = await new WindowsInventoryProvider().ReadAsync();
        await using var source = new WindowsPassiveSource();
        var options = new DiagnosticOptions { Modules = [ModuleId.Cpu, ModuleId.Memory, ModuleId.Storage, ModuleId.Processes], DiskIds = inventory.Disks.Select(x => x.Id).ToArray() };
        var first = await source.SampleAsync(options, DateTimeOffset.UtcNow, true, false, default);
        Assert.DoesNotContain(first.Measurements, x => x.Metric == "CPU" && x.Quality == DataQuality.Valid);
        await Task.Delay(2100);
        var second = await source.SampleAsync(options, DateTimeOffset.UtcNow, true, false, default);
        Assert.All(second.Measurements, m => { m.Validate(); Assert.False(m.IsDemo); });
        if (WindowsNativeMetrics.LogicalProcessors <= 64) Assert.Contains(second.Measurements, m => m.Metric == "CPU" && m.Value is >= 0 and <= 100);
        Assert.Contains(second.Measurements, m => m.Metric == "RAM" && m.Value is >= 0 and <= 100);
        Assert.Contains(second.Measurements, m => m.Metric == "Comprometida" && m.Unit == "GiB" && m.Value >= 0);
        Assert.Contains(second.Processes, p => p.Pid == Environment.ProcessId && p.MemoryObservedSeconds > 0);
        if (inventory.Disks.Length > 0) Assert.Contains(second.Measurements, m => m.Module == ModuleId.Storage && m.Quality == DataQuality.Valid && m.Unit == "MB/s");
    }
    [Fact]
    public async Task BatchPersistsSamplesModulesFindingsAndEndedProcessRankingsAtomically()
    {
        var repo = new SqliteSessionRepository(Database); await repo.InitializeAsync();
        var time = DateTimeOffset.UtcNow;
        var session = new DiagnosticSession
        {
            ComputerName = "FIXTURE",
            IsDemo = false,
            Options = new() { Modules = [ModuleId.Cpu] },
            State = SessionState.Running,
            Modules = [new(ModuleId.Cpu, ModuleState.Running, "fixture", "fixture")]
        };
        var measurement = new Measurement(ModuleId.Cpu, "CPU", "fixture", 20, "%", time, "fixture", DataQuality.Valid, false);
        var ended = new ProcessAggregate(42, time, "fixture.exe", 5, 10, 50, 2000, 10, 400, time, time.AddSeconds(10), true, 3);
        await repo.SaveBatchAsync(session, new([measurement], [ended], []));
        Assert.True((await repo.ReadProcessRankingsAsync(session.Id)).Single().Ended);
        await using var connection = new SqliteConnection($"Data Source={Database};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM Measurements WHERE SessionId=$id;"; command.Parameters.AddWithValue("$id", session.Id.ToString());
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        var invalid = measurement with { IsDemo = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveBatchAsync(session, new([invalid], [], [])));
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }
    [Fact]
    public async Task CpuCanBeComparedWithEquivalentWindowsPdhCounterWithoutGeneratingLoad()
    {
        if (WindowsNativeMetrics.LogicalProcessors > 64) return;
        using var reference = new WindowsPerformanceQuery(@"\Processor(_Total)\% Processor Time");
        reference.Sample(); var previous = WindowsNativeMetrics.ReadCpu();
        var differences = new List<double>();
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(2100);
            var current = WindowsNativeMetrics.ReadCpu(); var measured = CpuUsage.Calculate(previous, current); previous = current;
            var comparison = reference.Sample().Values.Single().Values.SingleOrDefault(double.NaN);
            Assert.True(measured.HasValue && double.IsFinite(comparison)); Assert.InRange(measured.Value, 0, 100); Assert.InRange(comparison, 0, 100);
            differences.Add(Math.Abs(measured.Value - comparison));
        }
        string evidenceFolder = Path.Combine(EngineProcessTests.FindRoot(), ".artifacts", "validation"); Directory.CreateDirectory(evidenceFolder);
        await File.WriteAllTextAsync(Path.Combine(evidenceFolder, "cpu-reference.json"), JsonSerializer.Serialize(new
        {
            Version = "0.3.0",
            NativeSource = "GetSystemTimes",
            Reference = "PDH Processor(_Total) % Processor Time",
            Samples = differences.Count,
            MeanAbsoluteDifferencePercentagePoints = differences.Average(),
            MaximumAbsoluteDifferencePercentagePoints = differences.Max(),
            AccuracyToleranceApproved = false,
            StressExecuted = false,
            IdentifiersExcluded = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    [Fact]
    public async Task RecoveryDoesNotInterruptASessionWithMatchingLiveProcessIdentity()
    {
        using var own = Process.GetCurrentProcess();
        var repo = new SqliteSessionRepository(Database); await repo.InitializeAsync();
        var session = new DiagnosticSession { State = SessionState.Running, EngineProcessId = own.Id, EngineProcessStartedAt = new DateTimeOffset(own.StartTime.ToUniversalTime(), TimeSpan.Zero) };
        await repo.SaveAsync(session); Assert.Equal(0, await repo.RecoverInterruptedAsync());
        Assert.Equal(SessionState.Running, (await repo.GetAsync(session.Id))!.State);
    }
    [Fact]
    public async Task PassiveEngineCanCancelAndReopenWithRealSamplesAndProcessRankings()
    {
        var inventory = await new WindowsInventoryProvider().ReadAsync();
        await using var client = new SessionEngineClient(EngineProcessTests.Executable("GcmMonitor.Engine"), Database);
        DiagnosticSession? last = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        await foreach (var snapshot in client.RunAsync(new() { Modules = [ModuleId.Cpu, ModuleId.Memory, ModuleId.Processes, ModuleId.Storage], DiskIds = inventory.Disks.Select(x => x.Id).ToArray(), MonitoringMinutes = 5 }, timeout.Token))
        {
            last = snapshot;
            if (snapshot.State == SessionState.Running && snapshot.Processes.Length > 0) await client.CancelAsync();
        }
        Assert.NotNull(last); Assert.False(last.IsDemo); Assert.Equal(SessionState.Cancelled, last.State);
        Assert.False(last.Options.StressEnabled); Assert.NotEmpty(last.Measurements); Assert.NotEmpty(last.Processes);
        var loaded = await new SqliteSessionRepository(Database).GetAsync(last.Id);
        Assert.Equal(last.Statistics, loaded!.Statistics); Assert.Equal(last.Processes, loaded.Processes);
    }
    [Fact]
    public async Task KillingOnlyTheOwnedEnginePreservesCommittedSamplesForRecovery()
    {
        await using var client = new SessionEngineClient(EngineProcessTests.Executable("GcmMonitor.Engine"), Database);
        Guid id = Guid.Empty; bool killed = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await Assert.ThrowsAnyAsync<IOException>(async () =>
        {
            await foreach (var snapshot in client.RunAsync(new() { Modules = [ModuleId.Memory], MonitoringMinutes = 5 }, timeout.Token))
            {
                id = snapshot.Id;
                if (!killed && snapshot.Measurements.Length > 0)
                {
                    using var process = Process.GetProcessById(snapshot.EngineProcessId!.Value);
                    Assert.Equal("GcmMonitor.Engine", process.ProcessName);
                    Assert.Equal(snapshot.EngineProcessStartedAt!.Value.UtcDateTime, process.StartTime.ToUniversalTime());
                    killed = true; process.Kill(entireProcessTree: true); await process.WaitForExitAsync(timeout.Token);
                }
            }
        });
        Assert.True(killed);
        var repo = new SqliteSessionRepository(Database); Assert.Equal(1, await repo.RecoverInterruptedAsync());
        var loaded = await repo.GetAsync(id); Assert.Equal(SessionState.Interrupted, loaded!.State); Assert.NotEmpty(loaded.Measurements);
    }

    [LiveValidationFact]
    public async Task FullFiveMinutePassiveSessionCompletesAndKeepsBoundedUiSnapshots()
    {
        var inventory = await new WindowsInventoryProvider().ReadAsync();
        await using var client = new SessionEngineClient(EngineProcessTests.Executable("GcmMonitor.Engine"), Database);
        DiagnosticSession? last = null; int maxSamples = 0, messages = 0, maxBytes = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var watch = Stopwatch.StartNew();
        await foreach (var snapshot in client.RunAsync(new() { Modules = ModuleCatalog.All.Select(x => x.Id).ToArray(),
            DiskIds = inventory.Disks.Select(x => x.Id).ToArray(), MonitoringMinutes = 5, DetailedBattery = true, DetailedWifi = true }, timeout.Token))
        {
            last = snapshot; maxSamples = Math.Max(maxSamples, snapshot.Measurements.Length);
            maxBytes = Math.Max(maxBytes, JsonSerializer.SerializeToUtf8Bytes(snapshot, EngineProtocol.Json).Length); messages++;
        }
        Assert.Equal(SessionState.Completed, last!.State); Assert.InRange(watch.Elapsed.TotalSeconds, 300, 345);
        Assert.True(maxSamples <= 512); Assert.NotEmpty(last.Processes); Assert.False(last.IsDemo);
        if (inventory.Disks.Length > 0) Assert.Contains(last.Statistics, x => x.Module == ModuleId.Storage && x.Metric == "Leitura do disco" && x.ValidSamples > 1);
        var repo = new SqliteSessionRepository(Database); var restored = await repo.GetAsync(last.Id);
        Assert.Equal(SessionState.Completed, restored!.State); Assert.NotEmpty(restored.Records);
        Assert.Equal(11, restored.Modules.Length); Assert.True(maxBytes < EngineProtocol.MaxMessageLength);
        await using var connection = new SqliteConnection($"Data Source={Database};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM Measurements;";
        var persisted = Convert.ToInt64(await command.ExecuteScalarAsync()); Assert.True(persisted > maxSamples);
        string evidenceFolder = Path.Combine(EngineProcessTests.FindRoot(), ".artifacts", "validation"); Directory.CreateDirectory(evidenceFolder);
        await File.WriteAllTextAsync(Path.Combine(evidenceFolder, "passive-five-minutes.json"), JsonSerializer.Serialize(new
        {
            Version = "0.3.0",
            EndedUtc = DateTimeOffset.UtcNow,
            Result = "Passed",
            ActualSeconds = watch.Elapsed.TotalSeconds,
            PersistedMeasurements = persisted,
            MaxUiSamples = maxSamples,
            MaxMessageBytes = maxBytes,
            RestoredRecords = restored.Records.Length,
            Modules = restored.Modules.Select(x => new { Module = x.Id.ToString(), State = x.State.ToString(), x.ReadingCount }).ToArray(),
            Messages = messages,
            ProcessRankings = last.Processes.Length,
            StressExecuted = false,
            IdentifiersExcluded = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}

public sealed class LiveValidationFactAttribute : FactAttribute
{
    public LiveValidationFactAttribute()
    { if (Environment.GetEnvironmentVariable("GCM_RUN_LONG_PASSIVE_VALIDATION") != "1") Skip = "Ensaio passivo de 5 minutos executado separadamente com opt-in; sem estresse."; }
}
