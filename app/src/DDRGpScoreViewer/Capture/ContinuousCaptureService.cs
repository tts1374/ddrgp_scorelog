using System.IO;
using System.Runtime.InteropServices;

namespace DDRGpScoreViewer.Capture;

public sealed class ContinuousCaptureService(
    IContinuousGraphicsCaptureAdapter captureAdapter,
    ICaptureSessionOutputWriter outputWriter) : IMonitoringContinuousCaptureService
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int DxgiDeviceRemovedHResult = unchecked((int)0x887A0005);
    private const int DxgiDeviceHungHResult = unchecked((int)0x887A0006);
    private const int DxgiDeviceResetHResult = unchecked((int)0x887A0007);
    private readonly object stateLock = new();
    private IContinuousFrameSource? activeSource;
    private CancellationTokenSource? startupCancellation;
    private bool starting;
    private bool stopRequested;

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
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default) =>
        await RunCoreAsync(ownerWindowHandle, null, cancellationToken);

    public async Task<CaptureSessionOperationResult> RunAsync(
        nint ownerWindowHandle,
        IProgress<CaptureSessionProgress> progress,
        CancellationToken cancellationToken = default) =>
        await RunCoreAsync(ownerWindowHandle, progress, cancellationToken);

    private async Task<CaptureSessionOperationResult> RunCoreAsync(
        nint ownerWindowHandle,
        IProgress<CaptureSessionProgress>? progress,
        CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            if (starting || activeSource is not null)
            {
                return Result(
                    CaptureOperationStatus.AlreadyRunning,
                    "連続キャプチャは既に開始済みです。");
            }
            starting = true;
            stopRequested = false;
            startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        IContinuousFrameSource? source = null;
        try
        {
            if (!captureAdapter.IsSupported)
            {
                return Result(
                    CaptureOperationStatus.Unsupported,
                    "このWindows環境では画面キャプチャを利用できません。");
            }

            CancellationToken startupToken;
            lock (stateLock)
            {
                startupToken = startupCancellation!.Token;
            }
            source = await captureAdapter.StartSessionAsync(ownerWindowHandle, startupToken);
            if (source is null)
            {
                return Result(
                    CaptureOperationStatus.Cancelled,
                    "対象windowの選択をキャンセルしました。");
            }

            lock (stateLock)
            {
                if (stopRequested)
                {
                    starting = false;
                }
                else
                {
                    activeSource = source;
                    starting = false;
                }
            }

            if (stopRequested)
            {
                await source.StopAsync();
                return Result(
                    CaptureOperationStatus.Cancelled,
                    "開始処理中に停止しました。session outputは作成していません。");
            }

            var startedAtUtc = DateTimeOffset.UtcNow;
            var target = source is IContinuousFrameSourceMetadata metadata
                ? metadata.Target
                : new CaptureTargetInfo("選択済みwindow", 0, 0);
            progress?.Report(new CaptureSessionProgress(
                target,
                0,
                startedAtUtc,
                startedAtUtc));

            var transaction = await BeginOutputAsync(cancellationToken);
            try
            {
                await foreach (var frame in source.ReadFramesAsync(cancellationToken))
                {
                    await WriteFrameAsync(transaction, frame, cancellationToken);
                    target = new CaptureTargetInfo(frame.CaptureSource, frame.Width, frame.Height);
                    progress?.Report(new CaptureSessionProgress(
                        target,
                        transaction.FrameCount,
                        startedAtUtc,
                        frame.CapturedAtUtc));
                }

                var endReason = await source.Completion;
                if (endReason != CaptureSessionEndReason.Stopped)
                {
                    return EndReasonResult(endReason);
                }
                if (transaction.FrameCount == 0)
                {
                    return Result(
                        CaptureOperationStatus.Cancelled,
                        "フレーム取得前に停止しました。session outputは作成していません。");
                }

                var output = await CompleteOutputAsync(transaction, cancellationToken);
                return new CaptureSessionOperationResult(
                    CaptureOperationStatus.Saved,
                    $"{output.FrameCount}フレームを保存しました: {output.DirectoryPath}",
                    output);
            }
            finally
            {
                await DisposeOutputAsync(transaction);
            }
        }
        catch (OperationCanceledException)
        {
            return Result(
                CaptureOperationStatus.Cancelled,
                "連続キャプチャをキャンセルしました。session outputは作成していません。");
        }
        catch (CaptureTargetClosedException)
        {
            return EndReasonResult(CaptureSessionEndReason.TargetClosed);
        }
        catch (CaptureInvalidSizeException)
        {
            return Result(
                CaptureOperationStatus.InvalidSize,
                "対象windowのサイズが0x0のため、sessionを開始しませんでした。");
        }
        catch (CaptureResizedException)
        {
            return EndReasonResult(CaptureSessionEndReason.Resized);
        }
        catch (CaptureDeviceLostException)
        {
            return EndReasonResult(CaptureSessionEndReason.DeviceLost);
        }
        catch (IOException exception)
        {
            return Result(
                CaptureOperationStatus.WriteFailed,
                $"session outputを書き込めませんでした。部分出力は破棄しました。{exception.Message}");
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
        catch (Exception exception)
        {
            return Result(
                CaptureOperationStatus.Failed,
                $"連続キャプチャに失敗しました。session outputは作成していません。{exception.Message}");
        }
        finally
        {
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
            if (starting)
            {
                stopRequested = true;
                startupCancellation?.Cancel();
            }
        }
        if (source is not null)
        {
            await source.StopAsync();
        }
    }

    private static CaptureSessionOperationResult EndReasonResult(CaptureSessionEndReason reason) =>
        reason switch
        {
            CaptureSessionEndReason.TargetClosed => Result(
                CaptureOperationStatus.TargetClosed,
                "対象windowが終了したため、session outputは破棄しました。"),
            CaptureSessionEndReason.Resized => Result(
                CaptureOperationStatus.Resized,
                "対象windowのサイズが変わったためsessionを停止し、部分出力を破棄しました。再選択してください。"),
            CaptureSessionEndReason.DeviceLost => Result(
                CaptureOperationStatus.DeviceLost,
                "GPU deviceが失われたためsessionを停止し、部分出力を破棄しました。"),
            CaptureSessionEndReason.Failed => Result(
                CaptureOperationStatus.Failed,
                "capture sessionで予期しない失敗が発生したため、部分出力を破棄しました。"),
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static bool IsDeviceLost(int hresult) =>
        hresult is DxgiDeviceRemovedHResult or DxgiDeviceHungHResult or DxgiDeviceResetHResult;

    private async Task<ICaptureSessionOutputTransaction> BeginOutputAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await outputWriter.BeginAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw OutputAccessException(exception);
        }
    }

    private static async Task WriteFrameAsync(
        ICaptureSessionOutputTransaction transaction,
        CapturedFrame frame,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.WriteFrameAsync(frame, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw OutputAccessException(exception);
        }
    }

    private static async Task<CaptureSessionOutput> CompleteOutputAsync(
        ICaptureSessionOutputTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.CompleteAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw OutputAccessException(exception);
        }
    }

    private static async ValueTask DisposeOutputAsync(
        ICaptureSessionOutputTransaction transaction)
    {
        try
        {
            await transaction.DisposeAsync();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw OutputAccessException(exception);
        }
    }

    private static IOException OutputAccessException(UnauthorizedAccessException exception) =>
        new("Capture session output access was denied.", exception);

    private static CaptureSessionOperationResult Result(
        CaptureOperationStatus status,
        string message) => new(status, message);
}
