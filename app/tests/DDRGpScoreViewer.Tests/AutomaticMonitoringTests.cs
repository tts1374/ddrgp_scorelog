using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class AutomaticMonitoringTests
{
    [Fact]
    public async Task Starts_after_two_consecutive_detections()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.StartAutomaticMonitoring(123);
        await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 1);
        await WaitForStateAsync(viewModel, MonitoringState.Monitoring);

        Assert.Equal(1, capture.TargetedRunCount);
        Assert.Equal(MonitoringState.Monitoring, viewModel.CurrentMonitoringState);
        Assert.Contains("DDR GRAND PRIX", viewModel.MonitoringTarget, StringComparison.Ordinal);

        await StopAutomaticMonitoringAsync(viewModel, capture);
    }

    [Fact]
    public async Task A_transient_detection_failure_keeps_polling_without_blocking_auto_start()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new TransientFailureWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.StartAutomaticMonitoring(123);
        await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 1);
        await WaitForStateAsync(viewModel, MonitoringState.Monitoring);

        Assert.Equal(1, capture.TargetedRunCount);
        Assert.NotEqual(MonitoringState.Blocked, viewModel.CurrentMonitoringState);

        await StopAutomaticMonitoringAsync(viewModel, capture);
    }

    [Fact]
    public async Task A_single_detection_miss_does_not_start_or_stop_monitoring()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(
            (IReadOnlyList<DdrGpWindowCandidate>)[Candidate(101)],
            (IReadOnlyList<DdrGpWindowCandidate>)[],
            (IReadOnlyList<DdrGpWindowCandidate>)[Candidate(101)],
            (IReadOnlyList<DdrGpWindowCandidate>)[]);
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.StartAutomaticMonitoring(123);
        await enumerator.WaitForCallsAsync(4);
        await Task.Delay(30);

        Assert.Equal(0, capture.TargetedRunCount);
        Assert.Equal(MonitoringState.WaitingForGame, viewModel.CurrentMonitoringState);

        viewModel.RequestApplicationExit();
        await viewModel.WaitForOperationsAsync();
    }

    [Fact]
    public async Task Manual_stop_prevents_automatic_reopening_in_the_same_app_session()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.StartAutomaticMonitoring(123);
        await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 1);
        await WaitForStateAsync(viewModel, MonitoringState.Monitoring);

        await viewModel.StopContinuousCaptureAsync();

        Assert.Equal(1, capture.StopCount);
        Assert.Equal(MonitoringState.ManuallyStopped, viewModel.CurrentMonitoringState);
        await Task.Delay(80);
        Assert.Equal(1, capture.TargetedRunCount);

        viewModel.RequestApplicationExit();
        await viewModel.WaitForOperationsAsync();
    }

    [Fact]
    public async Task Window_disappearance_is_debounced_then_monitoring_recovers_after_reappearance()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.StartAutomaticMonitoring(123);
        await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 1);
        await WaitForStateAsync(viewModel, MonitoringState.Monitoring);

        enumerator.SetCandidates([]);
        await capture.StopSignals[0].Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitForStateAsync(viewModel, MonitoringState.WaitingForGame);
        Assert.Equal(1, capture.TargetedRunCount);

        enumerator.SetCandidates([Candidate(101)]);
        await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 2);
        await WaitForStateAsync(viewModel, MonitoringState.Monitoring);
        Assert.Equal(2, capture.TargetedRunCount);

        await StopAutomaticMonitoringAsync(viewModel, capture);
    }

    [Fact]
    public async Task Fast_window_restart_during_automatic_stop_recovers_without_an_extra_gap()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService
        {
            DelayStop = true,
        };
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        try
        {
            viewModel.StartAutomaticMonitoring(123);
            await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 1);
            await WaitForStateAsync(viewModel, MonitoringState.Monitoring);

            enumerator.SetCandidates([]);
            await capture.StopStartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // The replacement window appears while the previous session is still stopping.
            enumerator.SetCandidates([Candidate(202)]);
            capture.ReleaseStop();

            await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 2);
            await WaitForStateAsync(viewModel, MonitoringState.Monitoring);
            Assert.Equal(2, capture.TargetedRunCount);
        }
        finally
        {
            capture.ReleaseStop();
            capture.DelayStop = false;
            if (viewModel.IsContinuousCapturing || viewModel.CanStopMonitoring)
            {
                await viewModel.StopContinuousCaptureAsync(manualStop: false);
            }
            viewModel.RequestApplicationExit();
            await viewModel.WaitForOperationsAsync();
        }
    }

    [Fact]
    public async Task Additional_matching_window_does_not_stop_active_monitoring_when_active_handle_remains()
    {
        using var fixture = new DatabaseFixture();
        var activeCandidate = Candidate(101);
        var enumerator = new MutableWindowEnumerator(activeCandidate);
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.StartAutomaticMonitoring(123);
        await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 1);
        await WaitForStateAsync(viewModel, MonitoringState.Monitoring);

        enumerator.SetCandidates([activeCandidate, Candidate(202)]);
        await Task.Delay(80);

        Assert.Equal(0, capture.StopCount);
        Assert.Equal(MonitoringState.Monitoring, viewModel.CurrentMonitoringState);

        await StopAutomaticMonitoringAsync(viewModel, capture);
    }

    [Fact]
    public async Task Database_failure_blocks_automatic_start()
    {
        using var fixture = new DatabaseFixture();
        var missingMasterPath = Path.Combine(fixture.DirectoryPath, "missing-master.sqlite");
        var paths = ConfiguredPaths(fixture, missingMasterPath);
        var enumerator = new MutableWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture, paths);

        viewModel.StartAutomaticMonitoring(123);
        await WaitForStateAsync(viewModel, MonitoringState.Blocked);

        Assert.Equal(0, capture.TargetedRunCount);
        Assert.Contains("自動監視を開始しません", viewModel.MonitoringReason, StringComparison.Ordinal);

        viewModel.RequestApplicationExit();
        await viewModel.WaitForOperationsAsync();
    }

    [Fact]
    public async Task Runtime_failure_blocks_automatic_retries()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService(CaptureOperationStatus.Failed);
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.StartAutomaticMonitoring(123);
        await WaitForStateAsync(viewModel, MonitoringState.Blocked);
        await Task.Delay(80);

        Assert.Equal(1, capture.TargetedRunCount);
        Assert.Equal(MonitoringState.Blocked, viewModel.CurrentMonitoringState);

        viewModel.RequestApplicationExit();
        await viewModel.WaitForOperationsAsync();
    }

    [Fact]
    public async Task Update_processing_blocks_automatic_start_until_update_finishes()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.SetReferenceDataUpdateInProgress(true);
        viewModel.StartAutomaticMonitoring(123);
        await WaitForStateAsync(viewModel, MonitoringState.Blocked);
        Assert.Equal(0, capture.TargetedRunCount);

        viewModel.SetReferenceDataUpdateInProgress(false);
        await WaitForTargetedRunAsync(viewModel, capture, expectedCount: 1);
        await WaitForStateAsync(viewModel, MonitoringState.Monitoring);
        Assert.Equal(1, capture.TargetedRunCount);

        await StopAutomaticMonitoringAsync(viewModel, capture);
    }

    [Fact]
    public async Task Application_exit_stops_the_automatic_monitoring_worker()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(Array.Empty<DdrGpWindowCandidate>());
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(fixture, enumerator, capture);

        viewModel.StartAutomaticMonitoring(123);
        await WaitForStateAsync(viewModel, MonitoringState.WaitingForGame);

        viewModel.RequestApplicationExit();
        await viewModel.WaitForOperationsAsync();

        Assert.Equal(MonitoringState.ShuttingDown, viewModel.CurrentMonitoringState);
        Assert.Equal(0, capture.TargetedRunCount);
    }

    [Fact]
    public async Task Startup_monitoring_setting_off_does_not_start_the_worker()
    {
        using var fixture = new DatabaseFixture();
        var enumerator = new MutableWindowEnumerator(Candidate(101));
        var capture = new BlockingTargetedCaptureService();
        var viewModel = CreateViewModel(
            fixture,
            enumerator,
            capture,
            userSettingsStore: new MemoryUserSettingsStore(new UserSettings(
                StartMonitoringOnLaunch: false,
                NotifyUnresolvedResults: true,
                DefaultPlayStyle: UserSettings.SinglePlayStyle,
                StartupPage: UserSettings.HomeStartupPage)));

        viewModel.RestoreUserSettings();
        viewModel.StartAutomaticMonitoring(123);
        await Task.Delay(80);

        Assert.False(viewModel.IsAutomaticMonitoringEnabled);
        Assert.True(viewModel.CanStartMonitoring);
        Assert.Equal(0, capture.TargetedRunCount);

        viewModel.RequestApplicationExit();
        await viewModel.WaitForOperationsAsync();
    }

    private static MainViewModel CreateViewModel(
        DatabaseFixture fixture,
        IDdrGpWindowEnumerator enumerator,
        BlockingTargetedCaptureService capture,
        ViewerDatabasePaths? paths = null,
        IUserSettingsStore? userSettingsStore = null) =>
        new(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture,
            defaultDatabasePaths: paths ?? ConfiguredPaths(fixture),
            ddrGpWindowEnumerator: enumerator,
            userSettingsStore: userSettingsStore,
            automaticMonitoringOptions: new AutomaticMonitoringOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(5),
                RequiredConsecutiveDetections = 2,
                RequiredConsecutiveMisses = 2,
            });

    private static ViewerDatabasePaths ConfiguredPaths(
        DatabaseFixture fixture,
        string? masterPath = null) =>
        new(
            ViewerDatabaseEnvironment.Development,
            fixture.DirectoryPath,
            masterPath ?? fixture.MasterPath,
            fixture.CatalogPath,
            fixture.ScorePath,
            null,
            fixture.DirectoryPath,
            Path.Combine(fixture.DirectoryPath, "logs"),
            Path.Combine(fixture.DirectoryPath, "viewer-paths.json"));

    private static DdrGpWindowCandidate Candidate(nint handle) =>
        new(handle, 42, "ddr-konaste", "DDR GRAND PRIX", 1280, 720);

    private static async Task StopAutomaticMonitoringAsync(
        MainViewModel viewModel,
        BlockingTargetedCaptureService capture)
    {
        await viewModel.StopContinuousCaptureAsync(manualStop: false);
        Assert.True(capture.StopCount > 0);
        viewModel.RequestApplicationExit();
        await viewModel.WaitForOperationsAsync();
    }

    private static async Task WaitForStateAsync(
        MainViewModel viewModel,
        MonitoringState expectedState)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (viewModel.CurrentMonitoringState == expectedState)
            {
                return;
            }
            await Task.Delay(10);
        }

        Assert.Equal(expectedState, viewModel.CurrentMonitoringState);
    }

    private static async Task WaitForTargetedRunAsync(
        MainViewModel viewModel,
        BlockingTargetedCaptureService capture,
        int expectedCount)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (capture.TargetedRunCount >= expectedCount)
            {
                return;
            }
            await Task.Delay(10);
        }

        Assert.True(
            capture.TargetedRunCount >= expectedCount,
            $"state={viewModel.CurrentMonitoringState}; reason={viewModel.MonitoringReason}; " +
            $"capture={capture.TargetedRunCount}");
    }

    private sealed class MutableWindowEnumerator(params IReadOnlyList<DdrGpWindowCandidate>[] sequence)
        : IDdrGpWindowEnumerator
    {
        private readonly object stateLock = new();
        private readonly Queue<IReadOnlyList<DdrGpWindowCandidate>> scripted = new(sequence);
        private IReadOnlyList<DdrGpWindowCandidate> current = [];
        private TaskCompletionSource? callsReached;
        private int callCount;

        public MutableWindowEnumerator(params DdrGpWindowCandidate[] candidates)
            : this((IReadOnlyList<DdrGpWindowCandidate>[])[candidates])
        {
            current = candidates;
        }

        public Task<IReadOnlyList<DdrGpWindowCandidate>> EnumerateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (stateLock)
            {
                callCount++;
                callsReached?.TrySetResult();
                callsReached = null;
                if (scripted.Count > 0)
                {
                    current = scripted.Dequeue();
                }
                return Task.FromResult(current);
            }
        }

        public void SetCandidates(IReadOnlyList<DdrGpWindowCandidate> candidates)
        {
            lock (stateLock)
            {
                scripted.Clear();
                current = candidates;
            }
        }

        public Task WaitForCallsAsync(int expectedCount)
        {
            lock (stateLock)
            {
                if (callCount >= expectedCount)
                {
                    return Task.CompletedTask;
                }
                callsReached = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return callsReached.Task;
            }
        }
    }

    private sealed class TransientFailureWindowEnumerator(DdrGpWindowCandidate candidate)
        : IDdrGpWindowEnumerator
    {
        private int callCount;

        public Task<IReadOnlyList<DdrGpWindowCandidate>> EnumerateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref callCount) == 1)
            {
                throw new InvalidOperationException("fixture transient detection failure");
            }

            return Task.FromResult<IReadOnlyList<DdrGpWindowCandidate>>([candidate]);
        }
    }

    private sealed class BlockingTargetedCaptureService(
        params CaptureOperationStatus[] statuses) :
        IContinuousCaptureService,
        ITargetedMonitoringContinuousCaptureService
    {
        private readonly Queue<CaptureOperationStatus> remaining = new(statuses);
        private TaskCompletionSource<CaptureSessionOperationResult>? completion;

        public List<TaskCompletionSource> StartedSignals { get; } = [];
        public List<TaskCompletionSource> StopSignals { get; } = [];
        public int TargetedRunCount { get; private set; }
        public int StopCount { get; private set; }
        public bool DelayStop { get; set; }
        public TaskCompletionSource StopStartedSignal { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource StopReleaseSignal { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning => completion is { Task.IsCompleted: false };

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(CaptureOperationStatus.Cancelled));

        public Task<CaptureSessionOperationResult> RunAsync(
            nint targetWindowHandle,
            CaptureTargetInfo target,
            IProgress<CaptureSessionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            TargetedRunCount++;
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            StartedSignals.Add(started);
            StopSignals.Add(stopped);
            progress.Report(new CaptureSessionProgress(
                target,
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
            started.TrySetResult();

            var status = remaining.Count == 0
                ? (CaptureOperationStatus?)null
                : remaining.Dequeue();
            if (status is not null)
            {
                return Task.FromResult(Result(status.Value));
            }

            completion = new TaskCompletionSource<CaptureSessionOperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return completion.Task;
        }

        public async Task StopAsync()
        {
            StopCount++;
            if (StopSignals.Count > 0)
            {
                StopSignals[^1].TrySetResult();
            }
            if (DelayStop)
            {
                StopStartedSignal.TrySetResult();
                await StopReleaseSignal.Task;
            }
            completion?.TrySetResult(Result(CaptureOperationStatus.Cancelled));
        }

        public void ReleaseStop() => StopReleaseSignal.TrySetResult();

        private static CaptureSessionOperationResult Result(CaptureOperationStatus status) =>
            new(status, $"fixture {status}");
    }

    private sealed class UnusedManualWorkflowRunner : IPersonalScoreDbWorkflowRunner
    {
        public Task<PersonalScoreDbWorkflowResult> RunAsync(
            string workflowInputPath,
            string scoreDatabasePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
