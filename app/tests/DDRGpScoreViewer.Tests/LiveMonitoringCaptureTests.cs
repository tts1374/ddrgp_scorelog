using System.Runtime.CompilerServices;
using DDRGpScoreViewer.Capture;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class LiveMonitoringCaptureTests
{
    [Fact]
    public async Task Live_monitor_requires_two_stable_score_samples_and_two_result_resets()
    {
        var observations = new Queue<LiveResultObservation>(
        [
            NonResult("grid"),
            NonResult("play"),
            Result("100", "song-a"),
            Result("100", "song-a"),
            Result("100", "song-a"),
            Result("100", "song-b"),
            Result("100", "song-b"),
            Result("100", ""),
            Result("100", ""),
            Result("100", ""),
            NonResult("grid"),
            NonResult("play"),
            Result("100", ""),
            Result("100", ""),
        ]);
        var source = new StubFrameSource(
            Frames(
                0,
                1_000,
                2_000,
                3_000,
                4_000,
                5_000,
                6_000,
                7_000,
                8_000,
                9_000,
                10_000,
                11_000,
                12_000,
                13_000),
            frameDelayMs: 5);
        var progress = new List<CaptureSessionProgress>();
        var processed = new List<string>();
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(observations));

        var result = await service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(progress.Add),
            (_, observation, _) =>
            {
                processed.Add(observation.Score);
                return Task.CompletedTask;
            });

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(["100", "100", "100", "100"], processed);
        Assert.Equal(14, progress[^1].SampledFrameCount);
        Assert.Equal(10, progress[^1].ResultFrameCount);
        Assert.Equal(4, progress[^1].ConfirmedCandidateCount);
        Assert.True(progress[^1].DiscardedFrameCount >= 6);
        Assert.Contains(
            progress,
            item => item.StatusMessage.Contains("次のRESULT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Live_monitor_keeps_only_one_pending_candidate_while_processing()
    {
        var firstCandidateStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new Queue<LiveResultObservation>(
        [
            Result("100", "song-a"),
            Result("100", "song-a"),
            Result("200", "song-b"),
            Result("200", "song-b"),
            Result("300", "song-c"),
            Result("300", "song-c"),
        ]);
        var source = new StubFrameSource(Frames(0, 1_000, 2_000, 3_000, 4_000, 5_000));
        var releaseFirstCandidate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var candidateQueueDropObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new List<string>();
        var progress = new List<CaptureSessionProgress>();
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(
                observations,
                waitBeforeThirdObservation: firstCandidateStarted.Task));

        var run = service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(item =>
            {
                progress.Add(item);
                if (item.CandidateQueueDropCount >= 1)
                {
                    candidateQueueDropObserved.TrySetResult();
                }
            }),
            async (_, observation, _) =>
            {
                processed.Add(observation.Score);
                if (processed.Count == 1)
                {
                    firstCandidateStarted.TrySetResult();
                    await releaseFirstCandidate.Task;
                }
            });

        await firstCandidateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await candidateQueueDropObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseFirstCandidate.TrySetResult();
        var result = await run;

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(2, processed.Count);
        Assert.Equal("100", processed[0]);
        Assert.NotEqual("100", processed[1]);
        Assert.Equal(2, progress[^1].ConfirmedCandidateCount);
        Assert.Equal(1, progress[^1].CandidateQueueDropCount);
        Assert.True(progress[^1].DiscardedFrameCount >= 1);
    }

    [Fact]
    public async Task Live_monitor_groups_animated_samples_with_the_same_adopted_result()
    {
        var observations = new Queue<LiveResultObservation>(
        [
            FormalResult("100", "animated-a"),
            FormalResult("100", "animated-b"),
            FormalResult("100", "animated-c"),
            FormalResult("100", "animated-d"),
        ]);
        var source = new StubFrameSource(Frames(0, 1_000, 2_000, 3_000));
        var processed = new List<(string Signature, string? EventId)>();
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(observations));

        var result = await service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(_ => { }),
            (_, observation, _) =>
            {
                processed.Add((observation.TitleSignature, observation.ConfirmedEventId));
                return Task.CompletedTask;
            });

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Single(processed);
        Assert.Equal("animated-b", processed[0].Signature);
        Assert.StartsWith("confirmed-event-v1:", processed[0].EventId);
    }

    [Fact]
    public async Task Live_monitor_does_not_accept_frames_after_explicit_stop()
    {
        var observations = new Queue<LiveResultObservation>(
        [
            Result("100", "song-a"),
            Result("100", "song-a"),
            Result("200", "song-b"),
            Result("200", "song-b"),
        ]);
        var source = new StubFrameSource(
            Frames(0, 1_000, 2_000, 3_000),
            frameDelayMs: 5);
        var processed = new List<string>();
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(observations));

        var result = await service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(_ => { }),
            async (_, observation, _) =>
            {
                processed.Add(observation.Score);
                await service.StopAsync();
            });

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(["100"], processed);
    }

    private static IReadOnlyList<CapturedFrame> Frames(params long[] timestamps) =>
        timestamps.Select(timestamp => new CapturedFrame(
            [1, 2, 3],
            1280,
            720,
            timestamp,
            DateTimeOffset.UtcNow,
            "DDR GRAND PRIX / ddr-konaste / client=1280 x 720")).ToArray();

    private static LiveResultObservation Result(string score, string title) =>
        new(true, score, title, "result_score_detected");

    private static LiveResultObservation FormalResult(string score, string title) =>
        Result(score, title) with
        {
            FormalEvidence = new AppOwnedFormalEvidence(
                null,
                null,
                null,
                int.Parse(score),
                1,
                1,
                1,
                1,
                1,
                1,
                1,
                "AAA",
                "CLEAR",
                null,
                new Dictionary<string, string>(),
                new Dictionary<string, double?>()),
        };

    private static LiveResultObservation NonResult(string reason) =>
        new(false, "", "", reason);

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class StubResultAnalyzer(
        Queue<LiveResultObservation> observations,
        Task? waitBeforeThirdObservation = null)
        : ILiveResultAnalyzer
    {
        private int observationCount;

        public async Task<LiveResultObservation> AnalyzeAsync(
            CapturedFrame frame,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref observationCount) == 3 &&
                waitBeforeThirdObservation is not null)
            {
                await waitBeforeThirdObservation.WaitAsync(cancellationToken);
            }
            return observations.Dequeue();
        }
    }

    private sealed class StubTargetedAdapter(StubFrameSource source)
        : IContinuousGraphicsCaptureAdapter, ITargetedContinuousGraphicsCaptureAdapter
    {
        public bool IsSupported => true;

        public Task<IContinuousFrameSource?> StartSessionAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IContinuousFrameSource?>(source);

        public Task<IContinuousFrameSource?> StartSessionForWindowAsync(
            nint targetWindowHandle,
            CaptureTargetInfo target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IContinuousFrameSource?>(source);
    }

    private sealed class StubFrameSource : IContinuousFrameSource, IContinuousFrameSourceMetadata
    {
        private readonly IReadOnlyList<CapturedFrame> frames;
        private readonly int frameDelayMs;
        private readonly TaskCompletionSource<CaptureSessionEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public StubFrameSource(IReadOnlyList<CapturedFrame> frames, int frameDelayMs = 0)
        {
            this.frames = frames;
            this.frameDelayMs = frameDelayMs;
            completion.TrySetResult(CaptureSessionEndReason.Stopped);
        }

        public Task<CaptureSessionEndReason> Completion => completion.Task;
        public CaptureTargetInfo Target => new("fixture target", 1280, 720);

        public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (frameDelayMs > 0)
                {
                    await Task.Delay(frameDelayMs, cancellationToken);
                }
                else
                {
                    await Task.Yield();
                }
                yield return frame;
            }
        }

        public Task StopAsync()
        {
            completion.TrySetResult(CaptureSessionEndReason.Stopped);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
