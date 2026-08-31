using System.Text.Json;

namespace JacketCatalogCollector.Tests;

public sealed class OfficialJacketSnapshotServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "ddrgp-official-snapshot-tests-" + Guid.NewGuid().ToString("N"));

    public OfficialJacketSnapshotServiceTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoaderRequiresCompleteFixedRootAndReturnsCounts()
    {
        var snapshotRoot = Path.Combine(root, "data", "ddrworld_music_snapshot");
        WriteCompleteSnapshot(snapshotRoot, "official-v1", 2, 1);

        var metadata = OfficialJacketSnapshotMetadataLoader.ReadRequired(snapshotRoot);

        Assert.Equal("official-v1", metadata.SnapshotId);
        Assert.Equal(2, metadata.SongCount);
        Assert.Equal(1, metadata.StoredImageCount);
        Assert.Equal(Path.GetFullPath(snapshotRoot), metadata.RootPath);
        Assert.NotNull(OfficialJacketSnapshotMetadataLoader.TryLoad(snapshotRoot));
    }

    [Fact]
    public void LoaderTreatsIncompleteRootAsUnavailable()
    {
        var snapshotRoot = Path.Combine(root, "data", "ddrworld_music_snapshot");
        Directory.CreateDirectory(snapshotRoot);
        File.WriteAllText(
            Path.Combine(snapshotRoot, "manifest.json"),
            "{\"status\":\"incomplete\"}");

        Assert.Null(OfficialJacketSnapshotMetadataLoader.TryLoad(snapshotRoot));
    }

    [Fact]
    public void LoaderAcceptsLegacyRecordCountForSharedHashPath()
    {
        var snapshotRoot = Path.Combine(root, "data", "ddrworld_music_snapshot");
        WriteCompleteSnapshot(
            snapshotRoot,
            "official-v1",
            songCount: 2,
            storedImageCount: 1,
            imageRecordCount: 2,
            summaryStoredImageCount: 2);

        var metadata = OfficialJacketSnapshotMetadataLoader.ReadRequired(snapshotRoot);

        Assert.Equal(1, metadata.StoredImageCount);
    }

    [Fact]
    public async Task AdapterUsesFixedOutputAndForwardsPageAndJacketProgress()
    {
        var snapshotRoot = Path.Combine(root, "data", "ddrworld_music_snapshot");
        WriteCompleteSnapshot(snapshotRoot, "official-v2", 2, 1);
        var runner = new StreamingRunner(
            "{\"event\":\"progress\",\"phase\":\"pages\",\"completed\":0,\"total\":null}",
            "{\"event\":\"progress\",\"phase\":\"pages\",\"completed\":27,\"total\":27}",
            "{\"event\":\"progress\",\"phase\":\"jackets\",\"completed\":463,\"total\":1287}");
        var service = new PythonOfficialJacketSnapshotService(
            runner,
            root,
            Path.GetRelativePath(root, snapshotRoot),
            pythonExecutable: "python-test");
        var progress = new List<OfficialJacketSnapshotProgress>();

        var result = await service.UpdateAsync(new ImmediateProgress(progress), CancellationToken.None);

        Assert.Equal("official-v2", result.Metadata.SnapshotId);
        Assert.Equal(
            [
                new OfficialJacketSnapshotProgress("pages", 0, null),
                new OfficialJacketSnapshotProgress("pages", 27, 27),
                new OfficialJacketSnapshotProgress("jackets", 463, 1287),
            ],
            progress);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("python-test", request.FileName);
        Assert.Equal(root, request.WorkingDirectory);
        Assert.Contains(snapshotRoot, request.Arguments);
        Assert.Contains("--fixed-output", request.Arguments);
        Assert.Contains("--progress-json", request.Arguments);
        Assert.DoesNotContain("--snapshot-id", request.Arguments);
        Assert.DoesNotContain("--page-count", request.Arguments);
        Assert.DoesNotContain("--delay-seconds", request.Arguments);
    }

    [Fact]
    public async Task AdapterSurfacesPhaseAndSanitizedCollectorReason()
    {
        var snapshotRoot = Path.Combine(root, "data", "ddrworld_music_snapshot");
        var service = new PythonOfficialJacketSnapshotService(
            new FailureRunner(
                new ProcessResult(
                    2,
                    "",
                    $"error: snapshot is incomplete (2 failures); diagnostics retained at {root}")),
            root,
            snapshotRoot);

        var exception = await Assert.ThrowsAsync<OfficialJacketSnapshotUpdateException>(
            () => service.UpdateAsync(null, CancellationToken.None));

        Assert.Equal("ページ取得", exception.Phase);
        Assert.Contains("ページ取得", exception.Message, StringComparison.Ordinal);
        Assert.Contains("snapshot is incomplete (2 failures)", exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(root, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdapterTreatsExitZeroAfterLateCancellationAsPublishedSuccess()
    {
        var snapshotRoot = Path.Combine(root, "data", "ddrworld_music_snapshot");
        WriteCompleteSnapshot(snapshotRoot, "official-v2", 2, 1);
        using var cancellation = new CancellationTokenSource();
        var runner = new StreamingRunner();
        runner.OnRun = cancellation.Cancel;
        var service = new PythonOfficialJacketSnapshotService(
            runner,
            root,
            Path.GetRelativePath(root, snapshotRoot));

        var result = await service.UpdateAsync(null, cancellation.Token);

        Assert.Equal("official-v2", result.Metadata.SnapshotId);
        Assert.Equal(1, result.Metadata.StoredImageCount);
    }

    private static void WriteCompleteSnapshot(
        string snapshotRoot,
        string snapshotId,
        int songCount,
        int storedImageCount,
        int? imageRecordCount = null,
        int? summaryStoredImageCount = null)
    {
        Directory.CreateDirectory(Path.Combine(snapshotRoot, "pages"));
        Directory.CreateDirectory(Path.Combine(snapshotRoot, "jackets"));
        var recordCount = imageRecordCount ?? storedImageCount;
        var images = Enumerable.Range(0, recordCount)
            .Select(index => new
            {
                source_url = $"https://example.test/jacket/{index}",
                local_path = $"jackets/{index % storedImageCount}.png",
                error = (string?)null,
            })
            .ToArray();
        foreach (var localPath in images.Select(image => image.local_path).Distinct())
        {
            File.WriteAllBytes(Path.Combine(snapshotRoot, localPath), [1, 2, 3]);
        }
        File.WriteAllText(
            Path.Combine(snapshotRoot, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                status = "complete",
                snapshot_id = snapshotId,
                completed_at = "2026-07-18T03:04:05Z",
                images,
                failures = Array.Empty<object>(),
            }));
        File.WriteAllText(
            Path.Combine(snapshotRoot, "summary.json"),
            JsonSerializer.Serialize(new
            {
                status = "complete",
                snapshot_id = snapshotId,
                song_count = songCount,
                page_request_count = 0,
                image_request_count = recordCount,
                stored_jacket_count = summaryStoredImageCount ?? storedImageCount,
            }));
        File.WriteAllText(
            Path.Combine(snapshotRoot, "songs.jsonl"),
            string.Concat(Enumerable.Repeat("{}\n", songCount)));
    }

    private sealed class ImmediateProgress(List<OfficialJacketSnapshotProgress> values)
        : IProgress<OfficialJacketSnapshotProgress>
    {
        public void Report(OfficialJacketSnapshotProgress value) => values.Add(value);
    }

    private sealed class StreamingRunner(params string[] lines)
        : IProcessRunner, IStreamingProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Action? OnRun { get; set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<ProcessResult>(
                new InvalidOperationException("streaming runner was not used"));

        public Task<ProcessResult> RunStreamingAsync(
            ProcessRequest request,
            Action<string> standardOutputLine,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            foreach (var line in lines)
            {
                standardOutputLine(line);
            }
            OnRun?.Invoke();
            return Task.FromResult(new ProcessResult(0, string.Join(Environment.NewLine, lines), ""));
        }
    }

    private sealed class FailureRunner(ProcessResult result) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
