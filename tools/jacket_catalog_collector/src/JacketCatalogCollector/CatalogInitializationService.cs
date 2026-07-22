using System.IO;

namespace JacketCatalogCollector;

public interface ICatalogInitializationService
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken);
}

public sealed class CatalogInitializationService(
    IProcessRunner processRunner,
    string repositoryRoot,
    string catalogPath,
    string pythonExecutable = "python") : ICatalogInitializationService
{
    private readonly string root = Path.GetFullPath(repositoryRoot);
    private readonly string target = Path.GetFullPath(catalogPath);

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(target))
        {
            throw new InvalidOperationException("Catalog target must not be a directory.");
        }
        if (File.Exists(target))
        {
            return;
        }

        try
        {
            var result = await processRunner.RunAsync(
                new ProcessRequest(
                    pythonExecutable,
                    [
                        "-X", "utf8", "-m", "tools.vision_poc.jacket_reference_catalog",
                        "create", "--catalog", target,
                    ],
                    root),
                cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Catalog creation failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
            }
            if (!File.Exists(target) || new FileInfo(target).Length == 0)
            {
                throw new InvalidOperationException(
                    "Catalog creation did not produce a complete database.");
            }
        }
        catch
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            throw;
        }
    }
}
