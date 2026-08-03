using System.IO;
using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Data;

public interface IScoreDatabaseMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Apply(SqliteConnection connection, SqliteTransaction transaction);
}

public sealed record ScoreDatabaseMigrationResult(bool Succeeded, bool Migrated, string Message);

public sealed class ScoreDatabaseMigrationService
{
    private const int SupportedVersion = 1;
    private readonly IReadOnlyDictionary<int, IScoreDatabaseMigration> migrations;

    public ScoreDatabaseMigrationService(IEnumerable<IScoreDatabaseMigration>? migrations = null)
    {
        this.migrations = (migrations ?? []).ToDictionary(item => item.FromVersion);
    }

    public ScoreDatabaseMigrationResult MigrateIfSupported(string scoreDatabasePath)
    {
        if (!File.Exists(scoreDatabasePath) || new FileInfo(scoreDatabasePath).Length == 0)
        {
            return new(true, false, "migration対象の既存score DBはありません。");
        }

        int version;
        try
        {
            using var inspection = Open(scoreDatabasePath, SqliteOpenMode.ReadOnly);
            version = checked((int)ExecuteVersion(inspection));
        }
        catch (Exception exception) when (exception is IOException or SqliteException or OverflowException)
        {
            return new(false, false, $"score DBを安全に検査できないため変更していません。{exception.Message}");
        }

        if (version == SupportedVersion)
        {
            return new(true, false, "score DB schemaは現行versionです。");
        }
        if (version > SupportedVersion)
        {
            return new(false, false, "このアプリより新しいscore DB schemaのため変更せず拒否しました。");
        }
        if (!migrations.TryGetValue(version, out var migration) || migration.ToVersion != SupportedVersion)
        {
            return new(false, false, "対応する明示的converterがないscore DB schemaのため変更せず拒否しました。");
        }

        var backupDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scoreDatabasePath))!, "migration-backup");
        var backupPath = Path.Combine(backupDirectory, "score.db.bak");
        Directory.CreateDirectory(backupDirectory);
        var pendingBackup = backupPath + ".pending";
        File.Copy(scoreDatabasePath, pendingBackup, overwrite: true);

        try
        {
            using (var connection = Open(scoreDatabasePath, SqliteOpenMode.ReadWrite))
            using (var transaction = connection.BeginTransaction())
            {
                migration.Apply(connection, transaction);
                transaction.Commit();
            }
            using (var verification = Open(scoreDatabasePath, SqliteOpenMode.ReadWrite))
            {
                ScoreViewerRepository.ValidateScoreDatabaseForWrite(verification);
                using var transaction = verification.BeginTransaction();
                using var command = verification.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT COUNT(*) FROM plays;";
                _ = command.ExecuteScalar();
                transaction.Rollback();
            }
            File.Move(pendingBackup, backupPath, overwrite: true);
            return new(true, true, $"score DBをschema version {SupportedVersion}へ移行し、最新backupを1件保持しました。");
        }
        catch (Exception exception) when (exception is IOException or SqliteException or ViewerDatabaseException or InvalidOperationException)
        {
            File.Copy(pendingBackup, scoreDatabasePath, overwrite: true);
            File.Delete(pendingBackup);
            return new(false, false, $"score DB migrationに失敗したため直前の内容へ戻しました。{exception.Message}");
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = mode,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long ExecuteVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }
}
