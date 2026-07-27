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
        var initializer = new PythonPersonalScoreDbInitializer(
            "python",
            RepositoryRootLocator.Find());

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
        var initializer = new PythonPersonalScoreDbInitializer(
            "python",
            RepositoryRootLocator.Find());

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
        var initializer = new PythonPersonalScoreDbInitializer(
            "python",
            RepositoryRootLocator.Find());

        var result = await initializer.InitializeIfMissingAsync(fixture.ScorePath);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.Initialized);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));
    }

    [Fact]
    public void ParseResult_reads_preparation_summary_from_pretty_json()
    {
        const string payload = """
            {
              "is_compatible": true,
              "compatibility_errors": [],
              "file_preparation": {
                "initialized": true
              }
            }
            """;

        var result = PythonPersonalScoreDbInitializer.ParseResult(
            payload,
            0,
            "score.sqlite");

        Assert.True(result.Succeeded);
        Assert.True(result.Initialized);
    }
}
