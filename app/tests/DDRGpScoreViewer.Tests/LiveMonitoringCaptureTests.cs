using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DDRGpScoreViewer.Capture;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class LiveMonitoringCaptureTests
{
    [Fact]
    public async Task Completed_unresolved_candidate_does_not_replace_saved_result_key()
    {
        var normal = FormalResult("100", "saved");
        var unresolved = FormalResult("200", "unresolved") with
        {
            FormalEvidence = FormalResult("200", "unresolved").FormalEvidence! with
            {
                Confidences = new Dictionary<string, double?> { ["score"] = 0.1 },
            },
        };
        var observations = new Queue<LiveResultObservation>([normal, normal, unresolved, unresolved, normal, normal]);
        var completed = Enumerable.Range(0, 2).Select(_ =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var reports = 0;
        var calls = 0;
        var source = new StubFrameSource(Frames(0, 1000, 2000, 3000, 4000, 5000),
            beforeFrame: (index, token) => index is 2 or 4
                ? completed[index / 2 - 1].Task.WaitAsync(token) : Task.CompletedTask);
        var service = new LiveMonitoringCaptureService(new StubTargetedAdapter(source), new StubResultAnalyzer(observations));
        await service.RunAsync(123, source.Target,
            new CallbackProgress<CaptureSessionProgress>(value =>
            {
                if (value.StatusMessage.StartsWith("RESULT同定根拠を確認しています。", StringComparison.Ordinal) &&
                    Interlocked.Increment(ref reports) is 2 or 4)
                    completed[reports / 2 - 1].TrySetResult();
            }),
            (_, _, _, _) =>
            {
                calls++;
                return Task.FromResult(LiveCandidateProcessingResult.Completed);
            });
        Assert.Equal(2, calls); // Saved attempt, unresolved attempt; no resave on recovery.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_completion_from_departed_screen_does_not_suppress_identical_next_event(bool retryIdentity)
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextScreenObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new Queue<LiveResultObservation>([
            FormalResult("100", "first"), FormalResult("100", "first"), NonResult("play"), NonResult("play"),
            FormalResult("100", "next"), FormalResult("100", "next"), FormalResult("100", "next")]);
        var source = new ControlledFrameSource();
        var events = new List<(string? EventId, bool Finalize)>();
        var service = new LiveMonitoringCaptureService(new StubTargetedAdapter(source), new StubResultAnalyzer(observations));
        var run = service.RunAsync(123, source.Target,
            new CallbackProgress<CaptureSessionProgress>(value =>
            {
                if (value.SampledFrameCount == 5)
                {
                    nextScreenObserved.TrySetResult();
                    if (releaseFirst.Task.IsCompleted) firstCompleted.TrySetResult();
                }
            }),
            async (_, observation, context, token) =>
            {
                events.Add((observation.ConfirmedEventId, context.FinalizeUnresolved));
                if (events.Count == 1)
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task.WaitAsync(token);
                    return retryIdentity ? LiveCandidateProcessingResult.RetryIdentity : LiveCandidateProcessingResult.Completed;
                }
                return LiveCandidateProcessingResult.Completed;
            });
        var frames = Frames(0, 1000, 2000, 3000, 4000, 5000, 6000);
        foreach (var frame in frames.Take(2)) source.Add(frame);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var frame in frames.Skip(2).Take(3)) source.Add(frame);
        await nextScreenObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        releaseFirst.TrySetResult();
        await firstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var frame in frames.Skip(5)) source.Add(frame);
        source.Complete();
        await run;
        Assert.Equal(2, events.Select(item => item.EventId).Distinct().Count());
        Assert.Equal(retryIdentity ? 3 : 2, events.Count);
        if (retryIdentity)
        {
            Assert.True(events[1].Finalize);
            Assert.Equal(events[0].EventId, events[1].EventId);
        }
    }

    [Fact]
    public async Task Initially_incomplete_app_owned_result_never_enters_workflow()
    {
        var incomplete = FormalResult("100", "incomplete") with
        {
            FormalEvidence = FormalResult("100", "incomplete").FormalEvidence! with { MaxCombo = null },
        };
        var source = new StubFrameSource(Frames(0, 1000, 2000));
        var service = new LiveMonitoringCaptureService(new StubTargetedAdapter(source),
            new StubResultAnalyzer(new Queue<LiveResultObservation>([incomplete, incomplete, incomplete])));
        var calls = 0;
        await service.RunAsync(123, source.Target, new CallbackProgress<CaptureSessionProgress>(_ => { }),
            (_, _, _, _) =>
            {
                calls++;
                return Task.FromResult(LiveCandidateProcessingResult.Completed);
            });
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Identity_retry_survives_incomplete_and_unknown_frames_with_the_same_event_id()
    {
        var normal = FormalResult("100", "stable");
        var incomplete = normal with { FormalEvidence = normal.FormalEvidence! with { Score = null } };
        var unknown = new LiveResultObservation(false, "", "", "frame_not_decodable");
        var retryReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new Queue<LiveResultObservation>([normal, normal, incomplete, incomplete, unknown, unknown, unknown, normal, normal]);
        var source = new StubFrameSource(Frames(0, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000),
            frameDelayMs: 5, beforeFrame: (index, token) => index == 2 ? retryReady.Task.WaitAsync(token) : Task.CompletedTask);
        var events = new List<string?>();
        var service = new LiveMonitoringCaptureService(new StubTargetedAdapter(source), new StubResultAnalyzer(observations));
        await service.RunAsync(123, source.Target,
            new CallbackProgress<CaptureSessionProgress>(value =>
            {
                if (value.StatusMessage.Contains("後続frameを再評価", StringComparison.Ordinal)) retryReady.TrySetResult();
            }),
            (_, observation, context, _) =>
            {
                Assert.False(context.FinalizeUnresolved);
                events.Add(observation.ConfirmedEventId);
                return Task.FromResult(events.Count == 1 ? LiveCandidateProcessingResult.RetryIdentity : LiveCandidateProcessingResult.Completed);
            });
        Assert.Equal(2, events.Count);
        Assert.Single(events.Distinct());
    }

    [LocalResultFact]
    public async Task Image_replay_holds_saved_event_through_failures_and_saves_identical_next_play_on_both_sides()
    {
        foreach (var number in new[] { "038", "373" })
        {
            using var database = ResultScreenLayoutTests.LocalDatabase();
            var png = File.ReadAllBytes(ResultScreenLayoutTests.Sample("result", number));
            var secondPlayer = number == "373";
            byte[] Mask(params (int X, int Y, int Width, int Height)[] regions)
            {
                var bitmap = new FormatConvertedBitmap(ResultScreenLayoutTests.Decode(png), PixelFormats.Bgra32, null, 0);
                var pixels = new byte[1280 * 720 * 4];
                bitmap.CopyPixels(pixels, 1280 * 4, 0);
                foreach (var (x, y, width, height) in regions)
                    for (var row = y; row < y + height; row++)
                        Array.Clear(pixels, (row * 1280 + x) * 4, width * 4);
                return ResultScreenLayoutTests.Encode(pixels);
            }
            var missingScore = Mask(DDRGpScoreViewer.Runtime.ResultScreenLayout.Map(
                DDRGpScoreViewer.Runtime.M7aDigitRecognizer.RoiDefinitions["score_digits"], secondPlayer));
            var missingRequired = Mask(DDRGpScoreViewer.Runtime.ResultScreenLayout.Map(
                DDRGpScoreViewer.Runtime.M7aDigitRecognizer.RoiDefinitions["marvelous"], secondPlayer));
            var missingStructure = Mask((480, 0, 320, 58), (662, 330, 462, 288), (164, 330, 462, 288));
            var blackFrame = ResultScreenLayoutTests.Encode(new byte[1280 * 720 * 4]);
            var gameplay = File.ReadAllBytes(Directory.GetFiles(
                Path.Combine(ResultScreenLayoutTests.RepositoryRoot!, "samples/screenshots/organized/gameplay"),
                "gameplay_037_*.png").Single());
            var source = new ControlledFrameSource();
            var analyzer = new AppOwnedLiveResultAnalyzer();
            var workflow = new DDRGpScoreViewer.Data.AppOwnedCaptureSaveWorkflowRunner();
            var completions = Channel.CreateUnbounded<int>();
            var events = new List<string?>();
            var processingReports = 0;
            var service = new LiveMonitoringCaptureService(new StubTargetedAdapter(source), analyzer);
            var run = service.RunAsync(123, source.Target,
                new CallbackProgress<CaptureSessionProgress>(value =>
                {
                    if (value.StatusMessage.StartsWith("RESULT同定根拠を確認しています。", StringComparison.Ordinal) &&
                        Interlocked.Increment(ref processingReports) % 2 == 0)
                        completions.Writer.TryWrite(events.Count);
                }),
                async (frame, observation, _, token) =>
                {
                    var result = await workflow.RunCandidateAsync(frame, observation, database.ScorePath,
                        database.MasterPath, database.CatalogPath, token);
                    Assert.Single(result.SavedPlayIds);
                    events.Add(observation.ConfirmedEventId);
                    return LiveCandidateProcessingResult.Completed;
                });
            long time = 0;
            void Add(byte[] bytes) { source.Add(ResultScreenLayoutTests.Frame(bytes, time)); time += 1000; }
            Add(png); Add(png);
            Assert.Equal(1, await completions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            foreach (var bytes in new[] { missingScore, missingRequired })
            {
                var observation = await analyzer.AnalyzeAsync(ResultScreenLayoutTests.Frame(bytes));
                Assert.True(observation.IsResultScreen);
                Assert.Null(AppOwnedResultEventFingerprint.TryCreate(observation, requireIdentity: false));
                Add(bytes); Add(bytes); Add(png); Add(png);
            }
            var missed = await analyzer.AnalyzeAsync(ResultScreenLayoutTests.Frame(missingStructure));
            Assert.False(missed.IsResultScreen);
            Assert.False(missed.HasResultStructure);
            Assert.NotNull(missed.ResultSceneFeature);
            Add(missingStructure); Add(missingStructure); Add(missingStructure); Add(png); Add(png);
            Add(blackFrame); Add(blackFrame); Add(png); Add(png);
            Add([1, 2, 3]); Add([1, 2, 3]); Add(png); Add(png);
            var departure = await analyzer.AnalyzeAsync(ResultScreenLayoutTests.Frame(gameplay));
            Assert.False(departure.IsResultScreen);
            Assert.False(departure.HasResultStructure);
            Add(gameplay); Add(gameplay); Add(png); Add(png);
            Assert.Equal(2, await completions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            source.Complete();
            await run;
            Assert.Equal(2L, ResultScreenLayoutTests.PlayCount(database.ScorePath));
            Assert.Equal(2, events.Distinct().Count());
        }
    }

    [Theory]
    [InlineData(false, "score")]
    [InlineData(true, "score")]
    [InlineData(false, "required")]
    [InlineData(true, "required")]
    [InlineData(false, "undetected")]
    [InlineData(true, "undetected")]
    [InlineData(false, "state")]
    [InlineData(true, "state")]
    public async Task Saved_result_survives_incomplete_or_undetected_samples(bool secondPlayer, string failure)
    {
        var normal = FormalResult("100", "stable") with { IsSecondPlayer = secondPlayer };
        var incomplete = failure == "undetected"
            ? new LiveResultObservation(false, "", "", "frame_not_decodable")
            : normal with
            {
                Score = failure == "score" ? "" : "100",
                DigitRecognitions = new Dictionary<string, DDRGpScoreViewer.Runtime.M7aDigitRecognitionResult>
                {
                    ["score"] = new("score", "score_digits", "", "", null,
                        failure == "score" ? "ambiguous" : "recognized", "", null, null, 0, 0, ""),
                },
                FormalEvidence = normal.FormalEvidence! with
                {
                    Score = failure == "score" ? null : 100,
                    Marvelous = failure == "required" ? null : 1,
                    ClearType = failure == "state" ? null : "CLEAR",
                },
            };
        var firstCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processingReports = 0;
        var observations = new Queue<LiveResultObservation>([normal, normal, incomplete, incomplete, incomplete, normal, normal]);
        var source = new StubFrameSource(Frames(0, 1000, 2000, 3000, 4000, 5000, 6000),
            frameDelayMs: 5, beforeFrame: (index, token) => index == 2
                ? firstCompleted.Task.WaitAsync(token) : Task.CompletedTask);
        var events = new List<string?>();
        var service = new LiveMonitoringCaptureService(new StubTargetedAdapter(source), new StubResultAnalyzer(observations));
        await service.RunAsync(123, source.Target,
            new CallbackProgress<CaptureSessionProgress>(value =>
            {
                if (value.StatusMessage.StartsWith("RESULT同定根拠を確認しています。", StringComparison.Ordinal) &&
                    Interlocked.Increment(ref processingReports) == 2) firstCompleted.TrySetResult();
            }),
            (_, observation, _, _) =>
            {
                events.Add(observation.ConfirmedEventId);
                return Task.FromResult(LiveCandidateProcessingResult.Completed);
            });
        Assert.Single(events);
    }

    [LocalResultFact]
    public async Task Two_player_image_replay_changes_score_confirms_once_and_resumes_next_result()
    {
        using var database = ResultScreenLayoutTests.LocalDatabase();
        var source = new ControlledFrameSource();
        var saved = Channel.CreateUnbounded<string>();
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source), new AppOwnedLiveResultAnalyzer());
        var workflow = new DDRGpScoreViewer.Data.AppOwnedCaptureSaveWorkflowRunner();
        var events = new List<string?>();
        var run = service.RunAsync(123, new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(_ => { }),
            async (frame, observation, _, token) =>
            {
                var result = await workflow.RunCandidateAsync(frame, observation, database.ScorePath,
                    database.MasterPath, database.CatalogPath, token);
                Assert.Single(result.SavedPlayIds);
                events.Add(observation.ConfirmedEventId);
                await saved.Writer.WriteAsync(observation.Score, token);
                return LiveCandidateProcessingResult.Completed;
            });

        void Add(string kind, string number, long time) => source.Add(ResultScreenLayoutTests.Frame(
            File.ReadAllBytes(ResultScreenLayoutTests.Sample(kind, number)), time));
        Add("transition", "372", 0);
        Add("transition", "374", 1000);
        Add("result", "373", 2000);
        Add("result", "373", 3000);
        Assert.Equal("999910", await saved.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Add("result", "373", 4000);
        source.Add(ResultScreenLayoutTests.Frame([1, 2, 3], 5000));
        source.Add(ResultScreenLayoutTests.Frame([1, 2, 3], 6000));
        Add("result", "375", 7000);
        Add("result", "375", 8000);
        Assert.Equal("950700", await saved.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Add("result", "375", 9000);
        source.Complete();
        await run;
        Assert.Equal(2L, ResultScreenLayoutTests.PlayCount(database.ScorePath));
        Assert.Equal(2, events.Distinct().Count());
    }

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
        var source = new ControlledFrameSource();
        var progress = new List<CaptureSessionProgress>();
        var progressLock = new object();
        var processed = new List<string>();
        var processedTitles = new List<string>();
        var candidateCompletions = Channel.CreateUnbounded<string>();
        var processingStatusReports = 0;
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(observations));

        var run = service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(item =>
            {
                lock (progressLock)
                {
                    progress.Add(item);
                }
                if (item.StatusMessage.StartsWith(
                        "RESULT同定根拠を確認しています。",
                        StringComparison.Ordinal) &&
                    Interlocked.Increment(ref processingStatusReports) % 2 == 0)
                {
                    candidateCompletions.Writer.TryWrite(processedTitles[^1]);
                }
            }),
            (_, observation, _, _) =>
            {
                processed.Add(observation.Score);
                processedTitles.Add(observation.TitleSignature);
                return Task.FromResult(LiveCandidateProcessingResult.Completed);
            });

        var frames = Frames(
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
            13_000);
        foreach (var frame in frames.Take(4))
        {
            source.Add(frame);
        }
        Assert.Equal("song-a", await candidateCompletions.Reader.ReadAsync());

        foreach (var frame in frames.Skip(4).Take(3))
        {
            source.Add(frame);
        }
        Assert.Equal("song-b", await candidateCompletions.Reader.ReadAsync());

        foreach (var frame in frames.Skip(7).Take(3))
        {
            source.Add(frame);
        }
        Assert.Equal(string.Empty, await candidateCompletions.Reader.ReadAsync());

        foreach (var frame in frames.Skip(10))
        {
            source.Add(frame);
        }
        Assert.Equal(string.Empty, await candidateCompletions.Reader.ReadAsync());
        source.Complete();

        var result = await run;
        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(["100", "100", "100", "100"], processed);
        Assert.Equal(["song-a", "song-b", string.Empty, string.Empty], processedTitles);
        CaptureSessionProgress finalProgress;
        lock (progressLock)
        {
            finalProgress = progress[^1];
        }
        Assert.Equal(14, finalProgress.SampledFrameCount);
        Assert.Equal(10, finalProgress.ResultFrameCount);
        Assert.Equal(4, finalProgress.ConfirmedCandidateCount);
        Assert.Equal(0, finalProgress.CandidateQueueDropCount);
        Assert.True(finalProgress.DiscardedFrameCount >= 6);
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
            async (_, observation, _, _) =>
            {
                processed.Add(observation.Score);
                if (processed.Count == 1)
                {
                    firstCandidateStarted.TrySetResult();
                    await releaseFirstCandidate.Task;
                }
                return LiveCandidateProcessingResult.Completed;
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
            (_, observation, _, _) =>
            {
                processed.Add((observation.TitleSignature, observation.ConfirmedEventId));
                return Task.FromResult(LiveCandidateProcessingResult.Completed);
            });

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Single(processed);
        Assert.Equal("animated-b", processed[0].Signature);
        Assert.StartsWith("confirmed-event-v1:", processed[0].EventId);
    }

    [Fact]
    public async Task Live_monitor_retries_ambiguous_identity_with_the_same_event_id_then_completes_once()
    {
        var observations = new Queue<LiveResultObservation>(
        [
            FormalResult("100", "ambiguous-a"),
            FormalResult("100", "ambiguous-b"),
            FormalResult("100", "resolved"),
            FormalResult("100", "resolved-after-save"),
        ]);
        var source = new StubFrameSource(Frames(0, 1_000, 2_000, 3_000), frameDelayMs: 5);
        var attempts = new List<(string? EventId, bool Finalize)>();
        var completedWorkflowCount = 0;
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(observations));

        var result = await service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(_ => { }),
            (_, observation, context, _) =>
            {
                attempts.Add((observation.ConfirmedEventId, context.FinalizeUnresolved));
                if (attempts.Count == 1)
                {
                    return Task.FromResult(LiveCandidateProcessingResult.RetryIdentity);
                }
                completedWorkflowCount++;
                return Task.FromResult(LiveCandidateProcessingResult.Completed);
            });

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(2, attempts.Count);
        Assert.False(attempts[0].Finalize);
        Assert.False(attempts[1].Finalize);
        Assert.Equal(attempts[0].EventId, attempts[1].EventId);
        Assert.Equal(1, completedWorkflowCount);
    }

    [Fact]
    public async Task Live_monitor_finalizes_unresolved_once_when_result_disappears()
    {
        var observations = new Queue<LiveResultObservation>(
        [
            FormalResult("100", "ambiguous-a"),
            FormalResult("100", "ambiguous-b"),
            NonResult("grid"),
            NonResult("music-select"),
        ]);
        var source = new StubFrameSource(
            Frames(0, 1_000, 2_000, 3_000),
            frameDelayMs: 5);
        var attempts = new List<(string? EventId, bool Finalize)>();
        var unresolvedWorkflowCount = 0;
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(observations));

        var result = await service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(_ => { }),
            (_, observation, context, _) =>
            {
                attempts.Add((observation.ConfirmedEventId, context.FinalizeUnresolved));
                if (!context.FinalizeUnresolved)
                {
                    return Task.FromResult(LiveCandidateProcessingResult.RetryIdentity);
                }
                unresolvedWorkflowCount++;
                return Task.FromResult(LiveCandidateProcessingResult.Completed);
            });

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(2, attempts.Count);
        Assert.True(attempts[1].Finalize);
        Assert.Equal(attempts[0].EventId, attempts[1].EventId);
        Assert.Equal(1, unresolvedWorkflowCount);
    }

    [Fact]
    public async Task Live_monitor_bounds_identity_retry_and_finalizes_the_eighth_attempt()
    {
        var retryReady = Enumerable.Range(0, 7)
            .Select(_ => new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var observations = new Queue<LiveResultObservation>(
            Enumerable.Range(0, 13).Select(index => FormalResult("100", $"ambiguous-{index}")));
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
                12_000),
            beforeFrame: (index, token) => index is >= 2 and <= 8
                ? retryReady[index - 2].Task.WaitAsync(token)
                : Task.CompletedTask);
        var attempts = new List<(string? EventId, bool Finalize)>();
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(observations));
        var progress = new List<CaptureSessionProgress>();
        var retryIndex = 0;

        var result = await service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(value =>
            {
                progress.Add(value);
                if (value.StatusMessage.Contains(
                        "後続frameを再評価",
                        StringComparison.Ordinal) &&
                    retryIndex < retryReady.Length)
                {
                    retryReady[retryIndex++].TrySetResult();
                }
            }),
            (_, observation, context, _) =>
            {
                attempts.Add((observation.ConfirmedEventId, context.FinalizeUnresolved));
                return Task.FromResult(context.FinalizeUnresolved
                    ? LiveCandidateProcessingResult.Completed
                    : LiveCandidateProcessingResult.RetryIdentity);
            });

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(8, attempts.Count);
        Assert.False(attempts[6].Finalize);
        Assert.True(attempts[7].Finalize);
        Assert.Single(attempts.Select(item => item.EventId).Distinct());
        Assert.True(progress[^1].DiscardedFrameCount >= 4);
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
            async (_, observation, _, _) =>
            {
                processed.Add(observation.Score);
                await service.StopAsync();
                return LiveCandidateProcessingResult.Completed;
            });

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(["100"], processed);
    }

    [Theory]
    [InlineData("stop", CaptureOperationStatus.Cancelled)]
    [InlineData("cancel", CaptureOperationStatus.Cancelled)]
    [InlineData("window_closed", CaptureOperationStatus.TargetClosed)]
    public async Task Live_monitor_discards_pending_identity_retry_at_session_boundary(
        string boundary,
        CaptureOperationStatus expectedStatus)
    {
        var candidateStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new Queue<LiveResultObservation>(
        [
            FormalResult("100", "ambiguous-a"),
            FormalResult("100", "ambiguous-b"),
            NonResult("after-boundary"),
        ]);
        var source = new StubFrameSource(
            Frames(0, 1_000, 2_000),
            endReason: boundary == "window_closed"
                ? CaptureSessionEndReason.TargetClosed
                : CaptureSessionEndReason.Stopped);
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var service = new LiveMonitoringCaptureService(
            new StubTargetedAdapter(source),
            new StubResultAnalyzer(observations, candidateStarted.Task));

        var result = await service.RunAsync(
            123,
            new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
            new CallbackProgress<CaptureSessionProgress>(_ => { }),
            async (_, _, context, _) =>
            {
                calls++;
                Assert.False(context.FinalizeUnresolved);
                candidateStarted.TrySetResult();
                if (boundary == "stop")
                {
                    await service.StopAsync();
                }
                else if (boundary == "cancel")
                {
                    await cancellation.CancelAsync();
                }
                return LiveCandidateProcessingResult.RetryIdentity;
            },
            cancellation.Token);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(1, calls);
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
        new(true, score, title, "result_score_detected", HasResultStructure: true,
            ResultSceneFeature: [0.1, 0.2, 0.3]);

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
        new(false, "", "", reason, ResultSceneFeature: [0.8, 0.9, 1.0]);

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

    private sealed class ControlledFrameSource : IContinuousFrameSource, IContinuousFrameSourceMetadata
    {
        private readonly Channel<CapturedFrame> frameChannel =
            Channel.CreateUnbounded<CapturedFrame>();
        private readonly TaskCompletionSource<CaptureSessionEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CaptureSessionEndReason> Completion => completion.Task;
        public CaptureTargetInfo Target => new("fixture target", 1280, 720);

        public void Add(CapturedFrame frame) => frameChannel.Writer.TryWrite(frame);

        public void Complete(CaptureSessionEndReason endReason = CaptureSessionEndReason.Stopped)
        {
            frameChannel.Writer.TryComplete();
            completion.TrySetResult(endReason);
        }

        public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var frame in frameChannel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }

        public Task StopAsync()
        {
            Complete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubTargetedAdapter(IContinuousFrameSource source)
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
        private readonly Func<int, CancellationToken, Task>? beforeFrame;
        private readonly TaskCompletionSource<CaptureSessionEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public StubFrameSource(
            IReadOnlyList<CapturedFrame> frames,
            int frameDelayMs = 0,
            CaptureSessionEndReason endReason = CaptureSessionEndReason.Stopped,
            Func<int, CancellationToken, Task>? beforeFrame = null)
        {
            this.frames = frames;
            this.frameDelayMs = frameDelayMs;
            this.beforeFrame = beforeFrame;
            completion.TrySetResult(endReason);
        }

        public Task<CaptureSessionEndReason> Completion => completion.Task;
        public CaptureTargetInfo Target => new("fixture target", 1280, 720);

        public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < frames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (beforeFrame is not null)
                {
                    await beforeFrame(index, cancellationToken);
                }
                if (frameDelayMs > 0)
                {
                    await Task.Delay(frameDelayMs, cancellationToken);
                }
                else
                {
                    await Task.Yield();
                }
                yield return frames[index];
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
