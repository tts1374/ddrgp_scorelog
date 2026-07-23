namespace JacketCatalogCollector.Tests;

public sealed class CollectionAutoConfirmationServiceTests
{
    [Fact]
    public async Task RunsTheCollectionEndCommandAndParsesTheStrictReceipt()
    {
        var runner = new StubProcessRunner((request, _) =>
        {
            Assert.Equal(
                [
                    "-X", "utf8", "-m", "tools.vision_poc.collection_auto_confirmation",
                    "--catalog", Path.GetFullPath("catalog.sqlite"),
                    "--master-db", Path.GetFullPath("master.sqlite"),
                    "--artifact-root", Path.GetFullPath("artifacts"),
                    "--session-id", "session-1",
                ],
                request.Arguments);
            return Task.FromResult(new ProcessResult(
                0,
                "{\"collection_auto_confirmation_schema_version\":\"m5c-collection-end-auto-confirmation-v1\",\"session_id\":\"session-1\",\"requested_count\":2,\"applied_count\":2,\"no_op_count\":0,\"auto_confirmed_count\":2,\"remaining_count\":1}",
                ""));
        });
        var service = new PythonCollectionAutoConfirmationService(
            runner,
            Directory.GetCurrentDirectory(),
            "artifacts",
            "master.sqlite",
            "catalog.sqlite");

        var receipt = await service.ApplyAsync("session-1");

        Assert.Equal(2, receipt.AutoConfirmedCount);
        Assert.Equal(1, receipt.RemainingCount);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task RejectsAReceiptForAnotherSession()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(new ProcessResult(
            0,
            "{\"collection_auto_confirmation_schema_version\":\"m5c-collection-end-auto-confirmation-v1\",\"session_id\":\"other\",\"requested_count\":0,\"applied_count\":0,\"no_op_count\":0,\"auto_confirmed_count\":0,\"remaining_count\":0}",
            "")));
        var service = new PythonCollectionAutoConfirmationService(
            runner,
            Directory.GetCurrentDirectory(),
            "artifacts",
            "master.sqlite",
            "catalog.sqlite");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync("session-1"));

        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }
}
