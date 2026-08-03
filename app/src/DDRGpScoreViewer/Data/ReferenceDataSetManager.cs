using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
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
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/tts1374/ddrgp_scorelog/releases/latest";
    public const string ManifestFileName = "reference-set.json";
    public const string MasterAssetFileName = "ddrgp-master.sqlite";
    public const string CatalogAssetFileName = "jacket-catalog.sqlite";
    public const string PreviousDirectoryName = ".reference-previous";
    private const string MasterFileName = "ddrgp-master.sqlite";
    private const string CatalogFileName = "jacket-catalog.sqlite";
    private const string StagingDirectoryPrefix = ".reference-staging-";
    private const string GitHubAssetHost = "github.com";
    private static readonly TimeSpan DefaultReferenceUpdateTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };
    private readonly ScoreViewerRepository repository;
    private readonly Action<string>? switchCheckpoint;
    private readonly HttpClient httpClient;
    private readonly Func<string, long> availableFreeSpace;
    private readonly TimeSpan referenceUpdateTimeout;

    public ReferenceDataSetManager(
        ScoreViewerRepository? repository = null,
        Action<string>? switchCheckpoint = null,
        HttpClient? httpClient = null,
        Func<string, long>? availableFreeSpace = null,
        TimeSpan? referenceUpdateTimeout = null)
    {
        this.repository = repository ?? new ScoreViewerRepository();
        this.switchCheckpoint = switchCheckpoint;
        this.httpClient = httpClient ?? CreateHttpClient();
        this.availableFreeSpace = availableFreeSpace ?? GetAvailableFreeSpace;
        this.referenceUpdateTimeout = referenceUpdateTimeout ?? DefaultReferenceUpdateTimeout;
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
        var destinationParent = Directory.GetParent(destinationDirectory)?.FullName
            ?? throw new InvalidOperationException("reference data setの親directoryを解決できません。");
        var previousDirectory = Path.Combine(destinationParent, PreviousDirectoryName);
        Directory.CreateDirectory(destinationDirectory);
        CleanupStagingDirectories(destinationParent);

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

        var stagingDirectory = Path.Combine(
            destinationParent,
            $"{StagingDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        var currentWasBackedUp = false;
        try
        {
            CopySet(packageDirectory, stagingDirectory);
            _ = ReadAndValidatePackage(stagingDirectory);
            var hadCurrent = CurrentSetExists(destinationDirectory);
            currentWasBackedUp = SwitchSet(
                destinationDirectory,
                stagingDirectory,
                previousDirectory);
            _ = ReadAndValidatePackage(destinationDirectory);
            TryDeleteDirectory(Path.Combine(previousDirectory, ".previous"));
            return new(
                hadCurrent ? ReferenceDataSetUpdateStatus.Updated : ReferenceDataSetUpdateStatus.Installed,
                hadCurrent
                    ? $"reference data setを {candidate.ContentVersion} へ更新しました。"
                    : $"reference data set {candidate.ContentVersion} を初回配置しました。");
        }
        catch (Exception exception) when (exception is IOException or JsonException or ViewerDatabaseException or InvalidOperationException)
        {
            if (currentWasBackedUp)
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
            return new(ReferenceDataSetUpdateStatus.Failed, $"reference data set更新に失敗しました。現行DBは変更していません。{exception.Message}");
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    public async Task<ReferenceDataSetUpdateResult> UpdateFromGitHubReleaseAsync(
        ViewerDatabasePaths paths,
        CancellationToken cancellationToken = default)
    {
        if (paths.Environment != Models.ViewerDatabaseEnvironment.Production)
        {
            return new(ReferenceDataSetUpdateStatus.Unchanged, "development環境ではGitHub Releasesからreference data setを取得しません。");
        }

        var downloadDirectory = Path.Combine(
            paths.DataDirectory,
            $".reference-download-{Guid.NewGuid():N}");
        using var timeoutCancellation = new CancellationTokenSource(referenceUpdateTimeout);
        using var updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        var updateToken = updateCancellation.Token;
        try
        {
            Directory.CreateDirectory(downloadDirectory);
            var release = await ReadLatestReleaseAsync(updateToken);
            var assets = ResolveReleaseAssets(release);

            var manifestPath = Path.Combine(downloadDirectory, ManifestFileName);
            await DownloadAssetAsync(
                assets.ManifestUri,
                manifestPath,
                "reference manifest",
                updateToken);
            var manifest = ReadManifest(manifestPath);
            ValidateManifestMetadata(manifest);

            var destinationDirectory = Path.GetDirectoryName(paths.MasterDatabasePath)!;
            var installedManifestPath = Path.Combine(destinationDirectory, ManifestFileName);
            if (File.Exists(installedManifestPath))
            {
                var installed = ReadManifest(installedManifestPath);
                var comparison = CompareVersions(
                    manifest.ContentVersion,
                    installed.ContentVersion);
                if (comparison == 0)
                {
                    return new(
                        ReferenceDataSetUpdateStatus.Unchanged,
                        $"GitHub Releasesのreference data set {manifest.ContentVersion} は配置済みです。DBは取得しませんでした。");
                }
                if (comparison < 0)
                {
                    return new(
                        ReferenceDataSetUpdateStatus.DowngradeRejected,
                        $"GitHub Releasesの古いreference data set {manifest.ContentVersion} への自動downgradeを拒否しました。現行DBは変更していません。");
                }
            }

            await DownloadAssetAsync(
                assets.MasterUri,
                Path.Combine(downloadDirectory, MasterFileName),
                "master DB",
                updateToken);
            await DownloadAssetAsync(
                assets.CatalogUri,
                Path.Combine(downloadDirectory, CatalogFileName),
                "jacket参照catalog",
                updateToken);

            var result = InstallPackageDataSet(downloadDirectory, paths);
            return result with
            {
                Message = $"GitHub Releasesからreference data setを取得しました。{result.Message}",
            };
        }
        catch (OperationCanceledException)
        {
            return new(
                ReferenceDataSetUpdateStatus.Failed,
                "GitHub Releasesからのreference data set取得がタイムアウトまたはキャンセルされました。既存DBは変更していません。オフラインのまま利用できます。");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            return new(
                ReferenceDataSetUpdateStatus.Failed,
                $"GitHub Releasesからreference data setを取得できませんでした。既存DBは変更していません。オフラインのまま利用できます。{exception.Message}");
        }
        finally
        {
            TryDeleteDirectory(downloadDirectory);
        }
    }

    private ReferenceDataSetManifest ReadAndValidatePackage(string directory)
    {
        var manifest = ReadManifest(Path.Combine(directory, ManifestFileName));
        ValidateManifestMetadata(manifest);

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
        if (!string.Equals(
                catalog.MasterContentVersion,
                manifest.CatalogMasterContentVersion,
                StringComparison.Ordinal) ||
            !string.Equals(catalog.MasterContentVersion, master.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("jacket参照catalog metadataとmaster DBのcontent versionが一致しません。");
        }
        return manifest;
    }

    private static void ValidateManifestMetadata(ReferenceDataSetManifest manifest)
    {
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
        ValidateHashFormat(manifest.MasterSha256, "master DB");
        ValidateHashFormat(manifest.CatalogSha256, "jacket参照catalog");
    }

    private static void ValidateHash(string path, string expected, string label)
    {
        ValidateHashFormat(expected, label);
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{label}のSHA-256がmanifestと一致しません。");
        }
    }

    private static void ValidateHashFormat(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 64 ||
            !value.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException($"{label}のSHA-256が不正です。");
        }
    }

    private bool SwitchSet(string destinationDirectory, string stagingDirectory, string previousDirectory)
    {
        var currentWasBackedUp = false;
        try
        {
            switchCheckpoint?.Invoke("before-current-backup");
            if (Directory.Exists(previousDirectory))
            {
                Directory.Delete(previousDirectory, recursive: true);
            }
            if (Directory.Exists(destinationDirectory))
            {
                Directory.Move(destinationDirectory, previousDirectory);
                currentWasBackedUp = true;
            }
            switchCheckpoint?.Invoke("current-backed-up");
            Directory.Move(stagingDirectory, destinationDirectory);
            switchCheckpoint?.Invoke("installed-reference-data-set");
            return currentWasBackedUp;
        }
        catch (Exception exception)
        {
            if (currentWasBackedUp)
            {
                try
                {
                    RestorePreviousSet(destinationDirectory, previousDirectory);
                }
                catch (Exception rollbackException)
                {
                    throw new IOException(
                        $"reference data set切替の復元に失敗しました。切替: {exception.Message} 復元: {rollbackException.Message}",
                        rollbackException);
                }
            }
            throw;
        }
    }

    private static void RestorePreviousSet(string destinationDirectory, string previousDirectory)
    {
        if (!Directory.Exists(previousDirectory))
        {
            return;
        }
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }
        Directory.Move(previousDirectory, destinationDirectory);
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
        foreach (var path in Directory.EnumerateDirectories(
                     directory,
                     $"{StagingDirectoryPrefix}*",
                     SearchOption.TopDirectoryOnly))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private async Task<GitHubReleasePayload> ReadLatestReleaseAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            LatestReleaseApiUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<GitHubReleasePayload>(
            JsonOptions,
            cancellationToken);
        return release ?? throw new InvalidOperationException("GitHub Release metadataが空です。");
    }

    private static ReferenceReleaseAssets ResolveReleaseAssets(GitHubReleasePayload release)
    {
        if (release.Assets is null)
        {
            throw new InvalidOperationException("GitHub Releaseにassetがありません。");
        }
        var assets = release.Assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name))
            .GroupBy(asset => asset.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var manifest = ResolveAsset(assets, ManifestFileName);
        var master = ResolveAsset(assets, MasterFileName);
        var catalog = ResolveAsset(assets, CatalogFileName);
        return new(
            ValidateAssetUri(manifest.BrowserDownloadUrl, ManifestFileName),
            ValidateAssetUri(master.BrowserDownloadUrl, MasterFileName),
            ValidateAssetUri(catalog.BrowserDownloadUrl, CatalogFileName));
    }

    private static GitHubReleaseAsset ResolveAsset(
        IReadOnlyDictionary<string, GitHubReleaseAsset> assets,
        string name) =>
        assets.TryGetValue(name, out var asset)
            ? asset
            : throw new InvalidOperationException($"GitHub Release asset {name} がありません。");

    private static Uri ValidateAssetUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, GitHubAssetHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"GitHub Release asset {name} のdownload URLが不正です。");
        }
        return uri;
    }

    private async Task DownloadAssetAsync(
        Uri uri,
        string destination,
        string label,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is < 0)
        {
            throw new IOException($"{label}のcontent lengthが不正です。");
        }
        if (contentLength is long length &&
            availableFreeSpace(Path.GetDirectoryName(destination)!) < length)
        {
            throw new IOException($"{label}の取得に必要な空き容量がありません。");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "GP-Score-Log-reference-data");
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept",
            "application/vnd.github+json");
        return client;
    }

    private static long GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrWhiteSpace(root)
                ? long.MaxValue
                : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The downloaded candidate is outside the repository and can be retried next time.
        }
    }

    private sealed record GitHubReleasePayload(
        [property: JsonPropertyName("assets")] GitHubReleaseAsset[]? Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

    private sealed record ReferenceReleaseAssets(
        Uri ManifestUri,
        Uri MasterUri,
        Uri CatalogUri);
}
