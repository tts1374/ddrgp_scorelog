using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
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
        Assert.Empty(viewModel.ChartBests);
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
