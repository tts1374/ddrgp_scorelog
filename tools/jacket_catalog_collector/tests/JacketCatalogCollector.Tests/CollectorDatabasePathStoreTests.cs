using System.Text;

namespace JacketCatalogCollector.Tests;

public sealed class CollectorDatabasePathStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"ddrgp-path-setting-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task MissingSettingReturnsNullWithoutCreatingDirectory()
    {
        var settingPath = Path.Combine(root, "nested", "database-paths.v1.json");
        var store = new JsonCollectorDatabasePathStore(settingPath);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task RoundTripsOnlyAbsolutePathsWithVersionedJson()
    {
        var settingPath = Path.Combine(root, "database-paths.v1.json");
        var masterPath = Path.Combine(root, "master.sqlite");
        var catalogPath = Path.Combine(root, "catalog.sqlite");
        var store = new JsonCollectorDatabasePathStore(settingPath);

        await store.SaveAsync(
            new CollectorDatabasePaths(masterPath, catalogPath),
            CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        var json = await File.ReadAllTextAsync(settingPath, Encoding.UTF8);

        Assert.Equal(Path.GetFullPath(masterPath), loaded!.MasterPath);
        Assert.Equal(Path.GetFullPath(catalogPath), loaded.CatalogPath);
        Assert.Contains("\"setting_schema_version\": 1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData("{\"setting_schema_version\":2,\"master_path\":\"C:\\\\master.sqlite\",\"catalog_path\":\"C:\\\\catalog.sqlite\"}")]
    [InlineData("{\"setting_schema_version\":1,\"master_path\":\"relative.sqlite\",\"catalog_path\":\"C:\\\\catalog.sqlite\"}")]
    [InlineData("{\"setting_schema_version\":1,\"master_path\":\"C:\\\\master.sqlite\",\"catalog_path\":\"C:\\\\catalog.sqlite\",\"unknown\":true}")]
    public async Task RejectsIncompatibleSettingWithoutChangingItsBytes(string json)
    {
        Directory.CreateDirectory(root);
        var settingPath = Path.Combine(root, "database-paths.v1.json");
        var original = Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(settingPath, original);
        var store = new JsonCollectorDatabasePathStore(settingPath);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.LoadAsync(CancellationToken.None));

        Assert.Equal(original, await File.ReadAllBytesAsync(settingPath));
    }

    [Fact]
    public async Task RejectsRelativePathsBeforeCreatingSettingDirectory()
    {
        var settingPath = Path.Combine(root, "nested", "database-paths.v1.json");
        var store = new JsonCollectorDatabasePathStore(settingPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(
            new CollectorDatabasePaths("master.sqlite", Path.Combine(root, "catalog.sqlite")),
            CancellationToken.None));

        Assert.False(Directory.Exists(root));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
