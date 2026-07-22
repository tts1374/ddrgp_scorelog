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

    [Fact]
    public async Task ManualReviewRowsKeepUnreflectedDraftsAndHideAppliedStatuses()
    {
        var projection = ProjectionWithVisibilityStates();
        var store = new StubManualReviewDraftStore(
        [
            new ManualReviewDraft
            {
                ObservationId = "observation-hold",
                Status = "hold",
                TruthSongId = null,
                Notes = "hold note",
            },
            new ManualReviewDraft
            {
                ObservationId = "observation-confirmed",
                Status = "confirmed",
                TruthSongId = "song-1",
                Notes = "confirmed draft",
            },
            new ManualReviewDraft
            {
                ObservationId = "observation-rejected",
                Status = "rejected",
                TruthSongId = null,
                Notes = "rejected draft",
            },
        ]);
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(projection),
            manualReviewDraftStore: store);

        await viewModel.LoadProjectionAsync("master.sqlite", "catalog.sqlite");

        Assert.Equal(4, viewModel.ManualReviewRows.Count);
        Assert.Contains(viewModel.ManualReviewRows, row => row.Status == "unreviewed");
        Assert.Contains(viewModel.ManualReviewRows, row => row.Status == "hold");
        Assert.Contains(viewModel.ManualReviewRows, row => row.Status == "confirmed");
        Assert.Contains(viewModel.ManualReviewRows, row => row.Status == "rejected");
        Assert.DoesNotContain(viewModel.ManualReviewRows, row => row.ReferenceId == "hidden-auto");
        Assert.DoesNotContain(viewModel.ManualReviewRows, row => row.ReferenceId == "hidden-manual");
        Assert.DoesNotContain(viewModel.ManualReviewRows, row => row.ReferenceId == "hidden-rejected");

        var holdRow = Assert.Single(
            viewModel.ManualReviewRows,
            row => row.ObservationId == "observation-hold");
        holdRow.Status = "unreviewed";
        Assert.Equal(4, viewModel.ManualReviewRows.Count);
    }

    [Fact]
    public async Task TruthSongSelectionSetsConfirmedAndRejectedClearsSong()
    {
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(Projection()),
            manualReviewDraftStore: new StubManualReviewDraftStore());

        await viewModel.LoadProjectionAsync("master.sqlite", "catalog.sqlite");
        var row = Assert.Single(viewModel.ManualReviewRows);

        row.TruthSongId = "song-2";
        Assert.Equal("confirmed", row.Status);
        Assert.Contains("Beta", row.TruthSongDisplay, StringComparison.Ordinal);

        row.Status = "rejected";
        Assert.Null(row.TruthSongId);
    }

    [Fact]
    public async Task ConfirmedDraftWithoutTruthSongIsRejectedWithoutWriting()
    {
        var store = new StubManualReviewDraftStore();
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(Projection()),
            manualReviewDraftStore: store);

        await viewModel.LoadProjectionAsync("master.sqlite", "catalog.sqlite");
        viewModel.SelectedManualReviewRow = Assert.Single(viewModel.ManualReviewRows);
        viewModel.SelectedManualReviewRow.Status = "confirmed";

        Assert.False(await viewModel.SaveSelectedDraftAsync());
        Assert.Equal(0, store.SaveCalls);
        Assert.Contains("truth song", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("入力エラー", viewModel.SelectedManualReviewRow.DraftStateDisplay,
            StringComparison.Ordinal);

        viewModel.SelectedManualReviewRow.Status = "invalid";
        Assert.False(await viewModel.SaveSelectedDraftAsync());
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task SavesAndReloadsDraftWithoutChangingCatalogReviewState()
    {
        var projection = Projection();
        var store = new StubManualReviewDraftStore();
        var workflow = new StubReviewWorkflowService();
        var catalogPath = Path.Combine(
            Path.GetTempPath(), $"ddrgp-scorelog-catalog-{Guid.NewGuid():N}.sqlite");
        File.WriteAllText(catalogPath, "catalog sentinel");
        try
        {
            var viewModel = new MainViewModel(
                new StubMasterUpdateService(),
                new StubProjectionService(projection),
                workflow,
                manualReviewDraftStore: store);
            await viewModel.LoadProjectionAsync("master.sqlite", catalogPath);
            viewModel.SelectedManualReviewRow = Assert.Single(viewModel.ManualReviewRows);
            viewModel.SelectedManualReviewRow.Status = "hold";
            viewModel.SelectedManualReviewRow.Notes = "keep this draft";

            Assert.True(await viewModel.SaveSelectedDraftAsync());
            Assert.Equal("catalog sentinel", File.ReadAllText(catalogPath));
            Assert.Empty(workflow.Mutations);
            Assert.Equal("needs_review", projection.ReviewReferences[0].StoredStatus);
            Assert.Equal(0, projection.ReviewReferences[0].Revision);
            Assert.Empty(projection.ReviewReferences[0].History);
            Assert.Null(projection.ReviewReferences[0].ManualActionId);

            var reloaded = new MainViewModel(
                new StubMasterUpdateService(),
                new StubProjectionService(projection),
                manualReviewDraftStore: store);
            await reloaded.LoadProjectionAsync("master.sqlite", catalogPath);
            var restored = Assert.Single(reloaded.ManualReviewRows);
            Assert.Equal("hold", restored.Status);
            Assert.Equal("keep this draft", restored.Notes);
            Assert.Equal("保存済み", restored.DraftStateDisplay);
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public async Task MasterSearchUsesCanonicalAliasPrefixPartialArtistAndIdOrder()
    {
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(Projection()),
            manualReviewDraftStore: new StubManualReviewDraftStore());
        await viewModel.LoadProjectionAsync("master.sqlite", "catalog.sqlite");

        viewModel.SongSearch = "Alpha";
        Assert.Equal("song-1", viewModel.SongChoices.First().SongId);
        viewModel.SongSearch = "Alpha Alias";
        Assert.Equal("song-1", viewModel.SongChoices.First().SongId);
        viewModel.SongSearch = "Al";
        Assert.Equal("song-1", viewModel.SongChoices.First().SongId);
        viewModel.SongSearch = "pha";
        Assert.Equal("song-1", viewModel.SongChoices.First().SongId);
        viewModel.SongSearch = "Artist B";
        Assert.Equal("song-2", viewModel.SongChoices.First().SongId);
        viewModel.SongSearch = "song-2";
        Assert.Equal("song-2", viewModel.SongChoices.First().SongId);
    }

    private static ReviewProjection ProjectionWithVisibilityStates()
    {
        var projection = Projection();
        projection.ReviewReferences.Clear();
        projection.ReviewReferences.Add(ReferenceWithStatus(
            "visible-unreviewed", "needs_review", "observation-unreviewed"));
        projection.ReviewReferences.Add(ReferenceWithStatus(
            "visible-hold", "unresolved", "observation-hold"));
        projection.ReviewReferences.Add(ReferenceWithStatus(
            "visible-confirmed", "needs_review", "observation-confirmed"));
        projection.ReviewReferences.Add(ReferenceWithStatus(
            "visible-rejected", "unresolved", "observation-rejected"));
        projection.ReviewReferences.Add(ReferenceWithStatus(
            "hidden-auto", "auto_confirmed", "observation-auto"));
        projection.ReviewReferences.Add(ReferenceWithStatus(
            "hidden-manual", "manual_confirmed", "observation-manual"));
        projection.ReviewReferences.Add(ReferenceWithStatus(
            "hidden-rejected", "rejected", "observation-applied-rejected"));
        projection.ReviewReferences.Add(ReferenceWithStatuses(
            "hidden-auto-drift", "needs_review", "auto_confirmed", "observation-auto-drift"));
        projection.ReviewReferences.Add(ReferenceWithStatuses(
            "hidden-manual-drift", "needs_review", "manual_confirmed", "observation-manual-drift"));
        projection.ReviewReferences.Add(ReferenceWithStatuses(
            "hidden-rejected-drift", "orphaned", "rejected", "observation-rejected-drift"));
        return projection;
    }

    private static ReviewReference ReferenceWithStatus(
        string referenceId,
        string status,
        string observationId) => ReferenceWithStatuses(referenceId, status, status, observationId);

    private static ReviewReference ReferenceWithStatuses(
        string referenceId,
        string reviewStatus,
        string storedStatus,
        string observationId) => new()
        {
            ReferenceId = referenceId,
            ReviewStatus = reviewStatus,
            Reason = "needs review",
            ObservedTitle = "Observed title",
            ObservedArtist = "Observed artist",
            ObservationStatus = "ok",
            MasterDrift = false,
            FeatureExtractorVersion = "m5-jacket-v2",
            AssignedSong = null,
            Candidates = [],
            StoredStatus = storedStatus,
            Revision = 0,
            ManualActionId = null,
            ManualNote = "",
            History = [],
            CandidateEvaluation = new CandidateEvaluation
            {
                EvaluationSchemaVersion = "m5c-unresolved-candidate-evaluation-v1",
                MethodVersion = "tesseract-autocontrast-v1",
                ObservationId = observationId,
                Classification = storedStatus is "auto_confirmed" or "manual_confirmed" or "rejected"
                ? "not_eligible"
                : "ambiguous",
                Reason = "needs_review",
                JacketPreviewPath = null,
                Title = new CandidateEvaluationField
                {
                    Raw = "Observed title",
                    Normalized = "observed title",
                    Confidence = 0.9,
                    Status = "ok",
                    FailureReason = "",
                },
                Artist = new CandidateEvaluationField
                {
                    Raw = "Observed artist",
                    Normalized = "observed artist",
                    Confidence = 0.9,
                    Status = "ok",
                    FailureReason = "",
                },
                Candidates = [],
            },
        };

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
                Aliases = ["Alpha Alias"],
            },
            new ProjectionSong
            {
                SongId = "song-2", Title = "Beta", Artist = "Artist B", MasterVersion = "master-v1",
                CoverageStatus = "uncollected", ReferenceCount = "0", Reason = "",
                Aliases = [],
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

    private sealed class StubManualReviewDraftStore(
        IEnumerable<ManualReviewDraft>? initialDrafts = null) : IManualReviewDraftStore
    {
        public Dictionary<string, ManualReviewDraft> Drafts { get; } = (initialDrafts ?? [])
            .ToDictionary(draft => draft.ObservationId, StringComparer.Ordinal);

        public int SaveCalls { get; private set; }

        public Task<IReadOnlyDictionary<string, ManualReviewDraft>> LoadAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, ManualReviewDraft> snapshot =
                Drafts.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            return Task.FromResult(snapshot);
        }

        public Task SaveAsync(
            IReadOnlyCollection<ManualReviewDraft> drafts,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            Drafts.Clear();
            foreach (var draft in drafts)
            {
                Drafts[draft.ObservationId] = draft;
            }
            return Task.CompletedTask;
        }
    }

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
