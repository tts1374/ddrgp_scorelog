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
