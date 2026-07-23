using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

public static class ManualReviewDraftStatuses
{
    public static readonly IReadOnlyList<string> Values =
        ["unreviewed", "confirmed", "rejected", "hold", "unchanged"];

    public static bool IsValid(string? status) =>
        status is not null && Values.Contains(status, StringComparer.Ordinal);
}

public sealed record ManualReviewStatusOption(string Value, string Display);

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
    private readonly IReadOnlyList<ProjectionSong> songs;
    private string status;
    private string? truthSongId;
    private string notes;
    private bool isSaved;
    private string validationError = "";
    private string songSearch = "";
    private ProjectionSong? selectedSearchResult;

    public ManualReviewDraftRow(
        ReviewReference reference,
        ManualReviewDraft? draft,
        IReadOnlyDictionary<string, ProjectionSong> songsById)
    {
        Reference = reference;
        this.songsById = songsById;
        songs = songsById.Values.ToList();
        ObservationId = reference.CandidateEvaluation.ObservationId;
        status = draft?.Status ?? "unreviewed";
        truthSongId = draft?.TruthSongId;
        notes = draft?.Notes ?? "";
        isSaved = draft is not null;
        SourceImagePath = reference.SourceImagePath;
        ApplySongSearch();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReviewReference Reference { get; }
    public string ObservationId { get; }
    public string ObservationIdShort => ObservationId.Length <= 12
        ? ObservationId
        : $"{ObservationId[..12]}…";
    public string ReferenceId => Reference.ReferenceId;
    public string StoredStatus => Reference.StoredStatus;
    public string? SourceImagePath { get; }
    public string? TitleRoiImagePath => SourceImagePath;
    public string? ArtistRoiImagePath => SourceImagePath;
    public bool IsSaved => isSaved;
    public bool ShouldPersistDraft => isSaved
        || Status != "unreviewed"
        || !string.IsNullOrWhiteSpace(TruthSongId)
        || Notes.Length > 0;
    public ObservableCollection<ProjectionSong> SongChoices { get; } = [];
    public IReadOnlyList<ManualReviewStatusOption> StatusOptions { get; } =
    [
        new("unreviewed", "未レビュー"),
        new("confirmed", "確定"),
        new("rejected", "却下"),
        new("hold", "保留"),
    ];

    public string SongSearch
    {
        get => songSearch;
        set
        {
            if (SetField(ref songSearch, value ?? ""))
            {
                ApplySongSearch();
            }
        }
    }

    public ProjectionSong? SelectedSearchResult
    {
        get => selectedSearchResult;
        set
        {
            if (value is null)
            {
                SetField(ref selectedSearchResult, null);
                return;
            }
            if (SetField(ref selectedSearchResult, value))
            {
                TruthSongId = value.SongId;
            }
        }
    }

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
                else if (value == "hold" && !string.IsNullOrWhiteSpace(truthSongId))
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
        OnPropertyChanged(nameof(IsSaved));
        ValidationError = "";
        OnPropertyChanged(nameof(DraftStateDisplay));
    }

    public void MarkDraftRemoved()
    {
        isSaved = false;
        OnPropertyChanged(nameof(IsSaved));
        ValidationError = "";
        OnPropertyChanged(nameof(DraftStateDisplay));
    }

    public void SetValidationError(string message) => ValidationError = message;

    private void ApplySongSearch()
    {
        SongChoices.Clear();
        var query = SongSearch.Trim();
        foreach (var song in songs
                     .Select(song => (Song: song, Rank: SongSearchRank(song, query)))
                     .Where(item => item.Rank is not null)
                     .OrderBy(item => item.Rank)
                     .ThenBy(item => item.Song.Title, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Song.SongId, StringComparer.Ordinal))
        {
            SongChoices.Add(song.Song);
        }
        OnPropertyChanged(nameof(SelectedSearchResult));
    }

    private static int? SongSearchRank(ProjectionSong song, string query)
    {
        if (query.Length == 0)
        {
            return 0;
        }
        if (string.Equals(song.Title, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (song.Aliases.Any(alias =>
                string.Equals(alias, query, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }
        if (song.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        if (song.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        if (song.Artist.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }
        if (song.SongId.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }
        return null;
    }

    private void MarkDirty()
    {
        isSaved = false;
        OnPropertyChanged(nameof(IsSaved));
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

public sealed class ReviewedManualReviewRow : INotifyPropertyChanged
{
    private readonly IReadOnlyDictionary<string, ProjectionSong> songsById;
    private readonly IReadOnlyList<ProjectionSong> songs;
    private string draftStatus;
    private string? draftSongId;
    private string notes;
    private bool isSaved;
    private string validationError = "";
    private string songSearch = "";
    private ProjectionSong? selectedSearchResult;

    public ReviewedManualReviewRow(
        ReviewReference reference,
        ManualReviewDraft? draft,
        IReadOnlyDictionary<string, ProjectionSong> songsById)
    {
        Reference = reference;
        this.songsById = songsById;
        songs = songsById.Values.ToList();
        ObservationId = reference.CandidateEvaluation.ObservationId;
        draftStatus = draft?.Status ?? "unchanged";
        draftSongId = draft?.TruthSongId
            ?? (draft is null || draft.Status == "unchanged"
                ? reference.CurrentSongId
                : null);
        notes = draft?.Notes ?? reference.Notes;
        isSaved = draft is not null;
        SourceImagePath = reference.SourceImagePath;
        ApplySongSearch();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReviewReference Reference { get; }
    public string ObservationId { get; }
    public string ObservationIdShort => ObservationId.Length <= 12
        ? ObservationId
        : $"{ObservationId[..12]}…";
    public string ReferenceId => Reference.ReferenceId;
    public string CurrentStatus => Reference.CurrentStatus;
    public string CurrentStatusDisplay => CurrentStatus switch
    {
        "manual_confirmed" => "確定",
        "rejected" => "reject",
        "auto_confirmed" => "自動確定",
        _ => CurrentStatus,
    };
    public string? CurrentSongId => Reference.CurrentSongId;
    public string CurrentSongDisplay => FormatSong(CurrentSongId);
    public string? SourceImagePath { get; }
    public string? TitleRoiImagePath => SourceImagePath;
    public string? ArtistRoiImagePath => SourceImagePath;
    public string RegisteredRoute => Reference.RegisteredRoute;
    public string ProcessedAt => Reference.ProcessedAt;
    public bool IsSaved => isSaved;
    public bool ShouldPersistDraft => isSaved
        || DraftStatus != "unchanged"
        || DraftSongId != CurrentSongId
        || Notes != Reference.Notes;
    public bool IsCurrentPlan
    {
        get
        {
            if (DraftStatus == "hold" || DraftSongId != CurrentSongId
                || Notes != Reference.Notes)
            {
                return false;
            }
            var currentStatus = CurrentStatus == "auto_confirmed"
                ? "confirmed"
                : CurrentStatus;
            var plannedStatus = DraftStatus == "confirmed"
                ? "confirmed"
                : DraftStatus == "rejected"
                    ? "rejected"
                    : currentStatus;
            return plannedStatus == currentStatus;
        }
    }
    public ObservableCollection<ProjectionSong> SongChoices { get; } = [];
    public IReadOnlyList<ManualReviewStatusOption> StatusOptions { get; } =
    [
        new("unchanged", "変更なし"),
        new("confirmed", "確定"),
        new("rejected", "reject"),
        new("hold", "保留"),
    ];

    public string DraftStatus
    {
        get => draftStatus;
        set
        {
            if (SetField(ref draftStatus, value))
            {
                if (value == "rejected")
                {
                    DraftSongId = null;
                }
                else if (value == "unchanged")
                {
                    DraftSongId = CurrentSongId;
                }
                else if (value == "hold")
                {
                    DraftSongId = null;
                }
                MarkDirty();
            }
        }
    }

    public string? DraftSongId
    {
        get => draftSongId;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SetField(ref draftSongId, normalized))
            {
                if (!string.IsNullOrWhiteSpace(normalized) && draftStatus != "confirmed")
                {
                    draftStatus = "confirmed";
                    OnPropertyChanged(nameof(DraftStatus));
                }
                OnPropertyChanged(nameof(DraftSongDisplay));
                MarkDirty();
            }
        }
    }

    public string DraftSongDisplay => FormatSong(DraftSongId);

    public string SongSearch
    {
        get => songSearch;
        set
        {
            if (SetField(ref songSearch, value ?? ""))
            {
                ApplySongSearch();
            }
        }
    }

    public ProjectionSong? SelectedSearchResult
    {
        get => selectedSearchResult;
        set
        {
            if (SetField(ref selectedSearchResult, value) && value is not null)
            {
                DraftSongId = value.SongId;
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
        if (!ManualReviewDraftStatuses.IsValid(DraftStatus))
        {
            return $"draft statusが不正です: {DraftStatus}";
        }
        if (DraftStatus == "confirmed" && string.IsNullOrWhiteSpace(DraftSongId))
        {
            return "確定では変更予定のsongを選択してください。";
        }
        if (!string.IsNullOrWhiteSpace(DraftSongId)
            && !validSongIds.Contains(DraftSongId))
        {
            return "変更予定のsongが現在のMasterにありません。";
        }
        if (DraftStatus == "rejected" && !string.IsNullOrWhiteSpace(DraftSongId))
        {
            return "rejectでは変更予定のsongを空欄にしてください。";
        }
        if (DraftStatus == "unchanged" && DraftSongId != CurrentSongId)
        {
            return "変更なしでは現在のsongを維持してください。";
        }
        if (CurrentStatus is not ("auto_confirmed" or "manual_confirmed" or "rejected"))
        {
            return $"current statusがレビュー済みではありません: {CurrentStatus}";
        }
        if (CurrentStatus is "auto_confirmed" or "manual_confirmed"
            && string.IsNullOrWhiteSpace(CurrentSongId))
        {
            return "current statusがconfirmedなのにcurrent songがありません。";
        }
        return null;
    }

    public ManualReviewDraft ToDraft() => new()
    {
        ObservationId = ObservationId,
        Status = DraftStatus,
        TruthSongId = DraftSongId,
        Notes = Notes,
    };

    public void MarkSaved()
    {
        isSaved = true;
        OnPropertyChanged(nameof(IsSaved));
        ValidationError = "";
        OnPropertyChanged(nameof(DraftStateDisplay));
    }

    public void MarkDraftRemoved()
    {
        isSaved = false;
        OnPropertyChanged(nameof(IsSaved));
        ValidationError = "";
        OnPropertyChanged(nameof(DraftStateDisplay));
    }

    public void SetValidationError(string message) => ValidationError = message;

    private string FormatSong(string? songId)
    {
        if (string.IsNullOrWhiteSpace(songId))
        {
            return "未選択";
        }
        return songsById.TryGetValue(songId, out var song)
            ? $"{song.Title} / {song.Artist} ({song.SongId})"
            : $"{songId} (Masterにありません)";
    }

    private void ApplySongSearch()
    {
        SongChoices.Clear();
        var query = SongSearch.Trim();
        foreach (var song in songs
                     .Select(song => (Song: song, Rank: SongSearchRank(song, query)))
                     .Where(item => item.Rank is not null)
                     .OrderBy(item => item.Rank)
                     .ThenBy(item => item.Song.Title, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Song.SongId, StringComparer.Ordinal))
        {
            SongChoices.Add(song.Song);
        }
        OnPropertyChanged(nameof(SelectedSearchResult));
    }

    private static int? SongSearchRank(ProjectionSong song, string query)
    {
        if (query.Length == 0)
        {
            return 0;
        }
        if (string.Equals(song.Title, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (song.Aliases.Any(alias =>
                string.Equals(alias, query, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }
        if (song.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        if (song.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        if (song.Artist.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }
        if (song.SongId.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }
        return null;
    }

    private void MarkDirty()
    {
        isSaved = false;
        OnPropertyChanged(nameof(IsSaved));
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
