using System.Buffers.Binary;
using GcmMonitor.Core;

namespace GcmMonitor.UnitTests;

public sealed class StressSafetyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(5, 30, 60, 390)]
    [InlineData(30, 60, 120, 1980)]
    [InlineData(60, 120, 180, 3900)]
    [InlineData(240, 180, 300, 14880)]
    public void PlansMatchApprovedObservationWindows(int minutes, int before, int after, int total)
    {
        var plan = StressPlan.Create(minutes, minutes);
        Assert.Equal(TimeSpan.FromSeconds(before), plan.Before); Assert.Equal(TimeSpan.FromSeconds(after), plan.After);
        Assert.Equal(TimeSpan.FromSeconds(total), plan.MinimumDuration); Assert.True(plan.RequiresExtension);
        Assert.Equal(plan.MinimumDuration, plan.ProposedDuration);
    }
    [Fact]
    public void LongEnoughMonitoringDoesNotRequireExtensionOrAddVisibleDurationOptions()
    {
        var plan = StressPlan.Create(30, 5);
        Assert.False(plan.RequiresExtension); Assert.Equal(TimeSpan.FromMinutes(30), plan.ProposedDuration);
        Assert.Equal(new[] { 5, 30, 60, 240 }, DiagnosticDurations.Minutes);
    }
    [Theory]
    [InlineData(10, 5)]
    [InlineData(5, 0)]
    public void UnapprovedDurationsAreRejected(int monitoring, int stress) => Assert.Throws<ArgumentException>(() => StressPlan.Create(monitoring, stress));
    [Fact]
    public void ConsentMustMatchSessionPlanExtensionAndExpirationAndCanBeConsumedOnlyOnce()
    {
        var id = Guid.NewGuid(); var plan = StressPlan.Create(5, 5);
        var consent = new StressConsent(id, plan.Fingerprint, true, true, Now, Guid.NewGuid());
        Assert.True(consent.AppliesTo(id, plan, Now));
        Assert.False((consent with { ExtensionAccepted = false }).AppliesTo(id, plan, Now));
        Assert.False((consent with { LoadAccepted = false }).AppliesTo(id, plan, Now));
        Assert.False((consent with { Nonce = Guid.Empty }).AppliesTo(id, plan, Now));
        Assert.False(consent.AppliesTo(Guid.NewGuid(), plan, Now));
        Assert.False(consent.AppliesTo(id, StressPlan.Create(30, 5), Now));
        Assert.False(consent.AppliesTo(id, plan, Now.AddMinutes(6)));
        Assert.False(consent.AppliesTo(id, plan, Now.AddSeconds(-1)));
        var ledger = new StressConsentLedger(); Assert.True(ledger.TryConsume(id, plan, consent, Now));
        Assert.False(ledger.TryConsume(id, plan, consent, Now));
        Assert.False(StressReleasePolicy.IsReleased); // Consentimento não é certificado de segurança.
    }
    [Fact]
    public void AlteredPhaseWindowsCannotBypassTheApprovedPlan()
    {
        var plan = StressPlan.Create(5, 5) with { Before = TimeSpan.Zero };
        Assert.Throws<ArgumentException>(plan.Validate);
    }
    [Fact]
    public void PlannedPhasesAndEarlyStopIncludeRecoveryWithoutCancellingPassiveMonitoring()
    {
        var plan = StressPlan.Create(30, 5);
        Assert.Equal(StressPhase.Baseline, plan.ExpectedPhaseAt(TimeSpan.Zero));
        Assert.Equal(StressPhase.Active, plan.ExpectedPhaseAt(TimeSpan.FromSeconds(30)));
        Assert.Equal(StressPhase.Recovery, plan.ExpectedPhaseAt(TimeSpan.FromSeconds(330)));
        Assert.Equal(StressPhase.Stopped, plan.ExpectedPhaseAt(TimeSpan.FromSeconds(390)));
        Assert.Equal(StressPhase.Recovery, plan.ExpectedPhaseAt(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90)));
        Assert.Equal(StressPhase.Stopped, plan.ExpectedPhaseAt(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(90)));
        Assert.Equal(TimeSpan.FromMinutes(30), plan.ProposedDuration);
    }
    [Fact]
    public void AllReleaseAssessmentsRemainBlockedRegardlessOfRequestAndConsent()
    {
        foreach (bool requested in new[] { false, true })
        {
            var assessment = StressReleasePolicy.Evaluate(requested, Now);
            Assert.False(assessment.Available); Assert.False(assessment.LoadExecuted); Assert.NotEmpty(assessment.Reasons);
            Assert.Equal(requested ? StressPhase.Blocked : StressPhase.Disabled, assessment.Phase);
        }
        Assert.Throws<ArgumentException>(() => new DiagnosticOptions { StressEnabled = true }.Validate());
    }
    [Fact]
    public void ProbeProtocolRejectsNullNonceWrongSessionAndWrongVersion()
    {
        var id = Guid.NewGuid(); string nonce = new('A', 64);
        SafetyProbeProtocol.Validate(1, id, id, nonce, nonce);
        Assert.Throws<InvalidDataException>(() => SafetyProbeProtocol.Validate(1, id, id, null, null));
        Assert.Throws<InvalidDataException>(() => SafetyProbeProtocol.Validate(2, id, id, nonce, nonce));
        Assert.Throws<InvalidDataException>(() => SafetyProbeProtocol.Validate(1, Guid.NewGuid(), id, nonce, nonce));
    }
    [Fact]
    public async Task ProbeMessageLimitRejectsOversizeBeforePayloadRead()
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, SafetyProbeProtocol.MaximumBytes + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => new EngineChannel(new MemoryStream(bytes), SafetyProbeProtocol.MaximumBytes).ReadAsync<SafetyProbeCommand>());
    }
}
