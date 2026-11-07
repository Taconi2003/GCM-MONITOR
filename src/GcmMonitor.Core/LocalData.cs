using System.Reflection;

namespace GcmMonitor.Core;

public static class ProductVersion
{
    public static string Value { get; } = typeof(ProductVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}

public sealed record UserPreferences(bool InformativeNotifications, string ReportFolder, int RetentionDays)
{
    public void Validate()
    {
        if (ReportFolder is null || ReportFolder.Length > 4096 || !Path.IsPathFullyQualified(ReportFolder) ||
            ReportFolder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || RetentionDays is not (0 or 30 or 90 or 365))
            throw new InvalidDataException("Preferências locais inválidas.");
    }
}

public interface IUserPreferencesStore
{
    Task<UserPreferences?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(UserPreferences preferences, CancellationToken ct = default);
}

public sealed record HistoryQuery(int Page = 0, string Computer = "", SessionState? State = null,
    DateTimeOffset? FromUtc = null, DateTimeOffset? UntilUtc = null)
{
    public const int PageSize = 25;
    public void Validate()
    {
        if (Page < 0 || Computer is null || Computer.Length > 256 || State is { } state && !Enum.IsDefined(state) ||
            FromUtc.HasValue != UntilUtc.HasValue || FromUtc >= UntilUtc)
            throw new ArgumentException("Filtro de histórico inválido.");
    }
}

public sealed record HistoryEntry(Guid Id, string ComputerName, DateTimeOffset StartedAt, TimeSpan EffectiveDuration,
    SessionState State, bool IsDemo)
{
    public bool IsTerminal => State is SessionState.Completed or SessionState.Cancelled or SessionState.Interrupted or SessionState.Error;
    public static HistoryEntry FromSession(DiagnosticSession session) => new(session.Id, session.ComputerName,
        session.StartedAt, session.EffectiveDuration, session.State, session.IsDemo);
}

public sealed record HistoryPage(HistoryEntry[] Entries, int MatchingCount, int TotalCount, int ActiveCount, long? DatabaseBytes);
