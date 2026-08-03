using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class AutoWindowMonitoringTests
{
    [Theory]
    [InlineData("ddr-konaste", 1280, 720, true)]
    [InlineData("DDR-KONASTE", 1280, 720, true)]
    [InlineData("ddr-konaste", 1279, 720, false)]
    [InlineData("ddr-konaste", 1280, 719, false)]
    [InlineData("other-process", 1280, 720, false)]
    public void Automatic_window_filter_requires_process_and_client_size(
        string processName,
        int clientWidth,
        int clientHeight,
        bool expected)
    {
        var candidate = new DdrGpWindowCandidate(
            101,
            42,
            processName,
            "DDR GRAND PRIX",
            clientWidth,
            clientHeight);

        Assert.Equal(expected, DdrGpWindowEnumerator.IsDdrGpTarget(candidate));
    }

    [Fact]
    public async Task One_candidate_connects_to_targeted_capture_and_projects_target_details()
    {
        using var fixture = new DatabaseFixture();
        var target = Candidate(101, "DDR GRAND PRIX");
        var capture = new TargetedCaptureService(CaptureOperationStatus.Cancelled);
        var workflow = new RecordingCaptureWorkflowRunner();
        var viewModel = CreateViewModel(fixture, [target], capture, workflow);

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(1, capture.TargetedRunCount);
        Assert.Equal(0, capture.ManualRunCount);
        Assert.Equal(target.Handle, capture.LastTargetHandle);
        Assert.Equal(target.TargetInfo, capture.LastTarget);
        Assert.Contains("DDR GRAND PRIX", viewModel.MonitoringTarget, StringComparison.Ordinal);
        Assert.Contains("ddr-konaste", viewModel.MonitoringTarget, StringComparison.Ordinal);
        Assert.Contains("1280 x 720", viewModel.MonitoringTarget, StringComparison.Ordinal);
        Assert.Equal("1280 x 720", viewModel.MonitoringTargetSize);
        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
        Assert.Equal(0, workflow.CallCount);
    }

    [Fact]
    public async Task No_candidate_does_not_start_capture_or_workflow()
    {
        using var fixture = new DatabaseFixture();
        var capture = new TargetedCaptureService(CaptureOperationStatus.Cancelled);
        var workflow = new RecordingCaptureWorkflowRunner();
        var viewModel = CreateViewModel(fixture, [], capture, workflow);

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(0, capture.TargetedRunCount);
        Assert.Equal(0, workflow.CallCount);
        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
        Assert.Contains("見つかりません", viewModel.CaptureStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_target_window_does_not_start_capture_or_workflow()
    {
        using var fixture = new DatabaseFixture();
        var nonTarget = new DdrGpWindowCandidate(
            102,
            42,
            "other-process",
            "DDR GRAND PRIX",
            1280,
            720);
        var capture = new TargetedCaptureService(CaptureOperationStatus.Cancelled);
        var workflow = new RecordingCaptureWorkflowRunner();
        var viewModel = CreateViewModel(fixture, [nonTarget], capture, workflow);

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(0, capture.TargetedRunCount);
        Assert.Equal(0, workflow.CallCount);
        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
        Assert.Contains("見つかりません", viewModel.CaptureStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multiple_candidates_do_not_guess_a_target()
    {
        using var fixture = new DatabaseFixture();
        var capture = new TargetedCaptureService(CaptureOperationStatus.Cancelled);
        var workflow = new RecordingCaptureWorkflowRunner();
        var viewModel = CreateViewModel(
            fixture,
            [Candidate(101, "DDR GRAND PRIX 1"), Candidate(102, "DDR GRAND PRIX 2")],
            capture,
            workflow);

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(0, capture.TargetedRunCount);
        Assert.Equal(0, workflow.CallCount);
        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
        Assert.Contains("2件", viewModel.CaptureStatusMessage, StringComparison.Ordinal);
        Assert.Contains("推測で選択せず", viewModel.CaptureStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_validation_runs_after_detection_and_blocks_capture_save()
    {
        using var fixture = new DatabaseFixture();
        var missingMasterPath = Path.Combine(fixture.DirectoryPath, "missing-master.sqlite");
        var capture = new TargetedCaptureService(CaptureOperationStatus.Cancelled);
        var workflow = new RecordingCaptureWorkflowRunner();
        var viewModel = CreateViewModel(
            fixture,
            [Candidate(101, "DDR GRAND PRIX")],
            capture,
            workflow,
            ConfiguredPaths(fixture, missingMasterPath));

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(MasterDatabaseStatus.Missing, viewModel.MasterDatabaseStatus);
        Assert.Contains("DDR GRAND PRIX", viewModel.MonitoringTarget, StringComparison.Ordinal);
        Assert.Contains("ddr-konaste", viewModel.MonitoringTarget, StringComparison.Ordinal);
        Assert.Equal("1280 x 720", viewModel.MonitoringTargetSize);
        Assert.Equal(0, capture.TargetedRunCount);
        Assert.Equal(0, workflow.CallCount);
        Assert.Equal(MonitoringState.WorkflowFailed, viewModel.CurrentMonitoringState);
        Assert.Contains("解析・正式保存を開始しません", viewModel.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Target_closed_requires_an_explicit_new_start_and_does_not_reconnect()
    {
        using var fixture = new DatabaseFixture();
        var capture = new TargetedCaptureService(
            CaptureOperationStatus.TargetClosed,
            CaptureOperationStatus.Cancelled);
        var viewModel = CreateViewModel(
            fixture,
            [Candidate(101, "DDR GRAND PRIX")],
            capture,
            new RecordingCaptureWorkflowRunner());

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);
        Assert.Equal(MonitoringState.TargetClosed, viewModel.CurrentMonitoringState);
        Assert.Equal(1, capture.TargetedRunCount);

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(2, capture.TargetedRunCount);
        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
    }

    [Fact]
    public async Task Explicit_stop_stops_targeted_capture_without_starting_workflow()
    {
        using var fixture = new DatabaseFixture();
        var capture = new BlockingTargetedCaptureService();
        var workflow = new RecordingCaptureWorkflowRunner();
        var viewModel = CreateViewModel(
            fixture,
            [Candidate(101, "DDR GRAND PRIX")],
            capture,
            workflow);

        var startTask = viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);
        await capture.Started.Task;

        await viewModel.StopContinuousCaptureAsync();
        await startTask;

        Assert.Equal(1, capture.StopCount);
        Assert.Equal(0, workflow.CallCount);
        Assert.Equal(MonitoringState.ManuallyStopped, viewModel.CurrentMonitoringState);
    }

    private static DdrGpWindowCandidate Candidate(nint handle, string title) =>
        new(handle, 42, "ddr-konaste", title, 1280, 720);

    private static MainViewModel CreateViewModel(
        DatabaseFixture fixture,
        IReadOnlyList<DdrGpWindowCandidate> candidates,
        IContinuousCaptureService capture,
        RecordingCaptureWorkflowRunner workflow,
        ViewerDatabasePaths? paths = null) =>
        new(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: capture,
            captureSaveWorkflowRunner: workflow,
            defaultDatabasePaths: paths ?? ConfiguredPaths(fixture),
            ddrGpWindowEnumerator: new StubWindowEnumerator(candidates));

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

    private sealed class StubWindowEnumerator(
        IReadOnlyList<DdrGpWindowCandidate> candidates) : IDdrGpWindowEnumerator
    {
        public Task<IReadOnlyList<DdrGpWindowCandidate>> EnumerateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(candidates);
        }
    }

    private sealed class TargetedCaptureService(
        params CaptureOperationStatus[] statuses) :
        IContinuousCaptureService,
        ITargetedMonitoringContinuousCaptureService
    {
        private readonly Queue<CaptureOperationStatus> remaining = new(statuses);

        public bool IsRunning => false;
        public int ManualRunCount { get; private set; }
        public int TargetedRunCount { get; private set; }
        public nint? LastTargetHandle { get; private set; }
        public CaptureTargetInfo? LastTarget { get; private set; }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            ManualRunCount++;
            return Task.FromResult(Result(CaptureOperationStatus.Cancelled));
        }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint targetWindowHandle,
            CaptureTargetInfo target,
            IProgress<CaptureSessionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            TargetedRunCount++;
            LastTargetHandle = targetWindowHandle;
            LastTarget = target;
            var now = DateTimeOffset.UtcNow;
            progress.Report(new CaptureSessionProgress(target, 0, now, now));
            var status = remaining.Count == 0
                ? CaptureOperationStatus.Cancelled
                : remaining.Dequeue();
            return Task.FromResult(Result(status));
        }

        public Task StopAsync() => Task.CompletedTask;

        private static CaptureSessionOperationResult Result(CaptureOperationStatus status) =>
            new(status, $"fixture {status}");
    }

    private sealed class BlockingTargetedCaptureService :
        IContinuousCaptureService,
        ITargetedMonitoringContinuousCaptureService
    {
        private readonly TaskCompletionSource<CaptureSessionOperationResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRunning => !completion.Task.IsCompleted;
        public int StopCount { get; private set; }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaptureSessionOperationResult(
                CaptureOperationStatus.Cancelled,
                "manual fixture"));

        public Task<CaptureSessionOperationResult> RunAsync(
            nint targetWindowHandle,
            CaptureTargetInfo target,
            IProgress<CaptureSessionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            progress.Report(new CaptureSessionProgress(target, 0, now, now));
            Started.TrySetResult();
            return completion.Task;
        }

        public Task StopAsync()
        {
            StopCount++;
            completion.TrySetResult(new CaptureSessionOperationResult(
                CaptureOperationStatus.Cancelled,
                "fixture stopped"));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCaptureWorkflowRunner : ICaptureSaveWorkflowRunner
    {
        public int CallCount { get; private set; }

        public Task<CaptureSaveWorkflowResult> RunAsync(
            string manifestPath,
            string scoreDatabasePath,
            string masterDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new CaptureSaveWorkflowResult(
                "completed",
                0,
                new Dictionary<string, int>(),
                [],
                [],
                null));
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
