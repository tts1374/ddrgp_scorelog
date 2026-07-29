using System.Text;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class CaptureSaveWorkflowRunnerTests
{
    [Fact]
    public async Task Live_candidate_uses_the_app_owned_workflow_without_a_checkout()
    {
        var frame = new CapturedFrame(
            [137, 80, 78, 71, 13, 10, 26, 10],
            1280,
            720,
            1_000,
            DateTimeOffset.Parse("2026-07-29T12:00:00+09:00"),
            "fixture");
        var runner = new AppOwnedCaptureSaveWorkflowRunner();

        var result = await runner.RunCandidateAsync(
            frame,
            Path.Combine(Path.GetTempPath(), "app-owned-score.sqlite"),
            "master.sqlite",
            null);

        Assert.Equal("completed", result.Status);
        Assert.Equal(1, result.EventCount);
        Assert.Equal(1, result.StatusCounts["unresolved"]);
        Assert.Contains("formal_play_required", result.Reasons);
    }

    [Fact]
    public async Task Manifest_capture_keeps_unresolved_result_events_out_of_formal_db()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-capture-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(
                Path.Combine(root, "frame.png"),
                [137, 80, 78, 71, 13, 10, 26, 10]);
            var manifestPath = Path.Combine(root, "frame_manifest.csv");
            File.WriteAllText(
                manifestPath,
                "image_path,timestamp_ms,screen_type,capture_source,width,height,captured_at_utc\n" +
                "frame.png,1000,result,fixture,1280,720,2026-07-29T12:00:00+09:00\n" +
                "frame.png,2000,result,fixture,1280,720,2026-07-29T12:00:01+09:00\n" +
                "frame.png,3000,result,fixture,1280,720,2026-07-29T12:00:02+09:00\n",
                new UTF8Encoding(false));
            var runner = new AppOwnedCaptureSaveWorkflowRunner();

            var result = await runner.RunAsync(
                manifestPath,
                Path.Combine(root, "score.sqlite"),
                "master.sqlite");

            Assert.Equal("completed", result.Status);
            Assert.Equal(1, result.EventCount);
            Assert.Equal(1, result.StatusCounts["unresolved"]);
            Assert.False(File.Exists(Path.Combine(root, "score.sqlite")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Invalid_manifest_is_reported_as_workflow_failure_without_process_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-capture-save-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "frame_manifest.csv");
            File.WriteAllText(path, "timestamp_ms\n1000\n", new UTF8Encoding(false));

            var result = await new AppOwnedCaptureSaveWorkflowRunner().RunAsync(
                path,
                Path.Combine(root, "score.sqlite"),
                "master.sqlite");

            Assert.Equal("workflow_failed", result.Status);
            Assert.NotEmpty(result.Reasons);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
