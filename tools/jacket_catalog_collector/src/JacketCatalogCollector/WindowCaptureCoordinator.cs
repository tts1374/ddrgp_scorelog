namespace JacketCatalogCollector;

public sealed class WindowCaptureCoordinator(
    IWindowEnumerator windowEnumerator,
    IWindowCaptureSessionFactory sessionFactory,
    ICaptureDispatcher dispatcher,
    int ringCapacity = 8)
{
    private readonly Lock sync = new();
    private readonly AsyncLocal<bool> inFrameCallback = new();
    private Task? runTask;
    private IWindowCaptureFrameSource? activeSource;
    private BoundedFrameRingBuffer? ringBuffer;
    private InFlightCallbackDrain? frameCallbackDrain;
    private WindowIdentitySnapshot? target;
    private CancellationTokenSource? startCancellation;
    private TaskCompletionSource? startFinished;
    private bool sessionClaimed;
    private bool stopRequested;
    private long capturedCount;
    private long sourceDroppedCount;
    private int currentClientWidth;
    private int currentClientHeight;
    private CaptureLifecycleSnapshot snapshot = CaptureLifecycleSnapshot.Idle;

    public event EventHandler<CaptureLifecycleSnapshot>? SnapshotChanged;
    public event EventHandler<RawCaptureFrame>? FrameReceived;

    public CaptureLifecycleSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    public async Task<bool> StartAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (sessionClaimed)
            {
                return false;
            }
            sessionClaimed = true;
            startCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startFinished = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var startToken = startCancellation.Token;

        var current = windowEnumerator.TryGetSnapshot(candidate.Identity.Handle);
        if (current is null || !candidate.Identity.IsSameTarget(current))
        {
            await PublishAsync(new CaptureLifecycleSnapshot(
                CaptureLifecycleState.Failed,
                current is null ? CaptureEndReason.TargetClosed : CaptureEndReason.IdentityChanged,
                0, 0,
                candidate.Identity.ClientWidth, candidate.Identity.ClientHeight,
                current?.ClientWidth ?? 0, current?.ClientHeight ?? 0,
                "not_created", candidate.PreviewPng,
                "候補identityが変化したため開始を拒否しました。収集開始を再実行してください。"));
            ReleaseStartClaim();
            return false;
        }
        if (!current.IsVisible || current.IsMinimized || current.ClientWidth <= 0 || current.ClientHeight <= 0)
        {
            await PublishAsync(new CaptureLifecycleSnapshot(
                CaptureLifecycleState.Failed,
                CaptureEndReason.Minimized,
                0, 0,
                current.ClientWidth, current.ClientHeight,
                current.ClientWidth, current.ClientHeight,
                "not_created", candidate.PreviewPng,
                "対象windowが非表示、最小化、または0 sizeのため開始できません。"));
            ReleaseStartClaim();
            return false;
        }
        if (!sessionFactory.IsSupported)
        {
            await PublishAsync(new CaptureLifecycleSnapshot(
                CaptureLifecycleState.Failed, CaptureEndReason.CaptureFailed,
                0, 0, current.ClientWidth, current.ClientHeight,
                current.ClientWidth, current.ClientHeight,
                "unsupported", candidate.PreviewPng,
                "Windows Graphics Captureはこの環境で利用できません。"));
            ReleaseStartClaim();
            return false;
        }

        await PublishAsync(new CaptureLifecycleSnapshot(
            CaptureLifecycleState.Starting, CaptureEndReason.None,
            0, 0, current.ClientWidth, current.ClientHeight,
            current.ClientWidth, current.ClientHeight,
            "creating", candidate.PreviewPng, "capture resourceを作成しています。"));

        IWindowCaptureFrameSource? source = null;
        try
        {
            source = await sessionFactory.StartAsync(current, startToken);
            startToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            if (source is not null)
            {
                await source.DisposeAsync();
            }
            await PublishTerminalAsync(CaptureEndReason.Cancelled, "開始を取り消しました。", "released");
            ReleaseStartClaim();
            return false;
        }
        catch (Exception exception)
        {
            await PublishTerminalAsync(CaptureEndReason.CaptureFailed, exception.Message, "released");
            ReleaseStartClaim();
            return false;
        }

        var cancelledBeforeInstall = false;
        lock (sync)
        {
            if (startToken.IsCancellationRequested)
            {
                cancelledBeforeInstall = true;
            }
            else if (runTask is not null)
            {
                throw new InvalidOperationException("A capture session started concurrently.");
            }
            else
            {
                target = current;
                ringBuffer = new BoundedFrameRingBuffer(ringCapacity);
                frameCallbackDrain = new InFlightCallbackDrain();
                activeSource = source;
                stopRequested = false;
                capturedCount = 0;
                sourceDroppedCount = 0;
                currentClientWidth = current.ClientWidth;
                currentClientHeight = current.ClientHeight;
                startCancellation?.Dispose();
                startCancellation = null;
                startFinished?.TrySetResult();
                startFinished = null;
                runTask = Task.Run(() => RunAsync(source));
            }
        }
        if (cancelledBeforeInstall)
        {
            await source.DisposeAsync();
            await PublishTerminalAsync(
                CaptureEndReason.Cancelled, "開始を取り消しました。", "released");
            ReleaseStartClaim();
            return false;
        }
        return true;
    }

    public async Task StopAsync()
    {
        if (inFrameCallback.Value)
        {
            throw new InvalidOperationException("capture stop cannot be called reentrantly from FrameReceived");
        }
        while (true)
        {
            IWindowCaptureFrameSource? source;
            Task? running;
            Task? pendingStart;
            InFlightCallbackDrain? callbacks;
            lock (sync)
            {
                source = activeSource;
                running = runTask;
                pendingStart = startFinished?.Task;
                callbacks = frameCallbackDrain;
                if (source is null || running is null)
                {
                    startCancellation?.Cancel();
                }
                else
                {
                    stopRequested = true;
                }
            }
            if (source is null || running is null)
            {
                if (pendingStart is null)
                {
                    return;
                }
                await pendingStart;
                continue;
            }
            await PublishCurrentAsync(
                CaptureLifecycleState.Stopping, CaptureEndReason.None,
                "stopping", "in-flight callbackの完了を待って停止しています。");
            var callbackDrain = callbacks?.CloseAndWaitAsync() ?? Task.CompletedTask;
            await source.StopAsync();
            await callbackDrain;
            await running;
            return;
        }
    }

    private async Task RunAsync(IWindowCaptureFrameSource source)
    {
        var reason = CaptureEndReason.CaptureFailed;
        var message = "captureが失敗しました。";
        try
        {
            await PublishCurrentAsync(
                CaptureLifecycleState.Capturing, CaptureEndReason.None,
                "active", "capture中です。disk/catalogへは出力しません。");
            await foreach (var frame in source.ReadFramesAsync())
            {
                WindowIdentitySnapshot? expected;
                bool stopping;
                lock (sync)
                {
                    expected = target;
                    stopping = stopRequested;
                }
                if (expected is null)
                {
                    reason = CaptureEndReason.Cancelled;
                    message = "capture targetが解除されました。";
                    break;
                }
                if (stopping)
                {
                    continue;
                }
                var current = windowEnumerator.TryGetSnapshot(expected.Handle);
                if (current is null)
                {
                    reason = CaptureEndReason.TargetClosed;
                    message = "対象windowが閉じられました。";
                    await source.StopAsync();
                    break;
                }
                lock (sync)
                {
                    currentClientWidth = current.ClientWidth;
                    currentClientHeight = current.ClientHeight;
                }
                if (current.ProcessId != expected.ProcessId
                    || current.ProcessStartTicks != expected.ProcessStartTicks
                    || !string.Equals(current.ProcessName, expected.ProcessName, StringComparison.Ordinal)
                    || !string.Equals(current.Title, expected.Title, StringComparison.Ordinal)
                    || !string.Equals(current.ClassName, expected.ClassName, StringComparison.Ordinal))
                {
                    reason = CaptureEndReason.IdentityChanged;
                    message = "window identityが変化しました。暗黙の再選択は行いません。";
                    await source.StopAsync();
                    break;
                }
                if (!current.IsVisible || current.IsMinimized)
                {
                    reason = CaptureEndReason.Minimized;
                    message = "対象windowが非表示または最小化されました。";
                    await source.StopAsync();
                    break;
                }
                if (current.ClientWidth != expected.ClientWidth
                    || current.ClientHeight != expected.ClientHeight)
                {
                    reason = CaptureEndReason.Resized;
                    message = "対象windowがresizeされました。暗黙の再開始は行いません。";
                    await source.StopAsync();
                    break;
                }
                InFlightCallbackDrain? callbackDrain;
                lock (sync)
                {
                    callbackDrain = frameCallbackDrain;
                    if (stopRequested || callbackDrain is null || !callbackDrain.TryEnter())
                    {
                        continue;
                    }
                    ringBuffer!.Add(frame);
                    capturedCount++;
                }
                try
                {
                    inFrameCallback.Value = true;
                    FrameReceived?.Invoke(this, frame);
                }
                catch
                {
                    // Observation is downstream of capture; a faulty observer must not
                    // bypass the capture lifecycle cleanup boundary.
                }
                finally
                {
                    inFrameCallback.Value = false;
                    callbackDrain.Exit();
                }
                await PublishCurrentAsync(
                    CaptureLifecycleState.Capturing, CaptureEndReason.None,
                    "active", "capture中です。disk/catalogへは出力しません。");
            }
            if (reason == CaptureEndReason.CaptureFailed)
            {
                reason = await source.Completion;
                message = reason switch
                {
                    CaptureEndReason.ExplicitStop => "明示停止しました。",
                    CaptureEndReason.TargetClosed => "対象windowが閉じられました。",
                    CaptureEndReason.Resized => "capture surfaceがresizeされました。",
                    CaptureEndReason.DeviceLost => "capture deviceが失われました。",
                    _ => "captureが終了しました。",
                };
            }
        }
        catch (Exception exception)
        {
            if (source.Completion.IsCompletedSuccessfully)
            {
                reason = await source.Completion;
                message = reason == CaptureEndReason.DeviceLost
                    ? "capture deviceが失われました。"
                    : exception.Message;
            }
            else
            {
                reason = CaptureEndReason.CaptureFailed;
                message = exception.Message;
            }
        }
        finally
        {
            InFlightCallbackDrain? callbacks;
            lock (sync)
            {
                callbacks = frameCallbackDrain;
            }
            if (callbacks is not null)
            {
                await callbacks.CloseAndWaitAsync();
            }
            try
            {
                await source.DisposeAsync();
            }
            catch (Exception exception)
            {
                reason = CaptureEndReason.CaptureFailed;
                message = $"capture resource解放に失敗しました: {exception.Message}";
            }
            lock (sync)
            {
                sourceDroppedCount = source.DroppedCount;
                activeSource = null;
                frameCallbackDrain = null;
                target = null;
                runTask = null;
                sessionClaimed = false;
            }
            await PublishCurrentAsync(
                reason is CaptureEndReason.ExplicitStop or CaptureEndReason.Cancelled
                    ? CaptureLifecycleState.Stopped
                    : CaptureLifecycleState.Failed,
                reason,
                "released",
                message);
        }
    }

    private Task PublishCurrentAsync(
        CaptureLifecycleState state,
        CaptureEndReason reason,
        string resourceState,
        string message)
    {
        CaptureLifecycleSnapshot value;
        lock (sync)
        {
            var latest = ringBuffer?.Latest;
            value = new CaptureLifecycleSnapshot(
                state, reason, capturedCount,
                (ringBuffer?.DroppedCount ?? 0) + (activeSource?.DroppedCount ?? sourceDroppedCount),
                target?.ClientWidth ?? snapshot.StartWidth,
                target?.ClientHeight ?? snapshot.StartHeight,
                currentClientWidth != 0 ? currentClientWidth : snapshot.CurrentWidth,
                currentClientHeight != 0 ? currentClientHeight : snapshot.CurrentHeight,
                resourceState,
                latest?.PngBytes ?? snapshot.LatestPreviewPng,
                message);
        }
        return PublishAsync(value);
    }

    private Task PublishTerminalAsync(CaptureEndReason reason, string message, string resourceState) =>
        PublishCurrentAsync(
            reason == CaptureEndReason.Cancelled ? CaptureLifecycleState.Stopped : CaptureLifecycleState.Failed,
            reason,
            resourceState,
            message);

    private Task PublishAsync(CaptureLifecycleSnapshot value) => dispatcher.InvokeAsync(() =>
    {
        lock (sync)
        {
            snapshot = value;
        }
        SnapshotChanged?.Invoke(this, value);
    });

    private void ReleaseStartClaim()
    {
        TaskCompletionSource? completion;
        lock (sync)
        {
            startCancellation?.Dispose();
            startCancellation = null;
            sessionClaimed = false;
            completion = startFinished;
            startFinished = null;
        }
        completion?.TrySetResult();
    }
}
