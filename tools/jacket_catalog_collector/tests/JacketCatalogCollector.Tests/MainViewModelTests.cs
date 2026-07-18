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
        Assert.Contains("ambiguous", viewModel.CandidateClassificationOptions);

        viewModel.SelectedCoverageStatus = "needs_review";
        Assert.Single(viewModel.Songs);
        Assert.Single(viewModel.ReviewReferences);

        viewModel.SelectedReason = "opaque reason";
        Assert.Single(viewModel.Songs);
        Assert.Single(viewModel.ReviewReferences);

        viewModel.SelectedCandidateClassification = "exact_unique";
        Assert.Empty(viewModel.ReviewReferences);
    }

    [Fact]
    public async Task RemembersPathsOnlyAfterSuccessfulManualProjectionLoad()
    {
        var store = new StubDatabasePathStore();
        var projectionService = new SequenceProjectionService(
            Projection(),
            new InvalidOperationException("invalid projection"));
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projectionService,
            databasePathStore: store);

        await viewModel.LoadProjectionAsync("master-ok.sqlite", "catalog-ok.sqlite");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.LoadProjectionAsync("master-bad.sqlite", "catalog-bad.sqlite"));

        var saved = Assert.Single(store.Saved);
        Assert.Equal(Path.GetFullPath("master-ok.sqlite"), saved.MasterPath);
        Assert.Equal(Path.GetFullPath("catalog-ok.sqlite"), saved.CatalogPath);
    }

    [Fact]
    public async Task AutomaticallyReloadsRememberedPathsWithoutRewritingSetting()
    {
        var remembered = new CollectorDatabasePaths(
            Path.GetFullPath("remembered-master.sqlite"),
            Path.GetFullPath("remembered-catalog.sqlite"));
        var store = new StubDatabasePathStore(remembered);
        var projectionService = new RecordingProjectionService(Projection());
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projectionService,
            databasePathStore: store);

        await viewModel.InitializeRememberedProjectionAsync();

        Assert.Equal((remembered.MasterPath, remembered.CatalogPath), projectionService.Loads[0]);
        Assert.Empty(store.Saved);
        Assert.Equal("前回DBを自動読込", viewModel.StatusTitle);
        Assert.Equal("master-v1", viewModel.MasterVersion);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task MissingSettingKeepsFirstLaunchManualAndCreatesNothing()
    {
        var store = new StubDatabasePathStore();
        var projectionService = new RecordingProjectionService(Projection());
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projectionService,
            databasePathStore: store);

        await viewModel.InitializeRememberedProjectionAsync();

        Assert.Empty(projectionService.Loads);
        Assert.Empty(store.Saved);
        Assert.Equal("準備完了", viewModel.StatusTitle);
        Assert.Equal("未選択", viewModel.MasterVersion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedAutomaticReloadKeepsSettingAndReturnsToManualSelection(
        bool settingFailure)
    {
        var remembered = new CollectorDatabasePaths(
            Path.GetFullPath("remembered-master.sqlite"),
            Path.GetFullPath("remembered-catalog.sqlite"));
        var store = new StubDatabasePathStore(
            remembered,
            loadException: settingFailure ? new UnauthorizedAccessException("denied") : null);
        IProjectionService projectionService = settingFailure
            ? new RecordingProjectionService(Projection())
            : new FailingProjectionService(new InvalidOperationException("incompatible"));
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projectionService,
            databasePathStore: store);

        await viewModel.InitializeRememberedProjectionAsync();

        Assert.Empty(store.Saved);
        Assert.Equal("前回DBの自動読込失敗", viewModel.StatusTitle);
        Assert.Contains("手動選択", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("未選択", viewModel.MasterVersion);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SettingSaveFailureKeepsValidatedProjectionAvailable()
    {
        var store = new StubDatabasePathStore(
            saveException: new UnauthorizedAccessException("denied"));
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(Projection()),
            databasePathStore: store);

        await viewModel.LoadProjectionAsync("master.sqlite", "catalog.sqlite");

        Assert.Equal("読込完了（path記憶失敗）", viewModel.StatusTitle);
        Assert.Equal("master-v1", viewModel.MasterVersion);
        Assert.NotEmpty(viewModel.Songs);
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
            () => viewModel.LoadProjectionAsync("other-master.sqlite", "other-catalog.sqlite"));

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

    [Fact]
    public async Task AppliesExplicitSongMutationAndReloadsProjection()
    {
        var projection = Projection();
        var workflow = new StubReviewWorkflowService();
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(projection),
            workflow);
        await viewModel.LoadProjectionAsync("master.sqlite", "catalog.sqlite");
        viewModel.SelectedReference = viewModel.ReviewReferences[0];
        viewModel.SelectedSong = viewModel.SongChoices[1];
        viewModel.ReviewReason = "explicit";
        viewModel.ReviewNote = "opaque";

        await viewModel.ApplyReviewAsync("manual_confirm");

        var mutation = Assert.Single(workflow.Mutations);
        Assert.Equal(("ref-1", "song-2", 0, "needs_review"),
            (mutation.ReferenceId, mutation.SongId, mutation.ExpectedRevision, mutation.ExpectedStatus));
        Assert.Equal("review更新完了", viewModel.StatusTitle);
    }

    private static ReviewProjection Projection() => new()
    {
        ProjectionSchemaVersion = 4,
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
            CurrentFeatureExtractorVersion = "m5-jacket-v2",
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
                MasterDrift = false, FeatureExtractorVersion = "m5-jacket-v2", Candidates = [],
                StoredStatus = "needs_review",
                Revision = 0,
                ManualActionId = null,
                ManualNote = "",
                History = [],
                CandidateEvaluation = new CandidateEvaluation
                {
                    EvaluationSchemaVersion = "m5c-unresolved-candidate-evaluation-v1",
                    MethodVersion = "tesseract-autocontrast-v1",
                    ObservationId = "observation-1",
                    Classification = "ambiguous",
                    Reason = "title_match_artist_mismatch",
                    JacketPreviewPath = null,
                    Title = new CandidateEvaluationField
                    {
                        Raw = "Alpha", Normalized = "alpha", Confidence = 0.95,
                        Status = "ok", FailureReason = "",
                    },
                    Artist = new CandidateEvaluationField
                    {
                        Raw = "?", Normalized = "?", Confidence = 0.95,
                        Status = "ok", FailureReason = "",
                    },
                    Candidates =
                    [
                        new CandidateEvaluationSong
                        {
                            SongId = "song-1", Title = "Alpha", Artist = "A",
                        },
                    ],
                },
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

    private sealed class RecordingProjectionService(ReviewProjection projection) : IProjectionService
    {
        public List<(string Master, string Catalog)> Loads { get; } = [];

        public Task<ReviewProjection> LoadAsync(
            string masterPath,
            string catalogPath,
            CancellationToken cancellationToken)
        {
            Loads.Add((masterPath, catalogPath));
            return Task.FromResult(projection);
        }
    }

    private sealed class FailingProjectionService(Exception exception) : IProjectionService
    {
        public Task<ReviewProjection> LoadAsync(
            string masterPath,
            string catalogPath,
            CancellationToken cancellationToken) =>
            Task.FromException<ReviewProjection>(exception);
    }

    private sealed class StubDatabasePathStore(
        CollectorDatabasePaths? remembered = null,
        Exception? loadException = null,
        Exception? saveException = null) : ICollectorDatabasePathStore
    {
        public List<CollectorDatabasePaths> Saved { get; } = [];

        public Task<CollectorDatabasePaths?> LoadAsync(CancellationToken cancellationToken) =>
            loadException is null
                ? Task.FromResult(remembered)
                : Task.FromException<CollectorDatabasePaths?>(loadException);

        public Task SaveAsync(
            CollectorDatabasePaths paths,
            CancellationToken cancellationToken)
        {
            if (saveException is not null)
            {
                return Task.FromException(saveException);
            }
            Saved.Add(paths);
            return Task.CompletedTask;
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

    private sealed class StubReviewWorkflowService : IReviewWorkflowService
    {
        public List<ReviewMutation> Mutations { get; } = [];

        public Task<ReviewMutationReceipt> ApplyAsync(
            string masterPath,
            string catalogPath,
            ReviewMutation mutation,
            CancellationToken cancellationToken)
        {
            Mutations.Add(mutation);
            return Task.FromResult(new ReviewMutationReceipt(
                mutation.ActionId,
                mutation.ReferenceId,
                mutation.Action,
                "manual_confirmed",
                mutation.SongId,
                mutation.ExpectedRevision + 1,
                false));
        }
    }
}
