using System.Diagnostics;
using System.IO.Pipes;
using GcmMonitor.App;
using GcmMonitor.Core;
using GcmMonitor.Persistence;

namespace GcmMonitor.IntegrationTests;

public sealed class EngineProcessTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(folder, "demo.db");

    internal static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GcmMonitor.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repositório não localizado.");
    }
    internal static string Executable(string project)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        return Path.Combine(FindRoot(), "src", project, "bin", configuration, "net10.0", project + ".exe");
    }

    [Fact]
    public async Task SeparateEngineCompletesPointQueriesWithoutWaitingMonitoringDuration()
    {
        await using var client = new DemoEngineClient(Executable("GcmMonitor.Engine"), Database);
        var snapshots = new List<DiagnosticSession>(); var watch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var snapshot in client.RunAsync(new DiagnosticOptions { Modules = [ModuleId.WindowsUpdate, ModuleId.Devices], MonitoringMinutes = 240 }, timeout.Token)) snapshots.Add(snapshot);
        Assert.Equal(SessionState.Completed, snapshots[^1].State); Assert.Equal(TimeSpan.Zero, snapshots[^1].EffectiveDuration);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10)); Assert.All(snapshots, x => Assert.True(x.IsDemo));
        Assert.All(snapshots[^1].Modules, x => Assert.Equal(ModuleState.Completed, x.State));
        var repo = new SqliteSessionRepository(Database); Assert.Equal(SessionState.Completed, (await repo.GetAsync(snapshots[^1].Id))!.State);
    }
    [Fact]
    public async Task CancellingDemoPreservesPartialStateAndSelectedModulesOnly()
    {
        await using var client = new DemoEngineClient(Executable("GcmMonitor.Engine"), Database);
        DiagnosticSession? last = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var snapshot in client.RunAsync(new DiagnosticOptions { Modules = [ModuleId.Cpu] }, timeout.Token))
        {
            last = snapshot;
            if (snapshot.State == SessionState.Running) await client.CancelAsync(timeout.Token);
        }
        Assert.Equal(SessionState.Cancelled, last!.State); Assert.Single(last.Modules);
        Assert.Equal(ModuleId.Cpu, last.Modules[0].Id); Assert.False(last.Options.StressEnabled);
        Assert.Equal(SessionState.Cancelled, (await new SqliteSessionRepository(Database).GetAsync(last.Id))!.State);
    }
    [Fact]
    public async Task WrongProtocolVersionIsRejectedBeforeCreatingDatabase()
    {
        string pipeName = "gcm-demo-" + Guid.NewGuid().ToString("N"); var id = Guid.NewGuid();
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var info = new ProcessStartInfo(Executable("GcmMonitor.Engine")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--pipe", pipeName, "--session", id.ToString(), "--database", Database }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await new EngineChannel(server).WriteAsync(new StartSessionMessage(99, id, new()), timeout.Token);
            await process.WaitForExitAsync(timeout.Token); Assert.Equal(1, process.ExitCode); Assert.False(File.Exists(Database));
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
    [Fact]
    public async Task CompletedDemoHonorsDiskSelectionAndProducesOnlySyntheticBoundedSamples()
    {
        await using var client = new DemoEngineClient(Executable("GcmMonitor.Engine"), Database);
        DiagnosticSession? last = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await foreach (var snapshot in client.RunAsync(new DiagnosticOptions { Modules = [ModuleId.Storage], DiskIds = ["demo-disk-1"], MonitoringMinutes = 5 }, timeout.Token)) last = snapshot;
        Assert.Equal(SessionState.Completed, last!.State); Assert.Equal(TimeSpan.FromMinutes(5), last.EffectiveDuration);
        Assert.Equal(20, last.Measurements.Length);
        Assert.All(last.Measurements, m => { Assert.Equal("demo-disk-1", m.Component); Assert.True(m.IsDemo); m.Validate(); });
        Assert.Equal(ModuleState.Completed, last.Modules.Single().State);
    }
    [Fact]
    public async Task SupervisorExecutableRefusesToGenerateLoad()
    {
        using var process = Process.Start(new ProcessStartInfo(Executable("GcmMonitor.StressSupervisor")) { UseShellExecute = false, CreateNoWindow = true })!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); Assert.Equal(3, process.ExitCode); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
