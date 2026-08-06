using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void Load_projects_home_summary_recent_plays_and_best_updates()
    {
        using var fixture = new DatabaseFixture();
        var now = DateTimeOffset.Now;
        fixture.AddPlay("first", now.AddMinutes(-60).ToString("O"), 900_000, 1_000);
        fixture.AddPlay("score-update", now.AddMinutes(-50).ToString("O"), 950_000, 1_100);
        fixture.AddPlay("ex-update", now.AddMinutes(-40).ToString("O"), 940_000, 1_200);
        fixture.AddPlay("lower", now.AddMinutes(-30).ToString("O"), 930_000, 1_150);
        fixture.AddPlay("tie", now.AddMinutes(-20).ToString("O"), 950_000, 1_200);
        fixture.AddPlay("latest", now.AddMinutes(-10).ToString("O"), 960_000, 1_250);
        fixture.ExecuteScoreSql(
            "UPDATE plays SET created_at = '" +
            DateTimeOffset.UtcNow.ToString("O") +
            "'; " +
            "UPDATE plays SET clear_type = 'FC' WHERE play_id IN ('score-update', 'latest'); " +
            "UPDATE plays SET clear_type = 'PFC' WHERE play_id = 'ex-update';");

        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);

        Assert.Equal(6, viewModel.HomeTodayPlayCount);
        Assert.Equal(2, viewModel.HomeTodayScoreUpdateCount);
        Assert.Equal(3, viewModel.HomeTodayExScoreUpdateCount);
        Assert.Equal(3, viewModel.HomeTodayFullComboCount);
        Assert.Equal("latest", viewModel.HomeLatestPlay?.Play.PlayId);
        Assert.Equal(5, viewModel.HomeRecentPlays.Count);
        Assert.Equal(
            ["tie", "lower", "ex-update", "score-update", "first"],
            viewModel.HomeRecentPlays.Select(play => play.Play.PlayId));
        Assert.Equal(3, viewModel.HomeBestUpdates.Count);
        Assert.Equal(
            ["latest", "ex-update", "score-update"],
            viewModel.HomeBestUpdates.Select(play => play.Play.PlayId));

        Assert.NotNull(viewModel.HomeLatestPlay);
        var latest = viewModel.HomeLatestPlay!;
        Assert.Equal(950_000, latest.PreviousScore);
        Assert.Equal(1_200, latest.PreviousExScore);
        Assert.Equal("Up", latest.ScoreBestDeltaGroup);
        Assert.Equal("Up", latest.ExScoreBestDeltaGroup);

        var first = viewModel.HomeRecentPlays.Single(play => play.Play.PlayId == "first");
        Assert.Equal("First", first.ScoreBestDeltaGroup);
        Assert.Equal("初プレー", first.ScoreBestDeltaDisplay);
    }

    [Fact]
    public void Load_projects_unplayed_master_charts_when_score_db_has_no_plays()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddMasterSongAndChart(
            "song-unplayed",
            "UNPLAYED SONG",
            "Artist",
            "chart-unplayed",
            version: "DDR WORLD");

        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);

        Assert.Empty(viewModel.Plays);
        Assert.Equal(2, viewModel.ChartBestTotalCount);
        Assert.All(viewModel.ChartBests, item => Assert.False(item.IsPlayed));
        Assert.Contains(viewModel.ChartBests, item => item.ChartId == "chart-1");
        Assert.Contains(viewModel.ChartBests, item => item.ChartId == "chart-unplayed");
    }

    [Fact]
    public void Chart_detail_uses_only_the_selected_chart_and_supports_unplayed_charts()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddMasterSongAndChart(
            "song-unplayed",
            "UNPLAYED SONG",
            "Artist",
            "chart-unplayed",
            difficulty: "BEGINNER",
            level: 3,
            version: "DDR WORLD");
        fixture.AddPlay("first", "2026-07-10T10:00:00+00:00", 900_000, 1_000);
        fixture.AddPlay("score-best", "2026-07-11T10:00:00+00:00", 950_000, 1_100);
        fixture.AddPlay("ex-best", "2026-07-12T10:00:00+00:00", 940_000, 1_300);
        fixture.ExecuteScoreSql(
            "UPDATE plays SET rank = 'AA+', clear_type = 'FC', flare_rank = 'IX' " +
            "WHERE play_id = 'score-best'; " +
            "UPDATE plays SET rank = 'B', clear_type = 'PFC', flare_rank = 'VI' " +
            "WHERE play_id = 'ex-best';");

        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);

        var requested = new List<string>();
        viewModel.ChartBestSelectionRequested += chart => requested.Add(chart.ChartId);
        var playedChart = viewModel.ChartBests.Single(item => item.ChartId == "chart-1");
        viewModel.SelectChartBest(playedChart);
        viewModel.SelectChartBest(playedChart);

        Assert.Equal(["chart-1", "chart-1"], requested);
        Assert.Equal("MAX 300", viewModel.ChartDetailSongTitle);
        Assert.Equal("950,000", viewModel.ChartDetailBestScoreDisplay);
        Assert.Equal("1,300", viewModel.ChartDetailBestExScoreDisplay);
        Assert.Equal("AA+", viewModel.ChartDetailRankDisplay);
        Assert.Equal("FC", viewModel.ChartDetailClearDisplay);
        Assert.Equal("IX", viewModel.ChartDetailFlareRankDisplay);
        Assert.Equal("3回", viewModel.ChartDetailPlayCountDisplay);
        Assert.Equal("2回", viewModel.ChartDetailFullComboCountDisplay);
        Assert.Equal(
            ["ex-best", "score-best", "first"],
            viewModel.ChartDetailHistory.Select(play => play.Play.PlayId));
        Assert.Equal(
            ["first", "score-best", "ex-best"],
            viewModel.ChartDetailAllPlayPoints.Select(play => play.Play.PlayId));
        Assert.Equal(
            ["first", "score-best"],
            viewModel.ChartDetailBestPlayPoints.Select(play => play.Play.PlayId));
        Assert.Equal("↓ -10,000", viewModel.ChartDetailLatestPlay?.ScoreBestDeltaDisplay);
        Assert.Equal("↑ +200", viewModel.ChartDetailLatestPlay?.ExScoreBestDeltaDisplay);
        Assert.Contains("2026/07/11", viewModel.ChartDetailScoreBestAtDisplay);
        Assert.Contains("2026/07/12", viewModel.ChartDetailExScoreBestAtDisplay);

        viewModel.SetChartDetailGraphMode("自己ベスト推移");
        Assert.Equal("自己ベスト推移", viewModel.ChartDetailGraphMode);
        Assert.Equal(
            ["first", "score-best"],
            viewModel.ChartDetailGraphPlays.Select(play => play.Play.PlayId));

        viewModel.SelectChartBest(viewModel.ChartBests.Single(item => item.ChartId == "chart-unplayed"));

        Assert.Equal("UNPLAYED SONG", viewModel.ChartDetailSongTitle);
        Assert.Equal("—", viewModel.ChartDetailBestScoreDisplay);
        Assert.Equal("—", viewModel.ChartDetailBestExScoreDisplay);
        Assert.Equal("—", viewModel.ChartDetailRankDisplay);
        Assert.Equal("—", viewModel.ChartDetailClearDisplay);
        Assert.Equal("—", viewModel.ChartDetailFlareRankDisplay);
        Assert.Empty(viewModel.ChartDetailHistory);
        Assert.Empty(viewModel.ChartDetailGraphPlays);
        Assert.Equal("0回", viewModel.ChartDetailPlayCountDisplay);
        Assert.Equal(
            System.Windows.Visibility.Visible,
            viewModel.ChartDetailEmptyVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, viewModel.ChartDetailRankBadgeVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, viewModel.ChartDetailClearBadgeVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, viewModel.ChartDetailFlareBadgeVisibility);
    }

    [Fact]
    public void Selecting_chart_detail_does_not_reset_the_best_list_state()
    {
        using var fixture = new DatabaseFixture();
        for (var index = 2; index <= 61; index++)
        {
            var songId = $"song-{index}";
            var chartId = $"chart-{index}";
            fixture.AddMasterSongAndChart(
                songId,
                $"SONG {index:00}",
                "Artist",
                chartId,
                difficulty: "EXPERT",
                level: 17,
                version: "DDR WORLD");
            fixture.AddPlay(
                $"play-{index}",
                DateTimeOffset.UtcNow.AddMinutes(-index).ToString("O"),
                800_000 + index * 1_000,
                1_000 + index,
                songId,
                chartId);
        }

        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);
        viewModel.LoadMoreChartBests();
        viewModel.BestDifficultyFilter = "EXPERT";
        viewModel.BestSortFilter = "曲名（昇順）";
        viewModel.LoadMoreChartBests();

        var displayedCount = viewModel.ChartBestDisplayedCount;
        var selected = viewModel.ChartBests[10];
        viewModel.SelectChartBest(selected);

        Assert.Equal("EXPERT", viewModel.BestDifficultyFilter);
        Assert.Equal("曲名（昇順）", viewModel.BestSortFilter);
        Assert.Equal(displayedCount, viewModel.ChartBestDisplayedCount);
        Assert.Equal(displayedCount, viewModel.ChartBests.Count);
        Assert.Equal(selected.ChartId, viewModel.SelectedChartBest?.ChartId);
    }

    [Fact]
    public async Task SaveAndReloadAsync_reflects_only_committed_saved_play()
    {
        using var fixture = new DatabaseFixture();
        var runner = new StubWorkflowRunner((_, databasePath) =>
        {
            Assert.Equal(fixture.ScorePath, databasePath);
            fixture.AddPlay("saved-by-workflow", "2026-07-13T12:00:00+00:00", 999_000, 2_600);
            return Result("saved", playId: "saved-by-workflow", written: true);
        });
        var viewModel = new MainViewModel(new ScoreViewerRepository(), runner);

        await viewModel.SaveAndReloadAsync("workflow.json", fixture.ScorePath, fixture.MasterPath);

        Assert.Equal("プレーを保存しました", viewModel.SaveStatusTitle);
        Assert.Equal("saved-by-workflow", Assert.Single(viewModel.Plays).PlayId);
        Assert.Single(viewModel.ChartBests);
        Assert.True(viewModel.HasData);
    }

    [Fact]
    public async Task SaveAndReloadAsync_preserves_selected_chart_detail_and_refreshes_it()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("before-save", "2026-07-13T10:00:00+00:00", 900_000, 1_000);
        var runner = new StubWorkflowRunner((_, databasePath) =>
        {
            Assert.Equal(fixture.ScorePath, databasePath);
            fixture.AddPlay("after-save", "2026-07-13T12:00:00+00:00", 950_000, 1_200);
            return Result("saved", playId: "after-save", written: true);
        });
        var viewModel = new MainViewModel(new ScoreViewerRepository(), runner);
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);
        viewModel.SelectChartBest(Assert.Single(viewModel.ChartBests));

        Assert.Equal("before-save", viewModel.ChartDetailLatestPlay?.Play.PlayId);

        await viewModel.SaveAndReloadAsync("workflow.json", fixture.ScorePath, fixture.MasterPath);

        Assert.Equal("chart-1", viewModel.SelectedChartBest?.ChartId);
        Assert.Equal("after-save", viewModel.ChartDetailLatestPlay?.Play.PlayId);
        Assert.Equal("950,000", viewModel.ChartDetailBestScoreDisplay);
        Assert.Equal("2回", viewModel.ChartDetailPlayCountDisplay);
        Assert.Contains(
            viewModel.ChartDetailHistory,
            play => play.Play.PlayId == "after-save");
    }

    [Theory]
    [InlineData("excluded", "保存対象外です")]
    [InlineData("duplicate", "重複するプレーです")]
    [InlineData("unresolved", "正式保存値が未解決です")]
    [InlineData("invalid", "workflow入力が不正です")]
    [InlineData("db_rejected", "保存先DBを使用できません")]
    [InlineData("artifact_created_db_failed", "DB保存に失敗しました")]
    public async Task SaveAndReloadAsync_maps_non_saved_status_without_readback(
        string status,
        string expectedTitle)
    {
        using var fixture = new DatabaseFixture();
        var runner = new StubWorkflowRunner((_, _) => Result(status));
        var viewModel = new MainViewModel(new ScoreViewerRepository(), runner);

        await viewModel.SaveAndReloadAsync("workflow.json", "missing.sqlite", fixture.MasterPath);

        Assert.Equal(expectedTitle, viewModel.SaveStatusTitle);
        Assert.Empty(viewModel.Plays);
        Assert.False(viewModel.HasData);
    }

    [Fact]
    public async Task SaveAndReloadAsync_rejects_missing_master_before_starting_workflow()
    {
        var runner = new StubWorkflowRunner((_, _) =>
            throw new InvalidOperationException("workflow must not start"));
        var viewModel = new MainViewModel(new ScoreViewerRepository(), runner);

        await viewModel.SaveAndReloadAsync(
            "workflow.json",
            "missing.sqlite",
            "missing-master.sqlite");

        Assert.Equal(0, runner.CallCount);
        Assert.Equal(MasterDatabaseStatus.Missing, viewModel.MasterDatabaseStatus);
        Assert.Contains("保存を開始しません", viewModel.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAndReloadAsync_rejects_missing_jacket_catalog_before_starting_workflow()
    {
        using var fixture = new DatabaseFixture();
        var runner = new StubWorkflowRunner((_, _) =>
            throw new InvalidOperationException("workflow must not start"));
        var viewModel = new MainViewModel(new ScoreViewerRepository(), runner);

        await viewModel.SaveAndReloadAsync(
            "workflow.json",
            fixture.ScorePath,
            fixture.MasterPath,
            Path.Combine(fixture.DirectoryPath, "missing-catalog.sqlite"));

        Assert.Equal(0, runner.CallCount);
        Assert.Equal(MasterDatabaseStatus.Compatible, viewModel.MasterDatabaseStatus);
        Assert.Equal(MasterDatabaseStatus.Missing, viewModel.CatalogDatabaseStatus);
        Assert.Contains("解析・正式保存を開始しません", viewModel.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreSavedPaths_ignores_arbitrary_saved_paths_even_in_the_same_environment()
    {
        using var fixture = new DatabaseFixture();
        var store = new MemoryViewerPathStore(new ViewerPathSelection(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath,
            ViewerDatabaseEnvironment.Development));
        var defaults = ViewerDatabasePaths.ForDevelopment(
            Path.Combine(fixture.DirectoryPath, "configured-root"));
        var first = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner((_, _) => Result("excluded")),
            pathStore: store,
            defaultDatabasePaths: defaults);
        first.RestoreSavedPaths();

        Assert.Equal(defaults.ScoreDatabasePath, first.ScoreDatabasePath);
        Assert.Equal(defaults.MasterDatabasePath, first.MasterDatabasePath);
        Assert.Equal(defaults.JacketCatalogDatabasePath, first.CatalogDatabasePath);
        Assert.Equal(MasterDatabaseStatus.Missing, first.MasterDatabaseStatus);
        Assert.Equal(MasterDatabaseStatus.Missing, first.CatalogDatabaseStatus);
        Assert.False(first.HasData);
        Assert.Contains("既定DBだけを使用", first.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreSavedPaths_reuses_only_environment_defaults_and_revalidates_them_without_writing()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("existing", "2026-07-13T12:00:00+00:00", 999_000, 2_600);
        var store = new MemoryViewerPathStore(new ViewerPathSelection(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath,
            ViewerDatabaseEnvironment.Development));
        var paths = ConfiguredPaths(fixture);
        var scoreHashBefore = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));

        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner((_, _) => Result("excluded")),
            pathStore: store,
            defaultDatabasePaths: paths);
        viewModel.RestoreSavedPaths();

        Assert.Equal(MasterDatabaseStatus.Compatible, viewModel.MasterDatabaseStatus);
        Assert.Equal(MasterDatabaseStatus.Compatible, viewModel.CatalogDatabaseStatus);
        Assert.Equal(fixture.CatalogPath, viewModel.CatalogDatabasePath);
        Assert.Equal("existing", Assert.Single(viewModel.Plays).PlayId);
        Assert.Equal(
            scoreHashBefore,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixture.ScorePath))));

        fixture.ExecuteMasterSql("DROP TABLE charts;");
        var restarted = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner((_, _) => Result("excluded")),
            pathStore: store,
            defaultDatabasePaths: paths);
        restarted.RestoreSavedPaths();

        Assert.Equal(MasterDatabaseStatus.Incompatible, restarted.MasterDatabaseStatus);
        Assert.False(restarted.HasData);
        Assert.Equal(MonitoringResultSummary.Empty, restarted.MonitoringResults);
        Assert.Contains("既定path", restarted.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreSavedPaths_does_not_implicitly_restore_a_path_from_the_other_environment()
    {
        using var fixture = new DatabaseFixture();
        var defaults = ViewerDatabasePaths.ForDevelopment(
            Path.Combine(fixture.DirectoryPath, "development-checkout"));
        var store = new MemoryViewerPathStore(new ViewerPathSelection(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath,
            ViewerDatabaseEnvironment.Production));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner((_, _) => Result("excluded")),
            pathStore: store,
            defaultDatabasePaths: defaults);

        viewModel.RestoreSavedPaths();

        Assert.Equal(defaults.MasterDatabasePath, viewModel.MasterDatabasePath);
        Assert.Equal(defaults.JacketCatalogDatabasePath, viewModel.CatalogDatabasePath);
        Assert.Equal(MasterDatabaseStatus.Missing, viewModel.MasterDatabaseStatus);
        Assert.False(viewModel.HasData);
    }

    [Fact]
    public void RestoreSavedPaths_initializes_missing_fixed_score_db_and_loads_empty_state()
    {
        using var fixture = new DatabaseFixture();
        var paths = ConfiguredPaths(fixture) with
        {
            ScoreDatabasePath = Path.Combine(fixture.DirectoryPath, "initialized-score.sqlite"),
        };
        var initializer = new StubScoreDatabaseInitializer(path =>
        {
            Assert.Equal(paths.ScoreDatabasePath, path);
            File.Copy(fixture.ScorePath, path);
            return new ScoreDatabaseInitializationResult(
                true,
                true,
                "fixture initialized");
        });
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner((_, _) => Result("excluded")),
            pathStore: new MemoryViewerPathStore(null),
            defaultDatabasePaths: paths,
            scoreDatabaseInitializer: initializer);

        viewModel.RestoreSavedPaths();

        Assert.Equal(1, initializer.CallCount);
        Assert.True(File.Exists(paths.ScoreDatabasePath));
        Assert.Empty(viewModel.Plays);
        Assert.Contains(
            viewModel.ChartBests,
            item => item.ChartId == "chart-1" && !item.IsPlayed);
        Assert.False(viewModel.HasData);
        Assert.Equal("まだプレーデータがありません", viewModel.StatusTitle);
        Assert.Equal(MasterDatabaseStatus.Compatible, viewModel.MasterDatabaseStatus);
        Assert.Equal(MasterDatabaseStatus.Compatible, viewModel.CatalogDatabaseStatus);
    }

    [Fact]
    public void RestoreSavedPaths_does_not_initialize_score_db_when_catalog_is_invalid()
    {
        using var fixture = new DatabaseFixture();
        var paths = ConfiguredPaths(fixture) with
        {
            JacketCatalogDatabasePath = Path.Combine(fixture.DirectoryPath, "missing-catalog.sqlite"),
            ScoreDatabasePath = Path.Combine(fixture.DirectoryPath, "not-created-score.sqlite"),
        };
        var initializer = new StubScoreDatabaseInitializer(
            _ => throw new InvalidOperationException("score initialization must not start"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner((_, _) => Result("excluded")),
            pathStore: new MemoryViewerPathStore(null),
            defaultDatabasePaths: paths,
            scoreDatabaseInitializer: initializer);

        viewModel.RestoreSavedPaths();

        Assert.Equal(0, initializer.CallCount);
        Assert.False(File.Exists(paths.ScoreDatabasePath));
        Assert.Equal(MasterDatabaseStatus.Compatible, viewModel.MasterDatabaseStatus);
        Assert.Equal(MasterDatabaseStatus.Missing, viewModel.CatalogDatabaseStatus);
        Assert.Contains("初期化、解析、正式保存を開始しません", viewModel.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Best_list_filters_and_adds_fifty_rows_at_a_time()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddMasterSongAndChart(
            "song-unplayed",
            "UNPLAYED SONG",
            "Artist",
            "chart-unplayed",
            difficulty: "BEGINNER",
            level: 3,
            version: "DDR WORLD");
        fixture.AddPlay("base-play", "2026-08-01T12:00:00+00:00", 900_000, 1_000);

        for (var index = 2; index <= 61; index++)
        {
            var songId = $"song-{index}";
            var chartId = $"chart-{index}";
            fixture.AddMasterSongAndChart(
                songId,
                $"SONG {index:00}",
                "Artist",
                chartId,
                playStyle: "SINGLE",
                difficulty: index % 3 == 0 ? "EXPERT" : "BASIC",
                level: index % 19 + 1,
                version: index % 2 == 0 ? "DDR WORLD" : "DDR A3");
            fixture.AddPlay(
                $"play-{index}",
                DateTimeOffset.UtcNow.AddMinutes(-index).ToString("O"),
                800_000 + index * 1_000,
                1_000 + index,
                songId,
                chartId);
        }

        fixture.ExecuteScoreSql(
            "UPDATE plays SET rank = 'AA+', clear_type = 'FC', flare_rank = 'EX' " +
            "WHERE play_id = 'play-2'; " +
            "UPDATE plays SET rank = 'A', clear_type = 'PFC' " +
            "WHERE play_id = 'play-3';");

        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);

        Assert.Equal(50, viewModel.ChartBests.Count);
        Assert.Equal(62, viewModel.ChartBestTotalCount);
        Assert.Equal(50, viewModel.ChartBestDisplayedCount);
        Assert.True(viewModel.CanLoadMoreChartBests);

        viewModel.LoadMoreChartBests();
        Assert.Equal(62, viewModel.ChartBests.Count);
        Assert.False(viewModel.CanLoadMoreChartBests);

        viewModel.BestPlayStatusFilter = "未プレー";
        Assert.Single(viewModel.ChartBests);
        Assert.Equal("UNPLAYED SONG", viewModel.ChartBests[0].SongTitle);
        Assert.Equal("表示 1〜1 / 全1譜面", viewModel.ChartBestRangeDisplay);

        viewModel.BestPlayStatusFilter = "すべて";
        viewModel.BestVersionFilter = "DDR WORLD";
        Assert.All(viewModel.ChartBests, item => Assert.Equal("DDR WORLD", item.Version));

        viewModel.BestVersionFilter = "すべて";
        viewModel.BestSongQuery = "SONG 21";
        Assert.Single(viewModel.ChartBests);
        Assert.Equal("SONG 21", viewModel.ChartBests[0].SongTitle);

        viewModel.BestSongQuery = "";
        viewModel.BestLevelFilter = "Lv.17";
        Assert.All(viewModel.ChartBests, item => Assert.Equal("Lv.17", item.LevelDisplay));

        viewModel.ResetBestFilters();
        viewModel.BestDifficultyFilter = "EXPERT";
        Assert.All(viewModel.ChartBests, item => Assert.Equal("EXPERT", item.Difficulty));

        viewModel.ResetBestFilters();
        viewModel.BestRankFilter = "AA";
        Assert.All(
            viewModel.ChartBests,
            item => Assert.True(item.Rank is "AA+" or "AA" or "AA-"));

        viewModel.ResetBestFilters();
        viewModel.BestClearFilter = "FC";
        Assert.All(viewModel.ChartBests, item => Assert.Equal("FC", item.ClearDisplay));

        viewModel.ResetBestFilters();
        viewModel.BestSortFilter = "曲名（昇順）";
        Assert.Equal("MAX 300", viewModel.ChartBests[0].SongTitle);
        Assert.Equal("SONG 02", viewModel.ChartBests[1].SongTitle);

        viewModel.ResetBestFilters();
        Assert.Equal("SINGLE", viewModel.BestPlayStyleFilter);
        Assert.Equal(50, viewModel.ChartBests.Count);
        Assert.Equal("スコア（高い順）", viewModel.BestSortFilter);
    }

    [Fact]
    public void Best_version_options_follow_selected_play_style_and_reset_invalid_selection()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddMasterSongAndChart(
            "song-double",
            "DOUBLE SONG",
            "Artist",
            "chart-double",
            playStyle: "DOUBLE",
            version: "DDR A20");
        fixture.AddPlay(
            "double-play",
            "2026-08-01T12:00:00+00:00",
            900_000,
            1_000,
            songId: "song-double",
            chartId: "chart-double");

        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);

        Assert.DoesNotContain("DDR A20", viewModel.BestVersionOptions);

        viewModel.BestVersionFilter = "DDR GRAND PRIX";
        viewModel.BestPlayStyleFilter = "DOUBLE";
        Assert.Contains("DDR A20", viewModel.BestVersionOptions);
        Assert.Equal("すべて", viewModel.BestVersionFilter);

        viewModel.BestVersionFilter = "DDR A20";
        viewModel.BestPlayStyleFilter = "SINGLE";
        Assert.DoesNotContain("DDR A20", viewModel.BestVersionOptions);
        Assert.Equal("すべて", viewModel.BestVersionFilter);
    }

    [Fact]
    public void Best_version_options_use_release_labels_and_the_requested_order()
    {
        using var fixture = new DatabaseFixture();
        var sourceVersions = new[]
        {
            "2023/04/03配信",
            "DanceDanceRevolution WORLD",
            "DanceDanceRevolution A3",
            "DanceDanceRevolution A20 PL US",
            "DanceDanceRevolution A20",
            "DanceDanceRevolution A",
            "DanceDanceRevolution (2014)",
            "DanceDanceRevolution (2013)",
            "DDR X3 VS 2ndMIX",
            "DDR X2",
            "DDR X",
            "DDR SuperNOVA 2",
            "DDR SuperNOVA",
            "DDR EXTREME",
            "DDRMAX2",
            "DDRMAX",
            "DDR 5thMIX",
            "DDR 4thMIX",
            "DDR 3rdMIX",
            "DDR 2ndMIX",
            "DDR 1st",
        };
        for (var index = 0; index < sourceVersions.Length; index++)
        {
            fixture.AddMasterSongAndChart(
                $"song-version-{index}",
                $"VERSION SONG {index:00}",
                "Artist",
                $"chart-version-{index}",
                version: sourceVersions[index]);
        }

        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);

        Assert.Equal(
            [
                "DDR GRAND PRIX", "DDR WORLD", "DDR A3", "DDR A20 PLUS", "DDR A20", "DDR A",
                "DDR (2014)", "DDR (2013)", "X3 VS 2ndMIX", "X2", "X", "SuperNOVA 2",
                "SuperNOVA", "EXTREME", "DDRMAX2", "DDRMAX", "5thMIX", "4thMIX", "3rdMIX",
                "2ndMIX", "1st",
            ],
            viewModel.BestVersionOptions.Skip(1));

        viewModel.BestVersionFilter = "DDR GRAND PRIX";
        Assert.Contains(viewModel.ChartBests, item => item.SongTitle == "VERSION SONG 00");
    }

    private static PersonalScoreDbWorkflowResult Result(
        string status,
        string? playId = null,
        bool written = false) =>
        new(
            status,
            status == "artifact_created_db_failed" ? "created" : "not_requested",
            status is "saved" or "duplicate" ? "ready" : status,
            written ? "written" : "not_checked",
            written,
            null,
            null,
            playId,
            ["fixture_reason"],
            null,
            "score.sqlite");

    private static ViewerDatabasePaths ConfiguredPaths(DatabaseFixture fixture) =>
        new(
            ViewerDatabaseEnvironment.Development,
            fixture.DirectoryPath,
            fixture.MasterPath,
            fixture.CatalogPath,
            fixture.ScorePath,
            Path.Combine(fixture.DirectoryPath, "evaluation.db"),
            Path.Combine(fixture.DirectoryPath, "data"),
            Path.Combine(fixture.DirectoryPath, "logs"),
            Path.Combine(fixture.DirectoryPath, "viewer-paths.json"));

    private sealed class StubWorkflowRunner(
        Func<string, string, PersonalScoreDbWorkflowResult> run)
        : IPersonalScoreDbWorkflowRunner
    {
        public int CallCount { get; private set; }

        public Task<PersonalScoreDbWorkflowResult> RunAsync(
            string workflowInputPath,
            string scoreDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(run(workflowInputPath, scoreDatabasePath));
        }
    }

    private sealed class StubScoreDatabaseInitializer(
        Func<string, ScoreDatabaseInitializationResult> initialize)
        : IScoreDatabaseInitializer
    {
        public int CallCount { get; private set; }

        public Task<ScoreDatabaseInitializationResult> InitializeIfMissingAsync(
            string scoreDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(initialize(scoreDatabasePath));
        }
    }

    private sealed class MemoryViewerPathStore(ViewerPathSelection? selection) : IViewerPathStore
    {
        public ViewerPathSelection? Load() => selection;

        public void Save(ViewerPathSelection value) => selection = value;
    }
}
