using GcmMonitor.Core;

namespace GcmMonitor.UnitTests;

public sealed class ThermalRecoveryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly StressPlan Plan = StressPlan.Create(5, 5);

    [Theory]
    [InlineData(51, 29, 96.66666667)]
    [InlineData(71, 9, 30)]
    [InlineData(80, 0, 0)]
    public void ReportsDifferentCoolingCurvesWithoutClassifyingTheHardware(double final, double drop, double percentage)
    {
        var review = ThermalRecoveryAnalyzer.Analyze(Trace(final), Plan);
        Assert.Equal(50, review.Baseline.Average);
        Assert.Equal(86, review.Load.Peak);
        Assert.Equal(80, review.AtLoadEnd);
        Assert.Equal(drop, review.Drop);
        Assert.Equal(drop, review.CoolingPerMinute);
        Assert.Equal(percentage, review.RecoveredPercent!.Value, 5);
        Assert.Equal(final - 50, review.DifferenceFromBaseline);
        Assert.Empty(review.Limitations);
    }

    [Fact]
    public void RejectsFrozenOrMissingTemperaturesAndIncompleteObservation()
    {
        var trace = Trace(55);
        var points = trace.Points.ToArray();
        points[12] = points[12] with { CapturedAt = points[11].CapturedAt };
        points[13] = points[13] with { Celsius = null };
        trace = trace with { Points = points, ObservedUntil = Start.AddSeconds(65), Phase = StressPhase.Interrupted };
        var review = ThermalRecoveryAnalyzer.Analyze(trace, Plan);
        Assert.Null(review.RecoveredPercent);
        Assert.Null(review.DifferenceFromBaseline);
        Assert.Contains(review.Limitations, reason => reason.Contains("repetidas", StringComparison.Ordinal));
        Assert.Contains(review.Limitations, reason => reason.Contains("incompleta", StringComparison.Ordinal));
        Assert.Null(ThermalRecoveryAnalyzer.TemperatureSeries(trace).Single(p => p.ObservedAt == points[12].ObservedAt).Celsius);
    }

    [Fact]
    public void OmitsPercentageWithoutComparableBaselineOrPositiveIncrease()
    {
        var trace = Trace(47, atStop: 50);
        Assert.Null(ThermalRecoveryAnalyzer.Analyze(trace, Plan).RecoveredPercent);
        var nonComparable = ThermalRecoveryAnalyzer.Analyze(Trace(52) with { ComparableRecovery = false }, Plan);
        Assert.Null(nonComparable.RecoveredPercent);
        Assert.Equal(28, nonComparable.Drop);
    }

    [Fact]
    public void AcceptingAttentionDoesNotOverrideCriticalAndDoesNotRepeatSameWarning()
    {
        var alert = new ThermalAlertRehearsal(82, 93);
        Assert.Equal(ThermalAlertLevel.Attention, alert.Observe(Point(5, 85))!.Level);
        Assert.Equal(ThermalChoice.Continue, alert.Resolve(ThermalChoice.Continue, Start.AddSeconds(6))!.Choice);
        Assert.Null(alert.Observe(Point(10, 88)));
        Assert.Equal(ThermalAlertLevel.Critical, alert.Observe(Point(15, 94))!.Level);
        Assert.True(alert.MustStop);
        Assert.Null(alert.Resolve(ThermalChoice.Continue, Start.AddSeconds(16)));
        Assert.Null(alert.Observe(Point(20, 50)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MissingStaleOrUnsupervisedReadingRequiresStop(bool supervised, bool repeat)
    {
        var alert = new ThermalAlertRehearsal(82, 93);
        Assert.Null(alert.Observe(Point(5, 78)));
        var next = Point(10, repeat || !supervised ? 78 : null);
        if (repeat) next = next with { CapturedAt = Start.AddSeconds(5) };
        Assert.Equal(ThermalAlertLevel.Critical, alert.Observe(next, supervised)!.Level);
        Assert.True(alert.MustStop);
    }

    private static ThermalTrace Trace(double final, double atStop = 80)
    {
        var points = Enumerable.Range(0, 21).Select(i => Point(i * 5, i < 6 ? 50 : i < 8 ? 86 : atStop + (final - atStop) * (i - 8) / 12)).ToArray();
        return new(StressPhase.Stopped, "CPU Package fictício", "Fixture", Start, Start.AddSeconds(30), Start.AddSeconds(40),
            Start.AddSeconds(100), true, true, points, []);
    }

    private static ThermalPoint Point(int seconds, double? celsius) => new(Start.AddSeconds(seconds), Start.AddSeconds(seconds), celsius);
}
