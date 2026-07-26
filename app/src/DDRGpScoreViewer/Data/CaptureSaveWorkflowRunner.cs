using System.Diagnostics;
using System.IO;
using System.Text.Json;

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

public sealed class PythonCaptureSaveWorkflowRunner : ICaptureSaveWorkflowRunner
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
            foreach (var argument in new[]
            {
                "-m", "tools.vision_poc.capture_save_workflow_app",
                "--manifest", Path.GetFullPath(manifestPath),
                "--database", Path.GetFullPath(scoreDatabasePath),
                "--master-database", Path.GetFullPath(masterDatabasePath),
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("Python process could not be started.");
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var outputTask = Task.WhenAll(stdoutTask, stderrTask);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await outputTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TerminateProcessTreeAsync(process);
                await outputTask;
                throw;
            }
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;
            return ParseResult(process.ExitCode == 0 ? stdout : stderr);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return FailedResult(exception.Message);
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

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The process exited between HasExited and Kill.
        }
        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static CaptureSaveWorkflowResult FailedResult(string reason) =>
        new("process_failed", 0, new Dictionary<string, int>(), [], [reason], null);
}
