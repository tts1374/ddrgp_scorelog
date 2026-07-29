using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class DeveloperOperationsTests
{
    [Fact]
    public async Task Developer_commands_are_rejected_during_monitoring_and_stopping()
    {
        var capture = new BlockingMonitoringCaptureService();
        var singleFrame = new RecordingSingleFrameCaptureService();
        var workflow = new RecordingWorkflowRunner();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            workflow,
            singleFrame,
            capture);

        var monitoring = viewModel.StartContinuousCaptureAsync(123);
        await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.CanRunDeveloperOperations);
        await viewModel.CaptureOneFrameAsync(123);
        await viewModel.SaveAndReloadAsync("workflow.json", "score.sqlite", "master.sqlite");

        Assert.Equal(0, singleFrame.CallCount);
        Assert.Equal(0, workflow.CallCount);

        var stopping = viewModel.StopContinuousCaptureAsync();
        await capture.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MonitoringState.Stopping, viewModel.CurrentMonitoringState);
        Assert.False(viewModel.CanRunDeveloperOperations);
        await viewModel.CaptureOneFrameAsync(123);
        await viewModel.SaveAndReloadAsync("workflow.json", "score.sqlite", "master.sqlite");

        Assert.Equal(0, singleFrame.CallCount);
        Assert.Equal(0, workflow.CallCount);

        capture.Complete();
        await stopping;
        await monitoring;

        Assert.True(viewModel.CanRunDeveloperOperations);
    }

    [Fact]
    public async Task Developer_commands_are_rejected_while_monitoring_start_is_pending()
    {
        var singleFrame = new RecordingSingleFrameCaptureService();
        var workflow = new RecordingWorkflowRunner();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            workflow,
            singleFrame);

        Assert.True(viewModel.CanRunDeveloperOperations);
        viewModel.SetMonitoringStartPending(true);

        Assert.False(viewModel.CanRunDeveloperOperations);
        await viewModel.CaptureOneFrameAsync(123);
        await viewModel.SaveAndReloadAsync("workflow.json", "score.sqlite", "master.sqlite");

        Assert.Equal(0, singleFrame.CallCount);
        Assert.Equal(0, workflow.CallCount);

        viewModel.SetMonitoringStartPending(false);
        Assert.True(viewModel.CanRunDeveloperOperations);
    }

    private sealed class RecordingSingleFrameCaptureService : ISingleFrameCaptureService
    {
        public int CallCount { get; private set; }

        public Task<CaptureOperationResult> CaptureAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new CaptureOperationResult(
                CaptureOperationStatus.Saved,
                "captured"));
        }
    }

    private sealed class RecordingWorkflowRunner : IPersonalScoreDbWorkflowRunner
    {
        public int CallCount { get; private set; }

        public Task<PersonalScoreDbWorkflowResult> RunAsync(
            string workflowInputPath,
            string scoreDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new PersonalScoreDbWorkflowResult(
                "excluded",
                "not_requested",
                "excluded",
                "not_checked",
                false,
                null,
                null,
                null,
                ["fixture"],
                null,
                scoreDatabasePath));
        }
    }

    private sealed class BlockingMonitoringCaptureService
        : IMonitoringContinuousCaptureService
    {
        private readonly TaskCompletionSource<CaptureSessionOperationResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRunning => !completion.Task.IsCompleted;

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            RunAsync(ownerWindowHandle, new Progress<CaptureSessionProgress>(), cancellationToken);

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
            StopRequested.TrySetResult();
            return Task.CompletedTask;
        }

        public void Complete() => completion.TrySetResult(
            new CaptureSessionOperationResult(
                CaptureOperationStatus.Cancelled,
                "stopped"));
    }
}
