using System.Net;
using System.Net.Http;
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
        var previous = Path.Combine(paths.DataDirectory, ReferenceDataSetManager.PreviousDirectoryName);
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
                if (checkpoint == "installed-reference-data-set")
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
    public void Reference_data_set_keeps_current_set_when_switch_fails_before_backup()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var current = CreatePackage(fixture, "1.0.0");
        var candidate = CreatePackage(fixture, "1.1.0");
        var baselineManager = new ReferenceDataSetManager();
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            baselineManager.InstallPackageDataSet(current, paths).Status);
        var masterHash = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));
        var previousDirectory = Path.Combine(paths.DataDirectory, ReferenceDataSetManager.PreviousDirectoryName);
        Directory.CreateDirectory(previousDirectory);
        File.WriteAllText(Path.Combine(previousDirectory, "sentinel.txt"), "previous");

        var failingManager = new ReferenceDataSetManager(
            switchCheckpoint: checkpoint =>
            {
                if (checkpoint == "before-current-backup")
                {
                    throw new IOException("fixture switch failure before backup");
                }
            });

        var result = failingManager.InstallPackageDataSet(candidate, paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, result.Status);
        Assert.Equal(masterHash, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
        Assert.Contains("1.0.0", File.ReadAllText(Path.Combine(Path.GetDirectoryName(paths.MasterDatabasePath)!, ReferenceDataSetManager.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(previousDirectory, "sentinel.txt")));
    }

    [Fact]
    public void Reference_data_set_rolls_back_when_post_switch_reopen_fails()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var current = CreatePackage(fixture, "1.0.0");
        var candidate = CreatePackage(fixture, "1.1.0");
        var baselineManager = new ReferenceDataSetManager();
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            baselineManager.InstallPackageDataSet(current, paths).Status);
        var masterHash = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));
        var catalogHash = SHA256.HashData(File.ReadAllBytes(paths.JacketCatalogDatabasePath));
        var failingManager = new ReferenceDataSetManager(
            switchCheckpoint: checkpoint =>
            {
                if (checkpoint == "installed-reference-data-set")
                {
                    File.Delete(paths.MasterDatabasePath);
                }
            });

        var result = failingManager.InstallPackageDataSet(candidate, paths);

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
    public async Task GitHub_release_update_downloads_one_set_and_preserves_score_and_settings()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "local-app-data"));
        paths.EnsureDefaultDirectories();
        var package100 = CreatePackage(fixture, "1.0.0");
        var package110 = CreatePackage(fixture, "1.1.0");
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            new ReferenceDataSetManager().InstallPackageDataSet(package100, paths).Status);
        File.Copy(fixture.ScorePath, paths.ScoreDatabasePath, overwrite: true);
        File.WriteAllText(paths.SettingsPath, "{\"environment\":\"production\"}\n", new UTF8Encoding(false));
        var scoreBefore = SHA256.HashData(File.ReadAllBytes(paths.ScoreDatabasePath));
        var settingsBefore = SHA256.HashData(File.ReadAllBytes(paths.SettingsPath));

        var release = CreateReleaseClient(package110);
        var result = await new ReferenceDataSetManager(httpClient: release.Client)
            .UpdateFromGitHubReleaseAsync(paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Updated, result.Status);
        Assert.Contains("GitHub Releases", result.Message);
        Assert.Equal("1.1.0", ReadInstalledManifest(paths).ContentVersion);
        Assert.Equal(scoreBefore, SHA256.HashData(File.ReadAllBytes(paths.ScoreDatabasePath)));
        Assert.Equal(settingsBefore, SHA256.HashData(File.ReadAllBytes(paths.SettingsPath)));

        var unchanged = await new ReferenceDataSetManager(httpClient: release.Client)
            .UpdateFromGitHubReleaseAsync(paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Unchanged, unchanged.Status);
        Assert.Equal(2, release.Handler.ApiRequestCount);
        Assert.Equal(4, release.Handler.AssetRequestCount);

        var olderRelease = CreateReleaseClient(package100);
        var downgrade = await new ReferenceDataSetManager(httpClient: olderRelease.Client)
            .UpdateFromGitHubReleaseAsync(paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.DowngradeRejected, downgrade.Status);
        Assert.Equal("1.1.0", ReadInstalledManifest(paths).ContentVersion);
        Assert.Equal(scoreBefore, SHA256.HashData(File.ReadAllBytes(paths.ScoreDatabasePath)));
        Assert.Equal(settingsBefore, SHA256.HashData(File.ReadAllBytes(paths.SettingsPath)));
    }

    [Theory]
    [InlineData("ddrgp-master.sqlite")]
    [InlineData("jacket-catalog.sqlite")]
    public async Task GitHub_release_missing_one_database_keeps_current_set(string missingAsset)
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var current = CreatePackage(fixture, "1.0.0");
        var candidate = CreatePackage(fixture, "1.1.0");
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            new ReferenceDataSetManager().InstallPackageDataSet(current, paths).Status);
        var masterBefore = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));
        var catalogBefore = SHA256.HashData(File.ReadAllBytes(paths.JacketCatalogDatabasePath));

        var release = CreateReleaseClient(candidate, omittedAsset: missingAsset);
        var result = await new ReferenceDataSetManager(httpClient: release.Client)
            .UpdateFromGitHubReleaseAsync(paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, result.Status);
        Assert.Equal(masterBefore, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
        Assert.Equal(catalogBefore, SHA256.HashData(File.ReadAllBytes(paths.JacketCatalogDatabasePath)));
        Assert.Equal(0, release.Handler.AssetRequestCount);
    }

    [Fact]
    public async Task GitHub_release_checksum_mismatch_keeps_current_set()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var current = CreatePackage(fixture, "1.0.0");
        var candidate = CreatePackage(fixture, "1.1.0");
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            new ReferenceDataSetManager().InstallPackageDataSet(current, paths).Status);
        var masterBefore = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));

        var release = CreateReleaseClient(candidate, corruptedAsset: "ddrgp-master.sqlite");
        var result = await new ReferenceDataSetManager(httpClient: release.Client)
            .UpdateFromGitHubReleaseAsync(paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, result.Status);
        Assert.Equal(masterBefore, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
        Assert.Equal("1.0.0", ReadInstalledManifest(paths).ContentVersion);
    }

    [Fact]
    public async Task GitHub_release_schema_and_compatibility_mismatch_keep_current_set()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var current = CreatePackage(fixture, "1.0.0");
        var candidate = CreatePackage(fixture, "1.1.0");
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            new ReferenceDataSetManager().InstallPackageDataSet(current, paths).Status);
        var currentHash = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));

        var schemaMismatch = CreateReleaseClient(
            candidate,
            manifestBytes: CreateManifestBytes(candidate, masterSchemaVersion: 2));
        var schemaResult = await new ReferenceDataSetManager(httpClient: schemaMismatch.Client)
            .UpdateFromGitHubReleaseAsync(paths);
        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, schemaResult.Status);

        var compatibilityMismatch = CreateReleaseClient(
            candidate,
            manifestBytes: CreateManifestBytes(
                candidate,
                catalogMasterContentVersion: "older-master"));
        var compatibilityResult = await new ReferenceDataSetManager(httpClient: compatibilityMismatch.Client)
            .UpdateFromGitHubReleaseAsync(paths);
        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, compatibilityResult.Status);

        var contentMismatch = CreateReleaseClient(
            candidate,
            manifestBytes: CreateManifestBytes(
                candidate,
                masterContentVersion: "different-master",
                catalogMasterContentVersion: "different-master"));
        var contentResult = await new ReferenceDataSetManager(httpClient: contentMismatch.Client)
            .UpdateFromGitHubReleaseAsync(paths);
        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, contentResult.Status);

        Assert.Equal(currentHash, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
        Assert.Equal("1.0.0", ReadInstalledManifest(paths).ContentVersion);
    }

    [Fact]
    public async Task GitHub_release_network_failure_preserves_reference_score_and_settings()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var current = CreatePackage(fixture, "1.0.0");
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            new ReferenceDataSetManager().InstallPackageDataSet(current, paths).Status);
        paths.EnsureDefaultDirectories();
        File.Copy(fixture.ScorePath, paths.ScoreDatabasePath, overwrite: true);
        File.WriteAllText(paths.SettingsPath, "settings-before-network-failure\n", new UTF8Encoding(false));
        var referenceBefore = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));
        var scoreBefore = SHA256.HashData(File.ReadAllBytes(paths.ScoreDatabasePath));
        var settingsBefore = SHA256.HashData(File.ReadAllBytes(paths.SettingsPath));

        var result = await new ReferenceDataSetManager(
                httpClient: new HttpClient(new ThrowingHttpMessageHandler()))
            .UpdateFromGitHubReleaseAsync(paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, result.Status);
        Assert.Equal(referenceBefore, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
        Assert.Equal(scoreBefore, SHA256.HashData(File.ReadAllBytes(paths.ScoreDatabasePath)));
        Assert.Equal(settingsBefore, SHA256.HashData(File.ReadAllBytes(paths.SettingsPath)));
        _ = new ScoreViewerRepository().Load(
            paths.ScoreDatabasePath,
            paths.MasterDatabasePath,
            paths.JacketCatalogDatabasePath);
    }

    [Fact]
    public async Task GitHub_release_download_interruption_and_disk_shortage_keep_current_set()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var current = CreatePackage(fixture, "1.0.0");
        var candidate = CreatePackage(fixture, "1.1.0");
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            new ReferenceDataSetManager().InstallPackageDataSet(current, paths).Status);
        var referenceBefore = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));

        var interrupted = CreateReleaseClient(candidate, interruptedAsset: "ddrgp-master.sqlite");
        var interruptedResult = await new ReferenceDataSetManager(httpClient: interrupted.Client)
            .UpdateFromGitHubReleaseAsync(paths);
        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, interruptedResult.Status);
        Assert.Equal(referenceBefore, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));

        var shortage = CreateReleaseClient(candidate);
        var shortageResult = await new ReferenceDataSetManager(
                httpClient: shortage.Client,
                availableFreeSpace: _ => 0)
            .UpdateFromGitHubReleaseAsync(paths);
        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, shortageResult.Status);
        Assert.Equal(referenceBefore, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
    }

    [Fact]
    public async Task GitHub_release_body_stall_times_out_and_keeps_current_set()
    {
        using var fixture = new DatabaseFixture();
        var paths = ViewerDatabasePaths.ForProduction(
            Path.Combine(fixture.DirectoryPath, "local-app-data"));
        var current = CreatePackage(fixture, "1.0.0");
        var candidate = CreatePackage(fixture, "1.1.0");
        Assert.Equal(
            ReferenceDataSetUpdateStatus.Installed,
            new ReferenceDataSetManager().InstallPackageDataSet(current, paths).Status);
        var referenceBefore = SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath));

        var stalled = CreateReleaseClient(candidate, stalledAsset: "ddrgp-master.sqlite");
        var result = await new ReferenceDataSetManager(
                httpClient: stalled.Client,
                referenceUpdateTimeout: TimeSpan.FromMilliseconds(100))
            .UpdateFromGitHubReleaseAsync(paths);

        Assert.Equal(ReferenceDataSetUpdateStatus.Failed, result.Status);
        Assert.Equal(referenceBefore, SHA256.HashData(File.ReadAllBytes(paths.MasterDatabasePath)));
        Assert.Equal("1.0.0", ReadInstalledManifest(paths).ContentVersion);
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

    private static ReferenceDataSetManifest ReadInstalledManifest(ViewerDatabasePaths paths) =>
        JsonSerializer.Deserialize<ReferenceDataSetManifest>(
            File.ReadAllText(Path.Combine(
                Path.GetDirectoryName(paths.MasterDatabasePath)!,
                ReferenceDataSetManager.ManifestFileName)))!;

    private static byte[] CreateManifestBytes(
        string packageDirectory,
        int? masterSchemaVersion = null,
        string? masterContentVersion = null,
        string? catalogMasterContentVersion = null)
    {
        var manifest = JsonSerializer.Deserialize<ReferenceDataSetManifest>(
            File.ReadAllText(Path.Combine(
                packageDirectory,
                ReferenceDataSetManager.ManifestFileName)))!;
        var updated = manifest with
        {
            MasterSchemaVersion = masterSchemaVersion ?? manifest.MasterSchemaVersion,
            MasterContentVersion = masterContentVersion ?? manifest.MasterContentVersion,
            CatalogMasterContentVersion = catalogMasterContentVersion ?? manifest.CatalogMasterContentVersion,
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(updated) + "\n");
    }

    private static (HttpClient Client, ReleaseHttpMessageHandler Handler) CreateReleaseClient(
        string packageDirectory,
        string? omittedAsset = null,
        string? corruptedAsset = null,
        string? interruptedAsset = null,
        byte[]? manifestBytes = null,
        string? stalledAsset = null)
    {
        var handler = new ReleaseHttpMessageHandler(
            packageDirectory,
            omittedAsset,
            corruptedAsset,
            interruptedAsset,
            manifestBytes,
            stalledAsset);
        return (new HttpClient(handler), handler);
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

    private sealed class ReleaseHttpMessageHandler : HttpMessageHandler
    {
        private readonly string packageDirectory;
        private readonly string? omittedAsset;
        private readonly string? corruptedAsset;
        private readonly string? interruptedAsset;
        private readonly byte[]? manifestBytes;
        private readonly string? stalledAsset;

        public ReleaseHttpMessageHandler(
            string packageDirectory,
            string? omittedAsset,
            string? corruptedAsset,
            string? interruptedAsset,
            byte[]? manifestBytes,
            string? stalledAsset)
        {
            this.packageDirectory = packageDirectory;
            this.omittedAsset = omittedAsset;
            this.corruptedAsset = corruptedAsset;
            this.interruptedAsset = interruptedAsset;
            this.manifestBytes = manifestBytes;
            this.stalledAsset = stalledAsset;
        }

        public int ApiRequestCount { get; private set; }
        public int AssetRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri
                ?? throw new InvalidOperationException("fixture request URI is missing");
            if (string.Equals(
                    requestUri.AbsoluteUri,
                    ReferenceDataSetManager.LatestReleaseApiUrl,
                    StringComparison.Ordinal))
            {
                ApiRequestCount++;
                var assets = new[]
                    {
                        ReferenceDataSetManager.ManifestFileName,
                        ReferenceDataSetManager.MasterAssetFileName,
                        ReferenceDataSetManager.CatalogAssetFileName,
                    }
                    .Where(name => !string.Equals(name, omittedAsset, StringComparison.Ordinal))
                    .Select(name => new
                    {
                        name,
                        browser_download_url =
                            $"https://github.com/tts1374/ddrgp_scorelog/releases/download/v-test/{name}",
                    });
                var json = JsonSerializer.Serialize(new { assets });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }

            var assetName = Path.GetFileName(requestUri.AbsolutePath);
            AssetRequestCount++;
            if (string.Equals(assetName, stalledAsset, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new BlockingReadStream()),
                });
            }
            if (string.Equals(assetName, interruptedAsset, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new ThrowingReadStream()),
                });
            }
            if (string.Equals(assetName, ReferenceDataSetManager.ManifestFileName, StringComparison.Ordinal) &&
                manifestBytes is not null)
            {
                return Task.FromResult(BytesResponse(manifestBytes));
            }
            if (string.Equals(assetName, corruptedAsset, StringComparison.Ordinal))
            {
                return Task.FromResult(BytesResponse(Encoding.UTF8.GetBytes("corrupt")));
            }

            var assetPath = Path.Combine(packageDirectory, assetName);
            if (!File.Exists(assetPath))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            return Task.FromResult(BytesResponse(File.ReadAllBytes(assetPath)));
        }

        private static HttpResponseMessage BytesResponse(byte[] bytes)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentLength = bytes.Length;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            };
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("fixture network failure"));
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("fixture download interruption");

        public override int Read(Span<byte> buffer) =>
            throw new IOException("fixture download interruption");

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromException<int>(new IOException("fixture download interruption"));

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("fixture download interruption"));

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override int Read(Span<byte> buffer) =>
            throw new NotSupportedException();

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
