using System.Text;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ViewerDatabasePathsTests
{
    [Fact]
    public void Development_defaults_keep_the_two_master_files_and_evaluation_db_separate()
    {
        var paths = ViewerDatabasePaths.ForDevelopment("C:\\checkout");

        Assert.Equal(ViewerDatabaseEnvironment.Development, paths.Environment);
        Assert.Equal("C:\\checkout\\databases\\ddrgp-master.sqlite", paths.MasterDatabasePath);
        Assert.Equal("C:\\checkout\\databases\\jacket-catalog.sqlite", paths.JacketCatalogDatabasePath);
        Assert.Equal("C:\\checkout\\databases\\score.dev.db", paths.ScoreDatabasePath);
        Assert.Equal("C:\\checkout\\databases\\evaluation.db", paths.EvaluationDatabasePath);
        Assert.NotEqual(paths.MasterDatabasePath, paths.JacketCatalogDatabasePath);
        Assert.NotEqual(paths.ScoreDatabasePath, paths.EvaluationDatabasePath);
    }

#if DEBUG
    [Fact]
    public void Debug_defaults_detect_a_checkout_from_a_nested_debug_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-debug-root-{Guid.NewGuid():N}");
        var nestedOutput = Path.Combine(
            root,
            "app",
            "src",
            "DDRGpScoreViewer",
            "bin",
            "Debug",
            "net10.0-windows10.0.19041.0");
        var previousCurrentDirectory = Environment.CurrentDirectory;
        var previousDevelopmentRoot = Environment.GetEnvironmentVariable(
            "DDRGP_SCORE_VIEWER_DEVELOPMENT_ROOT");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "databases"));
            Directory.CreateDirectory(nestedOutput);
            File.WriteAllText(
                Path.Combine(root, "app", "src", "DDRGpScoreViewer", "DDRGpScoreViewer.csproj"),
                string.Empty);
            Environment.SetEnvironmentVariable("DDRGP_SCORE_VIEWER_DEVELOPMENT_ROOT", null);
            Directory.SetCurrentDirectory(nestedOutput);

            var paths = ViewerDatabasePaths.ResolveDefault();

            Assert.Equal(ViewerDatabaseEnvironment.Development, paths.Environment);
            Assert.Equal(
                Path.GetFullPath(root),
                paths.ApplicationRootDirectory);
            Assert.Equal(
                Path.Combine(root, "databases", "ddrgp-master.sqlite"),
                paths.MasterDatabasePath);
            Assert.Equal(
                Path.Combine(root, "databases", "jacket-catalog.sqlite"),
                paths.JacketCatalogDatabasePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DDRGP_SCORE_VIEWER_DEVELOPMENT_ROOT",
                previousDevelopmentRoot);
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
#endif

    [Fact]
    public void Production_defaults_use_local_application_data_and_have_no_evaluation_db()
    {
        var paths = ViewerDatabasePaths.ForProduction("C:\\Users\\test\\AppData\\Local");

        Assert.Equal(ViewerDatabaseEnvironment.Production, paths.Environment);
        Assert.Equal(
            "C:\\Users\\test\\AppData\\Local\\DDRGpScoreViewer\\data\\master\\ddrgp-master.sqlite",
            paths.MasterDatabasePath);
        Assert.Equal(
            "C:\\Users\\test\\AppData\\Local\\DDRGpScoreViewer\\data\\master\\jacket-catalog.sqlite",
            paths.JacketCatalogDatabasePath);
        Assert.Equal(
            "C:\\Users\\test\\AppData\\Local\\DDRGpScoreViewer\\data\\score\\score.db",
            paths.ScoreDatabasePath);
        Assert.Null(paths.EvaluationDatabasePath);
    }

    [Fact]
    public void EnsureDefaultDirectories_creates_parents_without_creating_any_database()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-paths-{Guid.NewGuid():N}");
        try
        {
            var paths = ViewerDatabasePaths.ForDevelopment(root);
            paths.EnsureDefaultDirectories();

            Assert.True(Directory.Exists(Path.Combine(root, "databases")));
            Assert.True(Directory.Exists(Path.Combine(root, "data")));
            Assert.True(Directory.Exists(Path.Combine(root, "logs")));
            Assert.False(File.Exists(paths.MasterDatabasePath));
            Assert.False(File.Exists(paths.JacketCatalogDatabasePath));
            Assert.False(File.Exists(paths.ScoreDatabasePath));
            Assert.False(File.Exists(paths.EvaluationDatabasePath!));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void LocalViewerPathStore_round_trips_all_three_paths_and_environment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-path-store-{Guid.NewGuid():N}");
        try
        {
            var settingsPath = Path.Combine(root, "settings", "viewer-paths.json");
            var selection = new ViewerPathSelection(
                Path.Combine(root, "score.dev.db"),
                Path.Combine(root, "ddrgp-master.sqlite"),
                Path.Combine(root, "jacket-catalog.sqlite"),
                ViewerDatabaseEnvironment.Development);
            var store = new LocalViewerPathStore(settingsPath);

            store.Save(selection);
            var restored = store.Load();

            Assert.Equal(selection, restored);
            var bytes = File.ReadAllBytes(settingsPath);
            Assert.False(bytes.Length >= 3 && bytes[..3].SequenceEqual(Encoding.UTF8.GetPreamble()));
            Assert.EndsWith("\n", File.ReadAllText(settingsPath, new UTF8Encoding(false)), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
