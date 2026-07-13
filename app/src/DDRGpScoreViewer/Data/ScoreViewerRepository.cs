using DDRGpScoreViewer.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace DDRGpScoreViewer.Data;

public sealed class ScoreViewerRepository
{
    private const int SupportedScoreSchemaVersion = 1;

    private static readonly IReadOnlyDictionary<string, string> ScoreMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["created_by"] = "tools.vision_poc.personal_score_db_schema",
            ["schema_name"] = "personal_score_db",
            ["schema_version"] = "1",
            ["schema_version_source"] = "PRAGMA user_version and score_db_metadata",
            ["schema_contract_scope"] = "production_personal_score_db",
            ["production_schema_status"] = "production_schema",
            ["preview_schema_status"] = "rejects_m8_score_db_preview",
        };

    private static readonly IReadOnlyDictionary<string, string[]> ScoreTableColumns =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["score_db_metadata"] = ["key", "value"],
            ["schema_migrations"] =
                ["migration_id", "schema_version", "applied_at", "app_version", "notes"],
            ["source_captures"] =
                ["capture_id", "capture_hash", "captured_at", "source_kind", "source_path",
                 "manifest_image_path", "frame_index", "created_at"],
            ["plays"] =
                ["play_id", "played_at", "master_version", "song_id", "chart_id", "score",
                 "max_combo", "marvelous", "perfect", "great", "good", "miss", "ex_score",
                 "rank", "clear_type", "capture_hash", "source_capture_id", "duplicate_key",
                 "analysis_confidence", "app_version", "created_at"],
            ["analysis_logs"] =
                ["analysis_id", "play_id", "source_capture_id", "analysis_status",
                 "save_boundary_status", "skip_reason", "event_type", "confirmed_result",
                 "duplicate", "confirmation_mode", "timestamp_ms", "candidate_duration_ms",
                 "identity_signal_status", "digit_review_status", "analysis_confidence",
                 "analysis_summary_json", "log_path", "app_version", "created_at"],
        };

    private static readonly IReadOnlyDictionary<string, string> ScoreTableSql =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["score_db_metadata"] =
                """
                CREATE TABLE score_db_metadata (
                  key TEXT PRIMARY KEY,
                  value TEXT NOT NULL
                )
                """,
            ["schema_migrations"] =
                """
                CREATE TABLE schema_migrations (
                  migration_id TEXT PRIMARY KEY,
                  schema_version INTEGER NOT NULL,
                  applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                  app_version TEXT NOT NULL,
                  notes TEXT NOT NULL
                )
                """,
            ["source_captures"] =
                """
                CREATE TABLE source_captures (
                  capture_id TEXT PRIMARY KEY,
                  capture_hash TEXT NOT NULL UNIQUE,
                  captured_at TEXT NOT NULL,
                  source_kind TEXT NOT NULL CHECK (
                    source_kind IN ('manifest', 'timestamped', 'capture', 'manual', 'unknown')
                  ),
                  source_path TEXT NOT NULL,
                  manifest_image_path TEXT NOT NULL DEFAULT '',
                  frame_index INTEGER,
                  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                )
                """,
            ["plays"] =
                """
                CREATE TABLE plays (
                  play_id TEXT PRIMARY KEY,
                  played_at TEXT NOT NULL,
                  master_version TEXT NOT NULL,
                  song_id TEXT NOT NULL,
                  chart_id TEXT NOT NULL,
                  score INTEGER NOT NULL CHECK (score BETWEEN 0 AND 1000000),
                  max_combo INTEGER NOT NULL CHECK (max_combo >= 0),
                  marvelous INTEGER NOT NULL CHECK (marvelous >= 0),
                  perfect INTEGER NOT NULL CHECK (perfect >= 0),
                  great INTEGER NOT NULL CHECK (great >= 0),
                  good INTEGER NOT NULL CHECK (good >= 0),
                  miss INTEGER NOT NULL CHECK (miss >= 0),
                  ex_score INTEGER NOT NULL CHECK (ex_score >= 0),
                  rank TEXT NOT NULL,
                  clear_type TEXT NOT NULL,
                  capture_hash TEXT NOT NULL REFERENCES source_captures(capture_hash),
                  source_capture_id TEXT NOT NULL REFERENCES source_captures(capture_id),
                  duplicate_key TEXT NOT NULL UNIQUE,
                  analysis_confidence REAL NOT NULL CHECK (
                    analysis_confidence >= 0.0 AND analysis_confidence <= 1.0
                  ),
                  app_version TEXT NOT NULL,
                  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                )
                """,
            ["analysis_logs"] =
                """
                CREATE TABLE analysis_logs (
                  analysis_id TEXT PRIMARY KEY,
                  play_id TEXT REFERENCES plays(play_id),
                  source_capture_id TEXT REFERENCES source_captures(capture_id),
                  analysis_status TEXT NOT NULL CHECK (
                    analysis_status IN ('saved', 'skipped', 'low_confidence', 'error')
                  ),
                  save_boundary_status TEXT NOT NULL,
                  skip_reason TEXT NOT NULL DEFAULT '',
                  event_type TEXT NOT NULL,
                  confirmed_result INTEGER NOT NULL CHECK (confirmed_result IN (0, 1)),
                  duplicate INTEGER NOT NULL CHECK (duplicate IN (0, 1)),
                  confirmation_mode TEXT NOT NULL,
                  timestamp_ms INTEGER,
                  candidate_duration_ms INTEGER,
                  identity_signal_status TEXT NOT NULL DEFAULT '',
                  digit_review_status TEXT NOT NULL DEFAULT '',
                  analysis_confidence REAL CHECK (
                    analysis_confidence IS NULL
                    OR (analysis_confidence >= 0.0 AND analysis_confidence <= 1.0)
                  ),
                  analysis_summary_json TEXT NOT NULL DEFAULT '',
                  log_path TEXT NOT NULL DEFAULT '',
                  app_version TEXT NOT NULL,
                  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                )
                """,
        };

    private static readonly string[] MasterTables =
        ["songs", "charts", "song_aliases", "master_metadata", "source_snapshots"];

    private static readonly string[] MasterMetadataKeys =
        ["master_version", "source_url", "generated_at", "generator_version", "source_hash",
         "song_count", "chart_count"];

    public ViewerData Load(string scoreDatabasePath, string masterDatabasePath)
    {
        ValidateInputPath(scoreDatabasePath, "プレーデータ");
        ValidateInputPath(masterDatabasePath, "楽曲データ");

        try
        {
            using var scoreConnection = OpenReadOnly(scoreDatabasePath);
            ValidateScoreDatabase(scoreConnection);

            using var masterConnection = OpenReadOnly(masterDatabasePath);
            var masterVersion = ValidateMasterDatabase(masterConnection);
            var masterCharts = ReadMasterCharts(masterConnection);

            var plays = ReadPlays(scoreConnection, masterCharts);
            var chartBests = ReadChartBests(scoreConnection, masterCharts);
            return new ViewerData(
                plays,
                chartBests,
                Path.GetFullPath(scoreDatabasePath),
                Path.GetFullPath(masterDatabasePath),
                masterVersion);
        }
        catch (ViewerDatabaseException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw new ViewerDatabaseException(
                "データを読み込めませんでした。ファイルを確認して、もう一度お試しください。",
                exception);
        }
        catch (IOException exception)
        {
            throw new ViewerDatabaseException(
                "データを読み込めませんでした。ファイルを確認して、もう一度お試しください。",
                exception);
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void ValidateInputPath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new ViewerDatabaseException($"{label}ファイルが見つかりません。ファイルを選び直してください。");
        }
    }

    private static void ValidateScoreDatabase(SqliteConnection connection)
    {
        var tables = ReadTableNames(connection);
        if (tables.Contains("preview_metadata"))
        {
            throw RejectedScoreDatabase("プレビュー用のデータは表示できません。");
        }

        var userVersion = ExecuteInt64(connection, "PRAGMA user_version;");
        if (userVersion > SupportedScoreSchemaVersion)
        {
            throw RejectedScoreDatabase("このアプリより新しい形式のプレーデータです。");
        }

        if (userVersion != SupportedScoreSchemaVersion)
        {
            throw RejectedScoreDatabase("対応していないバージョンのプレーデータです。");
        }

        foreach (var (table, expectedColumns) in ScoreTableColumns)
        {
            if (!tables.Contains(table) ||
                !ReadColumns(connection, table).SequenceEqual(expectedColumns) ||
                NormalizeSql(ReadTableSql(connection, table)) != NormalizeSql(ScoreTableSql[table]))
            {
                throw RejectedScoreDatabase("プレーデータの構造が完全ではありません。");
            }
        }

        var metadata = ReadMetadata(connection, "score_db_metadata");
        if (ScoreMetadata.Any(pair =>
                !metadata.TryGetValue(pair.Key, out var actual) || actual != pair.Value))
        {
            throw RejectedScoreDatabase("プレーデータの識別情報が一致しません。");
        }

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText =
            "SELECT migration_id, schema_version FROM schema_migrations ORDER BY schema_version;";
        using var migrations = migrationCommand.ExecuteReader();
        var hasInitialMigration = false;
        var latestVersion = 0L;
        while (migrations.Read())
        {
            var migrationId = migrations.GetString(0);
            var version = migrations.GetInt64(1);
            hasInitialMigration |=
                migrationId == "001_initial_personal_score_db_schema" && version == 1;
            latestVersion = Math.Max(latestVersion, version);
        }

        if (!hasInitialMigration || latestVersion != userVersion)
        {
            throw RejectedScoreDatabase("プレーデータの更新履歴が完全ではありません。");
        }
    }

    private static ViewerDatabaseException RejectedScoreDatabase(string reason) =>
        new($"このプレーデータは開けません。{reason} ファイルは変更されていません。");

    private static string ValidateMasterDatabase(SqliteConnection connection)
    {
        var tables = ReadTableNames(connection);
        if (MasterTables.Any(table => !tables.Contains(table)))
        {
            throw new ViewerDatabaseException(
                "楽曲データを読み込めませんでした。生成済みの楽曲データを選び直してください。");
        }

        var metadata = ReadMetadata(connection, "master_metadata");
        if (MasterMetadataKeys.Any(key =>
                !metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
        {
            throw new ViewerDatabaseException(
                "楽曲データの識別情報が完全ではありません。生成済みの楽曲データを選び直してください。");
        }

        var songCount = ExecuteInt64(connection, "SELECT COUNT(*) FROM songs;");
        var chartCount = ExecuteInt64(connection, "SELECT COUNT(*) FROM charts;");
        if (songCount <= 0 || chartCount <= 0 ||
            metadata["song_count"] != songCount.ToString() ||
            metadata["chart_count"] != chartCount.ToString())
        {
            throw new ViewerDatabaseException(
                "楽曲データの件数が一致しません。生成済みの楽曲データを選び直してください。");
        }

        using var snapshotCommand = connection.CreateCommand();
        snapshotCommand.CommandText = "SELECT source_url, content_hash FROM source_snapshots;";
        using var snapshots = snapshotCommand.ExecuteReader();
        var snapshotMap = new Dictionary<string, string>(StringComparer.Ordinal);
        while (snapshots.Read())
        {
            snapshotMap[snapshots.GetString(0)] = snapshots.GetString(1);
        }

        if (snapshotMap.Count is < 1 or > 2 ||
            !snapshotMap.TryGetValue(metadata["source_url"], out var sourceHash) ||
            sourceHash != metadata["source_hash"])
        {
            throw new ViewerDatabaseException(
                "楽曲データの生成元情報が一致しません。生成済みの楽曲データを選び直してください。");
        }

        return metadata["master_version"];
    }

    private static Dictionary<string, MasterChart> ReadMasterCharts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.chart_id, c.song_id, s.title, c.play_style, c.difficulty, c.level
            FROM charts c
            JOIN songs s ON s.song_id = c.song_id;
            """;
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, MasterChart>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result[reader.GetString(0)] = new MasterChart(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5));
        }
        return result;
    }

    private static IReadOnlyList<PlayHistoryItem> ReadPlays(
        SqliteConnection connection,
        IReadOnlyDictionary<string, MasterChart> masterCharts)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.play_id, p.played_at, p.created_at, p.song_id, p.chart_id,
                   p.score, p.ex_score, p.rank, p.clear_type, p.max_combo,
                   p.marvelous, p.perfect, p.great, p.good, p.miss,
                   COALESCE(sc.source_kind, 'unknown')
            FROM plays p
            LEFT JOIN source_captures sc ON sc.capture_id = p.source_capture_id
            ORDER BY julianday(p.played_at) DESC, p.played_at DESC, p.play_id DESC;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<PlayHistoryItem>();
        while (reader.Read())
        {
            var songId = reader.GetString(3);
            var chartId = reader.GetString(4);
            var found = masterCharts.TryGetValue(chartId, out var chart) && chart.SongId == songId;
            result.Add(new PlayHistoryItem(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), songId, chartId,
                found ? chart!.Title : $"参照情報なし（{songId}）",
                found ? chart!.PlayStyle : "",
                found ? chart!.Difficulty : "参照情報なし",
                found ? chart!.Level : null,
                reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7), reader.GetString(8),
                reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12),
                reader.GetInt32(13), reader.GetInt32(14), reader.GetString(15), !found));
        }
        return result;
    }

    private static IReadOnlyList<ChartBestItem> ReadChartBests(
        SqliteConnection connection,
        IReadOnlyDictionary<string, MasterChart> masterCharts)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.song_id, p.chart_id, MAX(p.score), MAX(p.ex_score),
                   (
                     SELECT recent.played_at
                     FROM plays recent
                     WHERE recent.song_id = p.song_id AND recent.chart_id = p.chart_id
                     ORDER BY julianday(recent.played_at) DESC,
                              recent.played_at DESC,
                              recent.play_id DESC
                     LIMIT 1
                   ),
                   COUNT(*)
            FROM plays p
            GROUP BY p.song_id, p.chart_id
            ORDER BY MAX(p.score) DESC, p.song_id, p.chart_id;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<ChartBestItem>();
        while (reader.Read())
        {
            var songId = reader.GetString(0);
            var chartId = reader.GetString(1);
            var found = masterCharts.TryGetValue(chartId, out var chart) && chart.SongId == songId;
            result.Add(new ChartBestItem(
                songId, chartId,
                found ? chart!.Title : $"参照情報なし（{songId}）",
                found ? chart!.PlayStyle : "",
                found ? chart!.Difficulty : "参照情報なし",
                found ? chart!.Level : null,
                reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4), reader.GetInt32(5),
                !found));
        }
        return result;
    }

    private static HashSet<string> ReadTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static string[] ReadColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(1));
        }
        return [.. result];
    }

    private static string ReadTableSql(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static string NormalizeSql(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant()
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal);

    private static Dictionary<string, string> ReadMetadata(
        SqliteConnection connection,
        string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT key, value FROM {table};";
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }
        return result;
    }

    private static long ExecuteInt64(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private sealed record MasterChart(
        string SongId,
        string Title,
        string PlayStyle,
        string Difficulty,
        int Level);
}
