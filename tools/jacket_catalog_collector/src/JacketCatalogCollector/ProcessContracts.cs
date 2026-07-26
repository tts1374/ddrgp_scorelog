using System.Diagnostics;
using System.IO;
using System.Text;

namespace JacketCatalogCollector;

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public interface IStreamingProcessRunner
{
    Task<ProcessResult> RunStreamingAsync(
        ProcessRequest request,
        Action<string> standardOutputLine,
        CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner, IStreamingProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken)
        => await RunCoreAsync(request, standardOutputLine: null, cancellationToken);

    public async Task<ProcessResult> RunStreamingAsync(
        ProcessRequest request,
        Action<string> standardOutputLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(standardOutputLine);
        return await RunCoreAsync(request, standardOutputLine, cancellationToken);
    }

    private static async Task<ProcessResult> RunCoreAsync(
        ProcessRequest request,
        Action<string>? standardOutputLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {request.FileName}");
        }
        var stdoutTask = standardOutputLine is null
            ? process.StandardOutput.ReadToEndAsync()
            : ReadLinesAsync(process.StandardOutput, standardOutputLine);
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
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
                // The process exited naturally between the state check and Kill.
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdoutTask, stderrTask);
            throw;
        }
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<string> ReadLinesAsync(
        StreamReader reader,
        Action<string> standardOutputLine)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            output.AppendLine(line);
            standardOutputLine(line);
        }
        return output.ToString();
    }
}
