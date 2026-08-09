using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class LiveMonitoringViewModelTests
{
    [Fact]
    public async Task Configured_monitoring_processes_live_candidate_before_stop()
    {
        using var fixture = new DatabaseFixture();
        var target = new DdrGpWindowCandidate(
            101,
            42,
            "ddr-konaste",
            "DDR GRAND PRIX",
            1280,
            720);
        var live = new StubLiveMonitoringService();
        var workflow = new StubLiveWorkflowRunner(() =>
        {
            fixture.AddPlay("live-play", "2026-07-27T12:00:00+00:00", 999_000, 2_500);
            return new CaptureSaveWorkflowResult(
                "completed",
                1,
                new Dictionary<string, int> { ["saved"] = 1 },
                ["live-play"],
                [],
                null);
        });
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new UnusedContinuousCaptureService(),
            captureSaveWorkflowRunner: workflow,
            defaultDatabasePaths: ConfiguredPaths(fixture),
            ddrGpWindowEnumerator: new StubWindowEnumerator([target]),
            liveMonitoringService: live);

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(1, live.RunCount);
        Assert.Equal(1, workflow.CallCount);
        Assert.Equal(fixture.CatalogPath, workflow.CatalogDatabasePath);
        Assert.Equal(1, viewModel.MonitoringResults.Saved);
        Assert.Contains(viewModel.Plays, play => play.PlayId == "live-play");
        Assert.Equal(MonitoringState.Stopped, viewModel.CurrentMonitoringState);
        Assert.Contains("saved=1", viewModel.SaveStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_identity_retry_prepares_twice_but_runs_save_workflow_once()
    {
        using var fixture = new DatabaseFixture();
        var target = new DdrGpWindowCandidate(
            101,
            42,
            "ddr-konaste",
            "DDR GRAND PRIX",
            1280,
            720);
        var live = new StubLiveMonitoringService(candidateCount: 2);
        var retryIdentity = new Queue<bool>([true, false]);
        var workflow = new StubLiveWorkflowRunner(
            () =>
            {
                fixture.AddPlay("retried-live-play", "2026-08-09T12:00:00+00:00", 999_000, 2_500);
                return new CaptureSaveWorkflowResult(
                    "completed",
                    1,
                    new Dictionary<string, int> { ["saved"] = 1 },
                    ["retried-live-play"],
                    [],
                    null);
            },
            retryIdentity);
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new UnusedContinuousCaptureService(),
            captureSaveWorkflowRunner: workflow,
            defaultDatabasePaths: ConfiguredPaths(fixture),
            ddrGpWindowEnumerator: new StubWindowEnumerator([target]),
            liveMonitoringService: live);

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(2, workflow.PreparationCount);
        Assert.Equal(1, workflow.CallCount);
        Assert.Equal(1, viewModel.MonitoringResults.Saved);
        Assert.Single(viewModel.Plays, play => play.PlayId == "retried-live-play");
    }

    private static ViewerDatabasePaths ConfiguredPaths(DatabaseFixture fixture) =>
        new(
            ViewerDatabaseEnvironment.Development,
            fixture.DirectoryPath,
            fixture.MasterPath,
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates);
    }

    private sealed class StubLiveMonitoringService(int candidateCount = 1)
        : ILiveMonitoringCaptureService
    {
        public bool IsRunning => false;
        public int RunCount { get; private set; }

        public async Task<CaptureSessionOperationResult> RunAsync(
            nint targetWindowHandle,
            CaptureTargetInfo target,
            IProgress<CaptureSessionProgress> progress,
            Func<CapturedFrame, LiveResultObservation, LiveCandidateProcessingContext,
                CancellationToken, Task<LiveCandidateProcessingResult>> processCandidate,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            var now = DateTimeOffset.UtcNow;
            progress.Report(new CaptureSessionProgress(target, 2, now, now, 2, 2, 1, 0, 0, 0, "RESULTを確定しました。"));
            const string eventId = "confirmed-event-v1:live-view-model-fixture";
            for (var index = 0; index < candidateCount; index++)
            {
                await processCandidate(
                    new CapturedFrame([1, 2, 3], 1280, 720, 1_000 + index, now, target.DisplayName),
                    new LiveResultObservation(
                        true,
                        "999000",
                        $"song-a-{index}",
                        "result_score_detected",
                        ConfirmedEventId: eventId),
                    new LiveCandidateProcessingContext(false),
                    cancellationToken);
            }
            return new CaptureSessionOperationResult(
                CaptureOperationStatus.Cancelled,
                "live fixture stopped");
        }

        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class StubLiveWorkflowRunner(
        Func<CaptureSaveWorkflowResult> run,
        Queue<bool>? retryIdentity = null)
        : ICaptureSaveWorkflowRunner, ILiveCaptureSaveWorkflowRunner
    {
        public int CallCount { get; private set; }
        public int PreparationCount { get; private set; }
        public string? CatalogDatabasePath { get; private set; }

        public LiveCaptureCandidatePreparation PrepareCandidate(
            CapturedFrame frame,
            LiveResultObservation observation,
            string masterDatabasePath,
            string? catalogDatabasePath)
        {
            PreparationCount++;
            CatalogDatabasePath = catalogDatabasePath;
            return new(
                observation,
                RetryIdentity: retryIdentity is { Count: > 0 } && retryIdentity.Dequeue());
        }

        public Task<CaptureSaveWorkflowResult> RunAsync(
            string manifestPath,
            string scoreDatabasePath,
            string masterDatabasePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CaptureSaveWorkflowResult> RunCandidateAsync(
            CapturedFrame frame,
            string scoreDatabasePath,
            string masterDatabasePath,
            string? catalogDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CatalogDatabasePath = catalogDatabasePath;
            return Task.FromResult(run());
        }

        public Task<CaptureSaveWorkflowResult> RunCandidateAsync(
            CapturedFrame frame,
            LiveResultObservation observation,
            string scoreDatabasePath,
            string masterDatabasePath,
            string? catalogDatabasePath,
            CancellationToken cancellationToken = default) =>
            RunCandidateAsync(
                frame,
                scoreDatabasePath,
                masterDatabasePath,
                catalogDatabasePath,
                cancellationToken);

        public Task<CaptureSaveWorkflowResult> RunPreparedCandidateAsync(
            CapturedFrame frame,
            LiveResultObservation observation,
            string scoreDatabasePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(run());
        }
    }

    private sealed class UnusedContinuousCaptureService : IContinuousCaptureService
    {
        public bool IsRunning => false;

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopAsync() => Task.CompletedTask;
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
