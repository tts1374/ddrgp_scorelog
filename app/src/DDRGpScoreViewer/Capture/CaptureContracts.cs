namespace DDRGpScoreViewer.Capture;

public enum CaptureOperationStatus
{
    Saved,
    Cancelled,
    Unsupported,
    AccessDenied,
    TargetClosed,
    InvalidSize,
    Resized,
    DeviceLost,
    WriteFailed,
    Failed,
}

public sealed record CapturedFrame(
    byte[] PngBytes,
    int Width,
    int Height,
    long TimestampMs,
    DateTimeOffset CapturedAtUtc,
    string CaptureSource);

public sealed record CaptureOutput(
    string DirectoryPath,
    string ImagePath,
    string ManifestPath,
    string MetadataPath);

public sealed record CaptureOperationResult(
    CaptureOperationStatus Status,
    string UserMessage,
    CaptureOutput? Output = null);

public interface IGraphicsCaptureAdapter
{
    bool IsSupported { get; }

    Task<CapturedFrame?> CaptureSingleFrameAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default);
}

public interface ICaptureOutputWriter
{
    Task<CaptureOutput> WriteAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default);
}

public interface ISingleFrameCaptureService
{
    Task<CaptureOperationResult> CaptureAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default);
}

public abstract class CaptureBoundaryException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class CaptureTargetClosedException(string message, Exception? innerException = null)
    : CaptureBoundaryException(message, innerException);

public sealed class CaptureInvalidSizeException(string message)
    : CaptureBoundaryException(message);

public sealed class CaptureResizedException(string message)
    : CaptureBoundaryException(message);

public sealed class CaptureDeviceLostException(string message, Exception? innerException = null)
    : CaptureBoundaryException(message, innerException);
