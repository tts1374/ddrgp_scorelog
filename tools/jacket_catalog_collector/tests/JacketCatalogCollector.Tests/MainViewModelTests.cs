namespace JacketCatalogCollector.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task LoadsProjectionAndUsesOneStatusReasonSetForBothLists()
    {
        var projection = Projection();
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(projection));

        await viewModel.LoadProjectionAsync("master.sqlite", "catalog.sqlite");

        Assert.Equal("master-v1", viewModel.MasterVersion);
        Assert.Equal(2, viewModel.Songs.Count);
        Assert.Single(viewModel.ReviewReferences);
        Assert.Contains("opaque reason", viewModel.ReasonOptions);

        viewModel.SelectedCoverageStatus = "needs_review";
        Assert.Single(viewModel.Songs);
        Assert.Single(viewModel.ReviewReferences);

        viewModel.SelectedReason = "opaque reason";
        Assert.Single(viewModel.Songs);
        Assert.Single(viewModel.ReviewReferences);
    }

    [Fact]
    public async Task ReportsMasterSuccessFailureAndCancellationStates()
    {
        var success = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(Projection()));
        await success.UpdateMasterAsync("master.sqlite");
        Assert.Equal("master更新完了", success.StatusTitle);

        var failure = new MainViewModel(
            new StubMasterUpdateService(new IOException("build failed")),
            new StubProjectionService(Projection()));
        await Assert.ThrowsAsync<IOException>(() => failure.UpdateMasterAsync("master.sqlite"));
        Assert.Equal("master更新失敗", failure.StatusTitle);

        var canceled = new MainViewModel(
            new StubMasterUpdateService(new OperationCanceledException()),
            new StubProjectionService(Projection()));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => canceled.UpdateMasterAsync("master.sqlite"));
        Assert.Equal("master更新取消", canceled.StatusTitle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearsPreviouslyDisplayedProjectionWhenNextLoadFailsOrIsCanceled(
        bool cancel)
    {
        var projectionService = new SequenceProjectionService(
            Projection(),
            cancel ? new OperationCanceledException() : new InvalidOperationException("invalid projection"));
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projectionService);
        await viewModel.LoadProjectionAsync("master-v1.sqlite", "catalog-v1.sqlite");
        viewModel.SelectedCoverageStatus = "needs_review";
        viewModel.SelectedReason = "opaque reason";

        await Assert.ThrowsAnyAsync<Exception>(
            () => viewModel.LoadProjectionAsync("master-v2.sqlite", "catalog-v2.sqlite"));

        Assert.Equal(cancel ? "取消" : "読込失敗", viewModel.StatusTitle);
        Assert.Equal("未選択", viewModel.MasterVersion);
        Assert.Equal("未選択", viewModel.CatalogIdentity);
        Assert.Equal("—", viewModel.CoverageSummary);
        Assert.Empty(viewModel.Songs);
        Assert.Empty(viewModel.ReviewReferences);
        Assert.Equal(["all"], viewModel.ReasonOptions);
        Assert.Equal("all", viewModel.SelectedCoverageStatus);
        Assert.Equal("all", viewModel.SelectedReason);
    }

    private static ReviewProjection Projection() => new()
    {
        ProjectionSchemaVersion = 1,
        Master = new ProjectionMaster
        {
            Path = "master.sqlite",
            MasterVersion = "master-v1",
            SourceHash = "hash",
            SongCount = 2,
            ChartCount = 2,
            GrandPrixSongCount = 2,
        },
        Catalog = new ProjectionCatalog
        {
            Path = "catalog.sqlite",
            CatalogIdentity = "ddrgp-local-jacket-reference-catalog",
            SchemaVersion = 1,
            CreatedAt = "now",
            CurrentFeatureExtractorVersion = "m5-jacket-v1",
        },
        Coverage = new ProjectionCoverage
        {
            GrandPrixSongCount = 2,
            StatusCounts = new Dictionary<string, int> { ["needs_review"] = 1, ["uncollected"] = 1 },
            OrphanedReferenceCount = 0,
            OrphanReasonCounts = [],
            UnassignedUnresolvedObservationCount = 0,
        },
        Songs =
        [
            new ProjectionSong
            {
                SongId = "song-1", Title = "Alpha", Artist = "A", MasterVersion = "master-v1",
                CoverageStatus = "needs_review", ReferenceCount = "1", Reason = "opaque reason",
            },
            new ProjectionSong
            {
                SongId = "song-2", Title = "Beta", Artist = "B", MasterVersion = "master-v1",
                CoverageStatus = "uncollected", ReferenceCount = "0", Reason = "",
            },
        ],
        ReviewReferences =
        [
            new ReviewReference
            {
                ReferenceId = "ref-1", ReviewStatus = "needs_review", Reason = "opaque reason",
                ObservedTitle = "Alpha", ObservedArtist = "?", ObservationStatus = "ok",
                MasterDrift = false, FeatureExtractorVersion = "m5-jacket-v1", Candidates = [],
            },
        ],
    };

    private sealed class StubProjectionService(ReviewProjection projection) : IProjectionService
    {
        public Task<ReviewProjection> LoadAsync(
            string masterPath,
            string catalogPath,
            CancellationToken cancellationToken) => Task.FromResult(projection);
    }

    private sealed class SequenceProjectionService(
        ReviewProjection first,
        Exception second) : IProjectionService
    {
        private int callCount;

        public Task<ReviewProjection> LoadAsync(
            string masterPath,
            string catalogPath,
            CancellationToken cancellationToken)
        {
            callCount++;
            return callCount == 1
                ? Task.FromResult(first)
                : Task.FromException<ReviewProjection>(second);
        }
    }

    private sealed class StubMasterUpdateService(Exception? exception = null) : IMasterUpdateService
    {
        public Task<MasterUpdateResult> UpdateAsync(string targetPath, CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                return Task.FromException<MasterUpdateResult>(exception);
            }
            return Task.FromResult(new MasterUpdateResult(
                new MasterSummary("master-v1", "old", 1, 1, 1),
                new MasterSummary("master-v2", "new", 2, 2, 2)));
        }
    }
}
