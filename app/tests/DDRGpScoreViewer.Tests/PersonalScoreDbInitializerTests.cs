using System.Security.Cryptography;
using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class PersonalScoreDbInitializerTests
{
    [Fact]
    public async Task Initializes_missing_score_db_through_the_formal_schema_boundary()
    {
        using var fixture = new DatabaseFixture();
        var scorePath = Path.Combine(fixture.DirectoryPath, "missing-score.sqlite");
        var initializer = new PersonalScoreDbInitializer();

        var result = await initializer.InitializeIfMissingAsync(scorePath);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Initialized, result.Message);
        var data = new ScoreViewerRepository().Load(
            scorePath,
            fixture.MasterPath,
            fixture.CatalogPath);
        Assert.Empty(data.Plays);
        Assert.Empty(data.ChartBests);
    }

    [Fact]
    public async Task Initializes_zero_byte_score_db_through_the_formal_schema_boundary()
    {
        using var fixture = new DatabaseFixture();
        var scorePath = Path.Combine(fixture.DirectoryPath, "zero-byte-score.sqlite");
        File.WriteAllBytes(scorePath, []);
        var initializer = new PersonalScoreDbInitializer();

        var result = await initializer.InitializeIfMissingAsync(scorePath);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Initialized, result.Message);
        var data = new ScoreViewerRepository().Load(
            scorePath,
            fixture.MasterPath,
            fixture.CatalogPath);
        Assert.Empty(data.Plays);
    }

    [Fact]
    public async Task Leaves_existing_score_db_unchanged_for_read_only_validation()
    {
        using var fixture = new DatabaseFixture();
        var before = SHA256.HashData(File.ReadAllBytes(fixture.ScorePath));
        var initializer = new PersonalScoreDbInitializer();

        var result = await initializer.InitializeIfMissingAsync(fixture.ScorePath);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.Initialized);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));
    }

    [Fact]
    public async Task Initializes_production_score_db_without_repository_root()
    {
        using var fixture = new DatabaseFixture();
        var productionPaths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "production-local-app-data"));
        var initializer = new PersonalScoreDbInitializer();

        var result = await initializer.InitializeIfMissingAsync(productionPaths.ScoreDatabasePath);

        Assert.True(result.Succeeded);
        Assert.True(result.Initialized);
        Assert.True(File.Exists(productionPaths.ScoreDatabasePath));
        var data = new ScoreViewerRepository().Load(
            productionPaths.ScoreDatabasePath,
            fixture.MasterPath,
            fixture.CatalogPath);
        Assert.Empty(data.Plays);
    }
}
