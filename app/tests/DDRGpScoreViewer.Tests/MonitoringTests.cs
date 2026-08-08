using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Tray;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class MonitoringTests
{
    [Fact]
    public async Task Monitoring_progress_and_all_workflow_outcomes_are_projected()
    {
        using var fixture = new DatabaseFixture();
        var capture = new MonitoringCaptureService(CaptureOperationStatus.Saved);
        var workflow = new CaptureWorkflowRunner(new CaptureSaveWorkflowResult(
            "completed",
            21,
            new Dictionary<string, int>
            {
                ["saved"] = 1,
                ["duplicate"] = 2,
                ["excluded"] = 3,
                ["policy_excluded"] = 7,
                ["unresolved"] = 4,
                ["analysis_failed"] = 5,
                ["db_rejected"] = 6,
            },
            [],
            ["fixture reason"],
            "data/run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture,
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123,
            fixture.ScorePath,
            fixture.MasterPath);

        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
        Assert.Equal("DDR GRAND PRIX", viewModel.MonitoringTarget);
        Assert.Equal("1280 x 720", viewModel.MonitoringTargetSize);
        Assert.Equal(12, viewModel.MonitoringFrameCount);
        Assert.Equal(1, viewModel.MonitoringResults.Saved);
        Assert.Equal(2, viewModel.MonitoringResults.Duplicate);
        Assert.Equal(10, viewModel.MonitoringResults.Excluded);
        Assert.Equal(4, viewModel.MonitoringResults.Unresolved);
        Assert.Equal(5, viewModel.MonitoringResults.AnalysisFailed);
        Assert.Equal(6, viewModel.MonitoringResults.DbRejected);
        Assert.Equal(0, viewModel.MonitoringResults.WorkflowFailed);
        Assert.Equal("fixture reason", viewModel.MonitoringReason);
        Assert.False(viewModel.CanStopMonitoring);
    }

    [Theory]
    [InlineData(CaptureOperationStatus.Cancelled, MonitoringState.Stopped)]
    [InlineData(CaptureOperationStatus.TargetClosed, MonitoringState.TargetClosed)]
    [InlineData(CaptureOperationStatus.Resized, MonitoringState.Resized)]
    [InlineData(CaptureOperationStatus.DeviceLost, MonitoringState.DeviceLost)]
    [InlineData(CaptureOperationStatus.WriteFailed, MonitoringState.CaptureFailed)]
    [InlineData(CaptureOperationStatus.Failed, MonitoringState.CaptureFailed)]
    public async Task Capture_completion_keeps_distinct_monitoring_states(
        CaptureOperationStatus captureStatus,
        MonitoringState expectedState)
    {
        using var fixture = new DatabaseFixture();
        var workflow = new ThrowingCaptureWorkflowRunner();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new MonitoringCaptureService(captureStatus),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123,
            fixture.ScorePath,
            fixture.MasterPath);

        Assert.Equal(expectedState, viewModel.CurrentMonitoringState);
        Assert.Equal(0, workflow.CallCount);
    }

    [Fact]
    public async Task Workflow_failure_preserves_committed_counts_and_reason()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new CaptureWorkflowRunner(new CaptureSaveWorkflowResult(
            "workflow_failed",
            2,
            new Dictionary<string, int> { ["saved"] = 1, ["db_rejected"] = 1 },
            [],
            ["frame_2:db_rejected"],
            "data/run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new MonitoringCaptureService(CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(MonitoringState.WorkflowFailed, viewModel.CurrentMonitoringState);
        Assert.Equal(1, viewModel.MonitoringResults.Saved);
        Assert.Equal(1, viewModel.MonitoringResults.DbRejected);
        Assert.Equal(1, viewModel.MonitoringResults.WorkflowFailed);
        Assert.Contains("db_rejected", viewModel.MonitoringReason);
    }

    [Fact]
    public async Task Analysis_process_failure_is_counted_and_stops_monitoring()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new CaptureWorkflowRunner(new CaptureSaveWorkflowResult(
            "analysis_failed",
            0,
            new Dictionary<string, int>(),
            [],
            ["vision process failed"],
            "data/run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new MonitoringCaptureService(CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(MonitoringState.WorkflowFailed, viewModel.CurrentMonitoringState);
        Assert.Equal(1, viewModel.MonitoringResults.AnalysisFailed);
        Assert.Equal(1, viewModel.MonitoringResults.WorkflowFailed);
        Assert.Contains("vision process failed", viewModel.MonitoringReason);
    }

#if DEBUG
    [Fact]
    public async Task Monitoring_can_be_explicitly_restarted_after_target_closed()
    {
        var capture = new SequenceMonitoringCaptureService(
            CaptureOperationStatus.TargetClosed,
            CaptureOperationStatus.Cancelled);
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture);

        await viewModel.StartContinuousCaptureAsync(123);
        Assert.Equal(MonitoringState.TargetClosed, viewModel.CurrentMonitoringState);

        await viewModel.StartContinuousCaptureAsync(123);

        Assert.Equal(2, capture.RunCount);
        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
        Assert.True(viewModel.CanStartMonitoring);
    }

    [Fact]
    public async Task Late_progress_after_capture_completion_cannot_reopen_monitoring()
    {
        var capture = new LateProgressCaptureService();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture);

        await viewModel.StartContinuousCaptureAsync(123);
        var frameCountAfterStop = viewModel.MonitoringFrameCount;

        capture.ReportLateProgress();

        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
        Assert.Equal(frameCountAfterStop, viewModel.MonitoringFrameCount);
    }
#endif

    [Fact]
    public async Task Stop_after_capture_finalizes_saved_manifest_and_runs_save_workflow_once()
    {
        using var fixture = new DatabaseFixture();
        var capture = new StopReturnsSavedCaptureService();
        var workflow = new CaptureWorkflowRunner(new CaptureSaveWorkflowResult(
            "completed", 1, new Dictionary<string, int> { ["saved"] = 1 }, [], [], "data/run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture,
            captureSaveWorkflowRunner: workflow);

        var startTask = viewModel.StartContinuousCaptureAndSaveAsync(
            123,
            fixture.ScorePath,
            fixture.MasterPath);
        await capture.Started.Task;

        await viewModel.StopContinuousCaptureAsync();
        await startTask;

        Assert.Equal(1, capture.StopCount);
        Assert.Equal(1, workflow.CallCount);
        Assert.Equal(MonitoringState.ManuallyStopped, viewModel.CurrentMonitoringState);
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task Stop_during_capture_save_workflow_waits_without_cancelling_or_restarting_it()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new BlockingCaptureWorkflowRunner();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new MonitoringCaptureService(CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        var startTask = viewModel.StartContinuousCaptureAndSaveAsync(
            123,
            fixture.ScorePath,
            fixture.MasterPath);
        await workflow.Started.Task;

        var stopTask = viewModel.StopContinuousCaptureAsync();
        Assert.False(stopTask.IsCompleted);

        workflow.Complete();
        await stopTask;
        await startTask;

        Assert.Equal(1, workflow.CallCount);
        Assert.False(workflow.CancellationToken.IsCancellationRequested);
        Assert.Equal(MonitoringState.ManuallyStopped, viewModel.CurrentMonitoringState);
        Assert.Equal(0, viewModel.MonitoringResults.Saved);
    }

    [Fact]
    public async Task Application_exit_still_cancels_in_flight_capture_save_workflow()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new BlockingCaptureWorkflowRunner();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new MonitoringCaptureService(CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        var startTask = viewModel.StartContinuousCaptureAndSaveAsync(
            123,
            fixture.ScorePath,
            fixture.MasterPath);
        await workflow.Started.Task;

        viewModel.RequestApplicationExit();
        await startTask;

        Assert.Equal(1, workflow.CallCount);
        Assert.True(workflow.CancellationToken.IsCancellationRequested);
        Assert.True(viewModel.IsApplicationExitRequested);
    }

    [Theory]
    [InlineData(MonitoringState.Idle, true, false)]
    [InlineData(MonitoringState.Starting, false, true)]
    [InlineData(MonitoringState.WaitingForGame, true, false)]
    [InlineData(MonitoringState.SelectingTarget, false, true)]
    [InlineData(MonitoringState.Monitoring, false, true)]
    [InlineData(MonitoringState.Stopping, false, false)]
    [InlineData(MonitoringState.Stopped, true, false)]
    [InlineData(MonitoringState.ManuallyStopped, true, false)]
    [InlineData(MonitoringState.Blocked, true, false)]
    [InlineData(MonitoringState.ShuttingDown, false, false)]
    [InlineData(MonitoringState.TargetClosed, true, false)]
    public void Tray_menu_state_follows_monitoring_state(
        MonitoringState state,
        bool canStart,
        bool canStop)
    {
        Assert.Equal(new TrayMenuState(canStart, canStop), TrayMenuState.FromMonitoringState(state));
    }

    [Fact]
    public void Window_close_and_minimize_hide_until_explicit_application_exit()
    {
        Assert.True(WindowLifecyclePolicy.HideOnClose(applicationExitRequested: false));
        Assert.False(WindowLifecyclePolicy.HideOnMinimize(applicationExitRequested: false));
        Assert.False(WindowLifecyclePolicy.HideOnClose(applicationExitRequested: true));
        Assert.False(WindowLifecyclePolicy.HideOnMinimize(applicationExitRequested: true));
    }

    [Fact]
    public async Task Tray_exit_waits_for_stop_and_disposes_once()
    {
        var tray = new FakeTrayIcon();
        var stopCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopCount = 0;
        var shutdownCount = 0;
        using var lifecycle = new ApplicationLifecycleCoordinator(
            tray,
            () => Task.CompletedTask,
            async () =>
            {
                stopCount++;
                await stopCompletion.Task;
            },
            () => { },
            () => shutdownCount++);

        var firstExit = lifecycle.ExitAsync();
        var repeatedExit = lifecycle.ExitAsync();

        Assert.Same(firstExit, repeatedExit);
        Assert.False(firstExit.IsCompleted);
        Assert.Equal(1, stopCount);
        Assert.Equal(0, tray.DisposeCount);
        Assert.Equal(0, shutdownCount);

        stopCompletion.SetResult();
        await firstExit;

        Assert.Equal(1, tray.DisposeCount);
        Assert.Equal(1, shutdownCount);
    }

    [Fact]
    public async Task Application_update_preparation_waits_for_stop_before_final_exit()
    {
        var tray = new FakeTrayIcon();
        var stopCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopCount = 0;
        var beginExitCount = 0;
        var shutdownCount = 0;
        using var lifecycle = new ApplicationLifecycleCoordinator(
            tray,
            () => Task.CompletedTask,
            async () =>
            {
                stopCount++;
                await stopCompletion.Task;
            },
            () => { },
            () => shutdownCount++,
            () => beginExitCount++);

        var preparation = lifecycle.PrepareForApplicationUpdateAsync();

        Assert.False(preparation.IsCompleted);
        Assert.Equal(1, beginExitCount);
        Assert.Equal(1, stopCount);
        Assert.Equal(0, shutdownCount);
        Assert.Equal(0, tray.DisposeCount);

        stopCompletion.SetResult();
        await preparation;
        await lifecycle.ExitAsync();

        Assert.Equal(1, beginExitCount);
        Assert.Equal(1, stopCount);
        Assert.Equal(1, shutdownCount);
        Assert.Equal(1, tray.DisposeCount);
    }

    [Fact]
    public async Task Application_update_preparation_failure_completes_full_exit()
    {
        var tray = new FakeTrayIcon();
        var shutdownCount = 0;
        using var lifecycle = new ApplicationLifecycleCoordinator(
            tray,
            () => Task.CompletedTask,
            () => throw new InvalidOperationException("prepare stop failed"),
            () => { },
            () => shutdownCount++);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.PrepareForApplicationUpdateAsync());

        Assert.Equal(1, tray.DisposeCount);
        Assert.Equal(1, shutdownCount);
    }

    [Fact]
    public async Task Operation_gate_deduplicates_start_and_exposes_cancellation_to_picker_flow()
    {
        using var gate = new AsyncOperationGate();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = false;
        var runCount = 0;

        var first = gate.RunAsync(async cancellationToken =>
        {
            runCount++;
            started.SetResult();
            await release.Task;
            cancellationObserved = cancellationToken.IsCancellationRequested;
        });
        await started.Task;
        var repeated = gate.RunAsync(_ => throw new InvalidOperationException("must not run"));

        Assert.Same(first, repeated);
        Assert.Equal(1, runCount);

        gate.Cancel();
        release.SetResult();
        await gate.WaitAsync();
        await gate.RunAsync(_ =>
        {
            runCount++;
            return Task.CompletedTask;
        });

        Assert.True(cancellationObserved);
        Assert.Equal(2, runCount);
    }

    [Fact]
    public async Task Tray_exit_disposes_and_shuts_down_when_stop_throws()
    {
        var tray = new FakeTrayIcon();
        var shutdownCount = 0;
        using var lifecycle = new ApplicationLifecycleCoordinator(
            tray,
            () => Task.CompletedTask,
            () => throw new InvalidOperationException("stop failed"),
            () => { },
            () => shutdownCount++);

        await lifecycle.ExitAsync();

        Assert.Equal(1, tray.DisposeCount);
        Assert.Equal(1, shutdownCount);
        var notification = Assert.Single(tray.Notifications);
        Assert.Equal(TrayNotificationKind.Error, notification.Kind);
        Assert.Contains("stop failed", notification.Message);
    }

#if DEBUG
    [Fact]
    public async Task Stop_failure_returns_to_retryable_capture_state()
    {
        var capture = new RetryableStopCaptureService();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture);

        var runTask = viewModel.StartContinuousCaptureAsync(123);
        await capture.Started.Task;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.StopContinuousCaptureAsync());

        Assert.True(viewModel.IsContinuousCapturing);
        Assert.False(viewModel.IsStoppingCapture);
        Assert.Equal(MonitoringState.CaptureFailed, viewModel.CurrentMonitoringState);
        Assert.True(viewModel.CanStopMonitoring);

        await viewModel.StopContinuousCaptureAsync();
        await runTask;

        Assert.Equal(2, capture.StopCount);
        Assert.False(viewModel.IsContinuousCapturing);
        Assert.Equal(MonitoringState.ManuallyStopped, viewModel.CurrentMonitoringState);
    }
#endif

    [Fact]
    public async Task Tray_exit_blocks_new_start_before_stop_completes()
    {
        var stopCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startCount = 0;
        var exitRequested = false;
        var shutdownCount = 0;
        using var lifecycle = new ApplicationLifecycleCoordinator(
            new FakeTrayIcon(),
            () =>
            {
                startCount++;
                return Task.CompletedTask;
            },
            async () => await stopCompletion.Task,
            () => { },
            () => shutdownCount++,
            () => exitRequested = true);

        var exitTask = lifecycle.ExitAsync();

        Assert.True(exitRequested);
        await lifecycle.StartAsync();
        Assert.Equal(0, startCount);
        Assert.False(exitTask.IsCompleted);

        stopCompletion.SetResult();
        await exitTask;

        Assert.Equal(1, shutdownCount);
    }

#if DEBUG
    [Fact]
    public async Task View_model_rejects_new_capture_after_exit_is_requested()
    {
        var capture = new SequenceMonitoringCaptureService(CaptureOperationStatus.Saved);
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture);

        viewModel.RequestApplicationExit();
        await viewModel.StartContinuousCaptureAsync(123);

        Assert.Equal(0, capture.RunCount);
        Assert.False(viewModel.CanStartMonitoring);
    }

    [Fact]
    public async Task Exit_waits_for_in_flight_manual_workflow()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new BlockingManualWorkflowRunner();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            workflow,
            continuousCaptureService: new SequenceMonitoringCaptureService(
                CaptureOperationStatus.Cancelled));

        var saveTask = viewModel.SaveAndReloadAsync(
            "input.json",
            fixture.ScorePath,
            fixture.MasterPath);
        await workflow.Started.Task;

        var waitTask = viewModel.WaitForOperationsAsync();
        Assert.False(waitTask.IsCompleted);

        workflow.Complete();
        await saveTask;
        await waitTask;

        Assert.False(viewModel.IsSaving);
    }
#endif

    [Fact]
    public async Task Tray_commands_forward_start_stop_and_show()
    {
        var tray = new FakeTrayIcon();
        var startCount = 0;
        var stopCount = 0;
        var showCount = 0;
        using var lifecycle = new ApplicationLifecycleCoordinator(
            tray,
            () =>
            {
                startCount++;
                return Task.CompletedTask;
            },
            () =>
            {
                stopCount++;
                return Task.CompletedTask;
            },
            () => showCount++,
            () => { });

        await lifecycle.StartAsync();
        await lifecycle.StopAsync();
        lifecycle.Show();

        Assert.Equal(1, startCount);
        Assert.Equal(1, stopCount);
        Assert.Equal(1, showCount);
    }

    [Fact]
    public void Tray_notifications_ignore_duplicate_only_and_report_saved_or_fatal_stop()
    {
        var tray = new FakeTrayIcon();
        using var lifecycle = new ApplicationLifecycleCoordinator(
            tray,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => { },
            () => { });
        var duplicateOnly = MonitoringResultSummary.FromWorkflow(
            new Dictionary<string, int> { ["duplicate"] = 5 },
            false,
            DateTimeOffset.UtcNow,
            []);

        lifecycle.UpdateMonitoringState(MonitoringState.Stopped, "停止済み", duplicateOnly, "—");
        lifecycle.UpdateMonitoringState(
            MonitoringState.Monitoring,
            "監視中",
            duplicateOnly,
            "running");
        lifecycle.UpdateMonitoringState(
            MonitoringState.Stopped,
            "停止済み",
            duplicateOnly with { Saved = 1 },
            "saved");
        lifecycle.UpdateMonitoringState(
            MonitoringState.DeviceLost,
            "device lost",
            duplicateOnly,
            "GPU lost");

        Assert.Equal(2, tray.Notifications.Count);
        Assert.Equal(TrayNotificationKind.Information, tray.Notifications[0].Kind);
        Assert.Equal(TrayNotificationKind.Error, tray.Notifications[1].Kind);
    }

    [Fact]
    public void Tray_unresolved_capture_notification_is_deduplicated_by_event()
    {
        var tray = new FakeTrayIcon();
        using var lifecycle = new ApplicationLifecycleCoordinator(
            tray,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => { },
            () => { });
        var notification = new UnresolvedCaptureNotification(
            "confirmed-event-v1:unresolved",
            "正式DBには保存されていません。理由: digit_recognition.ambiguous");

        lifecycle.NotifyUnresolvedCapture(notification);
        lifecycle.NotifyUnresolvedCapture(notification);
        lifecycle.NotifyUnresolvedCapture(notification with
        {
            EventId = "confirmed-event-v1:next",
        });

        Assert.Equal(2, tray.Notifications.Count);
        Assert.All(
            tray.Notifications,
            item => Assert.Equal(TrayNotificationKind.Information, item.Kind));
        Assert.Contains("正式DBには保存されていません", tray.Notifications[0].Message);
    }

    private sealed class MonitoringCaptureService(CaptureOperationStatus status)
        : IMonitoringContinuousCaptureService
    {
        public bool IsRunning => false;

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            RunCoreAsync(null);

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            IProgress<CaptureSessionProgress> progress,
            CancellationToken cancellationToken = default) =>
            RunCoreAsync(progress);

        public Task StopAsync() => Task.CompletedTask;

        private Task<CaptureSessionOperationResult> RunCoreAsync(
            IProgress<CaptureSessionProgress>? progress)
        {
            var now = DateTimeOffset.UtcNow;
            progress?.Report(new CaptureSessionProgress(
                new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
                12,
                now.AddMinutes(-1),
                now));
            return Task.FromResult(new CaptureSessionOperationResult(
                status,
                $"fixture {status}",
                status == CaptureOperationStatus.Saved
                    ? new CaptureSessionOutput(
                        "session",
                        "session/frame_manifest.csv",
                        "session/metadata.json",
                        12)
                    : null));
        }
    }

    private sealed class CaptureWorkflowRunner(CaptureSaveWorkflowResult result)
        : ICaptureSaveWorkflowRunner
    {
        public int CallCount { get; private set; }

        public Task<CaptureSaveWorkflowResult> RunAsync(
            string manifestPath,
            string scoreDatabasePath,
            string masterDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingCaptureWorkflowRunner : ICaptureSaveWorkflowRunner
    {
        private readonly TaskCompletionSource<CaptureSaveWorkflowResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public async Task<CaptureSaveWorkflowResult> RunAsync(
            string manifestPath,
            string scoreDatabasePath,
            string masterDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CancellationToken = cancellationToken;
            Started.TrySetResult();
            return await completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete() => completion.TrySetResult(new CaptureSaveWorkflowResult(
            "completed", 0, new Dictionary<string, int>(), [], [], null));
    }

    private sealed class SequenceMonitoringCaptureService(
        params CaptureOperationStatus[] statuses) : IContinuousCaptureService
    {
        private readonly Queue<CaptureOperationStatus> remaining = new(statuses);

        public bool IsRunning => false;
        public int RunCount { get; private set; }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            var status = remaining.Dequeue();
            return Task.FromResult(new CaptureSessionOperationResult(status, $"fixture {status}"));
        }

        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class LateProgressCaptureService : IMonitoringContinuousCaptureService
    {
        private IProgress<CaptureSessionProgress>? progress;

        public bool IsRunning => false;

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaptureSessionOperationResult(
                CaptureOperationStatus.Cancelled,
                "fixture stopped"));

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            IProgress<CaptureSessionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            this.progress = progress;
            progress.Report(new CaptureSessionProgress(
                new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
                1,
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow));
            return Task.FromResult(new CaptureSessionOperationResult(
                CaptureOperationStatus.Cancelled,
                "fixture stopped"));
        }

        public void ReportLateProgress() => progress?.Report(new CaptureSessionProgress(
            new CaptureTargetInfo("stale target", 640, 480),
            99,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class StopReturnsSavedCaptureService : IMonitoringContinuousCaptureService
    {
        private readonly TaskCompletionSource<CaptureSessionOperationResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRunning => !completion.Task.IsCompleted;
        public int StopCount { get; private set; }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return completion.Task;
        }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            IProgress<CaptureSessionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return completion.Task;
        }

        public Task StopAsync()
        {
            StopCount++;
            completion.TrySetResult(new CaptureSessionOperationResult(
                CaptureOperationStatus.Saved,
                "fixture saved",
                new CaptureSessionOutput(
                    "session",
                    "session/frame_manifest.csv",
                    "session/metadata.json",
                    1)));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCaptureWorkflowRunner : ICaptureSaveWorkflowRunner
    {
        public int CallCount { get; private set; }

        public Task<CaptureSaveWorkflowResult> RunAsync(
            string manifestPath,
            string scoreDatabasePath,
            string masterDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("must not run");
        }
    }

    private sealed class RetryableStopCaptureService : IMonitoringContinuousCaptureService
    {
        private readonly TaskCompletionSource<CaptureSessionOperationResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRunning => true;
        public int StopCount { get; private set; }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return completion.Task;
        }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            IProgress<CaptureSessionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return completion.Task;
        }

        public Task StopAsync()
        {
            StopCount++;
            if (StopCount == 1)
            {
                throw new InvalidOperationException("stop failed");
            }
            completion.TrySetResult(new CaptureSessionOperationResult(
                CaptureOperationStatus.Cancelled,
                "fixture stopped"));
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedManualWorkflowRunner : IPersonalScoreDbWorkflowRunner
    {
        public Task<PersonalScoreDbWorkflowResult> RunAsync(
            string workflowInputPath,
            string scoreDatabasePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingManualWorkflowRunner : IPersonalScoreDbWorkflowRunner
    {
        private readonly TaskCompletionSource<PersonalScoreDbWorkflowResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PersonalScoreDbWorkflowResult> RunAsync(
            string workflowInputPath,
            string scoreDatabasePath,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return completion.Task;
        }

        public void Complete() => completion.TrySetResult(new PersonalScoreDbWorkflowResult(
            "excluded",
            "not_requested",
            "excluded",
            "written",
            true,
            "capture-manual",
            "analysis-manual",
            null,
            ["fixture_excluded"],
            null,
            "score.sqlite"));
    }

    private sealed class FakeTrayIcon : ITrayIconService
    {
        public event EventHandler? StartRequested { add { } remove { } }
        public event EventHandler? StopRequested { add { } remove { } }
        public event EventHandler? ShowRequested { add { } remove { } }
        public event EventHandler? ExitRequested { add { } remove { } }

        public int DisposeCount { get; private set; }
        public List<(string Title, string Message, TrayNotificationKind Kind)> Notifications { get; } = [];

        public void UpdateMenu(TrayMenuState state, string statusText)
        {
        }

        public void ShowNotification(string title, string message, TrayNotificationKind kind) =>
            Notifications.Add((title, message, kind));

        public void Dispose() => DisposeCount++;
    }
}
