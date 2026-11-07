using GcmMonitor.App;
using GcmMonitor.Core;
using GcmMonitor.Persistence;

namespace GcmMonitor.IntegrationTests;

public sealed class ThermalRehearsalTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ThermalChoice.Continue)]
    [InlineData(ThermalChoice.Stop)]
    public async Task SimulatedChoiceAndRecoveryAreReopenedFromSqlite(ThermalChoice choice)
    {
        string database = Path.Combine(folder, "rehearsal.db");
        await using var client = new SessionEngineClient(EngineProcessTests.Executable("GcmMonitor.Engine"), database,
            CollectionMode.Demonstration, rehearseThermal: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        DiagnosticSession? final = null;
        bool sent = false;
        var phases = new HashSet<StressPhase>();
        await foreach (var session in client.RunAsync(new()
            { Modules = [ModuleId.Cpu, ModuleId.Sensors], MonitoringMinutes = 5 }, timeout.Token))
        {
            final = session;
            Assert.True(session.IsDemo);
            Assert.False(session.Options.StressEnabled);
            Assert.False(session.Stress!.LoadExecuted);
            if (session.ThermalRehearsal is not { } trace) continue;
            phases.Add(trace.Phase);
            if (sent || trace.Notices.LastOrDefault() is not { Level: ThermalAlertLevel.Attention, Choice: null }) continue;
            await client.ResolveThermalAttentionAsync(choice, timeout.Token);
            sent = true;
        }
        Assert.True(sent);
        Assert.NotNull(final);
        Assert.Equal(SessionState.Completed, final.State);
        Assert.Contains(StressPhase.Baseline, phases);
        Assert.Contains(StressPhase.Active, phases);
        Assert.Contains(StressPhase.Recovery, phases);
        var stored = await new SqliteSessionRepository(database).GetAsync(final.Id, timeout.Token);
        var thermal = Assert.IsType<ThermalTrace>(stored!.ThermalRehearsal);
        Assert.Equal(choice, thermal.Notices[0].Choice);
        Assert.NotNull(thermal.Notices[0].ChoiceRecordedAt);
        Assert.Equal(choice == ThermalChoice.Continue, thermal.Notices.Any(x => x.Level == ThermalAlertLevel.Critical));
        var review = ThermalRecoveryAnalyzer.Analyze(thermal, StressPlan.Create(5, 5));
        Assert.Equal(TimeSpan.FromMinutes(1), review.ObservedRecovery);
        Assert.True(review.Drop > 0);
        Assert.True(review.RecoveredPercent > 0);
    }

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
