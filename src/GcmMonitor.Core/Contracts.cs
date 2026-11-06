using System.Text.Json;

namespace GcmMonitor.Core;

public interface IDiagnosticCollector
{
    ModuleId Module { get; }
    ValueTask<ModuleRun> CheckCapabilityAsync(CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<Measurement>> CollectAsync(DateTimeOffset timestamp, int sample, CancellationToken cancellationToken);
}

public interface ISessionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DiagnosticSession session, CancellationToken cancellationToken = default);
    Task<DiagnosticSession?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiagnosticSession>> ListAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task SaveNotesAsync(Guid id, string notes, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> ClearTerminalAsync(CancellationToken cancellationToken = default);
    Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default);
    Task SaveBatchAsync(DiagnosticSession session, CollectionBatch batch, CancellationToken cancellationToken = default);
    Task<ProcessAggregate[]> ReadProcessRankingsAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IDiagnosticEngineClient : IAsyncDisposable
{
    IAsyncEnumerable<DiagnosticSession> RunAsync(DiagnosticOptions options, CancellationToken cancellationToken = default);
    Task CancelAsync(CancellationToken cancellationToken = default);
}

public interface IStressSupervisor
{
    bool IsAvailable { get; }
    Task StopAsync(CancellationToken cancellationToken = default);
}

public static class EngineProtocol
{
    public const int Version = 2;
    public const int MaxMessageLength = 1024 * 1024;
    public static JsonSerializerOptions Json { get; } = new() { PropertyNameCaseInsensitive = false };
    public static void Validate(int version, Guid sessionId, Guid expectedId)
    {
        if (version != Version || sessionId == Guid.Empty || sessionId != expectedId)
            throw new InvalidDataException("Versão ou identidade da sessão inválida.");
    }
}

public sealed record StartSessionMessage(int Version, Guid SessionId, DiagnosticOptions Options, CollectionMode Mode = CollectionMode.Passive);
public sealed record EngineMessage(int Version, Guid SessionId, long Sequence, DiagnosticSession Session);
public sealed record EngineCommand(int Version, Guid SessionId, string Command);
