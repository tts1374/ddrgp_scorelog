using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class MainViewModelTests
{
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
        var runner = new StubWorkflowRunner((_, _) => Result(status));
        var viewModel = new MainViewModel(new ScoreViewerRepository(), runner);

        await viewModel.SaveAndReloadAsync("workflow.json", "missing.sqlite", "missing-master.sqlite");

        Assert.Equal(expectedTitle, viewModel.SaveStatusTitle);
        Assert.Empty(viewModel.Plays);
        Assert.False(viewModel.HasData);
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

    private sealed class StubWorkflowRunner(
        Func<string, string, PersonalScoreDbWorkflowResult> run)
        : IPersonalScoreDbWorkflowRunner
    {
        public Task<PersonalScoreDbWorkflowResult> RunAsync(
            string workflowInputPath,
            string scoreDatabasePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(run(workflowInputPath, scoreDatabasePath));
    }
}
