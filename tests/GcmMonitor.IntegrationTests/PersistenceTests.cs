using GcmMonitor.App;
using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Persistence;
using GcmMonitor.Presentation;
using GcmMonitor.Reporting;
using Microsoft.Data.Sqlite;
using PdfSharp.Pdf.IO;

namespace GcmMonitor.IntegrationTests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));
    private SqliteSessionRepository Repository => new(Path.Combine(folder, "demo.db"));

    [Fact]
    public async Task RejectedSqliteBatchStopsCollectionAndPreservesOnlyCommittedSamples()
    {
        var repo = Repository;
        await repo.InitializeAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = repo.DatabasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(deadline.Token);
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER RejectSecondMeasurement BEFORE INSERT ON Measurements
                WHEN EXISTS (SELECT 1 FROM Measurements WHERE SessionId=NEW.SessionId AND Metric=NEW.Metric AND Component=NEW.Component)
                BEGIN SELECT RAISE(ABORT, 'Falha de persistência do ensaio'); END;
                """;
            await command.ExecuteNonQueryAsync(deadline.Token);
        }
        await using var client = new SessionEngineClient(EngineProcessTests.Executable("GcmMonitor.Engine"), repo.DatabasePath);
        DiagnosticSession? first = null, terminal = null;
        await foreach (var snapshot in client.RunAsync(new() { Modules = [ModuleId.Memory] }, deadline.Token))
        {
            if (snapshot is { State: SessionState.Running, Measurements.Length: > 0 })
            {
                Assert.Null(first);
                first = snapshot;
            }
            if (snapshot.IsTerminal) terminal = snapshot;
        }
        Assert.NotNull(first);
        Assert.NotNull(terminal);
        Assert.Equal(SessionState.Error, terminal.State);
        Assert.Contains("SqliteException", terminal.Transitions.Last().Reason);
        Assert.Equal(first.Measurements, terminal.Measurements);
        Assert.Equal(first.Statistics, terminal.Statistics);
        Assert.Equal(first.Modules.Single().ReadingCount, terminal.Modules.Single().ReadingCount);

        var reopened = await repo.GetAsync(first.Id, deadline.Token);
        Assert.NotNull(reopened);
        Assert.Equal(SessionState.Error, reopened.State);
        Assert.Equal(first.Measurements.Length, reopened.Measurements.Length);
        Assert.Equal(first.Statistics, reopened.Statistics);
        Assert.False(reopened.Stress!.LoadExecuted);

        await using var verification = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = repo.DatabasePath, Pooling = false }.ToString());
        await verification.OpenAsync(deadline.Token);
        using var verify = verification.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM Measurements;";
        Assert.Equal((long)first.Measurements.Length, await verify.ExecuteScalarAsync(deadline.Token));
        verify.CommandText = "PRAGMA quick_check;";
        Assert.Equal("ok", await verify.ExecuteScalarAsync(deadline.Token));
    }

    [Fact]
    public async Task HistoryPaginatesAcrossOldLimitAndRetentionKeepsActiveSessions()
    {
        var repo = Repository;
        await repo.InitializeAsync();
        var today = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 127; i++)
            await repo.SaveAsync(new DiagnosticSession { ComputerName = "FIXTURE " + i, StartedAt = today.AddMinutes(-i), State = SessionState.Completed });
        var older = new DiagnosticSession { ComputerName = "FIXTURE % antigo", StartedAt = today.AddDays(-120), State = SessionState.Completed };
        var active = older with { Id = Guid.NewGuid(), ComputerName = "FIXTURE ativo", State = SessionState.Running };
        await repo.SaveAsync(older); await repo.SaveAsync(active);
        var ids = new HashSet<Guid>();
        for (int page = 0; page < 6; page++)
        {
            var current = await repo.QueryHistoryAsync(new(Page: page));
            Assert.Equal(129, current.TotalCount); Assert.Equal(1, current.ActiveCount);
            Assert.InRange(current.Entries.Length, 1, 25);
            Assert.All(current.Entries, x => Assert.True(ids.Add(x.Id)));
        }
        Assert.Equal(129, ids.Count);
        var filtered = await repo.QueryHistoryAsync(new(Computer: "%", State: SessionState.Completed,
            FromUtc: older.StartedAt.AddHours(-1), UntilUtc: older.StartedAt.AddHours(1)));
        Assert.Equal(older.Id, Assert.Single(filtered.Entries).Id);
        Assert.Equal(1, await repo.DeleteTerminalBeforeAsync(today.AddDays(-90)));
        Assert.Null(await repo.GetAsync(older.Id)); Assert.Equal(SessionState.Running, (await repo.GetAsync(active.Id))!.State);
        Assert.Equal(128, (await repo.QueryHistoryAsync(new())).TotalCount);
    }

    [Fact]
    public async Task PreferencesSurviveReopeningAndAnInvalidSavePreservesThePreviousValues()
    {
        string settings = Path.Combine(folder, "Settings", "preferences.json");
        var store = new JsonUserPreferencesStore(settings);
        Assert.Null(await store.LoadAsync());
        var expected = new UserPreferences(false, Path.Combine(folder, "Relatórios"), 90);
        await store.SaveAsync(expected);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(expected with { RetentionDays = 3 }));
        Assert.Equal(expected, await new JsonUserPreferencesStore(settings).LoadAsync());
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(settings)!));
    }

    [Fact]
    public async Task PdfCanBeReexportedFromHistoryAndSurvivesSessionDeletion()
    {
        var repo = Repository;
        var time = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var fixture = DemoFixtures.Archived(SessionState.Interrupted, time) with
        {
            ComputerName = "DEMO-GCM01", Notes = "Observação sintética do técnico. A ventilação e a alimentação são exemplos.",
            Inventory = new("DEMO-GCM01", "Windows 11 fictício", "Versão de exemplo", "x64", null,
                "Processador fictício", 4, 8, 16L * 1073741824, [], [], [], time, IsDemo: true),
            Measurements = Enumerable.Range(0, 16).Select(i => new Measurement(ModuleId.Cpu, "CPU", "Processador fictício",
                i == 7 ? null : 20 + i % 5 * 7, "%", time.AddSeconds(i * 5), "Amostras sintéticas",
                i == 7 ? DataQuality.Unavailable : DataQuality.Valid, true, i == 7 ? "Lacuna ilustrativa" : null)).ToArray(),
            Statistics = [new(ModuleId.Cpu, "CPU", "Processador fictício", "%", 20, 48, 1800, 60, 15, time, time.AddSeconds(75))],
            Processes = [new(101, time, "editor-demonstrativo.exe", 12, 40, 32, 8388608d * 40, 40, 10485760, time, time.AddSeconds(40), true, 8)],
            Records = [new(ModuleId.WindowsEvents, "demo:power", "Ocorrência sintética de energia",
                "Kernel-Power, ID 41. O evento sozinho não identifica a causa.", time, "Fonte demonstrativa", DataQuality.Valid, true)],
            Issues = [new(time.AddSeconds(35), ModuleId.Cpu, "Lacuna sintética de amostragem.")]
        };
        await repo.InitializeAsync(); await repo.SaveAsync(fixture);
        string path = Path.Combine(folder, "relatorio.pdf");
        var vm = new ShellViewModel(new ReportEngine(), repo, new ReportDialogs(path), pdfExporter: new PdfReportExporter());
        await vm.InitializeAsync();
        var row = Assert.Single(vm.History);
        await vm.OpenHistoryCommand.ExecuteAsync(row);
        await vm.ExportPdfCommand.ExecuteAsync(null);
        Assert.True(File.Exists(path), vm.Message);
        await vm.ExportPdfCommand.ExecuteAsync(row);
        Assert.Contains("salvo", vm.Message);
        using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import))
        {
            Assert.InRange(pdf.PageCount, 2, 12);
            Assert.Contains("Relatório técnico", pdf.Info.Title);
        }
        await using (var db = new SqliteConnection($"Data Source={repo.DatabasePath};Mode=ReadOnly;Pooling=False"))
        {
            await db.OpenAsync(); using var count = db.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM ExportHistory WHERE Bytes>0;";
            Assert.Equal(2L, await count.ExecuteScalarAsync());
        }
        Assert.True(await repo.DeleteAsync(fixture.Id)); Assert.True(File.Exists(path));
        if (Environment.GetEnvironmentVariable("GCM_EXPORT_PDF_EXAMPLE") == "1")
        {
            string evidence = Path.Combine(EngineProcessTests.FindRoot(), ".artifacts", "pdf");
            Directory.CreateDirectory(evidence);
            File.Copy(path, Path.Combine(evidence, "GCM-relatorio-demonstrativo.pdf"), overwrite: true);
        }
    }

    [Fact]
    public async Task ExportRefusesActiveSessionAndPreservesExistingFileWithoutOverwrite()
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "existente.pdf");
        await File.WriteAllTextAsync(path, "fixture original");
        var exporter = new PdfReportExporter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(new DiagnosticSession { State = SessionState.Running }, path, true));
        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(DemoFixtures.Archived(SessionState.Completed, DateTimeOffset.UtcNow), path, false));
        Assert.Equal("fixture original", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.EnumerateFiles(folder));
    }

    private sealed class ReportDialogs(string destination) : IUserDialogs
    {
        public bool Confirm(string title, string message) => true;
        public string? ChooseReportFolder(string current) => null;
        public string? ChoosePdfPath(string folder, string fileName) => destination;
    }
    private sealed class ReportEngine : IDiagnosticEngineClient
    {
        public IAsyncEnumerable<DiagnosticSession> RunAsync(DiagnosticOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ReopeningDatabasePreservesSessionTransitionsAndTechnicianNotes()
    {
        var repo = Repository; await repo.InitializeAsync();
        var session = DemoFixtures.Archived(SessionState.Cancelled, DateTimeOffset.UtcNow);
        await repo.SaveAsync(session); await repo.SaveNotesAsync(session.Id, "Observação sintética com acentos; ' e parâmetros.");
        var reopened = Repository; await reopened.InitializeAsync(); var loaded = await reopened.GetAsync(session.Id);
        Assert.NotNull(loaded); Assert.Equal(SessionState.Cancelled, loaded.State); Assert.Equal(session.Transitions, loaded.Transitions);
        Assert.Equal("Observação sintética com acentos; ' e parâmetros.", loaded.Notes);
        await repo.SaveAsync(session); Assert.Equal(loaded.Notes, (await repo.GetAsync(session.Id))!.Notes);
    }
    [Fact]
    public async Task CleanupCannotDeleteRunningSessionAndDoesNotTouchExternalPdf()
    {
        var repo = Repository; await repo.InitializeAsync();
        var active = new DiagnosticSession().TransitionTo(SessionState.Validating, "test", DateTimeOffset.UtcNow).TransitionTo(SessionState.Running, "test", DateTimeOffset.UtcNow);
        var finished = DemoFixtures.Archived(SessionState.Completed, DateTimeOffset.UtcNow);
        await repo.SaveAsync(active); await repo.SaveAsync(finished);
        var external = Path.Combine(folder, "external-report.pdf"); await File.WriteAllTextAsync(external, "Synthetic external PDF sentinel");
        Assert.False(await repo.DeleteAsync(active.Id)); Assert.Equal(1, await repo.ClearTerminalAsync());
        Assert.NotNull(await repo.GetAsync(active.Id)); Assert.Null(await repo.GetAsync(finished.Id)); Assert.True(File.Exists(external));
    }
    [Fact]
    public async Task RecoveryMarksUnfinishedSessionInterruptedWithoutResumingStress()
    {
        var repo = Repository; await repo.InitializeAsync();
        var session = new DiagnosticSession().TransitionTo(SessionState.Validating, "test", DateTimeOffset.UtcNow).TransitionTo(SessionState.Running, "test", DateTimeOffset.UtcNow);
        await repo.SaveAsync(session); Assert.Equal(1, await Repository.RecoverInterruptedAsync());
        var loaded = await repo.GetAsync(session.Id); Assert.Equal(SessionState.Interrupted, loaded!.State); Assert.False(loaded.Options.StressEnabled);
        Assert.Equal(0, await repo.RecoverInterruptedAsync());
    }
    [Fact]
    public async Task WalAndPatchedNativeSqliteAreActuallyLoaded()
    {
        var repo = Repository; await repo.InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={repo.DatabasePath};Pooling=False"); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", await command.ExecuteScalarAsync());
        command.CommandText = "select sqlite_version();"; var version = Version.Parse((string)(await command.ExecuteScalarAsync())!);
        Assert.True(version >= new Version(3, 50, 2), "A biblioteca nativa deve corrigir CVE-2025-6965.");
    }
    [Fact]
    public async Task MixingRealAndSyntheticDataInSameSessionIsRejected()
    {
        var repo = Repository; await repo.InitializeAsync();
        var measurement = new Measurement(ModuleId.Cpu, "CPU", "fixture", 10, "%", DateTimeOffset.UtcNow, "test", DataQuality.Valid, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveAsync(new DiagnosticSession { IsDemo = false, Measurements = [measurement] }));
    }
    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); // Diretório único criado pelo próprio teste.
    }
}
