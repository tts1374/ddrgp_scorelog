namespace JacketCatalogCollector.Tests;

public sealed class ReferenceDeletionServiceTests
{
    [Fact]
    public async Task DeletesOneReferenceWithStatePreconditions()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(
                0,
                "{\"reference_id\":\"ref-1\",\"deleted\":true,"
                + "\"song_id\":\"song-1\",\"review_status\":\"manual_confirmed\","
                + "\"revision\":1}",
                "")));
        var service = new ReferenceDeletionService(
            runner,
            Directory.GetCurrentDirectory());

        var receipt = await service.DeleteAsync(
            "catalog.sqlite",
            "ref-1",
            1,
            "manual_confirmed",
            "song-1",
            CancellationToken.None);

        Assert.Equal("ref-1", receipt.ReferenceId);
        Assert.True(receipt.Deleted);
        var arguments = Assert.Single(runner.Requests).Arguments;
        Assert.Contains("delete-reference", arguments);
        Assert.Contains("--expected-revision", arguments);
        Assert.Contains("1", arguments);
        Assert.Contains("--expected-song-id", arguments);
        Assert.Contains("song-1", arguments);
    }

    [Fact]
    public async Task RejectsProcessFailureWithoutParsingReceipt()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(2, "{}", "stale reference state")));
        var service = new ReferenceDeletionService(
            runner,
            Directory.GetCurrentDirectory());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteAsync(
                "catalog.sqlite",
                "ref-1",
                1,
                "manual_confirmed",
                "song-1",
                CancellationToken.None));

        Assert.Contains("stale reference state", exception.Message);
    }
}
