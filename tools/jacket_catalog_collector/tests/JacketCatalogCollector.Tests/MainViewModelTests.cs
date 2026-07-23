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

        await viewModel.LoadProjectionAsync();

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
    public async Task MissingMasterKeepsStartupReadOnlyAndDoesNotCreateCatalog()
    {
        using var database = new TestDatabase();
        var catalog = new StubCatalogInitializationService(database.Paths.CatalogPath);
        var projection = new RecordingProjectionService(Projection());
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projection,
            databasePaths: database.Paths,
            catalogInitializationService: catalog);

        await viewModel.InitializeDatabasesAsync();

        Assert.Equal("曲情報がありません", viewModel.StatusTitle);
        Assert.Equal("未選択", viewModel.MasterVersion);
        Assert.Empty(projection.Loads);
        Assert.Equal(0, catalog.Calls);
    }

    [Fact]
    public async Task ValidMasterCreatesMissingCatalogAndLoadsFixedProjection()
    {
        using var database = new TestDatabase(master: true);
        var catalog = new StubCatalogInitializationService(database.Paths.CatalogPath);
        var master = new StubMasterUpdateService();
        var projection = new RecordingProjectionService(Projection());
        var viewModel = new MainViewModel(
            master,
            projection,
            databasePaths: database.Paths,
            catalogInitializationService: catalog);

        await viewModel.InitializeDatabasesAsync();

        Assert.Equal("読込完了", viewModel.StatusTitle);
        Assert.Equal((database.Paths.MasterPath, database.Paths.CatalogPath), projection.Loads[0]);
        Assert.Equal(1, catalog.Calls);
        Assert.Equal(database.Paths.MasterPath, master.InspectedPaths[0]);
        Assert.True(File.Exists(database.Paths.CatalogPath));
    }

    [Fact]
    public async Task ExistingCatalogIsValidatedAndNeverReplaced()
    {
        using var database = new TestDatabase(master: true, catalog: true);
        var before = File.ReadAllBytes(database.Paths.CatalogPath);
        var catalog = new StubCatalogInitializationService(database.Paths.CatalogPath);
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new StubProjectionService(Projection()),
            databasePaths: database.Paths,
            catalogInitializationService: catalog);

        await viewModel.InitializeDatabasesAsync();

        Assert.Equal(0, catalog.Calls);
        Assert.Equal(before, File.ReadAllBytes(database.Paths.CatalogPath));
    }

    [Fact]
    public async Task InvalidMasterStopsBeforeCatalogCreationOrProjection()
    {
        using var database = new TestDatabase(master: true);
        var catalog = new StubCatalogInitializationService(database.Paths.CatalogPath);
        var projection = new RecordingProjectionService(Projection());
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(
                inspectException: new InvalidOperationException("master is corrupt")),
            projection,
            databasePaths: database.Paths,
            catalogInitializationService: catalog);

        await viewModel.InitializeDatabasesAsync();

        Assert.Equal("DB初期化/検証失敗", viewModel.StatusTitle);
        Assert.Contains("master is corrupt", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(projection.Loads);
        Assert.Equal(0, catalog.Calls);
    }

    [Fact]
    public async Task InvalidCatalogIsReportedWithoutReplacingExistingBytes()
    {
        using var database = new TestDatabase(master: true, catalog: true);
        var before = File.ReadAllBytes(database.Paths.CatalogPath);
        var projection = new FailingProjectionService(new InvalidOperationException("catalog is corrupt"));
        var catalog = new StubCatalogInitializationService(database.Paths.CatalogPath);
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projection,
            databasePaths: database.Paths,
            catalogInitializationService: catalog);

        await viewModel.InitializeDatabasesAsync();

        Assert.Equal("DB初期化/検証失敗", viewModel.StatusTitle);
        Assert.Contains("catalog is corrupt", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(database.Paths.CatalogPath));
        Assert.Equal(0, catalog.Calls);
    }

    [Fact]
    public async Task RemovesNewCatalogWhenStrictValidationFails()
    {
        using var database = new TestDatabase(master: true);
        var catalog = new StubCatalogInitializationService(database.Paths.CatalogPath);
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            new FailingProjectionService(new InvalidOperationException("catalog is invalid")),
            databasePaths: database.Paths,
            catalogInitializationService: catalog);

        await viewModel.InitializeDatabasesAsync();

        Assert.Equal("DB初期化/検証失敗", viewModel.StatusTitle);
        Assert.Contains("catalog is invalid", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(1, catalog.Calls);
        Assert.False(File.Exists(database.Paths.CatalogPath));
    }

    [Fact]
    public async Task ReportsMasterSuccessFailureAndCancellationStates()
    {
        using var successDatabase = new TestDatabase(master: true, catalog: true);
        var successMaster = new StubMasterUpdateService();
        var successProjection = new RecordingProjectionService(Projection());
        var success = new MainViewModel(
            successMaster,
            successProjection,
            databasePaths: successDatabase.Paths,
            catalogInitializationService: new StubCatalogInitializationService(
                successDatabase.Paths.CatalogPath));
        await success.UpdateMasterAsync();
        Assert.Equal("master更新完了", success.StatusTitle);
        Assert.Equal(successDatabase.Paths.MasterPath, Assert.Single(successMaster.UpdateTargets));
        Assert.Equal(
            (successDatabase.Paths.MasterPath, successDatabase.Paths.CatalogPath),
            Assert.Single(successProjection.Loads));

        var failure = new MainViewModel(
            new StubMasterUpdateService(new IOException("build failed")),
            new StubProjectionService(Projection()));
        await Assert.ThrowsAsync<IOException>(() => failure.UpdateMasterAsync());
        Assert.Equal("master更新失敗", failure.StatusTitle);

        var canceled = new MainViewModel(
            new StubMasterUpdateService(new OperationCanceledException()),
            new StubProjectionService(Projection()));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => canceled.UpdateMasterAsync());
        Assert.Equal("master更新取消", canceled.StatusTitle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearsProjectionWhenMasterUpdateReloadFailsOrIsCanceled(bool cancel)
    {
        using var database = new TestDatabase(master: true, catalog: true);
        var projectionService = new SequenceProjectionService(
            Projection(),
            cancel ? new OperationCanceledException() : new InvalidOperationException("reload failed"));
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projectionService,
            databasePaths: database.Paths);

        await viewModel.LoadProjectionAsync();
        viewModel.SelectedCoverageStatus = "needs_review";
        viewModel.SelectedReason = "opaque reason";
        Assert.Equal(database.Paths.MasterPath, viewModel.CurrentMasterPath);
        Assert.Equal(database.Paths.CatalogPath, viewModel.CurrentCatalogPath);

        await Assert.ThrowsAnyAsync<Exception>(() => viewModel.UpdateMasterAsync());

        Assert.Equal(
            cancel ? "master更新後の再読込取消" : "master更新後の再読込失敗",
            viewModel.StatusTitle);
        Assert.Equal("未選択", viewModel.MasterVersion);
        Assert.Null(viewModel.CurrentMasterPath);
        Assert.Null(viewModel.CurrentCatalogPath);
        Assert.Empty(viewModel.Songs);
        Assert.Empty(viewModel.ReviewReferences);
        Assert.Equal(["all"], viewModel.ReasonOptions);
        Assert.Equal("all", viewModel.SelectedCoverageStatus);
        Assert.Equal("all", viewModel.SelectedReason);
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
        await viewModel.LoadProjectionAsync();
        viewModel.SelectedCoverageStatus = "needs_review";
        viewModel.SelectedReason = "opaque reason";

        await Assert.ThrowsAnyAsync<Exception>(
            () => viewModel.LoadProjectionAsync());

        Assert.Equal(cancel ? "読込取消" : "読込失敗", viewModel.StatusTitle);
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
    public async Task Final_collection_separates_retry_result_and_clears_projection_on_reload_failure()
    {
        var observationSession = new JacketObservationSession(
            new JacketObservationDetector(),
            new NoopCheckpointStore(),
            new NoopArtifactPublisher(),
            new NoopCatalogAdapter());
        var capture = new WindowCaptureViewModel(
            new EmptyWindowEnumerator(),
            new WindowCaptureCoordinator(
                new EmptyWindowEnumerator(),
                new UnsupportedCaptureFactory(),
                new ImmediateCaptureDispatcher()));
        await using var observation = new JacketObservationViewModel(
            capture,
            observationSession,
            new ImmediateCaptureDispatcher());
        var projection = new SequenceProjectionService(
            Projection(),
            new InvalidOperationException("projection reload failed"));
        var viewModel = new MainViewModel(
            new StubMasterUpdateService(),
            projection,
            observation: observation);

        await viewModel.LoadProjectionAsync();
        await viewModel.FinalizeObservationSessionAsync();

        Assert.Equal("収集終了・projection再読込失敗", viewModel.StatusTitle);
        Assert.Contains("catalog retry:", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("projection再読込: 失敗", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Null(viewModel.CurrentMasterPath);
        Assert.Null(viewModel.CurrentCatalogPath);
        Assert.Empty(viewModel.Songs);
        Assert.Empty(viewModel.ReviewReferences);
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
        await viewModel.LoadProjectionAsync();
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

    private sealed class EmptyWindowEnumerator : IWindowEnumerator
    {
        public Task<IReadOnlyList<WindowCandidate>> EnumerateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WindowCandidate>>([]);

        public WindowIdentitySnapshot? TryGetSnapshot(nint handle) => null;
    }

    private sealed class UnsupportedCaptureFactory : IWindowCaptureSessionFactory
    {
        public bool IsSupported => false;

        public Task<IWindowCaptureFrameSource> StartAsync(
            WindowIdentitySnapshot target,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IWindowCaptureFrameSource>(
                new InvalidOperationException("capture is unsupported in test"));
    }

    private sealed class NoopCheckpointStore : IObservationCheckpointStore
    {
        public Task SaveAsync(
            ObservationCheckpoint checkpoint,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ObservationCheckpoint> LoadAsync(
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ObservationCheckpoint>(
                new InvalidOperationException("checkpoint is not expected in test"));
    }

    private sealed class NoopArtifactPublisher : IObservationArtifactPublisher
    {
        public Task<ArtifactPublishReceipt> PublishAsync(
            ObservationArtifact artifact,
            ObservationCheckpoint checkpoint,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ArtifactPublishReceipt>(
                new InvalidOperationException("artifact publish is not expected in test"));

        public Task RollbackAsync(
            ArtifactPublishReceipt receipt,
            ObservationCheckpoint? previousCheckpoint,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopCatalogAdapter : IObservationCatalogAdapter
    {
        public Task<IReadOnlySet<CompositeObservationIdentity>> LoadCompositeIdentitySetAsync(
            ObservationSessionIdentity session,
            string catalogPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<CompositeObservationIdentity>>(
                new HashSet<CompositeObservationIdentity>());

        public Task ValidateSessionAsync(
            ObservationSessionIdentity session,
            string catalogPath,
            string masterPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ValidateReceiptAsync(
            ObservationCheckpointObservation observation,
            ObservationArtifact artifact,
            string catalogPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<CatalogIngestReceipt> IngestAsync(
            ObservationArtifact artifact,
            string sourceImagePath,
            string catalogPath,
            string masterPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogIngestReceipt(
                CatalogIngestDisposition.Existing,
                "reference-test",
                "existing"));
    }

    private sealed class TestDatabase : IDisposable
    {
        public TestDatabase(bool master = false, bool catalog = false)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"ddrgp-main-view-model-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Paths = CollectorDatabasePaths.FromRepositoryRoot(Root);
            if (master || catalog)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Paths.MasterPath)!);
            }
            if (master)
            {
                File.WriteAllText(Paths.MasterPath, "master");
            }
            if (catalog)
            {
                File.WriteAllText(Paths.CatalogPath, "catalog");
            }
        }

        public string Root { get; }
        public CollectorDatabasePaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class StubCatalogInitializationService(string catalogPath)
        : ICatalogInitializationService
    {
        public int Calls { get; private set; }

        public Task EnsureCreatedAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath, "created-catalog");
            return Task.CompletedTask;
        }
    }

    private sealed class StubMasterUpdateService(
        Exception? updateException = null,
        Exception? inspectException = null) : IMasterUpdateService
    {
        public List<string> InspectedPaths { get; } = [];
        public List<string> UpdateTargets { get; } = [];

        public Task<MasterSummary> InspectAsync(
            string path,
            CancellationToken cancellationToken)
        {
            InspectedPaths.Add(path);
            return inspectException is null
                ? Task.FromResult(new MasterSummary("master-v1", "hash", 1, 1, 1))
                : Task.FromException<MasterSummary>(inspectException);
        }

        public Task<MasterUpdateResult> UpdateAsync(
            string targetPath,
            CancellationToken cancellationToken)
        {
            UpdateTargets.Add(targetPath);
            if (updateException is not null)
            {
                return Task.FromException<MasterUpdateResult>(updateException);
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
