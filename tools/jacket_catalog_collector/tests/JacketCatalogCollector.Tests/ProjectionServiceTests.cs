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
        Assert.Equal(["-X", "utf8", "-m"], runner.Requests[0].Arguments.Take(3));
    }

    [Fact]
    public async Task ExportsManualReviewOdsThroughCurrentProjectionCommand()
    {
        var fixture = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "fixtures", "current.json"));
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(0, fixture, "")));
        var service = new ProjectionService(
            runner,
            new ProjectionJsonLoader(),
            Directory.GetCurrentDirectory(),
            artifactRoot: "artifacts");

        await service.ExportManualReviewAsync(
            "master.sqlite",
            "catalog.sqlite",
            "data/manual-review.ods",
            CancellationToken.None);

        var arguments = Assert.Single(runner.Requests).Arguments;
        Assert.Contains("--artifact-root", arguments);
        Assert.Contains(Path.GetFullPath("artifacts"), arguments);
        Assert.Contains("--manual-ods-output", arguments);
        Assert.Contains(Path.GetFullPath("data/manual-review.ods"), arguments);
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
