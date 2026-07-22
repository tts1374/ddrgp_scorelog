using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

public static class ManualReviewDraftStatuses
{
    public static readonly IReadOnlyList<string> Values =
        ["unreviewed", "confirmed", "rejected", "hold"];

    public static bool IsValid(string? status) =>
        status is not null && Values.Contains(status, StringComparer.Ordinal);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ManualReviewDraft
{
    [JsonPropertyName("observation_id")]
    public required string ObservationId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("truth_song_id")]
    public string? TruthSongId { get; init; }

    [JsonPropertyName("notes")]
    public required string Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class ManualReviewDraftDocument
{
    [JsonPropertyName("draft_schema_version")]
    public required int DraftSchemaVersion { get; init; }

    [JsonPropertyName("drafts")]
    public required List<ManualReviewDraft> Drafts { get; init; }
}

public interface IManualReviewDraftStore
{
    Task<IReadOnlyDictionary<string, ManualReviewDraft>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyCollection<ManualReviewDraft> drafts,
        CancellationToken cancellationToken = default);
}

public sealed class JsonManualReviewDraftStore(string draftPath) : IManualReviewDraftStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string fullDraftPath = Path.GetFullPath(draftPath);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public async Task<IReadOnlyDictionary<string, ManualReviewDraft>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fullDraftPath))
        {
            return new Dictionary<string, ManualReviewDraft>(StringComparer.Ordinal);
        }

        await using var stream = new FileStream(
            fullDraftPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        ManualReviewDraftDocument document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<ManualReviewDraftDocument>(
                    stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Manual review draft JSON is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Manual review draft JSON is invalid.", exception);
        }

        ValidateDocument(document);
        return document.Drafts.ToDictionary(
            draft => draft.ObservationId,
            draft => draft,
            StringComparer.Ordinal);
    }

    public async Task SaveAsync(
        IReadOnlyCollection<ManualReviewDraft> drafts,
        CancellationToken cancellationToken = default)
    {
        var orderedDrafts = drafts
            .OrderBy(draft => draft.ObservationId, StringComparer.Ordinal)
            .ToList();
        ValidateDrafts(orderedDrafts);

        var parent = Path.GetDirectoryName(fullDraftPath)
            ?? throw new InvalidOperationException("Manual review draft path has no parent directory.");
        Directory.CreateDirectory(parent);
        var temporaryPath = Path.Combine(
            parent,
            $".{Path.GetFileName(fullDraftPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new ManualReviewDraftDocument
                    {
                        DraftSchemaVersion = CurrentSchemaVersion,
                        Drafts = orderedDrafts,
                    },
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullDraftPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void ValidateDocument(ManualReviewDraftDocument document)
    {
        if (document.DraftSchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported manual review draft schema: {document.DraftSchemaVersion}");
        }
        if (document.Drafts is null)
        {
            throw new InvalidOperationException("Manual review draft list is null.");
        }
        ValidateDrafts(document.Drafts);
    }

    private static void ValidateDrafts(IEnumerable<ManualReviewDraft> drafts)
    {
        var observationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var draft in drafts)
        {
            if (draft is null
                || string.IsNullOrWhiteSpace(draft.ObservationId)
                || !observationIds.Add(draft.ObservationId))
            {
                throw new InvalidOperationException(
                    "Manual review drafts must have unique non-empty observation IDs.");
            }
            if (!ManualReviewDraftStatuses.IsValid(draft.Status))
            {
                throw new InvalidOperationException(
                    $"Manual review draft status is invalid: {draft.Status}");
            }
            if (draft.Notes is null)
            {
                throw new InvalidOperationException("Manual review draft notes must not be null.");
            }
            if (draft.Status == "confirmed" && string.IsNullOrWhiteSpace(draft.TruthSongId))
            {
                throw new InvalidOperationException(
                    "A confirmed manual review draft requires truth_song_id.");
            }
            if (draft.Status == "rejected" && !string.IsNullOrWhiteSpace(draft.TruthSongId))
            {
                throw new InvalidOperationException(
                    "A rejected manual review draft must not have truth_song_id.");
            }
        }
    }
}

public sealed class ManualReviewDraftRow : INotifyPropertyChanged
{
    private readonly IReadOnlyDictionary<string, ProjectionSong> songsById;
    private string status;
    private string? truthSongId;
    private string notes;
    private bool isSaved;
    private string validationError = "";

    public ManualReviewDraftRow(
        ReviewReference reference,
        ManualReviewDraft? draft,
        IReadOnlyDictionary<string, ProjectionSong> songsById)
    {
        Reference = reference;
        this.songsById = songsById;
        ObservationId = reference.CandidateEvaluation.ObservationId;
        status = draft?.Status ?? "unreviewed";
        truthSongId = draft?.TruthSongId;
        notes = draft?.Notes ?? "";
        isSaved = draft is not null;
        SourceImagePath = GetSourceImagePath(reference.CandidateEvaluation.JacketPreviewPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReviewReference Reference { get; }
    public string ObservationId { get; }
    public string ReferenceId => Reference.ReferenceId;
    public string StoredStatus => Reference.StoredStatus;
    public string? SourceImagePath { get; }
    public string? TitleRoiImagePath => SourceImagePath;
    public string? ArtistRoiImagePath => SourceImagePath;

    public string Status
    {
        get => status;
        set
        {
            if (SetField(ref status, value))
            {
                if (value == "rejected" && !string.IsNullOrWhiteSpace(truthSongId))
                {
                    truthSongId = null;
                    OnPropertyChanged(nameof(TruthSongId));
                    OnPropertyChanged(nameof(TruthSongDisplay));
                }
                MarkDirty();
            }
        }
    }

    public string? TruthSongId
    {
        get => truthSongId;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SetField(ref truthSongId, normalized))
            {
                if (!string.IsNullOrWhiteSpace(normalized) && status != "confirmed")
                {
                    status = "confirmed";
                    OnPropertyChanged(nameof(Status));
                }
                OnPropertyChanged(nameof(TruthSongDisplay));
                MarkDirty();
            }
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (SetField(ref notes, value ?? ""))
            {
                MarkDirty();
            }
        }
    }

    public string TruthSongDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TruthSongId))
            {
                return "未選択";
            }
            return songsById.TryGetValue(TruthSongId, out var song)
                ? $"{song.Title} / {song.Artist} ({song.SongId})"
                : $"{TruthSongId} (Masterにありません)";
        }
    }

    public string DraftStateDisplay => !string.IsNullOrWhiteSpace(validationError)
        ? $"入力エラー: {validationError}"
        : isSaved ? "保存済み" : "未保存";

    public string ValidationError
    {
        get => validationError;
        private set
        {
            if (SetField(ref validationError, value))
            {
                OnPropertyChanged(nameof(DraftStateDisplay));
            }
        }
    }

    public string? Validate(IReadOnlySet<string> validSongIds)
    {
        if (string.IsNullOrWhiteSpace(ObservationId))
        {
            return "observation_idがありません。";
        }
        if (!ManualReviewDraftStatuses.IsValid(Status))
        {
            return $"statusが不正です: {Status}";
        }
        if (Status == "confirmed" && string.IsNullOrWhiteSpace(TruthSongId))
        {
            return "confirmedではtruth songを選択してください。";
        }
        if (!string.IsNullOrWhiteSpace(TruthSongId)
            && !validSongIds.Contains(TruthSongId))
        {
            return "truth songが現在のMasterにありません。";
        }
        if (Status == "rejected" && !string.IsNullOrWhiteSpace(TruthSongId))
        {
            return "rejectedではtruth songを空欄にしてください。";
        }
        return null;
    }

    public ManualReviewDraft ToDraft() => new()
    {
        ObservationId = ObservationId,
        Status = Status,
        TruthSongId = TruthSongId,
        Notes = Notes,
    };

    public void MarkSaved()
    {
        isSaved = true;
        ValidationError = "";
        OnPropertyChanged(nameof(DraftStateDisplay));
    }

    public void SetValidationError(string message) => ValidationError = message;

    private static string? GetSourceImagePath(string? jacketPreviewPath)
    {
        if (string.IsNullOrWhiteSpace(jacketPreviewPath))
        {
            return null;
        }
        var previewPath = Path.GetFullPath(jacketPreviewPath);
        var directory = Path.GetDirectoryName(previewPath);
        return directory is null ? null : Path.Combine(directory, "source.png");
    }

    private void MarkDirty()
    {
        isSaved = false;
        ValidationError = "";
        OnPropertyChanged(nameof(DraftStateDisplay));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
