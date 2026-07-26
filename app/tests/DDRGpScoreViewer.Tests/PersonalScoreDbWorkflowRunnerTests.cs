using System.Diagnostics;
using System.Text.Json;
using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class PersonalScoreDbWorkflowRunnerTests
{
    [Fact]
    public void Default_constructor_does_not_require_repository_lookup()
    {
        var exception = Record.Exception(() => new PythonPersonalScoreDbWorkflowRunner());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("saved", "play-1")]
    [InlineData("duplicate", null)]
    [InlineData("excluded", null)]
    [InlineData("unresolved", null)]
    [InlineData("invalid", null)]
    [InlineData("db_rejected", null)]
    [InlineData("artifact_created_db_failed", null)]
    public void ParseResult_preserves_workflow_status_and_nullable_play(
        string workflowStatus,
        string? playId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            result_schema_version = 1,
            workflow_status = workflowStatus,
            artifact_status = "not_requested",
            adapter_status = "ready",
            db_status = "not_checked",
            written = playId is not null,
            source_capture_id = (string?)null,
            analysis_id = (string?)null,
            play_id = playId,
            reasons = Array.Empty<string>(),
            artifact_path = (string?)null,
            db_path = "score.sqlite",
        });

        var result = PythonPersonalScoreDbWorkflowRunner.ParseResult(payload);

        Assert.Equal(workflowStatus, result.WorkflowStatus);
        Assert.Equal(playId, result.PlayId);
    }

    [Fact]
    public void ParseResult_maps_malformed_process_output_to_failure()
    {
        var result = PythonPersonalScoreDbWorkflowRunner.ParseResult("not json");

        Assert.Equal("process_failed", result.WorkflowStatus);
        Assert.False(result.Written);
        Assert.NotEmpty(result.Reasons);
    }

    [Fact]
    public void ParseResult_reads_final_json_line_after_runtime_warning()
    {
        const string payload = "runtime warning\n" +
            "{\"result_schema_version\":1,\"workflow_status\":\"unresolved\"," +
            "\"artifact_status\":\"not_requested\",\"adapter_status\":\"unresolved\"," +
            "\"db_status\":\"not_checked\",\"written\":false," +
            "\"source_capture_id\":null,\"analysis_id\":null,\"play_id\":null," +
            "\"reasons\":[\"fixture\"],\"artifact_path\":null," +
            "\"db_path\":\"score.sqlite\"}\n";

        var result = PythonPersonalScoreDbWorkflowRunner.ParseResult(payload);

        Assert.Equal("unresolved", result.WorkflowStatus);
    }

    [Fact]
    public Task RunAsync_terminates_python_process_tree_when_cancelled() =>
        AssertPythonProcessTreeTerminatesAsync(
            "personal_score_db_workflow_app",
            async (root, cancellationToken) =>
                await new PythonPersonalScoreDbWorkflowRunner("python", root).RunAsync(
                    Path.Combine(root, "input.json"),
                    Path.Combine(root, "score.sqlite"),
                    cancellationToken));

    [Fact]
    public Task CaptureSaveRunAsync_terminates_python_process_tree_when_cancelled() =>
        AssertPythonProcessTreeTerminatesAsync(
            "capture_save_workflow_app",
            async (root, cancellationToken) =>
                await new PythonCaptureSaveWorkflowRunner("python", root).RunAsync(
                    Path.Combine(root, "manifest.csv"),
                    Path.Combine(root, "score.sqlite"),
                    Path.Combine(root, "master.sqlite"),
                    cancellationToken));

    private static async Task AssertPythonProcessTreeTerminatesAsync(
        string moduleName,
        Func<string, CancellationToken, Task> start)
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"ddrgp-workflow-runner-{Guid.NewGuid():N}");
        var packagePath = Path.Combine(root, "tools", "vision_poc");
        var pidPath = Path.Combine(root, "child.pid");
        Directory.CreateDirectory(packagePath);
        File.WriteAllText(Path.Combine(root, "tools", "__init__.py"), string.Empty);
        File.WriteAllText(Path.Combine(packagePath, "__init__.py"), string.Empty);
        File.WriteAllText(
            Path.Combine(packagePath, $"{moduleName}.py"),
            $"""
            import os
            import pathlib
            import time

            pathlib.Path({JsonSerializer.Serialize(pidPath)}).write_text(
                str(os.getpid()), encoding="utf-8"
            )
            while True:
                time.sleep(1)
            """);

        using var cancellation = new CancellationTokenSource();
        var runTask = start(root, cancellation.Token);
        var childProcessId = 0;
        try
        {
            Assert.True(await WaitForFileAsync(pidPath));
            childProcessId = int.Parse(await File.ReadAllTextAsync(pidPath));

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runTask.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(await WaitForProcessExitAsync(childProcessId));
        }
        finally
        {
            cancellation.Cancel();
            if (!runTask.IsCompleted)
            {
                try
                {
                    await runTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (Exception)
                {
                    // Preserve the primary assertion while cleaning up the test child.
                }
            }
            if (childProcessId != 0)
            {
                TryTerminateProcessTree(childProcessId);
            }
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Test temp cleanup must not hide the assertion result.
            }
        }
    }

    private static async Task<bool> WaitForFileAsync(string path)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            if (File.Exists(path))
            {
                return true;
            }
            await Task.Delay(20);
        }
        return false;
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            if (!IsProcessRunning(processId))
            {
                return true;
            }
            await Task.Delay(20);
        }
        return !IsProcessRunning(processId);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryTerminateProcessTree(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2_000);
            }
        }
        catch (ArgumentException)
        {
            // The process already exited.
        }
    }
}
