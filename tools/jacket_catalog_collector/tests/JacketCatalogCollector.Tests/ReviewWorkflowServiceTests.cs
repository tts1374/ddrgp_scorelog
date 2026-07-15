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
    public async Task MigrationAndMutationFailuresDoNotParseStdoutAsSuccess()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(2, "{}", "stale review state")));
        var service = new ReviewWorkflowService(runner, Directory.GetCurrentDirectory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MigrateAsync("v1.sqlite", "v2.sqlite", CancellationToken.None));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(
                "master.sqlite",
                "catalog.sqlite",
                new ReviewMutation("a", "r", "reject", 0, "needs_review", null, null, "", ""),
                CancellationToken.None));
        Assert.Contains("stale review state", exception.Message);
    }
}
