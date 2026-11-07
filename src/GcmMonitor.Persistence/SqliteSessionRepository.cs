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
        command.CommandText = "PRAGMA journal_mode=WAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        int version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (version > 4) throw new InvalidDataException("Banco de dados criado por uma versão posterior do GCM Monitor.");
        command.CommandText = """
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
            CREATE TABLE IF NOT EXISTS DiagnosticRecords (
                SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE, Module INTEGER NOT NULL,
                RecordKey TEXT NOT NULL, Timestamp TEXT NOT NULL, Payload TEXT NOT NULL,
                PRIMARY KEY(SessionId,Module,RecordKey));
            CREATE TABLE IF NOT EXISTS SessionHistory (
                SessionId TEXT PRIMARY KEY REFERENCES Sessions(Id) ON DELETE CASCADE,
                StartedAt TEXT NOT NULL, ComputerName TEXT NOT NULL, State INTEGER NOT NULL,
                Duration TEXT NOT NULL, IsDemo INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_History_Date ON SessionHistory(StartedAt DESC,SessionId DESC);
            CREATE INDEX IF NOT EXISTS IX_History_StateDate ON SessionHistory(State,StartedAt DESC,SessionId DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (version < 4)
        {
            command.CommandText = """
                INSERT OR REPLACE INTO SessionHistory(SessionId,StartedAt,ComputerName,State,Duration,IsDemo)
                SELECT Id,StartedAt,coalesce(json_extract(Snapshot,'$.ComputerName'),'Não disponível'),
                    coalesce(json_extract(Snapshot,'$.State'),0),coalesce(json_extract(Snapshot,'$.EffectiveDuration'),'00:00:00'),
                    coalesce(json_extract(Snapshot,'$.IsDemo'),1) FROM Sessions;
                PRAGMA user_version=4;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public Task SaveAsync(DiagnosticSession session, CancellationToken cancellationToken = default)
        => SaveBatchAsync(session, new([], [], []), cancellationToken);

    public async Task SaveBatchAsync(DiagnosticSession session, CollectionBatch batch, CancellationToken cancellationToken = default)
    {
        session.Options.Validate();
        if (session.Stress is { } safety && (safety.Available || safety.LoadExecuted))
            throw new InvalidOperationException("Esta versão bloqueada não registra execução de carga como validada.");
        if (session.Measurements.Concat(batch.Measurements).Any(x => x.IsDemo != session.IsDemo))
            throw new InvalidOperationException("Dados reais e sintéticos não podem ser misturados na mesma sessão.");
        foreach (var measurement in session.Measurements) measurement.Validate();
        foreach (var measurement in batch.Measurements) measurement.Validate();
        foreach (var record in session.Records.Concat(batch.Records))
        {
            record.Validate();
            if (record.IsDemo != session.IsDemo) throw new InvalidOperationException("Origem do registro inconsistente.");
        }
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
        await Execute("""
            INSERT INTO SessionHistory(SessionId,StartedAt,ComputerName,State,Duration,IsDemo)
            VALUES($id,$started,$name,$state,$duration,$demo)
            ON CONFLICT(SessionId) DO UPDATE SET ComputerName=$name, State=$state, Duration=$duration, IsDemo=$demo;
            """, ("$id", session.Id.ToString()), ("$started", session.StartedAt.UtcDateTime.ToString("O")),
            ("$name", session.ComputerName), ("$state", (int)session.State), ("$duration", session.EffectiveDuration.ToString("c")), ("$demo", session.IsDemo ? 1 : 0));
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
        foreach (var record in batch.Records)
            await Execute("INSERT INTO DiagnosticRecords VALUES($id,$module,$key,$time,$payload) ON CONFLICT(SessionId,Module,RecordKey) DO UPDATE SET Timestamp=$time,Payload=$payload;",
                ("$id", session.Id.ToString()), ("$module", (int)record.Module), ("$key", record.Key),
                ("$time", record.Timestamp.UtcDateTime.ToString("O")), ("$payload", JsonSerializer.Serialize(record)));
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
        DiagnosticSession? session;
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            session = await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        if (session is null) return null;
        using var details = connection.CreateCommand();
        details.CommandText = "SELECT Payload FROM DiagnosticRecords WHERE SessionId=$id ORDER BY Module,Timestamp DESC,RecordKey LIMIT 4096;";
        details.Parameters.AddWithValue("$id", id.ToString());
        var records = new List<DiagnosticRecord>();
        using (var reader = await details.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) records.Add(JsonSerializer.Deserialize<DiagnosticRecord>(reader.GetString(0))!);
        return records.Count > 0 ? session with { Records = records.ToArray() } : session;
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

    public async Task<HistoryPage> QueryHistoryAsync(HistoryQuery query, CancellationToken ct = default)
    {
        query.Validate();
        await using var connection = await OpenAsync(ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        const string filter = """
            FROM SessionHistory WHERE ($name='' OR instr(lower(ComputerName),lower($name))>0)
            AND ($state IS NULL OR State=$state) AND ($start IS NULL OR (StartedAt >= $start AND StartedAt < $end))
            """;
        command.Parameters.AddWithValue("$name", query.Computer);
        command.Parameters.AddWithValue("$state", query.State is { } state ? (int)state : DBNull.Value);
        command.Parameters.AddWithValue("$start", (object?)query.FromUtc?.UtcDateTime.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$end", (object?)query.UntilUtc?.UtcDateTime.ToString("O") ?? DBNull.Value);
        command.CommandText = "SELECT (SELECT COUNT(*) " + filter + "), COUNT(*), coalesce(SUM(1-Terminal),0) FROM Sessions;";
        int matching, total, active;
        using (var counts = await command.ExecuteReaderAsync(ct))
        {
            await counts.ReadAsync(ct);
            matching = counts.GetInt32(0); total = counts.GetInt32(1); active = counts.GetInt32(2);
        }
        command.Parameters.AddWithValue("$limit", HistoryQuery.PageSize);
        command.Parameters.AddWithValue("$offset", (long)query.Page * HistoryQuery.PageSize);
        command.CommandText = "SELECT SessionId,ComputerName,StartedAt,Duration,State,IsDemo " + filter +
            " ORDER BY StartedAt DESC, SessionId DESC LIMIT $limit OFFSET $offset;";
        var entries = new List<HistoryEntry>();
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                entries.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture),
                    TimeSpan.ParseExact(reader.GetString(3), "c", System.Globalization.CultureInfo.InvariantCulture), (SessionState)reader.GetInt32(4), reader.GetBoolean(5)));
        }
        transaction.Commit();
        return new(entries.ToArray(), matching, total, active, DatabaseSize());
    }

    private long? DatabaseSize()
    {
        try
        {
            return new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" }
                .Select(path => new FileInfo(path)).Where(file => file.Exists).Sum(file => file.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task<int> DeleteTerminalBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE Terminal=1 AND StartedAt < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff.UtcDateTime.ToString("O"));
        return await command.ExecuteNonQueryAsync(ct);
    }

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
