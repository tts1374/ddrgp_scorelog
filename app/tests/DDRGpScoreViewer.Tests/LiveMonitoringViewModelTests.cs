using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class LiveMonitoringViewModelTests
{
    [Fact]
    public async Task New_unresolved_notification_restarts_the_auto_clear_deadline()
    {
        using var fixture = new DatabaseFixture();
        var target = new DdrGpWindowCandidate(
            101,
            42,
            "ddr-konaste",
            "DDR GRAND PRIX",
            1280,
            720);
        var results = new Queue<CaptureSaveWorkflowResult>(
        [
            UnresolvedResult(
                "confirmed-event-v1:first",
                "first_reason"),
            UnresolvedResult(
                "confirmed-event-v1:latest",
                "latest_reason"),
        ]);
        var live = new StubLiveMonitoringService(candidateCount: 2);
        var workflow = new StubLiveWorkflowRunner(() => results.Dequeue());
        var scheduler = new ControlledNotificationScheduler();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new UnusedContinuousCaptureService(),
            captureSaveWorkflowRunner: workflow,
            defaultDatabasePaths: ConfiguredPaths(fixture),
            ddrGpWindowEnumerator: new StubWindowEnumerator([target]),
            liveMonitoringService: live)
        {
            UnresolvedNotificationScheduler = scheduler.ScheduleAsync,
        };

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.True(viewModel.HasUnresolvedNotification);
        Assert.Contains("confirmed-event-v1:latest", viewModel.UnresolvedNotificationMessage);
        Assert.Contains("latest_reason", viewModel.UnresolvedNotificationMessage);
        Assert.Equal(2, viewModel.MonitoringResults.Unresolved);
        Assert.Empty(viewModel.Plays);
        Assert.Equal(2, scheduler.Scheduled.Count);
        Assert.All(
            scheduler.Scheduled,
            scheduled => Assert.Equal(TimeSpan.FromSeconds(3), scheduled.Delay));

        await scheduler.Scheduled[0].ExpireAsync();
        Assert.True(viewModel.HasUnresolvedNotification);
        Assert.Contains("confirmed-event-v1:latest", viewModel.UnresolvedNotificationMessage);

        await scheduler.Scheduled[1].ExpireAsync();

        Assert.False(viewModel.HasUnresolvedNotification);
        Assert.Equal("", viewModel.UnresolvedNotificationTitle);
        Assert.Equal("", viewModel.UnresolvedNotificationMessage);
        Assert.Equal(2, viewModel.MonitoringResults.Unresolved);
        Assert.Empty(viewModel.Plays);
    }

    [Fact]
    public async Task Repeated_unresolved_capture_event_keeps_one_wpf_notification()
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
        var workflow = new StubLiveWorkflowRunner(
            () => UnresolvedResult(
                "confirmed-event-v1:repeated",
                "digit_recognition.ambiguous"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new UnusedManualWorkflowRunner(),
            continuousCaptureService: new UnusedContinuousCaptureService(),
            captureSaveWorkflowRunner: workflow,
            defaultDatabasePaths: ConfiguredPaths(fixture),
            ddrGpWindowEnumerator: new StubWindowEnumerator([target]),
            liveMonitoringService: live);
        var notifications = new List<UnresolvedCaptureNotification>();
        var diagnostics = new List<UnresolvedCaptureNotification>();
        viewModel.UnresolvedCaptureNotificationRequested += notifications.Add;
        viewModel.UnresolvedCaptureDiagnosticRecorded += diagnostics.Add;

        await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(123);

        Assert.Equal(2, viewModel.MonitoringResults.Unresolved);
        Assert.Single(notifications);
        Assert.Single(diagnostics);
        Assert.True(viewModel.HasUnresolvedNotification);
        Assert.Empty(viewModel.Plays);
    }

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

    [Fact]
    public async Task Consecutive_saved_plays_refresh_home_latest_recent_and_history()
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
        var savedPlayIds = new Queue<string>(["first-live-play", "second-live-play"]);
        var workflow = new StubLiveWorkflowRunner(() =>
        {
            var playId = savedPlayIds.Dequeue();
            var isFirst = playId == "first-live-play";
            fixture.AddPlay(
                playId,
                isFirst ? "2026-08-09T10:00:00+00:00" : "2026-08-09T10:10:00+00:00",
                isFirst ? 900_000 : 950_000,
                isFirst ? 2_000 : 2_100);
            return new CaptureSaveWorkflowResult(
                "completed",
                1,
                new Dictionary<string, int> { ["saved"] = 1 },
                [playId],
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

        Assert.Equal("second-live-play", viewModel.HomeLatestPlay?.Play.PlayId);
        Assert.Equal(
            ["first-live-play"],
            viewModel.HomeRecentPlays.Select(play => play.Play.PlayId));
        Assert.Equal(
            ["second-live-play", "first-live-play"],
            viewModel.Plays.Select(play => play.PlayId));
        Assert.Equal(viewModel.Plays[0].PlayId, viewModel.HomeLatestPlay?.Play.PlayId);
        Assert.DoesNotContain(
            viewModel.HomeRecentPlays,
            play => play.Play.PlayId == viewModel.HomeLatestPlay?.Play.PlayId);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unresolved")]
    public async Task Non_saved_second_candidate_keeps_first_saved_play_as_home_latest(
        string secondStatus)
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
        var callCount = 0;
        var workflow = new StubLiveWorkflowRunner(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                fixture.AddPlay("first-live-play", "2026-08-09T10:00:00+00:00", 900_000, 2_000);
                return new CaptureSaveWorkflowResult(
                    "completed",
                    1,
                    new Dictionary<string, int> { ["saved"] = 1 },
                    ["first-live-play"],
                    [],
                    null);
            }

            return new CaptureSaveWorkflowResult(
                "completed",
                1,
                new Dictionary<string, int> { [secondStatus] = 1 },
                [],
                [secondStatus],
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

        Assert.Equal("first-live-play", viewModel.HomeLatestPlay?.Play.PlayId);
        Assert.Empty(viewModel.HomeRecentPlays);
        Assert.Equal("first-live-play", Assert.Single(viewModel.Plays).PlayId);
    }

    private static CaptureSaveWorkflowResult UnresolvedResult(
        string eventId,
        string reason) =>
        new(
            "completed",
            1,
            new Dictionary<string, int> { ["unresolved"] = 1 },
            [],
            [reason],
            null,
            [new CaptureSaveEventResult(eventId, "unresolved", [reason])]);

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

    private sealed class StubLiveMonitoringService(
        int candidateCount = 1,
        TimeSpan? delayBeforeNextCandidate = null)
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
                if (index > 0 && delayBeforeNextCandidate is { } delay)
                {
                    await Task.Delay(delay, cancellationToken);
                }
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
