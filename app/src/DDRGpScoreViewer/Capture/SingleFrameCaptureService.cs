using System.IO;
using System.Runtime.InteropServices;

namespace DDRGpScoreViewer.Capture;

public sealed class SingleFrameCaptureService(
    IGraphicsCaptureAdapter captureAdapter,
    ICaptureOutputWriter outputWriter) : ISingleFrameCaptureService
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int DxgiDeviceRemovedHResult = unchecked((int)0x887A0005);
    private const int DxgiDeviceHungHResult = unchecked((int)0x887A0006);
    private const int DxgiDeviceResetHResult = unchecked((int)0x887A0007);

    public async Task<CaptureOperationResult> CaptureAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!captureAdapter.IsSupported)
            {
                return Result(
                    CaptureOperationStatus.Unsupported,
                    "このWindows環境では画面キャプチャを利用できません。");
            }
            var frame = await captureAdapter.CaptureSingleFrameAsync(
                ownerWindowHandle,
                cancellationToken);
            if (frame is null)
            {
                return Result(CaptureOperationStatus.Cancelled, "対象windowの選択をキャンセルしました。");
            }

            try
            {
                var output = await outputWriter.WriteAsync(frame, cancellationToken);
                return new CaptureOperationResult(
                    CaptureOperationStatus.Saved,
                    $"1フレームを保存しました: {output.DirectoryPath}",
                    output);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Result(
                    CaptureOperationStatus.WriteFailed,
                    $"キャプチャ画像を書き込めませんでした。部分出力は破棄しました。{exception.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            return Result(CaptureOperationStatus.Cancelled, "1フレーム取得をキャンセルしました。");
        }
        catch (CaptureTargetClosedException)
        {
            return Result(
                CaptureOperationStatus.TargetClosed,
                "選択したwindowが終了したため、フレームを保存しませんでした。");
        }
        catch (CaptureInvalidSizeException)
        {
            return Result(
                CaptureOperationStatus.InvalidSize,
                "選択したwindowのサイズが0x0のため、フレームを保存しませんでした。");
        }
        catch (CaptureResizedException)
        {
            return Result(
                CaptureOperationStatus.Resized,
                "取得中にwindowサイズが変わったため、フレームを保存しませんでした。もう一度実行してください。");
        }
        catch (CaptureDeviceLostException)
        {
            return Result(
                CaptureOperationStatus.DeviceLost,
                "GPU deviceが失われたため、フレームを保存しませんでした。もう一度実行してください。");
        }
        catch (UnauthorizedAccessException)
        {
            return Result(
                CaptureOperationStatus.AccessDenied,
                "画面キャプチャへのアクセスが拒否されました。Windowsの設定を確認してください。");
        }
        catch (COMException exception) when (exception.HResult == AccessDeniedHResult)
        {
            return Result(
                CaptureOperationStatus.AccessDenied,
                "画面キャプチャへのアクセスが拒否されました。Windowsの設定を確認してください。");
        }
        catch (COMException exception) when (IsDeviceLost(exception.HResult))
        {
            return Result(
                CaptureOperationStatus.DeviceLost,
                "GPU deviceが失われたため、フレームを保存しませんでした。もう一度実行してください。");
        }
        catch (Exception exception)
        {
            return Result(
                CaptureOperationStatus.Failed,
                $"1フレーム取得に失敗しました。出力は作成していません。{exception.Message}");
        }
    }

    private static bool IsDeviceLost(int hresult) =>
        hresult is DxgiDeviceRemovedHResult or DxgiDeviceHungHResult or DxgiDeviceResetHResult;

    private static CaptureOperationResult Result(CaptureOperationStatus status, string message) =>
        new(status, message);
}
