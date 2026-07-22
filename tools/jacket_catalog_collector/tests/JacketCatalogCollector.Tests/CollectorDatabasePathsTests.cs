namespace JacketCatalogCollector.Tests;

public sealed class CollectorDatabasePathsTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"ddrgp-fixed-database-path-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesRepositoryRootFromApplicationTreeAndUsesFixedDatabaseNames()
    {
        var repositoryRoot = Path.Combine(root, "repository");
        var nested = Path.Combine(repositoryRoot, "tools", "collector", "bin");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".git"));
        Directory.CreateDirectory(nested);

        var paths = CollectorDatabasePaths.Resolve(nested);

        Assert.Equal(Path.GetFullPath(repositoryRoot), paths.RepositoryRoot);
        Assert.Equal(
            Path.Combine(repositoryRoot, "databases", "ddrgp-master.sqlite"),
            paths.MasterPath);
        Assert.Equal(
            Path.Combine(repositoryRoot, "databases", "jacket-catalog.sqlite"),
            paths.CatalogPath);
    }

    [Fact]
    public void SupportsGitWorktreeFileWithoutUsingCurrentDirectory()
    {
        var repositoryRoot = Path.Combine(root, "worktree");
        var nested = Path.Combine(repositoryRoot, "tools");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(repositoryRoot, ".git"), "gitdir: C:/git/worktree");

        var paths = CollectorDatabasePaths.Resolve(nested);

        Assert.Equal(Path.GetFullPath(repositoryRoot), paths.RepositoryRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
