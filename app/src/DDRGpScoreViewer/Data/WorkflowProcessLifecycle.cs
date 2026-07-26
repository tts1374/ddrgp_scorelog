using System.Diagnostics;

namespace DDRGpScoreViewer.Data;

internal static class WorkflowProcessLifecycle
{
    public static async Task<string[]> WaitForExitAndOutputAsync(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        CancellationToken cancellationToken)
    {
        var outputTask = Task.WhenAll(stdoutTask, stderrTask);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return await outputTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TerminateProcessTreeAsync(process);
            await outputTask;
            throw;
        }
    }

    public static async Task TerminateProcessTreeAsync(Process process)
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
}
