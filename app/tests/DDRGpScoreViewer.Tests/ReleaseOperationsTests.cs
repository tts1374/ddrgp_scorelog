using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Diagnostics;
using DDRGpScoreViewer.Tray;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ReleaseOperationsTests
{
    [Fact]
    public void Reference_data_set_installs_updates_noops_rejects_downgrade_and_keeps_one_previous_set()
    {
        using var fixture = new DatabaseFixture();
        var localAppData = Path.Combine(fixture.DirectoryPath, "local-app-data");
        var paths = ViewerDatabasePaths.ForProduction(localAppData);
        var manager = new ReferenceDataSetManager();
        var package100 = CreatePackage(fixture, "1.0.0");
        var package110 = CreatePackage(fixture, "1.1.0");

        var installed = manager.InstallPackageDataSet(package100, paths);
        var unchanged = manager.InstallPackageDataSet(package100, paths);
        var updated = manager.InstallPackageDataSet(package110, paths);
        var rejected = manager.InstallPackageDataSet(package100, paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Installed, installed.Status);
        Assert.Equal(ReferenceDataSetUpdateStatus.Unchanged, unchanged.Status);
        Assert.Equal(ReferenceDataSetUpdateStatus.Updated, updated.Status);
        Assert.Equal(ReferenceDataSetUpdateStatus.DowngradeRejected, rejected.Status);
        var installedManifest = JsonSerializer.Deserialize<ReferenceDataSetManifest>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(paths.MasterDatabasePath)!, ReferenceDataSetManager.ManifestFileName)));
        Assert.Equal("1.1.0", installedManifest!.ContentVersion);
        var previous = Path.Combine(Path.GetDirectoryName(paths.MasterDatabasePath)!, ".previous");
        Assert.Equal(3, Directory.GetFiles(previous).Length);
    }

    [Fact]
    public void Reference_data_set_rolls_back_both_files_when_switch_fails()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var package100 = CreatePackage(fixture, "1.0.0");
        var package110 = CreatePackage(fixture, "1.1.0");
        var baselineManager = new ReferenceDataSetManager();
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            baselineManager.InstallPackageDataSet(package100, paths).Status);
        var masterHash = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));
        var catalogHash = SHA256.HashData(File.ReadAllBytes(paths.JacketCatalogDatabasePath));
        var failingManager = new ReferenceDataSetManager(
            switchCheckpoint: checkpoint =>
            {
                if (checkpoint == "installed-ddrgp-master.sqlite")
                {
                    throw new IOException("fixture switch failure");
                }
            });

        var result = failingManager.InstallPackageDataSet(package110, paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, result.Status);
        Assert.Equal(masterHash, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
        Assert.Equal(catalogHash, SHA256.HashData(File.ReadAllBytes(paths.JacketCatalogDatabasePath)));
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(Path.GetDirectoryName(paths.MasterDatabasePath)!, ReferenceDataSetManager.ManifestFileName)));
    }

    [Fact]
    public void Reference_data_set_rejects_incomplete_candidate_without_changing_current_files()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var valid = CreatePackage(fixture, "1.0.0");
        var invalid = CreatePackage(fixture, "1.1.0");
        var manager = new ReferenceDataSetManager();
        Assert.Equal(ReferenceDataSetUpdateStatus.Installed, manager.InstallPackageDataSet(valid, paths).Status);
        File.Delete(Path.Combine(invalid, "jacket-catalog.sqlite"));
        var masterHash = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));

        var result = manager.InstallPackageDataSet(invalid, paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, result.Status);
        Assert.Equal(masterHash, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
    }

    [Fact]
    public void Reference_data_set_rejects_catalog_bound_to_another_master_version()
    {
        using var fixture = new DatabaseFixture();
        using (var connection = OpenWritable(fixture.CatalogPath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE catalog_metadata SET value = 'older-master' WHERE key = 'master_version';";
            command.ExecuteNonQuery();
        }
        var paths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var package = CreatePackage(fixture, "1.0.0");

        var result = new ReferenceDataSetManager().InstallPackageDataSet(package, paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, result.Status);
        Assert.False(File.Exists(paths.MasterDatabasePath));
        Assert.False(File.Exists(paths.JacketCatalogDatabasePath));
    }

    [Fact]
    public void Explicit_score_migration_keeps_one_backup_and_reopens_current_schema()
    {
        using var fixture = new DatabaseFixture();
        MakeVersionZero(fixture.ScorePath);
        var before = SHA256.HashData(File.ReadAllBytes(fixture.ScorePath));
        var service = new ScoreDatabaseMigrationService([new VersionZeroToOneMigration()]);

        var result = service.MigrateIfSupported(fixture.ScorePath);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Migrated);
        var backupPath = Path.Combine(fixture.DirectoryPath, "migration-backup", "score.db.bak");
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(backupPath)));
        _ = new ScoreViewerRepository().Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(backupPath)!));
    }

    [Fact]
    public void Failed_score_migration_restores_original_database()
    {
        using var fixture = new DatabaseFixture();
        MakeVersionZero(fixture.ScorePath);
        var before = SHA256.HashData(File.ReadAllBytes(fixture.ScorePath));
        var service = new ScoreDatabaseMigrationService([new ThrowingMigration()]);

        var result = service.MigrateIfSupported(fixture.ScorePath);

        Assert.False(result.Succeeded);
        Assert.False(result.Migrated);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));
    }

    [Fact]
    public void Newer_score_schema_is_rejected_without_changes()
    {
        using var fixture = new DatabaseFixture();
        SetUserVersion(fixture.ScorePath, 2);
        var before = SHA256.HashData(File.ReadAllBytes(fixture.ScorePath));

        var result = new ScoreDatabaseMigrationService().MigrateIfSupported(fixture.ScorePath);

        Assert.False(result.Succeeded);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));
    }

    [Fact]
    public void Release_log_rotates_to_configured_file_count()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"release-log-{Guid.NewGuid():N}");
        try
        {
            using var log = new ReleaseLog(directory, maximumBytes: 80, fileCount: 3);
            for (var index = 0; index < 20; index++)
            {
                log.Information("fixture", new string('x', 40));
            }
            Assert.InRange(Directory.GetFiles(directory, "gp-score-log*.log").Length, 2, 3);
            Assert.All(Directory.GetFiles(directory), path => Assert.True(new FileInfo(path).Length > 0));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Second_instance_signals_primary_instance()
    {
        var name = $"Local\\ddrgp-score-viewer-test-{Guid.NewGuid():N}";
        using var primary = SingleInstanceCoordinator.Acquire(name);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Listen(() => activated.TrySetResult());

        using var secondary = SingleInstanceCoordinator.Acquire(name);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static string CreatePackage(DatabaseFixture fixture, string contentVersion)
    {
        var directory = Path.Combine(fixture.DirectoryPath, $"package-{contentVersion.Replace('.', '-')}");
        Directory.CreateDirectory(directory);
        File.Copy(fixture.MasterPath, Path.Combine(directory, "ddrgp-master.sqlite"));
        File.Copy(fixture.CatalogPath, Path.Combine(directory, "jacket-catalog.sqlite"));
        var masterPath = Path.Combine(directory, "ddrgp-master.sqlite");
        var catalogPath = Path.Combine(directory, "jacket-catalog.sqlite");
        var manifest = new ReferenceDataSetManifest(
            contentVersion,
            1,
            1,
            "master-v1",
            "master-v1",
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(masterPath))),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(catalogPath))));
        File.WriteAllText(
            Path.Combine(directory, ReferenceDataSetManager.ManifestFileName),
            JsonSerializer.Serialize(manifest) + "\n",
            new UTF8Encoding(false));
        return directory;
    }

    private static void MakeVersionZero(string path)
    {
        using var connection = OpenWritable(path);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "PRAGMA user_version = 0; " +
            "UPDATE score_db_metadata SET value = '0' WHERE key = 'schema_version'; " +
            "DELETE FROM schema_migrations;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void SetUserVersion(string path, int version)
    {
        using var connection = OpenWritable(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenWritable(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private sealed class VersionZeroToOneMigration : IScoreDatabaseMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 1;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "PRAGMA user_version = 1; " +
                "UPDATE score_db_metadata SET value = '1' WHERE key = 'schema_version'; " +
                "INSERT INTO schema_migrations (migration_id, schema_version, app_version, notes) " +
                "VALUES ('001_initial_personal_score_db_schema', 1, 'migration-test', 'explicit fixture converter');";
            command.ExecuteNonQuery();
        }
    }

    private sealed class ThrowingMigration : IScoreDatabaseMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 1;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "PRAGMA user_version = 1;";
            command.ExecuteNonQuery();
            throw new InvalidOperationException("fixture migration failure");
        }
    }
}
