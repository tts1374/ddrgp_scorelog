using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

public sealed class AtomicObservationCheckpointStore(string evidenceRoot)
    : IObservationCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string root = Path.GetFullPath(evidenceRoot);

    public Task SaveAsync(
        ObservationCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessionDirectory = SessionDirectory(checkpoint.Session.SessionId);
        Directory.CreateDirectory(sessionDirectory);
        var target = Path.Combine(sessionDirectory, "checkpoint.json");
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            WriteJson(temporary, ToDocument(checkpoint));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
            return Task.CompletedTask;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public async Task<ObservationCheckpoint> LoadAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(SessionDirectory(sessionId), "checkpoint.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("observation checkpoint does not exist");
        }
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            var document = JsonSerializer.Deserialize<CheckpointDocument>(json, JsonOptions)
                ?? throw new InvalidOperationException("observation checkpoint is empty");
            var checkpoint = FromDocument(document);
            ValidateObservationPaths(checkpoint);
            return checkpoint;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("observation checkpoint is corrupt", exception);
        }
    }

    internal string SessionDirectory(string sessionId)
    {
        ValidateSessionId(sessionId);
        return Path.Combine(root, sessionId);
    }

    internal static void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || sessionId.Length > 128
            || sessionId.Any(character => !char.IsLetterOrDigit(character) && character != '-'
                && character != '_'))
        {
            throw new InvalidOperationException("observation session id is invalid");
        }
    }

    internal static void ValidateObservationId(string observationId)
    {
        if (observationId.Length != 64
            || observationId.Any(character =>
                character is not (>= '0' and <= '9')
                && character is not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException("observation id must be a lowercase SHA-256 value");
        }
    }

    internal static bool IsSha256(string? value) => value is not null && value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private void ValidateObservationPaths(ObservationCheckpoint checkpoint)
    {
        var observationsDirectory = Path.Combine(
            SessionDirectory(checkpoint.Session.SessionId), "observations");
        foreach (var observation in checkpoint.Observations)
        {
            ValidateObservationId(observation.ObservationId);
            var expected = Path.GetFullPath(Path.Combine(
                observationsDirectory, observation.ObservationId));
            if (!string.Equals(
                    Path.GetFullPath(observation.ArtifactPath),
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "observation checkpoint artifact path escapes its session observation directory");
            }
        }
    }

    internal static void WriteJson<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions) + "\n";
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static T ReadJson<T>(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException("observation artifact JSON is empty");
    }

    internal static string CanonicalJson<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    internal static CheckpointDocument ToDocument(ObservationCheckpoint value) => new()
    {
        CheckpointVersion = value.CheckpointVersion,
        Session = ToDocument(value.Session),
        LastStableFeatureHash = value.LastStableFeatureHash,
        StableFeatureHashes = value.StableFeatureHashes.ToList(),
        ProcessedFrameCount = value.ProcessedFrameCount,
        DroppedFrameCount = value.DroppedFrameCount,
        Observations = value.Observations.Select(item => new CheckpointObservationDocument
        {
            ObservationId = item.ObservationId,
            SourceImageHash = item.SourceImageHash,
            JacketCropHash = item.JacketCropHash,
            FeatureHash = item.FeatureHash,
            CatalogStatus = item.CatalogStatus,
            CatalogReferenceId = item.CatalogReferenceId,
            ArtifactPath = item.ArtifactPath,
            AdoptedAtUtc = item.AdoptedAtUtc,
        }).ToList(),
        UpdatedAtUtc = value.UpdatedAtUtc,
    };

    internal static ObservationCheckpoint FromDocument(CheckpointDocument value)
    {
        if (value.CheckpointVersion != JacketObservationVersions.SessionCheckpoint)
        {
            throw new InvalidOperationException("observation checkpoint version is unsupported");
        }
        if (value.ProcessedFrameCount < 0 || value.DroppedFrameCount < 0)
        {
            throw new InvalidOperationException("observation checkpoint progress is invalid");
        }
        var observations = value.Observations
            ?? throw new InvalidOperationException("observation checkpoint observations are missing");
        var stableFeatureHashes = value.StableFeatureHashes
            ?? throw new InvalidOperationException("observation checkpoint stable feature hashes are missing");
        if (stableFeatureHashes.Any(value => !IsSha256(value))
            || stableFeatureHashes.Distinct(StringComparer.Ordinal).Count()
                != stableFeatureHashes.Count
            || value.LastStableFeatureHash is not null
                && !stableFeatureHashes.Contains(value.LastStableFeatureHash, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("observation checkpoint stable feature hashes are invalid");
        }
        if (observations.Any(item => item is null))
        {
            throw new InvalidOperationException("observation checkpoint contains a null observation");
        }
        if (observations.GroupBy(item => item.ObservationId, StringComparer.Ordinal)
            .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1))
        {
            throw new InvalidOperationException("observation checkpoint observation ids are invalid");
        }
        if (observations.Any(item => item.CatalogStatus is not ("pending" or "ingested" or "deferred")
            || !IsSha256(item.SourceImageHash)
            || !IsSha256(item.JacketCropHash)
            || !IsSha256(item.FeatureHash)
            || !stableFeatureHashes.Contains(item.FeatureHash, StringComparer.Ordinal)
            || string.IsNullOrWhiteSpace(item.ArtifactPath)
            || item.CatalogStatus == "ingested" && string.IsNullOrWhiteSpace(item.CatalogReferenceId)
            || item.CatalogStatus is "pending" or "deferred" && item.CatalogReferenceId is not null))
        {
            throw new InvalidOperationException("observation checkpoint observation is invalid");
        }
        return new ObservationCheckpoint(
            value.CheckpointVersion,
            FromDocument(value.Session),
            value.LastStableFeatureHash,
            stableFeatureHashes.ToList(),
            value.ProcessedFrameCount,
            value.DroppedFrameCount,
            observations.Select(item => new ObservationCheckpointObservation(
                item.ObservationId,
                item.SourceImageHash,
                item.JacketCropHash,
                item.FeatureHash,
                item.CatalogStatus,
                item.CatalogReferenceId,
                item.ArtifactPath,
                item.AdoptedAtUtc)).ToList(),
            value.UpdatedAtUtc);
    }

    internal static SessionDocument ToDocument(ObservationSessionIdentity value) => new()
    {
        SessionId = value.SessionId,
        MasterVersion = value.MasterVersion,
        MasterSourceHash = value.MasterSourceHash,
        CatalogIdentity = value.CatalogIdentity,
        CatalogSchemaVersion = value.CatalogSchemaVersion,
        CatalogCreatedAt = value.CatalogCreatedAt,
        FeatureExtractorVersion = value.FeatureExtractorVersion,
        DetectorVersion = value.DetectorVersion,
        RoiVersion = value.RoiVersion,
        FrameClockVersion = value.FrameClockVersion,
        Window = ToDocument(value.Window),
        StartedAtUtc = value.StartedAtUtc,
    };

    internal static ObservationSessionIdentity FromDocument(SessionDocument value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("observation checkpoint session is missing");
        }
        if (string.IsNullOrWhiteSpace(value.SessionId)
            || string.IsNullOrWhiteSpace(value.MasterVersion)
            || string.IsNullOrWhiteSpace(value.MasterSourceHash)
            || string.IsNullOrWhiteSpace(value.CatalogIdentity)
            || value.CatalogSchemaVersion <= 0
            || string.IsNullOrWhiteSpace(value.CatalogCreatedAt)
            || string.IsNullOrWhiteSpace(value.FeatureExtractorVersion)
            || string.IsNullOrWhiteSpace(value.DetectorVersion)
            || string.IsNullOrWhiteSpace(value.RoiVersion)
            || string.IsNullOrWhiteSpace(value.FrameClockVersion))
        {
            throw new InvalidOperationException("observation checkpoint identity is incomplete");
        }
        ValidateSessionId(value.SessionId);
        return new ObservationSessionIdentity(
            value.SessionId,
            value.MasterVersion,
            value.MasterSourceHash,
            value.CatalogIdentity,
            value.CatalogSchemaVersion,
            value.FeatureExtractorVersion,
            value.DetectorVersion,
            value.RoiVersion,
            FromDocument(value.Window),
            value.StartedAtUtc,
            value.FrameClockVersion,
            value.CatalogCreatedAt);
    }

    internal static WindowDocument ToDocument(WindowIdentitySnapshot value) => new()
    {
        Handle = $"0x{value.Handle.ToInt64():X}",
        ProcessId = value.ProcessId,
        ProcessStartTicks = value.ProcessStartTicks,
        ProcessName = value.ProcessName,
        Title = value.Title,
        ClassName = value.ClassName,
        ClientWidth = value.ClientWidth,
        ClientHeight = value.ClientHeight,
        IsVisible = value.IsVisible,
        IsMinimized = value.IsMinimized,
    };

    internal static WindowIdentitySnapshot FromDocument(WindowDocument value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("observation checkpoint window is missing");
        }
        if (string.IsNullOrWhiteSpace(value.Handle)
            || !long.TryParse(value.Handle.Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var handle)
            || value.ProcessId <= 0
            || value.ProcessStartTicks <= 0
            || string.IsNullOrWhiteSpace(value.ProcessName)
            || value.ClientWidth <= 0
            || value.ClientHeight <= 0)
        {
            throw new InvalidOperationException("observation checkpoint window identity is invalid");
        }
        return new WindowIdentitySnapshot(
            (nint)handle,
            value.ProcessId,
            value.ProcessStartTicks,
            value.ProcessName,
            value.Title,
            value.ClassName,
            value.ClientWidth,
            value.ClientHeight,
            value.IsVisible,
            value.IsMinimized);
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = null,
    };
}

public sealed class AtomicObservationArtifactPublisher(string evidenceRoot)
    : IObservationArtifactPublisher, IObservationArtifactReader
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string root = Path.GetFullPath(evidenceRoot);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<ArtifactPublishReceipt> PublishAsync(
        ObservationArtifact artifact,
        ObservationCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ValidateArtifact(artifact, checkpoint);
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(root);
            var store = new AtomicObservationCheckpointStore(root);
            var sessionDirectory = store.SessionDirectory(artifact.Session.SessionId);
            var observationsDirectory = Path.Combine(sessionDirectory, "observations");
            var observationDirectory = Path.Combine(observationsDirectory, artifact.ObservationId);
            var documentPath = Path.Combine(observationDirectory, "observation.json");
            var sessionDocumentPath = Path.Combine(sessionDirectory, "session.json");
            if (Directory.Exists(sessionDirectory))
            {
                if (!File.Exists(sessionDocumentPath)
                    || !File.Exists(Path.Combine(sessionDirectory, "checkpoint.json")))
                {
                    throw new InvalidOperationException("observation session directory is incomplete");
                }
                var existingSession = AtomicObservationCheckpointStore.ReadJson<SessionDocument>(
                    sessionDocumentPath);
                if (!string.Equals(
                        AtomicObservationCheckpointStore.CanonicalJson(existingSession),
                        AtomicObservationCheckpointStore.CanonicalJson(
                            AtomicObservationCheckpointStore.ToDocument(artifact.Session)),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("observation session identity drift was detected");
                }
            }
            var checkpointToPublish = checkpoint with
            {
                Observations = checkpoint.Observations.Select(item => item.ObservationId == artifact.ObservationId
                    ? item with { ArtifactPath = observationDirectory }
                    : item).ToList(),
            };
            if (File.Exists(documentPath))
            {
                var existing = AtomicObservationCheckpointStore.ReadJson<ObservationDocument>(documentPath);
                if (!string.Equals(
                        AtomicObservationCheckpointStore.CanonicalJson(existing),
                        AtomicObservationCheckpointStore.CanonicalJson(ToDocument(artifact)),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "observation id is already used with a different payload");
                }
                await LoadAsync(artifact.Session, artifact.ObservationId, cancellationToken);
                await store.SaveAsync(checkpointToPublish, cancellationToken);
                return Receipt(artifact, observationDirectory, ArtifactDisposition.Existing);
            }
            var stagingDirectory = Path.Combine(
                root,
                ".staging-" + Guid.NewGuid().ToString("N"),
                artifact.Session.SessionId,
                "observations",
                artifact.ObservationId);
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                File.WriteAllBytes(Path.Combine(stagingDirectory, "source.png"), artifact.SourceFrame.PngBytes);
                File.WriteAllBytes(Path.Combine(stagingDirectory, "jacket-crop.png"), artifact.JacketCropPng);
                AtomicObservationCheckpointStore.WriteJson(
                    Path.Combine(stagingDirectory, "observation.json"),
                    ToDocument(artifact));
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(sessionDirectory))
                {
                    var sessionStaging = Directory.GetParent(
                        Directory.GetParent(stagingDirectory)!.FullName)!.FullName;
                    Directory.CreateDirectory(sessionStaging);
                    AtomicObservationCheckpointStore.WriteJson(
                        Path.Combine(sessionStaging, "session.json"),
                        AtomicObservationCheckpointStore.ToDocument(artifact.Session));
                    AtomicObservationCheckpointStore.WriteJson(
                        Path.Combine(sessionStaging, "checkpoint.json"),
                        AtomicObservationCheckpointStore.ToDocument(checkpointToPublish));
                    Directory.Move(sessionStaging, sessionDirectory);
                }
                else
                {
                    Directory.CreateDirectory(observationsDirectory);
                    Directory.Move(stagingDirectory, observationDirectory);
                    try
                    {
                        await store.SaveAsync(checkpointToPublish, cancellationToken);
                    }
                    catch
                    {
                        Directory.Delete(observationDirectory, recursive: true);
                        throw;
                    }
                }
                return Receipt(artifact, observationDirectory, ArtifactDisposition.Created);
            }
            finally
            {
                var stagingRoot = Directory.GetParent(
                    Directory.GetParent(
                        Directory.GetParent(stagingDirectory)!.FullName)!.FullName)!.FullName;
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ObservationArtifact> LoadAsync(
        ObservationSessionIdentity session,
        string observationId,
        CancellationToken cancellationToken = default)
    {
        var store = new AtomicObservationCheckpointStore(root);
        var observationDirectory = Path.Combine(
            store.SessionDirectory(session.SessionId), "observations", observationId);
        var document = AtomicObservationCheckpointStore.ReadJson<ObservationDocument>(
            Path.Combine(observationDirectory, "observation.json"));
        cancellationToken.ThrowIfCancellationRequested();
        var sourceBytes = await File.ReadAllBytesAsync(
            Path.Combine(observationDirectory, "source.png"), cancellationToken);
        var cropBytes = await File.ReadAllBytesAsync(
            Path.Combine(observationDirectory, "jacket-crop.png"), cancellationToken);
        if (Hash(sourceBytes) != document.SourceImageHash || Hash(cropBytes) != document.JacketCropHash)
        {
            throw new InvalidOperationException("observation artifact image hash mismatch");
        }
        var expectedRoi = JacketRoi.Base.ScaleTo(document.SourceWidth, document.SourceHeight);
        if (document.ManifestVersion != JacketObservationVersions.ObservationManifest
            || document.SessionId != session.SessionId
            || document.ObservationId != observationId
            || document.SourceImage != "source.png"
            || document.JacketCrop != "jacket-crop.png"
            || document.SourceWidth <= 0
            || document.SourceHeight <= 0
            || document.RoiWidth <= 0
            || document.RoiHeight <= 0
            || string.IsNullOrWhiteSpace(document.FeatureVersion)
            || string.IsNullOrWhiteSpace(document.RoiVersion)
            || string.IsNullOrWhiteSpace(document.MasterVersion)
            || string.IsNullOrWhiteSpace(document.MasterSourceHash)
            || string.IsNullOrWhiteSpace(document.CatalogIdentity)
            || document.CatalogSchemaVersion <= 0
            || string.IsNullOrWhiteSpace(document.CatalogCreatedAt)
            || string.IsNullOrWhiteSpace(document.FeatureExtractorVersion)
            || string.IsNullOrWhiteSpace(document.DetectorVersion)
            || string.IsNullOrWhiteSpace(document.FrameClockVersion)
            || !AtomicObservationCheckpointStore.IsSha256(document.FeatureHash)
            || document.FeatureVersion != JacketObservationVersions.FrameFeature
            || document.SampleWidth != 16
            || document.SampleHeight != 16
            || new JacketRoi(document.RoiX, document.RoiY, document.RoiWidth, document.RoiHeight) != expectedRoi
            || document.ObservedTitle != ""
            || document.ObservedArtist != ""
            || document.ObservationStatus != "unresolved"
            || document.SourceSequence < 0
            || document.CapturedAtUtc == default
            || document.CreatedAtUtc == default
            || document.CreatedAtUtc < document.CapturedAtUtc
            || !double.IsFinite(document.ChangeThreshold)
            || document.ChangeThreshold < 0
            || document.ChangeThreshold > 1
            || document.StableFrameCountRequired < 2
            || document.MinimumStableDurationMilliseconds < 0
            || !double.IsFinite(document.MeanAbsoluteDifference))
        {
            throw new InvalidOperationException("observation artifact manifest is invalid");
        }
        if (document.MasterVersion != session.MasterVersion
            || document.MasterSourceHash != session.MasterSourceHash
            || document.CatalogIdentity != session.CatalogIdentity
            || document.CatalogSchemaVersion != session.CatalogSchemaVersion
            || document.CatalogCreatedAt != session.CatalogCreatedAt
            || document.FeatureExtractorVersion != session.FeatureExtractorVersion
            || document.DetectorVersion != session.DetectorVersion
            || document.RoiVersion != session.RoiVersion
            || document.FeatureVersion != JacketObservationVersions.FrameFeature
            || document.FrameClockVersion != session.FrameClockVersion
            || !string.Equals(
                AtomicObservationCheckpointStore.CanonicalJson(document.Window),
                AtomicObservationCheckpointStore.CanonicalJson(
                    AtomicObservationCheckpointStore.ToDocument(session.Window)),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("observation artifact identity drift was detected");
        }
        return new ObservationArtifact(
            document.ObservationId,
            session,
            new RawCaptureFrame(
                sourceBytes,
                document.SourceWidth,
                document.SourceHeight,
                document.SourceSequence,
                document.CapturedAtUtc),
            cropBytes,
            new JacketFeatureObservation(
                document.FeatureVersion,
                document.RoiVersion,
                new JacketRoi(document.RoiX, document.RoiY, document.RoiWidth, document.RoiHeight),
                document.FeatureHash,
                document.MeanAbsoluteDifference,
                document.SampleWidth,
                document.SampleHeight,
                document.DetectorVersion,
                document.ChangeThreshold,
                document.StableFrameCountRequired,
                document.MinimumStableDurationMilliseconds),
            document.SourceImageHash,
            document.JacketCropHash,
            document.ObservedTitle,
            document.ObservedArtist,
            document.ObservationStatus,
            document.CreatedAtUtc,
            observationDirectory);
    }

    public async Task RollbackAsync(
        ArtifactPublishReceipt receipt,
        ObservationCheckpoint? previousCheckpoint,
        CancellationToken cancellationToken = default)
    {
        if (receipt.Disposition == ArtifactDisposition.Existing)
        {
            if (previousCheckpoint is not null)
            {
                await new AtomicObservationCheckpointStore(root).SaveAsync(
                    previousCheckpoint, cancellationToken);
            }
            return;
        }
        await gate.WaitAsync(cancellationToken);
        try
        {
            var observationDirectory = Path.GetFullPath(receipt.ArtifactPath);
            var observationsDirectory = Directory.GetParent(observationDirectory)?.FullName
                ?? throw new InvalidOperationException("artifact rollback path is invalid");
            var sessionDirectory = Directory.GetParent(observationsDirectory)?.FullName
                ?? throw new InvalidOperationException("artifact rollback session path is invalid");
            if (!string.Equals(
                    Directory.GetParent(sessionDirectory)?.FullName,
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("artifact rollback path escapes evidence root");
            }
            if (previousCheckpoint is null)
            {
                Directory.Delete(sessionDirectory, recursive: true);
                return;
            }
            if (previousCheckpoint.Observations.Any(item => item.ObservationId == receipt.ObservationId))
            {
                throw new InvalidOperationException("artifact rollback would remove an existing observation");
            }
            var rollbackDirectory = observationDirectory + ".rollback-" + Guid.NewGuid().ToString("N");
            Directory.Move(observationDirectory, rollbackDirectory);
            try
            {
                await new AtomicObservationCheckpointStore(root).SaveAsync(
                    previousCheckpoint, cancellationToken);
                Directory.Delete(rollbackDirectory, recursive: true);
            }
            catch
            {
                if (Directory.Exists(rollbackDirectory) && !Directory.Exists(observationDirectory))
                {
                    Directory.Move(rollbackDirectory, observationDirectory);
                }
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static void ValidateArtifact(
        ObservationArtifact artifact,
        ObservationCheckpoint checkpoint)
    {
        AtomicObservationCheckpointStore.ValidateSessionId(artifact.Session.SessionId);
        AtomicObservationCheckpointStore.ValidateObservationId(artifact.ObservationId);
        if (artifact.Session != checkpoint.Session
            || string.IsNullOrWhiteSpace(artifact.ObservationId)
            || artifact.SourceFrame.PngBytes.Length == 0
            || artifact.JacketCropPng.Length == 0
            || string.IsNullOrWhiteSpace(artifact.SourceImageHash)
            || string.IsNullOrWhiteSpace(artifact.JacketCropHash)
            || string.IsNullOrWhiteSpace(artifact.Feature.FeatureHash))
        {
            throw new InvalidOperationException("observation artifact/checkpoint identity is invalid");
        }
        if (!checkpoint.Observations.Any(item =>
                item.ObservationId == artifact.ObservationId
                && item.SourceImageHash == artifact.SourceImageHash
                && item.JacketCropHash == artifact.JacketCropHash
                && item.FeatureHash == artifact.Feature.FeatureHash))
        {
            throw new InvalidOperationException("observation checkpoint does not contain the artifact");
        }
    }

    private static ArtifactPublishReceipt Receipt(
        ObservationArtifact artifact,
        string observationDirectory,
        ArtifactDisposition disposition) => new(
        artifact.ObservationId,
        observationDirectory,
        disposition,
        artifact.SourceImageHash,
        artifact.JacketCropHash);

    private static ObservationDocument ToDocument(ObservationArtifact value) => new()
    {
        ManifestVersion = JacketObservationVersions.ObservationManifest,
        SessionId = value.Session.SessionId,
        ObservationId = value.ObservationId,
        SourceImage = "source.png",
        JacketCrop = "jacket-crop.png",
        SourceImageHash = value.SourceImageHash,
        JacketCropHash = value.JacketCropHash,
        SourceWidth = value.SourceFrame.Width,
        SourceHeight = value.SourceFrame.Height,
        SourceSequence = value.SourceFrame.Sequence,
        CapturedAtUtc = value.SourceFrame.CapturedAtUtc,
        FeatureVersion = value.Feature.FeatureVersion,
        RoiVersion = value.Feature.RoiVersion,
        MasterVersion = value.Session.MasterVersion,
        MasterSourceHash = value.Session.MasterSourceHash,
        CatalogIdentity = value.Session.CatalogIdentity,
        CatalogSchemaVersion = value.Session.CatalogSchemaVersion,
        CatalogCreatedAt = value.Session.CatalogCreatedAt,
        FeatureExtractorVersion = value.Session.FeatureExtractorVersion,
        DetectorVersion = value.Feature.DetectorVersion,
        FrameClockVersion = value.Session.FrameClockVersion,
        Window = AtomicObservationCheckpointStore.ToDocument(value.Session.Window),
        ChangeThreshold = value.Feature.ChangeThreshold,
        StableFrameCountRequired = value.Feature.StableFrameCountRequired,
        MinimumStableDurationMilliseconds = value.Feature.MinimumStableDurationMilliseconds,
        RoiX = value.Feature.Roi.X,
        RoiY = value.Feature.Roi.Y,
        RoiWidth = value.Feature.Roi.Width,
        RoiHeight = value.Feature.Roi.Height,
        FeatureHash = value.Feature.FeatureHash,
        MeanAbsoluteDifference = value.Feature.MeanAbsoluteDifference,
        SampleWidth = value.Feature.SampleWidth,
        SampleHeight = value.Feature.SampleHeight,
        ObservedTitle = value.ObservedTitle,
        ObservedArtist = value.ObservedArtist,
        ObservationStatus = value.ObservationStatus,
        CreatedAtUtc = value.CreatedAtUtc,
    };

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = null,
    };
}

public sealed class PythonCatalogObservationAdapter(
    IProcessRunner processRunner,
    string repositoryRoot,
    string pythonExecutable = "python") : IObservationCatalogAdapter
{
    public async Task ValidateSessionAsync(
        ObservationSessionIdentity session,
        string catalogPath,
        string masterPath,
        CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                [
                    "-X", "utf8", "-m", "tools.vision_poc.jacket_reference_catalog",
                    "validate-session",
                    "--catalog", Path.GetFullPath(catalogPath),
                    "--master-db", Path.GetFullPath(masterPath),
                    "--expected-catalog-identity", session.CatalogIdentity,
                    "--expected-catalog-schema-version", session.CatalogSchemaVersion.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    "--expected-catalog-created-at", session.CatalogCreatedAt,
                    "--expected-master-version", session.MasterVersion,
                    "--expected-master-source-hash", session.MasterSourceHash,
                    "--expected-feature-extractor-version", session.FeatureExtractorVersion,
                ],
                repositoryRoot),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new ObservationIdentityDriftException(
                $"observation session identity drift preflight failed (exit {result.ExitCode})");
        }
    }

    public async Task ValidateReceiptAsync(
        ObservationCheckpointObservation observation,
        ObservationArtifact artifact,
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-X", "utf8", "-m", "tools.vision_poc.jacket_reference_catalog",
            "validate-receipt",
            "--catalog", Path.GetFullPath(catalogPath),
            "--observation-id", observation.ObservationId,
            "--catalog-status", observation.CatalogStatus,
            "--jacket-crop-hash", artifact.JacketCropHash,
            "--expected-feature-extractor-version", artifact.Session.FeatureExtractorVersion,
            "--expected-catalog-schema-version", artifact.Session.CatalogSchemaVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            "--expected-catalog-created-at", artifact.Session.CatalogCreatedAt,
        };
        if (observation.CatalogReferenceId is not null)
        {
            arguments.Add("--catalog-reference-id");
            arguments.Add(observation.CatalogReferenceId);
        }
        var result = await processRunner.RunAsync(
            new ProcessRequest(pythonExecutable, arguments, repositoryRoot), cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"catalog receipt validation failed (exit {result.ExitCode})");
        }
    }

    public async Task<CatalogIngestReceipt> IngestAsync(
        ObservationArtifact artifact,
        string sourceImagePath,
        string catalogPath,
        string masterPath,
        CancellationToken cancellationToken = default)
    {
        if (artifact.Session.CatalogSchemaVersion is not (1 or 2))
        {
            throw new ObservationIdentityDriftException(
                $"unsupported catalog schema version: {artifact.Session.CatalogSchemaVersion}");
        }
        var isV2 = artifact.Session.CatalogSchemaVersion == 2;
        var arguments = new List<string>
        {
            "-X", "utf8", "-m", "tools.vision_poc.jacket_reference_catalog",
            isV2 ? "ingest-v2" : "ingest",
            "--catalog", Path.GetFullPath(catalogPath),
            "--master-db", Path.GetFullPath(masterPath),
            "--source-image", Path.GetFullPath(sourceImagePath),
        };
        if (isV2)
        {
            arguments.AddRange(
            [
                "--observation-id", artifact.ObservationId,
                "--expected-image-hash", artifact.JacketCropHash,
            ]);
        }
        else
        {
            arguments.AddRange(
            [
                "--source-capture-id", artifact.ObservationId,
                "--observed-title", artifact.ObservedTitle,
                "--observed-artist", artifact.ObservedArtist,
                "--observation-status", artifact.ObservationStatus,
            ]);
        }
        arguments.AddRange(
        [
            "--image-kind", "jacket_crop",
            "--expected-master-version", artifact.Session.MasterVersion,
            "--expected-master-source-hash", artifact.Session.MasterSourceHash,
            "--expected-feature-extractor-version", artifact.Session.FeatureExtractorVersion,
            "--expected-catalog-identity", artifact.Session.CatalogIdentity,
            "--expected-catalog-schema-version", artifact.Session.CatalogSchemaVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            "--expected-catalog-created-at", artifact.Session.CatalogCreatedAt,
        ]);
        var result = await processRunner.RunAsync(
            new ProcessRequest(pythonExecutable, arguments, repositoryRoot),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            if (IsIdentityDriftFailure(result.StandardError))
            {
                throw new ObservationIdentityDriftException(
                    $"catalog identity/payload/artifact conflict detected during ingest "
                    + $"(exit {result.ExitCode})");
            }
            return new CatalogIngestReceipt(
                CatalogIngestDisposition.Failed,
                null,
                $"catalog ingest failed (exit {result.ExitCode})");
        }
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var disposition = root.GetProperty("disposition").GetString() switch
            {
                "created" => CatalogIngestDisposition.Created,
                "existing" => CatalogIngestDisposition.Existing,
                _ => throw new InvalidOperationException("catalog ingest returned an unsupported disposition"),
            };
            var referenceId = root.GetProperty("reference_id").GetString();
            if (string.IsNullOrWhiteSpace(referenceId))
            {
                throw new InvalidOperationException("catalog ingest returned an empty reference id");
            }
            return new CatalogIngestReceipt(
                disposition,
                referenceId,
                root.GetProperty("reason").GetString() ?? "unresolved");
        }
        catch (JsonException exception)
        {
            return new CatalogIngestReceipt(
                CatalogIngestDisposition.Failed,
                null,
                $"catalog ingest returned invalid JSON: {exception.Message}");
        }
        catch (KeyNotFoundException exception)
        {
            return new CatalogIngestReceipt(
                CatalogIngestDisposition.Failed,
                null,
                $"catalog ingest returned incomplete JSON: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return new CatalogIngestReceipt(
                CatalogIngestDisposition.Failed,
                null,
                exception.Message);
        }
    }

    private static bool IsIdentityDriftFailure(string standardError) =>
        standardError.Contains("drift", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("schema version", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("canonical payload", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("collides with", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("observation artifact image", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("source image does not exist", StringComparison.OrdinalIgnoreCase);
}

public sealed class JacketObservationSession(
    JacketObservationDetector detector,
    IObservationCheckpointStore checkpointStore,
    IObservationArtifactPublisher artifactPublisher,
    IObservationCatalogAdapter catalogAdapter) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ObservationSessionIdentity? identity;
    private string? catalogPath;
    private string? masterPath;
    private ObservationCheckpoint? checkpoint;
    private JacketObservationCandidate? lastStableCandidate;
    private JacketDetectionResult lastDetection = new(
        JacketDetectionState.NoFrame, null, "session is not started", 0, 0, 0);
    private long processedFrameBase;
    private bool active;
    private long droppedFrameCount;
    private bool checkpointSavePending;

    public event EventHandler<JacketDetectionResult>? DetectionChanged;

    public JacketDetectionResult LastDetection => lastDetection;
    public ObservationCheckpoint? Checkpoint => checkpoint;
    public bool IsActive => active;
    public bool HasAdoptableCandidate => active && lastStableCandidate is not null;

    public async Task StartAsync(
        ObservationSessionIdentity session,
        string masterDbPath,
        string catalogDbPath,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (active)
            {
                throw new InvalidOperationException("observation session is already active");
            }
            detector.Reset();
            identity = session;
            masterPath = Path.GetFullPath(masterDbPath);
            catalogPath = Path.GetFullPath(catalogDbPath);
            checkpoint = null;
            droppedFrameCount = 0;
            processedFrameBase = 0;
            checkpointSavePending = false;
            lastStableCandidate = null;
            lastDetection = new JacketDetectionResult(
                JacketDetectionState.NoFrame,
                null,
                "session started; stable candidate requires explicit adoption",
                0,
                0,
                0);
            active = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ObservationResumeValidation> ResumeAsync(
        ObservationResumeRequest expected,
        string masterDbPath,
        string catalogDbPath,
        CancellationToken cancellationToken = default)
    {
        ValidateResumeRequest(expected);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (active)
            {
                throw new InvalidOperationException("observation session is already active");
            }
            ObservationCheckpoint loaded;
            try
            {
                loaded = await checkpointStore.LoadAsync(expected.SessionId, cancellationToken);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException or JsonException
                    or ArgumentException or NotSupportedException)
            {
                active = false;
                return new ObservationResumeValidation(false, "checkpoint is corrupt or unavailable", null);
            }
            if (!IdentityMatches(expected, loaded.Session))
            {
                active = false;
                return new ObservationResumeValidation(false, "checkpoint identity/version drift was detected", null);
            }
            try
            {
                await catalogAdapter.ValidateSessionAsync(
                    loaded.Session,
                    Path.GetFullPath(catalogDbPath),
                    Path.GetFullPath(masterDbPath),
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException or JsonException)
            {
                active = false;
                return new ObservationResumeValidation(
                    false,
                    "current master/catalog/extractor drift was detected",
                    null);
            }
            if (artifactPublisher is not IObservationArtifactReader reader)
            {
                active = false;
                return new ObservationResumeValidation(false, "artifact reader is unavailable", null);
            }
            try
            {
                foreach (var observation in loaded.Observations)
                {
                    var artifact = await reader.LoadAsync(
                        loaded.Session, observation.ObservationId, cancellationToken);
                    if (artifact.SourceImageHash != observation.SourceImageHash
                        || artifact.JacketCropHash != observation.JacketCropHash
                        || artifact.Feature.FeatureHash != observation.FeatureHash
                        || artifact.PublishedArtifactPath != observation.ArtifactPath
                        || artifact.CreatedAtUtc != observation.AdoptedAtUtc)
                    {
                        throw new InvalidOperationException(
                            "checkpoint ledger does not match its observation artifact");
                    }
                    await catalogAdapter.ValidateReceiptAsync(
                        observation,
                        artifact,
                        Path.GetFullPath(catalogDbPath),
                        cancellationToken);
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException or JsonException)
            {
                active = false;
                return new ObservationResumeValidation(false, "checkpoint artifact is corrupt or unavailable", null);
            }
            detector.Reset();
            detector.RestoreStableFeatureHashes(loaded.StableFeatureHashes);
            identity = loaded.Session;
            masterPath = Path.GetFullPath(masterDbPath);
            catalogPath = Path.GetFullPath(catalogDbPath);
            checkpoint = loaded;
            droppedFrameCount = loaded.DroppedFrameCount;
            processedFrameBase = loaded.ProcessedFrameCount;
            checkpointSavePending = false;
            lastStableCandidate = null;
            lastDetection = new JacketDetectionResult(
                JacketDetectionState.NoFrame,
                null,
                "compatible checkpoint resumed; existing observations will not be regenerated",
                loaded.ProcessedFrameCount,
                0,
                0);
            active = true;
            return new ObservationResumeValidation(true, "compatible checkpoint resumed", loaded);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<JacketDetectionResult> ObserveFrameAsync(
        RawCaptureFrame frame,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!active)
            {
                return new JacketDetectionResult(
                    JacketDetectionState.NoFrame,
                    null,
                    "inactive session ignored frame",
                    lastDetection.ProcessedFrameCount,
                    lastDetection.InvalidFrameCount,
                    lastDetection.DuplicatePreviewCount);
            }
            var detectorResult = detector.Observe(frame);
            lastDetection = detectorResult with
            {
                ProcessedFrameCount = processedFrameBase + detectorResult.ProcessedFrameCount,
            };
            if (lastDetection.State is (JacketDetectionState.StableCandidate
                    or JacketDetectionState.DuplicatePreview)
                && lastDetection.Candidate is not null)
            {
                lastStableCandidate = lastDetection.Candidate;
            }
            DetectionChanged?.Invoke(this, lastDetection);
            return lastDetection;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ObservationAdoptionResult> AdoptLastStableAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!active || identity is null || masterPath is null || catalogPath is null)
            {
                throw new InvalidOperationException("observation session is not active");
            }
            if (lastStableCandidate is null)
            {
                throw new InvalidOperationException("stable candidate is not ready for explicit adoption");
            }
            var candidate = lastStableCandidate;
            var previousCheckpoint = checkpoint;
            var observationId = BuildObservationId(identity.SessionId, candidate.FeatureHash);
            var existing = checkpoint?.Observations.FirstOrDefault(
                item => item.ObservationId == observationId);
            ObservationArtifact artifact;
            if (existing is not null)
            {
                if (artifactPublisher is not IObservationArtifactReader reader)
                {
                    throw new InvalidOperationException(
                        "artifact reader is unavailable for duplicate adoption");
                }
                artifact = await reader.LoadAsync(identity, observationId, cancellationToken);
            }
            else
            {
                artifact = new ObservationArtifact(
                    observationId,
                    identity,
                    candidate.SourceFrame,
                    candidate.JacketCropPng,
                    candidate.Feature,
                    Hash(candidate.SourceFrame.PngBytes),
                    Hash(candidate.JacketCropPng),
                    "",
                    "",
                    "unresolved",
                    DateTimeOffset.UtcNow);
            }
            var sourceHash = artifact.SourceImageHash;
            var cropHash = artifact.JacketCropHash;
            var observations = checkpoint?.Observations.ToList() ?? [];
            var pending = existing is null
                ? new ObservationCheckpointObservation(
                    observationId,
                    sourceHash,
                    cropHash,
                    artifact.Feature.FeatureHash,
                    "pending",
                    null,
                    "",
                    artifact.CreatedAtUtc)
                : existing;
            if (existing is not null
                && (existing.SourceImageHash != sourceHash
                    || existing.JacketCropHash != cropHash
                    || existing.FeatureHash != artifact.Feature.FeatureHash))
            {
                throw new InvalidOperationException(
                    "observation id is already used with a different payload");
            }
            if (existing is null)
            {
                observations.Add(pending);
            }
            await catalogAdapter.ValidateSessionAsync(
                identity,
                catalogPath,
                masterPath,
                cancellationToken);
            var checkpointValue = NewCheckpoint(observations, candidate.FeatureHash);
            var publishReceipt = await artifactPublisher.PublishAsync(
                artifact,
                checkpointValue,
                cancellationToken);
            pending = pending with { ArtifactPath = publishReceipt.ArtifactPath };
            observations = observations.Select(item => item.ObservationId == observationId
                ? pending
                : item).ToList();
            checkpointValue = NewCheckpoint(observations, candidate.FeatureHash);
            checkpoint = checkpointValue;
            var sourceImagePath = Path.Combine(publishReceipt.ArtifactPath, "jacket-crop.png");
            artifact = artifact with { PublishedArtifactPath = publishReceipt.ArtifactPath };
            if (existing?.CatalogStatus is "ingested" or "deferred")
            {
                var alreadyHandled = existing.CatalogStatus == "ingested"
                    ? new CatalogIngestReceipt(
                        CatalogIngestDisposition.Existing,
                        existing.CatalogReferenceId,
                        "same observation already has an idempotent catalog receipt")
                    : new CatalogIngestReceipt(
                        CatalogIngestDisposition.DeferredUnsupportedSchema,
                        existing.CatalogReferenceId,
                        "same observation remains deferred for catalog schema compatibility");
                return new ObservationAdoptionResult(
                    observationId,
                    publishReceipt,
                    alreadyHandled,
                    checkpointValue);
            }
            CatalogIngestReceipt catalog;
            try
            {
                await catalogAdapter.ValidateSessionAsync(
                    identity,
                    catalogPath,
                    masterPath,
                    cancellationToken);
                catalog = await catalogAdapter.IngestAsync(
                    artifact,
                    sourceImagePath,
                    catalogPath,
                    masterPath,
                    cancellationToken);
            }
            catch (ObservationIdentityDriftException)
            {
                await artifactPublisher.RollbackAsync(
                    publishReceipt, previousCheckpoint, cancellationToken);
                checkpoint = previousCheckpoint;
                throw;
            }
            catch (OperationCanceledException)
            {
                await artifactPublisher.RollbackAsync(
                    publishReceipt, previousCheckpoint, CancellationToken.None);
                checkpoint = previousCheckpoint;
                throw;
            }
            catch (Exception exception)
            {
                catalog = new CatalogIngestReceipt(
                    CatalogIngestDisposition.Failed,
                    null,
                    $"catalog ingest exception: {exception.Message}");
            }
            var catalogStatus = catalog.Disposition switch
            {
                CatalogIngestDisposition.Created or CatalogIngestDisposition.Existing => "ingested",
                CatalogIngestDisposition.DeferredUnsupportedSchema => "deferred",
                _ => "pending",
            };
            pending = pending with
            {
                CatalogStatus = catalogStatus,
                CatalogReferenceId = catalog.CatalogReferenceId,
            };
            observations = observations.Select(item => item.ObservationId == observationId
                ? pending
                : item).ToList();
            var finalCheckpoint = NewCheckpoint(observations, candidate.FeatureHash);
            try
            {
                await checkpointStore.SaveAsync(finalCheckpoint, cancellationToken);
                checkpoint = finalCheckpoint;
            }
            catch (Exception exception) when (catalogStatus == "ingested")
            {
                checkpoint = checkpointValue;
                catalog = new CatalogIngestReceipt(
                    CatalogIngestDisposition.Failed,
                    catalog.CatalogReferenceId,
                    $"catalog receipt is pending checkpoint retry: {exception.Message}");
            }
            return new ObservationAdoptionResult(
                observationId,
                publishReceipt,
                catalog,
                checkpoint ?? finalCheckpoint);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ObservationAdoptionResult>> RetryPendingCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!active || identity is null || masterPath is null || catalogPath is null || checkpoint is null)
            {
                throw new InvalidOperationException("observation session is not resumable");
            }
            var retryableStatuses = identity.CatalogSchemaVersion switch
            {
                1 => new[] { "pending" },
                2 => new[] { "pending", "deferred" },
                _ => throw new ObservationIdentityDriftException(
                    $"unsupported catalog schema version: {identity.CatalogSchemaVersion}"),
            };
            var pendingObservations = checkpoint.Observations
                .Where(item => retryableStatuses.Contains(item.CatalogStatus, StringComparer.Ordinal))
                .ToList();
            if (pendingObservations.Count == 0)
            {
                return [];
            }
            if (artifactPublisher is not IObservationArtifactReader reader)
            {
                throw new InvalidOperationException("artifact reader is unavailable for catalog retry");
            }
            await catalogAdapter.ValidateSessionAsync(
                identity,
                catalogPath,
                masterPath,
                cancellationToken);
            var results = new List<ObservationAdoptionResult>();
            foreach (var pending in pendingObservations)
            {
                var artifact = await reader.LoadAsync(identity, pending.ObservationId, cancellationToken);
                var catalog = await catalogAdapter.IngestAsync(
                    artifact,
                    Path.Combine(pending.ArtifactPath, "jacket-crop.png"),
                    catalogPath,
                    masterPath,
                    cancellationToken);
                var status = catalog.Disposition switch
                {
                    CatalogIngestDisposition.Created or CatalogIngestDisposition.Existing => "ingested",
                    CatalogIngestDisposition.DeferredUnsupportedSchema => "deferred",
                    _ => "pending",
                };
                var updated = pending with
                {
                    CatalogStatus = status,
                    CatalogReferenceId = catalog.CatalogReferenceId,
                };
                var observations = checkpoint.Observations.Select(item =>
                    item.ObservationId == pending.ObservationId ? updated : item).ToList();
                var next = NewCheckpoint(observations, checkpoint.LastStableFeatureHash);
                await checkpointStore.SaveAsync(next, cancellationToken);
                checkpoint = next;
                results.Add(new ObservationAdoptionResult(
                    pending.ObservationId,
                    new ArtifactPublishReceipt(
                        pending.ObservationId,
                        pending.ArtifactPath,
                        ArtifactDisposition.Existing,
                        pending.SourceImageHash,
                        pending.JacketCropHash),
                    catalog,
                    next));
            }
            return results;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var wasActive = active;
            active = false;
            if (wasActive && checkpoint is not null)
            {
                checkpoint = NewCheckpoint(
                    checkpoint.Observations,
                    lastStableCandidate?.FeatureHash ?? checkpoint.LastStableFeatureHash);
                checkpointSavePending = true;
            }
            if (checkpointSavePending && checkpoint is not null)
            {
                await checkpointStore.SaveAsync(checkpoint, cancellationToken);
                checkpointSavePending = false;
            }
            lastStableCandidate = null;
            lastDetection = lastDetection with
            {
                State = JacketDetectionState.NoFrame,
                Candidate = null,
                Diagnostic = "capture stopped; post-stop frames and adoption are ignored",
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateDroppedFrameCountAsync(
        long droppedCount,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            droppedFrameCount = Math.Max(droppedFrameCount, droppedCount);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        gate.Dispose();
    }

    private ObservationCheckpoint NewCheckpoint(
        IReadOnlyList<ObservationCheckpointObservation> observations,
        string? lastStableFeatureHash) => new(
        JacketObservationVersions.SessionCheckpoint,
        identity ?? throw new InvalidOperationException("session identity is missing"),
        lastStableFeatureHash,
        detector.StableFeatureHashes,
        lastDetection.ProcessedFrameCount,
        droppedFrameCount,
        observations,
        DateTimeOffset.UtcNow);

    private static string BuildObservationId(string sessionId, string featureHash) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{sessionId}\0{featureHash}"))).ToLowerInvariant();

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ValidateSession(ObservationSessionIdentity value)
    {
        AtomicObservationCheckpointStore.ValidateSessionId(value.SessionId);
        if (string.IsNullOrWhiteSpace(value.MasterVersion)
            || string.IsNullOrWhiteSpace(value.MasterSourceHash)
            || string.IsNullOrWhiteSpace(value.CatalogIdentity)
            || value.CatalogSchemaVersion <= 0
            || string.IsNullOrWhiteSpace(value.CatalogCreatedAt)
            || string.IsNullOrWhiteSpace(value.FeatureExtractorVersion)
            || string.IsNullOrWhiteSpace(value.DetectorVersion)
            || string.IsNullOrWhiteSpace(value.RoiVersion)
            || string.IsNullOrWhiteSpace(value.FrameClockVersion))
        {
            throw new InvalidOperationException("observation session identity is incomplete");
        }
    }

    private static void ValidateResumeRequest(ObservationResumeRequest value)
    {
        AtomicObservationCheckpointStore.ValidateSessionId(value.SessionId);
        if (string.IsNullOrWhiteSpace(value.MasterVersion)
            || string.IsNullOrWhiteSpace(value.MasterSourceHash)
            || string.IsNullOrWhiteSpace(value.CatalogIdentity)
            || value.CatalogSchemaVersion <= 0
            || string.IsNullOrWhiteSpace(value.CatalogCreatedAt)
            || string.IsNullOrWhiteSpace(value.FeatureExtractorVersion)
            || string.IsNullOrWhiteSpace(value.DetectorVersion)
            || string.IsNullOrWhiteSpace(value.RoiVersion)
            || string.IsNullOrWhiteSpace(value.FrameClockVersion))
        {
            throw new InvalidOperationException("observation resume identity is incomplete");
        }
    }

    private static bool IdentityMatches(
        ObservationResumeRequest expected,
        ObservationSessionIdentity actual) =>
        expected.SessionId == actual.SessionId
        && expected.MasterVersion == actual.MasterVersion
        && expected.MasterSourceHash == actual.MasterSourceHash
        && expected.CatalogIdentity == actual.CatalogIdentity
        && expected.CatalogSchemaVersion == actual.CatalogSchemaVersion
        && expected.CatalogCreatedAt == actual.CatalogCreatedAt
        && expected.FeatureExtractorVersion == actual.FeatureExtractorVersion
        && expected.DetectorVersion == actual.DetectorVersion
        && expected.RoiVersion == actual.RoiVersion
        && expected.FrameClockVersion == actual.FrameClockVersion
        && expected.Window == actual.Window
        && expected.Window.IsSameTarget(actual.Window);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CheckpointDocument
{
    [JsonPropertyName("checkpoint_version")]
    public required string CheckpointVersion { get; init; }
    [JsonPropertyName("session")]
    public required SessionDocument Session { get; init; }
    [JsonPropertyName("last_stable_feature_hash")]
    public string? LastStableFeatureHash { get; init; }
    [JsonPropertyName("stable_feature_hashes")]
    public List<string>? StableFeatureHashes { get; init; }
    [JsonPropertyName("processed_frame_count")]
    public long ProcessedFrameCount { get; init; }
    [JsonPropertyName("dropped_frame_count")]
    public long DroppedFrameCount { get; init; }
    [JsonPropertyName("observations")]
    public List<CheckpointObservationDocument>? Observations { get; init; }
    [JsonPropertyName("updated_at_utc")]
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CheckpointObservationDocument
{
    [JsonPropertyName("observation_id")]
    public required string ObservationId { get; init; }
    [JsonPropertyName("source_image_hash")]
    public required string SourceImageHash { get; init; }
    [JsonPropertyName("jacket_crop_hash")]
    public required string JacketCropHash { get; init; }
    [JsonPropertyName("feature_hash")]
    public required string FeatureHash { get; init; }
    [JsonPropertyName("catalog_status")]
    public required string CatalogStatus { get; init; }
    [JsonPropertyName("catalog_reference_id")]
    public string? CatalogReferenceId { get; init; }
    [JsonPropertyName("artifact_path")]
    public required string ArtifactPath { get; init; }
    [JsonPropertyName("adopted_at_utc")]
    public DateTimeOffset AdoptedAtUtc { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SessionDocument
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
    [JsonPropertyName("master_version")]
    public required string MasterVersion { get; init; }
    [JsonPropertyName("master_source_hash")]
    public required string MasterSourceHash { get; init; }
    [JsonPropertyName("catalog_identity")]
    public required string CatalogIdentity { get; init; }
    [JsonPropertyName("catalog_schema_version")]
    public int CatalogSchemaVersion { get; init; }
    [JsonPropertyName("catalog_created_at")]
    public required string CatalogCreatedAt { get; init; }
    [JsonPropertyName("feature_extractor_version")]
    public required string FeatureExtractorVersion { get; init; }
    [JsonPropertyName("detector_version")]
    public required string DetectorVersion { get; init; }
    [JsonPropertyName("roi_version")]
    public required string RoiVersion { get; init; }
    [JsonPropertyName("frame_clock_version")]
    public required string FrameClockVersion { get; init; }
    [JsonPropertyName("window")]
    public required WindowDocument Window { get; init; }
    [JsonPropertyName("started_at_utc")]
    public DateTimeOffset StartedAtUtc { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class WindowDocument
{
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }
    [JsonPropertyName("process_id")]
    public int ProcessId { get; init; }
    [JsonPropertyName("process_start_ticks")]
    public long ProcessStartTicks { get; init; }
    [JsonPropertyName("process_name")]
    public required string ProcessName { get; init; }
    [JsonPropertyName("title")]
    public required string Title { get; init; }
    [JsonPropertyName("class_name")]
    public required string ClassName { get; init; }
    [JsonPropertyName("client_width")]
    public int ClientWidth { get; init; }
    [JsonPropertyName("client_height")]
    public int ClientHeight { get; init; }
    [JsonPropertyName("is_visible")]
    public bool IsVisible { get; init; }
    [JsonPropertyName("is_minimized")]
    public bool IsMinimized { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class ObservationDocument
{
    [JsonPropertyName("manifest_version")]
    public required string ManifestVersion { get; init; }
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
    [JsonPropertyName("observation_id")]
    public required string ObservationId { get; init; }
    [JsonPropertyName("source_image")]
    public required string SourceImage { get; init; }
    [JsonPropertyName("jacket_crop")]
    public required string JacketCrop { get; init; }
    [JsonPropertyName("source_image_hash")]
    public required string SourceImageHash { get; init; }
    [JsonPropertyName("jacket_crop_hash")]
    public required string JacketCropHash { get; init; }
    [JsonPropertyName("source_width")]
    public int SourceWidth { get; init; }
    [JsonPropertyName("source_height")]
    public int SourceHeight { get; init; }
    [JsonPropertyName("source_sequence")]
    public long SourceSequence { get; init; }
    [JsonPropertyName("captured_at_utc")]
    public DateTimeOffset CapturedAtUtc { get; init; }
    [JsonPropertyName("feature_version")]
    public required string FeatureVersion { get; init; }
    [JsonPropertyName("roi_version")]
    public required string RoiVersion { get; init; }
    [JsonPropertyName("master_version")]
    public required string MasterVersion { get; init; }
    [JsonPropertyName("master_source_hash")]
    public required string MasterSourceHash { get; init; }
    [JsonPropertyName("catalog_identity")]
    public required string CatalogIdentity { get; init; }
    [JsonPropertyName("catalog_schema_version")]
    public int CatalogSchemaVersion { get; init; }
    [JsonPropertyName("catalog_created_at")]
    public required string CatalogCreatedAt { get; init; }
    [JsonPropertyName("feature_extractor_version")]
    public required string FeatureExtractorVersion { get; init; }
    [JsonPropertyName("detector_version")]
    public required string DetectorVersion { get; init; }
    [JsonPropertyName("frame_clock_version")]
    public required string FrameClockVersion { get; init; }
    [JsonPropertyName("window")]
    public required WindowDocument Window { get; init; }
    [JsonPropertyName("change_threshold")]
    public double ChangeThreshold { get; init; }
    [JsonPropertyName("stable_frame_count_required")]
    public int StableFrameCountRequired { get; init; }
    [JsonPropertyName("minimum_stable_duration_milliseconds")]
    public long MinimumStableDurationMilliseconds { get; init; }
    [JsonPropertyName("roi_x")]
    public int RoiX { get; init; }
    [JsonPropertyName("roi_y")]
    public int RoiY { get; init; }
    [JsonPropertyName("roi_width")]
    public int RoiWidth { get; init; }
    [JsonPropertyName("roi_height")]
    public int RoiHeight { get; init; }
    [JsonPropertyName("feature_hash")]
    public required string FeatureHash { get; init; }
    [JsonPropertyName("mean_absolute_difference")]
    public double MeanAbsoluteDifference { get; init; }
    [JsonPropertyName("sample_width")]
    public int SampleWidth { get; init; }
    [JsonPropertyName("sample_height")]
    public int SampleHeight { get; init; }
    [JsonPropertyName("observed_title")]
    public required string ObservedTitle { get; init; }
    [JsonPropertyName("observed_artist")]
    public required string ObservedArtist { get; init; }
    [JsonPropertyName("observation_status")]
    public required string ObservationStatus { get; init; }
    [JsonPropertyName("created_at_utc")]
    public DateTimeOffset CreatedAtUtc { get; init; }
}
