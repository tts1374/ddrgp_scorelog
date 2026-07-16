using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public void Information_detector_reports_presence_stability_and_title_hash_changes()
    {
        var detector = new InformationTitleLineDetector(
            new InformationTitleLineDetectorOptions(
                StableFrameCount: 3,
                MinimumStableDuration: TimeSpan.FromMilliseconds(100)));

        var first = detector.Observe(InformationFrame(1, 1, 0));
        var settling = detector.Observe(InformationFrame(1, 2, 50));
        var stable = detector.Observe(InformationFrame(1, 3, 150));
        var changed = detector.Observe(InformationFrame(2, 4, 200));

        Assert.Equal(InformationTitleLineState.Settling, first.State);
        Assert.Equal(InformationTitleLineState.Settling, settling.State);
        Assert.Equal(InformationTitleLineState.Stable, stable.State);
        Assert.True(stable.IsDisplayed);
        Assert.True(stable.IsStable);
        Assert.Equal(64, stable.TitleLineHash!.Length);
        Assert.Equal(JacketObservationVersions.InformationTitleLineFeature, stable.FeatureVersion);
        Assert.Equal(InformationTitleLineState.Settling, changed.State);
        Assert.NotEqual(stable.TitleLineHash, changed.TitleLineHash);
        Assert.Equal(1, changed.ConsecutiveFrameCount);
    }

    [Fact]
    public void Information_detector_rejects_absent_and_non_monotonic_frames()
    {
        var detector = new InformationTitleLineDetector(
            new InformationTitleLineDetectorOptions(
                StableFrameCount: 2,
                MinimumStableDuration: TimeSpan.Zero));

        var absent = detector.Observe(Frame(20, 1, 0));
        detector.Observe(InformationFrame(1, 2, 100));
        var stable = detector.Observe(InformationFrame(1, 3, 200));
        var backwards = detector.Observe(InformationFrame(1, 4, 150));
        var restarted = detector.Observe(InformationFrame(1, 5, 300));
        var invalid = detector.Observe(new RawCaptureFrame(
            [1, 2, 3],
            1280,
            720,
            6,
            DateTimeOffset.UnixEpoch.AddMilliseconds(400)));

        Assert.Equal(InformationTitleLineState.NotDisplayed, absent.State);
        Assert.False(absent.IsDisplayed);
        Assert.Null(absent.TitleLineHash);
        Assert.Equal(InformationTitleLineState.Stable, stable.State);
        Assert.Equal(InformationTitleLineState.InvalidFrame, backwards.State);
        Assert.Equal(1, backwards.InvalidFrameCount);
        Assert.Equal(InformationTitleLineState.Settling, restarted.State);
        Assert.Equal(1, restarted.ConsecutiveFrameCount);
        Assert.Equal(InformationTitleLineState.InvalidFrame, invalid.State);
        Assert.Equal(2, invalid.InvalidFrameCount);
    }

    [Fact]
    public void Composite_identity_is_deterministic_and_separates_title_line_hashes()
    {
        var jacketHash = Hash(Encoding.UTF8.GetBytes("shared-jacket"));
        var titleA = Hash(Encoding.UTF8.GetBytes("New York EVOLVED Type A"));
        var titleB = Hash(Encoding.UTF8.GetBytes("New York EVOLVED Type B"));

        var first = CompositeObservationIdentityBuilder.Create(
            JacketObservationVersions.FrameFeature,
            jacketHash,
            JacketObservationVersions.InformationTitleLineFeature,
            titleA);
        var restarted = CompositeObservationIdentityBuilder.Create(
            JacketObservationVersions.FrameFeature,
            jacketHash,
            JacketObservationVersions.InformationTitleLineFeature,
            titleA);
        var variant = CompositeObservationIdentityBuilder.Create(
            JacketObservationVersions.FrameFeature,
            jacketHash,
            JacketObservationVersions.InformationTitleLineFeature,
            titleB);

        Assert.Equal(first, restarted);
        Assert.NotEqual(first.IdentityHash, variant.IdentityHash);
        Assert.Equal(JacketObservationVersions.CompositeIdentity, first.IdentityVersion);
    }

    [Fact]
    public async Task Composite_session_requires_same_frame_title_and_persists_distinct_shared_jacket_variants()
    {
        var identity = Identity("session-composite");
        var checkpointStore = new AtomicObservationCheckpointStore(root);
        var publisher = new AtomicObservationArtifactPublisher(root);
        var informationDetector = StableInformationDetector();
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            checkpointStore,
            publisher,
            new FakeCatalogAdapter());
        await session.StartAsync(
            identity,
            "master.sqlite",
            "catalog.sqlite",
            requireCompositeIdentity: true);

        var firstFrame = CompositeFrame(40, 1, 1, 0);
        await session.ObserveFrameAsync(
            firstFrame,
            information: informationDetector.Observe(firstFrame));
        var mismatchedInformation = informationDetector.Observe(
            CompositeFrame(40, 1, 2, 100));
        var stableJacketWithoutPair = await session.ObserveFrameAsync(
            CompositeFrame(40, 1, 3, 100),
            information: mismatchedInformation);
        Assert.Null(stableJacketWithoutPair.Candidate!.CompositeIdentity);
        Assert.False(session.HasAdoptableCandidate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AdoptLastStableAsync());
        Assert.False(Directory.Exists(Path.Combine(root, identity.SessionId)));

        var pairedA = CompositeFrame(40, 1, 4, 200);
        var stableA = await session.ObserveFrameAsync(
            pairedA,
            information: informationDetector.Observe(pairedA));
        var adoptedA = await session.AdoptLastStableAsync();
        var pairedB1 = CompositeFrame(40, 2, 5, 300);
        await session.ObserveFrameAsync(
            pairedB1,
            information: informationDetector.Observe(pairedB1));
        var pairedB2 = CompositeFrame(40, 2, 6, 400);
        var stableB = await session.ObserveFrameAsync(
            pairedB2,
            information: informationDetector.Observe(pairedB2));
        var adoptedB = await session.AdoptLastStableAsync();

        Assert.Equal(stableA.Candidate!.FeatureHash, stableB.Candidate!.FeatureHash);
        Assert.NotEqual(
            stableA.Candidate.CompositeIdentity!.IdentityHash,
            stableB.Candidate.CompositeIdentity!.IdentityHash);
        Assert.NotEqual(adoptedA.ObservationId, adoptedB.ObservationId);
        Assert.Equal(2, adoptedB.Checkpoint.Observations.Count);
        Assert.All(adoptedB.Checkpoint.Observations, observation =>
        {
            Assert.Equal(JacketObservationVersions.FrameFeature, observation.JacketFeatureVersion);
            Assert.Equal(
                JacketObservationVersions.InformationTitleLineFeature,
                observation.TitleLineFeatureVersion);
            Assert.Equal(JacketObservationVersions.CompositeIdentity, observation.CompositeIdentityVersion);
        });
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(adoptedB.Artifact.ArtifactPath, "observation.json")))!;
        Assert.Equal(JacketObservationVersions.ObservationManifest, manifest["manifest_version"]!.GetValue<string>());
        Assert.Equal(
            stableB.Candidate.CompositeIdentity.IdentityHash,
            manifest["composite_identity_hash"]!.GetValue<string>());
        Assert.Equal(stableB.Candidate.TitleLineFeature!.FeatureHash, manifest["title_line_hash"]!.GetValue<string>());

        await session.StopAsync();
        var checkpointPath = Path.Combine(root, identity.SessionId, "checkpoint.json");
        var originalCheckpoint = await File.ReadAllBytesAsync(checkpointPath);
        var corrupt = JsonNode.Parse(await File.ReadAllTextAsync(checkpointPath))!;
        corrupt["observations"]![0]!["composite_identity_hash"] = null;
        await File.WriteAllTextAsync(checkpointPath, corrupt.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => checkpointStore.LoadAsync(identity.SessionId));
        await File.WriteAllBytesAsync(checkpointPath, originalCheckpoint);

        await using var resumed = new JacketObservationSession(
            new JacketObservationDetector(), checkpointStore, publisher, new FakeCatalogAdapter());
        var resume = await resumed.ResumeAsync(
            ResumeRequest(identity), "master.sqlite", "catalog.sqlite");
        Assert.True(resume.Compatible);
        Assert.True(resumed.RequiresCompositeIdentity);
    }

    [Fact]
    public async Task Current_catalog_identity_preflight_blocks_new_artifact_and_checkpoint()
    {
        var identity = Identity("session-catalog-existing");
        var catalog = new FakeCatalogAdapter();
        await using var session = new JacketObservationSession(
            new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
            new AtomicObservationCheckpointStore(root),
            new AtomicObservationArtifactPublisher(root),
            catalog);
        await session.StartAsync(
            identity,
            "master.sqlite",
            "catalog.sqlite",
            requireCompositeIdentity: true);
        var detector = StableInformationDetector();
        var first = CompositeFrame(40, 1, 1, 0);
        await session.ObserveFrameAsync(first, information: detector.Observe(first));
        var second = CompositeFrame(40, 1, 2, 100);
        var stable = await session.ObserveFrameAsync(second, information: detector.Observe(second));
        var compositeIdentity = stable.Candidate!.CompositeIdentity!;
        catalog.CompositeIdentities.Add(compositeIdentity);

        var preflight = await session.InspectLastStableSavePreflightAsync();
        var exception = await Assert.ThrowsAsync<ObservationAlreadyCatalogedException>(
            () => session.AdoptLastStableAsync());

        Assert.Equal(ObservationSavePreflightDisposition.CatalogExisting, preflight.Disposition);
        Assert.Equal(compositeIdentity.IdentityHash, exception.IdentityHash);
        Assert.Null(session.Checkpoint);
        Assert.False(Directory.Exists(Path.Combine(root, identity.SessionId)));
        Assert.Equal(0, catalog.CallCount);
        Assert.Equal(2, catalog.IdentitySetCallCount);
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
            dispatcher,
            StableInformationDetector());

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

        source.Write(CompositeFrame(20, 1, 1, 0));
        await initialChange.Task;
        source.Write(CompositeFrame(20, 1, 2, 100));
        await initialStable.Task;
        await viewModel.AdoptAsync();

        dispatcher.BlockNext();
        source.Write(CompositeFrame(20, 1, 3, 200));
        await dispatcher.Entered;
        source.Write(CompositeFrame(20, 1, 4, 300));
        source.Write(CompositeFrame(20, 1, 5, 400));
        source.Write(CompositeFrame(20, 1, 6, 500));
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
            dispatcher,
            StableInformationDetector());

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

        source.Write(CompositeFrame(20, 1, 1, 0));
        await initialChange.Task;
        source.Write(CompositeFrame(20, 1, 2, 100));
        await initialStable.Task;
        await viewModel.AdoptAsync();

        dispatcher.BlockNext();
        source.Write(CompositeFrame(20, 1, 3, 200));
        await dispatcher.Entered;
        source.Write(CompositeFrame(20, 1, 4, 300));
        source.Write(CompositeFrame(20, 1, 5, 400));
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
            new ImmediateCaptureDispatcher(),
            StableInformationDetector());
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

            source.Write(CompositeFrame(20, 1, 1, 0));
            await firstChange.Task;
            Assert.Equal("ジャケットを確認中", viewModel.CollectionStateTitle);
            source.Write(CompositeFrame(20, 1, 2, 100));
            var stableA = await firstStable.Task;
            Assert.Contains($"jacket={stableA.Candidate!.FeatureHash}", viewModel.StableCandidate);
            Assert.Equal("新しいジャケットを検出", viewModel.CollectionStateTitle);

            source.Write(CompositeFrame(20, 1, 3, 200));
            var duplicateResult = await duplicate.Task;
            Assert.Equal(stableA.Candidate.FeatureHash, duplicateResult.Candidate!.FeatureHash);
            Assert.Equal("新しいジャケットを検出", viewModel.CollectionStateTitle);
            var adoptedA = await viewModel.AdoptAsync();
            Assert.Equal(
                stableA.Candidate.FeatureHash,
                adoptedA.Checkpoint.Observations.Single(
                    observation => observation.ObservationId == adoptedA.ObservationId).FeatureHash);
            Assert.False(viewModel.CanAdopt);

            source.Write(CompositeFrame(80, 1, 4, 300));
            var changedDisplay = await changedFeature.Task;
            Assert.Contains($"jacket={stableA.Candidate.FeatureHash}", changedDisplay);
            Assert.Equal("ジャケットを確認中", viewModel.CollectionStateTitle);
            Assert.False(viewModel.CanAdopt);

            source.Write(CompositeFrame(80, 1, 5, 400));
            var stableB = await secondStable.Task;
            Assert.NotEqual(stableA.Candidate.FeatureHash, stableB.Candidate!.FeatureHash);
            Assert.Contains($"jacket={stableB.Candidate.FeatureHash}", viewModel.StableCandidate);
            Assert.True(viewModel.CanAdopt);

            var adopted = await viewModel.AdoptAsync();
            Assert.Equal(
                stableB.Candidate.FeatureHash,
                adopted.Checkpoint.Observations.Single(
                    observation => observation.ObservationId == adopted.ObservationId).FeatureHash);
            Assert.Equal("このジャケットは保存済み", viewModel.CollectionStateTitle);
            Assert.False(viewModel.CanAdopt);

            expectRevisitedSaved = true;
            source.Write(CompositeFrame(20, 1, 6, 500));
            await revisitChange.Task;
            source.Write(CompositeFrame(20, 1, 7, 600));
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
    public async Task Information_detection_updates_view_model_without_persisting_observation()
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
        await using var viewModel = new JacketObservationViewModel(
            capture,
            new JacketObservationSession(
                new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                checkpointStore,
                new AtomicObservationArtifactPublisher(root),
                new FakeCatalogAdapter()),
            new ImmediateCaptureDispatcher(),
            new InformationTitleLineDetector(
                new InformationTitleLineDetectorOptions(
                    StableFrameCount: 2,
                    MinimumStableDuration: TimeSpan.Zero)));
        var settling = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stable = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(viewModel.InformationTitleLineHash))
            {
                return;
            }
            if (viewModel.InformationDetection.State == InformationTitleLineState.Settling)
            {
                settling.TrySetResult();
            }
            else if (viewModel.InformationDetection.State == InformationTitleLineState.Stable)
            {
                stable.TrySetResult();
            }
        };

        await viewModel.StartSessionAsync(
            Master(), Catalog(), candidate, "master.sqlite", "catalog.sqlite");
        Assert.True(await coordinator.StartAsync(candidate));
        source.Write(InformationFrame(1, 1, 0));
        await settling.Task;
        source.Write(InformationFrame(1, 2, 100));
        await stable.Task;

        Assert.Equal("表示あり", viewModel.InformationPanelDisplay);
        Assert.Equal("安定", viewModel.InformationTitleLineStability);
        Assert.Equal(64, viewModel.InformationTitleLineHash.Length);
        Assert.False(Directory.Exists(Path.Combine(root, viewModel.SessionId)));

        await viewModel.StopAsync();
        Assert.Equal("表示なし", viewModel.InformationPanelDisplay);
        Assert.Equal("—", viewModel.InformationTitleLineHash);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task Explicit_session_opt_in_auto_saves_eligible_composite_candidate()
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
        await using var viewModel = new JacketObservationViewModel(
            capture,
            new JacketObservationSession(
                new JacketObservationDetector(new JacketDetectorOptions(0.01, 2, TimeSpan.Zero)),
                checkpointStore,
                new AtomicObservationArtifactPublisher(root),
                new FakeCatalogAdapter()),
            new ImmediateCaptureDispatcher(),
            StableInformationDetector());
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.StatusTitle)
                && viewModel.StatusTitle == "自動保存・catalog投入完了")
            {
                saved.TrySetResult();
            }
        };

        await viewModel.StartSessionAsync(
            Master(), Catalog(), candidate, "master.sqlite", "catalog.sqlite");
        Assert.False(viewModel.AutoSaveEnabled);
        viewModel.AutoSaveEnabled = true;
        Assert.True(await coordinator.StartAsync(candidate));
        source.Write(CompositeFrame(20, 1, 1, 0));
        source.Write(CompositeFrame(20, 1, 2, 100));
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var checkpoint = await checkpointStore.LoadAsync(viewModel.SessionId);
        Assert.Single(checkpoint.Observations);
        Assert.Equal("ingested", checkpoint.Observations[0].CatalogStatus);
        Assert.False(viewModel.CanAdopt);

        await coordinator.StopAsync();
        Assert.False(viewModel.AutoSaveEnabled);
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
        var resumedFrame1 = Frame(80, 5, 400);
        await resumed.ObserveFrameAsync(
            resumedFrame1,
            information: StableInformation(resumedFrame1));
        var resumedFrame2 = Frame(80, 6, 500);
        Assert.Equal(
            JacketDetectionState.DuplicatePreview,
            (await resumed.ObserveFrameAsync(
                resumedFrame2,
                information: StableInformation(resumedFrame2))).State);
        var adoptedAfterResume = await resumed.AdoptLastStableAsync();
        Assert.Equal(2, adoptedAfterResume.Checkpoint.Observations.Count);
        Assert.Equal(
            JacketObservationVersions.LegacySessionCheckpoint,
            adoptedAfterResume.Checkpoint.CheckpointVersion);
        var legacyManifest = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(adoptedAfterResume.Artifact.ArtifactPath, "observation.json")))!;
        Assert.Equal(
            JacketObservationVersions.LegacyObservationManifest,
            legacyManifest["manifest_version"]!.GetValue<string>());
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
    public async Task Python_adapter_uses_current_ingest_with_identity_and_artifact_hash()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(
                0,
                "{\"disposition\":\"created\",\"reference_id\":\"reference-current\","
                    + "\"review_status\":\"unresolved\",\"reason\":\"observation_unresolved\"}",
                "")));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter");
        var artifact = CurrentArtifact(identity, [1, 2], [3, 4]);

        await adapter.ValidateSessionAsync(identity, "catalog.sqlite", "master.sqlite");
        var receipt = await adapter.IngestAsync(
            artifact, "crop.png", "catalog.sqlite", "master.sqlite");

        Assert.Equal(CatalogIngestDisposition.Created, receipt.Disposition);
        Assert.Equal("reference-current", receipt.CatalogReferenceId);
        Assert.Equal(2, runner.Requests.Count);
        Assert.Contains("validate-session", runner.Requests[0].Arguments);
        Assert.Contains("--expected-catalog-created-at", runner.Requests[0].Arguments);
        Assert.Contains(identity.CatalogCreatedAt, runner.Requests[0].Arguments);
        Assert.Contains("ingest", runner.Requests[1].Arguments);
        Assert.DoesNotContain("ingest-v2", runner.Requests[1].Arguments);
        Assert.Contains("--observation-id", runner.Requests[1].Arguments);
        Assert.Contains(artifact.ObservationId, runner.Requests[1].Arguments);
        Assert.Contains("--expected-image-hash", runner.Requests[1].Arguments);
        Assert.Contains(artifact.JacketCropHash, runner.Requests[1].Arguments);
        Assert.Contains("--jacket-feature-hash", runner.Requests[1].Arguments);
        Assert.Contains("--title-line-hash", runner.Requests[1].Arguments);
        Assert.Contains("--composite-identity-hash", runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task Python_adapter_strictly_reads_current_catalog_identity_set()
    {
        var identity = Identity("session-identity-set");
        var composite = CompositeObservationIdentityBuilder.Create(
            JacketObservationVersions.FrameFeature,
            Hash(Encoding.UTF8.GetBytes("jacket")),
            JacketObservationVersions.InformationTitleLineFeature,
            Hash(Encoding.UTF8.GetBytes("title")));
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(
                0,
                JsonSerializer.Serialize(new
                {
                    identity_set_schema_version = "m5c-catalog-composite-identity-set-v1",
                    catalog_identity = identity.CatalogIdentity,
                    catalog_schema_version = identity.CatalogSchemaVersion,
                    catalog_created_at = identity.CatalogCreatedAt,
                    identities = new[]
                    {
                        new
                        {
                            identity_version = composite.IdentityVersion,
                            identity_hash = composite.IdentityHash,
                        },
                    },
                }),
                "")));
        var adapter = new PythonCatalogObservationAdapter(runner, root);

        var values = await adapter.LoadCompositeIdentitySetAsync(
            identity, "catalog.sqlite");

        Assert.Contains(composite, values);
        Assert.Single(runner.Requests);
        Assert.Contains("identity-set", runner.Requests[0].Arguments);
        Assert.Contains("--expected-catalog-created-at", runner.Requests[0].Arguments);
    }

    [Theory]
    [InlineData("observation artifact image hash does not match its checkpoint")]
    [InlineData("observation artifact image is empty")]
    [InlineData("observation artifact image is invalid")]
    [InlineData("source image does not exist: crop.png")]
    [InlineData("master source hash drift detected during observation ingest")]
    [InlineData("observation id was already used with different canonical payload")]
    [InlineData("observation composite identity is invalid")]
    public async Task Python_adapter_classifies_artifact_and_identity_rejections_as_drift(
        string standardError)
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(1, "", standardError)));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter-rejected");
        var artifact = CurrentArtifact(identity, [1, 2], [3, 4]);

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
        var identity = Identity("session-adapter-transient");
        var artifact = CurrentArtifact(identity, [1, 2], [3, 4]);

        var receipt = await adapter.IngestAsync(
            artifact, "crop.png", "catalog.sqlite", "master.sqlite");

        Assert.Equal(CatalogIngestDisposition.Failed, receipt.Disposition);
        Assert.Null(receipt.CatalogReferenceId);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task Python_adapter_rejects_unsupported_schema_without_running_python()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(0, "", "")));
        var adapter = new PythonCatalogObservationAdapter(runner, root);
        var identity = Identity("session-adapter-unsupported") with { CatalogSchemaVersion = 4 };
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

    private ObservationArtifact CurrentArtifact(
        ObservationSessionIdentity identity,
        byte[] source,
        byte[] crop)
    {
        var titleHash = Hash(Encoding.UTF8.GetBytes($"title:{identity.SessionId}"));
        return Artifact(identity, source, crop) with
        {
            TitleLineFeature = new TitleLineFeatureObservation(
                JacketObservationVersions.InformationTitleLineFeature,
                titleHash,
                1,
                DateTimeOffset.UtcNow,
                JacketObservationVersions.InformationDetector,
                JacketObservationVersions.InformationPanelRoi),
            CompositeIdentity = CompositeObservationIdentityBuilder.Create(
                JacketObservationVersions.FrameFeature,
                TestFeatureHash,
                JacketObservationVersions.InformationTitleLineFeature,
                titleHash),
        };
    }

    private static ObservationCheckpoint Checkpoint(
        ObservationSessionIdentity identity,
        ObservationArtifact artifact,
        string artifactPath) => new(
        JacketObservationVersions.LegacySessionCheckpoint,
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

    private static RawCaptureFrame InformationFrame(
        byte titleVariant,
        long sequence,
        long milliseconds) => new(
        EncodeInformationPng(titleVariant),
        1280,
        720,
        sequence,
        DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds));

    private static RawCaptureFrame CompositeFrame(
        byte jacketValue,
        byte titleVariant,
        long sequence,
        long milliseconds) => new(
        EncodeInformationPng(titleVariant, jacketValue),
        1280,
        720,
        sequence,
        DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds));

    private static InformationTitleLineDetector StableInformationDetector() => new(
        new InformationTitleLineDetectorOptions(
            StableFrameCount: 2,
            MinimumStableDuration: TimeSpan.Zero));

    private static InformationTitleLineDetectionResult StableInformation(RawCaptureFrame frame) => new(
        InformationTitleLineState.Stable,
        Hash(Encoding.UTF8.GetBytes("legacy-compatible-title")),
        3,
        TimeSpan.FromMilliseconds(100),
        "stable fixture",
        1,
        0,
        SourceSequence: frame.Sequence,
        CapturedAtUtc: frame.CapturedAtUtc);

    private static byte[] EncodeInformationPng(byte titleVariant, byte jacketValue = 0)
    {
        var pixels = new byte[1280 * 720 * 4];
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }
        PaintWhiteRectangle(pixels, 1280, 292, 39, 22, 8);
        PaintWhiteRectangle(pixels, 1280, 324, 39, 18, 8);
        var titleX = titleVariant == 1 ? 300 : 360;
        PaintWhiteRectangle(pixels, 1280, titleX, 69, 28, 10);
        PaintWhiteRectangle(pixels, 1280, titleX + 36, 69, 20, 10);
        PaintRectangle(pixels, 1280, 812, 28, 150, 150, jacketValue);
        var source = BitmapSource.Create(
            1280, 720, 96, 96, PixelFormats.Bgra32, null, pixels, 1280 * 4);
        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void PaintWhiteRectangle(
        byte[] pixels,
        int width,
        int x,
        int y,
        int rectangleWidth,
        int rectangleHeight)
    {
        for (var row = y; row < y + rectangleHeight; row++)
        {
            for (var column = x; column < x + rectangleWidth; column++)
            {
                var offset = (row * width + column) * 4;
                pixels[offset] = 255;
                pixels[offset + 1] = 255;
                pixels[offset + 2] = 255;
            }
        }
    }

    private static void PaintRectangle(
        byte[] pixels,
        int width,
        int x,
        int y,
        int rectangleWidth,
        int rectangleHeight,
        byte value)
    {
        for (var row = y; row < y + rectangleHeight; row++)
        {
            for (var column = x; column < x + rectangleWidth; column++)
            {
                var offset = (row * width + column) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }
    }

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
        public int IdentitySetCallCount { get; private set; }
        public int ValidationCallCount { get; private set; }
        public HashSet<CompositeObservationIdentity> CompositeIdentities { get; } = [];
        public Exception? ValidationFailure { get; init; }
        public Exception? IngestFailure { get; init; }
        public int FailValidationOnCall { get; init; } = 1;
        public int CancelIngestOnCall { get; set; }

        public Task<IReadOnlySet<CompositeObservationIdentity>> LoadCompositeIdentitySetAsync(
            ObservationSessionIdentity session,
            string catalogPath,
            CancellationToken cancellationToken = default)
        {
            IdentitySetCallCount++;
            return Task.FromResult<IReadOnlySet<CompositeObservationIdentity>>(
                new HashSet<CompositeObservationIdentity>(CompositeIdentities));
        }

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
