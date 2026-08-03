using System.Security.Cryptography;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
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
    public void Load_reads_history_detail_and_chart_bests_without_changing_databases()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("older", "2026-07-12T10:00:00+00:00", 990_000, 2_400);
        fixture.AddPlay("newer", "2026-07-12T11:00:00+00:00", 980_000, 2_500, flareRank: "EX");
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
        Assert.Equal(scoreHashBefore, Hash(fixture.ScorePath));
        Assert.Equal(masterHashBefore, Hash(fixture.MasterPath));
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
    [InlineData("CREATE TABLE preview_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);")]
    [InlineData("UPDATE score_db_metadata SET value = 'other' WHERE key = 'schema_name';")]
    [InlineData("PRAGMA user_version = 2;")]
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
            "manual",
            false);
}
