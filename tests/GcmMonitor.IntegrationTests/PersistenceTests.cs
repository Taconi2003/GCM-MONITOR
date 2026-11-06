using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Persistence;
using Microsoft.Data.Sqlite;

namespace GcmMonitor.IntegrationTests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));
    private SqliteSessionRepository Repository => new(Path.Combine(folder, "demo.db"));

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
