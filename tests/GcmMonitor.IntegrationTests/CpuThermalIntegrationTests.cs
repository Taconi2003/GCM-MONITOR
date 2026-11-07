using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using GcmMonitor.App;
using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Execution;
using GcmMonitor.Persistence;
using GcmMonitor.Presentation;

namespace GcmMonitor.IntegrationTests;

public sealed class CpuThermalIntegrationTests
{
    [Fact]
    public async Task LocalPipeChecksIdentityAndRejectsOversizedThermalFrameBeforePayload()
    {
        string name = "gcm-thermal-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = ThermalPipe.CreateServer(name);
        await using var client = ThermalPipe.CreateClient(name);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        using var own = Process.GetCurrentProcess(); var start = new DateTimeOffset(own.StartTime.ToUniversalTime(), TimeSpan.Zero);
        ThermalPipe.ValidateServer(client, own.Id, start);
        LocalPipePeer.ValidateClient(server.SafePipeHandle, own.Id, start);
        Assert.Throws<InvalidDataException>(() => LocalPipePeer.ValidateClient(server.SafePipeHandle, own.Id, start.AddSeconds(1)));
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, CpuThermalProtocol.MaximumBytes + 1);
        var receive = new EngineChannel(server, CpuThermalProtocol.MaximumBytes, CpuThermalProtocol.Json).ReadAsync<CpuThermalFrame>(timeout.Token);
        await client.WriteAsync(header, timeout.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => receive);
        TokenImpersonationLevel? level = null;
        server.RunAsClient(() => { using var identity = WindowsIdentity.GetCurrent(ifImpersonating: true); level = identity?.ImpersonationLevel; });
        Assert.Equal(TokenImpersonationLevel.Identification, level);
    }

    [Fact]
    public async Task AbsentHelperDoesNotElevateAndThermalFailureDoesNotDisableMemoryCollection()
    {
        await using var feed = new CpuThermalBridge(Guid.NewGuid(), "C:\\gcm-fixture-missing\\missing.dll", "C:\\gcm-fixture-missing\\dotnet.exe");
        var missing = await feed.ReadAsync(default); Assert.False(missing.Available); Assert.Null(missing.Frame);
        await using var source = new WindowsPassiveSource(feed);
        var ram = await source.SampleAsync(new() { Modules = [ModuleId.Memory] }, DateTimeOffset.UtcNow, false, false, default);
        Assert.Contains(ram.Measurements, x => x.Metric == "RAM" && x.Quality == DataQuality.Valid);
    }

    [ThermalValidationFact]
    public async Task FiveMinuteDirectThermalSessionReopensChartsWithoutElevatingControllerOrExecutingStress()
    {
        Assert.False(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
        string root = EngineProcessTests.FindRoot();
        string engine = Path.Combine(root, "src", "GcmMonitor.App", "bin", "Release", "net10.0-windows", "engine", "GcmMonitor.Engine.exe");
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(engine)!, "thermal", "GcmMonitor.SensorProbe.dll")), "Prepare o auxiliar usando scripts/thermal-probe.ps1 -PrepareBridge.");
        string output = Path.Combine(root, ".artifacts", "thermal-validation", "session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var repository = new SqliteSessionRepository(Path.Combine(output, "private-session.db"));
        await repository.InitializeAsync();
        // Opt-in exclusivo deste ensaio. A interface real pede consentimento e o UAC continua normal.
        await using var client = new SessionEngineClient(engine, Path.Combine(output, "private-session.db"), authorizeCpuTemperature: () => true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        DiagnosticSession? final = null; int messages = 0, maximumSamples = 0;
        var watch = Stopwatch.StartNew();
        await foreach (var snapshot in client.RunAsync(new() { Modules = [ModuleId.Cpu, ModuleId.Memory, ModuleId.Sensors, ModuleId.Power], MonitoringMinutes = 5 }, timeout.Token))
        {
            final = snapshot; messages++; maximumSamples = Math.Max(maximumSamples, snapshot.Measurements.Length);
            Assert.NotNull(snapshot.Stress); Assert.False(snapshot.Stress.Available); Assert.False(snapshot.Stress.LoadExecuted);
        }
        Assert.NotNull(final); Assert.Equal(SessionState.Completed, final.State);
        Assert.InRange(watch.Elapsed.TotalSeconds, 300, 390);
        var reopened = await repository.GetAsync(final.Id); Assert.NotNull(reopened);
        var temperatures = reopened.Measurements.Where(x => x.Source == CpuThermalCollection.Source && x.Quality == DataQuality.Valid).ToArray();
        Assert.True(temperatures.Length >= 50, "Nenhuma captura suficiente de temperaturas diretas. Verifique a permissão UAC e os registros privados da sessão.");
        Assert.All(temperatures, x => { x.Validate(); Assert.False(x.IsDemo); Assert.Equal("°C", x.Unit); });
        Assert.DoesNotContain(temperatures, x => x.Metric.Contains("Distance") || x.Metric.Contains("Average") || x.Metric.Contains("Max"));
        var vm = new ShellViewModel(client, repository, new DeclineDialogs(), new WindowsInventoryProvider());
        await vm.InitializeAsync();
        await vm.OpenHistoryCommand.ExecuteAsync(vm.History.Single(x => x.Session.Id == final.Id));
        int charts = vm.ResultMetricCards.Count(x => x.Source.Contains(CpuThermalCollection.Source));
        Assert.True(charts > 0); Assert.InRange(maximumSamples, 1, 512);
        await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            Result = "Passed", ActualSeconds = watch.Elapsed.TotalSeconds, ControllerElevated = false,
            Messages = messages, MaximumUiSamples = maximumSamples, RestoredDirectReadings = temperatures.Length,
            DirectSensorSeries = temperatures.Select(x => x.Component).Distinct().Count(), RestoredThermalCharts = charts,
            StressExecuted = false, SafetyQualified = false, IdentifiersExcluded = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class DeclineDialogs : IUserDialogs
    {
        public bool Confirm(string title, string message) => false;
        public string? ChooseReportFolder(string current) => null;
    }
}

public sealed class ThermalValidationFactAttribute : FactAttribute
{
    public ThermalValidationFactAttribute()
    { if (Environment.GetEnvironmentVariable("GCM_RUN_THERMAL_VALIDATION") != "1") Skip = "Sessão térmica passiva de 5 minutos com auxiliar elevado; somente opt-in local, sem estresse."; }
}
