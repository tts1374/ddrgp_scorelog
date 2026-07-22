using System.IO;

namespace JacketCatalogCollector;

public sealed record CollectorDatabasePaths
{
    private CollectorDatabasePaths(string repositoryRoot)
    {
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    public string RepositoryRoot { get; }

    public string MasterPath => Path.Combine(
        RepositoryRoot,
        "databases",
        "ddrgp-master.sqlite");

    public string CatalogPath => Path.Combine(
        RepositoryRoot,
        "databases",
        "jacket-catalog.sqlite");

    public static CollectorDatabasePaths FromRepositoryRoot(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new InvalidOperationException("Repository root is empty.");
        }
        return new CollectorDatabasePaths(repositoryRoot);
    }

    public static CollectorDatabasePaths Resolve(string? startDirectory = null)
    {
        var start = Path.GetFullPath(startDirectory ?? AppContext.BaseDirectory);
        var directory = new DirectoryInfo(start);
        if (!directory.Exists)
        {
            directory = directory.Parent
                ?? throw new InvalidOperationException(
                    "Repository root cannot be resolved from the application directory.");
        }

        for (var current = directory; current is not null; current = current.Parent)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return FromRepositoryRoot(current.FullName);
            }
        }

        throw new InvalidOperationException(
            $"Repository root cannot be resolved from: {start}");
    }
}
