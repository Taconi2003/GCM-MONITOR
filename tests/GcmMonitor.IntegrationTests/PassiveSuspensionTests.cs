using System.Globalization;
using System.Text.Json;
using GcmMonitor.App;
using GcmMonitor.Core;
using GcmMonitor.Persistence;
using GcmMonitor.Reporting;
using Microsoft.Data.Sqlite;
using PdfSharp.Pdf.IO;

namespace GcmMonitor.IntegrationTests;

public sealed class PassiveSuspensionTests
{
    [PassiveSuspensionFact]
    public async Task CapturePassiveSessionForWindowsStandbyCorrelation()
    {
        const int minutes = 5;
        string output = Environment.GetEnvironmentVariable("GCM_PASSIVE_SUSPENSION_OUTPUT")!;
        string engine = Environment.GetEnvironmentVariable("GCM_PASSIVE_VALIDATION_ENGINE") ?? EngineProcessTests.Executable("GcmMonitor.Engine");
        int readyAfter = int.Parse(Environment.GetEnvironmentVariable("GCM_PASSIVE_SUSPENSION_READY_SECONDS")!, CultureInfo.InvariantCulture);
        string database = Path.Combine(output, "private-passive.db");
        var repo = new SqliteSessionRepository(database);
        await using var client = new SessionEngineClient(engine, database);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        var ct = timeout.Token;
        DiagnosticSession? final = null;
        bool ready = false;
        await foreach (var snapshot in client.RunAsync(new() { Modules = [ModuleId.Cpu, ModuleId.Memory], MonitoringMinutes = minutes }, ct))
        {
            final = snapshot;
            Assert.False(snapshot.IsDemo);
            Assert.False(snapshot.Options.StressEnabled);
            Assert.NotNull(snapshot.Stress);
            Assert.False(snapshot.Stress.Available);
            Assert.False(snapshot.Stress.LoadExecuted);
            if (ready || snapshot.State != SessionState.Running || snapshot.EffectiveDuration.TotalSeconds < readyAfter ||
                snapshot.Statistics.All(x => x.Module != ModuleId.Memory || x.Metric != "RAM" || x.ValidSamples < 2)) continue;
            await WriteJsonAsync("ready.json", new
            {
                ReadyUtc = DateTimeOffset.UtcNow, snapshot.StartedAt, DeadlineUtc = snapshot.StartedAt.AddMinutes(minutes)
            });
            ready = true;
        }
        Assert.True(ready);
        Assert.NotNull(final);
        Assert.Equal(SessionState.Completed, final.State);
        var history = await repo.QueryHistoryAsync(new(), ct);
        Assert.Equal(SessionState.Completed, Assert.Single(history.Entries).State);
        Assert.Equal(0, history.ActiveCount);
        var reopened = await repo.GetAsync(final.Id, ct);
        Assert.NotNull(reopened);
        Assert.Equal(final.Issues, reopened.Issues);
        Assert.NotNull(reopened.EndedAt);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(ct);
        using var query = connection.CreateCommand();
        query.CommandText = "PRAGMA quick_check;";
        Assert.Equal("ok", await query.ExecuteScalarAsync(ct));
        query.CommandText = "SELECT Timestamp FROM Measurements WHERE Module=$module AND Metric='RAM' AND Quality=$valid ORDER BY Timestamp;";
        query.Parameters.AddWithValue("$module", (int)ModuleId.Memory);
        query.Parameters.AddWithValue("$valid", (int)DataQuality.Valid);
        var ramTimes = new List<DateTimeOffset>();
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) ramTimes.Add(DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture));
        Assert.True(ramTimes.Count >= 2);
        var intervals = ramTimes.Zip(ramTimes.Skip(1), (before, after) => new { BeforeUtc = before, AfterUtc = after, Seconds = (after - before).TotalSeconds }).ToArray();
        var stats = Assert.Single(reopened.Statistics, x => x.Module == ModuleId.Memory && x.Metric == "RAM");
        Assert.Equal(ramTimes.Count, stats.ValidSamples);
        Assert.InRange(stats.ObservedSeconds, 0, intervals.Where(x => x.Seconds <= SamplingContinuity.MaximumIntervalSeconds).Sum(x => x.Seconds) + 0.001);
        query.CommandText = "SELECT MAX(Timestamp) FROM Measurements;";
        var latestMeasurementUtc = DateTimeOffset.Parse(Assert.IsType<string>(await query.ExecuteScalarAsync(ct)), CultureInfo.InvariantCulture);
        // O SQLite guarda o módulo no JSON da ocorrência. Filtre apenas o texto de lacuna do motor.
        query.CommandText = "SELECT Timestamp FROM CollectionIssues WHERE json_extract(Payload,'$.Message') LIKE 'Lacuna de amostragem%';";
        var gaps = new List<DateTimeOffset>();
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) gaps.Add(DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture));
        Assert.Equal(reopened.Issues.Where(x => x.Message.StartsWith("Lacuna de amostragem", StringComparison.Ordinal))
            .Select(x => x.Timestamp).Order(), gaps.Order());

        string pdfPath = Path.Combine(output, "private-report.pdf");
        await new PdfReportExporter().ExportAsync(reopened, pdfPath, false, ct);
        using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 0);
        await WriteJsonAsync("collection.json", new
        {
            reopened.StartedAt, reopened.EndedAt, SessionSeconds = reopened.EffectiveDuration.TotalSeconds,
            LatestMeasurementUtc = latestMeasurementUtc, RamSamples = ramTimes.Count, stats.ObservedSeconds,
            RamGaps = intervals.Where(x => x.Seconds > SamplingContinuity.MaximumIntervalSeconds), GapIssuesUtc = gaps,
            SQLiteQuickCheck = "ok", PdfPages = pdf.PageCount, StressExecuted = false, IdentifiersExcluded = true
        });

        async Task WriteJsonAsync(string name, object value)
        {
            string path = Path.Combine(output, name);
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), ct);
            File.Move(path + ".tmp", path);
        }
    }
}

public sealed class PassiveSuspensionFactAttribute : FactAttribute
{
    public PassiveSuspensionFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GCM_PASSIVE_SUSPENSION_OUTPUT")))
            Skip = "Prova passiva de cinco minutos para correlação local com eventos do Windows.";
    }
}
