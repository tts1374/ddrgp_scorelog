using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace DDRGpScoreViewer.Capture;

public sealed class LiveMonitoringCaptureService(
    IContinuousGraphicsCaptureAdapter captureAdapter,
    ILiveResultAnalyzer resultAnalyzer) : ILiveMonitoringCaptureService
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int DxgiDeviceRemovedHResult = unchecked((int)0x887A0005);
    private const int DxgiDeviceHungHResult = unchecked((int)0x887A0006);
    private const int DxgiDeviceResetHResult = unchecked((int)0x887A0007);
    private const int MaximumIdentityAttempts = 8;
    private readonly object stateLock = new();
    private IContinuousFrameSource? activeSource;
    private CancellationTokenSource? startupCancellation;
    private bool starting;
    private int stopRequested;

    public bool IsRunning
    {
        get
        {
            lock (stateLock)
            {
                return starting || activeSource is not null;
            }
        }
    }

    public async Task<CaptureSessionOperationResult> RunAsync(
        nint targetWindowHandle,
        CaptureTargetInfo target,
        IProgress<CaptureSessionProgress> progress,
        Func<CapturedFrame, LiveResultObservation, LiveCandidateProcessingContext,
            CancellationToken, Task<LiveCandidateProcessingResult>> processCandidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(processCandidate);

        lock (stateLock)
        {
            if (starting || activeSource is not null)
            {
                return Result(
                    CaptureOperationStatus.AlreadyRunning,
                    "連続キャプチャは既に開始済みです。");
            }

            starting = true;
            Volatile.Write(ref stopRequested, 0);
            startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        IContinuousFrameSource? source = null;
        var state = new LiveRunState(target, DateTimeOffset.UtcNow);
        var candidateQueue = Channel.CreateBounded<LiveCandidate>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var worker = ProcessCandidatesAsync(
            candidateQueue.Reader,
            processCandidate,
            state,
            progress,
            workerCancellation.Token);
        var workerAwaited = false;

        try
        {
            if (!captureAdapter.IsSupported)
            {
                return Result(
                    CaptureOperationStatus.Unsupported,
                    "このWindows環境では画面キャプチャを利用できません。");
            }
            if (captureAdapter is not ITargetedContinuousGraphicsCaptureAdapter targetedAdapter)
            {
                return Result(
                    CaptureOperationStatus.Unsupported,
                    "自動特定した対象windowへ接続できるcapture adapterが構成されていません。");
            }

            CancellationToken startupToken;
            lock (stateLock)
            {
                startupToken = startupCancellation!.Token;
            }
            source = await targetedAdapter.StartSessionForWindowAsync(
                targetWindowHandle,
                target,
                startupToken);
            if (source is null)
            {
                return Result(
                    CaptureOperationStatus.Cancelled,
                    "自動特定した対象windowを取得できなかったため、監視を開始しませんでした。");
            }

            lock (stateLock)
            {
                if (Volatile.Read(ref stopRequested) != 0)
                {
                    starting = false;
                }
                else
                {
                    activeSource = source;
                    starting = false;
                }
            }

            if (Volatile.Read(ref stopRequested) != 0)
            {
                await source.StopAsync();
                return Result(
                    CaptureOperationStatus.Cancelled,
                    "開始処理中に停止しました。解析・正式保存は開始していません。");
            }

            progress.Report(state.ToProgress("RESULT画面を検出しています。"));
            CaptureSessionEndReason? endReason = null;
            try
            {
                await foreach (var frame in source.ReadFramesAsync(cancellationToken))
                {
                    if (Volatile.Read(ref stopRequested) != 0)
                    {
                        continue;
                    }
                    state.SetTarget(new CaptureTargetInfo(
                        frame.CaptureSource,
                        frame.Width,
                        frame.Height));
                    state.IncrementFrameCount(frame.CapturedAtUtc);
                    if (!state.ShouldSample(frame.TimestampMs))
                    {
                        continue;
                    }

                    state.IncrementSampledFrameCount();
                    LiveResultObservation observation;
                    try
                    {
                        observation = await resultAnalyzer.AnalyzeAsync(
                            frame,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        state.DiscardFrame($"live解析に失敗しました。{exception.Message}");
                        progress.Report(state.ToProgress(state.StatusMessage));
                        continue;
                    }

                    if (Volatile.Read(ref stopRequested) != 0)
                    {
                        continue;
                    }
                    ProcessObservation(
                        frame,
                        observation,
                        candidateQueue.Writer,
                        state,
                        progress);
                }

                endReason = await source.Completion;
                if (endReason != CaptureSessionEndReason.Stopped)
                {
                    workerCancellation.Cancel();
                }
                candidateQueue.Writer.TryComplete();
                await worker;
                workerAwaited = true;
                if (endReason != CaptureSessionEndReason.Stopped)
                {
                    return EndReasonResult(endReason.Value);
                }
                return Result(
                    CaptureOperationStatus.Cancelled,
                    state.FrameCount == 0
                        ? "フレーム取得前に停止しました。解析・正式保存は開始していません。"
                        : "監視を停止しました。RESULT候補の処理を完了しました。");
            }
            catch
            {
                candidateQueue.Writer.TryComplete();
                workerCancellation.Cancel();
                await worker;
                workerAwaited = true;
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            return Result(
                CaptureOperationStatus.Cancelled,
                "監視をキャンセルしました。新しい解析・正式保存は開始していません。");
        }
        catch (CaptureTargetClosedException)
        {
            return EndReasonResult(CaptureSessionEndReason.TargetClosed);
        }
        catch (CaptureInvalidSizeException)
        {
            return Result(
                CaptureOperationStatus.InvalidSize,
                "対象windowのサイズが0x0のため、監視を開始しませんでした。");
        }
        catch (CaptureResizedException)
        {
            return EndReasonResult(CaptureSessionEndReason.Resized);
        }
        catch (CaptureDeviceLostException)
        {
            return EndReasonResult(CaptureSessionEndReason.DeviceLost);
        }
        catch (COMException exception) when (exception.HResult == AccessDeniedHResult)
        {
            return Result(
                CaptureOperationStatus.AccessDenied,
                "画面キャプチャへのアクセスが拒否されました。Windowsの設定を確認してください。");
        }
        catch (COMException exception) when (IsDeviceLost(exception.HResult))
        {
            return EndReasonResult(CaptureSessionEndReason.DeviceLost);
        }
        catch (UnauthorizedAccessException)
        {
            return Result(
                CaptureOperationStatus.AccessDenied,
                "画面キャプチャへのアクセスが拒否されました。Windowsの設定を確認してください。");
        }
        catch (IOException exception)
        {
            return Result(
                CaptureOperationStatus.Failed,
                $"live監視に失敗しました。{exception.Message}");
        }
        catch (Exception exception)
        {
            return Result(
                CaptureOperationStatus.Failed,
                $"live監視に失敗しました。{exception.Message}");
        }
        finally
        {
            if (!workerAwaited)
            {
                candidateQueue.Writer.TryComplete();
                workerCancellation.Cancel();
                try
                {
                    await worker;
                }
                catch (OperationCanceledException)
                {
                    // The run is already ending; the boundary result is reported above.
                }
            }

            lock (stateLock)
            {
                starting = false;
                startupCancellation?.Dispose();
                startupCancellation = null;
                if (ReferenceEquals(activeSource, source))
                {
                    activeSource = null;
                }
            }
            if (source is not null)
            {
                await source.DisposeAsync();
            }
        }
    }

    public async Task StopAsync()
    {
        IContinuousFrameSource? source;
        lock (stateLock)
        {
            source = activeSource;
            Volatile.Write(ref stopRequested, 1);
            if (starting)
            {
                startupCancellation?.Cancel();
            }
        }
        if (source is not null)
        {
            await source.StopAsync();
        }
    }

    private static void ProcessObservation(
        CapturedFrame frame,
        LiveResultObservation observation,
        ChannelWriter<LiveCandidate> candidateWriter,
        LiveRunState state,
        IProgress<CaptureSessionProgress> progress)
    {
        if (!observation.IsResultScreen)
        {
            var finalCandidate = state.ObserveNonResultScreen();
            if (finalCandidate is not null)
            {
                WriteCandidate(finalCandidate, candidateWriter, state);
            }
            progress.Report(state.ToProgress(state.StatusMessage));
            return;
        }

        state.IncrementResultFrameCount();
        if (string.IsNullOrWhiteSpace(observation.Score) &&
            observation.DigitRecognitions is null)
        {
            state.ObserveInvalidResult($"RESULT画面を検出しましたがSCOREを取得できません。{observation.Reason}");
            progress.Report(state.ToProgress(state.StatusMessage));
            return;
        }

        state.SetLatestFrame(frame);
        var candidate = state.ObserveResult(observation);
        if (candidate is null)
        {
            progress.Report(state.ToProgress(state.StatusMessage));
            return;
        }

        WriteCandidate(candidate, candidateWriter, state);
        progress.Report(state.ToProgress(state.StatusMessage));
    }

    private static void WriteCandidate(
        LiveCandidate candidate,
        ChannelWriter<LiveCandidate> candidateWriter,
        LiveRunState state)
    {
        if (candidateWriter.TryWrite(candidate))
        {
            state.IncrementConfirmedCandidateCount();
            state.IncrementPendingCandidateCount();
            state.SetStatus(
                candidate.FinalizeUnresolved
                    ? "RESULT同定根拠が未解決のままRESULTSが消失したため、保存せず未解決として記録します。"
                    : $"RESULTを確定しました。SCORE={DisplayScore(candidate.Observation)}、RESULT同定根拠を確認します。");
        }
        else
        {
            state.FailCandidate(candidate);
            state.IncrementDiscardedFrameCount();
            state.IncrementCandidateQueueDropCount();
            state.SetStatus(
                "RESULT候補の解析中です。待機枠が埋まっているため、この候補は破棄しました。");
        }
    }

    private static string DisplayScore(LiveResultObservation observation) =>
        string.IsNullOrWhiteSpace(observation.Score)
            ? "未認識"
            : observation.Score;

    private static async Task ProcessCandidatesAsync(
        ChannelReader<LiveCandidate> candidateReader,
        Func<CapturedFrame, LiveResultObservation, LiveCandidateProcessingContext,
            CancellationToken, Task<LiveCandidateProcessingResult>> processCandidate,
        LiveRunState state,
        IProgress<CaptureSessionProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var queuedCandidate in candidateReader.ReadAllAsync(cancellationToken))
            {
                state.DecrementPendingCandidateCount();
                LiveCandidate? candidate = queuedCandidate;
                while (candidate is not null)
                {
                    state.SetActiveCandidate(true);
                    state.SetStatus(candidate.FinalizeUnresolved
                        ? "未解決のRESULT候補を保存せず記録しています。"
                        : $"RESULT同定根拠を確認しています。SCORE={DisplayScore(candidate.Observation)}");
                    progress.Report(state.ToProgress(state.StatusMessage));
                    try
                    {
                        var result = await processCandidate(
                            candidate.Frame,
                            candidate.Observation,
                            new LiveCandidateProcessingContext(candidate.FinalizeUnresolved),
                            cancellationToken);
                        candidate = state.CompleteCandidate(candidate, result);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        if (candidate is not null)
                        {
                            state.FailCandidate(candidate);
                        }
                        state.SetStatus($"RESULT候補の処理に失敗しました。{exception.Message}");
                        candidate = null;
                    }
                    finally
                    {
                        state.SetActiveCandidate(false);
                        progress.Report(state.ToProgress(state.StatusMessage));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Abnormal capture boundaries discard in-flight live candidates.
        }
    }

    private static CaptureSessionOperationResult EndReasonResult(CaptureSessionEndReason reason) =>
        reason switch
        {
            CaptureSessionEndReason.TargetClosed => Result(
                CaptureOperationStatus.TargetClosed,
                "対象windowが終了したため、live監視を停止しました。未処理の候補は破棄しました。"),
            CaptureSessionEndReason.Resized => Result(
                CaptureOperationStatus.Resized,
                "対象windowのサイズが変わったためlive監視を停止しました。未処理の候補は破棄しました。"),
            CaptureSessionEndReason.DeviceLost => Result(
                CaptureOperationStatus.DeviceLost,
                "GPU deviceが失われたためlive監視を停止しました。未処理の候補は破棄しました。"),
            CaptureSessionEndReason.Failed => Result(
                CaptureOperationStatus.Failed,
                "capture sessionで予期しない失敗が発生したためlive監視を停止しました。"),
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static bool IsDeviceLost(int hresult) =>
        hresult is DxgiDeviceRemovedHResult or DxgiDeviceHungHResult or DxgiDeviceResetHResult;

    private static CaptureSessionOperationResult Result(
        CaptureOperationStatus status,
        string message) => new(status, message);

    private sealed record LiveCandidate(
        CapturedFrame Frame,
        LiveResultObservation Observation,
        string ResultKey,
        int Attempt,
        bool FinalizeUnresolved = false);

    private sealed class LiveRunState(CaptureTargetInfo target, DateTimeOffset startedAtUtc)
    {
        private readonly object gate = new();
        private CaptureTargetInfo target = target;
        private string statusMessage = "RESULT画面を検出しています。";
        private long? lastSampleTimestampMs;
        private string? candidateScore;
        private string? candidateEventId;
        private int candidateStreak;
        private int nonResultStreak;
        private string? activeResultKey;
        private string? inFlightResultKey;
        private string? pendingRetryResultKey;
        private string? pendingRetryEventId;
        private CapturedFrame? pendingRetryFrame;
        private LiveResultObservation? pendingRetryObservation;
        private int pendingRetryAttempt;
        private bool resultMissing;
        private int frameCount;
        private int sampledFrameCount;
        private int resultFrameCount;
        private int confirmedCandidateCount;
        private int discardedFrameCount;
        private int pendingCandidateCount;
        private int candidateQueueDropCount;
        private int activeCandidate;

        public DateTimeOffset StartedAtUtc { get; } = startedAtUtc;
        public int FrameCount => Volatile.Read(ref frameCount);
        public string StatusMessage
        {
            get
            {
                lock (gate)
                {
                    return statusMessage;
                }
            }
        }

        public void SetTarget(CaptureTargetInfo value)
        {
            lock (gate)
            {
                target = value;
            }
        }

        public void IncrementFrameCount(DateTimeOffset capturedAtUtc)
        {
            Interlocked.Increment(ref frameCount);
            lock (gate)
            {
                latestEventAtUtc = capturedAtUtc;
            }
        }

        public void IncrementSampledFrameCount() => Interlocked.Increment(ref sampledFrameCount);
        public void IncrementResultFrameCount() => Interlocked.Increment(ref resultFrameCount);
        public void IncrementConfirmedCandidateCount() =>
            Interlocked.Increment(ref confirmedCandidateCount);
        public void IncrementDiscardedFrameCount() =>
            Interlocked.Increment(ref discardedFrameCount);
        public void IncrementPendingCandidateCount() =>
            Interlocked.Increment(ref pendingCandidateCount);
        public void DecrementPendingCandidateCount() =>
            Interlocked.Decrement(ref pendingCandidateCount);
        public void IncrementCandidateQueueDropCount() =>
            Interlocked.Increment(ref candidateQueueDropCount);

        public void DiscardFrame(string message)
        {
            lock (gate)
            {
                statusMessage = message;
            }
            Interlocked.Increment(ref discardedFrameCount);
        }

        public void SetActiveCandidate(bool value) =>
            Volatile.Write(ref activeCandidate, value ? 1 : 0);

        public bool ShouldSample(long timestampMs)
        {
            lock (gate)
            {
                if (lastSampleTimestampMs is { } previous && timestampMs - previous < 1_000)
                {
                    return false;
                }
                lastSampleTimestampMs = timestampMs;
                return true;
            }
        }

        public LiveCandidate? ObserveNonResultScreen()
        {
            lock (gate)
            {
                candidateScore = null;
                candidateEventId = null;
                candidateStreak = 0;
                nonResultStreak++;
                if (nonResultStreak >= 2)
                {
                    activeResultKey = null;
                    resultMissing = true;
                    statusMessage = "RESULTSが2回連続で消失したため、次のRESULTを新規候補として待機しています。";
                    if (inFlightResultKey is null && pendingRetryResultKey is not null)
                    {
                        return CreateFinalUnresolvedCandidate();
                    }
                }
                else
                {
                    statusMessage = "RESULT画面ではないため、このframeを破棄しました。";
                }
            }
            Interlocked.Increment(ref discardedFrameCount);
            return null;
        }

        public void ObserveInvalidResult(string message)
        {
            lock (gate)
            {
                nonResultStreak = 0;
                resultMissing = false;
                candidateScore = null;
                candidateEventId = null;
                candidateStreak = 0;
                statusMessage = message;
            }
            Interlocked.Increment(ref discardedFrameCount);
        }

        public LiveCandidate? ObserveResult(LiveResultObservation observation)
        {
            lock (gate)
            {
                nonResultStreak = 0;
                resultMissing = false;
                var resultKey = AppOwnedResultEventFingerprint.TryCreate(
                        observation,
                        requireIdentity: false) ??
                    $"{observation.Score}\u001f{observation.TitleSignature}";
                if (activeResultKey == resultKey)
                {
                    candidateScore = null;
                    candidateStreak = 0;
                    statusMessage =
                        $"同じRESULTを検出したためduplicate候補として破棄しました。SCORE={observation.Score}";
                    Interlocked.Increment(ref discardedFrameCount);
                    return null;
                }
                if (inFlightResultKey == resultKey)
                {
                    pendingRetryFrame = LatestFrame;
                    pendingRetryObservation = observation;
                    statusMessage =
                        $"同じRESULTのRESULT同定根拠を確認中です。SCORE={observation.Score}";
                    Interlocked.Increment(ref discardedFrameCount);
                    return null;
                }
                if (pendingRetryResultKey == resultKey)
                {
                    var eventId = pendingRetryEventId ??= ConfirmedResultEventId.Create();
                    var attempt = pendingRetryAttempt + 1;
                    pendingRetryAttempt = attempt;
                    pendingRetryFrame = LatestFrame;
                    pendingRetryObservation = observation with { ConfirmedEventId = eventId };
                    inFlightResultKey = resultKey;
                    pendingRetryResultKey = null;
                    return new LiveCandidate(
                        LatestFrame,
                        pendingRetryObservation,
                        resultKey,
                        attempt,
                        FinalizeUnresolved: attempt >= MaximumIdentityAttempts);
                }

                if (candidateScore == observation.Score)
                {
                    candidateStreak++;
                }
                else
                {
                    candidateScore = observation.Score;
                    candidateEventId = ConfirmedResultEventId.Create();
                    candidateStreak = 1;
                }

                if (candidateStreak < 2)
                {
                    statusMessage =
                        $"RESULTを検出しました。SCORE={observation.Score}の安定を確認しています。";
                    return null;
                }

                var confirmedEventId = candidateEventId ??= ConfirmedResultEventId.Create();
                candidateScore = null;
                candidateEventId = null;
                candidateStreak = 0;
                inFlightResultKey = resultKey;
                pendingRetryFrame = LatestFrame;
                pendingRetryObservation = observation with { ConfirmedEventId = confirmedEventId };
                pendingRetryAttempt = 1;
                return new LiveCandidate(
                    LatestFrame,
                    pendingRetryObservation,
                    resultKey,
                    Attempt: 1);
            }
        }

        public LiveCandidate? CompleteCandidate(
            LiveCandidate candidate,
            LiveCandidateProcessingResult result)
        {
            lock (gate)
            {
                inFlightResultKey = null;
                if (result.Disposition == LiveCandidateProcessingDisposition.RetryIdentity &&
                    !candidate.FinalizeUnresolved)
                {
                    pendingRetryResultKey = candidate.ResultKey;
                    pendingRetryEventId = candidate.Observation.ConfirmedEventId;
                    pendingRetryFrame ??= candidate.Frame;
                    pendingRetryObservation ??= candidate.Observation;
                    pendingRetryAttempt = candidate.Attempt;
                    statusMessage =
                        $"RESULT同定根拠が未解決です。同じcapture eventで後続frameを再評価します。SCORE={candidate.Observation.Score}";
                    return resultMissing ? CreateFinalUnresolvedCandidate() : null;
                }

                if (!resultMissing)
                {
                    activeResultKey = candidate.ResultKey;
                }
                ClearPendingRetry();
                return null;
            }
        }

        public void FailCandidate(LiveCandidate candidate)
        {
            lock (gate)
            {
                if (inFlightResultKey == candidate.ResultKey)
                {
                    inFlightResultKey = null;
                }
                ClearPendingRetry();
            }
        }

        private LiveCandidate CreateFinalUnresolvedCandidate()
        {
            var resultKey = pendingRetryResultKey!;
            var eventId = pendingRetryEventId ?? ConfirmedResultEventId.Create();
            var observation =
                (pendingRetryObservation ?? throw new InvalidOperationException(
                    "Pending retry observation is missing.")) with
                {
                    ConfirmedEventId = eventId,
                };
            var frame = pendingRetryFrame ?? throw new InvalidOperationException(
                "Pending retry frame is missing.");
            var attempt = pendingRetryAttempt + 1;
            inFlightResultKey = resultKey;
            pendingRetryResultKey = null;
            return new LiveCandidate(
                frame,
                observation,
                resultKey,
                attempt,
                FinalizeUnresolved: true);
        }

        private void ClearPendingRetry()
        {
            pendingRetryResultKey = null;
            pendingRetryEventId = null;
            pendingRetryFrame = null;
            pendingRetryObservation = null;
            pendingRetryAttempt = 0;
        }

        // The candidate frame is assigned immediately before ObserveResult is called.
        public CapturedFrame LatestFrame { get; private set; } = null!;

        public void SetLatestFrame(CapturedFrame frame) => LatestFrame = frame;

        public void SetStatus(string message)
        {
            lock (gate)
            {
                statusMessage = message;
            }
        }

        public CaptureSessionProgress ToProgress(string message)
        {
            lock (gate)
            {
                return new CaptureSessionProgress(
                    target,
                    Volatile.Read(ref frameCount),
                    StartedAtUtc,
                    latestEventAtUtc,
                    Volatile.Read(ref sampledFrameCount),
                    Volatile.Read(ref resultFrameCount),
                    Volatile.Read(ref confirmedCandidateCount),
                    Volatile.Read(ref discardedFrameCount),
                    Math.Max(0, Volatile.Read(ref pendingCandidateCount)),
                    Volatile.Read(ref candidateQueueDropCount),
                    message);
            }
        }

        private DateTimeOffset latestEventAtUtc = startedAtUtc;
    }
}
