using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

public static class JacketObservationVersions
{
    public const string Detector = "m5c-3b-jacket-detector-v1";
    public const string Roi = "m5c-song-select-jacket-roi-v2";
    public const string FrameFeature = "m5c-jacket-rgb-grid-v1";
    public const string FrameClock = "m5c-capture-utc-clock-v1";
    public const string FeatureExtractor = "m5-jacket-v2";
    public const string LegacySessionCheckpoint = "m5c-observation-checkpoint-v1";
    public const string SessionCheckpoint = "m5c-observation-checkpoint-v2";
    public const string LegacyObservationManifest = "m5c-observation-manifest-v1";
    public const string ObservationManifest = "m5c-observation-manifest-v2";
    public const string InformationDetector = "m5c-information-title-line-detector-v1";
    public const string InformationPanelRoi = "m5c-song-select-information-panel-roi-v1";
    public const string InformationTitleLineFeature =
        "m5c-information-title-line-binary-sha256-v1";
    public const string CompositeIdentity = "m5c-jacket-title-composite-identity-v1";
}

public static class CompositeObservationIdentityBuilder
{
    public static CompositeObservationIdentity Create(
        string jacketFeatureVersion,
        string jacketFeatureHash,
        string titleLineFeatureVersion,
        string titleLineHash)
    {
        if (jacketFeatureVersion != JacketObservationVersions.FrameFeature
            || titleLineFeatureVersion != JacketObservationVersions.InformationTitleLineFeature
            || !IsSha256(jacketFeatureHash)
            || !IsSha256(titleLineHash))
        {
            throw new InvalidOperationException("composite observation feature identity is invalid");
        }
        var canonical = string.Join('\0',
            JacketObservationVersions.CompositeIdentity,
            jacketFeatureVersion,
            jacketFeatureHash,
            titleLineFeatureVersion,
            titleLineHash);
        return new CompositeObservationIdentity(
            JacketObservationVersions.CompositeIdentity,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());
    }

    public static bool IsSha256(string? value) => value is not null
        && value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public readonly record struct JacketRoi(int X, int Y, int Width, int Height)
{
    public static JacketRoi Base { get; } = new(809, 27, 149, 149);

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
    TimeSpan StableDuration,
    TitleLineFeatureObservation? TitleLineFeature = null,
    CompositeObservationIdentity? CompositeIdentity = null);

public sealed record TitleLineFeatureObservation(
    string FeatureVersion,
    string FeatureHash,
    long SourceSequence,
    DateTimeOffset CapturedAtUtc,
    string DetectorVersion,
    string RoiVersion);

public sealed record CompositeObservationIdentity(
    string IdentityVersion,
    string IdentityHash);

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
    DateTimeOffset AdoptedAtUtc,
    string? JacketFeatureVersion = null,
    string? TitleLineFeatureVersion = null,
    string? TitleLineHash = null,
    string? CompositeIdentityVersion = null,
    string? CompositeIdentityHash = null);

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
    string? PublishedArtifactPath = null,
    TitleLineFeatureObservation? TitleLineFeature = null,
    CompositeObservationIdentity? CompositeIdentity = null);

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
    Task<IReadOnlySet<CompositeObservationIdentity>> LoadCompositeIdentitySetAsync(
        ObservationSessionIdentity session,
        string catalogPath,
        CancellationToken cancellationToken = default);

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

public sealed class ObservationAlreadyCatalogedException(string identityHash)
    : InvalidOperationException("composite identity is already present in the current catalog")
{
    public string IdentityHash { get; } = identityHash;
}

public enum ObservationSavePreflightDisposition
{
    Eligible,
    CheckpointExisting,
    CatalogExisting,
}

public sealed record ObservationSavePreflight(
    ObservationSavePreflightDisposition Disposition,
    string CompositeIdentityHash);

public sealed record ObservationAdoptionResult(
    string ObservationId,
    ArtifactPublishReceipt Artifact,
    CatalogIngestReceipt Catalog,
    ObservationCheckpoint Checkpoint);
