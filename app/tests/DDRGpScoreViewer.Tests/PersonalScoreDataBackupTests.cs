using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class PersonalScoreDataBackupTests
{
    [Fact]
    public void Backup_contains_only_personal_play_fields()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "saved-play",
            "2026-08-01T10:00:00+00:00",
            950_000,
            1_100,
            ok: 21,
            calories: 28.6);
        fixture.ExecuteScoreSql(
            """
            INSERT INTO analysis_logs (
              analysis_id, play_id, source_capture_id, analysis_status, save_boundary_status,
              skip_reason, event_type, confirmed_result, duplicate, confirmation_mode,
              identity_signal_status, digit_review_status, analysis_summary_json, log_path, app_version
            ) VALUES (
              'analysis-1', 'saved-play', 'capture-saved-play', 'skipped', 'excluded',
              'ambiguous', 'fixture', 0, 0, 'automatic', 'unresolved', '', '{}',
              'diagnostic.json', 'test');
            """);
        var backupPath = Path.Combine(fixture.DirectoryPath, "personal-score-backup.json");

        var result = new PersonalScoreDataBackupService().CreateBackup(fixture.ScorePath, backupPath);

        Assert.True(result.Succeeded, result.Message);
        using var document = JsonDocument.Parse(File.ReadAllText(backupPath));
        var root = document.RootElement;
        Assert.Equal("ddrgp.personal-score-data", root.GetProperty("format").GetString());
        Assert.Equal(2, root.GetProperty("formatVersion").GetInt32());
        var play = Assert.Single(root.GetProperty("plays").EnumerateArray());
        Assert.Equal("saved-play", play.GetProperty("playId").GetString());
        Assert.Equal(950_000, play.GetProperty("score").GetInt32());
        Assert.Equal(1_100, play.GetProperty("exScore").GetInt32());
        Assert.Equal(21, play.GetProperty("ok").GetInt32());
        Assert.Equal(28.6, play.GetProperty("calories").GetDouble());

        var backupText = File.ReadAllText(backupPath);
        Assert.DoesNotContain("source_captures", backupText, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceCaptureId", backupText, StringComparison.Ordinal);
        Assert.DoesNotContain("captureHash", backupText, StringComparison.Ordinal);
        Assert.DoesNotContain("analysis_logs", backupText, StringComparison.Ordinal);
        Assert.DoesNotContain("analysisConfidence", backupText, StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostic", backupText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unresolved", backupText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings", backupText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jacket", backupText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_restore_replaces_plays_and_reloads_best_and_history_data()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "first-play",
            "2026-08-01T10:00:00+00:00",
            900_000,
            1_000,
            ok: 21,
            calories: 28.6);
        var backupPath = Path.Combine(fixture.DirectoryPath, "personal-score-backup.json");
        var service = new PersonalScoreDataBackupService();
        Assert.True(service.CreateBackup(fixture.ScorePath, backupPath).Succeeded);
        using var backupDocument = JsonDocument.Parse(File.ReadAllText(backupPath));
        var expectedSavedAt = backupDocument.RootElement
            .GetProperty("plays")[0]
            .GetProperty("savedAt")
            .GetString();

        fixture.AddPlay(
            "second-play",
            "2026-08-02T10:00:00+00:00",
            990_000,
            1_300);

        var result = service.RestoreBackup(fixture.ScorePath, backupPath);

        Assert.True(result.Succeeded, result.Message);
        var data = new ScoreViewerRepository().Load(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath);
        var play = Assert.Single(data.Plays);
        Assert.Equal("first-play", play.PlayId);
        var chartBest = Assert.Single(data.ChartBests);
        Assert.Equal(900_000, chartBest.BestScore);
        Assert.Equal(1_000, chartBest.BestExScore);
        Assert.Equal(1, chartBest.PlayCount);
        Assert.Equal("2026-08-01T10:00:00+00:00", play.PlayedAt);
        Assert.Equal(expectedSavedAt, play.SavedAt);
        Assert.Equal(21, play.Ok);
        Assert.Equal(28.6, play.Calories);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_v1_backup_restores_new_metrics_as_missing(
        bool includeUnsupportedMetrics)
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "legacy-source",
            "2026-08-01T10:00:00+00:00",
            900_000,
            1_000,
            ok: 21,
            calories: 28.6);
        var backupPath = Path.Combine(fixture.DirectoryPath, "legacy-personal-score-backup.json");
        var service = new PersonalScoreDataBackupService();
        Assert.True(service.CreateBackup(fixture.ScorePath, backupPath).Succeeded);
        var root = JsonNode.Parse(File.ReadAllText(backupPath))!.AsObject();
        root["formatVersion"] = 1;
        if (!includeUnsupportedMetrics)
        {
            var legacyPlay = root["plays"]!.AsArray()[0]!.AsObject();
            legacyPlay.Remove("ok");
            legacyPlay.Remove("calories");
        }
        File.WriteAllText(
            backupPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new System.Text.UTF8Encoding(false));

        var result = service.RestoreBackup(fixture.ScorePath, backupPath);
        var play = Assert.Single(new ScoreViewerRepository().Load(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath).Plays);

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(play.Ok);
        Assert.Null(play.Calories);
    }

    [Fact]
    public void Restoring_the_same_backup_twice_uses_new_source_capture_keys()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "restore-source",
            "2026-08-01T10:00:00+00:00",
            900_000,
            1_000);
        var backupPath = Path.Combine(fixture.DirectoryPath, "personal-score-backup.json");
        var service = new PersonalScoreDataBackupService();
        Assert.True(service.CreateBackup(fixture.ScorePath, backupPath).Succeeded);

        var firstRestore = service.RestoreBackup(fixture.ScorePath, backupPath);
        var secondRestore = service.RestoreBackup(fixture.ScorePath, backupPath);

        Assert.True(firstRestore.Succeeded, firstRestore.Message);
        Assert.True(secondRestore.Succeeded, secondRestore.Message);
        using var connection = OpenReadOnly(fixture.ScorePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT capture_id, capture_hash FROM source_captures " +
            "WHERE source_path = 'personal-score-backup' ORDER BY rowid;";
        var captures = new List<(string Id, string Hash)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                captures.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.Equal(2, captures.Count);
        Assert.Equal(2, captures.Select(capture => capture.Id).Distinct().Count());
        Assert.Equal(2, captures.Select(capture => capture.Hash).Distinct().Count());
        Assert.Equal(3L, CountRows(connection, "source_captures"));
        Assert.Equal(1L, CountRows(connection, "plays"));
    }

    public static TheoryData<int, string, string?> InvalidRequiredPlayFields()
    {
        var cases = new TheoryData<int, string, string?>();
        foreach (var version in new[] { 1, 2 })
        {
            foreach (var field in new[]
            {
                "score", "maxCombo", "marvelous", "perfect", "great", "good", "miss", "exScore",
            })
            {
                cases.Add(version, field, null);
            }
            foreach (var field in new[] { "playedAt", "savedAt" })
            {
                foreach (var timestamp in new[]
                {
                    "invalid-timestamp",
                    "2026-08-01",
                    "10:00:00",
                    "2026-08-01T10:00:00",
                    "2026-02-30T10:00:00+09:00",
                    "2026-08-01T10:00:00.",
                    "2026-08-01T10:00:00..Z",
                    "2026-08-01T10:00:00+09:00.",
                    "2026-08-01T10:00:00+09:00Z",
                    "20260230T100000+0900",
                    "2025-W53-1T10:00+09",
                    "20260801T250000+0900",
                    "2026-08-01T10:00:00+24:00",
                    "2026-W31-250000+09",
                    "10:00:00Z",
                    "08/01 10:00:00Z",
                    "August 2026 10:00:00Z",
                    "08/01 +09:00",
                    "August 2026 +09:00",
                    "2026-02-30 +09:00",
                    "10:00:00+09:00",
                })
                {
                    cases.Add(version, field, timestamp);
                }
            }
            cases.Add(version, "playedAt", "2026-08-01 10:00:00");
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(InvalidRequiredPlayFields))]
    public void Missing_integer_or_invalid_timestamp_rejects_restore_before_database_changes(
        int version,
        string field,
        string? invalidTimestamp)
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("backup-play", "2026-08-01T10:00:00+09:00", 900_000, 1_000);
        var backupPath = Path.Combine(fixture.DirectoryPath, "personal-score-backup.json");
        var service = new PersonalScoreDataBackupService();
        Assert.True(service.CreateBackup(fixture.ScorePath, backupPath).Succeeded);
        var root = JsonNode.Parse(File.ReadAllText(backupPath))!.AsObject();
        root["formatVersion"] = version;
        var play = root["plays"]![0]!.AsObject();
        if (invalidTimestamp is null)
        {
            Assert.True(play.Remove(field));
        }
        else
        {
            play[field] = invalidTimestamp;
        }
        WriteBackup(backupPath, root);
        fixture.AddPlay("current-play", "2026-08-02T10:00:00+09:00", 990_000, 1_300);
        AddAnalysisLog(fixture, "current-play");
        var before = SHA256.HashData(File.ReadAllBytes(fixture.ScorePath));

        var result = service.RestoreBackup(fixture.ScorePath, backupPath);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.PlayCount);
        Assert.Contains("現在のデータは変更していません。", result.Message, StringComparison.Ordinal);
        if (invalidTimestamp is not null)
        {
            Assert.Contains(field == "playedAt" ? "プレー日時" : "保存日時", result.Message, StringComparison.Ordinal);
        }
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));
        using var connection = OpenReadOnly(fixture.ScorePath);
        Assert.Equal(2L, CountRows(connection, "plays"));
        Assert.Equal(2L, CountRows(connection, "source_captures"));
        Assert.Equal(1L, CountRows(connection, "analysis_logs"));
    }

    public static TheoryData<int, string, string> FormalWriterTimestampForms()
    {
        var cases = new TheoryData<int, string, string>();
        foreach (var version in new[] { 1, 2 })
        {
            foreach (var timestamp in new[]
            {
                "2026-08-01T10:00:00+0900",
                "2026-08-01T10:00:00.123456789+09:00",
                "2026-08-01T10:00:00.123456789Z",
                "2026-08-01 10:00:00+0900",
                "2026-08-01T10:00:00+09",
                "2026-08-01T10:00+09:00",
                "2026-08-01T10:00:00,123456-0400",
                "20260801T100000+0900",
                "2026-W31-6T10:00:00+09:00",
                "2026-08-01T10:00:00+090001",
                "2026-08-01T10:00:00+15:00",
                "2026-08-01T10:00:00+09:00:00.5",
                "2026-08-01T10+09",
                "2026-08-01X10:00:00+09:00",
                "2026-08-01🎮10:00:00+09:00",
                "2026W316T1000+09",
                "2026-W31T10+09",
                "2026-08-01T10:00:00+09:99",
                "2026-08-01T10:00:00.Z",
                "2026-08-01T10:00:00,Z",
                "2026-08-01T10:00:00.+09:00",
                "2026-08-01T10:00:00,+0900",
                "2026-08-01T10.+09",
                "2026-08-01T10:00,+09:00:01",
                "20260801T100000.Z",
                "2026-W31-6T10:00:00,+09:00",
                "08/01/2026 10:00:00+09:00",
                "Aug 1, 2026 10:00:00+09:00",
                "08.01.2026 10:00:00+09:00",
                "Sat, 01 Aug 2026 10:00:00 +0900",
                "08/01/26 10:00 PM+09:00",
                "10:00:00 08/01/2026+09:00",
                "10:00:00+09:00 2026-08-01",
                "2026-08-01+09:00 10:00:00",
                "+09:00 2026-08-01 10:00:00",
                "+09:00 10:00:00 2026-08-01",
                "08/01/2026 10: 00:00 +09:00",
                "08 / 01 / 2026 10 : 00 : 00 +09:00",
                "08/01/2026 10:0:0+09:00",
                "08/01/2026 10: 0: 0 +9:0",
                "08/01/2026 10:00:00+09:0",
                "10:00:00 2026-08-01 GMT",
                "08/01/2026 +09:00",
                "08.01.2026 +09:00",
                "Aug 1, 2026 +09:00",
                "2026-08-01Z",
                "+09:00 2026-08-01",
                "08 / 01 / 2026 +9:0",
            })
            {
                cases.Add(version, timestamp, timestamp);
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(FormalWriterTimestampForms))]
    [InlineData(1, "2026-08-01T10:00:00+09:00", "2026-08-01 01:00:00")]
    [InlineData(2, "2026-08-01T10:00:00+09:00", "2026-08-01 01:00:00")]
    [InlineData(1, "2026-08-01T01:00:00.1234567Z", "2026-08-01T10:00:00.1234567+09:00")]
    [InlineData(2, "2026-08-01T01:00:00.1234567Z", "2026-08-01T10:00:00.1234567+09:00")]
    [InlineData(1, "2026-08-01T10:00:00.123456-04:00", "2026-08-01T14:00:00.1Z")]
    [InlineData(2, "2026-08-01T10:00:00.123456-04:00", "2026-08-01T14:00:00.1Z")]
    public void Valid_restore_roundtrips_zero_null_and_supported_timestamps(
        int version,
        string playedAt,
        string savedAt)
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("backup-play", playedAt, 0, 0);
        fixture.ExecuteScoreSql(
            $"UPDATE plays SET max_combo = 0, marvelous = 0, perfect = 0, great = 0, " +
            $"good = 0, miss = 0, created_at = '{savedAt}';");
        var backupPath = Path.Combine(fixture.DirectoryPath, "personal-score-backup.json");
        var service = new PersonalScoreDataBackupService();
        Assert.True(service.CreateBackup(fixture.ScorePath, backupPath).Succeeded);
        var root = JsonNode.Parse(File.ReadAllText(backupPath))!.AsObject();
        root["formatVersion"] = version;
        WriteBackup(backupPath, root);
        fixture.AddPlay("current-play", "2026-08-02T10:00:00+09:00", 990_000, 1_300);
        AddAnalysisLog(fixture, "current-play");

        var result = service.RestoreBackup(fixture.ScorePath, backupPath);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.PlayCount);
        var restoredPath = Path.Combine(fixture.DirectoryPath, "restored-backup.json");
        Assert.True(service.CreateBackup(fixture.ScorePath, restoredPath).Succeeded);
        var restored = JsonNode.Parse(File.ReadAllText(restoredPath))!;
        Assert.True(JsonNode.DeepEquals(root["plays"], restored["plays"]));
        var restoredPlay = restored["plays"]![0]!;
        Assert.Null(restoredPlay["flareRank"]);
        Assert.Null(restoredPlay["ok"]);
        Assert.Null(restoredPlay["calories"]);
        Assert.Equal(playedAt, restoredPlay["playedAt"]!.GetValue<string>());
        Assert.Equal(savedAt, restoredPlay["savedAt"]!.GetValue<string>());
        using var connection = OpenReadOnly(fixture.ScorePath);
        Assert.Equal(3L, CountRows(connection, "source_captures"));
        Assert.Equal(1L, CountRows(connection, "analysis_logs"));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT play_id FROM analysis_logs WHERE analysis_id = 'analysis-current';";
        Assert.Equal(DBNull.Value, command.ExecuteScalar());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Empty_valid_backup_restores_without_removing_source_captures_or_analysis_logs(int version)
    {
        using var fixture = new DatabaseFixture();
        var backupPath = Path.Combine(fixture.DirectoryPath, "empty-backup.json");
        var service = new PersonalScoreDataBackupService();
        Assert.True(service.CreateBackup(fixture.ScorePath, backupPath).Succeeded);
        var root = JsonNode.Parse(File.ReadAllText(backupPath))!.AsObject();
        root["formatVersion"] = version;
        WriteBackup(backupPath, root);
        fixture.AddPlay("current-play", "2026-08-02T10:00:00+09:00", 990_000, 1_300);
        AddAnalysisLog(fixture, "current-play");

        var result = service.RestoreBackup(fixture.ScorePath, backupPath);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(0, result.PlayCount);
        using var connection = OpenReadOnly(fixture.ScorePath);
        Assert.Equal(0L, CountRows(connection, "plays"));
        Assert.Equal(1L, CountRows(connection, "source_captures"));
        Assert.Equal(1L, CountRows(connection, "analysis_logs"));
    }

    [Fact]
    public void Invalid_or_unsupported_restore_leaves_current_data_unchanged()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "existing-play",
            "2026-08-01T10:00:00+00:00",
            920_000,
            1_050);
        var service = new PersonalScoreDataBackupService();
        var before = SHA256.HashData(File.ReadAllBytes(fixture.ScorePath));
        var invalidPath = Path.Combine(fixture.DirectoryPath, "invalid.json");
        File.WriteAllText(invalidPath, "{\"format\":", new System.Text.UTF8Encoding(false));

        var invalid = service.RestoreBackup(fixture.ScorePath, invalidPath);

        Assert.False(invalid.Succeeded);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));

        var unsupportedPath = Path.Combine(fixture.DirectoryPath, "unsupported.json");
        File.WriteAllText(
            unsupportedPath,
            "{\"format\":\"ddrgp.personal-score-data\",\"formatVersion\":999,\"createdAt\":\"2026-08-01T00:00:00Z\",\"plays\":[]}",
            new System.Text.UTF8Encoding(false));

        var unsupported = service.RestoreBackup(fixture.ScorePath, unsupportedPath);

        Assert.False(unsupported.Succeeded);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));
        var data = new ScoreViewerRepository().Load(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath);
        Assert.Single(data.Plays);
        Assert.Equal("existing-play", data.Plays[0].PlayId);
    }

    [Fact]
    public void View_model_reports_bundled_data_and_personal_data_state()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddMasterSongAndChart(
            "song-2",
            "SECOND SONG",
            "Artist",
            "chart-2");
        fixture.AddPlay(
            "saved-play",
            "2026-08-01T10:00:00+00:00",
            950_000,
            1_100);
        var paths = new ViewerDatabasePaths(
            ViewerDatabaseEnvironment.Development,
            fixture.DirectoryPath,
            fixture.MasterPath,
            fixture.CatalogPath,
            fixture.ScorePath,
            Path.Combine(fixture.DirectoryPath, "evaluation.db"),
            Path.Combine(fixture.DirectoryPath, "data"),
            Path.Combine(fixture.DirectoryPath, "logs"),
            Path.Combine(fixture.DirectoryPath, "viewer-paths.json"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            defaultDatabasePaths: paths);

        viewModel.Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath, persist: false);

        Assert.Equal("1件", viewModel.DataManagementPlayCountDisplay);
        Assert.Equal("1譜面", viewModel.DataManagementBestChartCountDisplay);
        Assert.Equal("正常", viewModel.PersonalScoreDataStatusDisplay);
        Assert.Equal("利用可能", viewModel.BundledDataStatusDisplay);
        Assert.Equal("master-v1", viewModel.BundledDataVersionDisplay);
        Assert.Equal("2譜面", viewModel.BundledChartCountDisplay);
    }

    [Fact]
    public void View_model_reloads_history_and_best_after_restore()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "restore-source",
            "2026-08-01T10:00:00+00:00",
            900_000,
            1_000);
        var backupPath = Path.Combine(fixture.DirectoryPath, "personal-score-backup.json");
        Assert.True(new PersonalScoreDataBackupService()
            .CreateBackup(fixture.ScorePath, backupPath)
            .Succeeded);
        fixture.AddPlay(
            "restore-extra",
            "2026-08-02T10:00:00+00:00",
            990_000,
            1_300);
        var paths = new ViewerDatabasePaths(
            ViewerDatabaseEnvironment.Development,
            fixture.DirectoryPath,
            fixture.MasterPath,
            fixture.CatalogPath,
            fixture.ScorePath,
            Path.Combine(fixture.DirectoryPath, "evaluation.db"),
            Path.Combine(fixture.DirectoryPath, "data"),
            Path.Combine(fixture.DirectoryPath, "logs"),
            Path.Combine(fixture.DirectoryPath, "viewer-paths.json"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            defaultDatabasePaths: paths);
        viewModel.BestBrowseMode = UserSettings.TitleBrowseMode;
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath, persist: false);

        var result = viewModel.RestorePersonalScoreBackup(backupPath);

        Assert.True(result.Succeeded, result.Message);
        var play = Assert.Single(viewModel.Plays);
        Assert.Equal("restore-source", play.PlayId);
        Assert.Equal(900_000, viewModel.ChartBests.Single().BestScore);
        Assert.Equal("個人スコアデータを復元しました。保存済みプレー: 1件。", viewModel.DataManagementStatusMessage);
    }

    private static void WriteBackup(string path, JsonObject root) =>
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new System.Text.UTF8Encoding(false));

    private static void AddAnalysisLog(DatabaseFixture fixture, string playId) =>
        fixture.ExecuteScoreSql(
            $$"""
            INSERT INTO analysis_logs (
              analysis_id, play_id, source_capture_id, analysis_status, save_boundary_status,
              skip_reason, event_type, confirmed_result, duplicate, confirmation_mode,
              identity_signal_status, digit_review_status, analysis_summary_json, log_path, app_version
            ) VALUES (
              'analysis-current', '{{playId}}', 'capture-{{playId}}', 'skipped', 'excluded',
              'ambiguous', 'fixture', 0, 0, 'automatic', 'unresolved', '', '{}',
              'diagnostic.json', 'test');
            """);

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return (long)command.ExecuteScalar()!;
    }
}
