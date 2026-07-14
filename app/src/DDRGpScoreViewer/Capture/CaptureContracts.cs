namespace DDRGpScoreViewer.Capture;

public enum CaptureOperationStatus
{
    Saved,
    AlreadyRunning,
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

public enum CaptureSessionEndReason
{
    Stopped,
    TargetClosed,
    Resized,
    DeviceLost,
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

public sealed record CaptureSessionOutput(
    string DirectoryPath,
    string ManifestPath,
    string MetadataPath,
    int FrameCount);

public sealed record CaptureSessionOperationResult(
    CaptureOperationStatus Status,
    string UserMessage,
    CaptureSessionOutput? Output = null);

public sealed record CaptureTargetInfo(
    string DisplayName,
    int Width,
    int Height);

public sealed record CaptureSessionProgress(
    CaptureTargetInfo Target,
    int FrameCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LatestEventAtUtc);

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

public interface IContinuousGraphicsCaptureAdapter
{
    bool IsSupported { get; }

    Task<IContinuousFrameSource?> StartSessionAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default);
}

public interface IContinuousFrameSource : IAsyncDisposable
{
    IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
        CancellationToken cancellationToken = default);

    Task<CaptureSessionEndReason> Completion { get; }

    Task StopAsync();
}

public interface IContinuousFrameSourceMetadata
{
    CaptureTargetInfo Target { get; }
}

public interface ICaptureSessionOutputWriter
{
    Task<ICaptureSessionOutputTransaction> BeginAsync(
        CancellationToken cancellationToken = default);
}

public interface ICaptureSessionOutputTransaction : IAsyncDisposable
{
    int FrameCount { get; }

    Task WriteFrameAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default);

    Task<CaptureSessionOutput> CompleteAsync(
        CancellationToken cancellationToken = default);
}

public interface IContinuousCaptureService
{
    bool IsRunning { get; }

    Task<CaptureSessionOperationResult> RunAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default);

    Task StopAsync();
}

public interface IMonitoringContinuousCaptureService : IContinuousCaptureService
{
    Task<CaptureSessionOperationResult> RunAsync(
        nint ownerWindowHandle,
        IProgress<CaptureSessionProgress> progress,
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
