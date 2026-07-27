using System.IO;
using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Data;

public sealed record ScoreDatabaseInitializationResult(
    bool Succeeded,
    bool Initialized,
    string Message);

public interface IScoreDatabaseInitializer
{
    Task<ScoreDatabaseInitializationResult> InitializeIfMissingAsync(
        string scoreDatabasePath,
        CancellationToken cancellationToken = default);
}

public sealed class PersonalScoreDbInitializer : IScoreDatabaseInitializer
{
    public Task<ScoreDatabaseInitializationResult> InitializeIfMissingAsync(
        string scoreDatabasePath,
        CancellationToken cancellationToken = default)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(scoreDatabasePath);
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(
                FailedResult(scoreDatabasePath, $"score DBのpathを確認できません。{exception.Message}"));
        }

        var preparation = InspectPathForInitialization(fullPath);
        if (!preparation.ShouldInitialize)
        {
            return Task.FromResult(preparation.Result!);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScoreViewerRepository.InitializeEmptyScoreDatabase(fullPath);
            return Task.FromResult(
                new ScoreDatabaseInitializationResult(
                    true,
                    true,
                    "固定score DBを正式schemaへ初期化しました。"));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                SqliteException)
        {
            return Task.FromResult(
                FailedResult(fullPath, $"score DBの初期化処理を実行できません。{exception.Message}"));
        }
    }

    private static InitializationPathInspection InspectPathForInitialization(string path)
    {
        if (Directory.Exists(path))
        {
            return new(
                false,
                FailedResult(path, "score DBのpathがdirectoryです。固定pathにSQLite fileを配置してください."));
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length != 0)
            {
                return new(
                    false,
                    new ScoreDatabaseInitializationResult(
                        true,
                        false,
                        "既存のscore DBは変更せず、後段のread-only互換性検証へ進みます。"));
            }
        }
        catch (FileNotFoundException)
        {
            // The fixed path is missing and may be prepared by the existing boundary.
        }
        catch (DirectoryNotFoundException)
        {
            // The fixed parent directory is prepared before this method is called.
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(false, FailedResult(path, $"score DBをreadできません。{exception.Message}"));
        }
        catch (IOException exception)
        {
            return new(false, FailedResult(path, $"score DBの状態を確認できません。{exception.Message}"));
        }

        return new(true, null);
    }

    private static ScoreDatabaseInitializationResult FailedResult(string path, string reason) =>
        new(false, false, $"{reason} path: {path}");

    private sealed record InitializationPathInspection(
        bool ShouldInitialize,
        ScoreDatabaseInitializationResult? Result);
}
