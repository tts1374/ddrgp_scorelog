using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace JacketCatalogCollector.Tests;

public sealed class WindowCaptureTests
{
    [Fact]
    public void Ring_buffer_drops_oldest_and_keeps_fixed_capacity()
    {
        var ring = new BoundedFrameRingBuffer(2);

        ring.Add(Frame(1));
        ring.Add(Frame(2));
        ring.Add(Frame(3));

        Assert.Equal(2, ring.Count);
        Assert.Equal(1, ring.DroppedCount);
        Assert.Equal([2, 3], ring.Snapshot.Select(frame => frame.Sequence));
        Assert.Equal(3, ring.Latest?.Sequence);
    }

    [Fact]
    public async Task Callback_drain_waits_for_in_flight_callback_and_rejects_new_entries()
    {
        var drain = new InFlightCallbackDrain();
        Assert.True(drain.TryEnter());

        var closing = drain.CloseAndWaitAsync();

        Assert.False(closing.IsCompleted);
        Assert.False(drain.TryEnter());
        drain.Exit();
        await closing;
    }

    [Fact]
    public async Task Empty_candidates_do_not_select_or_create_capture_resources()
    {
        var windows = new FakeWindowEnumerator([]);
        var factory = new FakeSessionFactory();
        var coordinator = new WindowCaptureCoordinator(
            windows, factory, new RecordingDispatcher());
        var viewModel = new WindowCaptureViewModel(windows, coordinator);

        await viewModel.RefreshCandidatesAsync();

        Assert.Empty(viewModel.Candidates);
        Assert.Null(viewModel.SelectedCandidate);
        Assert.Contains("0件", viewModel.CandidateStatus);
        Assert.Equal(0, factory.StartCount);
    }

    [Fact]
    public async Task Refresh_never_auto_selects_or_starts_single_candidate()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var factory = new FakeSessionFactory();
        var coordinator = new WindowCaptureCoordinator(
            windows, factory, new RecordingDispatcher());
        var viewModel = new WindowCaptureViewModel(windows, coordinator);

        await viewModel.RefreshCandidatesAsync();

        Assert.Single(viewModel.Candidates);
        Assert.Null(viewModel.SelectedCandidate);
        Assert.Equal(0, factory.StartCount);
    }

    [Fact]
    public async Task Stale_candidate_is_rejected_before_session_or_ring_creation()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        windows.Current = candidate.Identity with { Title = "reused window" };
        var factory = new FakeSessionFactory();
        var coordinator = new WindowCaptureCoordinator(
            windows, factory, new RecordingDispatcher());

        var started = await coordinator.StartAsync(candidate);

        Assert.False(started);
        Assert.Equal(0, factory.StartCount);
        Assert.Equal(CaptureEndReason.IdentityChanged, coordinator.Snapshot.EndReason);
        Assert.Equal("not_created", coordinator.Snapshot.ResourceState);
    }

    [Fact]
    public async Task Captures_bounded_frames_and_releases_resources_once_on_repeated_stop()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var source = new FakeFrameSource();
        var factory = new FakeSessionFactory(source);
        var dispatcher = new RecordingDispatcher();
        var coordinator = new WindowCaptureCoordinator(windows, factory, dispatcher, ringCapacity: 2);

        Assert.True(await coordinator.StartAsync(candidate));
        Assert.False(await coordinator.StartAsync(candidate));
        source.Write(Frame(1));
        source.Write(Frame(2));
        source.Write(Frame(3));
        await WaitUntilAsync(() => coordinator.Snapshot.CapturedCount == 3);
        await Task.WhenAll(coordinator.StopAsync(), coordinator.StopAsync());

        Assert.Equal(CaptureLifecycleState.Stopped, coordinator.Snapshot.State);
        Assert.Equal(CaptureEndReason.ExplicitStop, coordinator.Snapshot.EndReason);
        Assert.Equal(3, coordinator.Snapshot.CapturedCount);
        Assert.Equal(1, coordinator.Snapshot.DroppedCount);
        Assert.Equal("released", coordinator.Snapshot.ResourceState);
        Assert.Equal(1, source.StopCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.True(dispatcher.InvokeCount >= 4);
    }

    [Theory]
    [InlineData("resize", CaptureEndReason.Resized)]
    [InlineData("identity", CaptureEndReason.IdentityChanged)]
    [InlineData("minimized", CaptureEndReason.Minimized)]
    [InlineData("closed", CaptureEndReason.TargetClosed)]
    public async Task Target_drift_stops_without_implicit_reselection(
        string drift,
        CaptureEndReason expectedReason)
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var source = new FakeFrameSource();
        var factory = new FakeSessionFactory(source);
        var coordinator = new WindowCaptureCoordinator(
            windows, factory, new RecordingDispatcher());
        Assert.True(await coordinator.StartAsync(candidate));

        windows.Current = drift switch
        {
            "resize" => candidate.Identity with { ClientWidth = 1279 },
            "identity" => candidate.Identity with { ProcessStartTicks = 999 },
            "minimized" => candidate.Identity with { IsMinimized = true },
            "closed" => null,
            _ => candidate.Identity,
        };
        source.Write(Frame(1));
        await WaitUntilAsync(() => coordinator.Snapshot.ResourceState == "released");

        Assert.Equal(expectedReason, coordinator.Snapshot.EndReason);
        if (drift == "resize")
        {
            Assert.Equal(1279, coordinator.Snapshot.CurrentWidth);
            Assert.Equal(720, coordinator.Snapshot.CurrentHeight);
        }
        Assert.Equal(0, coordinator.Snapshot.CapturedCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, factory.StartCount);
    }

    [Theory]
    [InlineData(CaptureEndReason.DeviceLost)]
    [InlineData(CaptureEndReason.CaptureFailed)]
    public async Task Source_failure_releases_resources_without_output(
        CaptureEndReason reason)
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var source = new FakeFrameSource();
        var coordinator = new WindowCaptureCoordinator(
            windows, new FakeSessionFactory(source), new RecordingDispatcher());
        Assert.True(await coordinator.StartAsync(candidate));

        source.Complete(reason);
        await WaitUntilAsync(() => coordinator.Snapshot.ResourceState == "released");

        Assert.Equal(CaptureLifecycleState.Failed, coordinator.Snapshot.State);
        Assert.Equal(reason, coordinator.Snapshot.EndReason);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task Encode_failure_preserves_completed_device_loss_reason()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var source = new ThrowingFrameSource(CaptureEndReason.DeviceLost);
        var coordinator = new WindowCaptureCoordinator(
            windows, new FakeSessionFactory(source), new RecordingDispatcher());
        Assert.True(await coordinator.StartAsync(candidate));

        await WaitUntilAsync(() => coordinator.Snapshot.ResourceState == "released");

        Assert.Equal(CaptureEndReason.DeviceLost, coordinator.Snapshot.EndReason);
        Assert.Equal("capture deviceが失われました。", coordinator.Snapshot.Message);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task Stop_during_in_flight_frame_discards_late_frame()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var source = new InFlightFrameSource(Frame(1));
        var coordinator = new WindowCaptureCoordinator(
            windows, new FakeSessionFactory(source), new RecordingDispatcher());
        var forwardedFrames = 0;
        coordinator.FrameReceived += (_, _) => forwardedFrames++;
        Assert.True(await coordinator.StartAsync(candidate));
        await source.ReaderStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stop = coordinator.StopAsync();
        source.ReleaseFrame.TrySetResult();
        await stop;

        Assert.Equal(0, coordinator.Snapshot.CapturedCount);
        Assert.Equal(0, forwardedFrames);
        Assert.Equal(CaptureEndReason.ExplicitStop, coordinator.Snapshot.EndReason);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task Stop_waits_for_an_accepted_frame_callback_before_crossing_boundary()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var source = new FakeFrameSource();
        var coordinator = new WindowCaptureCoordinator(
            windows, new FakeSessionFactory(source), new RecordingDispatcher());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var forwardedFrames = 0;
        coordinator.FrameReceived += (_, _) =>
        {
            forwardedFrames++;
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        };
        Assert.True(await coordinator.StartAsync(candidate));
        source.Write(Frame(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stop = Task.Run(async () => await coordinator.StopAsync());
        Assert.False(stop.IsCompleted);
        release.TrySetResult();
        await stop;

        Assert.Equal(1, forwardedFrames);
        Assert.Equal(CaptureEndReason.ExplicitStop, coordinator.Snapshot.EndReason);
    }

    [Fact]
    public async Task Reentrant_stop_from_frame_callback_is_rejected_without_deadlock()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var source = new FakeFrameSource();
        var coordinator = new WindowCaptureCoordinator(
            windows, new FakeSessionFactory(source), new RecordingDispatcher());
        Exception? rejection = null;
        coordinator.FrameReceived += (_, _) =>
        {
            try
            {
                coordinator.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                rejection = exception;
            }
        };
        Assert.True(await coordinator.StartAsync(candidate));
        source.Write(Frame(1));
        await WaitUntilAsync(() => coordinator.Snapshot.CapturedCount == 1);
        await coordinator.StopAsync();

        Assert.IsType<InvalidOperationException>(rejection);
        Assert.Equal(CaptureEndReason.ExplicitStop, coordinator.Snapshot.EndReason);
    }

    [Fact]
    public async Task Stop_cancels_pending_start_and_allows_clean_retry()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var factory = new BlockingSessionFactory();
        var coordinator = new WindowCaptureCoordinator(
            windows, factory, new RecordingDispatcher());

        var start = coordinator.StartAsync(candidate);
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(await coordinator.StartAsync(candidate));
        await coordinator.StopAsync();
        Assert.False(await start);

        Assert.Equal(CaptureEndReason.Cancelled, coordinator.Snapshot.EndReason);
        Assert.Equal("released", coordinator.Snapshot.ResourceState);
        Assert.True(start.IsCompleted);

        Assert.True(await coordinator.StartAsync(candidate));
        await coordinator.StopAsync();
        Assert.Equal(2, factory.StartCount);
        Assert.Equal(1, factory.RetrySource.DisposeCount);
    }

    [Fact]
    public async Task Stop_waits_for_pending_factory_that_ignores_cancellation()
    {
        var candidate = Candidate();
        var windows = new FakeWindowEnumerator([candidate]);
        var factory = new NonCancelableBlockingSessionFactory();
        var coordinator = new WindowCaptureCoordinator(
            windows, factory, new RecordingDispatcher());

        var start = coordinator.StartAsync(candidate);
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = coordinator.StopAsync();

        Assert.False(stop.IsCompleted);
        factory.Release.TrySetResult();
        await stop;
        Assert.False(await start);
        Assert.Equal(1, factory.Source.DisposeCount);
        Assert.Equal(CaptureEndReason.Cancelled, coordinator.Snapshot.EndReason);
    }

    private static WindowCandidate Candidate()
    {
        var identity = new WindowIdentitySnapshot(
            (nint)0x1234, 42, 100, "ddrgp", "DDR GRAND PRIX", "game",
            1280, 720, true, false);
        return new WindowCandidate(identity, "title contains DDR GRAND PRIX", [1, 2, 3]);
    }

    private static RawCaptureFrame Frame(long sequence) =>
        new([checked((byte)sequence)], 1280, 720, sequence, DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("Expected capture state was not reached.");
            }
            await Task.Delay(10);
        }
    }

    private sealed class FakeWindowEnumerator(IReadOnlyList<WindowCandidate> candidates)
        : IWindowEnumerator
    {
        public WindowIdentitySnapshot? Current { get; set; } = candidates.FirstOrDefault()?.Identity;

        public Task<IReadOnlyList<WindowCandidate>> EnumerateAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(candidates);

        public WindowIdentitySnapshot? TryGetSnapshot(nint handle) => Current;
    }

    private sealed class FakeSessionFactory(IWindowCaptureFrameSource? source = null)
        : IWindowCaptureSessionFactory
    {
        public bool IsSupported => true;
        public int StartCount { get; private set; }

        public Task<IWindowCaptureFrameSource> StartAsync(
            WindowIdentitySnapshot target,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.FromResult(source ?? new FakeFrameSource() as IWindowCaptureFrameSource);
        }
    }

    private sealed class BlockingSessionFactory : IWindowCaptureSessionFactory
    {
        public bool IsSupported => true;
        public int StartCount { get; private set; }
        public FakeFrameSource RetrySource { get; } = new();
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IWindowCaptureFrameSource> StartAsync(
            WindowIdentitySnapshot target,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            if (StartCount > 1)
            {
                return RetrySource;
            }
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class NonCancelableBlockingSessionFactory : IWindowCaptureSessionFactory
    {
        public bool IsSupported => true;
        public FakeFrameSource Source { get; } = new();
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IWindowCaptureFrameSource> StartAsync(
            WindowIdentitySnapshot target,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task;
            return Source;
        }
    }

    private sealed class ThrowingFrameSource(CaptureEndReason reason) : IWindowCaptureFrameSource
    {
        private readonly TaskCompletionSource<CaptureEndReason> completion = Completed(reason);

        public Task<CaptureEndReason> Completion => completion.Task;
        public long DroppedCount => 0;
        public int DisposeCount { get; private set; }

        public async IAsyncEnumerable<RawCaptureFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new IOException("encode failed");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        private static TaskCompletionSource<CaptureEndReason> Completed(CaptureEndReason value)
        {
            var result = new TaskCompletionSource<CaptureEndReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            result.SetResult(value);
            return result;
        }
    }

    private sealed class RecordingDispatcher : ICaptureDispatcher
    {
        public int InvokeCount { get; private set; }

        public Task InvokeAsync(Action action)
        {
            InvokeCount++;
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFrameSource : IWindowCaptureFrameSource
    {
        private readonly Channel<RawCaptureFrame> frames = Channel.CreateUnbounded<RawCaptureFrame>();
        private readonly TaskCompletionSource<CaptureEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int stopped;

        public Task<CaptureEndReason> Completion => completion.Task;
        public long DroppedCount => 0;
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Write(RawCaptureFrame frame) => frames.Writer.TryWrite(frame);

        public void Complete(CaptureEndReason reason)
        {
            completion.TrySetResult(reason);
            frames.Writer.TryComplete();
        }

        public async IAsyncEnumerable<RawCaptureFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var frame in frames.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }

        public Task StopAsync()
        {
            if (Interlocked.Exchange(ref stopped, 1) == 0)
            {
                StopCount++;
                Complete(CaptureEndReason.ExplicitStop);
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            Complete(CaptureEndReason.ExplicitStop);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InFlightFrameSource(RawCaptureFrame frame) : IWindowCaptureFrameSource
    {
        private readonly TaskCompletionSource<CaptureEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReaderStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFrame { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CaptureEndReason> Completion => completion.Task;
        public long DroppedCount => 0;
        public int DisposeCount { get; private set; }

        public async IAsyncEnumerable<RawCaptureFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReaderStarted.TrySetResult();
            await ReleaseFrame.Task.WaitAsync(cancellationToken);
            yield return frame;
        }

        public Task StopAsync()
        {
            completion.TrySetResult(CaptureEndReason.ExplicitStop);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
