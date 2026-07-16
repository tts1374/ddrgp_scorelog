using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacketCatalogCollector.Tests;

public sealed class JacketObservationTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "jacket-observation-tests-" + Guid.NewGuid().ToString("N"));

    public JacketObservationTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detector_distinguishes_change_stable_and_duplicate_preview()
    {
        var detector = new JacketObservationDetector(
            new JacketDetectorOptions(0.01, 3, TimeSpan.FromMilliseconds(100)));

        var first = detector.Observe(Frame(20, 1, 0));
        var settling = detector.Observe(Frame(20, 2, 50));
        var stable = detector.Observe(Frame(20, 3, 150));
        var duplicate = detector.Observe(Frame(20, 4, 250));
        var changed = detector.Observe(Frame(220, 5, 300));

        Assert.Equal(JacketDetectionState.ChangeCandidate, first.State);
        Assert.Equal(JacketDetectionState.ChangeCandidate, settling.State);
        Assert.Equal(JacketDetectionState.StableCandidate, stable.State);
        Assert.Equal(JacketDetectionState.DuplicatePreview, duplicate.State);
        Assert.True(duplicate.HasStableCandidate);
        Assert.Equal(JacketDetectionState.ChangeCandidate, changed.State);
        Assert.Equal(1, duplicate.DuplicatePreviewCount);
        Assert.NotEqual(stable.Candidate!.FeatureHash, changed.Candidate!.FeatureHash);
    }

    [Fact]
    public async Task Observation_queue_drops_are_persisted_with_capture_drops_after_stop()
    {
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var source = new TestFrameSource();
        var candidate = Candidate();
        var windows = new TestWindowEnumerator(candidate);
        var coordinator = new WindowCaptureCoordinator(
            windows,
            new TestSessionFactory(source),
            new ImmediateCaptureDispatcher(),
            ringCapacity: 2);
        var capture = new WindowCaptureViewModel(windows, coordinator);
        var dispatcher = new BlockingObservationDispatcher();
        var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            new AtomicObservationArtifactPublisher(root),
            new FakeCatalogAdapter());
        await using var viewModel = new JacketObservationViewModel(
            capture,
            session,
            dispatcher);

        await viewModel.StartSessionAsync(
            Master(), Catalog(), candidate, "master.sqlite", "catalog.sqlite");
        Assert.True(await coordinator.StartAsync(candidate));

        var initialChange = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var initialStable = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(viewModel.StableCandidate))
            {
                return;
            }
            if (viewModel.Detection.State == JacketDetectionState.ChangeCandidate)
            {
                initialChange.TrySetResult();
            }
            if (viewModel.Detection.State == JacketDetectionState.StableCandidate)
            {
                initialStable.TrySetResult();
            }
        };

        var delivered = 0;
        var allFramesDelivered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        capture.FrameReceived += (_, _) =>
        {
            if (Interlocked.Increment(ref delivered) == 6)
            {
                allFramesDelivered.TrySetResult();
            }
        };

        source.Write(Frame(20, 1, 0));
        await initialChange.Task;
        source.Write(Frame(20, 2, 100));
        await initialStable.Task;
        await viewModel.AdoptAsync();

        dispatcher.BlockNext();
        source.Write(Frame(20, 3, 200));
        await dispatcher.Entered;
        source.Write(Frame(20, 4, 300));
        source.Write(Frame(20, 5, 400));
        source.Write(Frame(20, 6, 500));
        await allFramesDelivered.Task;

        Assert.Contains("observation_drop=2", viewModel.DetectorProgress);

        var stopping = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inactive = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.IsActive) && !viewModel.IsActive)
            {
                inactive.TrySetResult();
            }
        };
        capture.LifecycleChanged += (_, value) =>
        {
            if (value.State == CaptureLifecycleState.Stopping)
            {
                stopping.TrySetResult();
            }
        };

        var stop = coordinator.StopAsync();
        await stopping.Task;
        dispatcher.Release();
        await stop;
        await inactive.Task;

        var persisted = await checkpointStore.LoadAsync(viewModel.SessionId);
        Assert.Equal(4, persisted.ProcessedFrameCount);
        Assert.Equal(6, persisted.DroppedFrameCount);

        await using var resumed = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            new AtomicObservationArtifactPublisher(root),
            new FakeCatalogAdapter());
        var resume = await resumed.ResumeAsync(
            ResumeRequest(persisted.Session), "master.sqlite", "catalog.sqlite");
        Assert.True(resume.Compatible);
        Assert.Equal(6, resume.Checkpoint!.DroppedFrameCount);
        await resumed.StopAsync();
    }

    [Fact]
    public async Task Stopping_waits_for_final_source_drop_before_checkpoint_save()
    {
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var source = new TestFrameSource(droppedOnStop: 3);
        var candidate = Candidate();
        var windows = new TestWindowEnumerator(candidate);
        var coordinator = new WindowCaptureCoordinator(
            windows,
            new TestSessionFactory(source),
            new ImmediateCaptureDispatcher(),
            ringCapacity: 32);
        var capture = new WindowCaptureViewModel(windows, coordinator);
        var dispatcher = new BlockingObservationDispatcher();
        var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            new AtomicObservationArtifactPublisher(root),
            new FakeCatalogAdapter());
        await using var viewModel = new JacketObservationViewModel(
            capture,
            session,
            dispatcher);

        await viewModel.StartSessionAsync(
            Master(), Catalog(), candidate, "master.sqlite", "catalog.sqlite");
        Assert.True(await coordinator.StartAsync(candidate));

        var initialChange = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var initialStable = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(viewModel.StableCandidate))
            {
                return;
            }
            if (viewModel.Detection.State == JacketDetectionState.ChangeCandidate)
            {
                initialChange.TrySetResult();
            }
            if (viewModel.Detection.State == JacketDetectionState.StableCandidate)
            {
                initialStable.TrySetResult();
            }
        };

        var delivered = 0;
        var allFramesDelivered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        capture.FrameReceived += (_, _) =>
        {
            if (Interlocked.Increment(ref delivered) == 5)
            {
                allFramesDelivered.TrySetResult();
            }
        };

        source.Write(Frame(20, 1, 0));
        await initialChange.Task;
        source.Write(Frame(20, 2, 100));
        await initialStable.Task;
        await viewModel.AdoptAsync();

        dispatcher.BlockNext();
        source.Write(Frame(20, 3, 200));
        await dispatcher.Entered;
        source.Write(Frame(20, 4, 300));
        source.Write(Frame(20, 5, 400));
        await allFramesDelivered.Task;
        Assert.Contains("observation_drop=1", viewModel.DetectorProgress);

        var stopping = new TaskCompletionSource<CaptureLifecycleSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<CaptureLifecycleSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inactive = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.IsActive) && !viewModel.IsActive)
            {
                inactive.TrySetResult();
            }
        };
        capture.LifecycleChanged += (_, value) =>
        {
            if (value.State == CaptureLifecycleState.Stopping)
            {
                stopping.TrySetResult(value);
            }
            else if (value.State is CaptureLifecycleState.Stopped or CaptureLifecycleState.Failed)
            {
                stopped.TrySetResult(value);
            }
        };

        var stop = coordinator.StopAsync();
        var stoppingSnapshot = await stopping.Task;
        Assert.Equal(0, stoppingSnapshot.DroppedCount);
        Assert.True(viewModel.IsActive);
        Assert.Equal(0, (await checkpointStore.LoadAsync(viewModel.SessionId)).DroppedFrameCount);

        dispatcher.Release();
        var stoppedSnapshot = await stopped.Task;
        Assert.Equal(3, stoppedSnapshot.DroppedCount);
        await stop;
        await inactive.Task;
        Assert.False(viewModel.IsActive);

        var persisted = await checkpointStore.LoadAsync(viewModel.SessionId);
        Assert.Equal(4, persisted.DroppedFrameCount);
        var checkpointBytes = await File.ReadAllBytesAsync(
            Path.Combine(root, viewModel.SessionId, "checkpoint.json"));
        await viewModel.StopAsync();
        Assert.Equal(
            checkpointBytes,
            await File.ReadAllBytesAsync(Path.Combine(root, viewModel.SessionId, "checkpoint.json")));
        Assert.Equal(4, (await checkpointStore.LoadAsync(viewModel.SessionId)).DroppedFrameCount);
    }

    [Fact]
    public async Task Detection_changes_notify_stable_candidate_and_adopt_displayed_feature()
    {
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var source = new TestFrameSource();
        var candidate = Candidate();
        var windows = new TestWindowEnumerator(candidate);
        var coordinator = new WindowCaptureCoordinator(
            windows,
            new TestSessionFactory(source),
            new ImmediateCaptureDispatcher(),
            ringCapacity: 32);
        var capture = new WindowCaptureViewModel(windows, coordinator);
        var viewModel = new JacketObservationViewModel(
            capture,
            new JacketObservationSession(
                new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                checkpointStore,
                new AtomicObservationArtifactPublisher(root),
                new FakeCatalogAdapter()),
            new ImmediateCaptureDispatcher());
        await using (viewModel)
        {
            var firstStable = new TaskCompletionSource<JacketDetectionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstChange = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var duplicate = new TaskCompletionSource<JacketDetectionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var revisitedSaved = new TaskCompletionSource<JacketDetectionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var revisitChange = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var changedFeature = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStable = new TaskCompletionSource<JacketDetectionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var clearedAfterStop = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            JacketDetectionResult? firstStableResult = null;
            var expectCleared = false;
            var expectRevisitedSaved = false;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(viewModel.StableCandidate))
                {
                    return;
                }
                var value = viewModel.Detection;
                if (value.State == JacketDetectionState.StableCandidate
                    && value.Candidate is not null)
                {
                    if (firstStableResult is null)
                    {
                        firstStableResult = value;
                        firstStable.TrySetResult(value);
                    }
                    else if (value.Candidate.FeatureHash != firstStableResult.Candidate?.FeatureHash)
                    {
                        secondStable.TrySetResult(value);
                    }
                }
                else if (value.State == JacketDetectionState.DuplicatePreview)
                {
                    if (expectRevisitedSaved)
                    {
                        revisitedSaved.TrySetResult(value);
                    }
                    else
                    {
                        duplicate.TrySetResult(value);
                    }
                }
                else if (value.State == JacketDetectionState.ChangeCandidate
                    && value.Candidate is not null
                    && firstStableResult is not null
                    && value.Candidate.FeatureHash != firstStableResult.Candidate?.FeatureHash)
                {
                    changedFeature.TrySetResult(viewModel.StableCandidate);
                }
                else if (expectRevisitedSaved
                    && value.State == JacketDetectionState.ChangeCandidate)
                {
                    revisitChange.TrySetResult();
                }
                else if (expectCleared && value.State == JacketDetectionState.NoFrame)
                {
                    clearedAfterStop.TrySetResult();
                }
                else if (value.State == JacketDetectionState.ChangeCandidate
                    && firstStableResult is null)
                {
                    firstChange.TrySetResult();
                }
            };

            await viewModel.StartSessionAsync(
                Master(), Catalog(), candidate, "master.sqlite", "catalog.sqlite");
            Assert.Equal("—", viewModel.StableCandidate);
            Assert.Equal("DDR GPの曲選択画面を待っています", viewModel.CollectionStateTitle);
            Assert.True(await coordinator.StartAsync(candidate));

            source.Write(Frame(20, 1, 0));
            await firstChange.Task;
            Assert.Equal("ジャケットを確認中", viewModel.CollectionStateTitle);
            source.Write(Frame(20, 2, 100));
            var stableA = await firstStable.Task;
            Assert.Contains($"feature={stableA.Candidate!.FeatureHash}", viewModel.StableCandidate);
            Assert.Equal("新しいジャケットを検出", viewModel.CollectionStateTitle);

            source.Write(Frame(20, 3, 200));
            var duplicateResult = await duplicate.Task;
            Assert.Equal(stableA.Candidate.FeatureHash, duplicateResult.Candidate!.FeatureHash);
            Assert.Equal("新しいジャケットを検出", viewModel.CollectionStateTitle);
            var adoptedA = await viewModel.AdoptAsync();
            Assert.Equal(
                stableA.Candidate.FeatureHash,
                adoptedA.Checkpoint.Observations.Single(
                    observation => observation.ObservationId == adoptedA.ObservationId).FeatureHash);
            Assert.False(viewModel.CanAdopt);

            source.Write(Frame(80, 4, 300));
            var changedDisplay = await changedFeature.Task;
            Assert.Contains($"feature={stableA.Candidate.FeatureHash}", changedDisplay);
            Assert.Equal("ジャケットを確認中", viewModel.CollectionStateTitle);
            Assert.False(viewModel.CanAdopt);

            source.Write(Frame(80, 5, 400));
            var stableB = await secondStable.Task;
            Assert.NotEqual(stableA.Candidate.FeatureHash, stableB.Candidate!.FeatureHash);
            Assert.Contains($"feature={stableB.Candidate.FeatureHash}", viewModel.StableCandidate);
            Assert.True(viewModel.CanAdopt);

            var adopted = await viewModel.AdoptAsync();
            Assert.Equal(
                stableB.Candidate.FeatureHash,
                adopted.Checkpoint.Observations.Single(
                    observation => observation.ObservationId == adopted.ObservationId).FeatureHash);
            Assert.Equal("このジャケットは保存済み", viewModel.CollectionStateTitle);
            Assert.False(viewModel.CanAdopt);

            expectRevisitedSaved = true;
            source.Write(Frame(20, 6, 500));
            await revisitChange.Task;
            source.Write(Frame(20, 7, 600));
            var revisitedA = await revisitedSaved.Task;
            Assert.Equal(stableA.Candidate.FeatureHash, revisitedA.Candidate!.FeatureHash);
            Assert.Equal("このジャケットは保存済み", viewModel.CollectionStateTitle);
            Assert.False(viewModel.CanAdopt);

            expectCleared = true;
            await viewModel.StopAsync();
            await clearedAfterStop.Task;
            Assert.Equal("—", viewModel.StableCandidate);
            Assert.Equal("収集は停止中", viewModel.CollectionStateTitle);
            await coordinator.StopAsync();
        }
    }

    [Fact]
    public void Invalid_roi_never_becomes_a_stable_candidate()
    {
        var detector = new JacketObservationDetector(
            new JacketDetectorOptions(0.01, 2, TimeSpan.Zero),
            new JacketRoi(2000, 0, 10, 10));

        var result = detector.Observe(Frame(20, 1, 0));

        Assert.Equal(JacketDetectionState.InvalidFrame, result.State);
        Assert.Null(result.Candidate);
        Assert.Equal(1, result.InvalidFrameCount);
    }

    [Fact]
    public void Detector_suppresses_revisited_stable_hash_and_invalid_breaks_continuity()
    {
        var detector = new JacketObservationDetector(
            new JacketDetectorOptions(0.01, 2, TimeSpan.Zero));

        detector.Observe(Frame(20, 1, 0));
        Assert.Equal(JacketDetectionState.StableCandidate, detector.Observe(Frame(20, 2, 100)).State);
        detector.Observe(Frame(80, 3, 200));
        Assert.Equal(JacketDetectionState.StableCandidate, detector.Observe(Frame(80, 4, 300)).State);
        detector.Observe(Frame(20, 5, 400));
        Assert.Equal(JacketDetectionState.DuplicatePreview, detector.Observe(Frame(20, 6, 500)).State);

        detector.Reset();
        detector.Observe(Frame(20, 1, 0));
        detector.Observe(new RawCaptureFrame([1, 2, 3], 1280, 720, 2, DateTimeOffset.UnixEpoch.AddMilliseconds(50)));
        Assert.Equal(JacketDetectionState.ChangeCandidate, detector.Observe(Frame(20, 3, 100)).State);
    }

    [Fact]
    public async Task Artifact_publish_is_atomic_idempotent_and_rejects_payload_conflict()
    {
        var identity = Identity("session-atomic");
        var publisher = new AtomicObservationArtifactPublisher(root);
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var artifact = Artifact(identity, [1, 2], [3, 4]);
        var checkpoint = Checkpoint(identity, artifact, "");

        var first = await publisher.PublishAsync(artifact, checkpoint);
        var second = await publisher.PublishAsync(artifact, checkpoint);
        var persisted = await checkpointStore.LoadAsync(identity.SessionId);

        Assert.Equal(ArtifactDisposition.Created, first.Disposition);
        Assert.Equal(ArtifactDisposition.Existing, second.Disposition);
        Assert.Single(persisted.Observations);
        Assert.Equal(first.ArtifactPath, persisted.Observations[0].ArtifactPath);
        Assert.True(File.Exists(Path.Combine(first.ArtifactPath, "source.png")));
        Assert.Empty(Directory.GetDirectories(root, ".staging-*", SearchOption.TopDirectoryOnly));

        var conflict = artifact with
        {
            SourceFrame = artifact.SourceFrame with { PngBytes = [9, 9] },
            SourceImageHash = Hash([9, 9]),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(conflict, checkpoint));
        Assert.Equal(first.SourceImageHash, persisted.Observations[0].SourceImageHash);
    }

    [Fact]
    public async Task Existing_artifact_rejects_missing_or_modified_image()
    {
        var identity = Identity("session-corrupt-image");
        var publisher = new AtomicObservationArtifactPublisher(root);
        var artifact = Artifact(identity, [1, 2], [3, 4]);
        var checkpoint = Checkpoint(identity, artifact, "");
        var first = await publisher.PublishAsync(artifact, checkpoint);
        File.WriteAllBytes(Path.Combine(first.ArtifactPath, "source.png"), [9, 9]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(artifact, checkpoint));
    }

    [Fact]
    public async Task Session_requires_explicit_adoption_and_retries_catalog_failure_without_duplicate()
    {
        var identity = Identity("session-session");
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Failed, null, "temporary failure"),
            new CatalogIngestReceipt(CatalogIngestDisposition.Created, "reference-1", "missing_title_or_artist"));
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            publisher,
            catalog);
        await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");

        await session.ObserveFrameAsync(Frame(40, 1, 0));
        var stable = await session.ObserveFrameAsync(Frame(40, 2, 150));
        Assert.Equal(JacketDetectionState.StableCandidate, stable.State);
        Assert.Equal(
            JacketDetectionState.DuplicatePreview,
            (await session.ObserveFrameAsync(Frame(40, 3, 250))).State);
        Assert.True(session.HasAdoptableCandidate);
        Assert.Empty(Directory.Exists(Path.Combine(root, identity.SessionId))
            ? Directory.GetFiles(Path.Combine(root, identity.SessionId), "*", SearchOption.AllDirectories)
            : []);

        var adopted = await session.AdoptLastStableAsync();
        Assert.Equal(CatalogIngestDisposition.Failed, adopted.Catalog.Disposition);
        Assert.Single(adopted.Checkpoint.Observations);
        Assert.Equal("pending", adopted.Checkpoint.Observations[0].CatalogStatus);
        Assert.Equal(1, catalog.CallCount);

        var retried = await session.RetryPendingCatalogAsync();
        Assert.Single(retried);
        Assert.Equal(CatalogIngestDisposition.Created, retried[0].Catalog.Disposition);
        Assert.Equal("ingested", retried[0].Checkpoint.Observations[0].CatalogStatus);
        Assert.Equal(2, catalog.CallCount);
        Assert.Single(retried[0].Checkpoint.Observations);

        var repeated = await session.AdoptLastStableAsync();
        Assert.Equal(ArtifactDisposition.Existing, repeated.Artifact.Disposition);
        Assert.Equal(CatalogIngestDisposition.Existing, repeated.Catalog.Disposition);
        Assert.Equal(2, catalog.CallCount);
    }

    [Fact]
    public async Task Catalog_success_checkpoint_failure_retries_existing_and_converges_checkpoint()
    {
        var identity = Identity("session-catalog-checkpoint-retry");
        var checkpointStore = new FailOnceCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Created, "reference-1", "created"),
            new CatalogIngestReceipt(CatalogIngestDisposition.Existing, "reference-1", "existing"));
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            publisher,
            catalog);
        await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
        await session.ObserveFrameAsync(Frame(40, 1, 0));
        await session.ObserveFrameAsync(Frame(40, 2, 150));
        checkpointStore.FailNextSave = true;

        var adopted = await session.AdoptLastStableAsync();

        Assert.Equal(CatalogIngestDisposition.Failed, adopted.Catalog.Disposition);
        Assert.Equal("reference-1", adopted.Catalog.CatalogReferenceId);
        Assert.Equal("pending", adopted.Checkpoint.Observations.Single().CatalogStatus);
        Assert.Equal(1, catalog.CallCount);

        var retried = await session.RetryPendingCatalogAsync();

        Assert.Single(retried);
        Assert.Equal(CatalogIngestDisposition.Existing, retried[0].Catalog.Disposition);
        Assert.Equal("reference-1", retried[0].Catalog.CatalogReferenceId);
        Assert.Equal("ingested", retried[0].Checkpoint.Observations.Single().CatalogStatus);
        Assert.Equal("ingested", session.Checkpoint!.Observations.Single().CatalogStatus);
        Assert.Equal(2, catalog.CallCount);
        Assert.Single(session.Checkpoint.Observations);
    }

    [Fact]
    public async Task Retry_pending_catalog_rejects_identity_drift_before_ingest_or_checkpoint_update()
    {
        var identity = Identity("session-retry-preflight");
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Failed, null, "temporary failure"))
        {
            ValidationFailure = new ObservationIdentityDriftException("retry drift"),
            FailValidationOnCall = 3,
        };
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            publisher,
            catalog);
        await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
        await session.ObserveFrameAsync(Frame(40, 1, 0));
        await session.ObserveFrameAsync(Frame(40, 2, 150));
        var adopted = await session.AdoptLastStableAsync();
        var checkpointPath = Path.Combine(root, identity.SessionId, "checkpoint.json");
        var checkpointBytes = await File.ReadAllBytesAsync(checkpointPath);

        await Assert.ThrowsAsync<ObservationIdentityDriftException>(
            () => session.RetryPendingCatalogAsync());

        Assert.Equal(3, catalog.ValidationCallCount);
        Assert.Equal(1, catalog.CallCount);
        Assert.Equal(checkpointBytes, await File.ReadAllBytesAsync(checkpointPath));
        Assert.Equal("pending", session.Checkpoint!.Observations.Single().CatalogStatus);
        Assert.Equal("pending", adopted.Checkpoint.Observations.Single().CatalogStatus);
    }

    [Fact]
    public async Task Retry_catalog_explicitly_reprocesses_deferred_v2_checkpoint()
    {
        var identity = Identity("session-retry-no-pending") with { CatalogSchemaVersion = 2 };
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(
                CatalogIngestDisposition.DeferredUnsupportedSchema,
                null,
                "deferred"),
            new CatalogIngestReceipt(
                CatalogIngestDisposition.Existing,
                "reference-v2",
                "same receipt"));
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            publisher,
            catalog);
        await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
        await session.ObserveFrameAsync(Frame(40, 1, 0));
        await session.ObserveFrameAsync(Frame(40, 2, 150));
        var adopted = await session.AdoptLastStableAsync();
        var checkpointPath = Path.Combine(root, identity.SessionId, "checkpoint.json");
        var checkpointBytes = await File.ReadAllBytesAsync(checkpointPath);

        var retried = await session.RetryPendingCatalogAsync();

        Assert.Single(retried);
        Assert.Equal(CatalogIngestDisposition.Existing, retried[0].Catalog.Disposition);
        Assert.Equal(3, catalog.ValidationCallCount);
        Assert.Equal(2, catalog.CallCount);
        Assert.NotEqual(checkpointBytes, await File.ReadAllBytesAsync(checkpointPath));
        Assert.Equal("ingested", session.Checkpoint!.Observations.Single().CatalogStatus);
        Assert.Equal("deferred", adopted.Checkpoint.Observations.Single().CatalogStatus);
        Assert.Equal("reference-v2", session.Checkpoint.Observations.Single().CatalogReferenceId);
    }

    [Fact]
    public async Task Revisited_preview_adopts_the_candidate_shown_in_the_ui()
    {
        var identity = Identity("session-revisited-preview");
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Created, "reference-a", "created"));
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            new AtomicObservationCheckpointStore(root),
            new AtomicObservationArtifactPublisher(root),
            catalog);
        await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
        await session.ObserveFrameAsync(Frame(20, 1, 0));
        var stableA = await session.ObserveFrameAsync(Frame(20, 2, 100));
        var adoptedA = await session.AdoptLastStableAsync();
        await session.ObserveFrameAsync(Frame(80, 3, 200));
        await session.ObserveFrameAsync(Frame(80, 4, 300));
        await session.ObserveFrameAsync(Frame(20, 5, 400));
        var revisitedA = await session.ObserveFrameAsync(Frame(20, 6, 500));

        var repeatedA = await session.AdoptLastStableAsync();

        Assert.Equal(JacketDetectionState.DuplicatePreview, revisitedA.State);
        Assert.Equal(stableA.Candidate!.FeatureHash, revisitedA.Candidate!.FeatureHash);
        Assert.Equal(adoptedA.ObservationId, repeatedA.ObservationId);
        Assert.Equal(ArtifactDisposition.Existing, repeatedA.Artifact.Disposition);
        Assert.Single(repeatedA.Checkpoint.Observations);
        Assert.Equal(1, catalog.CallCount);
    }

    [Fact]
    public async Task Resume_is_strict_and_does_not_regenerate_adopted_observation()
    {
        var identity = Identity("session-resume");
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Existing, "reference-1", "missing_title_or_artist"));
        await using (var initial = new JacketObservationSession(
                         new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                         checkpointStore,
                         publisher,
                         catalog))
        {
            await initial.StartAsync(identity, "master.sqlite", "catalog.sqlite");
            await initial.ObserveFrameAsync(Frame(60, 1, 0));
            await initial.ObserveFrameAsync(Frame(60, 2, 150));
            await initial.AdoptLastStableAsync();
            await initial.StopAsync();
        }

        await using var resumed = new JacketObservationSession(
            new JacketObservationDetector(), checkpointStore, publisher, catalog);
        var compatible = await resumed.ResumeAsync(
            ResumeRequest(identity), "master.sqlite", "catalog.sqlite");
        await resumed.StopAsync();
        var incompatible = await resumed.ResumeAsync(
            ResumeRequest(identity) with { DetectorVersion = "old-detector" },
            "master.sqlite",
            "catalog.sqlite");

        Assert.True(compatible.Compatible);
        Assert.Single(compatible.Checkpoint!.Observations);
        Assert.False(incompatible.Compatible);
        Assert.False(resumed.IsActive);
        Assert.Equal(1, catalog.CallCount);
    }

    [Fact]
    public async Task Stop_persists_progress_and_resume_restores_session_dedupe()
    {
        var identity = Identity("session-progress");
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Existing, "reference-1", "existing"));
        await using (var initial = new JacketObservationSession(
                         new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                         checkpointStore,
                         publisher,
                         catalog))
        {
            await initial.StartAsync(identity, "master.sqlite", "catalog.sqlite");
            await initial.ObserveFrameAsync(Frame(20, 1, 0));
            await initial.ObserveFrameAsync(Frame(20, 2, 100));
            await initial.AdoptLastStableAsync();
            await initial.ObserveFrameAsync(Frame(80, 3, 200));
            await initial.ObserveFrameAsync(Frame(80, 4, 300));
            await initial.UpdateDroppedFrameCountAsync(7);
            await initial.StopAsync();
        }

        var persisted = await checkpointStore.LoadAsync(identity.SessionId);
        Assert.Equal(4, persisted.ProcessedFrameCount);
        Assert.Equal(7, persisted.DroppedFrameCount);
        Assert.Equal(2, persisted.StableFeatureHashes.Count);
        Assert.Contains(persisted.LastStableFeatureHash!, persisted.StableFeatureHashes);

        await using var resumed = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            publisher,
            catalog);
        Assert.True((await resumed.ResumeAsync(
            ResumeRequest(identity), "master.sqlite", "catalog.sqlite")).Compatible);
        await resumed.ObserveFrameAsync(Frame(80, 5, 400));
        Assert.Equal(
            JacketDetectionState.DuplicatePreview,
            (await resumed.ObserveFrameAsync(Frame(80, 6, 500))).State);
        var adoptedAfterResume = await resumed.AdoptLastStableAsync();
        Assert.Equal(2, adoptedAfterResume.Checkpoint.Observations.Count);
        Assert.Equal(adoptedAfterResume.Checkpoint.LastStableFeatureHash,
            adoptedAfterResume.Checkpoint.Observations[1].FeatureHash);
    }

    [Fact]
    public async Task Cancellation_after_publish_rolls_back_new_artifact_but_preserves_existing_one()
    {
        var identity = Identity("session-cancel-publish");
        var store = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var preflightCancel = new FakeCatalogAdapter
        {
            ValidationFailure = new OperationCanceledException("cancel after publish"),
            FailValidationOnCall = 2,
        };
        await using (var session = new JacketObservationSession(
                         new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                         store, publisher, preflightCancel))
        {
            await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
            await session.ObserveFrameAsync(Frame(20, 1, 0));
            await session.ObserveFrameAsync(Frame(20, 2, 100));
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.AdoptLastStableAsync());
        }
        Assert.False(Directory.Exists(Path.Combine(root, identity.SessionId)));

        var existingIdentity = Identity("session-cancel-existing");
        var ingestCancel = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Failed, null, "retry"));
        await using var existingSession = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            store, publisher, ingestCancel);
        await existingSession.StartAsync(existingIdentity, "master.sqlite", "catalog.sqlite");
        await existingSession.ObserveFrameAsync(Frame(20, 1, 0));
        await existingSession.ObserveFrameAsync(Frame(20, 2, 100));
        var first = await existingSession.AdoptLastStableAsync();
        var checkpointPath = Path.Combine(root, existingIdentity.SessionId, "checkpoint.json");
        var checkpointBytes = await File.ReadAllBytesAsync(checkpointPath);
        ingestCancel.CancelIngestOnCall = 2;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => existingSession.AdoptLastStableAsync());

        Assert.True(Directory.Exists(first.Artifact.ArtifactPath));
        Assert.Equal(checkpointBytes, await File.ReadAllBytesAsync(checkpointPath));
    }

    [Fact]
    public async Task Stop_checkpoint_failure_can_be_retried_while_session_stays_inactive()
    {
        var identity = Identity("session-stop-retry");
        var checkpointStore = new FailOnceCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Existing, "reference-1", "existing"));
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            publisher,
            catalog);
        await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
        await session.ObserveFrameAsync(Frame(20, 1, 0));
        await session.ObserveFrameAsync(Frame(20, 2, 100));
        await session.AdoptLastStableAsync();
        await session.ObserveFrameAsync(Frame(80, 3, 200));
        checkpointStore.FailNextSave = true;

        await Assert.ThrowsAsync<IOException>(() => session.StopAsync());
        Assert.False(session.IsActive);
        await session.StopAsync();
        Assert.Equal(3, (await checkpointStore.LoadAsync(identity.SessionId)).ProcessedFrameCount);
    }

    [Fact]
    public async Task Preflight_drift_and_corrupt_checkpoint_are_side_effect_free()
    {
        var identity = Identity("session-preflight");
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter
        {
            ValidationFailure = new InvalidOperationException("drift"),
        };
        await using (var session = new JacketObservationSession(
                         new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                         checkpointStore,
                         publisher,
                         catalog))
        {
            await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
            await session.ObserveFrameAsync(Frame(20, 1, 0));
            await session.ObserveFrameAsync(Frame(20, 2, 100));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.AdoptLastStableAsync());
        }
        Assert.False(Directory.Exists(Path.Combine(root, identity.SessionId)));

        var racedIdentity = Identity("session-preflight-race");
        var racedCatalog = new FakeCatalogAdapter
        {
            ValidationFailure = new ObservationIdentityDriftException("raced drift"),
            FailValidationOnCall = 2,
        };
        await using (var raced = new JacketObservationSession(
                         new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                         checkpointStore,
                         publisher,
                         racedCatalog))
        {
            await raced.StartAsync(racedIdentity, "master.sqlite", "catalog.sqlite");
            await raced.ObserveFrameAsync(Frame(20, 1, 0));
            await raced.ObserveFrameAsync(Frame(20, 2, 100));
            await Assert.ThrowsAsync<ObservationIdentityDriftException>(
                () => raced.AdoptLastStableAsync());
        }
        Assert.False(Directory.Exists(Path.Combine(root, racedIdentity.SessionId)));

        var validCatalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Existing, "reference-1", "existing"));
        await using (var session = new JacketObservationSession(
                         new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                         checkpointStore,
                         publisher,
                         validCatalog))
        {
            await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
            await session.ObserveFrameAsync(Frame(20, 1, 0));
            await session.ObserveFrameAsync(Frame(20, 2, 100));
            await session.AdoptLastStableAsync();
            await session.StopAsync();
        }
        var checkpointPath = Path.Combine(root, identity.SessionId, "checkpoint.json");
        var originalCheckpoint = await File.ReadAllTextAsync(checkpointPath);
        var nullHashMutations = new Action<JsonNode>[]
        {
            node => node["observations"]![0]!["source_image_hash"] = null,
            node => node["stable_feature_hashes"]![0] = null,
        };
        var validationCallsBeforeNullHashes = validCatalog.ValidationCallCount;
        var ingestCallsBeforeNullHashes = validCatalog.CallCount;
        foreach (var mutate in nullHashMutations)
        {
            var nullHashDocument = JsonNode.Parse(originalCheckpoint)!;
            mutate(nullHashDocument);
            await File.WriteAllTextAsync(checkpointPath, nullHashDocument.ToJsonString());
            var corruptBytes = await File.ReadAllBytesAsync(checkpointPath);
            await using var nullHashCorrupt = new JacketObservationSession(
                new JacketObservationDetector(), checkpointStore, publisher, validCatalog);

            Assert.False((await nullHashCorrupt.ResumeAsync(
                ResumeRequest(identity), "master.sqlite", "catalog.sqlite")).Compatible);
            Assert.False(nullHashCorrupt.IsActive);
            Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(checkpointPath));
            Assert.Equal(validationCallsBeforeNullHashes, validCatalog.ValidationCallCount);
            Assert.Equal(ingestCallsBeforeNullHashes, validCatalog.CallCount);
        }

        var document = JsonNode.Parse(originalCheckpoint)!;
        document["observations"]![0]!["source_image_hash"] = new string('0', 64);
        await File.WriteAllTextAsync(checkpointPath, document.ToJsonString());
        await using (var semanticCorrupt = new JacketObservationSession(
                         new JacketObservationDetector(), checkpointStore, publisher, validCatalog))
        {
            Assert.False((await semanticCorrupt.ResumeAsync(
                ResumeRequest(identity), "master.sqlite", "catalog.sqlite")).Compatible);
        }

        document = JsonNode.Parse(originalCheckpoint)!;
        document["observations"]![0]!["artifact_path"] = Path.Combine(root, "outside");
        await File.WriteAllTextAsync(checkpointPath, document.ToJsonString());
        await using var corrupt = new JacketObservationSession(
            new JacketObservationDetector(), checkpointStore, publisher, validCatalog);
        Assert.False((await corrupt.ResumeAsync(
            ResumeRequest(identity), "master.sqlite", "catalog.sqlite")).Compatible);
        Assert.False(corrupt.IsActive);
    }

    [Fact]
    public async Task Resume_rejects_modified_canonical_manifest_payload_without_side_effects()
    {
        var identity = Identity("session-manifest-payload");
        var store = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter(
            new CatalogIngestReceipt(CatalogIngestDisposition.Failed, null, "pending"));
        string artifactPath;
        await using (var initial = new JacketObservationSession(
                         new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                         store, publisher, catalog))
        {
            await initial.StartAsync(identity, "master.sqlite", "catalog.sqlite");
            await initial.ObserveFrameAsync(Frame(20, 1, 0));
            await initial.ObserveFrameAsync(Frame(20, 2, 100));
            artifactPath = (await initial.AdoptLastStableAsync()).Artifact.ArtifactPath;
            await initial.StopAsync();
        }
        var checkpointPath = Path.Combine(root, identity.SessionId, "checkpoint.json");
        var checkpointBytes = await File.ReadAllBytesAsync(checkpointPath);
        var manifestPath = Path.Combine(artifactPath, "observation.json");
        var originalManifest = await File.ReadAllTextAsync(manifestPath);
        var mutations = new Action<JsonNode>[]
        {
            node => node["observed_title"] = "forged title",
            node => node["observed_artist"] = "forged artist",
            node => node["observation_status"] = "confirmed",
            node => node["feature_version"] = "old-feature",
            node => node["sample_width"] = 8,
            node => node["roi_x"] = 0,
            node => node["created_at_utc"] = "1970-01-01T00:00:00+00:00",
            node => node["feature_hash"] = null,
        };
        foreach (var mutate in mutations)
        {
            var document = JsonNode.Parse(originalManifest)!;
            mutate(document);
            await File.WriteAllTextAsync(manifestPath, document.ToJsonString());
            await using var resumed = new JacketObservationSession(
                new JacketObservationDetector(), store, publisher, catalog);

            Assert.False((await resumed.ResumeAsync(
                ResumeRequest(identity), "master.sqlite", "catalog.sqlite")).Compatible);
            Assert.False(resumed.IsActive);
            Assert.Equal(checkpointBytes, await File.ReadAllBytesAsync(checkpointPath));
        }
        await File.WriteAllTextAsync(manifestPath, originalManifest);
        Assert.Equal(1, catalog.CallCount);
    }

    [Fact]
    public async Task Ingest_identity_drift_rolls_back_published_artifact_and_checkpoint()
    {
        var identity = Identity("session-ingest-drift");
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var catalog = new FakeCatalogAdapter
        {
            IngestFailure = new ObservationIdentityDriftException("artifact hash mismatch"),
        };
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            publisher,
            catalog);
        await session.StartAsync(identity, "master.sqlite", "catalog.sqlite");
        await session.ObserveFrameAsync(Frame(20, 1, 0));
        await session.ObserveFrameAsync(Frame(20, 2, 100));

        await Assert.ThrowsAsync<ObservationIdentityDriftException>(
            () => session.AdoptLastStableAsync());

        Assert.Equal(1, catalog.CallCount);
        Assert.False(Directory.Exists(Path.Combine(root, identity.SessionId)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => checkpointStore.LoadAsync(identity.SessionId));
    }

    [Fact]
    public async Task Python_adapter_selects_explicit_v2_ingest_and_passes_artifact_hash()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(
                0,
                "{\"disposition\":\"created\",\"reference_id\":\"reference-v2\","
                    + "\"review_status\":\"unresolved\",\"reason\":\"observation_unresolved\"}",
                "")));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter") with { CatalogSchemaVersion = 2 };
        var artifact = Artifact(identity, [1, 2], [3, 4]);

        await adapter.ValidateSessionAsync(identity, "catalog.sqlite", "master.sqlite");
        var receipt = await adapter.IngestAsync(
            artifact, "crop.png", "catalog.sqlite", "master.sqlite");

        Assert.Equal(CatalogIngestDisposition.Created, receipt.Disposition);
        Assert.Equal("reference-v2", receipt.CatalogReferenceId);
        Assert.Equal(2, runner.Requests.Count);
        Assert.Contains("validate-session", runner.Requests[0].Arguments);
        Assert.Contains("--expected-catalog-created-at", runner.Requests[0].Arguments);
        Assert.Contains(identity.CatalogCreatedAt, runner.Requests[0].Arguments);
        Assert.Contains("ingest-v2", runner.Requests[1].Arguments);
        Assert.Contains("--observation-id", runner.Requests[1].Arguments);
        Assert.Contains(artifact.ObservationId, runner.Requests[1].Arguments);
        Assert.Contains("--expected-image-hash", runner.Requests[1].Arguments);
        Assert.Contains(artifact.JacketCropHash, runner.Requests[1].Arguments);
    }

    [Theory]
    [InlineData("observation artifact image hash does not match its checkpoint")]
    [InlineData("observation artifact image is empty")]
    [InlineData("observation artifact image is invalid")]
    [InlineData("source image does not exist: crop.png")]
    [InlineData("master source hash drift detected during v2 observation ingest")]
    [InlineData("observation id was already used with different canonical payload")]
    public async Task Python_adapter_classifies_artifact_and_identity_rejections_as_drift(
        string standardError)
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(1, "", standardError)));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter-rejected") with { CatalogSchemaVersion = 2 };
        var artifact = Artifact(identity, [1, 2], [3, 4]);

        var exception = await Assert.ThrowsAsync<ObservationIdentityDriftException>(
            () => adapter.IngestAsync(artifact, "crop.png", "catalog.sqlite", "master.sqlite"));

        Assert.Contains("identity/payload/artifact conflict", exception.Message);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task Python_adapter_keeps_transient_ingest_failures_retryable()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(1, "", "database is locked")));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter-transient") with { CatalogSchemaVersion = 2 };
        var artifact = Artifact(identity, [1, 2], [3, 4]);

        var receipt = await adapter.IngestAsync(
            artifact, "crop.png", "catalog.sqlite", "master.sqlite");

        Assert.Equal(CatalogIngestDisposition.Failed, receipt.Disposition);
        Assert.Null(receipt.CatalogReferenceId);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task Python_adapter_keeps_v1_ingest_command_and_observation_options()
    {
        var runner = new StubProcessRunner((request, _) => Task.FromResult(
            request.Arguments.Contains("validate-session")
                ? new ProcessResult(0, "", "")
                : new ProcessResult(
                    0,
                    "{\"disposition\":\"created\",\"reference_id\":\"reference-v1\","
                        + "\"review_status\":\"unresolved\",\"reason\":\"created\"}",
                    "")));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter-v1");
        var artifact = Artifact(identity, [1, 2], [3, 4]) with
        {
            ObservedTitle = "Observed title",
            ObservedArtist = "Observed artist",
        };

        await adapter.ValidateSessionAsync(identity, "catalog.sqlite", "master.sqlite");
        var receipt = await adapter.IngestAsync(
            artifact, "crop.png", "catalog.sqlite", "master.sqlite");

        Assert.Equal(CatalogIngestDisposition.Created, receipt.Disposition);
        Assert.Equal(2, runner.Requests.Count);
        Assert.Contains("ingest", runner.Requests[1].Arguments);
        Assert.DoesNotContain("ingest-v2", runner.Requests[1].Arguments);
        Assert.Contains("--source-capture-id", runner.Requests[1].Arguments);
        Assert.Contains(artifact.ObservationId, runner.Requests[1].Arguments);
        Assert.Contains("--observed-title", runner.Requests[1].Arguments);
        Assert.Contains(artifact.ObservedTitle, runner.Requests[1].Arguments);
        Assert.Contains("--observed-artist", runner.Requests[1].Arguments);
        Assert.Contains(artifact.ObservedArtist, runner.Requests[1].Arguments);
        Assert.Contains("--observation-status", runner.Requests[1].Arguments);
        Assert.Contains(artifact.ObservationStatus, runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task Python_adapter_rejects_unsupported_schema_without_running_python()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(0, "", "")));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter-unsupported") with { CatalogSchemaVersion = 3 };
        var artifact = Artifact(identity, [1, 2], [3, 4]);

        var exception = await Assert.ThrowsAsync<ObservationIdentityDriftException>(
            () => adapter.IngestAsync(artifact, "crop.png", "catalog.sqlite", "master.sqlite"));

        Assert.Contains("unsupported catalog schema version", exception.Message);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Python_adapter_rejects_session_schema_drift_before_ingest()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(1, "", "catalog schema version mismatch")));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter-schema-drift") with { CatalogSchemaVersion = 2 };

        var exception = await Assert.ThrowsAsync<ObservationIdentityDriftException>(
            () => adapter.ValidateSessionAsync(identity, "catalog.sqlite", "master.sqlite"));

        Assert.Contains("identity drift preflight failed", exception.Message);
        Assert.Single(runner.Requests);
        Assert.Contains("validate-session", runner.Requests[0].Arguments);
    }

    private static ProjectionMaster Master() => new()
    {
        Path = "master.sqlite",
        MasterVersion = "master-v1",
        SourceHash = "master-hash",
        SongCount = 1,
        ChartCount = 1,
        GrandPrixSongCount = 1,
    };

    private static ProjectionCatalog Catalog() => new()
    {
        Path = "catalog.sqlite",
        CatalogIdentity = "ddrgp-local-jacket-reference-catalog",
        SchemaVersion = 1,
        CreatedAt = "catalog-created",
        CurrentFeatureExtractorVersion = JacketObservationVersions.FeatureExtractor,
    };

    private static WindowCandidate Candidate() => new(
        new WindowIdentitySnapshot(
            (nint)0x1234,
            42,
            100,
            "ddrgp",
            "DDR GRAND PRIX",
            "game",
            1280,
            720,
            true,
            false),
        "test candidate",
        null);

    private sealed class TestWindowEnumerator(WindowCandidate candidate) : IWindowEnumerator
    {
        public Task<IReadOnlyList<WindowCandidate>> EnumerateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WindowCandidate>>([candidate]);

        public WindowIdentitySnapshot? TryGetSnapshot(nint handle) => candidate.Identity;
    }

    private sealed class TestSessionFactory(IWindowCaptureFrameSource source)
        : IWindowCaptureSessionFactory
    {
        public bool IsSupported => true;

        public Task<IWindowCaptureFrameSource> StartAsync(
            WindowIdentitySnapshot target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(source);
    }

    private sealed class TestFrameSource : IWindowCaptureFrameSource
    {
        private readonly long droppedOnStop;
        private readonly Channel<RawCaptureFrame> frames = Channel.CreateUnbounded<RawCaptureFrame>();
        private readonly TaskCompletionSource<CaptureEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private long droppedCount;
        private int stopped;

        public TestFrameSource(long droppedOnStop = 0) => this.droppedOnStop = droppedOnStop;

        public long DroppedCount => Interlocked.Read(ref droppedCount);
        public Task<CaptureEndReason> Completion => completion.Task;

        public void Write(RawCaptureFrame frame) => frames.Writer.TryWrite(frame);

        public async IAsyncEnumerable<RawCaptureFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var frame in frames.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }

        public Task StopAsync()
        {
            if (Interlocked.Exchange(ref stopped, 1) == 0)
            {
                Interlocked.Add(ref droppedCount, droppedOnStop);
                completion.TrySetResult(CaptureEndReason.ExplicitStop);
                frames.Writer.TryComplete();
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            completion.TrySetResult(CaptureEndReason.ExplicitStop);
            frames.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingObservationDispatcher : ICaptureDispatcher
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int blockNext;

        public Task Entered => entered.Task;

        public void BlockNext() => Volatile.Write(ref blockNext, 1);

        public async Task InvokeAsync(Action action)
        {
            if (Interlocked.Exchange(ref blockNext, 0) == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
            action();
        }

        public void Release() => release.TrySetResult();
    }

    private ObservationArtifact Artifact(
        ObservationSessionIdentity identity,
        byte[] source,
        byte[] crop) => new(
        ObservationId(identity.SessionId, TestFeatureHash),
        identity,
        new RawCaptureFrame(source, 1280, 720, 1, DateTimeOffset.UtcNow),
        crop,
        new JacketFeatureObservation(
            JacketObservationVersions.FrameFeature,
            JacketObservationVersions.Roi,
            JacketRoi.Base,
            TestFeatureHash,
            0,
            16,
            16),
        Hash(source),
        Hash(crop),
        "",
        "",
        "unresolved",
        DateTimeOffset.UtcNow);

    private static ObservationCheckpoint Checkpoint(
        ObservationSessionIdentity identity,
        ObservationArtifact artifact,
        string artifactPath) => new(
        JacketObservationVersions.SessionCheckpoint,
        identity,
        artifact.Feature.FeatureHash,
        [artifact.Feature.FeatureHash],
        1,
        0,
        [new ObservationCheckpointObservation(
            artifact.ObservationId,
            artifact.SourceImageHash,
            artifact.JacketCropHash,
            artifact.Feature.FeatureHash,
            "pending",
            null,
            artifactPath,
            artifact.CreatedAtUtc)],
        DateTimeOffset.UtcNow);

    private static ObservationSessionIdentity Identity(string sessionId) => new(
        sessionId,
        "master-v1",
        "master-hash",
        "ddrgp-local-jacket-reference-catalog",
        1,
        JacketObservationVersions.FeatureExtractor,
        JacketObservationVersions.Detector,
        JacketObservationVersions.Roi,
        new WindowIdentitySnapshot(
            (nint)0x1234, 42, 100, "ddrgp", "DDR GRAND PRIX", "game", 1280, 720, true, false),
        DateTimeOffset.UtcNow,
        JacketObservationVersions.FrameClock,
        "catalog-created");

    private static ObservationResumeRequest ResumeRequest(ObservationSessionIdentity value) => new(
        value.SessionId,
        value.MasterVersion,
        value.MasterSourceHash,
        value.CatalogIdentity,
        value.CatalogSchemaVersion,
        value.FeatureExtractorVersion,
        value.DetectorVersion,
        value.RoiVersion,
        value.Window,
        value.FrameClockVersion,
        value.CatalogCreatedAt);

    private static RawCaptureFrame Frame(byte value, long sequence, long milliseconds) =>
        new(EncodePng(value), 1280, 720, sequence, DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds));

    private static byte[] EncodePng(byte value)
    {
        var pixels = new byte[1280 * 720 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = value;
            pixels[index + 1] = value;
            pixels[index + 2] = value;
            pixels[index + 3] = 255;
        }
        var source = BitmapSource.Create(
            1280, 720, 96, 96, PixelFormats.Bgra32, null, pixels, 1280 * 4);
        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string TestFeatureHash => Hash(Encoding.UTF8.GetBytes("feature-1"));

    private static string ObservationId(string sessionId, string featureHash) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{sessionId}\0{featureHash}"))).ToLowerInvariant();

    private sealed class FakeCatalogAdapter(params CatalogIngestReceipt[] receipts)
        : IObservationCatalogAdapter
    {
        private readonly Queue<CatalogIngestReceipt> receipts = new(receipts);

        public int CallCount { get; private set; }
        public int ValidationCallCount { get; private set; }
        public Exception? ValidationFailure { get; init; }
        public Exception? IngestFailure { get; init; }
        public int FailValidationOnCall { get; init; } = 1;
        public int CancelIngestOnCall { get; set; }

        public Task ValidateSessionAsync(
            ObservationSessionIdentity session,
            string catalogPath,
            string masterPath,
            CancellationToken cancellationToken = default)
        {
            ValidationCallCount++;
            return ValidationFailure is null || ValidationCallCount != FailValidationOnCall
                ? Task.CompletedTask
                : Task.FromException(ValidationFailure);
        }

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
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == CancelIngestOnCall)
            {
                throw new OperationCanceledException("injected ingest cancellation");
            }
            if (IngestFailure is not null)
            {
                throw IngestFailure;
            }
            return Task.FromResult(
                receipts.Count == 0
                    ? new CatalogIngestReceipt(CatalogIngestDisposition.Existing, "reference-1", "existing")
                    : receipts.Dequeue());
        }
    }

    private sealed class FailOnceCheckpointStore(string evidenceRoot) : IObservationCheckpointStore
    {
        private readonly AtomicObservationCheckpointStore inner = new(evidenceRoot);

        public bool FailNextSave { get; set; }

        public Task SaveAsync(
            ObservationCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("injected checkpoint failure");
            }
            return inner.SaveAsync(checkpoint, cancellationToken);
        }

        public Task<ObservationCheckpoint> LoadAsync(
            string sessionId,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(sessionId, cancellationToken);
    }
}
