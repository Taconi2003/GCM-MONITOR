using System.Text.Json;
using GcmMonitor.Core;
using Microsoft.Data.Sqlite;

namespace GcmMonitor.Persistence;

/// <summary>Snapshots limitados para a UI; amostras, módulos e agregados duráveis em tabelas próprias.</summary>
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
            CREATE TABLE IF NOT EXISTS Measurements (
                Id INTEGER PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
                Module INTEGER NOT NULL, Metric TEXT NOT NULL, Component TEXT NOT NULL,
                Timestamp TEXT NOT NULL, Value REAL, Unit TEXT NOT NULL, Source TEXT NOT NULL,
                Quality INTEGER NOT NULL, IsDemo INTEGER NOT NULL, Reason TEXT);
            CREATE INDEX IF NOT EXISTS IX_Measurements_SessionMetricTime ON Measurements(SessionId,Metric,Component,Timestamp);
            CREATE TABLE IF NOT EXISTS ModuleRuns (
                SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE, Module INTEGER NOT NULL,
                State INTEGER NOT NULL, Payload TEXT NOT NULL, PRIMARY KEY(SessionId,Module));
            CREATE TABLE IF NOT EXISTS Findings (
                SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE, Ordinal INTEGER NOT NULL,
                Payload TEXT NOT NULL, PRIMARY KEY(SessionId,Ordinal));
            CREATE TABLE IF NOT EXISTS CollectionIssues (
                Id INTEGER PRIMARY KEY, SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
                Timestamp TEXT NOT NULL, Payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ProcessAggregates (
                SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE, Pid INTEGER NOT NULL,
                StartedAt TEXT NOT NULL, CpuSeconds REAL NOT NULL, CpuObservedSeconds REAL NOT NULL,
                MemoryMean REAL, Payload TEXT NOT NULL, PRIMARY KEY(SessionId,Pid,StartedAt));
            CREATE INDEX IF NOT EXISTS IX_ProcessCpu ON ProcessAggregates(SessionId,CpuSeconds DESC);
            CREATE INDEX IF NOT EXISTS IX_ProcessMemory ON ProcessAggregates(SessionId,MemoryMean DESC);
            PRAGMA user_version=2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task SaveAsync(DiagnosticSession session, CancellationToken cancellationToken = default)
        => SaveBatchAsync(session, new([], [], []), cancellationToken);

    public async Task SaveBatchAsync(DiagnosticSession session, CollectionBatch batch, CancellationToken cancellationToken = default)
    {
        session.Options.Validate();
        if (session.Measurements.Concat(batch.Measurements).Any(x => x.IsDemo != session.IsDemo))
            throw new InvalidOperationException("Dados reais e sintéticos não podem ser misturados na mesma sessão.");
        foreach (var measurement in session.Measurements) measurement.Validate();
        foreach (var measurement in batch.Measurements) measurement.Validate();
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
        async Task Execute(string sql, params (string Name, object? Value)[] parameters)
        {
            using var statement = connection.CreateCommand(); statement.Transaction = transaction; statement.CommandText = sql;
            foreach (var parameter in parameters) statement.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            await statement.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var sample in batch.Measurements)
            await Execute("INSERT INTO Measurements(SessionId,Module,Metric,Component,Timestamp,Value,Unit,Source,Quality,IsDemo,Reason) VALUES($id,$module,$metric,$component,$time,$value,$unit,$source,$quality,$demo,$reason);",
                ("$id", session.Id.ToString()), ("$module", (int)sample.Module), ("$metric", sample.Metric), ("$component", sample.Component),
                ("$time", sample.Timestamp.UtcDateTime.ToString("O")), ("$value", sample.Value), ("$unit", sample.Unit), ("$source", sample.Source),
                ("$quality", (int)sample.Quality), ("$demo", sample.IsDemo ? 1 : 0), ("$reason", sample.Reason));
        foreach (var module in session.Modules)
            await Execute("INSERT INTO ModuleRuns VALUES($id,$module,$state,$payload) ON CONFLICT(SessionId,Module) DO UPDATE SET State=$state,Payload=$payload;",
                ("$id", session.Id.ToString()), ("$module", (int)module.Id), ("$state", (int)module.State), ("$payload", JsonSerializer.Serialize(module)));
        for (int i = 0; i < session.Findings.Length; i++)
            await Execute("INSERT INTO Findings VALUES($id,$ordinal,$payload) ON CONFLICT(SessionId,Ordinal) DO UPDATE SET Payload=$payload;",
                ("$id", session.Id.ToString()), ("$ordinal", i), ("$payload", JsonSerializer.Serialize(session.Findings[i])));
        foreach (var issue in batch.Issues)
            await Execute("INSERT INTO CollectionIssues(SessionId,Timestamp,Payload) VALUES($id,$time,$payload);",
                ("$id", session.Id.ToString()), ("$time", issue.Timestamp.UtcDateTime.ToString("O")), ("$payload", JsonSerializer.Serialize(issue)));
        foreach (var process in batch.Processes)
            await Execute("""
                INSERT INTO ProcessAggregates VALUES($id,$pid,$start,$cpu,$observed,$memory,$payload)
                ON CONFLICT(SessionId,Pid,StartedAt) DO UPDATE SET CpuSeconds=$cpu,CpuObservedSeconds=$observed,MemoryMean=$memory,Payload=$payload;
                """, ("$id", session.Id.ToString()), ("$pid", process.Pid), ("$start", process.StartedAt.UtcDateTime.ToString("O")),
                ("$cpu", process.CpuSeconds), ("$observed", process.CpuObservedSeconds), ("$memory", process.WorkingSetMeanBytes), ("$payload", JsonSerializer.Serialize(process)));
        transaction.Commit();
    }

    public async Task<ProcessAggregate[]> ReadProcessRankingsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Payload FROM (SELECT Payload FROM ProcessAggregates WHERE SessionId=$id AND CpuObservedSeconds>0 ORDER BY CpuSeconds DESC,Pid LIMIT 10)
            UNION SELECT Payload FROM (SELECT Payload FROM ProcessAggregates WHERE SessionId=$id AND MemoryMean IS NOT NULL ORDER BY MemoryMean DESC,Pid LIMIT 10);
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        var items = new List<ProcessAggregate>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(JsonSerializer.Deserialize<ProcessAggregate>(reader.GetString(0)) ?? throw new InvalidDataException("Agregado de processo inválido."));
        return items.ToArray();
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
        // Chamar somente quando o controlador confirmou que não há um motor vivo da sessão.
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Snapshot, Notes FROM Sessions WHERE Terminal=0;";
        var sessions = new List<DiagnosticSession>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) sessions.Add(Read(reader));
        int recovered = 0;
        foreach (var session in sessions)
        {
            if (session.EngineProcessId is { } pid && session.EngineProcessStartedAt is { } expected)
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    if (!process.HasExited && process.StartTime.ToUniversalTime() == expected.UtcDateTime) continue;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { continue; } // Identidade incerta: não declarar interrupção indevidamente.
            }
            var interrupted = session.State == SessionState.NotStarted
                ? session.TransitionTo(SessionState.Validating, "Recuperação de sessão", DateTimeOffset.UtcNow) : session;
            interrupted = interrupted.TransitionTo(SessionState.Interrupted, "Motor da sessão não está ativo; continuidade não presumida.", DateTimeOffset.UtcNow);
            await SaveAsync(interrupted with
            {
                Modules = interrupted.Modules.Select(x => x.State == ModuleState.Running ? x with { State = ModuleState.Partial } : x.State == ModuleState.Pending ? x with { State = ModuleState.Cancelled } : x).ToArray(),
                Processes = await ReadProcessRankingsAsync(session.Id, cancellationToken)
            }, cancellationToken);
            recovered++;
        }
        return recovered;
    }
}
