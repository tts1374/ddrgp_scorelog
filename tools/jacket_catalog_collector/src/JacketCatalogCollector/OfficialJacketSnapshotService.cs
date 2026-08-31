using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace JacketCatalogCollector;

public sealed record OfficialJacketSnapshotProgress(
    string Phase,
    int Completed,
    int? Total);

public sealed record OfficialJacketSnapshotMetadata(
    string SnapshotId,
    string CompletedAt,
    int SongCount,
    int StoredImageCount,
    string RootPath);

public enum OfficialSnapshotOperationOutcome
{
    NotRun,
    Succeeded,
    Failed,
    Canceled,
}

public sealed record OfficialJacketSnapshotUpdateResult(
    OfficialJacketSnapshotMetadata Metadata);

public interface IOfficialJacketSnapshotService
{
    Task<OfficialJacketSnapshotMetadata?> LoadAsync(CancellationToken cancellationToken);

    Task<OfficialJacketSnapshotUpdateResult> UpdateAsync(
        IProgress<OfficialJacketSnapshotProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class OfficialJacketSnapshotUpdateException : InvalidOperationException
{
    public OfficialJacketSnapshotUpdateException(string message, string phase)
        : base(message)
    {
        Phase = phase;
    }

    public string Phase { get; }
}

public static class OfficialJacketSnapshotMetadataLoader
{
    private const string CompleteStatus = "complete";

    public static OfficialJacketSnapshotMetadata? TryLoad(string rootPath)
    {
        try
        {
            return ReadRequired(rootPath);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static OfficialJacketSnapshotMetadata ReadRequired(string rootPath)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(fullRoot);
        }

        RequireFile(fullRoot, "manifest.json");
        RequireFile(fullRoot, "songs.jsonl");
        RequireFile(fullRoot, "summary.json");
        RequireDirectory(fullRoot, "pages");
        RequireDirectory(fullRoot, "jackets");

        using var manifest = ReadJson(Path.Combine(fullRoot, "manifest.json"));
        using var summary = ReadJson(Path.Combine(fullRoot, "summary.json"));
        var manifestRoot = manifest.RootElement;
        var summaryRoot = summary.RootElement;
        RequireCompleteStatus(manifestRoot, "manifest");
        RequireCompleteStatus(summaryRoot, "summary");

        var snapshotId = RequiredString(manifestRoot, "snapshot_id", "manifest");
        if (RequiredString(summaryRoot, "snapshot_id", "summary") != snapshotId)
        {
            throw new InvalidDataException("manifest and summary snapshot IDs differ.");
        }
        var completedAt = RequiredString(manifestRoot, "completed_at", "manifest");
        if (!DateTimeOffset.TryParse(
                completedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _))
        {
            throw new InvalidDataException("manifest completed_at is invalid.");
        }

        var songCount = NonNegativeInt(summaryRoot, "song_count", "summary");
        var pageRequestCount = NonNegativeInt(summaryRoot, "page_request_count", "summary");
        var imageRequestCount = NonNegativeInt(summaryRoot, "image_request_count", "summary");
        var songsPath = Path.Combine(fullRoot, "songs.jsonl");
        var songLineCount = File.ReadLines(songsPath)
            .Count(line => !string.IsNullOrWhiteSpace(line));
        if (songLineCount != songCount)
        {
            throw new InvalidDataException("summary song_count does not match songs.jsonl.");
        }
        var pageFileCount = Directory.EnumerateFiles(
                Path.Combine(fullRoot, "pages"),
                "*",
                SearchOption.TopDirectoryOnly)
            .Count();
        if (pageFileCount != pageRequestCount)
        {
            throw new InvalidDataException("summary page_request_count does not match pages.");
        }
        var imageRecords = RequiredArray(manifestRoot, "images", "manifest");
        if (RequiredArray(manifestRoot, "failures", "manifest").GetArrayLength() != 0)
        {
            throw new InvalidDataException("complete snapshot contains failures.");
        }
        if (imageRecords.GetArrayLength() != imageRequestCount)
        {
            throw new InvalidDataException(
                "summary image_request_count does not match manifest images.");
        }
        var storedPaths = imageRecords.EnumerateArray()
            .Select(image =>
            {
                if (!image.TryGetProperty("error", out var error)
                    || error.ValueKind != JsonValueKind.Null)
                {
                    throw new InvalidDataException("complete snapshot contains an image error.");
                }
                return RequiredString(image, "local_path", "manifest image");
            })
            .ToHashSet(StringComparer.Ordinal);
        var jacketFileCount = Directory.EnumerateFiles(
                Path.Combine(fullRoot, "jackets"),
                "*",
                SearchOption.TopDirectoryOnly)
            .Count();
        var storedImageCount = NonNegativeInt(summaryRoot, "stored_jacket_count", "summary");
        if (jacketFileCount != storedPaths.Count
            || (storedImageCount != storedPaths.Count
                && storedImageCount != imageRecords.GetArrayLength()))
        {
            throw new InvalidDataException("summary stored_jacket_count does not match jackets.");
        }
        return new OfficialJacketSnapshotMetadata(
            snapshotId,
            completedAt,
            songCount,
            storedPaths.Count,
            fullRoot);
    }

    private static JsonDocument ReadJson(string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (JsonException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"snapshot metadata cannot be read: {path}", exception);
        }
    }

    private static void RequireCompleteStatus(JsonElement root, string documentName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("status", out var status)
            || status.GetString() != CompleteStatus)
        {
            throw new InvalidDataException($"{documentName} status is not complete.");
        }
    }

    private static string RequiredString(JsonElement root, string propertyName, string documentName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException(
                $"{documentName} {propertyName} is missing or empty.");
        }
        return value.GetString()!;
    }

    private static int NonNegativeInt(JsonElement root, string propertyName, string documentName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed)
            || parsed < 0)
        {
            throw new InvalidDataException(
                $"{documentName} {propertyName} is invalid.");
        }
        return parsed;
    }

    private static JsonElement RequiredArray(
        JsonElement root,
        string propertyName,
        string documentName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"{documentName} {propertyName} is missing or not an array.");
        }
        return value;
    }

    private static void RequireFile(string rootPath, string name)
    {
        if (!File.Exists(Path.Combine(rootPath, name)))
        {
            throw new FileNotFoundException($"snapshot file is missing: {name}");
        }
    }

    private static void RequireDirectory(string rootPath, string name)
    {
        if (!Directory.Exists(Path.Combine(rootPath, name)))
        {
            throw new InvalidDataException($"snapshot directory is missing: {name}");
        }
    }
}

public sealed class PythonOfficialJacketSnapshotService(
    IProcessRunner processRunner,
    string repositoryRoot,
    string snapshotRootPath,
    string pythonExecutable = "python") : IOfficialJacketSnapshotService
{
    private const int CancelledExitCode = 3;

    public Task<OfficialJacketSnapshotMetadata?> LoadAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            OfficialJacketSnapshotMetadataLoader.TryLoad(ResolvePath(snapshotRootPath)));
    }

    public async Task<OfficialJacketSnapshotUpdateResult> UpdateAsync(
        IProgress<OfficialJacketSnapshotProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        var resolvedSnapshotRoot = ResolvePath(snapshotRootPath);
        var cancelFile = Path.Combine(
            Path.GetTempPath(),
            $"ddrgp-official-snapshot-cancel-{Guid.NewGuid():N}.flag");
        var incompleteRoot = Path.Combine(
            Path.GetDirectoryName(resolvedSnapshotRoot)
                ?? throw new InvalidOperationException("snapshot root has no parent directory."),
            Path.GetFileName(resolvedSnapshotRoot) + ".incomplete");
        var arguments = new List<string>
        {
            "-X", "utf8", "-m", "tools.ddrworld_music_snapshot", "fetch",
            "--allow-network",
            "--fixed-output",
            "--output-root", resolvedSnapshotRoot,
            "--incomplete-root", incompleteRoot,
            "--cancel-file", cancelFile,
            "--progress-json",
        };
        var request = new ProcessRequest(
            pythonExecutable,
            arguments,
            resolvedRepositoryRoot);
        var lastPhase = "ページ取得";
        using var cancellationRegistration = cancellationToken.Register(
            () => CreateCancellationMarker(cancelFile));
        try
        {
            ProcessResult result;
            if (processRunner is IStreamingProcessRunner streamingRunner)
            {
                result = await streamingRunner.RunStreamingAsync(
                    request,
                    line => ReadProgress(line, progress, phase => lastPhase = phase),
                    CancellationToken.None);
            }
            else
            {
                result = await processRunner.RunAsync(request, cancellationToken);
            }

            if (result.ExitCode == CancelledExitCode)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            if (result.ExitCode != 0)
            {
                var reason = ExtractFailureReason(result.StandardError);
                throw new OfficialJacketSnapshotUpdateException(
                    $"公式ジャケット情報の取得に失敗しました（{lastPhase}）。"
                    + $"理由: {reason}。"
                    + "既存の公式ジャケット情報は維持されています。",
                    lastPhase);
            }

            OfficialJacketSnapshotMetadata metadata;
            try
            {
                metadata = OfficialJacketSnapshotMetadataLoader.ReadRequired(resolvedSnapshotRoot);
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or FileNotFoundException
                    or DirectoryNotFoundException
                    or JsonException
                    or InvalidOperationException)
            {
                throw new OfficialJacketSnapshotUpdateException(
                    "公式ジャケット情報の取得結果を検証できませんでした。"
                    + "既存の公式ジャケット情報は維持されています。",
                    "検証");
            }
            return new OfficialJacketSnapshotUpdateResult(metadata);
        }
        finally
        {
            TryDelete(cancelFile);
        }
    }

    private string ResolvePath(string path) => Path.GetFullPath(
        Path.IsPathRooted(path)
            ? path
            : Path.Combine(repositoryRoot, path));

    private static string ExtractFailureReason(string standardError)
    {
        var line = standardError
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.StartsWith("error:", StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return "collectorの処理が終了しました";
        }

        var reason = line["error:".Length..].Trim();
        var diagnosticMarker = reason.IndexOf(
            "; diagnostics retained at ",
            StringComparison.OrdinalIgnoreCase);
        if (diagnosticMarker >= 0)
        {
            reason = reason[..diagnosticMarker].TrimEnd();
        }
        return reason.Length <= 160 ? reason : reason[..160] + "…";
    }

    private static void ReadProgress(
        string line,
        IProgress<OfficialJacketSnapshotProgress>? progress,
        Action<string> setPhase)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("event", out var eventName)
                || eventName.ValueKind != JsonValueKind.String
                || eventName.GetString() != "progress")
            {
                return;
            }
            if (!root.TryGetProperty("phase", out var phaseValue)
                || phaseValue.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("completed", out var completedValue)
                || completedValue.ValueKind != JsonValueKind.Number
                || !completedValue.TryGetInt32(out var completed)
                || !root.TryGetProperty("total", out var totalValue))
            {
                throw new InvalidDataException("progress event is invalid.");
            }
            var phase = phaseValue.GetString();
            int? total = totalValue.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.Number when totalValue.TryGetInt32(out var parsedTotal)
                    => parsedTotal,
                _ => throw new InvalidDataException("progress event is invalid."),
            };
            if (phase is not ("pages" or "jackets")
                || completed < 0
                || (total.HasValue && total.Value < 0)
                || (total.HasValue && completed > total.Value)
                || (phase == "jackets" && !total.HasValue))
            {
                throw new InvalidDataException("progress event is invalid.");
            }
            setPhase(phase == "pages" ? "ページ取得" : "ジャケット取得");
            progress?.Report(new OfficialJacketSnapshotProgress(phase, completed, total));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("official snapshot progress is invalid.", exception);
        }
    }

    private static void CreateCancellationMarker(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (IOException)
        {
            // The process may already have observed the marker or exited.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A temporary cancellation marker is diagnostic-only.
        }
    }
}
