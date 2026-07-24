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
    public async Task ExportsManualReviewXlsxThroughCurrentProjectionCommand()
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

        await service.ExportManualReviewXlsxAsync(
            "master.sqlite",
            "catalog.sqlite",
            "exports/manual-review.xlsx",
            CancellationToken.None);

        var arguments = Assert.Single(runner.Requests).Arguments;
        Assert.Contains("--artifact-root", arguments);
        Assert.Contains(Path.GetFullPath("artifacts"), arguments);
        Assert.Contains("--manual-xlsx-output", arguments);
        Assert.Contains(Path.GetFullPath("exports/manual-review.xlsx"), arguments);
    }

    [Fact]
    public async Task ImportsManualReviewXlsxThroughReadOnlyProjectionCommand()
    {
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(
                0,
                "{\"metadata\":{\"schema_version\":\"m5c-manual-review-xlsx-v1\"},"
                + "\"drafts\":[{\"observation_id\":\"observation-1\","
                + "\"status\":\"hold\",\"truth_song_id\":null,\"notes\":\"keep\"}]}",
                "")));
        var service = new ProjectionService(
            runner,
            new ProjectionJsonLoader(),
            Directory.GetCurrentDirectory(),
            artifactRoot: "artifacts");

        var result = await service.ImportManualReviewXlsxAsync(
            "master.sqlite",
            "catalog.sqlite",
            "imports/manual-review.xlsx",
            CancellationToken.None);

        Assert.Equal("m5c-manual-review-xlsx-v1", result.Metadata["schema_version"]);
        Assert.Equal("observation-1", Assert.Single(result.Drafts).ObservationId);
        var arguments = Assert.Single(runner.Requests).Arguments;
        Assert.Contains("--manual-xlsx-input", arguments);
        Assert.Contains(Path.GetFullPath("imports/manual-review.xlsx"), arguments);
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
