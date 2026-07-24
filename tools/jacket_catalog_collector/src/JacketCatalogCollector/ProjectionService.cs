using System.Text.Json;
using System.IO;

namespace JacketCatalogCollector;

public sealed class ProjectionJsonLoader
{
    private static readonly HashSet<string> CoverageStatuses =
        ["referenced", "needs_review", "uncollected", "unresolved"];
    private static readonly HashSet<string> ReviewStatuses =
        ["auto_confirmed", "manual_confirmed", "needs_review", "unresolved", "rejected", "orphaned"];
    private static readonly HashSet<string> StoredStatuses =
        ["auto_confirmed", "manual_confirmed", "needs_review", "unresolved", "rejected"];
    private static readonly HashSet<string> ReviewActions =
        ["manual_confirm", "reassign", "reject", "reopen"];
    private static readonly HashSet<string> CandidateClassifications =
        ["exact_unique", "alias_unique", "ambiguous", "no_candidate", "low_confidence",
            "evaluation_failed", "evaluation_unavailable", "not_eligible"];
    private static readonly HashSet<string> CandidateFieldStatuses =
        ["ok", "empty", "low_confidence", "engine_unavailable", "ocr_failed", "not_evaluated"];
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
        if (projection.ProjectionSchemaVersion != 6)
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
            RequireValue(song.Aliases, "songs.aliases");
            foreach (var alias in song.Aliases)
            {
                RequireString(alias, "songs.aliases row");
            }
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
            RequireString(review.CurrentStatus, "review_references.current_status");
            RequireString(review.ObservedTitle, "review_references.observed_title");
            RequireString(review.ObservedArtist, "review_references.observed_artist");
            RequireString(review.ObservationStatus, "review_references.observation_status");
            RequireText(review.FeatureExtractorVersion, "review_references.feature_extractor_version");
            RequireString(review.Notes, "review_references.notes");
            RequireString(review.RegisteredRoute, "review_references.registered_route");
            RequireText(review.ProcessedAt, "review_references.processed_at");
            if (review.SourceImagePath is not null
                && (string.IsNullOrWhiteSpace(review.SourceImagePath)
                    || !Path.IsPathFullyQualified(review.SourceImagePath)))
            {
                throw new InvalidOperationException(
                    "Projection source_image_path must be null or an absolute path.");
            }
            RequireValue(review.Candidates, "review_references.candidates");
            if (!ReviewStatuses.Contains(review.ReviewStatus))
            {
                throw new InvalidOperationException("Projection review status is invalid.");
            }
            if (!StoredStatuses.Contains(review.CurrentStatus)
                || review.CurrentStatus != review.StoredStatus
                || review.Notes != review.ManualNote)
            {
                throw new InvalidOperationException("Projection current review state is invalid.");
            }
            if (review.AssignedSong is not null)
            {
                RequireText(review.AssignedSong.SongId, "review_references.assigned_song.song_id");
            }
            if (review.CurrentSongId != review.AssignedSong?.SongId)
            {
                throw new InvalidOperationException("Projection current song state is invalid.");
            }
            foreach (var candidate in review.Candidates)
            {
                RequireValue(candidate, "review_references.candidates row");
                RequireText(candidate.SongId, "review_references.candidates.song_id");
                RequireString(candidate.Reason, "review_references.candidates.reason");
            }
            RequireString(review.StoredStatus, "review_references.stored_status");
            RequireString(review.ManualNote, "review_references.manual_note");
            RequireValue(review.History, "review_references.history");
            RequireValue(review.CandidateEvaluation, "review_references.candidate_evaluation");
            var evaluation = review.CandidateEvaluation;
            RequireText(evaluation.EvaluationSchemaVersion, "candidate_evaluation.evaluation_schema_version");
            RequireText(evaluation.MethodVersion, "candidate_evaluation.method_version");
            RequireString(evaluation.ObservationId, "candidate_evaluation.observation_id");
            RequireText(evaluation.Reason, "candidate_evaluation.reason");
            RequireValue(evaluation.Title, "candidate_evaluation.title");
            RequireValue(evaluation.Artist, "candidate_evaluation.artist");
            RequireValue(evaluation.Candidates, "candidate_evaluation.candidates");
            if (evaluation.EvaluationSchemaVersion != "m5c-unresolved-candidate-evaluation-v1"
                || !CandidateClassifications.Contains(evaluation.Classification)
                || evaluation.Title.Confidence is < 0 or > 1
                || evaluation.Artist.Confidence is < 0 or > 1
                || !CandidateFieldStatuses.Contains(evaluation.Title.Status)
                || !CandidateFieldStatuses.Contains(evaluation.Artist.Status))
            {
                throw new InvalidOperationException("Projection candidate evaluation is invalid.");
            }
            foreach (var field in new[] { evaluation.Title, evaluation.Artist })
            {
                RequireString(field.Raw, "candidate_evaluation field raw");
                RequireString(field.Normalized, "candidate_evaluation field normalized");
                RequireString(field.FailureReason, "candidate_evaluation field failure_reason");
            }
            foreach (var candidate in evaluation.Candidates)
            {
                RequireText(candidate.SongId, "candidate_evaluation.candidates.song_id");
                RequireText(candidate.Title, "candidate_evaluation.candidates.title");
                RequireText(candidate.Artist, "candidate_evaluation.candidates.artist");
            }
            if ((evaluation.Classification is "exact_unique" or "alias_unique"
                    && evaluation.Candidates.Count != 1)
                || (evaluation.Classification == "ambiguous" && evaluation.Candidates.Count < 1)
                || (evaluation.Classification is "no_candidate" or "low_confidence"
                        or "evaluation_failed" or "evaluation_unavailable" or "not_eligible"
                    && evaluation.Candidates.Count != 0)
                || (evaluation.Classification == "not_eligible"
                    && review.StoredStatus == "unresolved")
                || (evaluation.Classification != "not_eligible"
                    && review.StoredStatus != "unresolved"))
            {
                throw new InvalidOperationException(
                    "Projection candidate evaluation classification is inconsistent.");
            }
            if (review.Revision < 0 || !StoredStatuses.Contains(review.StoredStatus))
            {
                throw new InvalidOperationException("Projection review revision or stored status is invalid.");
            }
            var expectedRevision = 0;
            string? previousStatus = null;
            string? previousSongId = null;
            foreach (var history in review.History)
            {
                RequireText(history.ActionId, "review_references.history.action_id");
                RequireString(history.Reason, "review_references.history.reason");
                RequireString(history.Note, "review_references.history.note");
                RequireText(history.ActionAt, "review_references.history.action_at");
                if (!ReviewActions.Contains(history.Action)
                    || !StoredStatuses.Contains(history.BeforeStatus)
                    || !StoredStatuses.Contains(history.AfterStatus)
                    || history.BeforeRevision != expectedRevision
                    || history.AfterRevision != history.BeforeRevision + 1)
                {
                    throw new InvalidOperationException("Projection review history is invalid.");
                }
                if (expectedRevision > 0
                    && (history.BeforeStatus != previousStatus
                        || history.BeforeSongId != previousSongId))
                {
                    throw new InvalidOperationException(
                        "Projection review history state is discontinuous.");
                }
                if ((history.Action is "manual_confirm" or "reassign"
                        && (history.AfterSongId is null
                            || history.AfterStatus != "manual_confirmed"))
                    || (history.Action == "reject"
                        && (history.AfterSongId != history.BeforeSongId
                            || history.AfterStatus != "rejected"))
                    || (history.Action == "reopen"
                        && (history.AfterSongId is not null
                            || history.AfterStatus != "needs_review")))
                {
                    throw new InvalidOperationException(
                        "Projection review history action semantics are invalid.");
                }
                if ((history.Action == "manual_confirm"
                        && history.BeforeStatus is not ("needs_review" or "unresolved" or "rejected"))
                    || (history.Action == "reassign"
                        && history.BeforeStatus is not ("auto_confirmed" or "manual_confirmed"))
                    || (history.Action == "reopen" && history.BeforeStatus != "rejected"))
                {
                    throw new InvalidOperationException(
                        "Projection review history action source is invalid.");
                }
                previousStatus = history.AfterStatus;
                previousSongId = history.AfterSongId;
                expectedRevision = history.AfterRevision;
            }
            if (expectedRevision != review.Revision)
            {
                throw new InvalidOperationException("Projection review history revision is inconsistent.");
            }
            if (review.History.Count > 0)
            {
                var last = review.History[^1];
                if (review.StoredStatus != last.AfterStatus
                    || review.AssignedSong?.SongId != last.AfterSongId
                    || review.ManualActionId != last.ActionId
                    || review.ManualNote != last.Note)
                {
                    throw new InvalidOperationException(
                        "Projection current review state does not match history.");
                }
            }
            else if (review.Revision != 0
                || review.StoredStatus is "manual_confirmed" or "rejected"
                || review.ManualActionId is not null
                || review.ManualNote != "")
            {
                throw new InvalidOperationException(
                    "Projection review row has manual state without history.");
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
    string pythonExecutable = "python",
    string? artifactRoot = null) : IProjectionService
{
    public async Task<ReviewProjection> LoadAsync(
        string masterPath,
        string catalogPath,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-X",
            "utf8",
            "-m",
            "tools.vision_poc.jacket_catalog_review_projection",
            "--catalog",
            Path.GetFullPath(catalogPath),
            "--master-db",
            Path.GetFullPath(masterPath),
        };
        if (artifactRoot is not null)
        {
            arguments.Add("--artifact-root");
            arguments.Add(Path.GetFullPath(artifactRoot));
        }
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                arguments,
                repositoryRoot),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Catalog projection failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
        return loader.Load(result.StandardOutput);
    }

    public async Task GenerateReportAsync(
        string masterPath,
        string catalogPath,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (artifactRoot is null)
        {
            throw new InvalidOperationException("Candidate artifact root is not configured.");
        }
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                [
                    "-X", "utf8", "-m", "tools.vision_poc.jacket_catalog_review_projection",
                    "--catalog", Path.GetFullPath(catalogPath),
                    "--master-db", Path.GetFullPath(masterPath),
                    "--artifact-root", Path.GetFullPath(artifactRoot),
                    "--report-output-dir", Path.GetFullPath(outputDirectory),
                ],
                repositoryRoot),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Candidate report failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
        loader.Load(result.StandardOutput);
    }

    public async Task ExportManualReviewXlsxAsync(
        string masterPath,
        string catalogPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (artifactRoot is null)
        {
            throw new InvalidOperationException("Manual review XLSX export requires candidate artifact root.");
        }
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                [
                    "-X", "utf8", "-m", "tools.vision_poc.jacket_catalog_review_projection",
                    "--catalog", Path.GetFullPath(catalogPath),
                    "--master-db", Path.GetFullPath(masterPath),
                    "--artifact-root", Path.GetFullPath(artifactRoot),
                    "--manual-xlsx-output", Path.GetFullPath(outputPath),
                ],
                repositoryRoot),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Manual review XLSX export failed (exit {result.ExitCode}): "
                + result.StandardError.Trim());
        }
        loader.Load(result.StandardOutput);
    }
}
