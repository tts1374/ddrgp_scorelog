namespace JacketCatalogCollector.Tests;

public sealed class CaptureObservationControllerTests
{
    [Fact]
    public async Task Duplicate_start_does_not_stop_existing_observation_or_capture()
    {
        var operations = new FakeOperations
        {
            StartObservationException = new InvalidOperationException("observation is active"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.CreateController().StartAsync(Candidate()));

        Assert.Equal(["observation.start"], operations.Calls);
    }

    [Fact]
    public async Task Fresh_start_capture_false_stops_only_the_new_observation()
    {
        var operations = new FakeOperations { CaptureStartResult = false };

        var result = await operations.CreateController().StartAsync(Candidate());

        Assert.False(result);
        Assert.Equal(
            ["observation.start", "capture.start", "observation.stop"],
            operations.Calls);
    }

    [Fact]
    public async Task Fresh_start_capture_exception_stops_only_the_new_observation()
    {
        var operations = new FakeOperations
        {
            CaptureStartException = new InvalidOperationException("capture unavailable"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.CreateController().StartAsync(Candidate()));

        Assert.Equal(
            ["observation.start", "capture.start", "observation.stop"],
            operations.Calls);
    }

    [Fact]
    public async Task Fresh_start_capture_cancellation_stops_only_the_new_observation()
    {
        var operations = new FakeOperations
        {
            CaptureStartException = new OperationCanceledException("capture cancelled"),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operations.CreateController().StartAsync(Candidate()));

        Assert.Equal(
            ["observation.start", "capture.start", "observation.stop"],
            operations.Calls);
    }

    [Fact]
    public async Task Fresh_start_success_keeps_both_new_resources_active()
    {
        var operations = new FakeOperations();

        var result = await operations.CreateController().StartAsync(Candidate());

        Assert.True(result);
        Assert.Equal(["observation.start", "capture.start"], operations.Calls);
        Assert.True(operations.ActiveObservation);
        Assert.True(operations.ActiveCapture);
    }

    [Fact]
    public async Task Resume_starts_observation_before_capture()
    {
        var operations = new FakeOperations();

        var result = await operations.CreateController().ResumeAsync(Candidate());

        Assert.True(result);
        Assert.Equal(["observation.resume", "capture.start"], operations.Calls);
    }

    [Fact]
    public async Task Resume_passes_original_candidate_when_selection_changes_while_blocked()
    {
        var candidateA = Candidate((nint)0xA);
        var candidateB = Candidate((nint)0xB);
        var operations = new FakeOperations
        {
            BlockResume = true,
            SelectedCandidate = candidateA,
        };
        var resume = operations.CreateController().ResumeAsync(candidateA);

        await operations.ResumeObservationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        operations.SelectedCandidate = candidateB;
        operations.ResumeObservationRelease.TrySetResult();

        Assert.True(await resume);
        Assert.Equal(candidateA.Identity, Assert.Single(operations.CaptureCandidates).Identity);
    }

    [Fact]
    public async Task Resume_failure_does_not_start_or_stop_capture_or_observation()
    {
        var operations = new FakeOperations
        {
            ResumeObservationException = new InvalidOperationException("checkpoint is active"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.CreateController().ResumeAsync(Candidate()));

        Assert.Equal(["observation.resume"], operations.Calls);
    }

    [Fact]
    public async Task Resume_capture_false_stops_only_the_new_observation()
    {
        var operations = new FakeOperations { CaptureStartResult = false };

        var result = await operations.CreateController().ResumeAsync(Candidate());

        Assert.False(result);
        Assert.Equal(
            ["observation.resume", "capture.start", "observation.stop"],
            operations.Calls);
    }

    [Fact]
    public async Task Resume_capture_exception_stops_only_the_new_observation()
    {
        var operations = new FakeOperations
        {
            CaptureStartException = new InvalidOperationException("capture unavailable"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.CreateController().ResumeAsync(Candidate()));

        Assert.Equal(
            ["observation.resume", "capture.start", "observation.stop"],
            operations.Calls);
    }

    [Fact]
    public async Task Resume_immediate_terminal_capture_stops_new_capture_and_observation()
    {
        var operations = new FakeOperations { CaptureState = CaptureLifecycleState.Failed };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.CreateController().ResumeAsync(Candidate()));

        Assert.Equal(
            [
                "observation.resume",
                "capture.start",
                "capture.stop",
                "observation.stop",
            ],
            operations.Calls);
    }

    [Fact]
    public async Task Resume_immediate_stopped_capture_is_cleaned_up()
    {
        var operations = new FakeOperations { CaptureState = CaptureLifecycleState.Stopped };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.CreateController().ResumeAsync(Candidate()));

        Assert.Contains("capture.stop", operations.Calls);
        Assert.Contains("observation.stop", operations.Calls);
        Assert.False(operations.ActiveCapture);
        Assert.False(operations.ActiveObservation);
    }

    [Fact]
    public async Task Stop_cancels_blocked_resume_before_capture_start_and_stops_both_resources()
    {
        var operations = new FakeOperations { BlockResume = true };
        var controller = operations.CreateController();
        var resume = controller.ResumeAsync(Candidate());

        await operations.ResumeObservationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await controller.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resume);
        Assert.DoesNotContain("capture.start", operations.Calls);
        Assert.Contains("capture.stop", operations.Calls);
        Assert.Contains("observation.stop", operations.Calls);
        Assert.False(operations.ActiveCapture);
        Assert.False(operations.ActiveObservation);
    }

    [Fact]
    public async Task Stop_attempts_observation_cleanup_when_capture_stop_throws()
    {
        var operations = new FakeOperations
        {
            StopCaptureException = new InvalidOperationException("capture stop failed"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.CreateController().StopAsync());

        Assert.Equal(["capture.stop", "observation.stop"], operations.Calls);
        Assert.False(operations.ActiveCapture);
        Assert.False(operations.ActiveObservation);
    }

    [Fact]
    public async Task Explicit_stop_runs_catalog_finalizer_after_safe_observation_stop()
    {
        var operations = new FakeOperations();

        await operations.CreateController(withFinalizer: true).StopAsync();

        Assert.Equal(
            ["capture.stop", "observation.stop", "observation.finalize"],
            operations.Calls);
    }

    [Fact]
    public async Task Abort_does_not_run_catalog_finalizer()
    {
        var operations = new FakeOperations();

        await operations.CreateController(withFinalizer: true).AbortAsync();

        Assert.Equal(["capture.stop", "observation.stop"], operations.Calls);
    }

    [Fact]
    public async Task Capture_stop_failure_does_not_run_catalog_finalizer()
    {
        var operations = new FakeOperations
        {
            StopCaptureException = new InvalidOperationException("capture stop failed"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => operations.CreateController(withFinalizer: true).StopAsync());

        Assert.Equal(["capture.stop", "observation.stop"], operations.Calls);
    }

    [Fact]
    public async Task Cancellation_before_start_has_no_side_effects()
    {
        var operations = new FakeOperations();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operations.CreateController().StartAsync(Candidate(), cancellation.Token));

        Assert.Empty(operations.Calls);
    }

    private static WindowCandidate Candidate(nint handle = (nint)0x1234) => new(
        new WindowIdentitySnapshot(
            handle,
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

    private sealed class FakeOperations
    {
        public List<string> Calls { get; } = [];
        public List<WindowCandidate> CaptureCandidates { get; } = [];
        public CaptureLifecycleState CaptureState { get; init; } = CaptureLifecycleState.Capturing;
        public bool CaptureStartResult { get; init; } = true;
        public Exception? StartObservationException { get; init; }
        public Exception? ResumeObservationException { get; init; }
        public Exception? CaptureStartException { get; init; }
        public Exception? StopCaptureException { get; init; }
        public bool BlockResume { get; init; }
        public WindowCandidate? SelectedCandidate { get; set; }
        public bool ActiveObservation { get; private set; }
        public bool ActiveCapture { get; private set; }
        public TaskCompletionSource ResumeObservationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeObservationRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CaptureObservationController CreateController(bool withFinalizer = false) =>
            new(
                StartObservationAsync,
                ResumeObservationAsync,
                StopObservationAsync,
                StartCaptureAsync,
                StopCaptureAsync,
                () => CaptureState,
                withFinalizer ? FinalizeObservationAsync : null);

        private async Task StartObservationAsync(
            WindowCandidate candidate,
            CancellationToken cancellationToken)
        {
            Calls.Add("observation.start");
            await ThrowOrCompleteAsync(StartObservationException);
            ActiveObservation = true;
        }

        private async Task ResumeObservationAsync(
            WindowCandidate candidate,
            CancellationToken cancellationToken)
        {
            Calls.Add("observation.resume");
            await ThrowOrCompleteAsync(ResumeObservationException);
            if (BlockResume)
            {
                ResumeObservationEntered.TrySetResult();
                await ResumeObservationRelease.Task.WaitAsync(cancellationToken);
            }
            ActiveObservation = true;
        }

        private Task StopObservationAsync(CancellationToken cancellationToken)
        {
            Calls.Add("observation.stop");
            ActiveObservation = false;
            return Task.CompletedTask;
        }

        private Task FinalizeObservationAsync(CancellationToken cancellationToken)
        {
            Calls.Add("observation.finalize");
            return Task.CompletedTask;
        }

        private Task<bool> StartCaptureAsync(
            WindowCandidate candidate,
            CancellationToken cancellationToken)
        {
            Calls.Add("capture.start");
            CaptureCandidates.Add(candidate);
            if (CaptureStartException is not null)
            {
                return Task.FromException<bool>(CaptureStartException);
            }
            ActiveCapture = CaptureStartResult;
            return Task.FromResult(CaptureStartResult);
        }

        private Task StopCaptureAsync()
        {
            Calls.Add("capture.stop");
            ActiveCapture = false;
            if (StopCaptureException is not null)
            {
                return Task.FromException(StopCaptureException);
            }
            return Task.CompletedTask;
        }

        private static Task ThrowOrCompleteAsync(Exception? exception) =>
            exception is null ? Task.CompletedTask : Task.FromException(exception);
    }
}
