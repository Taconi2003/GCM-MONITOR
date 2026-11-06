using System.Buffers.Binary;
using GcmMonitor.Collector;
using GcmMonitor.Core;

namespace GcmMonitor.UnitTests;

public sealed class ContractTests
{
    [Fact]
    public void CatalogHasExactlyElevenDistinctModulesWithoutStress()
    {
        Assert.Equal(11, ModuleCatalog.All.Count);
        Assert.Equal(11, ModuleCatalog.All.Select(x => x.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 11).Select(x => $"M{x:00}"), ModuleCatalog.All.Select(x => x.Code));
    }
    [Fact] public void DurationsAreExactlyTheApprovedOptions() => Assert.Equal(new[] { 5, 30, 60, 240 }, DiagnosticDurations.Minutes);
    [Fact]
    public void StressIsOffByDefaultAndExplicitActivationIsRejected()
    {
        var options = new DiagnosticOptions(); Assert.False(options.StressEnabled); options.Validate();
        Assert.Throws<ArgumentException>(() => (options with { StressEnabled = true }).Validate());
    }
    [Fact] public void EmptySelectionIsRejected() => Assert.Throws<ArgumentException>(() => new DiagnosticOptions { Modules = [] }.Validate());
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(120)]
    public void UnsupportedDurationIsRejected(int minutes) => Assert.Throws<ArgumentException>(() => new DiagnosticOptions { MonitoringMinutes = minutes }.Validate());
    [Fact] public void DetailedWifiRequiresWifiModule() => Assert.Throws<ArgumentException>(() => new DiagnosticOptions { Modules = [ModuleId.Cpu], DetailedWifi = true }.Validate());
    [Fact] public void StorageRequiresSelectedPhysicalDisk() => Assert.Throws<ArgumentException>(() => new DiagnosticOptions { DiskIds = [] }.Validate());
    [Fact]
    public void UnknownAndDuplicateModulesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticOptions { Modules = [(ModuleId)99] }.Validate());
        Assert.Throws<ArgumentException>(() => new DiagnosticOptions { Modules = [ModuleId.Cpu, ModuleId.Cpu] }.Validate());
    }
    [Fact]
    public void SessionTransitionsRecordReasonsAndTerminalSessionsCannotResume()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new DiagnosticSession().TransitionTo(SessionState.Validating, "test", now)
            .TransitionTo(SessionState.Running, "test", now).TransitionTo(SessionState.Cancelled, "cancel", now);
        Assert.True(session.IsTerminal); Assert.Equal(now, session.EndedAt); Assert.Equal(3, session.Transitions.Length);
        Assert.Equal("cancel", session.Transitions[^1].Reason);
        Assert.Throws<InvalidOperationException>(() => session.TransitionTo(SessionState.Running, "invalid", now));
    }
    [Fact]
    public void RunningCannotSkipFinalizationToComplete()
    {
        var session = new DiagnosticSession { State = SessionState.Running };
        Assert.Throws<InvalidOperationException>(() => session.TransitionTo(SessionState.Completed, "invalid", DateTimeOffset.UtcNow));
    }
    [Fact]
    public void UnavailableSensorHasNoInventedValue()
    {
        var measurement = new Measurement(ModuleId.Sensors, "CPU", "sensor", null, "°C", DateTimeOffset.UtcNow, "fixture", DataQuality.Unavailable, true, "not exposed");
        measurement.Validate(); Assert.Null(measurement.Value);
        Assert.Throws<ArgumentException>(() => (measurement with { Value = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (measurement with { Quality = DataQuality.Valid }).Validate());
    }
    [Fact]
    public async Task SyntheticCollectorIsDeterministicAndMarksEverySample()
    {
        var collector = new DemoCollector(ModuleId.Cpu); var now = DateTimeOffset.UtcNow;
        var first = await collector.CollectAsync(now, 1, default); var second = await collector.CollectAsync(now, 1, default);
        Assert.Equal(first, second); Assert.All(first, x => Assert.True(x.IsDemo));
        var sensors = await new DemoCollector(ModuleId.Sensors).CollectAsync(now, 1, default);
        Assert.All(sensors, x => { Assert.Null(x.Value); Assert.Equal(DataQuality.Unavailable, x.Quality); });
    }
    [Fact]
    public void ProtocolRejectsWrongVersionAndIdentity()
    {
        var id = Guid.NewGuid(); EngineProtocol.Validate(1, id, id);
        Assert.Throws<InvalidDataException>(() => EngineProtocol.Validate(2, id, id));
        Assert.Throws<InvalidDataException>(() => EngineProtocol.Validate(1, Guid.NewGuid(), id));
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(262145)]
    public async Task ChannelRejectsInvalidSizesBeforeReadingPayload(int size)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, size);
        await Assert.ThrowsAsync<InvalidDataException>(() => new EngineChannel(new MemoryStream(header)).ReadAsync<EngineCommand>());
    }
    [Fact]
    public async Task ChannelRoundTripsOnlyStructuredData()
    {
        using var stream = new MemoryStream(); var channel = new EngineChannel(stream);
        var message = new EngineCommand(1, Guid.NewGuid(), "cancel"); await channel.WriteAsync(message);
        stream.Position = 0; Assert.Equal(message, await channel.ReadAsync<EngineCommand>());
    }
}
