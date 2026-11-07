using System.Security.Cryptography;
using System.Text;

namespace GcmMonitor.Core;

public enum StressPhase { Disabled, Blocked, AwaitingConsent, Baseline, Active, Recovery, Stopped, Cancelled, Interrupted, Error }
public sealed record StressPlan(int MonitoringMinutes, int StressMinutes, TimeSpan Before, TimeSpan Load, TimeSpan After)
{
    public TimeSpan MinimumDuration => Before + Load + After;
    public TimeSpan ProposedDuration => TimeSpan.FromMinutes(MonitoringMinutes) > MinimumDuration ? TimeSpan.FromMinutes(MonitoringMinutes) : MinimumDuration;
    public bool RequiresExtension => MinimumDuration > TimeSpan.FromMinutes(MonitoringMinutes);
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"gcm-stress-plan-v1:{MonitoringMinutes}:{StressMinutes}:{Before.Ticks}:{Load.Ticks}:{After.Ticks}"))));
    public void Validate()
    {
        if (this != Create(MonitoringMinutes, StressMinutes)) throw new ArgumentException("Fases divergentes do planejamento aprovado.");
    }
    /// <summary>Previsão do planejamento; não inicia nem autoriza carga.</summary>
    public StressPhase ExpectedPhaseAt(TimeSpan elapsed, TimeSpan? loadStoppedAt = null)
    {
        Validate();
        if (elapsed < TimeSpan.Zero || loadStoppedAt is { } stopped && (stopped < Before || stopped > Before + Load)) throw new ArgumentOutOfRangeException(nameof(elapsed));
        var loadEnd = loadStoppedAt ?? Before + Load;
        return elapsed < Before ? StressPhase.Baseline : elapsed < loadEnd ? StressPhase.Active :
            elapsed < loadEnd + After ? StressPhase.Recovery : StressPhase.Stopped;
    }

    public static StressPlan Create(int monitoringMinutes, int stressMinutes)
    {
        if (!DiagnosticDurations.Minutes.Contains(monitoringMinutes) || !DiagnosticDurations.Minutes.Contains(stressMinutes))
            throw new ArgumentException("As durações permitidas são 5 min, 30 min, 1 h e 4 h.");
        var (before, after) = stressMinutes switch { 5 => (30, 60), 30 => (60, 120), 60 => (120, 180), 240 => (180, 300), _ => throw new ArgumentException() };
        return new(monitoringMinutes, stressMinutes, TimeSpan.FromSeconds(before), TimeSpan.FromMinutes(stressMinutes), TimeSpan.FromSeconds(after));
    }
}

/// <summary>Consentimento específico de plano/sessão. Validá-lo não libera o recurso bloqueado.</summary>
public sealed record StressConsent(Guid SessionId, string PlanFingerprint, bool LoadAccepted, bool ExtensionAccepted,
    DateTimeOffset GrantedAt, Guid Nonce)
{
    public bool AppliesTo(Guid sessionId, StressPlan plan, DateTimeOffset now)
    {
        plan.Validate();
        return sessionId != Guid.Empty && SessionId == sessionId && Nonce != Guid.Empty && LoadAccepted &&
            (!plan.RequiresExtension || ExtensionAccepted) && PlanFingerprint == plan.Fingerprint &&
            GrantedAt <= now && now - GrantedAt <= TimeSpan.FromMinutes(5);
    }
}

public sealed class StressConsentLedger
{
    private bool consumed;
    private readonly object gate = new();
    public bool TryConsume(Guid sessionId, StressPlan plan, StressConsent consent, DateTimeOffset now)
    {
        lock (gate)
        {
            if (consumed || !consent.AppliesTo(sessionId, plan, now)) return false;
            consumed = true; return true;
        }
    }
}

public sealed record StressAssessment(bool Requested, bool Available, StressPhase Phase, string[] Reasons,
    DateTimeOffset Timestamp, bool LoadExecuted = false);
public static class StressReleasePolicy
{
    // Sem configuração, variável de ambiente ou mensagem IPC capaz de contornar a pendência de validação.
    public static bool IsReleased => false;
    public static StressAssessment Evaluate(bool requested, DateTimeOffset timestamp) => new(requested, false,
        requested ? StressPhase.Blocked : StressPhase.Disabled,
        ["Estresse bloqueado nesta versão de desenvolvimento.",
         "Sensores térmicos essenciais e limites por hardware não foram validados para proteção.",
         "A matriz P0 completa de estresse ainda não está aprovada em laboratório. Provas com processos ociosos não a substituem."], timestamp);
}

public enum SafetyStopReason { DurationCompleted, UserStopped, Cancelled, HeartbeatLost, ControllerDisconnected,
    SensorLost, Suspended, PowerChanged, PersistenceFailed, InvalidProtocol, WorkerExited, SupervisorFailed }

/// <summary>Contrato exclusivo da prova ociosa de contenção. Não transporta autorização de carga.</summary>
public static class SafetyProbeProtocol
{
    public const int Version = 1;
    public const int MaximumBytes = 16384;
    public static TimeSpan HeartbeatTimeout => TimeSpan.FromSeconds(3);
    public const string Purpose = "IdleContainmentProbe";
    public static void Validate(int version, Guid id, Guid expected, string? nonce, string? expectedNonce)
    {
        if (version != Version || id == Guid.Empty || id != expected || string.IsNullOrEmpty(nonce) || nonce != expectedNonce || nonce.Length != 64)
            throw new InvalidDataException("Protocolo ou identidade da prova de contenção inválido.");
    }
}
public sealed record SafetyProbeStart(int Version, Guid SessionId, string Nonce, string Purpose, int DurationMilliseconds);
public sealed record SafetyProbeCommand(int Version, Guid SessionId, string Nonce, long Sequence, string Command);
public sealed record SafetyProbeEvent(int Version, Guid SessionId, long Sequence, string State,
    int? WorkerPid = null, DateTimeOffset? WorkerStartedAt = null, SafetyStopReason? Reason = null,
    bool IdleOnly = true, bool LoadExecuted = false);
