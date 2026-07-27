using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using DDRGpScoreViewer.Capture;

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
        CancellationToken cancellationToken = default);
}

public sealed class PythonCaptureSaveWorkflowRunner :
    ICaptureSaveWorkflowRunner,
    ILiveCaptureSaveWorkflowRunner
{
    private readonly string pythonExecutable;
    private readonly string? repositoryRoot;

    public PythonCaptureSaveWorkflowRunner()
        : this(Environment.GetEnvironmentVariable("DDRGP_PYTHON") ?? "python", null)
    {
    }

    public PythonCaptureSaveWorkflowRunner(string pythonExecutable, string? repositoryRoot)
    {
        this.pythonExecutable = pythonExecutable;
        this.repositoryRoot = repositoryRoot is null ? null : Path.GetFullPath(repositoryRoot);
    }

    public async Task<CaptureSaveWorkflowResult> RunAsync(
        string manifestPath,
        string scoreDatabasePath,
        string masterDatabasePath,
        CancellationToken cancellationToken = default)
    {
        return await RunPythonAsync(
            [
                "-m", "tools.vision_poc.capture_save_workflow_app",
                "--manifest", Path.GetFullPath(manifestPath),
                "--database", Path.GetFullPath(scoreDatabasePath),
                "--master-database", Path.GetFullPath(masterDatabasePath),
            ],
            cancellationToken);
    }

    public async Task<CaptureSaveWorkflowResult> RunCandidateAsync(
        CapturedFrame frame,
        string scoreDatabasePath,
        string masterDatabasePath,
        CancellationToken cancellationToken = default)
    {
        var transientRoot = Path.Combine(
            Path.GetTempPath(),
            "ddrgp-scorelog-live",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(transientRoot);
            var imagePath = Path.Combine(transientRoot, "live_result.png");
            var manifestPath = Path.Combine(transientRoot, "frame_manifest.csv");
            await File.WriteAllBytesAsync(imagePath, frame.PngBytes, cancellationToken);
            var capturedAt = frame.CapturedAtUtc.ToString("O");
            var manifest = string.Join(
                "\n",
                [
                    "image_path,timestamp_ms,captured_at_utc,screen_type,organized_file",
                    $"live_result.png,{frame.TimestampMs},\"{capturedAt}\",result,live_result.png",
                    string.Empty,
                ]);
            await File.WriteAllTextAsync(
                manifestPath,
                manifest,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var sourceReference = $"live-memory://{Path.GetFileName(transientRoot)}";
            return await RunPythonAsync(
                [
                    "-m", "tools.vision_poc.capture_save_workflow_app",
                    "--manifest", manifestPath,
                    "--database", Path.GetFullPath(scoreDatabasePath),
                    "--master-database", Path.GetFullPath(masterDatabasePath),
                    "--output", Path.Combine(transientRoot, "analysis"),
                    "--transient-source", sourceReference,
                ],
                cancellationToken);
        }
        finally
        {
            DeleteTransientRoot(transientRoot);
        }
    }

    private async Task<CaptureSaveWorkflowResult> RunPythonAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                WorkingDirectory = repositoryRoot ?? RepositoryRootLocator.Find(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("Python process could not be started.");
            }
            var output = await WorkflowProcessLifecycle.WaitForExitAndOutputAsync(
                process,
                process.StandardOutput.ReadToEndAsync(),
                process.StandardError.ReadToEndAsync(),
                cancellationToken);
            var stdout = output[0];
            var stderr = output[1];
            return ParseResult(process.ExitCode == 0 ? stdout : stderr);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return FailedResult(exception.Message);
        }
    }

    private static void DeleteTransientRoot(string path)
    {
        try
        {
            var tempRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
        catch (IOException)
        {
            // The transient directory is best-effort cleanup; no persistent output is published.
        }
        catch (UnauthorizedAccessException)
        {
            // The transient directory is best-effort cleanup; no persistent output is published.
        }
    }

    public static CaptureSaveWorkflowResult ParseResult(string payload)
    {
        try
        {
            var jsonLine = payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(line => line.TrimStart().StartsWith('{'));
            if (jsonLine is null)
            {
                throw new JsonException("No JSON result was emitted.");
            }
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.GetProperty("result_schema_version").GetInt32() != 1)
            {
                throw new JsonException("Unsupported result schema version.");
            }
            var counts = root.GetProperty("status_counts").EnumerateObject()
                .ToDictionary(item => item.Name, item => item.Value.GetInt32());
            return new CaptureSaveWorkflowResult(
                root.GetProperty("status").GetString() ?? "process_failed",
                root.GetProperty("event_count").GetInt32(),
                counts,
                root.GetProperty("saved_play_ids").EnumerateArray()
                    .Select(value => value.GetString() ?? string.Empty).ToArray(),
                root.GetProperty("reasons").EnumerateArray()
                    .Select(value => value.GetString() ?? string.Empty).ToArray(),
                OptionalString(root, "analysis_output"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return FailedResult($"Capture workflow result could not be read: {exception.Message}");
        }
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static CaptureSaveWorkflowResult FailedResult(string reason) =>
        new("process_failed", 0, new Dictionary<string, int>(), [], [reason], null);
}
