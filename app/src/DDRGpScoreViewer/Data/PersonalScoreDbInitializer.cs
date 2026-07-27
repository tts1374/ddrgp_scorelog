using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DDRGpScoreViewer.Data;

public sealed record ScoreDatabaseInitializationResult(
    bool Succeeded,
    bool Initialized,
    string Message);

public interface IScoreDatabaseInitializer
{
    Task<ScoreDatabaseInitializationResult> InitializeIfMissingAsync(
        string scoreDatabasePath,
        CancellationToken cancellationToken = default);
}

public sealed class PythonPersonalScoreDbInitializer : IScoreDatabaseInitializer
{
    private readonly string pythonExecutable;
    private readonly string? repositoryRoot;

    public PythonPersonalScoreDbInitializer()
        : this(Environment.GetEnvironmentVariable("DDRGP_PYTHON") ?? "python", null)
    {
    }

    public PythonPersonalScoreDbInitializer(
        string pythonExecutable,
        string? repositoryRoot)
    {
        this.pythonExecutable = pythonExecutable;
        this.repositoryRoot = repositoryRoot is null ? null : Path.GetFullPath(repositoryRoot);
    }

    public async Task<ScoreDatabaseInitializationResult> InitializeIfMissingAsync(
        string scoreDatabasePath,
        CancellationToken cancellationToken = default)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(scoreDatabasePath);
        }
        catch (ArgumentException exception)
        {
            return FailedResult(scoreDatabasePath, $"score DBのpathを確認できません。{exception.Message}");
        }

        var preparation = InspectPathForInitialization(fullPath);
        if (!preparation.ShouldInitialize)
        {
            return preparation.Result!;
        }

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
            startInfo.ArgumentList.Add("tools.vision_poc");
            startInfo.ArgumentList.Add("--personal-score-db-diagnostic");
            startInfo.ArgumentList.Add(fullPath);
            startInfo.ArgumentList.Add("--personal-score-db-diagnostic-mode");
            startInfo.ArgumentList.Add("prepare-write");
            startInfo.ArgumentList.Add("--personal-score-db-diagnostic-format");
            startInfo.ArgumentList.Add("json");

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
            var payload = string.IsNullOrWhiteSpace(output[0]) ? output[1] : output[0];
            return ParseResult(payload, process.ExitCode, fullPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.ComponentModel.Win32Exception or
                IOException or
                UnauthorizedAccessException)
        {
            return FailedResult(fullPath, $"score DBの初期化処理を実行できません。{exception.Message}");
        }
    }

    public static ScoreDatabaseInitializationResult ParseResult(
        string payload,
        int exitCode,
        string scoreDatabasePath)
    {
        try
        {
            using var document = JsonDocument.Parse(ExtractJsonObject(payload));
            var root = document.RootElement;
            var compatible = root.GetProperty("is_compatible").GetBoolean();
            var initialized = false;
            if (root.TryGetProperty("file_preparation", out var preparation) &&
                preparation.ValueKind == JsonValueKind.Object &&
                preparation.TryGetProperty("initialized", out var initializedValue))
            {
                initialized = initializedValue.GetBoolean();
            }

            var reasons = root.TryGetProperty("compatibility_errors", out var errors)
                ? errors.EnumerateArray()
                    .Select(value => value.GetString() ?? string.Empty)
                    .Where(value => value.Length > 0)
                    .ToArray()
                : Array.Empty<string>();
            var reason = reasons.Length == 0
                ? compatible
                    ? initialized
                        ? "固定score DBを正式schemaへ初期化しました。"
                        : "固定score DBは初期化せず、既存fileを使用します。"
                    : $"score DBの初期化結果がcompatibleではありません（exit code: {exitCode}）。"
                : $"score DBを使用できません。{string.Join(" / ", reasons)}";
            return new(compatible && exitCode == 0, initialized, reason);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException)
        {
            return FailedResult(
                scoreDatabasePath,
                $"score DBの初期化結果を読み込めません。{exception.Message}");
        }
    }

    private static InitializationPathInspection InspectPathForInitialization(string path)
    {
        if (Directory.Exists(path))
        {
            return new(
                false,
                FailedResult(path, "score DBのpathがdirectoryです。固定pathにSQLite fileを配置してください."));
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length != 0)
            {
                return new(
                    false,
                    new ScoreDatabaseInitializationResult(
                        true,
                        false,
                        "既存のscore DBは変更せず、後段のread-only互換性検証へ進みます。"));
            }
        }
        catch (FileNotFoundException)
        {
            // The fixed path is missing and may be prepared by the existing boundary.
        }
        catch (DirectoryNotFoundException)
        {
            // The fixed parent directory is prepared before this method is called.
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(false, FailedResult(path, $"score DBをreadできません。{exception.Message}"));
        }
        catch (IOException exception)
        {
            return new(false, FailedResult(path, $"score DBの状態を確認できません。{exception.Message}"));
        }

        return new(true, null);
    }

    private static string ExtractJsonObject(string payload)
    {
        var start = payload.IndexOf('{');
        var end = payload.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new JsonException("JSON object was not emitted.");
        }
        return payload[start..(end + 1)];
    }

    private static ScoreDatabaseInitializationResult FailedResult(string path, string reason) =>
        new(false, false, $"{reason} path: {path}");

    private sealed record InitializationPathInspection(
        bool ShouldInitialize,
        ScoreDatabaseInitializationResult? Result);
}
