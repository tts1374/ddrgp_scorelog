namespace DDRGpScoreViewer.Models;

public enum MasterDatabaseStatus
{
    Missing,
    Unreadable,
    Incompatible,
    Compatible,
}

public sealed record MasterDatabaseInspection(
    string Path,
    MasterDatabaseStatus Status,
    string Message,
    string? Version)
{
    public bool IsCompatible => Status == MasterDatabaseStatus.Compatible;

    public static MasterDatabaseInspection Missing(string path, string message) =>
        new(path, MasterDatabaseStatus.Missing, message, null);
}

public sealed record ViewerPathSelection(
    string ScoreDatabasePath,
    string MasterDatabasePath);
