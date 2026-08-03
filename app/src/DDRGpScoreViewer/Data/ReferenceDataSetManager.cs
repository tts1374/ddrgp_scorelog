using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDRGpScoreViewer.Data;

public sealed record ReferenceDataSetManifest(
    [property: JsonPropertyName("content_version")] string ContentVersion,
    [property: JsonPropertyName("master_schema_version")] int MasterSchemaVersion,
    [property: JsonPropertyName("catalog_schema_version")] int CatalogSchemaVersion,
    [property: JsonPropertyName("master_content_version")] string MasterContentVersion,
    [property: JsonPropertyName("catalog_master_content_version")] string CatalogMasterContentVersion,
    [property: JsonPropertyName("master_sha256")] string MasterSha256,
    [property: JsonPropertyName("catalog_sha256")] string CatalogSha256);

public enum ReferenceDataSetUpdateStatus
{
    Installed,
    Updated,
    Unchanged,
    DowngradeRejected,
    Failed,
}

public sealed record ReferenceDataSetUpdateResult(
    ReferenceDataSetUpdateStatus Status,
    string Message);

public sealed class ReferenceDataSetManager
{
    public const string ManifestFileName = "reference-set.json";
    private const string MasterFileName = "ddrgp-master.sqlite";
    private const string CatalogFileName = "jacket-catalog.sqlite";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };
    private readonly ScoreViewerRepository repository;
    private readonly Action<string>? switchCheckpoint;

    public ReferenceDataSetManager(
        ScoreViewerRepository? repository = null,
        Action<string>? switchCheckpoint = null)
    {
        this.repository = repository ?? new ScoreViewerRepository();
        this.switchCheckpoint = switchCheckpoint;
    }

    public ReferenceDataSetUpdateResult InstallPackageDataSet(
        string packageDirectory,
        ViewerDatabasePaths paths)
    {
        if (paths.Environment != Models.ViewerDatabaseEnvironment.Production)
        {
            return new(ReferenceDataSetUpdateStatus.Unchanged, "development環境では組み込みreference data setを配置しません。");
        }

        var destinationDirectory = Path.GetDirectoryName(paths.MasterDatabasePath)!;
        var previousDirectory = Path.Combine(destinationDirectory, ".previous");
        Directory.CreateDirectory(destinationDirectory);
        CleanupStagingDirectories(destinationDirectory);

        ReferenceDataSetManifest candidate;
        try
        {
            candidate = ReadAndValidatePackage(packageDirectory);
        }
        catch (Exception exception) when (exception is IOException or JsonException or ViewerDatabaseException or InvalidOperationException)
        {
            return new(ReferenceDataSetUpdateStatus.Failed, $"組み込みreference data setを検証できません。既存DBは変更していません。{exception.Message}");
        }

        var installedManifestPath = Path.Combine(destinationDirectory, ManifestFileName);
        if (File.Exists(installedManifestPath))
        {
            ReferenceDataSetManifest installed;
            try
            {
                installed = ReadManifest(installedManifestPath);
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
            {
                return new(ReferenceDataSetUpdateStatus.Failed, $"現在のreference data set versionを確認できません。既存DBは変更していません。{exception.Message}");
            }

            var comparison = CompareVersions(candidate.ContentVersion, installed.ContentVersion);
            if (comparison == 0)
            {
                return new(ReferenceDataSetUpdateStatus.Unchanged, $"reference data set {candidate.ContentVersion} は配置済みです。");
            }
            if (comparison < 0)
            {
                return new(ReferenceDataSetUpdateStatus.DowngradeRejected, $"古いreference data set {candidate.ContentVersion} への自動downgradeを拒否しました。");
            }
        }

        var stagingDirectory = Path.Combine(destinationDirectory, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            CopySet(packageDirectory, stagingDirectory);
            _ = ReadAndValidatePackage(stagingDirectory);
            var hadCurrent = CurrentSetExists(destinationDirectory);
            SwitchSet(destinationDirectory, stagingDirectory, previousDirectory);
            _ = ReadAndValidatePackage(destinationDirectory);
            return new(
                hadCurrent ? ReferenceDataSetUpdateStatus.Updated : ReferenceDataSetUpdateStatus.Installed,
                hadCurrent
                    ? $"reference data setを {candidate.ContentVersion} へ更新しました。"
                    : $"reference data set {candidate.ContentVersion} を初回配置しました。");
        }
        catch (Exception exception) when (exception is IOException or JsonException or ViewerDatabaseException or InvalidOperationException)
        {
            try
            {
                RestorePreviousSet(destinationDirectory, previousDirectory);
            }
            catch (Exception rollbackException)
            {
                return new(ReferenceDataSetUpdateStatus.Failed, $"reference data set切替と復元に失敗しました。解析・保存を開始しないでください。切替: {exception.Message} 復元: {rollbackException.Message}");
            }
            return new(ReferenceDataSetUpdateStatus.Failed, $"reference data set更新に失敗したため直前の組み合わせへ戻しました。{exception.Message}");
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private ReferenceDataSetManifest ReadAndValidatePackage(string directory)
    {
        var manifest = ReadManifest(Path.Combine(directory, ManifestFileName));
        if (manifest.MasterSchemaVersion != 1 || manifest.CatalogSchemaVersion != 1)
        {
            throw new InvalidOperationException("対応していないreference DB schema versionです。");
        }
        _ = ParseVersion(manifest.ContentVersion);
        if (string.IsNullOrWhiteSpace(manifest.MasterContentVersion))
        {
            throw new InvalidOperationException("master content versionが空です。");
        }
        if (!string.Equals(
                manifest.CatalogMasterContentVersion,
                manifest.MasterContentVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("catalogとmasterの対応content versionが一致しません。");
        }

        var masterPath = Path.Combine(directory, MasterFileName);
        var catalogPath = Path.Combine(directory, CatalogFileName);
        ValidateHash(masterPath, manifest.MasterSha256, "master DB");
        ValidateHash(catalogPath, manifest.CatalogSha256, "jacket参照catalog");
        var master = repository.InspectMasterDatabase(masterPath);
        var catalog = repository.InspectJacketCatalogDatabase(catalogPath);
        if (!master.IsCompatible || !catalog.IsCompatible)
        {
            throw new ViewerDatabaseException($"master DB: {master.Message} / jacket参照catalog: {catalog.Message}");
        }
        if (!string.Equals(master.Version, manifest.MasterContentVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("manifestとmaster DBのcontent versionが一致しません。");
        }
        return manifest;
    }

    private static void ValidateHash(string path, string expected, string label)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected.Length != 64)
        {
            throw new InvalidOperationException($"{label}のSHA-256が不正です。");
        }
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}のSHA-256がmanifestと一致しません。");
        }
    }

    private void SwitchSet(string destinationDirectory, string stagingDirectory, string previousDirectory)
    {
        if (Directory.Exists(previousDirectory))
        {
            Directory.Delete(previousDirectory, recursive: true);
        }
        Directory.CreateDirectory(previousDirectory);

        foreach (var fileName in SetFileNames())
        {
            var current = Path.Combine(destinationDirectory, fileName);
            if (File.Exists(current))
            {
                File.Move(current, Path.Combine(previousDirectory, fileName));
            }
        }
        switchCheckpoint?.Invoke("current-backed-up");

        foreach (var fileName in SetFileNames())
        {
            File.Move(Path.Combine(stagingDirectory, fileName), Path.Combine(destinationDirectory, fileName));
            switchCheckpoint?.Invoke($"installed-{fileName}");
        }
    }

    private static void RestorePreviousSet(string destinationDirectory, string previousDirectory)
    {
        if (!Directory.Exists(previousDirectory))
        {
            return;
        }
        foreach (var fileName in SetFileNames())
        {
            var current = Path.Combine(destinationDirectory, fileName);
            if (File.Exists(current))
            {
                File.Delete(current);
            }
            var previous = Path.Combine(previousDirectory, fileName);
            if (File.Exists(previous))
            {
                File.Move(previous, current);
            }
        }
    }

    private static ReferenceDataSetManifest ReadManifest(string path)
    {
        var manifest = JsonSerializer.Deserialize<ReferenceDataSetManifest>(File.ReadAllText(path), JsonOptions);
        return manifest ?? throw new InvalidOperationException("reference data set manifestが空です。");
    }

    private static int CompareVersions(string left, string right) => ParseVersion(left).CompareTo(ParseVersion(right));

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out var version) && version.Build >= 0
            ? version
            : throw new InvalidOperationException($"content_versionは3要素以上の数値versionが必要です: {value}");

    private static bool CurrentSetExists(string directory) =>
        SetFileNames().All(name => File.Exists(Path.Combine(directory, name)));

    private static void CopySet(string source, string destination)
    {
        foreach (var fileName in SetFileNames())
        {
            File.Copy(Path.Combine(source, fileName), Path.Combine(destination, fileName), overwrite: false);
        }
    }

    private static string[] SetFileNames() => [MasterFileName, CatalogFileName, ManifestFileName];

    private static void CleanupStagingDirectories(string directory)
    {
        foreach (var path in Directory.EnumerateDirectories(directory, ".staging-*", SearchOption.TopDirectoryOnly))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
