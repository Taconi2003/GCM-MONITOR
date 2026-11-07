using GcmMonitor.App;
using GcmMonitor.Core;
using GcmMonitor.Persistence;
using GcmMonitor.Reporting;

namespace GcmMonitor.IntegrationTests;

public sealed class InstallerUpgradeDataTests
{
    private const string Notes = "Observação sintética anterior à atualização.";
    private static string Lab => Environment.GetEnvironmentVariable("GCM_INSTALLER_LAB")!;
    private static string Report => Path.Combine(Lab, "relatorio-demonstrativo.pdf");
    private static string Database => Path.Combine(Lab, "profile", "Data", "gcm-monitor.db");
    private static JsonUserPreferencesStore Preferences => new(Path.Combine(Lab, "profile", "Settings", "preferences.json"));
    private static UserPreferences ExpectedPreferences => new(false, Lab, 90);

    [InstallerLabFact]
    public async Task SeedHistoryUsingPreviousInstalledEngine()
    {
        var session = await RunDemoAsync();
        var repository = new SqliteSessionRepository(Database);
        await repository.SaveNotesAsync(session.Id, Notes);
        session = (await repository.GetAsync(session.Id))!;
        long bytes = await new PdfReportExporter().ExportAsync(session, Report, false);
        Assert.True(bytes > 0);
        await repository.RecordPdfExportAsync(session.Id, bytes, DateTimeOffset.UtcNow);
        await Preferences.SaveAsync(ExpectedPreferences);
    }

    [InstallerLabFact]
    public async Task ReopenHistoryAndRunUpdatedInstalledEngine()
    {
        var repository = new SqliteSessionRepository(Database);
        await repository.InitializeAsync();
        var original = Assert.Single(await repository.ListAsync());
        Assert.True(original.IsDemo);
        Assert.Equal(SessionState.Completed, original.State);
        Assert.Equal(Notes, original.Notes);
        Assert.Equal(ExpectedPreferences, await Preferences.LoadAsync());
        var updated = await RunDemoAsync();
        Assert.NotEqual(original.AppVersion, updated.AppVersion);
        Assert.Equal(ProductVersion.Value, updated.AppVersion);
        Assert.Equal(2, (await repository.ListAsync()).Count);
    }

    private static async Task<DiagnosticSession> RunDemoAsync()
    {
        string engine = Environment.GetEnvironmentVariable("GCM_VALIDATE_INSTALLED_ENGINE")!;
        string version = Environment.GetEnvironmentVariable("GCM_INSTALLER_ENGINE_VERSION")!;
        await using var client = new SessionEngineClient(engine, Database, CollectionMode.Demonstration);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        DiagnosticSession? last = null;
        await foreach (var snapshot in client.RunAsync(new() { Modules = [ModuleId.WindowsUpdate] }, timeout.Token))
            last = snapshot;
        Assert.NotNull(last);
        Assert.Equal(version, last.AppVersion);
        Assert.Equal(SessionState.Completed, last.State);
        Assert.True(last.IsDemo);
        Assert.False(last.Options.StressEnabled);
        return last;
    }
}

public sealed class InstallerLabFactAttribute : FactAttribute
{
    public InstallerLabFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GCM_INSTALLER_LAB")))
            Skip = "Ensaio separado do instalador com perfil sintético.";
    }
}
