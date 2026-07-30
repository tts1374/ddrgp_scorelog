using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DDRGpScoreViewer.Data;

internal static class AppAnalysisDetailValidator
{
    private const string GeneratedBy =
        "tools.vision_poc.personal_score_db_analysis_artifacts";

    private static readonly string[] RootKeys =
    [
        "schema_version", "generated_by", "generated_at", "app_version", "analysis_id",
        "source_capture_id", "analysis_status", "save_boundary_status", "skip_reason",
        "event", "review", "investigation", "failure_image_path", "retention",
    ];

    private static readonly string[] EventKeys =
    [
        "confirmed_result", "duplicate", "event_type", "confirmation_mode", "timestamp_ms",
        "candidate_duration_ms",
    ];

    private static readonly string[] ReviewKeys =
    ["identity_status", "digit_status", "analysis_confidence"];

    private static readonly string[] InvestigationKeys =
    ["candidate_material", "diagnostic_summary"];

    private static readonly string[] CandidateKeys =
    ["kind", "status", "summary"];

    private static readonly string[] RetentionKeys =
    ["retention_class", "basis_at", "expires_at"];

    private static readonly HashSet<string> ForbiddenKeys =
    [
        "play_id", "played_at", "master_version", "song_id", "chart_id", "score",
        "max_combo", "marvelous", "perfect", "great", "good", "miss", "ex_score",
        "rank", "clear_type", "flare_rank", "duplicate_key", "validation_result_schema_version",
        "adapter_status", "save_input_constructed", "diagnostic", "diagnostic_output_path",
        "migration_plan_status",
    ];

    public static void Validate(JsonElement detail)
    {
        RequireObject(detail, "analysis detail");
        RequireExactKeys(detail, RootKeys, "analysis detail");
        var forbidden = new HashSet<string>(StringComparer.Ordinal);
        FindForbiddenKeys(detail, forbidden);
        if (forbidden.Count > 0)
        {
            throw new JsonException(
                "analysis detail contains forbidden projection keys: " +
                string.Join(", ", forbidden.Order(StringComparer.Ordinal)));
        }

        RequireExactInt(detail.GetProperty("schema_version"), 1, "schema_version");
        RequireExactText(detail.GetProperty("generated_by"), GeneratedBy, "generated_by");
        ParseUtcTimestamp(RequiredText(detail, "generated_at"), "generated_at");
        _ = RequiredText(detail, "app_version");
        _ = RequiredText(detail, "analysis_id");
        _ = RequiredText(detail, "source_capture_id");

        var analysisStatus = RequiredText(detail, "analysis_status");
        if (analysisStatus is not ("saved" or "low_confidence" or "error" or "skipped"))
        {
            throw new JsonException(
                "analysis_status must be saved, low_confidence, error, or skipped");
        }

        var saveBoundaryStatus = RequiredText(detail, "save_boundary_status");
        var skipReason = RequiredText(
            detail,
            "skip_reason",
            allowEmpty: analysisStatus == "saved");
        if (analysisStatus == "saved" &&
            (saveBoundaryStatus != "save_ready" || skipReason.Length > 0))
        {
            throw new JsonException("saved detail requires save_ready without a skip reason");
        }

        ValidateEvent(detail.GetProperty("event"), analysisStatus, saveBoundaryStatus, skipReason);
        ValidateReview(detail.GetProperty("review"));
        ValidateInvestigation(detail.GetProperty("investigation"));

        var failureImagePath = detail.GetProperty("failure_image_path");
        if (failureImagePath.ValueKind == JsonValueKind.String)
        {
            ValidateFailureImagePath(failureImagePath.GetString() ?? string.Empty);
        }
        else if (failureImagePath.ValueKind != JsonValueKind.Null)
        {
            throw new JsonException("failure_image_path must be text or null");
        }

        ValidateRetention(detail.GetProperty("retention"));
    }

    public static void ValidateLogPath(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        ValidateRelativeArtifactPath(
            path,
            "logs/analysis_details/",
            [".json"],
            "analysis_logs.log_path");
    }

    public static IReadOnlyList<string> SharedValueMismatches(
        JsonElement detail,
        AppAnalysisInput analysis,
        string artifactPath)
    {
        var mismatches = new List<string>();
        if (detail.GetProperty("analysis_id").GetString() != analysis.AnalysisId)
        {
            mismatches.Add("analysis_id mismatch");
        }
        if (detail.GetProperty("source_capture_id").GetString() != analysis.SourceCaptureId)
        {
            mismatches.Add("source_capture_id mismatch");
        }

        var expectedSaveStatus = analysis.SaveBoundaryStatus switch
        {
            "save_ready" => "save_ready",
            "duplicate" => "duplicate",
            "low_confidence" or "error" or "excluded" => "excluded",
            _ => string.Empty,
        };
        if (detail.GetProperty("save_boundary_status").GetString() != expectedSaveStatus)
        {
            mismatches.Add("save_boundary_status mismatch");
        }
        if (analysis.LogPath != artifactPath)
        {
            mismatches.Add("analysis.log_path and artifact output mismatch");
        }
        return mismatches;
    }

    private static void ValidateEvent(
        JsonElement value,
        string analysisStatus,
        string saveBoundaryStatus,
        string skipReason)
    {
        RequireObject(value, "event");
        RequireExactKeys(value, EventKeys, "event");
        var confirmed = RequiredBoolean(value, "confirmed_result");
        var duplicate = RequiredBoolean(value, "duplicate");
        if (analysisStatus == "saved" && duplicate)
        {
            throw new JsonException("saved detail must not be duplicate");
        }
        _ = RequiredText(value, "event_type");
        var confirmationMode = RequiredText(value, "confirmation_mode");
        if (confirmationMode is not ("frames" or "time"))
        {
            throw new JsonException("event.confirmation_mode is invalid");
        }
        var timestampMs = OptionalNonNegativeInt(value, "timestamp_ms");
        _ = OptionalNonNegativeInt(value, "candidate_duration_ms");
        if (confirmationMode == "time" && timestampMs is null)
        {
            throw new JsonException("time confirmation requires event.timestamp_ms");
        }
        if (confirmationMode == "frames" && timestampMs is not null)
        {
            throw new JsonException("frames confirmation must not carry event.timestamp_ms");
        }
        if (duplicate &&
            (analysisStatus != "skipped" || saveBoundaryStatus != "duplicate"))
        {
            throw new JsonException("duplicate detail requires skipped/duplicate statuses");
        }
        if (!duplicate && saveBoundaryStatus == "duplicate")
        {
            throw new JsonException("duplicate save status requires event.duplicate=true");
        }
        if (analysisStatus == "low_confidence" && duplicate)
        {
            throw new JsonException("low_confidence detail must not be duplicate");
        }
        if (confirmed && analysisStatus == "error" && skipReason.Length == 0)
        {
            throw new JsonException("error detail requires skip_reason");
        }
    }

    private static void ValidateReview(JsonElement value)
    {
        RequireObject(value, "review");
        RequireExactKeys(value, ReviewKeys, "review");
        _ = RequiredText(value, "identity_status", allowEmpty: true);
        _ = RequiredText(value, "digit_status", allowEmpty: true);
        var confidence = OptionalNumber(value, "analysis_confidence");
        if (confidence is < 0.0 or > 1.0)
        {
            throw new JsonException("review.analysis_confidence is out of range");
        }
    }

    private static void ValidateInvestigation(JsonElement value)
    {
        RequireObject(value, "investigation");
        RequireExactKeys(value, InvestigationKeys, "investigation");
        var materials = value.GetProperty("candidate_material");
        if (materials.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("investigation.candidate_material must be an array");
        }
        var index = 0;
        foreach (var material in materials.EnumerateArray())
        {
            var name = $"investigation.candidate_material[{index++}]";
            RequireObject(material, name);
            RequireExactKeys(material, CandidateKeys, name);
            _ = RequiredText(material, "kind");
            _ = RequiredText(material, "status");
            _ = RequiredText(material, "summary");
        }

        var diagnosticSummary = value.GetProperty("diagnostic_summary");
        if (diagnosticSummary.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("investigation.diagnostic_summary must be an array of text");
        }
        foreach (var item in diagnosticSummary.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new JsonException(
                    "investigation.diagnostic_summary must be an array of text");
            }
        }
    }

    private static void ValidateRetention(JsonElement value)
    {
        RequireObject(value, "retention");
        RequireExactKeys(value, RetentionKeys, "retention");
        var retentionClass = RequiredText(value, "retention_class");
        var basisAt = ParseUtcTimestamp(
            RequiredText(value, "basis_at"),
            "retention.basis_at");
        var expectedExpiresAt = retentionClass switch
        {
            "short" => FormatUtc(basisAt.AddDays(7)),
            "standard" => FormatUtc(basisAt.AddDays(30)),
            "indefinite" => null,
            _ => throw new JsonException("analysis detail retention class is invalid"),
        };
        var expiresAt = value.GetProperty("expires_at");
        string? actualExpiresAt = expiresAt.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => expiresAt.GetString(),
            _ => throw new JsonException("retention.expires_at must be text or null"),
        };
        if (actualExpiresAt != expectedExpiresAt)
        {
            throw new JsonException("retention metadata does not match the deterministic policy");
        }
    }

    private static void ValidateFailureImagePath(string path) =>
        ValidateRelativeArtifactPath(
            path,
            "logs/analysis_failures/",
            [".png", ".jpg", ".jpeg", ".webp"],
            "failure_image_path");

    private static void ValidateRelativeArtifactPath(
        string path,
        string root,
        IEnumerable<string> extensions,
        string fieldName)
    {
        var parts = path.Split('/');
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains("\\", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            parts.Any(part => part is "" or "." or "..") ||
            !path.StartsWith(root, StringComparison.Ordinal) ||
            path.Length <= root.Length + extension.Length ||
            !extensions.Contains(extension))
        {
            throw new JsonException(
                $"{fieldName} must be a safe POSIX-relative artifact path");
        }
    }

    private static DateTimeOffset ParseUtcTimestamp(string value, string fieldName)
    {
        var hasExplicitOffset = value.EndsWith('Z') ||
            value.Skip(10).Any(character => character is '+' or '-');
        if (!hasExplicitOffset ||
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            parsed.Offset != TimeSpan.Zero)
        {
            throw new JsonException($"{fieldName} must be UTC");
        }
        return parsed.ToUniversalTime();
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var result = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var microseconds = (utc.Ticks % TimeSpan.TicksPerSecond) / 10;
        if (microseconds != 0)
        {
            result += "." + microseconds.ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');
        }
        return result + "Z";
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{name} must be an object");
        }
    }

    private static void RequireExactKeys(
        JsonElement value,
        IEnumerable<string> expected,
        string name)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var actualSet = value.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (expectedSet.SetEquals(actualSet))
        {
            return;
        }
        var missing = expectedSet.Except(actualSet, StringComparer.Ordinal);
        var unknown = actualSet.Except(expectedSet, StringComparer.Ordinal);
        throw new JsonException(
            $"{name} keys are invalid; missing=[{string.Join(",", missing)}], " +
            $"unknown=[{string.Join(",", unknown)}]");
    }

    private static void FindForbiddenKeys(JsonElement value, HashSet<string> found)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (ForbiddenKeys.Contains(property.Name))
                {
                    found.Add(property.Name);
                }
                FindForbiddenKeys(property.Value, found);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                FindForbiddenKeys(item, found);
            }
        }
    }

    private static string RequiredText(
        JsonElement root,
        string name,
        bool allowEmpty = false)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{name} must be text");
        }
        var result = value.GetString() ?? string.Empty;
        if (!allowEmpty && string.IsNullOrWhiteSpace(result))
        {
            throw new JsonException($"{name} must be text");
        }
        return result;
    }

    private static bool RequiredBoolean(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonException($"{name} must be boolean");
        }
        return value.GetBoolean();
    }

    private static double? OptionalNumber(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result))
        {
            throw new JsonException($"{name} must be a number or null");
        }
        return result;
    }

    private static long? OptionalNonNegativeInt(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result) || result < 0)
        {
            throw new JsonException($"{name} must be a non-negative integer or null");
        }
        return result;
    }

    private static void RequireExactInt(JsonElement value, int expected, string name)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result) || result != expected)
        {
            throw new JsonException($"{name} is invalid");
        }
    }

    private static void RequireExactText(JsonElement value, string expected, string name)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() != expected)
        {
            throw new JsonException($"{name} is invalid");
        }
    }
}
