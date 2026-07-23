namespace JacketCatalogCollector.Tests;

public sealed class CatalogInitializationServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"ddrgp-catalog-initialization-tests-{Guid.NewGuid():N}");

    public CatalogInitializationServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task ExistingCatalogIsNotRecreatedOrOverwritten()
    {
        var paths = CollectorDatabasePaths.FromRepositoryRoot(root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.CatalogPath)!);
        File.WriteAllText(paths.CatalogPath, "existing-catalog");
        var runner = new StubProcessRunner((_, _) =>
            throw new Xunit.Sdk.XunitException("existing catalog must not invoke create"));
        var service = new CatalogInitializationService(
            runner,
            paths.RepositoryRoot,
            paths.CatalogPath);

        await service.EnsureCreatedAsync(CancellationToken.None);

        Assert.Equal("existing-catalog", File.ReadAllText(paths.CatalogPath));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task FailedCreationRemovesPartialCatalog()
    {
        var paths = CollectorDatabasePaths.FromRepositoryRoot(root);
        var runner = new StubProcessRunner((request, _) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.Arguments[^1])!);
            File.WriteAllBytes(request.Arguments[^1], [1, 2, 3]);
            return Task.FromResult(new ProcessResult(1, "", "schema creation failed"));
        });
        var service = new CatalogInitializationService(
            runner,
            paths.RepositoryRoot,
            paths.CatalogPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureCreatedAsync(CancellationToken.None));

        Assert.Contains("schema creation failed", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(paths.CatalogPath));
    }

    [Fact]
    public async Task SuccessfulProcessMustProduceNonEmptyCatalog()
    {
        var paths = CollectorDatabasePaths.FromRepositoryRoot(root);
        var runner = new StubProcessRunner((_, _) =>
            Task.FromResult(new ProcessResult(0, "created", "")));
        var service = new CatalogInitializationService(
            runner,
            paths.RepositoryRoot,
            paths.CatalogPath);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureCreatedAsync(CancellationToken.None));

        Assert.False(File.Exists(paths.CatalogPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
