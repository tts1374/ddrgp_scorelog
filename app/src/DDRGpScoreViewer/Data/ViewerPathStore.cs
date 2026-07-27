using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDRGpScoreViewer.Models;

namespace DDRGpScoreViewer.Data;

public interface IViewerPathStore
{
    ViewerPathSelection? Load();

    void Save(ViewerPathSelection selection);
}

public sealed class LocalViewerPathStore : IViewerPathStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly string filePath;

    public LocalViewerPathStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DDRGpScoreViewer",
            "viewer-paths.json"))
    {
    }

    public LocalViewerPathStore(string filePath)
    {
        this.filePath = Path.GetFullPath(filePath);
    }

    public ViewerPathSelection? Load()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = File.ReadAllText(filePath, Utf8NoBom);
        var selection = JsonSerializer.Deserialize<ViewerPathSelection>(json, JsonOptions);
        if (selection is null ||
            string.IsNullOrWhiteSpace(selection.ScoreDatabasePath) ||
            string.IsNullOrWhiteSpace(selection.MasterDatabasePath) ||
            string.IsNullOrWhiteSpace(selection.CatalogDatabasePath) ||
            selection.Environment == ViewerDatabaseEnvironment.Unknown)
        {
            return null;
        }

        return new ViewerPathSelection(
            Path.GetFullPath(selection.ScoreDatabasePath),
            Path.GetFullPath(selection.MasterDatabasePath),
            Path.GetFullPath(selection.CatalogDatabasePath),
            selection.Environment);
    }

    public void Save(ViewerPathSelection selection)
    {
        if (string.IsNullOrWhiteSpace(selection.ScoreDatabasePath) ||
            string.IsNullOrWhiteSpace(selection.MasterDatabasePath) ||
            string.IsNullOrWhiteSpace(selection.CatalogDatabasePath) ||
            selection.Environment == ViewerDatabaseEnvironment.Unknown)
        {
            throw new ArgumentException(
                "The score database, master databases, and environment are required.",
                nameof(selection));
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Viewer path settings directory could not be determined.");
        }

        Directory.CreateDirectory(directory);
        var normalized = new ViewerPathSelection(
            Path.GetFullPath(selection.ScoreDatabasePath),
            Path.GetFullPath(selection.MasterDatabasePath),
            Path.GetFullPath(selection.CatalogDatabasePath),
            selection.Environment);
        var json = JsonSerializer.Serialize(normalized, JsonOptions) + "\n";
        File.WriteAllText(filePath, json, Utf8NoBom);
    }
}
