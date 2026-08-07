using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class CaptureSaveViewModelTests
{
    [Fact]
    public async Task Saved_capture_runs_workflow_once_and_reloads_only_saved_play()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new StubCaptureSaveWorkflowRunner((manifest, score, master) =>
        {
            Assert.Equal("session/frame_manifest.csv", manifest);
            Assert.Equal(fixture.ScorePath, score);
            Assert.Equal(fixture.MasterPath, master);
            fixture.AddPlay("capture-play", "2026-07-14T12:00:00+00:00", 999_500, 2_700);
            return Result("saved", "capture-play");
        });
        var pathStore = new MemoryViewerPathStore();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow,
            pathStore: pathStore);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(1, workflow.CallCount);
        Assert.Equal("1件のプレーを保存しました", viewModel.SaveStatusTitle);
        Assert.Equal("capture-play", Assert.Single(viewModel.Plays).PlayId);
        Assert.Equal(
            new ViewerPathSelection(fixture.ScorePath, fixture.MasterPath),
            pathStore.Selection);
    }

    [Fact]
    public async Task Master_revalidation_blocks_workflow_after_capture_when_selected_file_changes()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            throw new InvalidOperationException("workflow must not run"));
        var capture = new StubContinuousCaptureService(
            CaptureOperationStatus.Saved,
            () => fixture.ExecuteMasterSql("DROP TABLE charts;"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture,
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(0, workflow.CallCount);
        Assert.Equal(MasterDatabaseStatus.Incompatible, viewModel.MasterDatabaseStatus);
        Assert.Equal(MonitoringState.WorkflowFailed, viewModel.CurrentMonitoringState);
        Assert.Contains("解析・正式保存を開始しません", viewModel.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unresolved_events_are_not_reloaded_or_presented_as_success()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            new CaptureSaveWorkflowResult(
                "completed", 1, new Dictionary<string, int> { ["unresolved"] = 1 },
                [], ["formal_evidence.song_id_missing"], "data/run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal("保存できるプレーはありませんでした", viewModel.SaveStatusTitle);
        Assert.Contains("unresolved=1", viewModel.SaveStatusMessage);
        Assert.Contains("formal_evidence.song_id_missing", viewModel.SaveStatusMessage);
        Assert.Empty(viewModel.Plays);
    }

    [Fact]
    public async Task Unresolved_capture_event_notifies_once_and_next_saved_event_continues()
    {
        using var fixture = new DatabaseFixture();
        const string unresolvedEventId = "confirmed-event-v1:unresolved";
        var remainingResults = new Queue<CaptureSaveWorkflowResult>(
        [
            new CaptureSaveWorkflowResult(
                "completed",
                1,
                new Dictionary<string, int> { ["unresolved"] = 1 },
                [],
                ["digit_recognition.ambiguous"],
                null,
                [new CaptureSaveEventResult(
                    unresolvedEventId,
                    "unresolved",
                    ["digit_recognition.ambiguous"])]),
            new CaptureSaveWorkflowResult(
                "completed",
                1,
                new Dictionary<string, int> { ["saved"] = 1 },
                ["next-play"],
                [],
                null,
                [new CaptureSaveEventResult(
                    "confirmed-event-v1:saved",
                    "saved",
                    [])]),
        ]);
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
        {
            var result = remainingResults.Dequeue();
            if (result.SavedPlayIds.Count > 0)
            {
                fixture.AddPlay("next-play", "2026-07-14T12:00:00+00:00", 999_500, 2_700);
            }
            return result;
        });
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);
        var notifications = new List<UnresolvedCaptureNotification>();
        viewModel.UnresolvedCaptureNotificationRequested += notifications.Add;

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.True(viewModel.HasUnresolvedNotification);
        Assert.Contains("正式DBには保存されていません", viewModel.UnresolvedNotificationMessage);
        Assert.Contains(unresolvedEventId, viewModel.UnresolvedNotificationMessage);
        Assert.Single(notifications);
        Assert.Equal(["digit_recognition.ambiguous"], notifications[0].Reasons);
        Assert.Empty(viewModel.Plays);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(1, viewModel.MonitoringResults.Saved);
        Assert.Equal("next-play", Assert.Single(viewModel.Plays).PlayId);
        Assert.Single(notifications);
    }

    [Fact]
    public async Task Notification_setting_only_suppresses_local_display_and_keeps_diagnostic_boundary()
    {
        using var fixture = new DatabaseFixture();
        const string unresolvedEventId = "confirmed-event-v1:notification-off";
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            new CaptureSaveWorkflowResult(
                "completed",
                1,
                new Dictionary<string, int> { ["unresolved"] = 1 },
                [],
                ["digit_recognition.ambiguous"],
                null,
                [new CaptureSaveEventResult(
                    unresolvedEventId,
                    "unresolved",
                    ["digit_recognition.ambiguous"])]));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow,
            userSettingsStore: new MemoryUserSettingsStore(new UserSettings(
                StartMonitoringOnLaunch: true,
                NotifyUnresolvedResults: false,
                DefaultPlayStyle: UserSettings.SinglePlayStyle,
                StartupPage: UserSettings.HomeStartupPage)));
        viewModel.RestoreUserSettings();
        var notifications = new List<UnresolvedCaptureNotification>();
        var diagnostics = new List<UnresolvedCaptureNotification>();
        viewModel.UnresolvedCaptureNotificationRequested += notifications.Add;
        viewModel.UnresolvedCaptureDiagnosticRecorded += diagnostics.Add;

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.False(viewModel.HasUnresolvedNotification);
        Assert.Empty(notifications);
        Assert.Single(diagnostics);
        Assert.Equal(unresolvedEventId, diagnostics[0].EventId);
        Assert.Equal(1, viewModel.MonitoringResults.Unresolved);
        Assert.Empty(viewModel.Plays);
    }

    [Fact]
    public async Task Capture_failure_does_not_run_analysis_or_save_workflow()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            throw new InvalidOperationException("must not run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.WriteFailed),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(0, workflow.CallCount);
        Assert.Equal("session outputに失敗しました", viewModel.CaptureStatusTitle);
        Assert.False(viewModel.HasSaveStatus);
    }

    [Fact]
    public async Task Missing_master_does_not_start_capture_or_workflow()
    {
        var capture = new StubContinuousCaptureService(CaptureOperationStatus.Saved);
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            throw new InvalidOperationException("workflow must not run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture,
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123,
            "score.sqlite",
            "missing-master.sqlite");

        Assert.Equal(0, capture.CallCount);
        Assert.Equal(0, workflow.CallCount);
        Assert.Equal(MasterDatabaseStatus.Missing, viewModel.MasterDatabaseStatus);
        Assert.Equal(MonitoringState.WorkflowFailed, viewModel.CurrentMonitoringState);
        Assert.Contains("保存を開始しません", viewModel.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_jacket_catalog_does_not_start_capture_save_workflow()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            throw new InvalidOperationException("workflow must not run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123,
            fixture.ScorePath,
            fixture.MasterPath,
            Path.Combine(fixture.DirectoryPath, "missing-catalog.sqlite"));

        Assert.Equal(0, workflow.CallCount);
        Assert.Equal(MasterDatabaseStatus.Missing, viewModel.CatalogDatabaseStatus);
        Assert.Contains("解析・正式保存を開始しません", viewModel.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capture_save_does_not_start_while_manual_save_is_running()
    {
        using var fixture = new DatabaseFixture();
        var manualWorkflow = new BlockingManualWorkflowRunner();
        var captureService = new StubContinuousCaptureService(CaptureOperationStatus.Saved);
        var captureWorkflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            throw new InvalidOperationException("must not run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            manualWorkflow,
            continuousCaptureService: captureService,
            captureSaveWorkflowRunner: captureWorkflow);

        var manualSave = viewModel.SaveAndReloadAsync(
            "workflow.json", fixture.ScorePath, fixture.MasterPath);
        await manualWorkflow.Started.Task;

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.True(viewModel.IsSaving);
        Assert.Equal(0, captureService.CallCount);
        Assert.Equal(0, captureWorkflow.CallCount);

        manualWorkflow.Complete();
        await manualSave;
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task Capture_save_reserves_save_state_until_capture_and_workflow_finish()
    {
        using var fixture = new DatabaseFixture();
        var captureService = new BlockingContinuousCaptureService();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: captureService,
            captureSaveWorkflowRunner: new StubCaptureSaveWorkflowRunner((_, _, _) =>
                new CaptureSaveWorkflowResult(
                    "completed", 0, new Dictionary<string, int>(), [], [], "data/run")));

        var captureSave = viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);
        await captureService.Started.Task;

        Assert.True(viewModel.IsSaving);
        await viewModel.SaveAndReloadAsync(
            "workflow.json", fixture.ScorePath, fixture.MasterPath);

        captureService.Complete(CaptureOperationStatus.Cancelled);
        await captureSave;
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task Workflow_failure_is_surfaced_instead_of_no_saveable_plays()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            new CaptureSaveWorkflowResult(
                "workflow_failed", 1,
                new Dictionary<string, int> { ["db_rejected"] = 1 },
                [], ["frame_2:db_rejected:incompatible DB"], "data/run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal("保存workflowに失敗しました", viewModel.SaveStatusTitle);
        Assert.Contains("db_rejected=1", viewModel.SaveStatusMessage);
        Assert.Contains("incompatible DB", viewModel.SaveStatusMessage);
        Assert.Empty(viewModel.Plays);
    }

    [Fact]
    public async Task Partial_workflow_failure_still_reloads_committed_saved_play()
    {
        using var fixture = new DatabaseFixture();
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
        {
            fixture.AddPlay("partial-play", "2026-07-14T12:00:00+00:00", 999_500, 2_700);
            return new CaptureSaveWorkflowResult(
                "workflow_failed", 2,
                new Dictionary<string, int> { ["saved"] = 1, ["db_rejected"] = 1 },
                ["partial-play"], ["frame_3:db_rejected:write failed"], "data/run");
        });
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(
            "1件を保存し、一部の保存処理に失敗しました",
            viewModel.SaveStatusTitle);
        Assert.Equal("partial-play", Assert.Single(viewModel.Plays).PlayId);
        Assert.Contains("db_rejected=1", viewModel.SaveStatusMessage);
    }

    private static CaptureSaveWorkflowResult Result(string status, string playId) =>
        new(
            "completed", 1, new Dictionary<string, int> { [status] = 1 },
            [playId], [], "data/run");

    private sealed class StubContinuousCaptureService(
        CaptureOperationStatus status,
        Action? beforeResult = null)
        : IContinuousCaptureService
    {
        public int CallCount { get; private set; }
        public bool IsRunning => false;

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            beforeResult?.Invoke();
            return Task.FromResult(new CaptureSessionOperationResult(
                status,
                status == CaptureOperationStatus.Saved ? "saved" : "capture failed",
                status == CaptureOperationStatus.Saved
                    ? new CaptureSessionOutput(
                        "session", "session/frame_manifest.csv", "session/metadata.json", 3)
                    : null));
        }

        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class BlockingContinuousCaptureService : IContinuousCaptureService
    {
        private readonly TaskCompletionSource<CaptureSessionOperationResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning => !completion.Task.IsCompleted;

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return completion.Task;
        }

        public Task StopAsync() => Task.CompletedTask;

        public void Complete(CaptureOperationStatus status) =>
            completion.TrySetResult(new CaptureSessionOperationResult(
                status, "completed", null));
    }

    private sealed class StubCaptureSaveWorkflowRunner(
        Func<string, string, string, CaptureSaveWorkflowResult> run)
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
            return Task.FromResult(run(manifestPath, scoreDatabasePath, masterDatabasePath));
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

    private sealed class MemoryViewerPathStore : IViewerPathStore
    {
        public ViewerPathSelection? Selection { get; private set; }

        public ViewerPathSelection? Load() => Selection;

        public void Save(ViewerPathSelection selection) => Selection = selection;
    }

    private sealed class BlockingManualWorkflowRunner : IPersonalScoreDbWorkflowRunner
    {
        private readonly TaskCompletionSource<PersonalScoreDbWorkflowResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
}
