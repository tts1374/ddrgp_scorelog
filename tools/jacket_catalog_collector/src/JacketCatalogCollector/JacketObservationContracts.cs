using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

public static class JacketObservationVersions
{
    public const string Detector = "m5c-3b-jacket-detector-v1";
    public const string Roi = "m5c-song-select-jacket-roi-v1";
    public const string FrameFeature = "m5c-jacket-rgb-grid-v1";
    public const string FrameClock = "m5c-capture-utc-clock-v1";
    public const string FeatureExtractor = "m5-jacket-v1";
    public const string SessionCheckpoint = "m5c-observation-checkpoint-v1";
    public const string ObservationManifest = "m5c-observation-manifest-v1";
}

public readonly record struct JacketRoi(int X, int Y, int Width, int Height)
{
    public static JacketRoi Base { get; } = new(812, 28, 150, 150);

    public JacketRoi ScaleTo(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return new JacketRoi(0, 0, 0, 0);
        }
        var scaleX = frameWidth / 1280d;
        var scaleY = frameHeight / 720d;
        return new JacketRoi(
            (int)Math.Round(X * scaleX),
            (int)Math.Round(Y * scaleY),
            Math.Max(1, (int)Math.Round(Width * scaleX)),
            Math.Max(1, (int)Math.Round(Height * scaleY)));
    }

    public bool IsInside(int frameWidth, int frameHeight) =>
        X >= 0 && Y >= 0 && Width > 0 && Height > 0
        && X <= frameWidth - Width
        && Y <= frameHeight - Height;
}

public sealed record JacketDetectorOptions(
    double ChangeThreshold = 0.08,
    int StableFrameCount = 3,
    TimeSpan? MinimumStableDuration = null)
{
    public TimeSpan MinimumStableDurationValue =>
        MinimumStableDuration ?? TimeSpan.FromMilliseconds(100);

    public void Validate()
    {
        if (ChangeThreshold < 0 || ChangeThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ChangeThreshold));
        }
        if (StableFrameCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(StableFrameCount));
        }
        if (MinimumStableDurationValue < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumStableDuration));
        }
    }
}

public enum JacketDetectionState
{
    NoFrame,
    InvalidFrame,
    ChangeCandidate,
    StableCandidate,
    DuplicatePreview,
}

public sealed record JacketFeatureObservation(
    string FeatureVersion,
    string RoiVersion,
    JacketRoi Roi,
    string FeatureHash,
    double MeanAbsoluteDifference,
    int SampleWidth,
    int SampleHeight,
    string DetectorVersion = JacketObservationVersions.Detector,
    double ChangeThreshold = 0.08,
    int StableFrameCountRequired = 3,
    long MinimumStableDurationMilliseconds = 100);

public sealed record JacketObservationCandidate(
    string FeatureHash,
    RawCaptureFrame SourceFrame,
    byte[] JacketCropPng,
    JacketFeatureObservation Feature,
    int StableFrameCount,
    TimeSpan StableDuration);

public sealed record JacketDetectionResult(
    JacketDetectionState State,
    JacketObservationCandidate? Candidate,
    string Diagnostic,
    long ProcessedFrameCount,
    long InvalidFrameCount,
    long DuplicatePreviewCount)
{
    public bool HasStableCandidate => Candidate is not null
        && State is (JacketDetectionState.StableCandidate
            or JacketDetectionState.DuplicatePreview);
}

public sealed record ObservationSessionIdentity(
    string SessionId,
    string MasterVersion,
    string MasterSourceHash,
    string CatalogIdentity,
    int CatalogSchemaVersion,
    string FeatureExtractorVersion,
    string DetectorVersion,
    string RoiVersion,
    WindowIdentitySnapshot Window,
    DateTimeOffset StartedAtUtc,
    string FrameClockVersion = JacketObservationVersions.FrameClock,
    string CatalogCreatedAt = "");

public sealed record ObservationResumeRequest(
    string SessionId,
    string MasterVersion,
    string MasterSourceHash,
    string CatalogIdentity,
    int CatalogSchemaVersion,
    string FeatureExtractorVersion,
    string DetectorVersion,
    string RoiVersion,
    WindowIdentitySnapshot Window,
    string FrameClockVersion = JacketObservationVersions.FrameClock,
    string CatalogCreatedAt = "");

public sealed record ObservationCheckpointObservation(
    string ObservationId,
    string SourceImageHash,
    string JacketCropHash,
    string FeatureHash,
    string CatalogStatus,
    string? CatalogReferenceId,
    string ArtifactPath,
    DateTimeOffset AdoptedAtUtc);

public sealed record ObservationCheckpoint(
    string CheckpointVersion,
    ObservationSessionIdentity Session,
    string? LastStableFeatureHash,
    IReadOnlyList<string> StableFeatureHashes,
    long ProcessedFrameCount,
    long DroppedFrameCount,
    IReadOnlyList<ObservationCheckpointObservation> Observations,
    DateTimeOffset UpdatedAtUtc);

public sealed record ObservationArtifact(
    string ObservationId,
    ObservationSessionIdentity Session,
    RawCaptureFrame SourceFrame,
    byte[] JacketCropPng,
    JacketFeatureObservation Feature,
    string SourceImageHash,
    string JacketCropHash,
    string ObservedTitle,
    string ObservedArtist,
    string ObservationStatus,
    DateTimeOffset CreatedAtUtc,
    string? PublishedArtifactPath = null);

public enum ArtifactDisposition
{
    Created,
    Existing,
}

public sealed record ArtifactPublishReceipt(
    string ObservationId,
    string ArtifactPath,
    ArtifactDisposition Disposition,
    string SourceImageHash,
    string JacketCropHash);

public enum CatalogIngestDisposition
{
    Created,
    Existing,
    DeferredUnsupportedSchema,
    Failed,
}

public sealed record CatalogIngestReceipt(
    CatalogIngestDisposition Disposition,
    string? CatalogReferenceId,
    string Message);

public interface IObservationCheckpointStore
{
    Task SaveAsync(ObservationCheckpoint checkpoint, CancellationToken cancellationToken = default);

    Task<ObservationCheckpoint> LoadAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

}

public interface IObservationArtifactPublisher
{
    Task<ArtifactPublishReceipt> PublishAsync(
        ObservationArtifact artifact,
        ObservationCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task RollbackAsync(
        ArtifactPublishReceipt receipt,
        ObservationCheckpoint? previousCheckpoint,
        CancellationToken cancellationToken = default);
}

public interface IObservationArtifactReader
{
    Task<ObservationArtifact> LoadAsync(
        ObservationSessionIdentity session,
        string observationId,
        CancellationToken cancellationToken = default);
}

public interface IObservationCatalogAdapter
{
    Task ValidateSessionAsync(
        ObservationSessionIdentity session,
        string catalogPath,
        string masterPath,
        CancellationToken cancellationToken = default);

    Task ValidateReceiptAsync(
        ObservationCheckpointObservation observation,
        ObservationArtifact artifact,
        string catalogPath,
        CancellationToken cancellationToken = default);

    Task<CatalogIngestReceipt> IngestAsync(
        ObservationArtifact artifact,
        string sourceImagePath,
        string catalogPath,
        string masterPath,
        CancellationToken cancellationToken = default);
}

public sealed record ObservationResumeValidation(
    bool Compatible,
    string Message,
    ObservationCheckpoint? Checkpoint);

public sealed class ObservationIdentityDriftException(string message)
    : InvalidOperationException(message);

public sealed record ObservationAdoptionResult(
    string ObservationId,
    ArtifactPublishReceipt Artifact,
    CatalogIngestReceipt Catalog,
    ObservationCheckpoint Checkpoint);
