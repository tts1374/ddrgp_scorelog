using System.IO;

namespace DDRGpScoreViewer;

public static class RepositoryRootLocator
{
    public static string Find()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "pyproject.toml")) &&
                    File.Exists(Path.Combine(
                        directory.FullName,
                        "tools",
                        "vision_poc",
                        "personal_score_db_workflow_app.py")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException(
            "Repository root was not found. Start the application from the repository checkout.");
    }
}
