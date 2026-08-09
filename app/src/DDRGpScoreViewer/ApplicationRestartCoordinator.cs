using System.ComponentModel;
using System.Diagnostics;

namespace DDRGpScoreViewer;

internal static class ApplicationRestartCoordinator
{
    internal const string WaitForProcessArgumentPrefix = "--ddrgp-wait-for-process=";

    internal static async Task WaitForPreviousProcessAsync(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var processId = TryGetPreviousProcessId(arguments);
        if (processId is null || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var previousProcess = Process.GetProcessById(processId.Value);
            await previousProcess.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // The previous process already exited before the child started waiting.
        }
        catch (InvalidOperationException)
        {
            // The previous process handle is no longer available.
        }
        catch (Win32Exception)
        {
            // The previous process could not be opened; continue with normal startup.
        }
    }

    internal static void StartAfterCurrentProcessExit(int currentProcessId)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("現在のアプリケーションpathを取得できません。");
        }

        using var restartProcess = Process.Start(
            BuildStartInfo(
                currentProcessId,
                Environment.GetCommandLineArgs().Skip(1),
                processPath,
                AppContext.BaseDirectory));
        if (restartProcess is null)
        {
            throw new InvalidOperationException("再起動用processを起動できません。");
        }
    }

    internal static ProcessStartInfo BuildStartInfo(
        int currentProcessId,
        IEnumerable<string> arguments,
        string processPath,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            if (!argument.StartsWith(WaitForProcessArgumentPrefix, StringComparison.Ordinal))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        startInfo.ArgumentList.Add($"{WaitForProcessArgumentPrefix}{currentProcessId}");
        return startInfo;
    }

    private static int? TryGetPreviousProcessId(IEnumerable<string> arguments)
    {
        var waitArgument = arguments.FirstOrDefault(argument =>
            argument.StartsWith(WaitForProcessArgumentPrefix, StringComparison.Ordinal));
        if (waitArgument is null ||
            !int.TryParse(waitArgument[WaitForProcessArgumentPrefix.Length..], out var processId) ||
            processId <= 0)
        {
            return null;
        }

        return processId;
    }
}
