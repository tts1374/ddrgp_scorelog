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
    DateTimeOffset LatestEventAtUtc,
    int SampledFrameCount = 0,
    int ResultFrameCount = 0,
    int ConfirmedCandidateCount = 0,
    int DiscardedFrameCount = 0,
    int PendingCandidateCount = 0,
    int CandidateQueueDropCount = 0,
    string StatusMessage = "");

/// <summary>
/// Explicit app-owned evidence that has already crossed the identity/result-field
/// adoption boundary. Candidate digits and preview values are intentionally not
/// represented here.
/// </summary>
public sealed record AppOwnedFormalEvidence(
    string? MasterVersion,
    string? SongId,
    string? ChartId,
    int? Score,
    int? MaxCombo,
    int? Marvelous,
    int? Perfect,
    int? Great,
    int? Good,
    int? Miss,
    int? ExScore,
    string? Rank,
    string? ClearType,
    string? FlareRank,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyDictionary<string, double?> Confidences,
    string IdentitySignalStatus = "resolved");

public sealed record LiveResultObservation(
    bool IsResultScreen,
    string Score,
    string TitleSignature,
    string Reason,
    IReadOnlyDictionary<string, Runtime.M7aDigitRecognitionResult>? DigitRecognitions = null,
    string DigitRecognitionStatus = "not_evaluated",
    AppOwnedFormalEvidence? FormalEvidence = null);

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

public interface ITargetedContinuousGraphicsCaptureAdapter
{
    Task<IContinuousFrameSource?> StartSessionForWindowAsync(
        nint targetWindowHandle,
        CaptureTargetInfo target,
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

public interface ILiveResultAnalyzer
{
    Task<LiveResultObservation> AnalyzeAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default);
}

public interface ILiveMonitoringCaptureService
{
    bool IsRunning { get; }

    Task<CaptureSessionOperationResult> RunAsync(
        nint targetWindowHandle,
        CaptureTargetInfo target,
        IProgress<CaptureSessionProgress> progress,
        Func<CapturedFrame, LiveResultObservation, CancellationToken, Task> processCandidate,
        CancellationToken cancellationToken = default);

    Task StopAsync();
}

public interface ITargetedMonitoringContinuousCaptureService
{
    Task<CaptureSessionOperationResult> RunAsync(
        nint targetWindowHandle,
        CaptureTargetInfo target,
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
