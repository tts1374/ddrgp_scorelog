using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDRGpScoreViewer.Data;
using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Tests;

internal sealed class DatabaseFixture : IDisposable
{
    public DatabaseFixture()
    {
        SQLitePCL.Batteries_V2.Init();
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"ddrgp-viewer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        ScorePath = Path.Combine(DirectoryPath, "scores.sqlite");
        MasterPath = Path.Combine(DirectoryPath, "master.sqlite");
        CatalogPath = Path.Combine(DirectoryPath, "jacket-catalog.sqlite");
        CreateScoreDatabase();
        CreateMasterDatabase();
        CreateJacketCatalogDatabase();
    }

    public string DirectoryPath { get; }
    public string ScorePath { get; }
    public string MasterPath { get; }
    public string CatalogPath { get; }

    public void AddPlay(
        string playId,
        string playedAt,
        int score,
        int exScore,
        string songId = "song-1",
        string chartId = "chart-1",
        string? flareRank = null)
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
              flare_rank, capture_hash, source_capture_id, duplicate_key, analysis_confidence, app_version
            ) VALUES (
              $play_id, $played_at, 'master-v1', $song_id, $chart_id, $score, 500,
              400, 80, 10, 2, 1, $ex_score, 'AAA', 'CLEAR', $flare_rank,
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
        command.Parameters.AddWithValue("$flare_rank", (object?)flareRank ?? DBNull.Value);
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

    public void ExecuteMasterSql(string sql)
    {
        using var connection = OpenWritable(MasterPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void ExecuteCatalogSql(string sql)
    {
        using var connection = OpenWritable(CatalogPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void AddMasterSongAndChart(
        string songId,
        string title,
        string artist,
        string chartId,
        string playStyle = "SINGLE",
        string difficulty = "EXPERT",
        int level = 17,
        string version = "DDR GRAND PRIX")
    {
        using var connection = OpenWritable(MasterPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO songs (song_id, title, artist, version, grand_prix_play_available, official_availability_match) " +
            "VALUES ($song_id, $title, $artist, $version, 1, 'fixture'); " +
            "INSERT INTO charts (chart_id, song_id, play_style, difficulty, level) " +
            "VALUES ($chart_id, $song_id, $play_style, $difficulty, $level); " +
            "UPDATE master_metadata SET value = (SELECT COUNT(*) FROM songs) WHERE key = 'song_count'; " +
            "UPDATE master_metadata SET value = (SELECT COUNT(*) FROM charts) WHERE key = 'chart_count';";
        command.Parameters.AddWithValue("$song_id", songId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$artist", artist);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$chart_id", chartId);
        command.Parameters.AddWithValue("$play_style", playStyle);
        command.Parameters.AddWithValue("$difficulty", difficulty);
        command.Parameters.AddWithValue("$level", level);
        command.ExecuteNonQuery();
    }

    public void AddJacketReference(
        string songId,
        IReadOnlyList<double> thumbnail,
        IReadOnlyList<double> histogram,
        IReadOnlyList<double> dhash,
        string canonicalTitle = "MAX 300",
        string canonicalArtist = "Artist")
    {
        using var connection = OpenWritable(CatalogPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO jacket_references (" +
            "reference_id, source_capture_id, source_image_hash, master_version, song_id, " +
            "canonical_title_snapshot, canonical_artist_snapshot, review_status, " +
            "resolution_reason, resolution_basis, feature_extractor_version, image_kind, " +
            "thumbnail_rgb_json, histogram_json, dhash_bits_json, dhash_hex, observed_title, " +
            "observed_artist, observation_status, expected_song_id, review_revision, " +
            "manual_action_id, manual_note, jacket_feature_version, jacket_feature_hash, " +
            "title_line_feature_version, title_line_hash, composite_identity_version, " +
            "composite_identity_hash, created_at, updated_at) VALUES (" +
            "$reference_id, NULL, $source_image_hash, $master_version, $song_id, " +
            "$canonical_title, $canonical_artist, 'manual_confirmed', 'fixture', 'fixture', " +
            "'m5-jacket-v2', 'jacket', $thumbnail, $histogram, $dhash, '0', " +
            "$canonical_title, $canonical_artist, 'ok', $song_id, 1, 'action-1', 'fixture', " +
            "'m5c-jacket-rgb-grid-v1', 'fixture-hash', NULL, NULL, " +
            "'m5c-jacket-title-composite-identity-v2', $composite_identity_hash, " +
            "'2026-07-30T00:00:00+00:00', '2026-07-30T00:00:00+00:00');";
        command.Parameters.AddWithValue("$reference_id", $"reference-{songId}");
        command.Parameters.AddWithValue("$source_image_hash", $"hash-{songId}");
        command.Parameters.AddWithValue("$master_version", "master-v1");
        command.Parameters.AddWithValue("$song_id", songId);
        command.Parameters.AddWithValue("$canonical_title", canonicalTitle);
        command.Parameters.AddWithValue("$canonical_artist", canonicalArtist);
        command.Parameters.AddWithValue("$composite_identity_hash", $"fixture-composite-{songId}");
        command.Parameters.AddWithValue("$thumbnail", JsonSerializer.Serialize(thumbnail));
        command.Parameters.AddWithValue("$histogram", JsonSerializer.Serialize(histogram));
        command.Parameters.AddWithValue("$dhash", JsonSerializer.Serialize(dhash));
        command.ExecuteNonQuery();
    }

    public void AddResultTextFeature(
        string songId,
        string fieldName,
        byte grayValue,
        string canonicalTitle,
        string canonicalArtist,
        string masterVersion = "master-v1",
        IReadOnlyList<string>? linehashRows = null,
        bool nestedVectors = false)
    {
        if (fieldName is not ("title" or "artist"))
        {
            throw new ArgumentOutOfRangeException(nameof(fieldName));
        }

        const string schemaVersion = "m7-result-text-feature-master-v1";
        const string featureVersion = "m7-result-text-image-v1";
        const string roiVersion = "m7-result-title-artist-roi-v1";
        var payloadJson = ResultTextPayload(
            grayValue,
            linehashRows,
            nestedVectors);
        var featureHash = Sha256Hex(payloadJson);
        var featureId = Sha256Hex(string.Join(
            "\0",
            schemaVersion,
            masterVersion,
            songId,
            fieldName,
            featureVersion,
            roiVersion,
            featureHash));

        using var connection = OpenWritable(CatalogPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO result_text_features (" +
            "feature_id, song_id, field_name, feature_version, roi_version, feature_hash, " +
            "payload_json, source_label, master_version, canonical_title_snapshot, " +
            "canonical_artist_snapshot, created_at) VALUES (" +
            "$feature_id, $song_id, $field_name, $feature_version, $roi_version, $feature_hash, " +
            "$payload_json, 'fixture', $master_version, $canonical_title, $canonical_artist, " +
            "'2026-07-30T00:00:00+00:00');";
        command.Parameters.AddWithValue("$feature_id", featureId);
        command.Parameters.AddWithValue("$song_id", songId);
        command.Parameters.AddWithValue("$field_name", fieldName);
        command.Parameters.AddWithValue("$feature_version", featureVersion);
        command.Parameters.AddWithValue("$roi_version", roiVersion);
        command.Parameters.AddWithValue("$feature_hash", featureHash);
        command.Parameters.AddWithValue("$payload_json", payloadJson);
        command.Parameters.AddWithValue("$master_version", masterVersion);
        command.Parameters.AddWithValue("$canonical_title", canonicalTitle);
        command.Parameters.AddWithValue("$canonical_artist", canonicalArtist);
        command.ExecuteNonQuery();
    }

    private static string ResultTextPayload(
        byte grayValue,
        IReadOnlyList<string>? linehashRows,
        bool nestedVectors)
    {
        var vector1536 = ResultTextVector(
            grayValue.ToString(),
            1536,
            96,
            nestedVectors);
        var vector640 = ResultTextVector(
            grayValue.ToString(),
            640,
            40,
            nestedVectors);
        var zeroVector1536 = ResultTextVector("0", 1536, 96, nestedVectors);
        var zeroVector640 = ResultTextVector("0", 640, 40, nestedVectors);
        var vector1536Shape = nestedVectors ? "[96,16]" : "[1536]";
        var vector640Shape = nestedVectors ? "[40,16]" : "[640]";
        var linehashValues = linehashRows ??
            Enumerable.Repeat(new string('0', 76), 28).ToArray();
        if (linehashValues.Count != 28 ||
            linehashValues.Any(row => row.Length != 76))
        {
            throw new ArgumentException("Fixture linehash rows must be 28 x 76.", nameof(linehashRows));
        }
        var linehashJson = JsonSerializer.Serialize(linehashValues);
        return "{\"dhash_hex\":\"0000000000000000\",\"edge\":" + zeroVector1536 +
            ",\"edge_shape\":" + vector1536Shape + ",\"feature_version\":\"m7-result-text-image-v1\",\"linehash_rows\":" +
            linehashJson +
            ",\"luma\":" + vector1536 + ",\"luma_shape\":" + vector1536Shape +
            ",\"roi_version\":\"m7-result-title-artist-roi-v1\",\"suffix_edge\":" +
            zeroVector640 + ",\"suffix_edge_shape\":" + vector640Shape +
            ",\"suffix_luma\":" + vector640 + ",\"suffix_luma_shape\":" +
            vector640Shape + ",\"vector_encoding\":\"uint8_0_255\"}";
    }

    private static string ResultTextVector(
        string value,
        int length,
        int rowCount,
        bool nested)
    {
        if (!nested)
        {
            return "[" + string.Join(",", Enumerable.Repeat(value, length)) + "]";
        }

        var row = "[" + string.Join(",", Enumerable.Repeat(value, length / rowCount)) + "]";
        return "[" + string.Join(",", Enumerable.Repeat(row, rowCount)) + "]";
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
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
        var result = new PersonalScoreDbInitializer()
            .InitializeIfMissingAsync(ScorePath)
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded || !result.Initialized)
        {
            throw new InvalidOperationException(
                $"Production score database initialization failed: {result.Message}");
        }
    }

    private void CreateMasterDatabase()
    {
        using var connection = OpenWritable(MasterPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE songs (
              song_id TEXT PRIMARY KEY, title TEXT NOT NULL, artist TEXT NOT NULL,
              version TEXT NOT NULL, grand_prix_play_available INTEGER NOT NULL,
              official_availability_match TEXT NOT NULL
            );
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
            INSERT INTO songs VALUES ('song-1', 'MAX 300', 'Artist', 'DDR GRAND PRIX', 1, 'fixture');
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

    private void CreateJacketCatalogDatabase()
    {
        using var connection = OpenWritable(CatalogPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA user_version = 1;
            CREATE TABLE catalog_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE result_text_features (
              feature_id TEXT PRIMARY KEY, song_id TEXT NOT NULL, field_name TEXT NOT NULL,
              feature_version TEXT NOT NULL, roi_version TEXT NOT NULL, feature_hash TEXT NOT NULL,
              payload_json TEXT NOT NULL, source_label TEXT NOT NULL, master_version TEXT NOT NULL,
              canonical_title_snapshot TEXT NOT NULL, canonical_artist_snapshot TEXT NOT NULL,
              created_at TEXT NOT NULL,
              UNIQUE (song_id, field_name, feature_version, feature_hash)
            );
            CREATE INDEX idx_result_text_features_song_field
              ON result_text_features(song_id, field_name, master_version);
            CREATE TABLE jacket_references (
              reference_id TEXT PRIMARY KEY, source_capture_id TEXT, source_image_hash TEXT NOT NULL,
              master_version TEXT NOT NULL, song_id TEXT, canonical_title_snapshot TEXT NOT NULL,
              canonical_artist_snapshot TEXT NOT NULL, review_status TEXT NOT NULL,
              resolution_reason TEXT NOT NULL, resolution_basis TEXT NOT NULL,
              feature_extractor_version TEXT NOT NULL, image_kind TEXT NOT NULL,
              thumbnail_rgb_json TEXT, histogram_json TEXT, dhash_bits_json TEXT,
              dhash_hex TEXT NOT NULL, observed_title TEXT NOT NULL, observed_artist TEXT NOT NULL,
              observation_status TEXT NOT NULL, expected_song_id TEXT NOT NULL,
              review_revision INTEGER NOT NULL, manual_action_id TEXT, manual_note TEXT NOT NULL,
              jacket_feature_version TEXT, jacket_feature_hash TEXT,
              title_line_feature_version TEXT, title_line_hash TEXT,
              composite_identity_version TEXT, composite_identity_hash TEXT,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL
            );
            CREATE INDEX idx_jacket_references_hash
              ON jacket_references(source_image_hash, feature_extractor_version);
            CREATE UNIQUE INDEX idx_jacket_references_hash_song
              ON jacket_references(source_image_hash, feature_extractor_version, song_id)
              WHERE song_id IS NOT NULL;
            CREATE UNIQUE INDEX idx_jacket_references_capture
              ON jacket_references(source_capture_id, feature_extractor_version)
              WHERE source_capture_id IS NOT NULL;
            CREATE INDEX idx_jacket_references_song ON jacket_references(song_id);
            CREATE UNIQUE INDEX idx_jacket_references_composite_identity
              ON jacket_references(composite_identity_version, composite_identity_hash)
              WHERE composite_identity_version IS NOT NULL
                AND composite_identity_hash IS NOT NULL;
            CREATE TABLE reference_candidates (
              reference_id TEXT NOT NULL REFERENCES jacket_references(reference_id),
              song_id TEXT NOT NULL, candidate_reason TEXT NOT NULL,
              PRIMARY KEY (reference_id, song_id)
            );
            CREATE TABLE reference_review_history (
              history_id INTEGER PRIMARY KEY AUTOINCREMENT, action_id TEXT NOT NULL UNIQUE,
              reference_id TEXT NOT NULL REFERENCES jacket_references(reference_id),
              action TEXT NOT NULL, before_status TEXT NOT NULL, after_status TEXT NOT NULL,
              before_song_id TEXT, after_song_id TEXT, reason TEXT NOT NULL, note TEXT NOT NULL,
              action_at TEXT NOT NULL, before_revision INTEGER NOT NULL,
              after_revision INTEGER NOT NULL, request_payload_json TEXT NOT NULL,
              receipt_json TEXT NOT NULL
            );
            CREATE INDEX idx_reference_review_history_reference
              ON reference_review_history(reference_id, history_id);
            INSERT INTO catalog_metadata (key, value) VALUES
              ('catalog_identity', 'ddrgp-local-jacket-reference-catalog'),
              ('schema_version', '1'),
              ('created_at', '2026-07-13T00:00:00+00:00'),
              ('master_version', 'master-v1');
            """;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenWritable(string path) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
}
