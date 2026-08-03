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
                 "rank", "clear_type", "flare_rank", "capture_hash", "source_capture_id", "duplicate_key",
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
                  score INTEGER NOT NULL CHECK (score BETWEEN 0 AND 1000000 AND score % 10 = 0),
                  max_combo INTEGER NOT NULL CHECK (max_combo >= 0),
                  marvelous INTEGER NOT NULL CHECK (marvelous >= 0),
                  perfect INTEGER NOT NULL CHECK (perfect >= 0),
                  great INTEGER NOT NULL CHECK (great >= 0),
                  good INTEGER NOT NULL CHECK (good >= 0),
                  miss INTEGER NOT NULL CHECK (miss >= 0),
                  ex_score INTEGER NOT NULL CHECK (ex_score >= 0),
                  rank TEXT NOT NULL,
                  clear_type TEXT NOT NULL,
                  flare_rank TEXT CHECK (
                    flare_rank IS NULL OR flare_rank IN (
                      'I', 'II', 'III', 'IV', 'V', 'VI',
                      'VII', 'VIII', 'IX', 'EX'
                    )
                  ),
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

    internal static void InitializeEmptyScoreDatabase(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parentDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException($"Database parent directory could not be determined: {fullPath}");
        }

        Directory.CreateDirectory(parentDirectory);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = string.Join(";\n", ScoreTableSql.Values) +
            """
            ;
            CREATE INDEX IF NOT EXISTS idx_plays_played_at ON plays(played_at);
            CREATE INDEX IF NOT EXISTS idx_plays_song_chart ON plays(song_id, chart_id);
            CREATE INDEX IF NOT EXISTS idx_plays_capture_hash ON plays(capture_hash);
            CREATE INDEX IF NOT EXISTS idx_analysis_logs_play_id ON analysis_logs(play_id);
            CREATE INDEX IF NOT EXISTS idx_analysis_logs_source_capture_id
              ON analysis_logs(source_capture_id);
            CREATE INDEX IF NOT EXISTS idx_source_captures_capture_hash
              ON source_captures(capture_hash);
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();

        foreach (var pair in ScoreMetadata.OrderBy(pair => pair.Key))
        {
            command.CommandText =
                "INSERT INTO score_db_metadata (key, value) VALUES ($key, $value);";
            command.Parameters.AddWithValue("$key", pair.Key);
            command.Parameters.AddWithValue("$value", pair.Value);
            command.ExecuteNonQuery();
            command.Parameters.Clear();
        }

        command.CommandText =
            """
            INSERT INTO schema_migrations (
              migration_id, schema_version, app_version, notes
            )
            VALUES ($migration_id, $schema_version, $app_version, $notes);
            """;
        command.Parameters.AddWithValue("$migration_id", "001_initial_personal_score_db_schema");
        command.Parameters.AddWithValue("$schema_version", 1);
        command.Parameters.AddWithValue("$app_version", "schema-contract");
        command.Parameters.AddWithValue(
            "$notes",
            "Initial formal personal score DB schema contract.");
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static readonly string[] MasterTables =
        ["songs", "charts", "song_aliases", "master_metadata", "source_snapshots"];

    private static readonly string[] MasterMetadataKeys =
        ["master_version", "source_url", "generated_at", "generator_version", "source_hash",
         "song_count", "chart_count"];

    private const int SupportedJacketCatalogSchemaVersion = 1;
    private const string JacketCatalogIdentity = "ddrgp-local-jacket-reference-catalog";

    private static readonly string[] JacketCatalogTables =
        [
            "catalog_metadata",
            "result_text_features",
            "jacket_references",
            "reference_candidates",
            "reference_review_history",
        ];

    private static readonly IReadOnlyDictionary<string, string[]> JacketCatalogTableColumns =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["catalog_metadata"] = ["key", "value"],
            ["result_text_features"] =
            [
                "feature_id", "song_id", "field_name", "feature_version", "roi_version",
                "feature_hash", "payload_json", "source_label", "master_version",
                "canonical_title_snapshot", "canonical_artist_snapshot", "created_at",
            ],
            ["jacket_references"] =
            [
                "reference_id", "source_capture_id", "source_image_hash", "master_version",
                "song_id", "canonical_title_snapshot", "canonical_artist_snapshot", "review_status",
                "resolution_reason", "resolution_basis", "feature_extractor_version", "image_kind",
                "thumbnail_rgb_json", "histogram_json", "dhash_bits_json", "dhash_hex",
                "observed_title", "observed_artist", "observation_status", "expected_song_id",
                "review_revision", "manual_action_id", "manual_note", "jacket_feature_version",
                "jacket_feature_hash", "title_line_feature_version", "title_line_hash",
                "composite_identity_version", "composite_identity_hash", "created_at", "updated_at",
            ],
            ["reference_candidates"] = ["reference_id", "song_id", "candidate_reason"],
            ["reference_review_history"] =
            [
                "history_id", "action_id", "reference_id", "action", "before_status",
                "after_status", "before_song_id", "after_song_id", "reason", "note", "action_at",
                "before_revision", "after_revision", "request_payload_json", "receipt_json",
            ],
        };

    public ViewerData Load(string scoreDatabasePath, string masterDatabasePath)
    {
        return LoadCore(scoreDatabasePath, masterDatabasePath, catalogDatabasePath: null);
    }

    public ViewerData Load(
        string scoreDatabasePath,
        string masterDatabasePath,
        string catalogDatabasePath)
    {
        return LoadCore(scoreDatabasePath, masterDatabasePath, catalogDatabasePath);
    }

    private ViewerData LoadCore(
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath)
    {
        ValidateInputPath(scoreDatabasePath, "プレーデータ");
        ValidateInputPath(masterDatabasePath, "楽曲データ");
        if (catalogDatabasePath is not null)
        {
            ValidateInputPath(catalogDatabasePath, "jacket参照catalog");
        }

        try
        {
            using var scoreConnection = OpenReadOnly(scoreDatabasePath);
            ValidateScoreDatabase(scoreConnection);

            using var masterConnection = OpenReadOnly(masterDatabasePath);
            var masterVersion = ValidateMasterDatabase(masterConnection);
            var masterCharts = ReadMasterCharts(masterConnection);
            if (catalogDatabasePath is not null)
            {
                using var catalogConnection = OpenReadOnly(catalogDatabasePath);
                ValidateJacketCatalogDatabase(catalogConnection);
            }

            var plays = ReadPlays(scoreConnection, masterCharts);
            var chartBests = ReadChartBests(scoreConnection, masterCharts);
            return new ViewerData(
                plays,
                chartBests,
                Path.GetFullPath(scoreDatabasePath),
                Path.GetFullPath(masterDatabasePath),
                masterVersion,
                catalogDatabasePath is null ? "" : Path.GetFullPath(catalogDatabasePath));
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
        catch (UnauthorizedAccessException exception)
        {
            throw new ViewerDatabaseException(
                "データを読み込めませんでした。ファイルのアクセス権を確認してください。",
                exception);
        }
        catch (ArgumentException exception)
        {
            throw new ViewerDatabaseException(
                "データのpathを読み込めませんでした。現在の環境の既定pathを確認してください。",
                exception);
        }
    }

    public MasterDatabaseInspection InspectMasterDatabase(string path)
    {
        string fullPath;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return MasterDatabaseInspection.Missing(
                    string.Empty,
                    "master DBが既定pathにありません。生成済みの楽曲データを既定pathへ配置してください。");
            }

            fullPath = Path.GetFullPath(path);
        }
        catch (ArgumentException exception)
        {
            return new MasterDatabaseInspection(
                path,
                MasterDatabaseStatus.Unreadable,
                $"master DBのpathを読み込めません。既定pathとアクセス権を確認してください。{exception.Message}",
                null);
        }

        if (Directory.Exists(fullPath))
        {
            return new MasterDatabaseInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                "master DBのpathがdirectoryです。既定pathにSQLite fileを配置してください。",
                null);
        }

        if (!File.Exists(fullPath))
        {
            return MasterDatabaseInspection.Missing(
                fullPath,
                "master DBが見つかりません。保存を開始せず、生成済みの楽曲データを既定pathへ配置してください。");
        }

        SqliteConnection connection;
        try
        {
            if (!HasSqliteHeader(fullPath))
            {
                return new MasterDatabaseInspection(
                    fullPath,
                    MasterDatabaseStatus.Unreadable,
                    "master DBをSQLiteとして読み込めません。対応する生成済みの楽曲データを既定pathへ配置してください。",
                    null);
            }
            connection = OpenReadOnly(fullPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new MasterDatabaseInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                $"master DBを読み込めません。アクセス権を確認してください。{exception.Message}",
                null);
        }
        catch (IOException exception)
        {
            return new MasterDatabaseInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                $"master DBを読み込めません。ファイルを確認してください。{exception.Message}",
                null);
        }
        catch (SqliteException exception)
        {
            return new MasterDatabaseInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                $"master DBをSQLiteとして読み込めません。生成済みの楽曲データと既定pathを確認してください。{exception.Message}",
                null);
        }

        using (connection)
        {
            try
            {
                var version = ValidateMasterDatabase(connection);
                _ = ReadMasterCharts(connection);
                return new MasterDatabaseInspection(
                    fullPath,
                    MasterDatabaseStatus.Compatible,
                    $"master DBを読み込めます（schema compatible、version: {version}）。",
                    version);
            }
            catch (ViewerDatabaseException exception)
            {
                return new MasterDatabaseInspection(
                    fullPath,
                    MasterDatabaseStatus.Incompatible,
                    exception.UserMessage,
                    null);
            }
            catch (SqliteException exception)
            {
                return new MasterDatabaseInspection(
                    fullPath,
                    MasterDatabaseStatus.Incompatible,
                    $"master DBのschemaを読み込めません。対応する生成済みDBを既定pathへ配置してください。{exception.Message}",
                    null);
            }
        }
    }

    public JacketCatalogInspection InspectJacketCatalogDatabase(string path)
    {
        string fullPath;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return JacketCatalogInspection.Missing(
                    string.Empty,
                    "jacket参照catalogが既定pathにありません。jacket-catalog.sqliteを既定pathへ配置してください。");
            }

            fullPath = Path.GetFullPath(path);
        }
        catch (ArgumentException exception)
        {
            return new JacketCatalogInspection(
                path,
                MasterDatabaseStatus.Unreadable,
                $"jacket参照catalogのpathを読み込めません。既定pathとアクセス権を確認してください。{exception.Message}",
                null);
        }

        if (Directory.Exists(fullPath))
        {
            return new JacketCatalogInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                "jacket参照catalogのpathがdirectoryです。既定pathにSQLite fileを配置してください。",
                null);
        }

        if (!File.Exists(fullPath))
        {
            return JacketCatalogInspection.Missing(
                fullPath,
                "jacket参照catalogが見つかりません。保存を開始せず、jacket-catalog.sqliteを既定pathへ配置してください。");
        }

        SqliteConnection connection;
        try
        {
            if (!HasSqliteHeader(fullPath))
            {
                return new JacketCatalogInspection(
                    fullPath,
                    MasterDatabaseStatus.Unreadable,
                    "jacket参照catalogをSQLiteとして読み込めません。対応するjacket-catalog.sqliteを既定pathへ配置してください。",
                    null);
            }
            connection = OpenReadOnly(fullPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new JacketCatalogInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                $"jacket参照catalogを読み込めません。アクセス権を確認してください。{exception.Message}",
                null);
        }
        catch (IOException exception)
        {
            return new JacketCatalogInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                $"jacket参照catalogを読み込めません。ファイルを確認してください。{exception.Message}",
                null);
        }
        catch (SqliteException exception)
        {
            return new JacketCatalogInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                $"jacket参照catalogをSQLiteとして読み込めません。対応するjacket-catalog.sqliteと既定pathを確認してください。{exception.Message}",
                null);
        }
        catch (ArgumentException exception)
        {
            return new JacketCatalogInspection(
                fullPath,
                MasterDatabaseStatus.Unreadable,
                $"jacket参照catalogのpathを読み込めません。既定pathとアクセス権を確認してください。{exception.Message}",
                null);
        }

        using (connection)
        {
            try
            {
                var (version, masterContentVersion) = ValidateJacketCatalogDatabase(connection);
                return new JacketCatalogInspection(
                    fullPath,
                    MasterDatabaseStatus.Compatible,
                    $"jacket参照catalogをread-onlyで検証できます（schema compatible、version: {version}、master: {masterContentVersion}）。",
                    version.ToString(),
                    masterContentVersion);
            }
            catch (ViewerDatabaseException exception)
            {
                return new JacketCatalogInspection(
                    fullPath,
                    MasterDatabaseStatus.Incompatible,
                    exception.UserMessage,
                    null);
            }
            catch (SqliteException exception)
            {
                return new JacketCatalogInspection(
                    fullPath,
                    MasterDatabaseStatus.Incompatible,
                    $"jacket参照catalogのschemaを読み込めません。対応するjacket-catalog.sqliteを既定pathへ配置してください。{exception.Message}",
                    null);
            }
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

    private static bool HasSqliteHeader(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[16];
        return stream.Read(header) == header.Length &&
            header.SequenceEqual("SQLite format 3\0"u8);
    }

    private static void ValidateInputPath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new ViewerDatabaseException($"{label}ファイルが見つかりません。現在の環境の既定pathを確認してください。");
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

    internal static void ValidateScoreDatabaseForWrite(SqliteConnection connection) =>
        ValidateScoreDatabase(connection);

    private static ViewerDatabaseException RejectedScoreDatabase(string reason) =>
        new($"このプレーデータは開けません。{reason} ファイルは変更されていません。");

    private static (int SchemaVersion, string MasterContentVersion) ValidateJacketCatalogDatabase(
        SqliteConnection connection)
    {
        var tables = ReadUserTableNames(connection);
        if (!tables.SetEquals(JacketCatalogTables))
        {
            throw RejectedJacketCatalog("table identityが一致しません。");
        }

        var userVersion = ExecuteInt64(connection, "PRAGMA user_version;");
        if (userVersion != SupportedJacketCatalogSchemaVersion)
        {
            throw RejectedJacketCatalog("対応していないschema versionです。");
        }

        foreach (var (table, expectedColumns) in JacketCatalogTableColumns)
        {
            if (!ReadColumns(connection, table).SequenceEqual(expectedColumns))
            {
                throw RejectedJacketCatalog($"{table}のcolumnsが一致しません。");
            }
        }

        var metadata = ReadMetadata(connection, "catalog_metadata");
        if (!metadata.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(
                ["catalog_identity", "schema_version", "created_at", "master_version"]) ||
            !metadata.TryGetValue("catalog_identity", out var identity) ||
            identity != JacketCatalogIdentity ||
            !metadata.TryGetValue("schema_version", out var schemaVersion) ||
            schemaVersion != SupportedJacketCatalogSchemaVersion.ToString() ||
            !metadata.TryGetValue("created_at", out var createdAt) ||
            string.IsNullOrWhiteSpace(createdAt) ||
            !metadata.TryGetValue("master_version", out var masterContentVersion) ||
            string.IsNullOrWhiteSpace(masterContentVersion))
        {
            throw RejectedJacketCatalog("metadata identityが一致しません。");
        }

        var referenceUnique = ReadUniqueIndexColumns(connection, "jacket_references");
        var expectedReferenceUnique = new[]
        {
            "reference_id",
            "source_image_hash\u001ffeature_extractor_version\u001fsong_id",
            "source_capture_id\u001ffeature_extractor_version",
            "composite_identity_version\u001fcomposite_identity_hash",
        };
        if (!expectedReferenceUnique.All(referenceUnique.Contains))
        {
            throw RejectedJacketCatalog("jacket_referencesのuniquenessが一致しません。");
        }

        var resultTextUnique = ReadUniqueIndexColumns(connection, "result_text_features");
        if (!resultTextUnique.Contains("feature_id") ||
            !resultTextUnique.Contains("song_id\u001ffield_name\u001ffeature_version\u001ffeature_hash"))
        {
            throw RejectedJacketCatalog("result_text_featuresのuniquenessが一致しません。");
        }

        if (!ReadUniqueIndexColumns(connection, "reference_candidates")
                .Contains("reference_id\u001fsong_id") ||
            !ReadUniqueIndexColumns(connection, "reference_review_history")
                .Contains("action_id") ||
            !HasForeignKeyTo(connection, "reference_candidates", "jacket_references") ||
            !HasForeignKeyTo(connection, "reference_review_history", "jacket_references"))
        {
            throw RejectedJacketCatalog("catalog tableのuniquenessまたはforeign keyが一致しません。");
        }

        return (checked((int)userVersion), masterContentVersion);
    }

    private static ViewerDatabaseException RejectedJacketCatalog(string reason) =>
        new($"このjacket参照catalogは開けません。{reason} ファイルは変更されていません。");

    private static string ValidateMasterDatabase(SqliteConnection connection)
    {
        var tables = ReadTableNames(connection);
        if (MasterTables.Any(table => !tables.Contains(table)))
        {
            throw new ViewerDatabaseException(
                "楽曲データを読み込めませんでした。生成済みの楽曲データを現在の環境の既定pathで確認してください。");
        }

        var metadata = ReadMetadata(connection, "master_metadata");
        if (MasterMetadataKeys.Any(key =>
                !metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
        {
            throw new ViewerDatabaseException(
                "楽曲データの識別情報が完全ではありません。生成済みの楽曲データを確認してください。");
        }

        var songCount = ExecuteInt64(connection, "SELECT COUNT(*) FROM songs;");
        var chartCount = ExecuteInt64(connection, "SELECT COUNT(*) FROM charts;");
        if (songCount <= 0 || chartCount <= 0 ||
            metadata["song_count"] != songCount.ToString() ||
            metadata["chart_count"] != chartCount.ToString())
        {
            throw new ViewerDatabaseException(
                "楽曲データの件数が一致しません。生成済みの楽曲データを確認してください。");
        }

        using var snapshotCommand = connection.CreateCommand();
        snapshotCommand.CommandText = "SELECT source_url, content_hash FROM source_snapshots;";
        using var snapshots = snapshotCommand.ExecuteReader();
        var snapshotMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshotCount = 0;
        while (snapshots.Read())
        {
            snapshotCount++;
            snapshotMap[snapshots.GetString(0)] = snapshots.GetString(1);
        }

        if (snapshotCount is < 1 or > 3 || snapshotMap.Count != snapshotCount ||
            !snapshotMap.TryGetValue(metadata["source_url"], out var sourceHash) ||
            sourceHash != metadata["source_hash"])
        {
            throw new ViewerDatabaseException(
                "楽曲データの生成元情報が一致しません。生成済みの楽曲データを確認してください。");
        }

        ValidateOptionalSourceMetadata(
            metadata,
            snapshotMap,
            "official_source_url",
            "official_source_hash",
            "公式source");
        ValidateOptionalSourceMetadata(
            metadata,
            snapshotMap,
            "new_song_source_url",
            "new_song_source_hash",
            "新曲source");

        var expectedSnapshotCount =
            1 +
            Convert.ToInt32(
                metadata.TryGetValue("official_source_url", out var officialUrl) &&
                !string.IsNullOrWhiteSpace(officialUrl)) +
            Convert.ToInt32(
                metadata.TryGetValue("new_song_source_url", out var newSongUrl) &&
                !string.IsNullOrWhiteSpace(newSongUrl));
        if (snapshotCount != expectedSnapshotCount)
        {
            throw new ViewerDatabaseException(
                "楽曲データのsource snapshot件数がmetadataと一致しません。生成済みの楽曲データを確認してください。");
        }

        return metadata["master_version"];
    }

    private static void ValidateOptionalSourceMetadata(
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyDictionary<string, string> snapshots,
        string urlKey,
        string hashKey,
        string label)
    {
        var hasUrl = metadata.TryGetValue(urlKey, out var url) && !string.IsNullOrWhiteSpace(url);
        var hasHash = metadata.TryGetValue(hashKey, out var hash) && !string.IsNullOrWhiteSpace(hash);
        if (hasUrl != hasHash)
        {
            throw new ViewerDatabaseException(
                $"楽曲データの{label} metadataが不完全です。生成済みの楽曲データを確認してください。");
        }

        if (hasUrl && (!snapshots.TryGetValue(url!, out var snapshotHash) || snapshotHash != hash))
        {
            throw new ViewerDatabaseException(
                $"楽曲データの{label} metadataがsource snapshotと一致しません。生成済みの楽曲データを確認してください。");
        }
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
                   p.score, p.ex_score, p.rank, p.clear_type, p.flare_rank, p.max_combo,
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
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetInt32(10),
                reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14),
                reader.GetInt32(15), reader.GetString(16), !found));
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

    private static HashSet<string> ReadUserTableNames(SqliteConnection connection) =>
        ReadTableNames(connection)
            .Where(name => !name.StartsWith("sqlite_", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> ReadUniqueIndexColumns(
        SqliteConnection connection,
        string table)
    {
        using var listCommand = connection.CreateCommand();
        listCommand.CommandText = $"PRAGMA index_list({table});";
        using var indexes = listCommand.ExecuteReader();
        var indexNames = new List<string>();
        while (indexes.Read())
        {
            if (indexes.GetInt64(2) == 1)
            {
                indexNames.Add(indexes.GetString(1));
            }
        }
        indexes.Dispose();

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawIndexName in indexNames)
        {
            var indexName = rawIndexName.Replace("'", "''", StringComparison.Ordinal);
            using var infoCommand = connection.CreateCommand();
            infoCommand.CommandText = $"PRAGMA index_info('{indexName}');";
            using var columns = infoCommand.ExecuteReader();
            var names = new List<string>();
            while (columns.Read())
            {
                names.Add(columns.GetString(2));
            }
            result.Add(string.Join('\u001f', names));
        }
        return result;
    }

    private static bool HasForeignKeyTo(
        SqliteConnection connection,
        string table,
        string referencedTable)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({table});";
        using var foreignKeys = command.ExecuteReader();
        while (foreignKeys.Read())
        {
            if (string.Equals(foreignKeys.GetString(2), referencedTable, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
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
