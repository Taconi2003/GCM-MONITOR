using System.Text.Json;
using GcmMonitor.Core;
using Microsoft.Data.Sqlite;

namespace GcmMonitor.Persistence;

/// <summary>Fundação v0.1: snapshots sintéticos, transições e notas. Telemetria normalizada na v0.2.</summary>
public sealed class SqliteSessionRepository(string databasePath) : ISessionRepository
{
    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        try
        {
            await connection.OpenAsync(ct);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA wal_autocheckpoint=1000;";
            await command.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Sessions (
                Id TEXT PRIMARY KEY, StartedAt TEXT NOT NULL, Terminal INTEGER NOT NULL,
                Snapshot TEXT NOT NULL, Notes TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS IX_Sessions_StartedAt ON Sessions(StartedAt DESC);
            CREATE TABLE IF NOT EXISTS SessionTransitions (
                SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
                Ordinal INTEGER NOT NULL, Payload TEXT NOT NULL,
                PRIMARY KEY(SessionId, Ordinal));
            PRAGMA user_version=1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(DiagnosticSession session, CancellationToken cancellationToken = default)
    {
        session.Options.Validate();
        if (!session.IsDemo || session.Measurements.Any(x => !x.IsDemo))
            throw new InvalidOperationException("O armazenamento v0.1 aceita somente demonstrações.");
        foreach (var measurement in session.Measurements) measurement.Validate();
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Sessions(Id, StartedAt, Terminal, Snapshot, Notes) VALUES($id,$start,$terminal,$json,$notes)
            ON CONFLICT(Id) DO UPDATE SET Terminal=$terminal, Snapshot=$json;
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$start", session.StartedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$terminal", session.IsTerminal ? 1 : 0);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(session, EngineProtocol.Json));
        command.Parameters.AddWithValue("$notes", session.Notes);
        await command.ExecuteNonQueryAsync(cancellationToken);
        for (int i = 0; i < session.Transitions.Length; i++)
        {
            using var transition = connection.CreateCommand();
            transition.Transaction = transaction;
            transition.CommandText = "INSERT OR IGNORE INTO SessionTransitions VALUES($id,$ordinal,$payload);";
            transition.Parameters.AddWithValue("$id", session.Id.ToString());
            transition.Parameters.AddWithValue("$ordinal", i);
            transition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session.Transitions[i]));
            await transition.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task<DiagnosticSession?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Snapshot, Notes FROM Sessions WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<DiagnosticSession>> ListAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Snapshot, Notes FROM Sessions ORDER BY StartedAt DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<DiagnosticSession>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private static DiagnosticSession Read(SqliteDataReader reader) =>
        (JsonSerializer.Deserialize<DiagnosticSession>(reader.GetString(0), EngineProtocol.Json)
            ?? throw new InvalidDataException("Snapshot inválido.")) with
        { Notes = reader.GetString(1) };

    public async Task SaveNotesAsync(Guid id, string notes, CancellationToken cancellationToken = default)
    {
        if (notes.Length > 10000) throw new ArgumentException("Limite de 10.000 caracteres para observações.");
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Sessions SET Notes=$notes WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$notes", notes);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new KeyNotFoundException("Sessão não encontrada.");
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE Id=$id AND Terminal=1;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> ClearTerminalAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE Terminal=1;";
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        // Recuperação limitada às sessões sintéticas desta etapa; nunca retoma processos ou estresse.
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Snapshot, Notes FROM Sessions WHERE Terminal=0;";
        var sessions = new List<DiagnosticSession>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) sessions.Add(Read(reader));
        foreach (var session in sessions)
        {
            var interrupted = session.State == SessionState.NotStarted
                ? session.TransitionTo(SessionState.Validating, "Recuperação da demonstração", DateTimeOffset.UtcNow) : session;
            interrupted = interrupted.TransitionTo(SessionState.Interrupted, "Processo demonstrativo não está ativo; continuidade não presumida.", DateTimeOffset.UtcNow);
            await SaveAsync(interrupted with { Modules = interrupted.Modules.Select(x => x.State == ModuleState.Running ? x with { State = ModuleState.Partial } : x).ToArray() }, cancellationToken);
        }
        return sessions.Count;
    }
}
