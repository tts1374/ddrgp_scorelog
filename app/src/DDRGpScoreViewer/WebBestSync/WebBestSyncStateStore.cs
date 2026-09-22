using System.IO;
using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.WebBestSync;

internal interface IWebBestSyncStateStore
{
    WebBestSyncSnapshot Load();

    WebBestSyncSnapshot SetEnabled(bool enabled);

    WebBestSyncSnapshot RequestFullSnapshot();

    WebBestSyncSnapshot Reconcile(
        IReadOnlyList<PlayerChartBestProjectionV1> projections);

    WebBestSyncSnapshot MarkSynced(string chartId, string? sentHash);

    WebBestSyncSnapshot MarkDeferred(string chartId, string errorCode);

    WebBestSyncSnapshot CompleteSnapshot(
        IReadOnlyList<PlayerChartBestProjectionV1> projections,
        DateTimeOffset completedAt);

    WebBestSyncSnapshot RecordStatus(
        WebBestSyncStatus status,
        DateTimeOffset? lastSuccessfulSyncAt = null,
        int? retryAttempt = null,
        DateTimeOffset? nextRetryAt = null,
        string? errorCode = null);

    WebBestSyncSnapshot ClearAfterPublicDelete();
}

internal sealed class SqliteWebBestSyncStateStore : IWebBestSyncStateStore
{
    private readonly string path;

    public SqliteWebBestSyncStateStore(string path)
    {
        this.path = Path.GetFullPath(path);
        Initialize();
    }

    public WebBestSyncSnapshot Load()
    {
        using var connection = Open();
        return Read(connection);
    }

    public WebBestSyncSnapshot SetEnabled(bool enabled)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(
            transaction,
            """
            UPDATE web_best_sync_metadata
            SET enabled = $enabled,
                full_snapshot_required = CASE WHEN $enabled = 1 THEN 1 ELSE full_snapshot_required END,
                status = CASE WHEN $enabled = 1 THEN 'Dirty' ELSE 'Disabled' END,
                retry_attempt = 0,
                next_retry_at = NULL,
                last_error_code = NULL
            WHERE singleton_id = 1;
            """,
            ("$enabled", enabled ? 1 : 0));
        transaction.Commit();
        return Read(connection);
    }

    public WebBestSyncSnapshot RequestFullSnapshot()
    {
        using var connection = Open();
        Execute(
            connection,
            """
            UPDATE web_best_sync_metadata
            SET full_snapshot_required = 1,
                status = CASE WHEN enabled = 1 THEN 'Dirty' ELSE status END
            WHERE singleton_id = 1;
            """);
        return Read(connection);
    }

    public WebBestSyncSnapshot Reconcile(
        IReadOnlyList<PlayerChartBestProjectionV1> projections)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(
            transaction,
            "CREATE TEMP TABLE current_web_best_charts (chart_id TEXT PRIMARY KEY) STRICT;");
        var now = DateTimeOffset.UtcNow.ToString("O");
        foreach (var projection in projections)
        {
            var hash = WebBestProjectionContract.Hash(projection);
            Execute(
                transaction,
                "INSERT INTO current_web_best_charts (chart_id) VALUES ($chart_id);",
                ("$chart_id", projection.ChartId));
            Execute(
                transaction,
                """
                INSERT INTO web_best_sync_entries (
                  chart_id, desired_projection_hash, synced_projection_hash,
                  deferred_error, updated_at
                ) VALUES ($chart_id, $desired_hash, NULL, NULL, $updated_at)
                ON CONFLICT(chart_id) DO UPDATE SET
                  desired_projection_hash = excluded.desired_projection_hash,
                  deferred_error = CASE
                    WHEN web_best_sync_entries.desired_projection_hash IS excluded.desired_projection_hash
                      THEN web_best_sync_entries.deferred_error
                    ELSE NULL
                  END,
                  updated_at = excluded.updated_at;
                """,
                ("$chart_id", projection.ChartId),
                ("$desired_hash", hash),
                ("$updated_at", now));
        }
        Execute(
            transaction,
            """
            UPDATE web_best_sync_entries
            SET desired_projection_hash = NULL,
                deferred_error = NULL,
                updated_at = $updated_at
            WHERE chart_id NOT IN (SELECT chart_id FROM current_web_best_charts);
            """,
            ("$updated_at", now));
        Execute(
            transaction,
            "DELETE FROM web_best_sync_entries WHERE desired_projection_hash IS NULL AND synced_projection_hash IS NULL;");
        Execute(
            transaction,
            """
            UPDATE web_best_sync_metadata
            SET status = CASE
              WHEN enabled = 0 THEN 'Disabled'
              WHEN full_snapshot_required = 1 THEN 'Dirty'
              WHEN EXISTS (
                SELECT 1 FROM web_best_sync_entries
                WHERE desired_projection_hash IS NOT synced_projection_hash
              ) THEN 'Dirty'
              ELSE 'Idle'
            END
            WHERE singleton_id = 1;
            """);
        Execute(transaction, "DROP TABLE current_web_best_charts;");
        transaction.Commit();
        return Read(connection);
    }

    public WebBestSyncSnapshot MarkSynced(string chartId, string? sentHash)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (sentHash is null)
        {
            Execute(
                transaction,
                """
                UPDATE web_best_sync_entries
                SET synced_projection_hash = NULL, deferred_error = NULL,
                    updated_at = $updated_at
                WHERE chart_id = $chart_id AND desired_projection_hash IS NULL;
                """,
                ("$chart_id", chartId),
                ("$updated_at", DateTimeOffset.UtcNow.ToString("O")));
        }
        else
        {
            Execute(
                transaction,
                """
                UPDATE web_best_sync_entries
                SET synced_projection_hash = $sent_hash, deferred_error = NULL,
                    updated_at = $updated_at
                WHERE chart_id = $chart_id AND desired_projection_hash = $sent_hash;
                """,
                ("$chart_id", chartId),
                ("$sent_hash", sentHash),
                ("$updated_at", DateTimeOffset.UtcNow.ToString("O")));
        }
        Execute(
            transaction,
            "DELETE FROM web_best_sync_entries WHERE desired_projection_hash IS NULL AND synced_projection_hash IS NULL;");
        transaction.Commit();
        return Read(connection);
    }

    public WebBestSyncSnapshot MarkDeferred(string chartId, string errorCode)
    {
        using var connection = Open();
        Execute(
            connection,
            """
            UPDATE web_best_sync_entries
            SET deferred_error = $error_code, updated_at = $updated_at
            WHERE chart_id = $chart_id;
            """,
            ("$chart_id", chartId),
            ("$error_code", errorCode),
            ("$updated_at", DateTimeOffset.UtcNow.ToString("O")));
        return Read(connection);
    }

    public WebBestSyncSnapshot CompleteSnapshot(
        IReadOnlyList<PlayerChartBestProjectionV1> projections,
        DateTimeOffset completedAt)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(transaction, "DELETE FROM web_best_sync_entries;");
        foreach (var projection in projections)
        {
            var hash = WebBestProjectionContract.Hash(projection);
            Execute(
                transaction,
                """
                INSERT INTO web_best_sync_entries (
                  chart_id, desired_projection_hash, synced_projection_hash,
                  deferred_error, updated_at
                ) VALUES ($chart_id, $hash, $hash, NULL, $updated_at);
                """,
                ("$chart_id", projection.ChartId),
                ("$hash", hash),
                ("$updated_at", completedAt.ToString("O")));
        }
        Execute(
            transaction,
            """
            UPDATE web_best_sync_metadata
            SET full_snapshot_required = 0,
                status = 'Idle',
                last_success_at = $completed_at,
                retry_attempt = 0,
                next_retry_at = NULL,
                last_error_code = NULL
            WHERE singleton_id = 1;
            """,
            ("$completed_at", completedAt.ToString("O")));
        transaction.Commit();
        return Read(connection);
    }

    public WebBestSyncSnapshot RecordStatus(
        WebBestSyncStatus status,
        DateTimeOffset? lastSuccessfulSyncAt = null,
        int? retryAttempt = null,
        DateTimeOffset? nextRetryAt = null,
        string? errorCode = null)
    {
        using var connection = Open();
        Execute(
            connection,
            """
            UPDATE web_best_sync_metadata
            SET status = $status,
                last_success_at = COALESCE($last_success_at, last_success_at),
                retry_attempt = COALESCE($retry_attempt, retry_attempt),
                next_retry_at = $next_retry_at,
                last_error_code = $error_code
            WHERE singleton_id = 1;
            """,
            ("$status", status.ToString()),
            ("$last_success_at", (object?)lastSuccessfulSyncAt?.ToString("O") ?? DBNull.Value),
            ("$retry_attempt", (object?)retryAttempt ?? DBNull.Value),
            ("$next_retry_at", (object?)nextRetryAt?.ToString("O") ?? DBNull.Value),
            ("$error_code", (object?)errorCode ?? DBNull.Value));
        return Read(connection);
    }

    public WebBestSyncSnapshot ClearAfterPublicDelete()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(transaction, "DELETE FROM web_best_sync_entries;");
        Execute(
            transaction,
            """
            UPDATE web_best_sync_metadata
            SET enabled = 0,
                full_snapshot_required = 0,
                status = 'PublicBestsDeleted',
                last_success_at = NULL,
                retry_attempt = 0,
                next_retry_at = NULL,
                last_error_code = NULL
            WHERE singleton_id = 1;
            """);
        transaction.Commit();
        return Read(connection);
    }

    private void Initialize()
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Web Best sync state parent directory is missing.");
        Directory.CreateDirectory(directory);
        using var connection = Open();
        Execute(
            connection,
            """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS web_best_sync_metadata (
              singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
              enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
              full_snapshot_required INTEGER NOT NULL CHECK (full_snapshot_required IN (0, 1)),
              status TEXT NOT NULL CHECK (
                status IN ('Disabled', 'Idle', 'Dirty', 'Syncing', 'Reconciling', 'ErrorRetryable', 'AuthInvalid', 'PublicBestsDeleted')
              ),
              last_success_at TEXT,
              retry_attempt INTEGER NOT NULL CHECK (retry_attempt >= 0),
              next_retry_at TEXT,
              last_error_code TEXT
            ) STRICT;
            CREATE TABLE IF NOT EXISTS web_best_sync_entries (
              chart_id TEXT PRIMARY KEY,
              desired_projection_hash TEXT,
              synced_projection_hash TEXT,
              deferred_error TEXT,
              updated_at TEXT NOT NULL,
              CHECK (desired_projection_hash IS NULL OR length(desired_projection_hash) = 64),
              CHECK (synced_projection_hash IS NULL OR length(synced_projection_hash) = 64)
            ) STRICT;
            INSERT OR IGNORE INTO web_best_sync_metadata (
              singleton_id, enabled, full_snapshot_required, status, retry_attempt
            ) VALUES (1, 0, 0, 'Disabled', 0);
            PRAGMA user_version = 1;
            """);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static WebBestSyncSnapshot Read(SqliteConnection connection)
    {
        using var metadata = connection.CreateCommand();
        metadata.CommandText =
            """
            SELECT enabled, full_snapshot_required, status, last_success_at,
                   retry_attempt, next_retry_at, last_error_code
            FROM web_best_sync_metadata WHERE singleton_id = 1;
            """;
        using var reader = metadata.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidDataException("Web Best sync metadata is missing.");
        }
        var enabled = reader.GetInt32(0) == 1;
        var fullSnapshotRequired = reader.GetInt32(1) == 1;
        var status = Enum.Parse<WebBestSyncStatus>(reader.GetString(2));
        DateTimeOffset? lastSuccess = reader.IsDBNull(3)
            ? null
            : DateTimeOffset.Parse(reader.GetString(3));
        var retryAttempt = reader.GetInt32(4);
        DateTimeOffset? nextRetry = reader.IsDBNull(5)
            ? null
            : DateTimeOffset.Parse(reader.GetString(5));
        var lastError = reader.IsDBNull(6) ? null : reader.GetString(6);
        reader.Close();

        using var entriesCommand = connection.CreateCommand();
        entriesCommand.CommandText =
            """
            SELECT chart_id, desired_projection_hash, synced_projection_hash, deferred_error
            FROM web_best_sync_entries ORDER BY chart_id;
            """;
        using var entriesReader = entriesCommand.ExecuteReader();
        var entries = new List<WebBestSyncEntry>();
        while (entriesReader.Read())
        {
            entries.Add(new WebBestSyncEntry(
                entriesReader.GetString(0),
                entriesReader.IsDBNull(1) ? null : entriesReader.GetString(1),
                entriesReader.IsDBNull(2) ? null : entriesReader.GetString(2),
                entriesReader.IsDBNull(3) ? null : entriesReader.GetString(3)));
        }
        return new(
            enabled,
            fullSnapshotRequired,
            status,
            lastSuccess,
            retryAttempt,
            nextRetry,
            lastError,
            entries);
    }

    private static void Execute(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        command.ExecuteNonQuery();
    }
}
