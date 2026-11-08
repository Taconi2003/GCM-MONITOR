using System.Diagnostics;
using System.Text.Json;
using GcmMonitor.Collector;
using GcmMonitor.Core;

namespace GcmMonitor.UnitTests;

public sealed class CpuThermalTests
{
    private static readonly Guid Id = Guid.Parse("27fc37c0-33c2-4667-bf7f-00a87f091528");
    private static readonly DateTimeOffset Time = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static CpuThermalSensor Sensor => new("/intelcpu/0", "/intelcpu/0/temperature/0", "CPU Package", CpuThermalRole.Package, 64, 100);
    private static CpuThermalFrame Frame => new(1, Id, 1, 0, 100, CpuThermalStatus.Reading, [Sensor]);
    private static long Counter(long milliseconds) => Stopwatch.Frequency + Stopwatch.Frequency * milliseconds / 1000;

    [Theory]
    [InlineData("CPU Package", CpuThermalRole.Package)]
    [InlineData("Core #1", CpuThermalRole.Core)]
    [InlineData("P-Core #2", CpuThermalRole.Core)]
    [InlineData("E-Core #8", CpuThermalRole.Core)]
    [InlineData("Core Max", null)]
    [InlineData("Core Average", null)]
    [InlineData("P-Core #1 Distance to TjMax", null)]
    [InlineData("Core #0", null)]
    [InlineData("Unknown", null)]
    public void OnlyReviewedDirectSensorsEnterTheProtocol(string name, CpuThermalRole? expected) =>
        Assert.Equal(expected, CpuThermalProtocol.ClassifyIntel(name));

    [Fact]
    public void QueryAgeIncludesTimeBeforeReceiptAndNeverQualifiesSafety()
    {
        var buffer = new CpuThermalBuffer(Id);
        buffer.Accept(new(Frame, Counter(0), Counter(100)), Counter(2000), Time);
        Assert.True(buffer.Read(Counter(6000)).Available);
        Assert.False(buffer.Read(Counter(6001)).Available);
        Assert.False(buffer.Read(Counter(1000)).Available);
        Assert.Equal(Time, buffer.Read(Counter(3000)).ReceivedAt);
        Assert.False(buffer.Read(Counter(3000)).Frame!.SafetyQualified);
    }

    [Theory]
    [InlineData(0, 100, 6001)]
    [InlineData(900, 1100, 1000)]
    [InlineData(1000, 900, 2000)]
    [InlineData(-1000, -900, 500)]
    public void AQueuedOrInconsistentQueryIsRejectedEvenWithValidSequence(long start, long end, long receipt)
    {
        var buffer = new CpuThermalBuffer(Id);
        var invalid = new CpuThermalTransfer(Frame, Counter(start), Counter(end));
        Assert.Throws<InvalidDataException>(() => buffer.Accept(invalid, Counter(receipt), Time));
        Assert.False(buffer.Read(Counter(receipt)).Available);
        Assert.Throws<InvalidDataException>(() => buffer.Accept(new(Frame, Counter(7000), Counter(7100)), Counter(7200), Time));
    }

    [Fact]
    public void HostQueryWindowsCannotOverlapEvenWhenRelativeSequenceIsValid()
    {
        var buffer = new CpuThermalBuffer(Id);
        buffer.Accept(new(Frame, Counter(0), Counter(100)), Counter(110), Time);
        var next = Frame with { Sequence = 2, ReadStartedMilliseconds = 2000, ReadCompletedMilliseconds = 2100 };
        Assert.Throws<InvalidDataException>(() => buffer.Accept(new(next, Counter(99), Counter(200)), Counter(210), Time));
        Assert.False(buffer.Read(Counter(210)).Available);
    }

    [Fact]
    public void HandshakeRefusesPreviousBridgeWithoutSharedQueryTimestamps()
    {
        string nonce = new('A', 64);
        CpuThermalProtocol.ValidateIdentity(CpuThermalProtocol.BridgeVersion, Id, Id, nonce, nonce);
        Assert.Throws<InvalidDataException>(() => CpuThermalProtocol.ValidateIdentity(1, Id, Id, nonce, nonce));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ReplayOrSequenceGapLatchesFaultAndPreservesOldFrameForUnavailableSeries(long sequence)
    {
        var buffer = new CpuThermalBuffer(Id);
        buffer.Accept(new(Frame, Counter(0), Counter(100)), Counter(110), Time);
        var next = Frame with { Sequence = sequence, ReadStartedMilliseconds = 2000, ReadCompletedMilliseconds = 2100 };
        Assert.Throws<InvalidDataException>(() => buffer.Accept(new(next, Counter(2000), Counter(2100)), Counter(2110), Time.AddSeconds(2)));
        Assert.False(buffer.Read(Counter(2200)).Available);
        Assert.Equal(64, buffer.Read(Counter(2200)).Frame!.Sensors.Single().Celsius);
        Assert.Throws<InvalidDataException>(() => buffer.Accept(new(next with { Sequence = 2 }, Counter(3000), Counter(3100)), Counter(3110), Time.AddSeconds(3)));
    }

    [Fact]
    public void InvalidIdentityMetadataDurationOrSafetyAssertionIsRejected()
    {
        CpuThermalFrame[] invalid =
        [
            Frame with { Version = 99 }, Frame with { SessionId = Guid.NewGuid() }, Frame with { SafetyQualified = true },
            Frame with { ReadCompletedMilliseconds = 6001 }, Frame with { ReadStartedMilliseconds = double.NaN },
            Frame with { Sensors = [Sensor, Sensor] }, Frame with { Status = CpuThermalStatus.SourceFailed },
            Frame with { Sensors = [Sensor, Sensor with { SensorId = "/intelcpu/0/temperature/1" }] },
            Frame with { Sensors = [Sensor with { Celsius = double.NaN }] },
            Frame with { Sensors = [Sensor with { Celsius = -273.15 }] },
            Frame with { Sensors = [Sensor with { Celsius = 101 }] },
            Frame with { Sensors = [Sensor with { TjMaxCelsius = null }] },
            Frame with { Sensors = [Sensor with { Name = "Core Max" }] },
            Frame with { Sensors = [Sensor with { Name = "Core #1 Distance to TjMax" }] },
            Frame with { Sensors = [Sensor with { SensorId = "/intelcpu/1/temperature/0" }] }
        ];
        Assert.All(invalid, frame => Assert.Throws<InvalidDataException>(() => CpuThermalProtocol.Validate(frame, Id, 0, 0)));
        CpuThermalProtocol.Validate(Frame with { Sensors = [Sensor with { Celsius = null, TjMaxCelsius = null }] }, Id, 0, 0);
        // Zero explícito na API nullable não tem a ambiguidade do WMI legado.
        CpuThermalProtocol.Validate(Frame with { Sensors = [Sensor with { Celsius = 0 }] }, Id, 0, 0);
    }

    [Fact]
    public void BufferCopiesArraysAndRejectsUnknownJsonMembers()
    {
        var buffer = new CpuThermalBuffer(Id); var frame = Frame;
        buffer.Accept(new(frame, Counter(0), Counter(100)), Counter(110), Time); frame.Sensors[0] = Sensor with { Celsius = 99 };
        var snapshot = buffer.Read(Counter(110)); snapshot.Frame!.Sensors[0] = Sensor with { Celsius = 98 };
        Assert.Equal(64, buffer.Read(Counter(110)).Frame!.Sensors.Single().Celsius);
        string json = JsonSerializer.Serialize(Frame, CpuThermalProtocol.Json);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CpuThermalFrame>(json[..^1] + ",\"StartLoad\":true}", CpuThermalProtocol.Json));
    }

    [Fact]
    public async Task RepeatedLostAndMissingReadingsCreateGapsInsteadOfRepeatingTemperature()
    {
        var feed = new Feed(new(Frame, Time, true, "")); var source = new CpuThermalCollection(feed);
        var first = await source.SampleAsync(Time, default);
        Assert.Equal(64, Assert.Single(first.Measurements).Value);
        Assert.Equal(Time, first.Measurements[0].Timestamp);
        var repeated = await source.SampleAsync(Time.AddSeconds(5), default);
        Assert.Null(Assert.Single(repeated.Measurements).Value);
        feed.Snapshot = new(Frame with { Sequence = 2, Sensors = [Sensor with { Celsius = null }] }, Time.AddSeconds(6), true, "");
        Assert.Null(Assert.Single((await source.SampleAsync(Time.AddSeconds(6), default)).Measurements).Value);
        feed.Snapshot = new(Frame with { Sequence = 3, Sensors = [] }, Time.AddSeconds(8), true, "");
        Assert.Null(Assert.Single((await source.SampleAsync(Time.AddSeconds(8), default)).Measurements).Value);
        feed.Snapshot = new(Frame, Time, false, "Conexão perdida.");
        var lost = await source.SampleAsync(Time.AddSeconds(10), default);
        Assert.Null(Assert.Single(lost.Measurements).Value);
        Assert.Equal(DataQuality.Unavailable, lost.Measurements[0].Quality);
        Assert.Contains("Conexão perdida", lost.Records[0].Detail);
        Assert.False(StressReleasePolicy.IsReleased);
    }

    private sealed class Feed(CpuThermalSnapshot snapshot) : ICpuThermalFeed
    {
        public CpuThermalSnapshot Snapshot { get; set; } = snapshot;
        public Task<CpuThermalSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(Snapshot);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
