using System.Security.Cryptography;
using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ScoreViewerRepositoryTests
{
    [Fact]
    public void Load_reads_history_detail_and_chart_bests_without_changing_databases()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("older", "2026-07-12T10:00:00+00:00", 990_000, 2_400);
        fixture.AddPlay("newer", "2026-07-12T11:00:00+00:00", 980_000, 2_500);
        var scoreHashBefore = Hash(fixture.ScorePath);
        var masterHashBefore = Hash(fixture.MasterPath);

        var data = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(["newer", "older"], data.Plays.Select(play => play.PlayId));
        Assert.Equal("MAX 300", data.Plays[0].SongTitle);
        Assert.Equal("SP", data.Plays[0].PlayStyleDisplay);
        Assert.Equal("EXPERT", data.Plays[0].Difficulty);
        Assert.Equal(17, data.Plays[0].Level);
        Assert.Equal(500, data.Plays[0].MaxCombo);
        Assert.Equal(400, data.Plays[0].Marvelous);
        Assert.Equal("manual", data.Plays[0].SourceKind);

        var best = Assert.Single(data.ChartBests);
        Assert.Equal(990_000, best.BestScore);
        Assert.Equal(2_500, best.BestExScore);
        Assert.Equal(2, best.PlayCount);
        Assert.Equal(scoreHashBefore, Hash(fixture.ScorePath));
        Assert.Equal(masterHashBefore, Hash(fixture.MasterPath));
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
                "'CHECK (score BETWEEN 0 AND 1000000)', '') WHERE name = 'plays'; " +
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

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
