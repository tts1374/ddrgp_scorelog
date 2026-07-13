using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Tests;

internal sealed class DatabaseFixture : IDisposable
{
    private const string ScoreSchema =
        """
        PRAGMA foreign_keys = ON;
        CREATE TABLE score_db_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE schema_migrations (
          migration_id TEXT PRIMARY KEY,
          schema_version INTEGER NOT NULL,
          applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
          app_version TEXT NOT NULL,
          notes TEXT NOT NULL
        );
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
        );
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
        );
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
        );
        PRAGMA user_version = 1;
        """;

    public DatabaseFixture()
    {
        SQLitePCL.Batteries_V2.Init();
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"ddrgp-viewer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        ScorePath = Path.Combine(DirectoryPath, "scores.sqlite");
        MasterPath = Path.Combine(DirectoryPath, "master.sqlite");
        CreateScoreDatabase();
        CreateMasterDatabase();
    }

    public string DirectoryPath { get; }
    public string ScorePath { get; }
    public string MasterPath { get; }

    public void AddPlay(
        string playId,
        string playedAt,
        int score,
        int exScore,
        string songId = "song-1",
        string chartId = "chart-1")
    {
        using var connection = OpenWritable(ScorePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO source_captures (
              capture_id, capture_hash, captured_at, source_kind, source_path
            ) VALUES ($capture_id, $capture_hash, $played_at, 'manual', 'fixture');
            INSERT INTO plays (
              play_id, played_at, master_version, song_id, chart_id, score, max_combo,
              marvelous, perfect, great, good, miss, ex_score, rank, clear_type,
              capture_hash, source_capture_id, duplicate_key, analysis_confidence, app_version
            ) VALUES (
              $play_id, $played_at, 'master-v1', $song_id, $chart_id, $score, 500,
              400, 80, 10, 2, 1, $ex_score, 'AAA', 'CLEAR',
              $capture_hash, $capture_id, $duplicate_key, 0.99, 'test'
            );
            """;
        command.Parameters.AddWithValue("$capture_id", $"capture-{playId}");
        command.Parameters.AddWithValue("$capture_hash", $"hash-{playId}");
        command.Parameters.AddWithValue("$play_id", playId);
        command.Parameters.AddWithValue("$played_at", playedAt);
        command.Parameters.AddWithValue("$song_id", songId);
        command.Parameters.AddWithValue("$chart_id", chartId);
        command.Parameters.AddWithValue("$score", score);
        command.Parameters.AddWithValue("$ex_score", exScore);
        command.Parameters.AddWithValue("$duplicate_key", $"duplicate-{playId}");
        command.ExecuteNonQuery();
    }

    public void ExecuteScoreSql(string sql)
    {
        using var connection = OpenWritable(ScorePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // Test temp cleanup must not hide the assertion result.
        }
    }

    private void CreateScoreDatabase()
    {
        using var connection = OpenWritable(ScorePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = ScoreSchema;
        command.ExecuteNonQuery();
        var metadata = new Dictionary<string, string>
        {
            ["created_by"] = "tools.vision_poc.personal_score_db_schema",
            ["schema_name"] = "personal_score_db",
            ["schema_version"] = "1",
            ["schema_version_source"] = "PRAGMA user_version and score_db_metadata",
            ["schema_contract_scope"] = "production_personal_score_db",
            ["production_schema_status"] = "production_schema",
            ["preview_schema_status"] = "rejects_m8_score_db_preview",
        };
        foreach (var pair in metadata)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO score_db_metadata (key, value) VALUES ($key, $value);";
            insert.Parameters.AddWithValue("$key", pair.Key);
            insert.Parameters.AddWithValue("$value", pair.Value);
            insert.ExecuteNonQuery();
        }
        using var migration = connection.CreateCommand();
        migration.CommandText =
            """
            INSERT INTO schema_migrations (migration_id, schema_version, app_version, notes)
            VALUES ('001_initial_personal_score_db_schema', 1, 'test', 'fixture');
            """;
        migration.ExecuteNonQuery();
    }

    private void CreateMasterDatabase()
    {
        using var connection = OpenWritable(MasterPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE songs (song_id TEXT PRIMARY KEY, title TEXT NOT NULL);
            CREATE TABLE charts (
              chart_id TEXT PRIMARY KEY, song_id TEXT NOT NULL, play_style TEXT NOT NULL,
              difficulty TEXT NOT NULL, level INTEGER NOT NULL
            );
            CREATE TABLE song_aliases (alias_id TEXT PRIMARY KEY);
            CREATE TABLE master_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE source_snapshots (
              snapshot_id TEXT PRIMARY KEY, source_url TEXT NOT NULL,
              content_hash TEXT NOT NULL
            );
            INSERT INTO songs VALUES ('song-1', 'MAX 300');
            INSERT INTO charts VALUES ('chart-1', 'song-1', 'SINGLE', 'EXPERT', 17);
            INSERT INTO source_snapshots VALUES ('snapshot-1', 'https://example.test/source', 'hash-v1');
            """;
        command.ExecuteNonQuery();
        var metadata = new Dictionary<string, string>
        {
            ["master_version"] = "master-v1",
            ["source_url"] = "https://example.test/source",
            ["generated_at"] = "2026-07-13T00:00:00+00:00",
            ["generator_version"] = "test",
            ["source_hash"] = "hash-v1",
            ["song_count"] = "1",
            ["chart_count"] = "1",
        };
        foreach (var pair in metadata)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO master_metadata (key, value) VALUES ($key, $value);";
            insert.Parameters.AddWithValue("$key", pair.Key);
            insert.Parameters.AddWithValue("$value", pair.Value);
            insert.ExecuteNonQuery();
        }
    }

    private static SqliteConnection OpenWritable(string path) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
}
