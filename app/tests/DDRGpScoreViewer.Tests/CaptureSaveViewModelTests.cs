using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
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
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, fixture.ScorePath, fixture.MasterPath);

        Assert.Equal(1, workflow.CallCount);
        Assert.Equal("1件のプレーを保存しました", viewModel.SaveStatusTitle);
        Assert.Equal("capture-play", Assert.Single(viewModel.Plays).PlayId);
    }

    [Fact]
    public async Task Unresolved_events_are_not_reloaded_or_presented_as_success()
    {
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            new CaptureSaveWorkflowResult(
                "completed", 1, new Dictionary<string, int> { ["unresolved"] = 1 },
                [], [], "data/run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.Saved),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, "not-created.sqlite", "not-read.sqlite");

        Assert.Equal("保存できるプレーはありませんでした", viewModel.SaveStatusTitle);
        Assert.Contains("unresolved=1", viewModel.SaveStatusMessage);
        Assert.Empty(viewModel.Plays);
    }

    [Fact]
    public async Task Capture_failure_does_not_run_analysis_or_save_workflow()
    {
        var workflow = new StubCaptureSaveWorkflowRunner((_, _, _) =>
            throw new InvalidOperationException("must not run"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new StubContinuousCaptureService(
                CaptureOperationStatus.WriteFailed),
            captureSaveWorkflowRunner: workflow);

        await viewModel.StartContinuousCaptureAndSaveAsync(123, "score.sqlite", "master.sqlite");

        Assert.Equal(0, workflow.CallCount);
        Assert.Equal("session outputに失敗しました", viewModel.CaptureStatusTitle);
        Assert.False(viewModel.HasSaveStatus);
    }

    [Fact]
    public async Task Capture_save_does_not_start_while_manual_save_is_running()
    {
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
            "workflow.json", "score.sqlite", "master.sqlite");
        await manualWorkflow.Started.Task;

        await viewModel.StartContinuousCaptureAndSaveAsync(
            123, "score.sqlite", "master.sqlite");

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
        var captureService = new BlockingContinuousCaptureService();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: captureService,
            captureSaveWorkflowRunner: new StubCaptureSaveWorkflowRunner((_, _, _) =>
                new CaptureSaveWorkflowResult(
                    "completed", 0, new Dictionary<string, int>(), [], [], "data/run")));

        var captureSave = viewModel.StartContinuousCaptureAndSaveAsync(
            123, "score.sqlite", "master.sqlite");
        await captureService.Started.Task;

        Assert.True(viewModel.IsSaving);
        await viewModel.SaveAndReloadAsync(
            "workflow.json", "score.sqlite", "master.sqlite");

        captureService.Complete(CaptureOperationStatus.Cancelled);
        await captureSave;
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task Workflow_failure_is_surfaced_instead_of_no_saveable_plays()
    {
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
            123, "rejected.sqlite", "master.sqlite");

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

    private sealed class StubContinuousCaptureService(CaptureOperationStatus status)
        : IContinuousCaptureService
    {
        public int CallCount { get; private set; }
        public bool IsRunning => false;

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
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
