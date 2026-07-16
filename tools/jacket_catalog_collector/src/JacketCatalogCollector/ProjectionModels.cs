using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReviewProjection
{
    [JsonPropertyName("projection_schema_version")]
    public required int ProjectionSchemaVersion { get; init; }

    [JsonPropertyName("master")]
    public required ProjectionMaster Master { get; init; }

    [JsonPropertyName("catalog")]
    public required ProjectionCatalog Catalog { get; init; }

    [JsonPropertyName("coverage")]
    public required ProjectionCoverage Coverage { get; init; }

    [JsonPropertyName("songs")]
    public required List<ProjectionSong> Songs { get; init; }

    [JsonPropertyName("review_references")]
    public required List<ReviewReference> ReviewReferences { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProjectionMaster
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }
    [JsonPropertyName("master_version")]
    public required string MasterVersion { get; init; }
    [JsonPropertyName("source_hash")]
    public required string SourceHash { get; init; }
    [JsonPropertyName("song_count")]
    public required int SongCount { get; init; }
    [JsonPropertyName("chart_count")]
    public required int ChartCount { get; init; }
    [JsonPropertyName("grand_prix_song_count")]
    public required int GrandPrixSongCount { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProjectionCatalog
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }
    [JsonPropertyName("catalog_identity")]
    public required string CatalogIdentity { get; init; }
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }
    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }
    [JsonPropertyName("current_feature_extractor_version")]
    public required string CurrentFeatureExtractorVersion { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProjectionCoverage
{
    [JsonPropertyName("grand_prix_song_count")]
    public required int GrandPrixSongCount { get; init; }
    [JsonPropertyName("status_counts")]
    public required Dictionary<string, int> StatusCounts { get; init; }
    [JsonPropertyName("orphaned_reference_count")]
    public required int OrphanedReferenceCount { get; init; }
    [JsonPropertyName("orphan_reason_counts")]
    public required Dictionary<string, int> OrphanReasonCounts { get; init; }
    [JsonPropertyName("unassigned_unresolved_observation_count")]
    public required int UnassignedUnresolvedObservationCount { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProjectionSong
{
    [JsonPropertyName("song_id")]
    public required string SongId { get; init; }
    [JsonPropertyName("title")]
    public required string Title { get; init; }
    [JsonPropertyName("artist")]
    public required string Artist { get; init; }
    [JsonPropertyName("master_version")]
    public required string MasterVersion { get; init; }
    [JsonPropertyName("coverage_status")]
    public required string CoverageStatus { get; init; }
    [JsonPropertyName("reference_count")]
    public required string ReferenceCount { get; init; }
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReviewReference
{
    [JsonPropertyName("reference_id")]
    public required string ReferenceId { get; init; }
    [JsonPropertyName("review_status")]
    public required string ReviewStatus { get; init; }
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
    [JsonPropertyName("observed_title")]
    public required string ObservedTitle { get; init; }
    [JsonPropertyName("observed_artist")]
    public required string ObservedArtist { get; init; }
    [JsonPropertyName("observation_status")]
    public required string ObservationStatus { get; init; }
    [JsonPropertyName("master_drift")]
    public required bool MasterDrift { get; init; }
    [JsonPropertyName("feature_extractor_version")]
    public required string FeatureExtractorVersion { get; init; }
    [JsonPropertyName("assigned_song")]
    public AssignedSong? AssignedSong { get; init; }
    [JsonPropertyName("candidates")]
    public required List<ReviewCandidate> Candidates { get; init; }
    [JsonPropertyName("stored_status")]
    public required string StoredStatus { get; init; }
    [JsonPropertyName("revision")]
    public required int Revision { get; init; }
    [JsonPropertyName("manual_action_id")]
    public required string? ManualActionId { get; init; }
    [JsonPropertyName("manual_note")]
    public required string ManualNote { get; init; }
    [JsonPropertyName("history")]
    public required List<ReviewHistory> History { get; init; }

    [JsonIgnore]
    public string CandidateDisplay => string.Join(
        "; ",
        Candidates.Select(candidate => $"{candidate.Title ?? candidate.SongId} ({candidate.Reason})"));

    [JsonIgnore]
    public string HistoryDisplay => string.Join(
        "; ",
        History.Select(item => $"r{item.AfterRevision} {item.Action} ({item.Note})"));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReviewHistory
{
    [JsonPropertyName("action_id")]
    public required string ActionId { get; init; }
    [JsonPropertyName("action")]
    public required string Action { get; init; }
    [JsonPropertyName("before_status")]
    public required string BeforeStatus { get; init; }
    [JsonPropertyName("after_status")]
    public required string AfterStatus { get; init; }
    [JsonPropertyName("before_song_id")]
    public string? BeforeSongId { get; init; }
    [JsonPropertyName("after_song_id")]
    public string? AfterSongId { get; init; }
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
    [JsonPropertyName("note")]
    public required string Note { get; init; }
    [JsonPropertyName("action_at")]
    public required string ActionAt { get; init; }
    [JsonPropertyName("before_revision")]
    public required int BeforeRevision { get; init; }
    [JsonPropertyName("after_revision")]
    public required int AfterRevision { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AssignedSong
{
    [JsonPropertyName("song_id")]
    public required string SongId { get; init; }
    [JsonPropertyName("title")]
    public string? Title { get; init; }
    [JsonPropertyName("artist")]
    public string? Artist { get; init; }
    [JsonPropertyName("master_song_missing")]
    public required bool MasterSongMissing { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReviewCandidate
{
    [JsonPropertyName("song_id")]
    public required string SongId { get; init; }
    [JsonPropertyName("title")]
    public string? Title { get; init; }
    [JsonPropertyName("artist")]
    public string? Artist { get; init; }
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
    [JsonPropertyName("master_song_missing")]
    public required bool MasterSongMissing { get; init; }
}
