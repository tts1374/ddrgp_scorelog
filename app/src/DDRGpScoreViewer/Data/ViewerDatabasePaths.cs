using System;
using System.IO;
using DDRGpScoreViewer.Models;

namespace DDRGpScoreViewer.Data;

public sealed record ViewerDatabasePaths(
    ViewerDatabaseEnvironment Environment,
    string ApplicationRootDirectory,
    string MasterDatabasePath,
    string JacketCatalogDatabasePath,
    string ScoreDatabasePath,
    string? EvaluationDatabasePath,
    string DataDirectory,
    string LogsDirectory)
{
    public static ViewerDatabasePaths ResolveDefault()
    {
#if DEBUG
        var explicitDevelopmentRoot =
            System.Environment.GetEnvironmentVariable(
                "DDRGP_SCORE_VIEWER_DEVELOPMENT_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitDevelopmentRoot))
        {
            return ForDevelopment(explicitDevelopmentRoot);
        }

        // Debug may be launched from the checkout for developer-only operations.
        // This is an explicit current-directory convention, not a parent search.
        var currentDirectory = Path.GetFullPath(System.Environment.CurrentDirectory);
        if (Directory.Exists(Path.Combine(currentDirectory, "databases")))
        {
            return ForDevelopment(currentDirectory);
        }
#endif

        return ForProduction(
            System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData));
    }

    public static ViewerDatabasePaths ForDevelopment(string developmentRoot)
    {
        var root = FullPath(developmentRoot, nameof(developmentRoot));
        var databaseDirectory = Path.Combine(root, "databases");
        var dataDirectory = Path.Combine(root, "data");
        return new ViewerDatabasePaths(
            ViewerDatabaseEnvironment.Development,
            root,
            Path.Combine(databaseDirectory, "ddrgp-master.sqlite"),
            Path.Combine(databaseDirectory, "jacket-catalog.sqlite"),
            Path.Combine(databaseDirectory, "score.dev.db"),
            Path.Combine(databaseDirectory, "evaluation.db"),
            dataDirectory,
            Path.Combine(root, "logs"));
    }

    public static ViewerDatabasePaths ForProduction(string localApplicationDataRoot)
    {
        var localRoot = FullPath(localApplicationDataRoot, nameof(localApplicationDataRoot));
        var applicationRoot = Path.Combine(localRoot, "DDRGpScoreViewer");
        var dataDirectory = Path.Combine(applicationRoot, "data");
        return new ViewerDatabasePaths(
            ViewerDatabaseEnvironment.Production,
            applicationRoot,
            Path.Combine(dataDirectory, "master", "ddrgp-master.sqlite"),
            Path.Combine(dataDirectory, "master", "jacket-catalog.sqlite"),
            Path.Combine(dataDirectory, "score", "score.db"),
            null,
            dataDirectory,
            Path.Combine(applicationRoot, "logs"));
    }

    public void EnsureDefaultDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        CreateParentDirectory(MasterDatabasePath);
        CreateParentDirectory(JacketCatalogDatabasePath);
        CreateParentDirectory(ScoreDatabasePath);
        if (EvaluationDatabasePath is not null)
        {
            CreateParentDirectory(EvaluationDatabasePath);
        }
    }

    private static void CreateParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Database parent directory could not be determined: {path}");
        }
        Directory.CreateDirectory(directory);
    }

    private static string FullPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A non-empty path is required.", parameterName);
        }
        return Path.GetFullPath(path);
    }
}
