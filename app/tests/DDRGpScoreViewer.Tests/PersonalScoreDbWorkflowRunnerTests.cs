using System.Text.Json;
using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class PersonalScoreDbWorkflowRunnerTests
{
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
}
