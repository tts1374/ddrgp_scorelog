using System.Text.Json;
using System.IO;

namespace JacketCatalogCollector;

public sealed class ProjectionJsonLoader
{
    private static readonly HashSet<string> CoverageStatuses =
        ["referenced", "needs_review", "uncollected", "unresolved"];
    private static readonly HashSet<string> ReviewStatuses =
        ["needs_review", "unresolved", "orphaned"];
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public ReviewProjection Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Projection stdout is empty.");
        }
        ReviewProjection projection;
        try
        {
            projection = JsonSerializer.Deserialize<ReviewProjection>(json, Options)
                ?? throw new InvalidOperationException("Projection JSON is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Projection JSON is invalid.", exception);
        }
        Validate(projection);
        return projection;
    }

    private static void Validate(ReviewProjection projection)
    {
        if (projection.ProjectionSchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported projection schema version: {projection.ProjectionSchemaVersion}");
        }
        RequireValue(projection.Master, "master");
        RequireValue(projection.Catalog, "catalog");
        RequireValue(projection.Coverage, "coverage");
        RequireValue(projection.Songs, "songs");
        RequireValue(projection.ReviewReferences, "review_references");
        RequireValue(projection.Coverage.StatusCounts, "coverage.status_counts");
        RequireValue(projection.Coverage.OrphanReasonCounts, "coverage.orphan_reason_counts");
        RequireText(projection.Master.Path, "master.path");
        RequireText(projection.Master.MasterVersion, "master.master_version");
        RequireText(projection.Master.SourceHash, "master.source_hash");
        if (projection.Master.SongCount <= 0
            || projection.Master.ChartCount <= 0
            || projection.Master.GrandPrixSongCount < 0)
        {
            throw new InvalidOperationException("Projection master counts are invalid.");
        }
        RequireText(projection.Catalog.Path, "catalog.path");
        RequireText(projection.Catalog.CatalogIdentity, "catalog.catalog_identity");
        RequireString(projection.Catalog.CreatedAt, "catalog.created_at");
        RequireText(
            projection.Catalog.CurrentFeatureExtractorVersion,
            "catalog.current_feature_extractor_version");
        if (projection.Catalog.CatalogIdentity != "ddrgp-local-jacket-reference-catalog"
            || projection.Catalog.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Projection catalog identity or schema is unsupported.");
        }
        if (projection.Master.GrandPrixSongCount != projection.Coverage.GrandPrixSongCount
            || projection.Songs.Count != projection.Coverage.GrandPrixSongCount)
        {
            throw new InvalidOperationException("Projection GP coverage denominator is inconsistent.");
        }
        if (projection.Coverage.StatusCounts.Keys.Any(status => !CoverageStatuses.Contains(status))
            || projection.Coverage.StatusCounts.Values.Any(count => count < 0)
            || projection.Coverage.StatusCounts.Values.Sum() != projection.Coverage.GrandPrixSongCount)
        {
            throw new InvalidOperationException("Projection coverage status counts are invalid.");
        }
        foreach (var song in projection.Songs)
        {
            RequireValue(song, "songs row");
            RequireText(song.SongId, "songs.song_id");
            RequireText(song.Title, "songs.title");
            RequireText(song.Artist, "songs.artist");
            RequireString(song.MasterVersion, "songs.master_version");
            RequireString(song.Reason, "songs.reason");
            if (!CoverageStatuses.Contains(song.CoverageStatus)
                || !int.TryParse(song.ReferenceCount, out var referenceCount)
                || referenceCount < 0)
            {
                throw new InvalidOperationException("Projection song row is invalid.");
            }
        }
        var songStatusCounts = projection.Songs
            .GroupBy(song => song.CoverageStatus, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (CoverageStatuses.Any(
                status => songStatusCounts.GetValueOrDefault(status)
                    != projection.Coverage.StatusCounts.GetValueOrDefault(status)))
        {
            throw new InvalidOperationException(
                "Projection coverage counts do not match the song status histogram.");
        }
        foreach (var review in projection.ReviewReferences)
        {
            RequireValue(review, "review_references row");
            RequireText(review.ReferenceId, "review_references.reference_id");
            RequireString(review.Reason, "review_references.reason");
            RequireString(review.ObservedTitle, "review_references.observed_title");
            RequireString(review.ObservedArtist, "review_references.observed_artist");
            RequireString(review.ObservationStatus, "review_references.observation_status");
            RequireText(review.FeatureExtractorVersion, "review_references.feature_extractor_version");
            RequireValue(review.Candidates, "review_references.candidates");
            if (!ReviewStatuses.Contains(review.ReviewStatus))
            {
                throw new InvalidOperationException("Projection review status is invalid.");
            }
            if (review.AssignedSong is not null)
            {
                RequireText(review.AssignedSong.SongId, "review_references.assigned_song.song_id");
            }
            foreach (var candidate in review.Candidates)
            {
                RequireValue(candidate, "review_references.candidates row");
                RequireText(candidate.SongId, "review_references.candidates.song_id");
                RequireString(candidate.Reason, "review_references.candidates.reason");
            }
        }
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Projection field is empty: {field}");
        }
    }

    private static void RequireString(string? value, string field)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Projection field is null: {field}");
        }
    }

    private static void RequireValue<T>(T? value, string field)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"Projection field is null: {field}");
        }
    }
}

public interface IProjectionService
{
    Task<ReviewProjection> LoadAsync(
        string masterPath,
        string catalogPath,
        CancellationToken cancellationToken);
}

public sealed class ProjectionService(
    IProcessRunner processRunner,
    ProjectionJsonLoader loader,
    string repositoryRoot,
    string pythonExecutable = "python") : IProjectionService
{
    public async Task<ReviewProjection> LoadAsync(
        string masterPath,
        string catalogPath,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                [
                    "-X",
                    "utf8",
                    "-m",
                    "tools.vision_poc.jacket_catalog_review_projection",
                    "--catalog",
                    Path.GetFullPath(catalogPath),
                    "--master-db",
                    Path.GetFullPath(masterPath),
                ],
                repositoryRoot),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Catalog projection failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
        return loader.Load(result.StandardOutput);
    }
}
