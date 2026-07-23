namespace JacketCatalogCollector.Tests;

public sealed class ReviewWorkflowServiceTests
{
    [Fact]
    public async Task BuildsExplicitMutationRequestAndLoadsStrictReceipt()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(
                0,
                "{\"action_id\":\"a1\",\"reference_id\":\"r1\",\"action\":\"manual_confirm\",\"status\":\"manual_confirmed\",\"song_id\":\"s1\",\"revision\":1,\"idempotent\":false}",
                "")));
        var service = new ReviewWorkflowService(runner, Directory.GetCurrentDirectory());

        var receipt = await service.ApplyAsync(
            "master.sqlite",
            "catalog.sqlite",
            new ReviewMutation(
                "a1", "r1", "manual_confirm", 0, "needs_review", null, "s1", "reason", "note"),
            CancellationToken.None);

        Assert.Equal(("manual_confirmed", "s1", 1), (receipt.Status, receipt.SongId, receipt.Revision));
        Assert.Contains("--expected-revision", runner.Requests[0].Arguments);
        Assert.Contains("--song-id", runner.Requests[0].Arguments);
        Assert.DoesNotContain("--expected-song-id", runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task MutationFailureDoesNotParseStdoutAsSuccess()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(2, "{}", "stale review state")));
        var service = new ReviewWorkflowService(runner, Directory.GetCurrentDirectory());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(
                "master.sqlite",
                "catalog.sqlite",
                new ReviewMutation("a", "r", "reject", 0, "needs_review", null, null, "", ""),
                CancellationToken.None));
        Assert.Contains("stale review state", exception.Message);
    }

    [Fact]
    public async Task BuildsStrictBatchRequestFileAndLoadsBatchReceipt()
    {
        string? requestJson = null;
        var runner = new StubProcessRunner((request, _) =>
        {
            var requestFileIndex = request.Arguments.ToList().IndexOf("--request-file");
            Assert.True(requestFileIndex >= 0);
            requestJson = File.ReadAllText(request.Arguments[requestFileIndex + 1]);
            return Task.FromResult(new ProcessResult(
                0,
                "{\"requested_count\":1,\"applied_count\":1,\"no_op_count\":0,\"receipts\":[]}",
                ""));
        });
        var service = new ReviewWorkflowService(runner, Directory.GetCurrentDirectory());

        var receipt = await service.ApplyBatchAsync(
            "master.sqlite",
            "catalog.sqlite",
            [
                new ReviewMutation(
                    "a1",
                    "r1",
                    "reassign",
                    2,
                    "manual_confirmed",
                    "s1",
                    "s2",
                    "reason",
                    "note",
                    "current note"),
            ],
            CancellationToken.None);

        Assert.Equal((1, 1, 0),
            (receipt.RequestedCount, receipt.AppliedCount, receipt.NoOpCount));
        Assert.NotNull(requestJson);
        Assert.Contains("\"expected_note\":\"current note\"", requestJson,
            StringComparison.Ordinal);
        Assert.Contains("review-batch", runner.Requests[0].Arguments);
        Assert.DoesNotContain("--action-id", runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task ClassifiesInvalidReceiptAfterSuccessfulBatchAsPostCommitFailure()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(0, "not-json", "")));
        var service = new ReviewWorkflowService(runner, Directory.GetCurrentDirectory());

        var exception = await Assert.ThrowsAsync<ReviewBatchPostCommitException>(
            () => service.ApplyBatchAsync(
                "master.sqlite",
                "catalog.sqlite",
                [
                    new ReviewMutation(
                        "a1", "r1", "reject", 0, "needs_review", null, null, "reason", "note"),
                ],
                CancellationToken.None));

        Assert.Contains("committed", exception.Message, StringComparison.Ordinal);
        Assert.IsType<System.Text.Json.JsonException>(exception.InnerException);
    }
}
