using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ApplicationRestartTests
{
    [Fact]
    public void BuildStartInfo_preserves_arguments_and_waits_for_the_current_process()
    {
        var startInfo = ApplicationRestartCoordinator.BuildStartInfo(
            currentProcessId: 1234,
            arguments:
            [
                "--sample",
                "value with spaces",
                "--ddrgp-wait-for-process=99",
            ],
            processPath: @"C:\Apps\DDRGpScoreViewer.exe",
            workingDirectory: @"C:\Apps");

        Assert.Equal(@"C:\Apps\DDRGpScoreViewer.exe", startInfo.FileName);
        Assert.Equal(@"C:\Apps", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(
            [
                "--sample",
                "value with spaces",
                "--ddrgp-wait-for-process=1234",
            ],
            startInfo.ArgumentList);
    }

    [Fact]
    public async Task WaitForPreviousProcessAsync_ignores_invalid_process_arguments()
    {
        await ApplicationRestartCoordinator.WaitForPreviousProcessAsync(
            ["--ddrgp-wait-for-process=not-a-process-id"]);
        await ApplicationRestartCoordinator.WaitForPreviousProcessAsync(
            ["--ddrgp-wait-for-process=2147483647"]);
    }
}
