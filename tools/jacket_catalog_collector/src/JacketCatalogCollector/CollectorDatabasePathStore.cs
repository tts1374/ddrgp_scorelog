using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

public sealed record CollectorDatabasePaths(string MasterPath, string CatalogPath);

public interface ICollectorDatabasePathStore
{
    Task<CollectorDatabasePaths?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(CollectorDatabasePaths paths, CancellationToken cancellationToken);
}

public sealed class JsonCollectorDatabasePathStore(string settingPath) : ICollectorDatabasePathStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static string GetDefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DDRGpScorelog",
        "JacketCatalogCollector",
        "database-paths.v1.json");

    public async Task<CollectorDatabasePaths?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settingPath))
        {
            return null;
        }

        await using var stream = new FileStream(
            settingPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        SettingPayload payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<SettingPayload>(
                    stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("DB path setting is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("DB path setting JSON is invalid.", exception);
        }

        if (payload.SettingSchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported DB path setting schema: {payload.SettingSchemaVersion}");
        }
        return new CollectorDatabasePaths(
            NormalizeAbsolutePath(payload.MasterPath, "master_path"),
            NormalizeAbsolutePath(payload.CatalogPath, "catalog_path"));
    }

    public async Task SaveAsync(
        CollectorDatabasePaths paths,
        CancellationToken cancellationToken)
    {
        var payload = new SettingPayload
        {
            SettingSchemaVersion = CurrentSchemaVersion,
            MasterPath = NormalizeAbsolutePath(paths.MasterPath, "master_path"),
            CatalogPath = NormalizeAbsolutePath(paths.CatalogPath, "catalog_path"),
        };
        var fullSettingPath = Path.GetFullPath(settingPath);
        var parent = Path.GetDirectoryName(fullSettingPath)
            ?? throw new InvalidOperationException("DB path setting has no parent directory.");
        Directory.CreateDirectory(parent);
        var temporaryPath = Path.Combine(
            parent,
            $".{Path.GetFileName(fullSettingPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream, payload, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullSettingPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string NormalizeAbsolutePath(string path, string field)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"DB path setting {field} must be absolute.");
        }
        return Path.GetFullPath(path);
    }

    private sealed class SettingPayload
    {
        public required int SettingSchemaVersion { get; init; }
        public required string MasterPath { get; init; }
        public required string CatalogPath { get; init; }
    }
}
