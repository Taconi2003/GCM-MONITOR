using System.Diagnostics;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using GcmMonitor.Core;
using GcmMonitor.Execution;
using GcmMonitor.Persistence;
using GcmMonitor.App;

namespace GcmMonitor.IntegrationTests;

/// <summary>Todos os filhos são fixtures ociosas; nenhum teste gera estresse de CPU.</summary>
public sealed class StressContainmentTests
{
    [Fact]
    public async Task CriticalSyntheticTemperatureStopsTheOwnedIdleWorkerWithoutAControllerChoice()
    {
        await using var probe = await Probe.ConnectAsync();
        var running = await probe.SendStartAsync(15000, thermalFixture: true, criticalFixtureCelsius: 90);
        Assert.Equal("IdleProbeRunning", running.State);
        using var worker = probe.OpenWorker();
        var frame = Fixture(probe.Id, 2);
        frame.Sensors[0] = frame.Sensors[0] with { Celsius = 92 };
        await probe.CommandAsync("thermal-fixture", frame);
        var stopped = await probe.ReadAsync();
        Assert.Equal("IdleProbeStopped", stopped.State);
        Assert.Equal(SafetyStopReason.CriticalTemperature, stopped.Reason);
        Assert.True(worker.HasExited);
    }
    [Fact]
    public async Task SuspendedChildIsAlreadyContainedAndClosingJobKillsItWithoutResuming()
    {
        var job = OwnedProcessJob.CreateSuspended(Executable, ["--idle-probe"]);
        using var child = Process.GetProcessById(job.ProcessId);
        Assert.Equal(job.StartedAt.UtcDateTime, child.StartTime.ToUniversalTime()); Assert.False(job.HasExited);
        job.Dispose(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(6)); Assert.True(child.HasExited);
    }

    [Theory]
    [InlineData("stop", SafetyStopReason.UserStopped)]
    [InlineData("cancel", SafetyStopReason.Cancelled)]
    [InlineData("sensor-lost", SafetyStopReason.SensorLost)]
    [InlineData("suspend", SafetyStopReason.Suspended)]
    [InlineData("power-changed", SafetyStopReason.PowerChanged)]
    [InlineData("persistence-failed", SafetyStopReason.PersistenceFailed)]
    public async Task StopConditionsTerminateOnlyTheOwnedIdleFixture(string command, SafetyStopReason expected)
    {
        await using var controller = await Probe.StartAsync();
        using var worker = controller.OpenWorker();
        await controller.CommandAsync(command);
        var stopped = await controller.ReadAsync();
        Assert.Equal("IdleProbeStopped", stopped.State); Assert.Equal(expected, stopped.Reason);
        Assert.True(stopped.IdleOnly); Assert.False(stopped.LoadExecuted);
        Assert.True(worker.HasExited); // Confirmação terminal só pode ser enviada após a morte do filho.
    }

    [Fact]
    public async Task MissingHeartbeatStopsIdleWorkerWhileControllerPipeRemainsOpen()
    {
        await using var controller = await Probe.StartAsync(); using var worker = controller.OpenWorker();
        var watch = Stopwatch.StartNew(); var stopped = await controller.ReadAsync();
        Assert.Equal(SafetyStopReason.HeartbeatLost, stopped.Reason); Assert.True(worker.HasExited);
        Assert.InRange(watch.Elapsed.TotalSeconds, 2, 9);
        string evidence = Path.Combine(EngineProcessTests.FindRoot(), ".artifacts", "validation"); Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "containment-heartbeat.json"), JsonSerializer.Serialize(new
        {
            Version = "0.4.0", TimestampUtc = DateTimeOffset.UtcNow, Environment = "DEV-LOCAL / Windows x64",
            ConfiguredHeartbeatSeconds = SafetyProbeProtocol.HeartbeatTimeout.TotalSeconds,
            ObservedStopSeconds = watch.Elapsed.TotalSeconds, Reason = stopped.Reason.ToString(),
            WorkerExitConfirmedBeforeAcknowledgement = worker.HasExited, stopped.IdleOnly, stopped.LoadExecuted,
            ThermalProtectionValidated = false, FullStressP0Approved = false, IdentifiersExcluded = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task ValidHeartbeatsKeepOnlyIdleProbeAliveUntilItsShortInternalDeadline()
    {
        await using var controller = await Probe.StartAsync(4500); using var worker = controller.OpenWorker();
        for (int i = 0; i < 5; i++) { await Task.Delay(650); await controller.CommandAsync("heartbeat"); Assert.False(worker.HasExited); }
        var stopped = await controller.ReadAsync(); Assert.Equal(SafetyStopReason.DurationCompleted, stopped.Reason); Assert.True(worker.HasExited);
    }

    [Fact]
    public async Task DelayingStartupMessageReadDoesNotExtendIdleWorkerDeadline()
    {
        await using var controller = await Probe.ConnectAsync();
        await controller.Channel.WriteAsync(new SafetyProbeStart(SafetyProbeProtocol.Version, controller.Id, controller.Nonce,
            SafetyProbeProtocol.Purpose, 250));
        await Task.Delay(1600);
        Assert.Equal("IdleProbeRunning", (await controller.ReadAsync()).State);
        var stopped = await controller.ReadAsync();
        Assert.Equal(SafetyStopReason.DurationCompleted, stopped.Reason);
        Assert.NotNull(stopped.WorkerExitMilliseconds);
        Assert.InRange(stopped.WorkerExitMilliseconds.Value, 200, 1200);
        await SaveEnvironmentEvidenceAsync("delayed-controller-read.json", new
        {
            ConfiguredDurationMilliseconds = 250, ControllerReadDelayMilliseconds = 1600, stopped.WorkerExitMilliseconds,
            Reason = stopped.Reason.ToString(), stopped.IdleOnly, stopped.LoadExecuted, IdentifiersExcluded = true
        });
    }

    [Fact]
    public async Task ControllerDisconnectionTerminatesItsIdleChild()
    {
        await using var controller = await Probe.StartAsync(); using var worker = controller.OpenWorker();
        controller.Pipe.Dispose();
        await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); Assert.True(worker.HasExited);
    }

    [Fact]
    public async Task UnexpectedSupervisorDeathClosesUninheritedJobHandleAndKillsChild()
    {
        await using var controller = await Probe.StartAsync(); using var worker = controller.OpenWorker();
        controller.Supervisor.Kill(entireProcessTree: false); // Não matar a árvore: o kernel precisa comprovar a contenção.
        await controller.Supervisor.WaitForExitAsync();
        await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); Assert.True(worker.HasExited);
    }

    [Fact]
    public async Task ReplayedOrUnknownCommandsFailClosedWithoutRenewingHeartbeat()
    {
        await using var controller = await Probe.StartAsync(); using var worker = controller.OpenWorker();
        await controller.CommandAsync("heartbeat");
        await controller.Channel.WriteAsync(new SafetyProbeCommand(SafetyProbeProtocol.Version, controller.Id, controller.Nonce, 1, "heartbeat"));
        var stopped = await controller.ReadAsync(); Assert.Equal(SafetyStopReason.InvalidProtocol, stopped.Reason); Assert.True(worker.HasExited);
    }

    [Fact]
    public async Task OversizedCommandHeaderStopsOwnedWorkerBeforeAllocatingThePayload()
    {
        await using var controller = await Probe.StartAsync(); using var worker = controller.OpenWorker();
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, SafetyProbeProtocol.MaximumBytes + 1);
        await controller.Pipe.WriteAsync(header); await controller.Pipe.FlushAsync();
        var stopped = await controller.ReadAsync(); Assert.Equal(SafetyStopReason.InvalidProtocol, stopped.Reason); Assert.True(worker.HasExited);
    }

    [Fact]
    public async Task IncompleteCommandPayloadCannotKeepTheWorkerAlivePastHeartbeatDeadline()
    {
        await using var controller = await Probe.StartAsync(); using var worker = controller.OpenWorker();
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, 10000);
        await controller.Pipe.WriteAsync(header); await controller.Pipe.FlushAsync();
        var stopped = await controller.ReadAsync(); Assert.Equal(SafetyStopReason.HeartbeatLost, stopped.Reason); Assert.True(worker.HasExited);
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("execute")]
    [InlineData("start-load")]
    public async Task ArbitraryOrLoadCommandsAreRejectedAndStopOnlyTheIdleProbe(string command)
    {
        await using var controller = await Probe.StartAsync(); using var worker = controller.OpenWorker();
        await controller.CommandAsync(command); var stopped = await controller.ReadAsync();
        Assert.Equal(SafetyStopReason.InvalidProtocol, stopped.Reason); Assert.True(worker.HasExited);
    }

    [Theory]
    [InlineData(99, "IdleContainmentProbe", 1000)]
    [InlineData(1, "IdleContainmentProbe", 1000)]
    [InlineData(SafetyProbeProtocol.Version, "CpuStress", 1000)]
    [InlineData(SafetyProbeProtocol.Version, "IdleContainmentProbe", 300000)]
    public async Task InvalidStartsDoNotCreateAWorker(int version, string purpose, int duration)
    {
        await using var controller = await Probe.ConnectAsync();
        await controller.Channel.WriteAsync(new SafetyProbeStart(version, controller.Id, controller.Nonce, purpose, duration));
        var rejected = await controller.ReadAsync(); Assert.Equal(SafetyStopReason.InvalidProtocol, rejected.Reason);
        Assert.Null(rejected.WorkerPid); Assert.False(rejected.LoadExecuted);
    }

    [Fact]
    public async Task WrongControllerIdentityIsRejectedBeforeWorkerCreation()
    {
        await Assert.ThrowsAnyAsync<IOException>(async () =>
        {
            await using var controller = await Probe.ConnectAsync(wrongControllerStart: true);
            await controller.ReadAsync();
        });
    }

    [Fact]
    public async Task ThermalDeadlineStopsIdleWorkerDespiteValidControllerHeartbeats()
    {
        await using var controller = await Probe.StartAsync(12000, thermalFixture: true);
        using var worker = controller.OpenWorker();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(800);
            await controller.CommandAsync("heartbeat");
        }
        var stopped = await controller.ReadAsync();
        Assert.Equal(SafetyStopReason.SensorLost, stopped.Reason);
        Assert.True(worker.HasExited);
        Assert.InRange(watch.Elapsed.TotalSeconds, 5, 10);
        await SaveEnvironmentEvidenceAsync("thermal-deadline.json", new
        {
            ThermalFramesSynthetic = true, HeartbeatsAccepted = 5, ConfiguredThermalSeconds = CpuThermalProtocol.MaximumAge.TotalSeconds,
            ObservedStopSeconds = watch.Elapsed.TotalSeconds, Reason = stopped.Reason.ToString(), WorkerExitConfirmed = worker.HasExited,
            stopped.IdleOnly, stopped.LoadExecuted, IdentifiersExcluded = true
        });
    }

    [Fact]
    public async Task LosingARequiredCoreAfterAValidFixtureStopsTheIdleWorker()
    {
        await using var controller = await Probe.StartAsync(12000, thermalFixture: true);
        using var worker = controller.OpenWorker();
        await controller.CommandAsync("thermal-fixture", Fixture(controller.Id, 2));
        var incomplete = Fixture(controller.Id, 3);
        await controller.CommandAsync("thermal-fixture", incomplete with { Sensors = [incomplete.Sensors[0]] });
        var stopped = await controller.ReadAsync();
        Assert.Equal(SafetyStopReason.SensorLost, stopped.Reason);
        Assert.True(worker.HasExited);
    }

    [Fact]
    public async Task IncompleteInitialThermalFixtureIsRejectedBeforeWorkerCreation()
    {
        await using var controller = await Probe.ConnectAsync();
        var incomplete = Fixture(controller.Id, 1);
        await controller.Channel.WriteAsync(new SafetyProbeStart(SafetyProbeProtocol.Version, controller.Id, controller.Nonce,
            SafetyProbeProtocol.Purpose, 4000, InitialThermalFixture: incomplete with { Sensors = [incomplete.Sensors[0]] }));
        var stopped = await controller.ReadAsync();
        Assert.Equal(SafetyStopReason.InvalidProtocol, stopped.Reason);
        Assert.Null(stopped.WorkerPid);
    }

    [Fact]
    public async Task NativeWindowsObservationIsRegisteredOrTheIdleProbeIsRefused()
    {
        await using var controller = await Probe.ConnectAsync();
        var initial = await controller.SendStartAsync(1600, observeWindows: true);
        if (initial.State == "IdleProbeStopped")
        {
            if (initial.WindowsSignalObserved)
                Assert.Contains(initial.Reason, new SafetyStopReason?[] { SafetyStopReason.PowerChanged, SafetyStopReason.Suspended, SafetyStopReason.EnvironmentUnavailable });
            else Assert.Equal(SafetyStopReason.EnvironmentUnavailable, initial.Reason);
            Assert.Null(initial.WorkerPid);
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("GCM_REQUIRE_WINDOWS_OBSERVATION"));
            return;
        }
        Assert.True(initial.WindowsObservationEnabled);
        using var worker = controller.OpenWorker();
        var stopped = await controller.ReadAsync();
        Assert.True(worker.HasExited);
        Assert.True(stopped.WindowsObservationEnabled);
        if (stopped.WindowsSignalObserved)
            Assert.Contains(stopped.Reason, new SafetyStopReason?[] { SafetyStopReason.PowerChanged, SafetyStopReason.Suspended, SafetyStopReason.EnvironmentUnavailable });
        else Assert.Equal(SafetyStopReason.DurationCompleted, stopped.Reason);
        await SaveEnvironmentEvidenceAsync("native-environment.json", new
        {
            NativeRegistrationExercised = true, NativePowerBaselineReceived = true, stopped.WindowsSignalObserved, Reason = stopped.Reason.ToString(),
            WorkerExitConfirmed = worker.HasExited, stopped.IdleOnly, stopped.LoadExecuted, IdentifiersExcluded = true
        });
    }

    [Fact]
    public async Task StoppingIdleProbeDoesNotCancelIndependentPassiveSession()
    {
        string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var passive = new SessionEngineClient(EngineProcessTests.Executable("GcmMonitor.Engine"), Path.Combine(folder, "passive.db"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await using var snapshots = passive.RunAsync(new() { Modules = [ModuleId.Memory], MonitoringMinutes = 5 }, timeout.Token).GetAsyncEnumerator();
            while (await snapshots.MoveNextAsync() && snapshots.Current.Measurements.Length == 0) { }
            Assert.Equal(SessionState.Running, snapshots.Current.State);
            await using (var probe = await Probe.StartAsync())
            {
                await probe.CommandAsync("stop"); Assert.Equal(SafetyStopReason.UserStopped, (await probe.ReadAsync()).Reason);
            }
            Assert.True(await snapshots.MoveNextAsync()); Assert.Equal(SessionState.Running, snapshots.Current.State);
            Assert.NotEmpty(snapshots.Current.Measurements); Assert.False(snapshots.Current.Stress!.LoadExecuted);
            await passive.CancelAsync();
            while (await snapshots.MoveNextAsync()) { if (snapshots.Current.IsTerminal) break; }
            Assert.Equal(SessionState.Cancelled, snapshots.Current.State);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task KillingAJobDoesNotTerminateAnUnrelatedIdleFixture()
    {
        using var unrelated = Process.Start(new ProcessStartInfo(Executable, "--idle-probe") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            using var job = OwnedProcessJob.CreateSuspended(Executable, ["--idle-probe"]); job.Resume();
            await job.StopAsync(); Assert.True(job.HasExited); Assert.False(unrelated.HasExited);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(entireProcessTree: true); await unrelated.WaitForExitAsync(); }
    }

    [Fact]
    public async Task NormalPassiveSessionPersistsBlockedAssessmentAndOldSessionsRemainReadable()
    {
        string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new SqliteSessionRepository(Path.Combine(folder, "fixture.db")); await repo.InitializeAsync();
            var assessment = StressReleasePolicy.Evaluate(false, DateTimeOffset.UtcNow);
            var session = new DiagnosticSession { IsDemo = false, State = SessionState.Completed, Stress = assessment };
            await repo.SaveAsync(session); Assert.Equal(assessment.Reasons, (await repo.GetAsync(session.Id))!.Stress!.Reasons);
            Assert.False((await repo.GetAsync(session.Id))!.Stress!.LoadExecuted);
            var forged = session with { Stress = assessment with { Available = true, LoadExecuted = true } };
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveAsync(forged));
            var old = new DiagnosticSession { State = SessionState.Completed, AppVersion = "0.3.0" }; await repo.SaveAsync(old);
            Assert.Null((await repo.GetAsync(old.Id))!.Stress);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static string Executable => EngineProcessTests.Executable("GcmMonitor.StressSupervisor");
    private static CpuThermalFrame Fixture(Guid id, long sequence) => new(CpuThermalProtocol.Version, id, sequence, sequence * 100, sequence * 100 + 10,
        CpuThermalStatus.Reading,
        [
            new("/intelcpu/0", "/intelcpu/0/temperature/0", "CPU Package", CpuThermalRole.Package, 61, 100),
            new("/intelcpu/0", "/intelcpu/0/temperature/1", "Core #1", CpuThermalRole.Core, 58, 100)
        ]);

    private static async Task SaveEnvironmentEvidenceAsync(string name, object evidence)
    {
        string directory = Path.Combine(EngineProcessTests.FindRoot(), ".artifacts", "validation");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Probe : IAsyncDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Nonce { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public NamedPipeServerStream Pipe { get; }
        public Process Supervisor { get; private set; } = null!;
        public EngineChannel Channel { get; private set; } = null!;
        private SafetyProbeEvent started = null!;
        private long sequence;
        private Probe(string name) => Pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        public static async Task<Probe> StartAsync(int duration = 10000, bool thermalFixture = false)
        {
            var probe = await ConnectAsync();
            try
            {
                await probe.SendStartAsync(duration, thermalFixture: thermalFixture);
                Assert.Equal("IdleProbeRunning", probe.started.State);
                return probe;
            }
            catch { await probe.DisposeAsync(); throw; }
        }
        public async Task<SafetyProbeEvent> SendStartAsync(int duration, bool observeWindows = false, bool thermalFixture = false,
            double? criticalFixtureCelsius = null)
        {
            await Channel.WriteAsync(new SafetyProbeStart(SafetyProbeProtocol.Version, Id, Nonce, SafetyProbeProtocol.Purpose,
                duration, observeWindows, thermalFixture ? Fixture(Id, 1) : null, criticalFixtureCelsius));
            return started = await ReadAsync();
        }
        public static async Task<Probe> ConnectAsync(bool wrongControllerStart = false)
        {
            string name = "gcm-safety-probe-" + Guid.NewGuid().ToString("N"); var probe = new Probe(name);
            using var own = Process.GetCurrentProcess();
            var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "--safety-probe", "--pipe", name, "--session", probe.Id.ToString(), "--controller", own.Id.ToString(CultureInfo.InvariantCulture),
                "--controller-start", (own.StartTime.ToUniversalTime().ToFileTimeUtc() + (wrongControllerStart ? 1 : 0)).ToString(CultureInfo.InvariantCulture) }) info.ArgumentList.Add(argument);
            probe.Supervisor = Process.Start(info)!;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await probe.Pipe.WaitForConnectionAsync(timeout.Token);
                LocalPipePeer.ValidateClient(probe.Pipe.SafePipeHandle, probe.Supervisor.Id, new DateTimeOffset(probe.Supervisor.StartTime.ToUniversalTime(), TimeSpan.Zero));
                probe.Channel = new(probe.Pipe, SafetyProbeProtocol.MaximumBytes);
                return probe;
            }
            catch { await probe.DisposeAsync(); throw; }
        }
        public Process OpenWorker()
        {
            var worker = Process.GetProcessById(started.WorkerPid!.Value);
            Assert.Equal(started.WorkerStartedAt!.Value.UtcDateTime, worker.StartTime.ToUniversalTime()); return worker;
        }
        public Task CommandAsync(string command, CpuThermalFrame? thermal = null) =>
            Channel.WriteAsync(new SafetyProbeCommand(SafetyProbeProtocol.Version, Id, Nonce, ++sequence, command, thermal));
        public async Task<SafetyProbeEvent> ReadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var message = await Channel.ReadAsync<SafetyProbeEvent>(timeout.Token);
            Assert.Equal(Id, message.SessionId); Assert.Equal(SafetyProbeProtocol.Version, message.Version); Assert.True(message.IdleOnly); Assert.False(message.LoadExecuted); return message;
        }
        public async ValueTask DisposeAsync()
        {
            Pipe.Dispose();
            try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await Supervisor.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!Supervisor.HasExited) Supervisor.Kill(entireProcessTree: false); await Supervisor.WaitForExitAsync(); }
            finally { Supervisor.Dispose(); }
        }
    }
}
