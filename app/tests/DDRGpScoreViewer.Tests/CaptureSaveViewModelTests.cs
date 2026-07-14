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

    private static CaptureSaveWorkflowResult Result(string status, string playId) =>
        new(
            "completed", 1, new Dictionary<string, int> { [status] = 1 },
            [playId], [], "data/run");

    private sealed class StubContinuousCaptureService(CaptureOperationStatus status)
        : IContinuousCaptureService
    {
        public bool IsRunning => false;

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaptureSessionOperationResult(
                status,
                status == CaptureOperationStatus.Saved ? "saved" : "capture failed",
                status == CaptureOperationStatus.Saved
                    ? new CaptureSessionOutput(
                        "session", "session/frame_manifest.csv", "session/metadata.json", 3)
                    : null));

        public Task StopAsync() => Task.CompletedTask;
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
}
