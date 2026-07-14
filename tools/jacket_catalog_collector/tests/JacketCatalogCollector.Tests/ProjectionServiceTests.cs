namespace JacketCatalogCollector.Tests;

public sealed class ProjectionServiceTests
{
    [Fact]
    public async Task RejectsPythonFailureWithoutParsingPartialStdout()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(2, "{\"projection_schema_version\":1}", "broken catalog")));
        var service = new ProjectionService(runner, new ProjectionJsonLoader(), Directory.GetCurrentDirectory());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LoadAsync("master.sqlite", "catalog.sqlite", CancellationToken.None));

        Assert.Contains("broken catalog", exception.Message);
        Assert.Single(runner.Requests);
    }
}

internal sealed class StubProcessRunner(
    Func<ProcessRequest, CancellationToken, Task<ProcessResult>> implementation) : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return implementation(request, cancellationToken);
    }
}
