namespace JacketCatalogCollector.Tests;

public sealed class ProcessRunnerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "collector-process-tests-" + Guid.NewGuid().ToString("N"));

    public ProcessRunnerTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task CancellationWaitsForKilledChildToReleaseFileHandles()
    {
        var lockedPath = Path.Combine(root, "locked.txt");
        var readyPath = Path.Combine(root, "ready.txt");
        File.WriteAllText(lockedPath, "locked");
        var script =
            $"$stream=[IO.File]::Open('{lockedPath}',[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); "
            + $"[IO.File]::WriteAllText('{readyPath}','ready'); "
            + "try { Start-Sleep -Seconds 30 } finally { $stream.Dispose() }";
        using var cancellation = new CancellationTokenSource();
        var runner = new ProcessRunner();
        var task = runner.RunAsync(
            new ProcessRequest("powershell", ["-NoProfile", "-Command", script], root),
            cancellation.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(readyPath) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
        Assert.True(File.Exists(readyPath), "Child process did not acquire the fixture handle.");
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        File.Delete(lockedPath);
        Assert.False(File.Exists(lockedPath));
    }
}
