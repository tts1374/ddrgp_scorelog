using System.Text.Json;

namespace JacketCatalogCollector.Tests;

public sealed class TitleArtistEvaluationServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "ddrgp-title-artist-tests-" + Guid.NewGuid().ToString("N"));

    public TitleArtistEvaluationServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task RunsExplicitEvaluationCommandAndParsesNotAdoptedReceipt()
    {
        var paths = CreatePaths();
        ProcessRequest? captured = null;
        var receiptJson = JsonSerializer.Serialize(new
        {
            receipt_schema_version = TitleArtistEvaluationReceipt.CurrentSchemaVersion,
            report_directory = Path.GetFullPath(paths.Output),
            adopted_methods = Array.Empty<string>(),
            methods = new[]
            {
                new
                {
                    method_version = "tesseract-autocontrast-v1",
                    evaluated_count = 0,
                    known_false_auto_confirm_count = 0,
                    adoption_status = "not_adopted",
                    adoption_failure_reasons = new[] { "insufficient_evaluated_artifacts" },
                },
            },
        });
        var runner = new StubProcessRunner((request, _) =>
        {
            captured = request;
            return Task.FromResult(new ProcessResult(0, receiptJson, ""));
        });
        var service = new TitleArtistEvaluationService(runner, root);

        var receipt = await service.RunAsync(
            paths.Dataset, paths.Artifacts, paths.Master, paths.Catalog, paths.Output);

        Assert.Empty(receipt.AdoptedMethods);
        Assert.Equal("not_adopted", receipt.Methods.Single().AdoptionStatus);
        Assert.NotNull(captured);
        Assert.Equal("python", captured!.FileName);
        Assert.Equal(root, captured.WorkingDirectory);
        Assert.Contains("tools.vision_poc.title_artist_evaluation", captured.Arguments);
        Assert.Contains(paths.Dataset, captured.Arguments);
        Assert.Contains(paths.Artifacts, captured.Arguments);
        Assert.Contains(paths.Output, captured.Arguments);
    }

    [Theory]
    [InlineData(1, "", "evaluation failed")]
    [InlineData(0, "", "empty stdout")]
    [InlineData(0, "{", "invalid JSON")]
    public async Task RejectsProcessAndReceiptFailures(
        int exitCode,
        string stdout,
        string expectedMessage)
    {
        var paths = CreatePaths();
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            new ProcessResult(exitCode, stdout, "evaluation failed")));
        var service = new TitleArtistEvaluationService(runner, root);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(
            paths.Dataset, paths.Artifacts, paths.Master, paths.Catalog, paths.Output));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsUnknownReceiptFieldsAndInconsistentAdoption()
    {
        var paths = CreatePaths();
        var unknown = "{\"receipt_schema_version\":\"m5c-title-artist-evaluation-receipt-v1\","
            + $"\"report_directory\":{JsonSerializer.Serialize(Path.GetFullPath(paths.Output))},"
            + "\"adopted_methods\":[],\"methods\":[],\"unknown\":true}";
        var service = new TitleArtistEvaluationService(
            new StubProcessRunner((_, _) => Task.FromResult(new ProcessResult(0, unknown, ""))),
            root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(
            paths.Dataset, paths.Artifacts, paths.Master, paths.Catalog, paths.Output));

        var inconsistent = JsonSerializer.Serialize(new
        {
            receipt_schema_version = TitleArtistEvaluationReceipt.CurrentSchemaVersion,
            report_directory = Path.GetFullPath(paths.Output),
            adopted_methods = new[] { "method-v1" },
            methods = new[]
            {
                new
                {
                    method_version = "method-v1",
                    evaluated_count = 30,
                    known_false_auto_confirm_count = 0,
                    adoption_status = "not_adopted",
                    adoption_failure_reasons = Array.Empty<string>(),
                },
            },
        });
        service = new TitleArtistEvaluationService(
            new StubProcessRunner((_, _) => Task.FromResult(new ProcessResult(0, inconsistent, ""))),
            root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(
            paths.Dataset, paths.Artifacts, paths.Master, paths.Catalog, paths.Output));
    }

    [Fact]
    public async Task RejectsDatasetOrOutputOutsideRepositoryDataBeforePython()
    {
        var paths = CreatePaths();
        var ran = false;
        var service = new TitleArtistEvaluationService(
            new StubProcessRunner((_, _) =>
            {
                ran = true;
                return Task.FromResult(new ProcessResult(0, "{}", ""));
            }),
            root);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(
            Path.Combine(root, "dataset.json"),
            paths.Artifacts,
            paths.Master,
            paths.Catalog,
            paths.Output));

        Assert.False(ran);
    }

    private (string Dataset, string Artifacts, string Master, string Catalog, string Output)
        CreatePaths()
    {
        var data = Path.Combine(root, "data");
        var artifacts = Path.Combine(data, "jacket_catalog_collector");
        Directory.CreateDirectory(artifacts);
        var dataset = Path.Combine(data, "dataset.json");
        var master = Path.Combine(data, "master.sqlite");
        var catalog = Path.Combine(data, "catalog.sqlite");
        File.WriteAllText(dataset, "{}");
        File.WriteAllText(master, "fixture");
        File.WriteAllText(catalog, "fixture");
        return (dataset, artifacts, master, catalog, Path.Combine(data, "title-artist-report"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
