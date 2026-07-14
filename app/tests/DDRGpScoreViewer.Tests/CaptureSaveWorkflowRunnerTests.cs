using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class CaptureSaveWorkflowRunnerTests
{
    [Fact]
    public void ParseResult_reads_last_json_line_and_status_counts()
    {
        const string payload = """
            analysis output
            {"result_schema_version":1,"status":"completed","analysis_output":"data/run","event_count":3,"status_counts":{"saved":1,"unresolved":2},"saved_play_ids":["play-1"],"reasons":[],"events":[]}
            """;

        var result = PythonCaptureSaveWorkflowRunner.ParseResult(payload);

        Assert.Equal("completed", result.Status);
        Assert.Equal(3, result.EventCount);
        Assert.Equal(1, result.StatusCounts["saved"]);
        Assert.Equal(2, result.StatusCounts["unresolved"]);
        Assert.Equal("play-1", Assert.Single(result.SavedPlayIds));
    }

    [Fact]
    public void ParseResult_rejects_missing_or_unsupported_payload()
    {
        var missing = PythonCaptureSaveWorkflowRunner.ParseResult("no json");
        var unsupported = PythonCaptureSaveWorkflowRunner.ParseResult(
            "{\"result_schema_version\":2}");

        Assert.Equal("process_failed", missing.Status);
        Assert.Equal("process_failed", unsupported.Status);
    }
}
