using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Runtime;

namespace DDRGpScoreViewer.Data;

public sealed record CaptureSaveWorkflowResult(
    string Status,
    int EventCount,
    IReadOnlyDictionary<string, int> StatusCounts,
    IReadOnlyList<string> SavedPlayIds,
    IReadOnlyList<string> Reasons,
    string? AnalysisOutput);

public interface ICaptureSaveWorkflowRunner
{
    Task<CaptureSaveWorkflowResult> RunAsync(
        string manifestPath,
        string scoreDatabasePath,
        string masterDatabasePath,
        CancellationToken cancellationToken = default);
}

public interface ILiveCaptureSaveWorkflowRunner
{
    Task<CaptureSaveWorkflowResult> RunCandidateAsync(
        CapturedFrame frame,
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// App-owned capture/save orchestration. It keeps candidate material separate from
/// formal save input; M7a digit recognition remains candidate evidence until the
/// existing formal evidence adapter accepts an explicit formal play.
/// </summary>
public sealed class AppOwnedCaptureSaveWorkflowRunner :
    ICaptureSaveWorkflowRunner,
    ILiveCaptureSaveWorkflowRunner
{
    private readonly AppOwnedLiveResultAnalyzer analyzer;
    private readonly AppOwnedPersonalScoreDbWorkflowRunner workflowRunner;

    public AppOwnedCaptureSaveWorkflowRunner()
        : this(new AppOwnedLiveResultAnalyzer(), new AppOwnedPersonalScoreDbWorkflowRunner())
    {
    }

    internal AppOwnedCaptureSaveWorkflowRunner(
        AppOwnedLiveResultAnalyzer analyzer,
        AppOwnedPersonalScoreDbWorkflowRunner workflowRunner)
    {
        this.analyzer = analyzer;
        this.workflowRunner = workflowRunner;
    }

    public async Task<CaptureSaveWorkflowResult> RunAsync(
        string manifestPath,
        string scoreDatabasePath,
        string masterDatabasePath,
        CancellationToken cancellationToken = default)
    {
        _ = masterDatabasePath;
        try
        {
            var rows = await AppCaptureManifest.ReadAsync(manifestPath, cancellationToken);
            return await RunRowsAsync(rows, manifestPath, scoreDatabasePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or InvalidDataException or FormatException)
        {
            return FailedResult(exception.Message);
        }
    }

    public async Task<CaptureSaveWorkflowResult> RunCandidateAsync(
        CapturedFrame frame,
        string scoreDatabasePath,
        string masterDatabasePath,
        string? catalogDatabasePath,
        CancellationToken cancellationToken = default)
    {
        _ = masterDatabasePath;
        _ = catalogDatabasePath;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = await analyzer.AnalyzeKnownResultAsync(frame, cancellationToken);
            if (!observation.IsResultScreen)
            {
                return FailedResult(observation.Reason);
            }
            var input = BuildInput(
                frame,
                observation,
                sourcePath: "live-memory://app-owned-candidate",
                imagePath: "",
                frameIndex: null,
                candidateDurationMs: null,
                duplicate: false);
            var result = await workflowRunner.RunAdapterInputAsync(
                input,
                scoreDatabasePath,
                cancellationToken);
            return ToCaptureResult(result, eventCount: 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or InvalidDataException or FormatException)
        {
            return FailedResult(exception.Message);
        }
    }

    private async Task<CaptureSaveWorkflowResult> RunRowsAsync(
        IReadOnlyList<AppCaptureManifestRow> rows,
        string manifestPath,
        string scoreDatabasePath,
        CancellationToken cancellationToken)
    {
        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidDataException("Capture manifest directory could not be determined.");
        var statusCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var savedPlayIds = new List<string>();
        var reasons = new List<string>();
        string? activeConfirmedKey = null;
        var workflowFailed = false;
        AppCaptureManifestRow? pending = null;
        CapturedFrame? pendingFrame = null;
        LiveResultObservation? pendingObservation = null;
        var eventCount = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imagePath = ResolveManifestImagePath(manifestDirectory, row.ImagePath);
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var frame = new CapturedFrame(
                bytes,
                row.Width ?? 1280,
                row.Height ?? 720,
                row.TimestampMs,
                row.CapturedAtUtc ?? DateTimeOffset.UtcNow,
                row.CaptureSource ?? "manifest");
            LiveResultObservation observation;
            if (row.ScreenType == "result")
            {
                observation = await analyzer.AnalyzeKnownResultAsync(frame, cancellationToken);
            }
            else if (row.ScreenType == "non_result")
            {
                observation = new LiveResultObservation(
                    false,
                    string.Empty,
                    string.Empty,
                    "manifest_non_result");
            }
            else
            {
                observation = await analyzer.AnalyzeAsync(frame, cancellationToken);
            }
            if (!observation.IsResultScreen)
            {
                activeConfirmedKey = null;
                pending = null;
                pendingFrame = null;
                pendingObservation = null;
                continue;
            }

            var key = observation.TitleSignature.Length == 0
                ? "result"
                : observation.TitleSignature;
            if (string.Equals(activeConfirmedKey, key, StringComparison.Ordinal))
            {
                // A stable RESULT with the same signature is a single event. Ignore
                // later samples until a non-RESULT frame reopens the event boundary.
                continue;
            }

            if (pending is not null &&
                pendingObservation is not null &&
                pendingObservation.TitleSignature == observation.TitleSignature &&
                row.TimestampMs - pending.TimestampMs >= 1_000)
            {
                eventCount++;
                activeConfirmedKey = key;
                var duration = row.TimestampMs - pending.TimestampMs;
                var confirmedResult = await ProcessEventAsync(
                    pendingFrame ?? frame,
                    observation,
                    scoreDatabasePath,
                    row,
                    manifestPath,
                    duration,
                    duplicate: false,
                    cancellationToken);
                workflowFailed |= MergeResult(confirmedResult, statusCounts, savedPlayIds, reasons);
                pending = null;
                pendingFrame = null;
                pendingObservation = null;
                continue;
            }

            pending = row;
            pendingFrame = frame;
            pendingObservation = observation;
        }

        return new CaptureSaveWorkflowResult(
            workflowFailed ? "workflow_failed" : "completed",
            eventCount,
            statusCounts,
            savedPlayIds,
            reasons.Distinct(StringComparer.Ordinal).ToArray(),
            null);
    }

    private async Task<PersonalScoreDbWorkflowResult> ProcessEventAsync(
        CapturedFrame frame,
        LiveResultObservation observation,
        string scoreDatabasePath,
        AppCaptureManifestRow row,
        string manifestPath,
        long? candidateDurationMs,
        bool duplicate,
        CancellationToken cancellationToken)
    {
        var input = BuildInput(
            frame,
            observation,
            Path.GetFullPath(manifestPath),
            row.ImagePath,
            row.FrameIndex,
            candidateDurationMs,
            duplicate);
        return await workflowRunner.RunAdapterInputAsync(
            input,
            scoreDatabasePath,
            cancellationToken);
    }

    private static AppSaveAdapterInput BuildInput(
        CapturedFrame frame,
        LiveResultObservation observation,
        string sourcePath,
        string imagePath,
        int? frameIndex,
        long? candidateDurationMs,
        bool duplicate)
    {
        var captureHash = "sha256:" + Convert.ToHexString(SHA256.HashData(frame.PngBytes)).ToLowerInvariant();
        var idSeed = captureHash.Replace(":", "-", StringComparison.Ordinal);
        var digitResults = observation.DigitRecognitions ??
            new Dictionary<string, M7aDigitRecognitionResult>(StringComparer.Ordinal);
        var candidateMaterial = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["result_screen"] = observation.IsResultScreen.ToString(CultureInfo.InvariantCulture),
            ["score"] = observation.Score,
            ["recognized_digits"] = observation.Score,
            ["title_signature"] = observation.TitleSignature,
            ["recognition_method"] = M7aDigitRecognizer.Method,
            ["recognition_status"] = observation.DigitRecognitionStatus,
        };
        foreach (var result in digitResults.Values)
        {
            candidateMaterial[$"{result.FieldName}_recognized_digits"] = result.RecognizedDigits;
            candidateMaterial[$"{result.FieldName}_expected_value"] = result.ExpectedValue;
            candidateMaterial[$"{result.FieldName}_status"] = result.Status;
            candidateMaterial[$"{result.FieldName}_failure_reason"] = result.FailureReason;
            candidateMaterial[$"{result.FieldName}_distance"] = FormatNumber(result.Distance);
            candidateMaterial[$"{result.FieldName}_confidence"] = FormatNumber(result.Confidence);
            candidateMaterial[$"{result.FieldName}_segment_count"] =
                result.SegmentCount.ToString(CultureInfo.InvariantCulture);
            candidateMaterial[$"{result.FieldName}_template_count"] =
                result.TemplateCount.ToString(CultureInfo.InvariantCulture);
            candidateMaterial[$"{result.FieldName}_per_digit_distances"] = result.PerDigitDistances;
            candidateMaterial[$"{result.FieldName}_match"] = result.Match?.ToString() ?? string.Empty;
        }

        var recognitionSummary = digitResults.ToDictionary(
            pair => pair.Key,
            pair => new
            {
                pair.Value.Status,
                pair.Value.FailureReason,
                pair.Value.Distance,
                pair.Value.Confidence,
                pair.Value.SegmentCount,
                pair.Value.TemplateCount,
            },
            StringComparer.Ordinal);
        var analysisSummary = JsonSerializer.Serialize(
            new
            {
                runtime = "app-owned",
                recognition_method = M7aDigitRecognizer.Method,
                digit_recognition = recognitionSummary,
                formal_evidence = "pending",
            });
        return new AppSaveAdapterInput(
            candidateMaterial,
            $"capture-{idSeed}-{frame.TimestampMs}",
            captureHash,
            frame.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            "manifest",
            sourcePath,
            $"analysis-{idSeed}-{frame.TimestampMs}",
            duplicate ? "duplicate" : "confirmed",
            true,
            duplicate,
            duplicate ? "duplicate_window" : "time",
            "unresolved",
            observation.DigitRecognitionStatus,
            null,
            analysisSummary,
            "score-viewer-runtime",
            null,
            null,
            imagePath,
            frameIndex,
            frame.TimestampMs,
            candidateDurationMs,
            "");
    }

    private static string FormatNumber(double? value) =>
        value?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string ResolveManifestImagePath(string manifestDirectory, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new InvalidDataException("Capture manifest image_path is empty.");
        }
        var resolved = Path.GetFullPath(Path.Combine(manifestDirectory, imagePath));
        if (!IsWithin(resolved, manifestDirectory))
        {
            throw new InvalidDataException("Capture manifest image_path escapes its directory.");
        }
        return resolved;
    }

    private static bool MergeResult(
        PersonalScoreDbWorkflowResult result,
        Dictionary<string, int> statusCounts,
        List<string> savedPlayIds,
        List<string> reasons)
    {
        var status = result.WorkflowStatus switch
        {
            "saved" => "saved",
            "duplicate" => "duplicate",
            "excluded" => "excluded",
            "unresolved" => "unresolved",
            _ => "analysis_failed",
        };
        statusCounts[status] = statusCounts.GetValueOrDefault(status) + 1;
        if (result.PlayId is not null && result.WorkflowStatus == "saved")
        {
            savedPlayIds.Add(result.PlayId);
        }
        if (result.Reasons.Count > 0)
        {
            reasons.AddRange(result.Reasons);
        }
        return status == "analysis_failed";
    }

    private static CaptureSaveWorkflowResult ToCaptureResult(
        PersonalScoreDbWorkflowResult result,
        int eventCount)
    {
        var status = result.WorkflowStatus switch
        {
            "saved" => "saved",
            "duplicate" => "duplicate",
            "excluded" => "excluded",
            "unresolved" => "unresolved",
            _ => "analysis_failed",
        };
        return new CaptureSaveWorkflowResult(
            result.WorkflowStatus is "saved" or "duplicate" or "excluded" or "unresolved"
                ? "completed"
                : "workflow_failed",
            eventCount,
            new Dictionary<string, int> { [status] = 1 },
            result.PlayId is null ? [] : [result.PlayId],
            result.Reasons,
            null);
    }

    private static CaptureSaveWorkflowResult FailedResult(string reason) =>
        new("workflow_failed", 0, new Dictionary<string, int>(), [], [reason], null);

    private static bool IsWithin(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }
}

internal sealed record AppCaptureManifestRow(
    string ImagePath,
    long TimestampMs,
    DateTimeOffset? CapturedAtUtc,
    string? ScreenType,
    string? CaptureSource,
    int? Width,
    int? Height,
    int? FrameIndex);

internal static class AppCaptureManifest
{
    public static async Task<IReadOnlyList<AppCaptureManifestRow>> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(
            Path.GetFullPath(path),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        var records = ParseCsv(text);
        if (records.Count < 2)
        {
            throw new InvalidDataException("Capture manifest has no frame rows.");
        }
        var header = records[0];
        var indexes = header.Select((value, index) => (value, index))
            .ToDictionary(pair => pair.value, pair => pair.index, StringComparer.Ordinal);
        if (!indexes.ContainsKey("image_path") || !indexes.ContainsKey("timestamp_ms"))
        {
            throw new InvalidDataException("Capture manifest requires image_path and timestamp_ms columns.");
        }

        var result = new List<AppCaptureManifestRow>();
        long previous = -1;
        for (var index = 1; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Count == 1 && record[0].Length == 0) continue;
            var imagePath = Value(record, indexes, "image_path");
            if (!long.TryParse(Value(record, indexes, "timestamp_ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp) || timestamp < 0)
            {
                throw new InvalidDataException($"Capture manifest timestamp is invalid at row {index + 1}.");
            }
            if (timestamp <= previous)
            {
                throw new InvalidDataException("Capture manifest timestamps must be strictly increasing.");
            }
            previous = timestamp;
            var captured = OptionalValue(record, indexes, "captured_at_utc");
            DateTimeOffset? capturedAt = null;
            if (!string.IsNullOrWhiteSpace(captured))
            {
                if (!DateTimeOffset.TryParse(captured, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    throw new InvalidDataException($"Capture manifest captured_at_utc is invalid at row {index + 1}.");
                }
                capturedAt = parsed;
            }
            result.Add(new AppCaptureManifestRow(
                imagePath,
                timestamp,
                capturedAt,
                OptionalValue(record, indexes, "screen_type"),
                OptionalValue(record, indexes, "capture_source"),
                OptionalInt(record, indexes, "width"),
                OptionalInt(record, indexes, "height"),
                OptionalInt(record, indexes, "frame_index")));
        }
        return result;
    }

    private static int? OptionalInt(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> indexes,
        string name)
    {
        var value = OptionalValue(row, indexes, name);
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidDataException($"Capture manifest {name} is invalid.");
    }

    private static string Value(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> indexes,
        string name) =>
        indexes.TryGetValue(name, out var index) && index < row.Count ? row[index] : string.Empty;

    private static string? OptionalValue(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> indexes,
        string name)
    {
        var value = Value(row, indexes, name);
        return value.Length == 0 ? null : value;
    }

    private static List<IReadOnlyList<string>> ParseCsv(string text)
    {
        var records = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (character == ',')
            {
                current.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                current.Add(field.ToString());
                field.Clear();
                records.Add(current);
                current = [];
            }
            else
            {
                field.Append(character);
            }
        }
        if (quoted) throw new InvalidDataException("Capture manifest contains an unterminated quoted field.");
        if (field.Length > 0 || current.Count > 0) { current.Add(field.ToString()); records.Add(current); }
        return records;
    }
}
