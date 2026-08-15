using System.Security.Cryptography;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ScoreViewerRepositoryTests
{
    [Theory]
    [InlineData("AAA", "Upper")]
    [InlineData("B", "B")]
    [InlineData("C", "C")]
    [InlineData("D", "D")]
    [InlineData("E", "E")]
    public void PlayHistoryItem_maps_rank_to_mock_badge_group(string rank, string expectedGroup)
    {
        Assert.Equal(expectedGroup, PresentationItem(rank: rank).RankBadgeGroup);
    }

    [Theory]
    [InlineData("PFC", "Pfc")]
    [InlineData("GFC", "Gfc")]
    [InlineData("FC", "Fc")]
    [InlineData("FULL COMBO", "Fc")]
    [InlineData("CLEAR", "Clear")]
    [InlineData("MFC", "Mfc")]
    public void PlayHistoryItem_maps_clear_to_mock_badge_group(string clearType, string expectedGroup)
    {
        Assert.Equal(expectedGroup, PresentationItem(clearType: clearType).ClearBadgeGroup);
    }

    [Theory]
    [InlineData("FULL COMBO", "FC")]
    [InlineData("FC", "FC")]
    [InlineData("CLEAR", "CLEAR")]
    public void PlayHistoryItem_uses_compact_clear_display(string clearType, string expectedDisplay)
    {
        Assert.Equal(expectedDisplay, PresentationItem(clearType: clearType).ClearDisplay);
    }

    [Theory]
    [InlineData("I")]
    [InlineData("II")]
    [InlineData("III")]
    [InlineData("IV")]
    [InlineData("V")]
    [InlineData("VI")]
    [InlineData("VII")]
    [InlineData("VIII")]
    [InlineData("IX")]
    [InlineData("EX")]
    public void PlayHistoryItem_maps_every_flare_rank_to_its_badge_group(string flareRank)
    {
        Assert.Equal(flareRank, PresentationItem(flareRank: flareRank).FlareBadgeGroup);
    }

    [Fact]
    public void PlayHistoryItem_uses_plain_dash_when_flare_rank_is_missing()
    {
        var item = PresentationItem(flareRank: null);

        Assert.Equal("None", item.FlareBadgeGroup);
        Assert.Equal("—", item.FlareRankDisplay);
    }

    [Fact]
    public void PlayHistoryItem_exposes_judgement_breakdown_in_display_order()
    {
        var item = PresentationItem();

        Assert.Equal(
            ["MARVELOUS", "PERFECT", "GREAT", "GOOD", "MISS", "MAX COMBO"],
            item.JudgementBreakdown.Select(judgement => judgement.Label));
        Assert.Equal([400, 80, 10, 2, 1, 500], item.JudgementBreakdown.Select(judgement => judgement.Value));
    }

    [Fact]
    public void LoadHome_uses_played_at_0700_period_counts_replays_and_sums_optional_values()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "before-boundary",
            "2026-08-14T06:59:59+09:00",
            900_000,
            1_000,
            ok: 9,
            calories: 99.0);
        fixture.AddPlay(
            "first",
            "2026-08-14T07:00:00+09:00",
            910_000,
            1_100,
            ok: 3,
            calories: 12.3);
        fixture.AddPlay(
            "repeat",
            "2026-08-15T06:59:00+09:00",
            920_000,
            1_200);
        fixture.AddPlay(
            "next-period",
            "2026-08-15T07:00:00+09:00",
            930_000,
            1_300,
            ok: 5,
            calories: 3.4);

        var repository = new ScoreViewerRepository();
        var scoreHashBefore = Hash(fixture.ScorePath);
        var masterHashBefore = Hash(fixture.MasterPath);
        var jst = TimeSpan.FromHours(9);
        var beforeBoundary = repository.LoadHome(
            fixture.ScorePath,
            fixture.MasterPath,
            new DateTimeOffset(2026, 8, 14, 6, 59, 59, jst));
        Assert.Equal("2026/08/13", beforeBoundary.TodaySummary.DateDisplay);
        Assert.Equal(1, beforeBoundary.TodaySummary.PlayCount);
        Assert.Equal(502L, beforeBoundary.TodaySummary.TotalNotes);
        Assert.Equal(99.0, beforeBoundary.TodaySummary.Calories!.Value, 3);

        var currentPeriod = repository.LoadHome(
            fixture.ScorePath,
            fixture.MasterPath,
            new DateTimeOffset(2026, 8, 15, 6, 59, 59, jst));
        Assert.Equal("2026/08/14", currentPeriod.TodaySummary.DateDisplay);
        Assert.Equal(2, currentPeriod.TodaySummary.PlayCount);
        Assert.Equal(989L, currentPeriod.TodaySummary.TotalNotes);
        Assert.Equal(12.3, currentPeriod.TodaySummary.Calories!.Value, 3);
        Assert.Equal(
            "8月14日のDDR GRAND PRIX\n\nプレー数：2\n総ノーツ数：989\n消費カロリー：12.3 kcal",
            currentPeriod.TodaySummary.CopyText);

        var reloaded = new ScoreViewerRepository().LoadHome(
            fixture.ScorePath,
            fixture.MasterPath,
            new DateTimeOffset(2026, 8, 15, 6, 59, 59, jst));
        Assert.Equal(currentPeriod.TodaySummary, reloaded.TodaySummary);

        var nextPeriod = repository.LoadHome(
            fixture.ScorePath,
            fixture.MasterPath,
            new DateTimeOffset(2026, 8, 15, 7, 0, 0, jst));
        Assert.Equal("2026/08/15", nextPeriod.TodaySummary.DateDisplay);
        Assert.Equal(1, nextPeriod.TodaySummary.PlayCount);
        Assert.Equal(498L, nextPeriod.TodaySummary.TotalNotes);
        Assert.Equal(3.4, nextPeriod.TodaySummary.Calories!.Value, 3);
        Assert.Equal(scoreHashBefore, Hash(fixture.ScorePath));
        Assert.Equal(masterHashBefore, Hash(fixture.MasterPath));
    }

    [Fact]
    public void LoadHome_uses_dashes_for_all_missing_values_and_no_plays()
    {
        using var missingValuesFixture = new DatabaseFixture();
        missingValuesFixture.AddPlay(
            "missing-calories",
            "2026-08-14T10:00:00+09:00",
            900_000,
            1_000);
        var repository = new ScoreViewerRepository();
        var missingValues = repository.LoadHome(
            missingValuesFixture.ScorePath,
            missingValuesFixture.MasterPath,
            new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.FromHours(9)));
        Assert.Equal(1, missingValues.TodaySummary.PlayCount);
        Assert.Equal(493L, missingValues.TodaySummary.TotalNotes);
        Assert.Equal("—", missingValues.TodaySummary.CaloriesDisplay);
        Assert.Contains("消費カロリー：—", missingValues.TodaySummary.CopyText);

        using var emptyFixture = new DatabaseFixture();
        var noPlays = repository.LoadHome(
            emptyFixture.ScorePath,
            emptyFixture.MasterPath,
            new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.FromHours(9)));
        Assert.Equal(0, noPlays.TodaySummary.PlayCount);
        Assert.Null(noPlays.TodaySummary.TotalNotes);
        Assert.Null(noPlays.TodaySummary.Calories);
        Assert.Equal("—", noPlays.TodaySummary.TotalNotesDisplay);
        Assert.Equal("—", noPlays.TodaySummary.CaloriesDisplay);
    }

    [Fact]
    public void Load_reads_history_detail_and_chart_bests_without_changing_databases()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("older", "2026-07-12T10:00:00+00:00", 990_000, 2_400);
        fixture.AddPlay("newer", "2026-07-12T11:00:00+00:00", 980_000, 2_500, flareRank: "EX");
        fixture.ExecuteScoreSql(
            "UPDATE plays SET rank = 'AA+', clear_type = 'FC' WHERE play_id = 'older'; " +
            "UPDATE plays SET rank = 'B', clear_type = 'CLEAR' WHERE play_id = 'newer';");
        var scoreHashBefore = Hash(fixture.ScorePath);
        var masterHashBefore = Hash(fixture.MasterPath);

        var data = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(["newer", "older"], data.Plays.Select(play => play.PlayId));
        Assert.Equal("MAX 300", data.Plays[0].SongTitle);
        Assert.Equal("SP", data.Plays[0].PlayStyleDisplay);
        Assert.Equal("EXPERT", data.Plays[0].Difficulty);
        Assert.Equal("SP EXPERT", data.Plays[0].ChartDisplay);
        Assert.Equal(17, data.Plays[0].Level);
        Assert.Equal(500, data.Plays[0].MaxCombo);
        Assert.Equal(400, data.Plays[0].Marvelous);
        Assert.Equal("manual", data.Plays[0].SourceKind);
        Assert.Equal("EX", data.Plays[0].FlareRank);
        Assert.Equal("FLARE EX", data.Plays[0].FlareRankDisplay);

        var best = Assert.Single(data.ChartBests);
        Assert.Equal(990_000, best.BestScore);
        Assert.Equal(2_500, best.BestExScore);
        Assert.Equal(2, best.PlayCount);
        Assert.Equal("2026-07-12T11:00:00+00:00", best.LastPlayedAt);
        Assert.Equal("AA+", best.Rank);
        Assert.Equal("FC", best.ClearType);
        Assert.Null(best.FlareRank);
        Assert.Equal(scoreHashBefore, Hash(fixture.ScorePath));
        Assert.Equal(masterHashBefore, Hash(fixture.MasterPath));
    }

    [Fact]
    public void Load_projects_chart_version_and_best_result_badges_for_the_list()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddMasterSongAndChart(
            "song-2",
            "SECOND SONG",
            "Artist",
            "chart-2",
            version: "DDR WORLD");
        fixture.AddPlay(
            "chart-2-older",
            "2026-07-12T10:00:00+00:00",
            900_000,
            1_900,
            songId: "song-2",
            chartId: "chart-2");
        fixture.AddPlay(
            "chart-2-best",
            "2026-07-12T11:00:00+00:00",
            950_000,
            2_100,
            songId: "song-2",
            chartId: "chart-2");
        fixture.ExecuteScoreSql(
            "UPDATE plays SET rank = 'AA+', clear_type = 'FULL COMBO', flare_rank = 'IX' " +
            "WHERE play_id = 'chart-2-best';");

        var data = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath);
        var best = Assert.Single(data.ChartBests, item => item.ChartId == "chart-2");

        Assert.Equal("DDR WORLD", best.Version);
        Assert.Equal("AA+", best.RankDisplay);
        Assert.Equal("FC", best.ClearDisplay);
        Assert.Equal("Upper", best.RankBadgeGroup);
        Assert.Equal("Fc", best.ClearBadgeGroup);
        Assert.Equal("IX", best.FlareRankDisplay);
        Assert.Equal("IX", best.FlareBadgeGroup);
        Assert.Contains(data.ChartCatalog, item => item.ChartId == "chart-1" && !item.IsPlayed);
    }

    [Fact]
    public void Load_treats_offsetless_schema_timestamp_as_utc_for_display()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("saved", "2026-07-13T10:00:00+00:00", 990_000, 2_400);
        fixture.ExecuteScoreSql(
            "UPDATE plays SET created_at = '2026-07-13 12:00:00' WHERE play_id = 'saved';");

        var play = Assert.Single(
            new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath).Plays);
        var expected = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero)
            .ToLocalTime()
            .ToString("yyyy/MM/dd HH:mm:ss");

        Assert.Equal(expected, play.SavedAtDisplay);
    }

    [Fact]
    public void Load_computes_last_play_by_instant_across_different_offsets()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "lexically-newer-but-earlier", "2026-07-13T00:30:00+09:00", 990_000, 2_400);
        fixture.AddPlay(
            "lexically-older-but-later", "2026-07-12T16:00:00+00:00", 980_000, 2_500);

        var data = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(
            ["lexically-older-but-later", "lexically-newer-but-earlier"],
            data.Plays.Select(play => play.PlayId));
        Assert.Equal("2026-07-12T16:00:00+00:00", Assert.Single(data.ChartBests).LastPlayedAt);
    }

    [Fact]
    public void Load_accepts_compatible_empty_history()
    {
        using var fixture = new DatabaseFixture();

        var data = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath);

        Assert.Empty(data.Plays);
        Assert.Empty(data.ChartBests);
    }

    [Fact]
    public void Load_validates_the_jacket_catalog_read_only_and_returns_its_path()
    {
        using var fixture = new DatabaseFixture();
        var catalogHashBefore = Hash(fixture.CatalogPath);

        var data = new ScoreViewerRepository().Load(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath);

        Assert.Equal(fixture.CatalogPath, data.CatalogDatabasePath);
        Assert.Equal(catalogHashBefore, Hash(fixture.CatalogPath));
    }

    [Fact]
    public void Load_preserves_rows_with_missing_master_reference()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "missing-master", "2026-07-12T10:00:00+00:00", 900_000, 2_000,
            songId: "unknown-song", chartId: "unknown-chart");

        var data = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath);

        var play = Assert.Single(data.Plays);
        Assert.True(play.MasterReferenceMissing);
        Assert.Contains("unknown-song", play.SongTitle, StringComparison.Ordinal);
        Assert.Contains("unknown-chart", play.MasterReferenceStatus, StringComparison.Ordinal);
        Assert.True(Assert.Single(data.ChartBests).MasterReferenceMissing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(100)]
    [InlineData(101)]
    public void Load_reads_only_the_first_recent_play_page(int count)
    {
        using var fixture = new DatabaseFixture();
        AddChronologicalPlays(fixture, count);

        var data = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(Math.Min(count, ScoreViewerRepository.RecentPlayPageSize), data.Plays.Count);
        Assert.Equal(count, data.TotalPlayCount);
        Assert.Equal(
            count == 0 ? null : $"play-{count - 1:000}",
            data.Home?.LatestPlay?.Play.PlayId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(20)]
    [InlineData(21)]
    public void LoadChartDetail_reads_history_pages_and_full_chart_aggregates(int count)
    {
        using var fixture = new DatabaseFixture();
        AddChronologicalPlays(fixture, count);
        var repository = new ScoreViewerRepository();

        var firstPage = repository.LoadChartDetail(
            fixture.ScorePath,
            fixture.MasterPath,
            "song-1",
            "chart-1",
            0,
            ScoreViewerRepository.ChartDetailHistoryPageSize);
        var secondPage = repository.LoadChartDetail(
            fixture.ScorePath,
            fixture.MasterPath,
            "song-1",
            "chart-1",
            ScoreViewerRepository.ChartDetailHistoryPageSize,
            ScoreViewerRepository.ChartDetailHistoryPageSize);

        Assert.Equal(Math.Min(count, 10), firstPage.History.Count);
        Assert.Equal(Math.Max(0, Math.Min(count - 10, 10)), secondPage.History.Count);
        Assert.Equal(count, firstPage.TotalPlayCount);
        Assert.Equal(count, firstPage.AllPlayPoints.Count);
        Assert.Equal(count, secondPage.TotalPlayCount);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public void LoadChartDetail_limits_graph_points_to_the_newest_100_plays(int count)
    {
        using var fixture = new DatabaseFixture();
        AddChronologicalPlays(fixture, count);

        var detail = new ScoreViewerRepository().LoadChartDetail(
            fixture.ScorePath,
            fixture.MasterPath,
            "song-1",
            "chart-1",
            0,
            ScoreViewerRepository.ChartDetailHistoryPageSize);

        Assert.Equal(Math.Min(count, ScoreViewerRepository.ChartDetailGraphPageSize), detail.AllPlayPoints.Count);
        Assert.Equal(count, detail.TotalPlayCount);
        Assert.Equal(Math.Min(count, ScoreViewerRepository.ChartDetailGraphPageSize), detail.BestPlayPoints.Count);
    }

    [Fact]
    public void LoadChartDetail_uses_all_history_for_self_best_delta_when_old_best_is_outside_graph()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("old-best", "2026-01-01T00:00:00+00:00", 900_000, 1_000);
        for (var index = 1; index <= 100; index++)
        {
            fixture.AddPlay(
                $"play-{index:000}",
                DateTimeOffset.Parse("2026-01-01T00:00:00+00:00").AddMinutes(index).ToString("O"),
                index == 100 ? 950_000 : 800_000,
                index == 100 ? 1_500 : 900);
        }

        var detail = new ScoreViewerRepository().LoadChartDetail(
            fixture.ScorePath,
            fixture.MasterPath,
            "song-1",
            "chart-1",
            0,
            ScoreViewerRepository.ChartDetailHistoryPageSize);
        var latest = Assert.Single(detail.AllPlayPoints, play => play.Play.PlayId == "play-100");

        Assert.DoesNotContain(detail.AllPlayPoints, play => play.Play.PlayId == "old-best");
        Assert.Equal(900_000, latest.PreviousScore);
        Assert.True(latest.IsScoreBestUpdate);
        Assert.Equal("↑ +50,000", latest.ScoreBestDeltaDisplay);
        Assert.Contains(detail.BestPlayPoints, play => play.Play.PlayId == "play-100");
    }

    [Fact]
    public void Chronological_play_queries_use_the_effective_order_indexes_without_temp_sort()
    {
        using var fixture = new DatabaseFixture();
        var overallPlan = ExplainQueryPlan(
            fixture.ScorePath,
            "SELECT p.play_id FROM plays p " +
            "LEFT JOIN source_captures sc ON sc.capture_id = p.source_capture_id " +
            "ORDER BY julianday(p.played_at) DESC, p.played_at DESC, p.play_id DESC " +
            "LIMIT 50 OFFSET 50;");
        var chartPlan = ExplainQueryPlan(
            fixture.ScorePath,
            "SELECT p.play_id FROM plays p " +
            "LEFT JOIN source_captures sc ON sc.capture_id = p.source_capture_id " +
            "WHERE p.song_id = 'song-1' AND p.chart_id = 'chart-1' " +
            "ORDER BY julianday(p.played_at) DESC, p.played_at DESC, p.play_id DESC " +
            "LIMIT 10 OFFSET 10;");

        Assert.Contains("idx_plays_played_at_order", overallPlan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idx_plays_song_chart_order", chartPlan, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TEMP B-TREE", overallPlan, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TEMP B-TREE", chartPlan, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CREATE TABLE preview_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);")]
    [InlineData("UPDATE score_db_metadata SET value = 'other' WHERE key = 'schema_name';")]
    [InlineData("PRAGMA user_version = 4;")]
    [InlineData("DELETE FROM schema_migrations;")]
    [InlineData("PRAGMA writable_schema = ON; " +
                "UPDATE sqlite_schema SET sql = REPLACE(sql, " +
                "'CHECK (score BETWEEN 0 AND 1000000 AND score % 10 = 0)', '') WHERE name = 'plays'; " +
                "PRAGMA writable_schema = OFF; PRAGMA schema_version = 2;")]
    public void Load_rejects_incompatible_score_database_without_modifying_it(string mutation)
    {
        using var fixture = new DatabaseFixture();
        fixture.ExecuteScoreSql(mutation);
        var hashBefore = Hash(fixture.ScorePath);

        var exception = Assert.Throws<ViewerDatabaseException>(
            () => new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath));

        Assert.Contains("開けません", exception.UserMessage, StringComparison.Ordinal);
        Assert.Equal(hashBefore, Hash(fixture.ScorePath));
    }

    [Fact]
    public void Load_accepts_sqlite_alter_table_spacing_in_play_schema()
    {
        using var fixture = new DatabaseFixture();
        fixture.ExecuteScoreSql(
            "PRAGMA writable_schema = ON; " +
            "UPDATE sqlite_schema " +
            "SET sql = REPLACE(sql, 'CURRENT_TIMESTAMP,', 'CURRENT_TIMESTAMP ,') " +
            "WHERE type = 'table' AND name = 'plays'; " +
            "PRAGMA writable_schema = OFF; PRAGMA schema_version = 2;");

        var data = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath);

        Assert.Empty(data.Plays);
    }

    [Fact]
    public void Load_reports_read_failure_for_non_sqlite_file()
    {
        using var fixture = new DatabaseFixture();
        var invalidPath = Path.Combine(fixture.DirectoryPath, "invalid.sqlite");
        File.WriteAllText(invalidPath, "not sqlite");
        var hashBefore = Hash(invalidPath);

        var exception = Assert.Throws<ViewerDatabaseException>(
            () => new ScoreViewerRepository().Load(invalidPath, fixture.MasterPath));

        Assert.Contains("読み込めません", exception.UserMessage, StringComparison.Ordinal);
        Assert.Equal(hashBefore, Hash(invalidPath));
    }

    [Fact]
    public void InspectMasterDatabase_distinguishes_missing_compatible_and_incompatible()
    {
        using var fixture = new DatabaseFixture();
        var repository = new ScoreViewerRepository();

        var compatible = repository.InspectMasterDatabase(fixture.MasterPath);
        Assert.Equal(MasterDatabaseStatus.Compatible, compatible.Status);
        Assert.Equal("master-v1", compatible.Version);

        var missing = repository.InspectMasterDatabase(
            Path.Combine(fixture.DirectoryPath, "missing-master.sqlite"));
        Assert.Equal(MasterDatabaseStatus.Missing, missing.Status);

        fixture.ExecuteMasterSql("DROP TABLE charts;");
        var incompatible = repository.InspectMasterDatabase(fixture.MasterPath);
        Assert.Equal(MasterDatabaseStatus.Incompatible, incompatible.Status);
        Assert.False(incompatible.IsCompatible);
    }

    [Fact]
    public void InspectMasterDatabase_does_not_modify_the_selected_file()
    {
        using var fixture = new DatabaseFixture();
        var hashBefore = Hash(fixture.MasterPath);

        _ = new ScoreViewerRepository().InspectMasterDatabase(fixture.MasterPath);

        Assert.Equal(hashBefore, Hash(fixture.MasterPath));
    }

    [Fact]
    public void InspectMasterDatabase_reports_non_sqlite_as_unreadable()
    {
        using var fixture = new DatabaseFixture();
        var invalidPath = Path.Combine(fixture.DirectoryPath, "invalid-master.sqlite");
        File.WriteAllText(invalidPath, "not sqlite");

        var inspection = new ScoreViewerRepository().InspectMasterDatabase(invalidPath);

        Assert.Equal(MasterDatabaseStatus.Unreadable, inspection.Status);
        Assert.Contains("SQLite", inspection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectJacketCatalogDatabase_distinguishes_all_input_states()
    {
        using var fixture = new DatabaseFixture();
        var repository = new ScoreViewerRepository();

        var compatible = repository.InspectJacketCatalogDatabase(fixture.CatalogPath);
        Assert.Equal(MasterDatabaseStatus.Compatible, compatible.Status);
        Assert.Equal("1", compatible.Version);

        var missing = repository.InspectJacketCatalogDatabase(
            Path.Combine(fixture.DirectoryPath, "missing-catalog.sqlite"));
        Assert.Equal(MasterDatabaseStatus.Missing, missing.Status);

        var invalidPath = Path.Combine(fixture.DirectoryPath, "invalid-catalog.sqlite");
        File.WriteAllText(invalidPath, "not sqlite");
        var unreadable = repository.InspectJacketCatalogDatabase(invalidPath);
        Assert.Equal(MasterDatabaseStatus.Unreadable, unreadable.Status);

        fixture.ExecuteCatalogSql("DROP TABLE result_text_features;");
        var incompatible = repository.InspectJacketCatalogDatabase(fixture.CatalogPath);
        Assert.Equal(MasterDatabaseStatus.Incompatible, incompatible.Status);
        Assert.False(incompatible.IsCompatible);
    }

    [Fact]
    public void InspectJacketCatalogDatabase_does_not_modify_the_selected_file()
    {
        using var fixture = new DatabaseFixture();
        var hashBefore = Hash(fixture.CatalogPath);

        _ = new ScoreViewerRepository().InspectJacketCatalogDatabase(fixture.CatalogPath);

        Assert.Equal(hashBefore, Hash(fixture.CatalogPath));
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static PlayHistoryItem PresentationItem(
        string rank = "AAA",
        string clearType = "CLEAR",
        string? flareRank = "I") =>
        new(
            "presentation-fixture",
            "2026-07-13T12:00:00+00:00",
            "2026-07-13T12:00:00+00:00",
            "song-1",
            "chart-1",
            "MAX 300",
            "SINGLE",
            "EXPERT",
            17,
            990_000,
            2_400,
            rank,
            clearType,
            flareRank,
            500,
            400,
            80,
            10,
            2,
            1,
            null,
            null,
            "manual",
            false);

    private static void AddChronologicalPlays(DatabaseFixture fixture, int count)
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
        for (var index = 0; index < count; index++)
        {
            fixture.AddPlay(
                $"play-{index:000}",
                start.AddMinutes(index).ToString("O"),
                800_000 + index * 10,
                1_000 + index);
        }
    }

    private static string ExplainQueryPlan(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        using var reader = command.ExecuteReader();
        var details = new List<string>();
        while (reader.Read())
        {
            details.Add(reader.GetString(3));
        }
        return string.Join("\n", details);
    }
}
