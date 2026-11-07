using System.Diagnostics;
using System.Text.Json;
using GcmMonitor.App;
using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Persistence;
using Microsoft.Data.Sqlite;

namespace GcmMonitor.IntegrationTests;

public sealed class AdvancedCollectionTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(folder, "advanced.db");

    [Fact]
    public async Task NativeAdvancedSourcesExposeRealReadingsOrExplicitCapabilityLimits()
    {
        var inventory = await new WindowsInventoryProvider().ReadAsync();
        await using var source = new WindowsPassiveSource();
        var modules = new[] { ModuleId.Storage, ModuleId.WindowsEvents, ModuleId.Battery, ModuleId.WindowsUpdate, ModuleId.Sensors, ModuleId.Devices, ModuleId.Wifi, ModuleId.Power };
        var summaries = new List<object>();
        foreach (var module in modules)
        {
            var options = new DiagnosticOptions
            {
                Modules = [module], DiskIds = inventory.Disks.Select(x => x.Id).ToArray(),
                DetailedBattery = module == ModuleId.Battery, DetailedWifi = module == ModuleId.Wifi
            };
            var watch = Stopwatch.StartNew();
            var batch = await source.SampleAsync(options, DateTimeOffset.UtcNow, false, false, default);
            Assert.All(batch.Measurements, x => { x.Validate(); Assert.False(x.IsDemo); Assert.Equal(module, x.Module); });
            Assert.All(batch.Records, x => { x.Validate(); Assert.False(x.IsDemo); Assert.Equal(module, x.Module); });
            Assert.Single(batch.Assessments);
            if (module == ModuleId.Power) Assert.Contains(batch.Records, x => x.Key == "state");
            summaries.Add(new
            {
                Module = module.ToString(), State = batch.Assessments.Single().State.ToString(),
                ValidMeasurements = batch.Measurements.Count(x => x.Quality == DataQuality.Valid),
                UnavailableMeasurements = batch.Measurements.Count(x => x.Quality == DataQuality.Unavailable),
                ValidRecords = batch.Records.Count(x => x.Quality == DataQuality.Valid),
                UnavailableRecords = batch.Records.Count(x => x.Quality != DataQuality.Valid),
                DurationSeconds = watch.Elapsed.TotalSeconds,
                Sources = batch.Measurements.Select(x => x.Source).Concat(batch.Records.Select(x => x.Source)).Distinct().ToArray()
            });
        }
        await Task.Delay(5100);
        var second = await source.SampleAsync(new() { Modules = [ModuleId.Sensors] }, DateTimeOffset.UtcNow, false, false, default);
        Assert.All(second.Measurements, x => x.Validate());
        var evidence = Path.Combine(EngineProcessTests.FindRoot(), ".artifacts", "validation"); Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "advanced-capabilities.json"), JsonSerializer.Serialize(new
        {
            Version = "0.4.0", TimestampUtc = DateTimeOffset.UtcNow, WindowsBuild = Environment.OSVersion.Version.Build,
            Environment = "Máquina local de desenvolvimento; identificação omitida", StressExecuted = false,
            IdentifiersExcluded = true, Modules = summaries,
            SecondSensorValidSamples = second.Measurements.Count(x => x.Quality == DataQuality.Valid),
            SensorHardwareAccuracyValidated = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task PunctualEngineCompletesWithoutWaitingFiveMinutesAndPersistsAdvancedRecords()
    {
        await using var client = new SessionEngineClient(EngineProcessTests.Executable("GcmMonitor.Engine"), Database);
        DiagnosticSession? last = null; int maxBytes = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var watch = Stopwatch.StartNew();
        await foreach (var snapshot in client.RunAsync(new() { Modules = [ModuleId.Devices, ModuleId.WindowsEvents, ModuleId.WindowsUpdate], MonitoringMinutes = 5 }, timeout.Token))
        { last = snapshot; maxBytes = Math.Max(maxBytes, JsonSerializer.SerializeToUtf8Bytes(snapshot, EngineProtocol.Json).Length); }
        Assert.Equal(SessionState.Completed, last!.State); Assert.InRange(watch.Elapsed.TotalSeconds, 0, 89);
        Assert.False(last.IsDemo); Assert.False(last.Options.StressEnabled); Assert.True(maxBytes < EngineProtocol.MaxMessageLength);
        var repo = new SqliteSessionRepository(Database);
        var restored = await repo.GetAsync(last.Id); Assert.NotNull(restored); Assert.NotEmpty(restored.Records);
        Assert.True(restored.Records.Length >= last.Records.Length);
        Assert.All(restored.Records, x => x.Validate());
        await repo.SaveNotesAsync(last.Id, "Observação sintética do teste.");
        Assert.Equal("Observação sintética do teste.", (await repo.GetAsync(last.Id))!.Notes);
        Assert.DoesNotContain(restored.Findings, x => x.Severity == FindingSeverity.Normal);
    }

    [Fact]
    public async Task RecordsAreAtomicRejectMixedOriginsAndDeleteOnlyWithTheirOwnSession()
    {
        var repo = new SqliteSessionRepository(Database); await repo.InitializeAsync();
        var session = new DiagnosticSession { IsDemo = false, State = SessionState.Completed, Options = new() { Modules = [ModuleId.Devices] } };
        var record = new DiagnosticRecord(ModuleId.Devices, "fixture", "Dispositivo fictício", "Versão fictícia", DateTimeOffset.UtcNow, "fixture", DataQuality.Valid);
        await repo.SaveBatchAsync(session, new([], [], []) { Records = [record] });
        Assert.Single((await repo.GetAsync(session.Id))!.Records);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveBatchAsync(session, new([], [], []) { Records = [record with { IsDemo = true }] }));
        Assert.Single((await repo.GetAsync(session.Id))!.Records);
        Assert.True(await repo.DeleteAsync(session.Id));
        await using var connection = new SqliteConnection($"Data Source={Database};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM DiagnosticRecords;";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task SchemaTwoMigratesAdditivelyAndPreservesExistingSessionAndNotes()
    {
        var repo = new SqliteSessionRepository(Database); await repo.InitializeAsync();
        var session = new DiagnosticSession { State = SessionState.Completed, AppVersion = "0.2.0", Notes = "Nota fictícia v0.2" };
        await repo.SaveAsync(session);
        await using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE DiagnosticRecords; PRAGMA user_version=2;"; await command.ExecuteNonQueryAsync();
        }
        await repo.InitializeAsync(); var restored = await repo.GetAsync(session.Id);
        Assert.Equal("0.2.0", restored!.AppVersion); Assert.Equal(session.Notes, restored.Notes);
        Assert.Empty(restored.Records);
        await using var read = new SqliteConnection($"Data Source={Database};Pooling=False"); await read.OpenAsync();
        using var version = read.CreateCommand(); version.CommandText = "PRAGMA user_version;"; Assert.Equal(3L, await version.ExecuteScalarAsync());
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
