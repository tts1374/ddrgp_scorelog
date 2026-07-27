using System.Text;
using DDRGpScoreViewer.Capture;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class PythonLiveResultAnalyzerTests
{
    [Fact]
    public async Task Analyzer_restarts_after_python_process_exits()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"ddrgp-live-analyzer-test-{Guid.NewGuid():N}");
        var package = Path.Combine(root, "tools", "vision_poc");
        Directory.CreateDirectory(package);
        WriteUtf8(Path.Combine(root, "tools", "__init__.py"), string.Empty);
        WriteUtf8(Path.Combine(package, "__init__.py"), string.Empty);
        WriteUtf8(
            Path.Combine(package, "live_result_app.py"),
            """
            from pathlib import Path
            import sys

            marker = Path("restart-marker")
            if not marker.exists():
                marker.write_text("started", encoding="utf-8")
                sys.stdin.readline()
                raise SystemExit(0)

            for line in sys.stdin:
                print(
                    '{"result_screen": false, "score": "", "title_signature": "", "reason": "restarted"}',
                    flush=True,
                )
            """);

        var analyzer = new PythonLiveResultAnalyzer(
            Environment.GetEnvironmentVariable("DDRGP_PYTHON") ?? "python",
            root);
        var frame = new CapturedFrame(
            [1, 2, 3],
            1280,
            720,
            1_000,
            DateTimeOffset.UtcNow,
            "fixture");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(frame));

            var observation = await analyzer.AnalyzeAsync(frame);

            Assert.False(observation.IsResultScreen);
            Assert.Equal("restarted", observation.Reason);
        }
        finally
        {
            await analyzer.DisposeAsync();
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // The test process has already been terminated; cleanup is best effort.
            }
            catch (UnauthorizedAccessException)
            {
                // The test process has already been terminated; cleanup is best effort.
            }
        }
    }

    private static void WriteUtf8(string path, string value) =>
        File.WriteAllText(path, value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
