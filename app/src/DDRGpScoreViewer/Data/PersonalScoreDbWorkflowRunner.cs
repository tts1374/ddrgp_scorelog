using System.Diagnostics;
using System.IO;
using System.Text.Json;

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

public sealed class PythonPersonalScoreDbWorkflowRunner : IPersonalScoreDbWorkflowRunner
{
    private readonly string pythonExecutable;
    private readonly string? repositoryRoot;

    public PythonPersonalScoreDbWorkflowRunner()
        : this(Environment.GetEnvironmentVariable("DDRGP_PYTHON") ?? "python", null)
    {
    }

    public PythonPersonalScoreDbWorkflowRunner(
        string pythonExecutable,
        string? repositoryRoot)
    {
        this.pythonExecutable = pythonExecutable;
        this.repositoryRoot = repositoryRoot is null ? null : Path.GetFullPath(repositoryRoot);
    }

    public async Task<PersonalScoreDbWorkflowResult> RunAsync(
        string workflowInputPath,
        string scoreDatabasePath,
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
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add("tools.vision_poc.personal_score_db_workflow_app");
            startInfo.ArgumentList.Add("--input");
            startInfo.ArgumentList.Add(Path.GetFullPath(workflowInputPath));
            startInfo.ArgumentList.Add("--database");
            startInfo.ArgumentList.Add(Path.GetFullPath(scoreDatabasePath));

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("Python process could not be started.");
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var payload = process.ExitCode == 0 ? stdout : stderr;
            return ParseResult(payload);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return FailedResult(scoreDatabasePath, exception.Message);
        }
    }

    public static PersonalScoreDbWorkflowResult ParseResult(string payload)
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
            return new PersonalScoreDbWorkflowResult(
                root.GetProperty("workflow_status").GetString() ?? "invalid",
                root.GetProperty("artifact_status").GetString() ?? "not_requested",
                root.GetProperty("adapter_status").GetString() ?? "invalid",
                root.GetProperty("db_status").GetString() ?? "not_checked",
                root.GetProperty("written").GetBoolean(),
                OptionalString(root, "source_capture_id"),
                OptionalString(root, "analysis_id"),
                OptionalString(root, "play_id"),
                root.GetProperty("reasons").EnumerateArray()
                    .Select(value => value.GetString() ?? string.Empty).ToArray(),
                OptionalString(root, "artifact_path"),
                root.GetProperty("db_path").GetString() ?? string.Empty);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return FailedResult(string.Empty, $"Workflow result could not be read: {exception.Message}");
        }
    }

    private static string? OptionalString(JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static PersonalScoreDbWorkflowResult FailedResult(string databasePath, string reason) =>
        new(
            "process_failed", "not_requested", "invalid", "not_checked", false,
            null, null, null, [reason], null, databasePath);

}
