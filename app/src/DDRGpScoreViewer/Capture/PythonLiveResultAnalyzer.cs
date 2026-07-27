using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DDRGpScoreViewer.Capture;

public sealed class PythonLiveResultAnalyzer : ILiveResultAnalyzer, IAsyncDisposable
{
    private readonly string pythonExecutable;
    private readonly string repositoryRoot;
    private readonly SemaphoreSlim processLock = new(1, 1);
    private Process? process;
    private StreamWriter? standardInput;
    private Task<string>? standardError;

    public PythonLiveResultAnalyzer()
        : this(
            Environment.GetEnvironmentVariable("DDRGP_PYTHON") ?? "python",
            null)
    {
    }

    public PythonLiveResultAnalyzer(string pythonExecutable, string? repositoryRoot)
    {
        this.pythonExecutable = pythonExecutable;
        this.repositoryRoot = Path.GetFullPath(repositoryRoot ?? RepositoryRootLocator.Find());
    }

    public async Task<LiveResultObservation> AnalyzeAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        await processLock.WaitAsync(cancellationToken);
        try
        {
            EnsureProcess();
            var payload = JsonSerializer.Serialize(new
            {
                png_base64 = Convert.ToBase64String(frame.PngBytes),
            });
            await standardInput!.WriteLineAsync(payload);
            await standardInput.FlushAsync(cancellationToken);
            var line = await process!.StandardOutput.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new InvalidOperationException(
                    "live result analyzer ended without returning an observation.");
            }
            return ParseObservation(line);
        }
        finally
        {
            processLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await processLock.WaitAsync();
        try
        {
            if (process is not null)
            {
                await TerminateProcessAsync(process);
            }
            process = null;
            standardInput = null;
            standardError = null;
        }
        finally
        {
            processLock.Release();
            processLock.Dispose();
        }
    }

    internal static LiveResultObservation ParseObservation(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return new LiveResultObservation(
            root.GetProperty("result_screen").GetBoolean(),
            OptionalString(root, "score"),
            OptionalString(root, "title_signature"),
            OptionalString(root, "reason"));
    }

    private void EnsureProcess()
    {
        if (process is not null)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "live result analyzer process exited; restart monitoring explicitly.");
            }
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-m", "tools.vision_poc.live_result_app" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            process = null;
            throw new InvalidOperationException("Python live result analyzer could not be started.");
        }
        standardInput = process.StandardInput;
        standardError = process.StandardError.ReadToEndAsync();
    }

    private static async Task TerminateProcessAsync(Process value)
    {
        try
        {
            if (!value.HasExited)
            {
                value.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (value.HasExited)
        {
            // The process exited between the state check and Kill.
        }
        await value.WaitForExitAsync();
        value.Dispose();
    }

    private static string OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
