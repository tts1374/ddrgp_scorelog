using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class FlareSkillCalculatorTests
{
    [Fact]
    public void Completed_value_table_matches_all_levels_and_flare_ranks()
    {
        string[] ranks = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"];
        int[][] expected =
        [
            [153,162,171,179,188,197,205,214,223,232],
            [164,173,182,192,201,210,220,229,238,248],
            [180,190,200,210,221,231,241,251,261,272],
            [196,207,218,229,240,251,262,273,284,296],
            [217,229,241,254,266,278,291,303,315,328],
            [243,257,271,285,299,312,326,340,354,368],
            [270,285,300,316,331,346,362,377,392,408],
            [307,324,342,359,377,394,411,429,446,464],
            [355,375,395,415,435,455,475,495,515,536],
            [424,448,472,496,520,544,568,592,616,640],
            [492,520,548,576,604,632,660,688,716,744],
            [540,571,601,632,663,693,724,754,785,816],
            [577,610,643,675,708,741,773,806,839,872],
            [609,644,678,713,747,782,816,851,885,920],
            [636,672,708,744,780,816,852,888,924,960],
            [657,694,731,768,806,843,880,917,954,992],
            [673,711,749,787,825,863,901,939,977,1016],
            [689,728,767,806,845,884,923,962,1001,1040],
            [704,744,784,824,864,904,944,984,1024,1064],
        ];

        for (var level = 1; level <= 19; level++)
        {
            for (var rank = 0; rank < ranks.Length; rank++)
            {
                Assert.Equal(expected[level - 1][rank], FlareSkillCalculator.GetCompletedValue(level, ranks[rank]));
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => FlareSkillCalculator.GetCompletedValue(0, "I"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlareSkillCalculator.GetCompletedValue(19, "NORMAL"));
    }

    [Fact]
    public void Null_and_failed_plays_are_excluded_without_becoming_abnormal()
    {
        var chart = Chart("chart", version: "DDR 1st");
        var result = FlareSkillCalculator.Calculate(
            [Play("null", "chart", null), Play("failed", "chart", "EX", clearType: "FAILED")],
            [chart]);

        Assert.Equal(0, result.Single.Total);
        Assert.Equal("NONE", result.Single.Rank.MainRank);
        Assert.Equal(0, result.Single.AbnormalExclusionCount);
        Assert.Empty(result.Single.Classic.TopCharts);
    }

    [Fact]
    public void Chart_best_uses_skill_then_flare_then_newer_time_then_stable_play_id()
    {
        var chart = Chart("chart", level: 17, version: "DDR 1st");
        var result = FlareSkillCalculator.Calculate(
            [
                Play("lower", "chart", "VIII", "2026-01-03T00:00:00+00:00"),
                Play("z-id", "chart", "IX", "2026-01-02T00:00:00+00:00"),
                Play("b-id", "chart", "IX", "2026-01-03T00:00:00+00:00"),
                Play("a-id", "chart", "IX", "2026-01-03T00:00:00+00:00"),
            ],
            [chart]);

        var best = Assert.Single(result.Single.Classic.TopCharts);
        Assert.Equal("a-id", best.PlayId);
        Assert.Equal("IX", best.FlareRank);
        Assert.Equal(977, best.FlareSkill);
    }

    [Fact]
    public void Master_mismatch_missing_removed_invalid_level_and_unknown_version_are_counted_as_abnormal()
    {
        var result = FlareSkillCalculator.Calculate(
            [
                Play("missing", "missing", "I"),
                Play("mismatch", "mismatch", "I", songId: "wrong-song"),
                Play("removed", "removed", "I"),
                Play("level", "level", "I"),
                Play("version", "version", "I"),
                Play("valid", "valid", "I"),
            ],
            [
                Chart("mismatch"),
                Chart("removed", removed: true),
                Chart("level", level: 20),
                Chart("version", version: "DDR GRAND PRIX"),
                Chart("valid", version: "DanceDanceRevolution (2013)"),
            ]);

        Assert.Equal(4, result.Single.AbnormalExclusionCount);
        Assert.Equal(0, result.Double.AbnormalExclusionCount);
        Assert.Equal(1, result.Single.UnknownStyleAbnormalExclusionCount);
        Assert.Equal(1, result.Double.UnknownStyleAbnormalExclusionCount);
        Assert.Equal(5, result.Single.TotalAbnormalExclusionCount);
        Assert.Equal(1, result.Double.TotalAbnormalExclusionCount);
        Assert.Equal(673, result.Single.Total);
        Assert.Single(result.Single.White.TopCharts);
    }

    [Fact]
    public void Abnormal_exclusions_are_separated_by_play_style()
    {
        var result = FlareSkillCalculator.Calculate(
            [
                Play("single-invalid", "single-invalid", "I"),
                Play("double-invalid-1", "double-invalid-1", "I"),
                Play("double-invalid-2", "double-invalid-2", "I"),
                Play("missing", "missing", "I"),
            ],
            [
                Chart("single-invalid", level: 20),
                Chart("double-invalid-1", playStyle: "DOUBLE", level: 20),
                Chart("double-invalid-2", playStyle: "DOUBLE", difficulty: "UNKNOWN"),
            ]);

        Assert.Equal(1, result.Single.AbnormalExclusionCount);
        Assert.Equal(2, result.Double.AbnormalExclusionCount);
        Assert.Equal(1, result.Single.UnknownStyleAbnormalExclusionCount);
        Assert.Equal(1, result.Double.UnknownStyleAbnormalExclusionCount);
        Assert.Equal("算出対象外: 2件（style不明: 1件）", result.Single.AbnormalExclusionDisplay);
        Assert.Equal("算出対象外: 3件（style不明: 1件）", result.Double.AbnormalExclusionDisplay);
    }

    [Fact]
    public void Styles_and_category_boundaries_are_separated()
    {
        var charts = new[]
        {
            Chart("classic-first", version: "DDR 1st"),
            Chart("classic-last", version: "DDR X3 VS 2ndMIX"),
            Chart("white-first", version: "DanceDanceRevolution (2013)"),
            Chart("white-last", version: "DanceDanceRevolution A"),
            Chart("gold-first", version: "DanceDanceRevolution A20"),
            Chart("gold-last", version: "DanceDanceRevolution WORLD"),
            Chart("double", version: "DanceDanceRevolution WORLD", playStyle: "DOUBLE"),
        };
        var plays = charts.Select(chart => Play(chart.ChartId, chart.ChartId, "I", songId: chart.SongId));

        var result = FlareSkillCalculator.Calculate(plays, charts);

        Assert.Equal(2, result.Single.Classic.TopCharts.Count);
        Assert.Equal(2, result.Single.White.TopCharts.Count);
        Assert.Equal(2, result.Single.Gold.TopCharts.Count);
        Assert.Single(result.Double.Gold.TopCharts);
        Assert.Equal(6 * 673, result.Single.Total);
        Assert.Equal(673, result.Double.Total);
    }

    [Fact]
    public void Top_thirty_runner_up_and_equal_value_tie_break_are_deterministic()
    {
        var charts = Enumerable.Range(0, 31)
            .Select(index => Chart(
                $"chart-{index:00}",
                title: index switch { 29 => "ZZZ", 30 => "ZZZ", _ => $"A TITLE {index:00}" },
                difficulty: index == 30 ? "CHALLENGE" : "EXPERT",
                version: "DDR 1st"))
            .ToArray();
        var plays = charts.Select(chart => Play(chart.ChartId, chart.ChartId, "I", songId: chart.SongId));

        var category = FlareSkillCalculator.Calculate(plays, charts).Single.Classic;

        Assert.Equal(30, category.TopCharts.Count);
        Assert.NotNull(category.RunnerUp);
        Assert.Equal(31, category.RunnerUp!.Position);
        Assert.Equal("chart-30", category.RunnerUp.ChartId);
        Assert.Equal(30 * 673, category.Total);
    }

    [Fact]
    public void Total_is_the_sum_of_three_category_top_thirty_values()
    {
        var charts = new[]
        {
            Chart("classic", level: 1, version: "DDR 1st"),
            Chart("white", level: 2, version: "DanceDanceRevolution (2013)"),
            Chart("gold", level: 3, version: "DanceDanceRevolution A20"),
        };
        var result = FlareSkillCalculator.Calculate(
            charts.Select(chart => Play(chart.ChartId, chart.ChartId, "EX", songId: chart.SongId)),
            charts).Single;

        Assert.Equal(232, result.Classic.Total);
        Assert.Equal(248, result.White.Total);
        Assert.Equal(272, result.Gold.Total);
        Assert.Equal(752, result.Total);
    }

    [Fact]
    public void Rank_thresholds_handle_before_exact_and_after_values()
    {
        var thresholds = new[]
        {
            ("NONE","",0), ("NONE","+",500), ("NONE","++",1000), ("NONE","+++",1500),
            ("MERCURY","",2000), ("MERCURY","+",3000), ("MERCURY","++",4000), ("MERCURY","+++",5000),
            ("VENUS","",6000), ("VENUS","+",7000), ("VENUS","++",8000), ("VENUS","+++",9000),
            ("EARTH","",10000), ("EARTH","+",11500), ("EARTH","++",13000), ("EARTH","+++",14500),
            ("MARS","",16000), ("MARS","+",18000), ("MARS","++",20000), ("MARS","+++",22000),
            ("JUPITER","",24000), ("JUPITER","+",26500), ("JUPITER","++",29000), ("JUPITER","+++",31500),
            ("SATURN","",34000), ("SATURN","+",36750), ("SATURN","++",39500), ("SATURN","+++",42250),
            ("URANUS","",45000), ("URANUS","+",48750), ("URANUS","++",52500), ("URANUS","+++",56250),
            ("NEPTUNE","",60000), ("NEPTUNE","+",63750), ("NEPTUNE","++",67500), ("NEPTUNE","+++",71250),
            ("SUN","",75000), ("SUN","+",78750), ("SUN","++",82500), ("SUN","+++",86250),
            ("WORLD","",90000),
        };

        for (var index = 0; index < thresholds.Length; index++)
        {
            var expected = thresholds[index];
            var exact = FlareSkillCalculator.GetRank(expected.Item3);
            Assert.Equal(expected.Item1, exact.MainRank);
            Assert.Equal(expected.Item2, exact.SubRank);
            if (index + 1 < thresholds.Length)
            {
                Assert.Equal(0d, exact.Progress(expected.Item3));
                Assert.Equal(thresholds[index + 1].Item3, exact.NextThreshold);
                Assert.Equal(thresholds[index + 1].Item3 - expected.Item3, exact.PointsToNext(expected.Item3));
                Assert.Equal(thresholds[index + 1].Item3 - expected.Item3 - 1, exact.PointsToNext(expected.Item3 + 1));
                Assert.InRange(exact.Progress(expected.Item3 + 1), 0d, 100d);
            }
            else
            {
                Assert.Null(exact.NextThreshold);
                Assert.Null(exact.PointsToNext(expected.Item3));
                Assert.Equal(100d, exact.Progress(expected.Item3));
            }
            var after = FlareSkillCalculator.GetRank(expected.Item3 + 1);
            Assert.Equal(expected.Item1, after.MainRank);
            Assert.Equal(expected.Item2, after.SubRank);
            if (index > 0)
            {
                var before = FlareSkillCalculator.GetRank(expected.Item3 - 1);
                Assert.Equal(thresholds[index - 1].Item1, before.MainRank);
                Assert.Equal(thresholds[index - 1].Item2, before.SubRank);
            }
        }

        Assert.Null(FlareSkillCalculator.GetRank(100_000).NextThreshold);
    }

    [Fact]
    public void Repository_and_view_model_reload_flare_skill_read_only()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddMasterSongAndChart(
            "flare-song", "FLARE SONG", "Artist", "flare-chart",
            level: 17, version: "DanceDanceRevolution WORLD");
        fixture.AddPlay(
            "flare-i", "2026-09-17T10:00:00+00:00", 900_000, 1_000,
            "flare-song", "flare-chart", "I");
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: new MemoryUserSettingsStore(null));

        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);
        Assert.False(viewModel.IsFlareSkillAvailable);

        viewModel.SetFlareSkillPage(true);
        viewModel.RefreshFlareSkill();
        Assert.True(viewModel.IsFlareSkillAvailable);
        Assert.Equal("673", viewModel.FlareTotalDisplay);

        fixture.AddPlay(
            "flare-ex", "2026-09-17T11:00:00+00:00", 910_000, 1_100,
            "flare-song", "flare-chart", "EX");
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);

        Assert.Equal("1,016", viewModel.FlareTotalDisplay);
        Assert.Equal("flare-ex", Assert.Single(viewModel.FlareGold.TopCharts).PlayId);

        fixture.ExecuteMasterSql("UPDATE charts SET level = 18 WHERE chart_id = 'flare-chart';");
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);
        Assert.Equal("1,040", viewModel.FlareTotalDisplay);
    }

    [Fact]
    public void Unavailable_score_or_master_is_not_reported_as_zero()
    {
        using var fixture = new DatabaseFixture();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: new MemoryUserSettingsStore(null));

        viewModel.Load(Path.Combine(fixture.DirectoryPath, "missing.sqlite"), fixture.MasterPath, persist: false);

        Assert.False(viewModel.IsFlareSkillAvailable);
        Assert.Equal(System.Windows.Visibility.Visible, viewModel.FlareSkillUnavailableVisibility);
        Assert.Contains("読み込め", viewModel.FlareSkillUnavailableMessage);

        fixture.ExecuteMasterSql("ALTER TABLE charts RENAME COLUMN is_removed TO removed_legacy;");
        var masterFailureViewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: new MemoryUserSettingsStore(null));
        masterFailureViewModel.Load(fixture.ScorePath, fixture.MasterPath, persist: false);

        Assert.False(masterFailureViewModel.IsFlareSkillAvailable);
        Assert.Equal(System.Windows.Visibility.Visible, masterFailureViewModel.FlareSkillUnavailableVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, masterFailureViewModel.FlareSkillContentVisibility);
    }

    private static FlareSkillPlayInput Play(
        string playId,
        string chartId,
        string? flareRank,
        string playedAt = "2026-09-17T00:00:00+00:00",
        string clearType = "CLEAR",
        string? songId = null) =>
        new(playId, playedAt, songId ?? $"song-{chartId}", chartId, clearType, flareRank);

    private static FlareSkillChartInput Chart(
        string chartId,
        string? title = null,
        string version = "DDR 1st",
        string playStyle = "SINGLE",
        string difficulty = "EXPERT",
        int level = 17,
        bool removed = false) =>
        new(chartId, $"song-{chartId}", title ?? chartId, version, playStyle, difficulty, level, removed);
}
