using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Data;

public sealed record PersonalScoreDbWorkflowResult(
    string WorkflowStatus,
    string ArtifactStatus,
    string AdapterStatus,
    string DatabaseStatus,
    bool Written,
    string? SourceCaptureId,
    string? AnalysisId,
    string? PlayId,
    IReadOnlyList<string> Reasons,
    string? ArtifactPath,
    string DatabasePath);

public interface IPersonalScoreDbWorkflowRunner
{
    Task<PersonalScoreDbWorkflowResult> RunAsync(
        string workflowInputPath,
        string scoreDatabasePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the formal personal-score workflow inside the application process.
/// Candidate material is parsed but is never promoted to formal play values.
/// </summary>
public sealed class AppOwnedPersonalScoreDbWorkflowRunner : IPersonalScoreDbWorkflowRunner
{
    private readonly Func<ViewerDatabasePaths> pathsResolver;

    public AppOwnedPersonalScoreDbWorkflowRunner()
        : this(ViewerDatabasePaths.ResolveDefault)
    {
    }

    internal AppOwnedPersonalScoreDbWorkflowRunner(
        Func<ViewerDatabasePaths> pathsResolver)
    {
        this.pathsResolver = pathsResolver;
    }

    public async Task<PersonalScoreDbWorkflowResult> RunAsync(
        string workflowInputPath,
        string scoreDatabasePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workflow = await LoadWorkflowAsync(workflowInputPath, cancellationToken);
            return RunWorkflow(workflow, scoreDatabasePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsWorkflowInputFailure(exception))
        {
            return FailedResult(
                scoreDatabasePath,
                "invalid",
                "not_requested",
                "invalid",
                "not_checked",
                exception.Message);
        }
    }

    internal Task<PersonalScoreDbWorkflowResult> RunAdapterInputAsync(
        AppSaveAdapterInput input,
        string scoreDatabasePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RunAdapterInput(input, scoreDatabasePath, null, cancellationToken));
    }

    private PersonalScoreDbWorkflowResult RunWorkflow(
        AppWorkflowInput workflow,
        string scoreDatabasePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = RunAdapterInput(
            workflow.SaveInput,
            scoreDatabasePath,
            workflow.AnalysisDetail,
            cancellationToken);
        return result;
    }

    private PersonalScoreDbWorkflowResult RunAdapterInput(
        AppSaveAdapterInput input,
        string scoreDatabasePath,
        JsonElement? analysisDetail,
        CancellationToken cancellationToken)
    {
        var fullDatabasePath = SafeFullPath(scoreDatabasePath);
        var adapter = AppSaveInputAdapter.Adapt(input);
        if (adapter.Status == "unresolved" || adapter.SaveInput is null)
        {
            return new PersonalScoreDbWorkflowResult(
                "unresolved",
                "not_requested",
                "unresolved",
                "not_checked",
                false,
                null,
                null,
                null,
                adapter.Reasons,
                input.LogPath.Length == 0 ? null : input.LogPath,
                fullDatabasePath);
        }

        var saveInput = adapter.SaveInput;
        var artifactStatus = "not_requested";
        string? artifactPath = input.LogPath.Length == 0 ? null : input.LogPath;
        try
        {
            ValidateArtifactContract(saveInput, analysisDetail, input.LogPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException or
                UnauthorizedAccessException or JsonException)
        {
            return new PersonalScoreDbWorkflowResult(
                "invalid",
                "not_requested",
                adapter.Status,
                "not_checked",
                false,
                null,
                null,
                null,
                [exception.Message],
                artifactPath,
                fullDatabasePath);
        }

        try
        {
            if (analysisDetail is not null)
            {
                artifactStatus = PublishAnalysisDetail(
                    analysisDetail.Value,
                    input.LogPath,
                    pathsResolver().ApplicationRootDirectory);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException or
                UnauthorizedAccessException or JsonException)
        {
            return new PersonalScoreDbWorkflowResult(
                artifactStatus == "not_requested" ? "artifact_failed" : "artifact_conflict",
                artifactStatus == "not_requested" ? "failed" : "conflict",
                adapter.Status,
                "not_checked",
                false,
                null,
                null,
                null,
                [exception.Message],
                artifactPath,
                fullDatabasePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var written = AppFormalScoreDbWriter.Write(fullDatabasePath, saveInput);
            var workflowStatus = written.Duplicate
                ? "duplicate"
                : written.PlayId is not null
                    ? "saved"
                    : "excluded";
            var reasons = adapter.Status == "excluded"
                ? adapter.Reasons
                : written.Duplicate
                    ? [written.SkipReason]
                    : Array.Empty<string>();
            var adapterStatus = written.Duplicate ? "excluded" : adapter.Status;
            return new PersonalScoreDbWorkflowResult(
                workflowStatus,
                artifactStatus,
                adapterStatus,
                "written",
                true,
                written.SourceCaptureId,
                written.AnalysisId,
                written.PlayId,
                reasons,
                artifactPath,
                fullDatabasePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException or
                UnauthorizedAccessException or SqliteException or ViewerDatabaseException)
        {
            return new PersonalScoreDbWorkflowResult(
                artifactStatus is "created" or "reused"
                    ? "artifact_created_db_failed"
                    : "db_rejected",
                artifactStatus,
                adapter.Status,
                "failed",
                false,
                null,
                null,
                null,
                [exception.Message],
                artifactPath,
                fullDatabasePath);
        }
    }

    private static async Task<AppWorkflowInput> LoadWorkflowAsync(
        string workflowInputPath,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(workflowInputPath);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        StrictJsonObjectValidator.ValidateNoDuplicateKeys(bytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RequireObject(root, "workflow input");
        RequireKeys(root, ["workflow_schema_version", "analysis_detail", "save_input"], []);
        if (!root.GetProperty("workflow_schema_version").TryGetInt32(out var version) || version != 1)
        {
            throw new JsonException("workflow_schema_version must be 1");
        }

        var detailValue = root.GetProperty("analysis_detail");
        JsonElement? detail = detailValue.ValueKind == JsonValueKind.Null
            ? null
            : detailValue.ValueKind == JsonValueKind.Object
                ? detailValue.Clone()
                : throw new JsonException("analysis_detail must be an object or null");
        return new AppWorkflowInput(
            detail,
            ParseSaveInput(root.GetProperty("save_input")));
    }

    private static AppSaveAdapterInput ParseSaveInput(JsonElement root)
    {
        RequireObject(root, "save_input");
        var required = new[]
        {
            "input_schema_version", "candidate_material", "capture_id", "capture_hash",
            "captured_at", "source_kind", "source_path", "analysis_id", "event_type",
            "confirmed_result", "duplicate", "confirmation_mode", "identity_signal_status",
            "digit_review_status", "analysis_confidence", "analysis_summary_json", "app_version",
        };
        var optional = new[]
        {
            "formal_play", "exclusion", "manifest_image_path", "frame_index", "timestamp_ms",
            "candidate_duration_ms", "log_path",
        };
        RequireKeys(root, required, optional);
        if (!root.GetProperty("input_schema_version").TryGetInt32(out var version) || version != 1)
        {
            throw new JsonException("input_schema_version must be 1");
        }

        var candidateMaterial = ParseStringMap(
            root.GetProperty("candidate_material"),
            "candidate_material");
        var formal = root.TryGetProperty("formal_play", out var formalValue)
            ? ParseFormalPlay(formalValue)
            : null;
        var exclusion = root.TryGetProperty("exclusion", out var exclusionValue)
            ? ParseExclusion(exclusionValue)
            : null;

        return new AppSaveAdapterInput(
            candidateMaterial,
            RequiredString(root, "capture_id"),
            RequiredString(root, "capture_hash"),
            RequiredString(root, "captured_at"),
            RequiredString(root, "source_kind"),
            RequiredString(root, "source_path"),
            RequiredString(root, "analysis_id"),
            RequiredString(root, "event_type"),
            RequiredBoolean(root, "confirmed_result"),
            RequiredBoolean(root, "duplicate"),
            RequiredString(root, "confirmation_mode"),
            RequiredString(root, "identity_signal_status"),
            RequiredString(root, "digit_review_status"),
            OptionalNumber(root, "analysis_confidence"),
            RequiredString(root, "analysis_summary_json"),
            RequiredString(root, "app_version"),
            formal,
            exclusion,
            OptionalString(root, "manifest_image_path") ?? string.Empty,
            OptionalInt(root, "frame_index"),
            OptionalLong(root, "timestamp_ms"),
            OptionalLong(root, "candidate_duration_ms"),
            OptionalString(root, "log_path") ?? string.Empty);
    }

    private static AppFormalPlay? ParseFormalPlay(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(value, "formal_play");
        var requiredText = new[]
        {
            "play_id", "played_at", "master_version", "song_id", "chart_id", "rank",
            "clear_type", "duplicate_key",
        };
        var requiredIntegers = new[]
        {
            "score", "max_combo", "marvelous", "perfect", "great", "good", "miss", "ex_score",
        };
        RequireKeys(value, requiredText.Concat(requiredIntegers), ["flare_rank", "ok", "calories"]);
        return new AppFormalPlay(
            RequiredString(value, "play_id"),
            RequiredString(value, "played_at"),
            RequiredString(value, "master_version"),
            RequiredString(value, "song_id"),
            RequiredString(value, "chart_id"),
            OptionalInt(value, "score"),
            OptionalInt(value, "max_combo"),
            OptionalInt(value, "marvelous"),
            OptionalInt(value, "perfect"),
            OptionalInt(value, "great"),
            OptionalInt(value, "good"),
            OptionalInt(value, "miss"),
            OptionalInt(value, "ex_score"),
            RequiredString(value, "rank"),
            RequiredString(value, "clear_type"),
            OptionalString(value, "flare_rank"),
            OptionalInt(value, "ok"),
            OptionalNumber(value, "calories"),
            RequiredString(value, "duplicate_key"));
    }

    private static AppSaveExclusion? ParseExclusion(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireObject(value, "exclusion");
        RequireKeys(value, ["kind", "reason"], []);
        return new AppSaveExclusion(
            RequiredString(value, "kind"),
            RequiredString(value, "reason"));
    }

    private static Dictionary<string, string> ParseStringMap(
        JsonElement value,
        string fieldName)
    {
        RequireObject(value, fieldName);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"{fieldName}.{property.Name} must be a string");
            }
            result.Add(property.Name, property.Value.GetString() ?? string.Empty);
        }
        return result;
    }

    private static void ValidateArtifactContract(
        AppSaveInput saveInput,
        JsonElement? detail,
        string logPath)
    {
        var required = saveInput.Analysis.AnalysisStatus is "low_confidence" or "error";
        if (required && (detail is null || logPath.Length == 0))
        {
            throw new InvalidOperationException(
                "analysis artifact is required for low-confidence or error analysis");
        }
        if (detail is null && logPath.Length > 0)
        {
            throw new InvalidOperationException(
                "analysis.log_path must be empty without analysis_detail");
        }
        if (detail is not null && logPath.Length == 0)
        {
            throw new InvalidOperationException(
                "analysis_detail requires log_path");
        }
        if (logPath.Length > 0)
        {
            AppAnalysisDetailValidator.ValidateLogPath(logPath);
        }
        if (detail is not null)
        {
            AppAnalysisDetailValidator.Validate(detail.Value);
            var mismatches = AppAnalysisDetailValidator.SharedValueMismatches(
                detail.Value,
                saveInput.Analysis,
                logPath);
            if (mismatches.Count > 0)
            {
                throw new InvalidOperationException(string.Join(", ", mismatches));
            }
        }
    }

    private static string PublishAnalysisDetail(
        JsonElement detail,
        string logPath,
        string applicationRoot)
    {
        AppAnalysisDetailValidator.ValidateLogPath(logPath);
        var root = Path.GetFullPath(applicationRoot);
        var target = Path.GetFullPath(Path.Combine(
            root,
            logPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(target, root) || string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("analysis detail output escapes application data");
        }

        var payload = JsonSerializer.Serialize(
                detail,
                new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine;
        if (File.Exists(target))
        {
            using var existing = JsonDocument.Parse(File.ReadAllText(target, Encoding.UTF8));
            if (!JsonElementsEqual(existing.RootElement, detail))
            {
                throw new InvalidOperationException("existing analysis artifact payload differs");
            }
            return "reused";
        }

        var directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("analysis detail directory could not be determined");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporary,
                payload,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            try
            {
                File.Move(temporary, target);
            }
            catch (IOException) when (File.Exists(target))
            {
                using var existing = JsonDocument.Parse(File.ReadAllText(target, Encoding.UTF8));
                if (JsonElementsEqual(existing.RootElement, detail))
                {
                    return "reused";
                }
                throw new InvalidOperationException("existing analysis artifact payload differs");
            }
            return "created";
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool JsonElementsEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object =>
                left.EnumerateObject().Count() == right.EnumerateObject().Count() &&
                left.EnumerateObject().All(property =>
                    right.TryGetProperty(property.Name, out var other) &&
                    JsonElementsEqual(property.Value, other)),
            JsonValueKind.Array => left.EnumerateArray()
                .Zip(right.EnumerateArray())
                .All(pair => JsonElementsEqual(pair.First, pair.Second)) &&
                left.GetArrayLength() == right.GetArrayLength(),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.TryGetDouble(out var leftNumber) &&
                right.TryGetDouble(out var rightNumber) && leftNumber == rightNumber,
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false,
        };
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{name} must be an object");
        }
    }

    private static void RequireKeys(
        JsonElement value,
        IEnumerable<string> required,
        IEnumerable<string> optional)
    {
        var requiredSet = required.ToHashSet(StringComparer.Ordinal);
        var allowed = requiredSet.Concat(optional).ToHashSet(StringComparer.Ordinal);
        var actual = value.EnumerateObject().Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missing = requiredSet.Except(actual, StringComparer.Ordinal).ToArray();
        var unknown = actual.Except(allowed, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new JsonException(
                $"JSON keys are invalid; missing=[{string.Join(",", missing)}], " +
                $"unknown=[{string.Join(",", unknown)}]");
        }
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{name} must be a string");
        }
        return value.GetString() ?? string.Empty;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{name} must be a string or null");
        }
        return value.GetString() ?? string.Empty;
    }

    private static bool RequiredBoolean(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonException($"{name} must be a boolean");
        }
        return value.GetBoolean();
    }

    private static int? OptionalInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (!value.TryGetInt32(out var result))
        {
            throw new JsonException($"{name} must be an integer or null");
        }
        return result;
    }

    private static long? OptionalLong(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (!value.TryGetInt64(out var result))
        {
            throw new JsonException($"{name} must be an integer or null");
        }
        return result;
    }

    private static double? OptionalNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result))
        {
            throw new JsonException($"{name} must be a number or null");
        }
        return result;
    }

    private static bool IsWorkflowInputFailure(Exception exception) =>
        exception is ArgumentException or IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or FormatException;

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return path;
        }
    }

    private static bool IsWithin(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static PersonalScoreDbWorkflowResult FailedResult(
        string path,
        string workflowStatus,
        string artifactStatus,
        string adapterStatus,
        string databaseStatus,
        string reason) =>
        new(
            workflowStatus,
            artifactStatus,
            adapterStatus,
            databaseStatus,
            false,
            null,
            null,
            null,
            [reason],
            null,
            SafeFullPath(path));

    private sealed record AppWorkflowInput(
        JsonElement? AnalysisDetail,
        AppSaveAdapterInput SaveInput);
}

internal sealed record AppSaveAdapterInput(
    IReadOnlyDictionary<string, string> CandidateMaterial,
    string CaptureId,
    string CaptureHash,
    string CapturedAt,
    string SourceKind,
    string SourcePath,
    string AnalysisId,
    string EventType,
    bool ConfirmedResult,
    bool Duplicate,
    string ConfirmationMode,
    string IdentitySignalStatus,
    string DigitReviewStatus,
    double? AnalysisConfidence,
    string AnalysisSummaryJson,
    string AppVersion,
    AppFormalPlay? FormalPlay,
    AppSaveExclusion? Exclusion,
    string ManifestImagePath,
    int? FrameIndex,
    long? TimestampMs,
    long? CandidateDurationMs,
    string LogPath,
    IReadOnlyList<string>? FormalEvidenceReasons = null);

internal sealed record AppFormalPlay(
    string PlayId,
    string PlayedAt,
    string MasterVersion,
    string SongId,
    string ChartId,
    int? Score,
    int? MaxCombo,
    int? Marvelous,
    int? Perfect,
    int? Great,
    int? Good,
    int? Miss,
    int? ExScore,
    string Rank,
    string ClearType,
    string? FlareRank,
    int? Ok,
    double? Calories,
    string DuplicateKey);

internal sealed record AppSaveExclusion(string Kind, string Reason);

internal sealed record AppSaveAdapterResult(
    string Status,
    IReadOnlyList<string> Reasons,
    AppSaveInput? SaveInput);

internal sealed record AppSaveInput(
    AppSourceCapture Source,
    AppFormalPlayInput? Play,
    AppAnalysisInput Analysis);

internal sealed record AppSourceCapture(
    string CaptureId,
    string CaptureHash,
    string CapturedAt,
    string SourceKind,
    string SourcePath,
    string ManifestImagePath,
    int? FrameIndex);

internal sealed record AppFormalPlayInput(
    string PlayId,
    string PlayedAt,
    string MasterVersion,
    string SongId,
    string ChartId,
    int Score,
    int MaxCombo,
    int Marvelous,
    int Perfect,
    int Great,
    int Good,
    int Miss,
    int ExScore,
    string Rank,
    string ClearType,
    string? FlareRank,
    int? Ok,
    double? Calories,
    string CaptureHash,
    string SourceCaptureId,
    string DuplicateKey,
    double AnalysisConfidence,
    string AppVersion);

internal sealed record AppAnalysisInput(
    string AnalysisId,
    string? PlayId,
    string SourceCaptureId,
    string AnalysisStatus,
    string SaveBoundaryStatus,
    string SkipReason,
    string EventType,
    bool ConfirmedResult,
    bool Duplicate,
    string ConfirmationMode,
    long? TimestampMs,
    long? CandidateDurationMs,
    string IdentitySignalStatus,
    string DigitReviewStatus,
    double? AnalysisConfidence,
    string AnalysisSummaryJson,
    string LogPath,
    string AppVersion);

internal static class AppSaveInputAdapter
{
    private static readonly string[] WritableSourceKinds =
        ["manifest", "timestamped", "capture", "manual"];

    private static readonly string[] ExclusionKinds =
        ["duplicate", "low_confidence", "skipped", "error"];

    public static AppSaveAdapterResult Adapt(AppSaveAdapterInput input)
    {
        var source = new AppSourceCapture(
            input.CaptureId,
            input.CaptureHash,
            input.CapturedAt,
            input.SourceKind,
            input.SourcePath,
            input.ManifestImagePath,
            input.FrameIndex);
        var exclusion = input.Exclusion;
        if (input.Duplicate)
        {
            exclusion = new AppSaveExclusion(
                "duplicate",
                exclusion?.Kind == "duplicate" ? exclusion.Reason : "duplicate_result");
        }

        if (exclusion is not null)
        {
            if (!ExclusionKinds.Contains(exclusion.Kind, StringComparer.Ordinal))
            {
                return Unresolved("exclusion.kind_invalid");
            }
            if (string.IsNullOrWhiteSpace(exclusion.Reason))
            {
                return Unresolved("exclusion.reason_required");
            }

            var values = exclusion.Kind switch
            {
                "duplicate" => ("skipped", "duplicate", true),
                "low_confidence" => ("low_confidence", "low_confidence", false),
                "skipped" => ("skipped", "excluded", false),
                "error" => ("error", "error", false),
                _ => throw new InvalidOperationException("unknown exclusion kind"),
            };
            var analysis = CreateAnalysis(
                input,
                null,
                values.Item1,
                values.Item2,
                exclusion.Reason,
                values.Item3);
            var saveInput = new AppSaveInput(source, null, analysis);
            var errors = AppFormalValidation.Errors(saveInput);
            return errors.Count > 0
                ? UnresolvedReasons(errors)
                : new AppSaveAdapterResult("excluded", [exclusion.Reason], saveInput);
        }

        if (input.FormalEvidenceReasons is { Count: > 0 })
        {
            return UnresolvedReasons(input.FormalEvidenceReasons);
        }

        if (input.FormalPlay is null)
        {
            return Unresolved("formal_play_required");
        }

        var formal = input.FormalPlay;
        var missing = MissingFormalValues(formal);
        if (missing.Count > 0)
        {
            return UnresolvedReasons(missing);
        }
        var confidence = input.AnalysisConfidence ?? -1.0;
        var play = new AppFormalPlayInput(
            formal.PlayId,
            formal.PlayedAt,
            formal.MasterVersion,
            formal.SongId,
            formal.ChartId,
            formal.Score!.Value,
            formal.MaxCombo!.Value,
            formal.Marvelous!.Value,
            formal.Perfect!.Value,
            formal.Great!.Value,
            formal.Good!.Value,
            formal.Miss!.Value,
            formal.ExScore!.Value,
            formal.Rank,
            formal.ClearType,
            formal.FlareRank,
            formal.Ok,
            formal.Calories,
            input.CaptureHash,
            input.CaptureId,
            formal.DuplicateKey,
            confidence,
            input.AppVersion);
        var analysisWithPlay = CreateAnalysis(
            input,
            play.PlayId,
            "saved",
            "save_ready",
            "",
            false);
        var readyInput = new AppSaveInput(source, play, analysisWithPlay);
        var errorsForReady = AppFormalValidation.Errors(readyInput);
        return errorsForReady.Count > 0
            ? UnresolvedReasons(errorsForReady)
            : new AppSaveAdapterResult("ready", [], readyInput);

        AppSaveAdapterResult Unresolved(string reason) =>
            new("unresolved", [reason], null);
        AppSaveAdapterResult UnresolvedReasons(IReadOnlyList<string> reasons) =>
            new("unresolved", reasons, null);
    }

    private static AppAnalysisInput CreateAnalysis(
        AppSaveAdapterInput input,
        string? playId,
        string status,
        string boundary,
        string skipReason,
        bool duplicate) =>
        new(
            input.AnalysisId,
            playId,
            input.CaptureId,
            status,
            boundary,
            skipReason,
            input.EventType,
            input.ConfirmedResult,
            duplicate,
            input.ConfirmationMode,
            input.TimestampMs,
            input.CandidateDurationMs,
            input.IdentitySignalStatus,
            input.DigitReviewStatus,
            input.AnalysisConfidence,
            input.AnalysisSummaryJson,
            input.LogPath,
            input.AppVersion);

    private static IReadOnlyList<string> MissingFormalValues(AppFormalPlay formal)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(formal.PlayId)) missing.Add("formal_play.play_id_required");
        if (string.IsNullOrWhiteSpace(formal.PlayedAt)) missing.Add("formal_play.played_at_required");
        if (string.IsNullOrWhiteSpace(formal.MasterVersion)) missing.Add("formal_play.master_version_required");
        if (string.IsNullOrWhiteSpace(formal.SongId)) missing.Add("formal_play.song_id_required");
        if (string.IsNullOrWhiteSpace(formal.ChartId)) missing.Add("formal_play.chart_id_required");
        if (string.IsNullOrWhiteSpace(formal.Rank)) missing.Add("formal_play.rank_required");
        if (string.IsNullOrWhiteSpace(formal.ClearType)) missing.Add("formal_play.clear_type_required");
        if (string.IsNullOrWhiteSpace(formal.DuplicateKey)) missing.Add("formal_play.duplicate_key_required");
        if (formal.Score is null) missing.Add("formal_play.score_required");
        if (formal.MaxCombo is null) missing.Add("formal_play.max_combo_required");
        if (formal.Marvelous is null) missing.Add("formal_play.marvelous_required");
        if (formal.Perfect is null) missing.Add("formal_play.perfect_required");
        if (formal.Great is null) missing.Add("formal_play.great_required");
        if (formal.Good is null) missing.Add("formal_play.good_required");
        if (formal.Miss is null) missing.Add("formal_play.miss_required");
        if (formal.ExScore is null) missing.Add("formal_play.ex_score_required");
        return missing;
    }
}

internal static class AppFormalValidation
{
    private static readonly string[] WritableSourceKinds =
        ["manifest", "timestamped", "capture", "manual"];

    private static readonly string[] AnalysisStatuses =
        ["saved", "skipped", "low_confidence", "error"];

    private static readonly string[] RankValues =
        ["AAA", "AA+", "AA", "AA-", "A+", "A", "A-", "B+", "B", "B-", "C+", "C", "C-", "D+", "D", "E"];

    private static readonly string[] ClearValues =
        ["FAILED", "MFC", "PFC", "GFC", "FULL COMBO", "CLEAR"];

    private static readonly string[] FlareValues =
        ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"];

    public static IReadOnlyList<string> Errors(AppSaveInput input)
    {
        var errors = new List<string>();
        var source = input.Source;
        RequireText(errors, "source_capture.capture_id", source.CaptureId);
        RequireText(errors, "source_capture.capture_hash", source.CaptureHash);
        RequireTimestamp(errors, "source_capture.captured_at", source.CapturedAt);
        RequireText(errors, "source_capture.source_path", source.SourcePath);
        if (!WritableSourceKinds.Contains(source.SourceKind, StringComparer.Ordinal))
        {
            errors.Add("source_capture.source_kind_not_writable");
        }
        if (source.FrameIndex is < 0) errors.Add("source_capture.frame_index_negative");

        var analysis = input.Analysis;
        RequireText(errors, "analysis.analysis_id", analysis.AnalysisId);
        RequireText(errors, "analysis.source_capture_id", analysis.SourceCaptureId);
        RequireText(errors, "analysis.save_boundary_status", analysis.SaveBoundaryStatus);
        RequireText(errors, "analysis.event_type", analysis.EventType);
        RequireText(errors, "analysis.confirmation_mode", analysis.ConfirmationMode);
        RequireText(errors, "analysis.app_version", analysis.AppVersion);
        if (!AnalysisStatuses.Contains(analysis.AnalysisStatus, StringComparer.Ordinal))
        {
            errors.Add("analysis.analysis_status_invalid");
        }
        if (analysis.TimestampMs is < 0) errors.Add("analysis.timestamp_ms_negative");
        if (analysis.CandidateDurationMs is < 0) errors.Add("analysis.candidate_duration_ms_negative");
        ValidateConfidence(errors, "analysis.analysis_confidence", analysis.AnalysisConfidence, true);
        ValidateSummary(errors, analysis.AnalysisSummaryJson);
        if (analysis.SourceCaptureId != source.CaptureId)
        {
            errors.Add("analysis.source_capture_id_mismatch");
        }
        if (analysis.LogPath.Length > 0 && !IsArtifactPath(analysis.LogPath))
        {
            errors.Add("analysis.log_path_invalid");
        }

        var play = input.Play;
        if (play is null)
        {
            if (analysis.PlayId is not null) errors.Add("analysis.play_id_requires_play");
            if (analysis.AnalysisStatus == "saved") errors.Add("saved_analysis_requires_play");
            if (analysis.SaveBoundaryStatus == "save_ready") errors.Add("save_ready_status_requires_play");
            if (string.IsNullOrWhiteSpace(analysis.SkipReason)) errors.Add("non_saved_analysis_requires_skip_reason");
        }
        else
        {
            ValidatePlay(errors, play);
            if (analysis.AnalysisStatus != "saved") errors.Add("play_requires_saved_analysis");
            if (analysis.SaveBoundaryStatus != "save_ready") errors.Add("play_requires_save_ready_status");
            if (analysis.SkipReason.Length > 0) errors.Add("saved_analysis_skip_reason_must_be_empty");
            if (analysis.PlayId != play.PlayId) errors.Add("analysis.play_id_mismatch");
            if (!analysis.ConfirmedResult) errors.Add("play_requires_confirmed_result");
            if (analysis.Duplicate) errors.Add("play_must_not_be_duplicate");
            if (analysis.EventType != "confirmed") errors.Add("play_requires_confirmed_event_type");
            if (play.SourceCaptureId != source.CaptureId) errors.Add("play.source_capture_id_mismatch");
            if (play.CaptureHash != source.CaptureHash) errors.Add("play.capture_hash_mismatch");
            if (analysis.AppVersion != play.AppVersion) errors.Add("analysis.app_version_mismatch");
            if (analysis.AnalysisConfidence != play.AnalysisConfidence)
            {
                errors.Add("analysis.analysis_confidence_mismatch");
            }
        }

        if (analysis.Duplicate)
        {
            if (analysis.AnalysisStatus != "skipped") errors.Add("duplicate_requires_skipped_analysis");
            if (analysis.SaveBoundaryStatus != "duplicate") errors.Add("duplicate_requires_duplicate_boundary_status");
        }
        if (analysis.AnalysisStatus == "low_confidence" && analysis.Duplicate)
        {
            errors.Add("low_confidence_must_not_be_duplicate");
        }
        return errors;
    }

    private static void ValidatePlay(List<string> errors, AppFormalPlayInput play)
    {
        foreach (var (name, value) in new[]
        {
            ("play.play_id", play.PlayId), ("play.master_version", play.MasterVersion),
            ("play.song_id", play.SongId), ("play.chart_id", play.ChartId),
            ("play.rank", play.Rank), ("play.clear_type", play.ClearType),
            ("play.capture_hash", play.CaptureHash), ("play.source_capture_id", play.SourceCaptureId),
            ("play.duplicate_key", play.DuplicateKey), ("play.app_version", play.AppVersion),
        }) RequireText(errors, name, value);
        RequireTimestamp(errors, "play.played_at", play.PlayedAt);
        if (!RankValues.Contains(play.Rank, StringComparer.Ordinal)) errors.Add("play.rank_invalid");
        if (!ClearValues.Contains(play.ClearType, StringComparer.Ordinal)) errors.Add("play.clear_type_invalid");
        if (play.FlareRank is not null && !FlareValues.Contains(play.FlareRank, StringComparer.Ordinal)) errors.Add("play.flare_rank_invalid");
        if (play.Ok < 0) errors.Add("play.ok_negative");
        if (play.Calories is not null &&
            (!double.IsFinite(play.Calories.Value) || play.Calories.Value < 0.0))
        {
            errors.Add("play.calories_invalid");
        }
        if (play.DuplicateKey.StartsWith("score:", StringComparison.Ordinal) || play.DuplicateKey.StartsWith("file:", StringComparison.Ordinal)) errors.Add("play.duplicate_key_uses_preview_format");
        if (play.Score is < 0 or > 1_000_000) errors.Add("play.score_out_of_range");
        else if (play.Score % 10 != 0) errors.Add("play.score_not_multiple_of_10");
        foreach (var (name, value) in new[]
        {
            ("max_combo", play.MaxCombo), ("marvelous", play.Marvelous), ("perfect", play.Perfect),
            ("great", play.Great), ("good", play.Good), ("miss", play.Miss), ("ex_score", play.ExScore),
        }) if (value < 0) errors.Add($"play.{name}_negative");
        ValidateConfidence(errors, "play.analysis_confidence", play.AnalysisConfidence, false);
    }

    private static void RequireText(List<string> errors, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{name}_required");
    }

    private static void RequireTimestamp(List<string> errors, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name}_required");
            return;
        }
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
            !(value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                value.Skip(10).Any(character => character is '+' or '-')))
        {
            errors.Add(value.Contains('T', StringComparison.Ordinal) ? $"{name}_timezone_required" : $"{name}_invalid");
        }
    }

    private static void ValidateConfidence(List<string> errors, string name, double? value, bool allowNull)
    {
        if (value is null)
        {
            if (!allowNull) errors.Add($"{name}_required");
            return;
        }
        if (double.IsNaN(value.Value) || value < 0.0 || value > 1.0) errors.Add($"{name}_out_of_range");
    }

    private static void ValidateSummary(List<string> errors, string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object) errors.Add("analysis.analysis_summary_json_must_be_object");
        }
        catch (JsonException)
        {
            errors.Add("analysis.analysis_summary_json_invalid");
        }
    }

    private static bool IsArtifactPath(string path) =>
        path.StartsWith("logs/analysis_details/", StringComparison.Ordinal) &&
        path.EndsWith(".json", StringComparison.Ordinal) &&
        !Path.IsPathRooted(path) && !path.Contains("\\", StringComparison.Ordinal) &&
        path.Split('/').All(part => part is not ("" or "." or ".."));
}

internal sealed record AppFormalWriteResult(
    string SourceCaptureId,
    string AnalysisId,
    string? PlayId,
    string AnalysisStatus,
    string SaveBoundaryStatus,
    string SkipReason,
    bool Duplicate);

internal static class AppFormalScoreDbWriter
{
    public static AppFormalWriteResult Write(string path, AppSaveInput saveInput)
    {
        var errors = AppFormalValidation.Errors(saveInput);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "formal score DB save input is invalid: " + string.Join(", ", errors));
        }

        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"personal score DB path is a directory: {fullPath}");
        }
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
        {
            ScoreViewerRepository.InitializeEmptyScoreDatabase(fullPath);
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        ScoreViewerRepository.ValidateScoreDatabaseForWrite(connection);

        var source = saveInput.Source;
        var analysis = saveInput.Analysis;
        var play = saveInput.Play;
        using var transaction = connection.BeginTransaction();
        if (play is not null && DuplicateKeyAlreadySaved(connection, transaction, play.DuplicateKey))
        {
            play = null;
            analysis = analysis with
            {
                PlayId = null,
                AnalysisStatus = "skipped",
                SaveBoundaryStatus = "duplicate",
                SkipReason = "duplicate_key_already_saved",
                Duplicate = true,
            };
        }

        Execute(
            connection,
            transaction,
            """
            INSERT INTO source_captures (
              capture_id, capture_hash, captured_at, source_kind, source_path,
              manifest_image_path, frame_index
            ) VALUES ($capture_id, $capture_hash, $captured_at, $source_kind, $source_path,
                      $manifest_image_path, $frame_index);
            """,
            ("$capture_id", source.CaptureId), ("$capture_hash", source.CaptureHash),
            ("$captured_at", source.CapturedAt), ("$source_kind", source.SourceKind),
            ("$source_path", source.SourcePath), ("$manifest_image_path", source.ManifestImagePath),
            ("$frame_index", source.FrameIndex));
        if (play is not null)
        {
            Execute(
                connection,
                transaction,
                """
                INSERT INTO plays (
                  play_id, played_at, master_version, song_id, chart_id, score,
                  max_combo, marvelous, perfect, great, good, miss, ex_score, rank,
                  clear_type, flare_rank, ok, calories, capture_hash, source_capture_id, duplicate_key,
                  analysis_confidence, app_version
                ) VALUES ($play_id, $played_at, $master_version, $song_id, $chart_id, $score,
                          $max_combo, $marvelous, $perfect, $great, $good, $miss, $ex_score, $rank,
                          $clear_type, $flare_rank, $ok, $calories, $capture_hash, $source_capture_id, $duplicate_key,
                          $analysis_confidence, $app_version);
                """,
                ("$play_id", play.PlayId), ("$played_at", play.PlayedAt),
                ("$master_version", play.MasterVersion), ("$song_id", play.SongId),
                ("$chart_id", play.ChartId), ("$score", play.Score), ("$max_combo", play.MaxCombo),
                ("$marvelous", play.Marvelous), ("$perfect", play.Perfect), ("$great", play.Great),
                ("$good", play.Good), ("$miss", play.Miss), ("$ex_score", play.ExScore),
                ("$rank", play.Rank), ("$clear_type", play.ClearType),
                ("$flare_rank", play.FlareRank), ("$ok", play.Ok), ("$calories", play.Calories),
                ("$capture_hash", play.CaptureHash),
                ("$source_capture_id", play.SourceCaptureId), ("$duplicate_key", play.DuplicateKey),
                ("$analysis_confidence", play.AnalysisConfidence), ("$app_version", play.AppVersion));
        }
        Execute(
            connection,
            transaction,
            """
            INSERT INTO analysis_logs (
              analysis_id, play_id, source_capture_id, analysis_status,
              save_boundary_status, skip_reason, event_type, confirmed_result,
              duplicate, confirmation_mode, timestamp_ms, candidate_duration_ms,
              identity_signal_status, digit_review_status, analysis_confidence,
              analysis_summary_json, log_path, app_version
            ) VALUES ($analysis_id, $play_id, $source_capture_id, $analysis_status,
                      $save_boundary_status, $skip_reason, $event_type, $confirmed_result,
                      $duplicate, $confirmation_mode, $timestamp_ms, $candidate_duration_ms,
                      $identity_signal_status, $digit_review_status, $analysis_confidence,
                      $analysis_summary_json, $log_path, $app_version);
            """,
            ("$analysis_id", analysis.AnalysisId), ("$play_id", analysis.PlayId),
            ("$source_capture_id", analysis.SourceCaptureId), ("$analysis_status", analysis.AnalysisStatus),
            ("$save_boundary_status", analysis.SaveBoundaryStatus), ("$skip_reason", analysis.SkipReason),
            ("$event_type", analysis.EventType), ("$confirmed_result", analysis.ConfirmedResult),
            ("$duplicate", analysis.Duplicate), ("$confirmation_mode", analysis.ConfirmationMode),
            ("$timestamp_ms", analysis.TimestampMs), ("$candidate_duration_ms", analysis.CandidateDurationMs),
            ("$identity_signal_status", analysis.IdentitySignalStatus),
            ("$digit_review_status", analysis.DigitReviewStatus),
            ("$analysis_confidence", analysis.AnalysisConfidence),
            ("$analysis_summary_json", analysis.AnalysisSummaryJson), ("$log_path", analysis.LogPath),
            ("$app_version", analysis.AppVersion));
        transaction.Commit();
        return new AppFormalWriteResult(
            source.CaptureId,
            analysis.AnalysisId,
            play?.PlayId,
            analysis.AnalysisStatus,
            analysis.SaveBoundaryStatus,
            analysis.SkipReason,
            analysis.Duplicate);
    }

    private static bool DuplicateKeyAlreadySaved(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string duplicateKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM plays WHERE duplicate_key = $duplicate_key LIMIT 1;";
        command.Parameters.AddWithValue("$duplicate_key", duplicateKey);
        return command.ExecuteScalar() is not null;
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
    }
}

internal static class StrictJsonObjectValidator
{
    public static void ValidateNoDuplicateKeys(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objects.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.PropertyName:
                    if (objects.Count == 0 || !objects.Peek().Add(reader.GetString() ?? string.Empty))
                    {
                        throw new JsonException("duplicate JSON object key");
                    }
                    break;
                case JsonTokenType.EndObject:
                    if (objects.Count == 0) throw new JsonException("invalid JSON object nesting");
                    objects.Pop();
                    break;
            }
        }
        if (objects.Count != 0) throw new JsonException("invalid JSON object nesting");
    }
}
