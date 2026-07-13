using System.IO;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class CaptureTests
{
    private static readonly byte[] PngFixture = [137, 80, 78, 71, 13, 10, 26, 10, 0];

    [Fact]
    public async Task Atomic_writer_creates_manifest_compatible_bundle_without_overwrite()
    {
        using var fixture = new CaptureDirectoryFixture();
        var writer = new AtomicCaptureOutputWriter(fixture.OutputRoot, fixture.DataRoot);
        var frame = Frame("DDR GRAND PRIX, result");

        var first = await writer.WriteAsync(frame);
        var second = await writer.WriteAsync(frame);

        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        Assert.Equal(PngFixture, await File.ReadAllBytesAsync(first.ImagePath));
        var manifest = await File.ReadAllTextAsync(first.ManifestPath);
        Assert.Equal(
            "image_path,timestamp_ms,screen_type,capture_source,width,height,captured_at_utc\n" +
            "frame.png,12345,unknown,\"DDR GRAND PRIX, result\",1280,720," +
            "2026-07-13T01:02:03.0000000+00:00\n",
            manifest);
        Assert.DoesNotContain('\r', manifest);
        Assert.False(HasUtf8Bom(first.ManifestPath));
        Assert.False(HasUtf8Bom(first.MetadataPath));
        Assert.Empty(Directory.EnumerateDirectories(fixture.DataRoot, ".*.tmp"));
    }

    [Fact]
    public async Task Atomic_writer_removes_staging_output_when_cancelled()
    {
        using var fixture = new CaptureDirectoryFixture();
        var writer = new AtomicCaptureOutputWriter(fixture.OutputRoot, fixture.DataRoot);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.WriteAsync(Frame("fixture"), cancellation.Token));

        Assert.False(Directory.Exists(fixture.OutputRoot));
        Assert.Empty(Directory.EnumerateDirectories(fixture.DataRoot, ".*.tmp"));
    }

    [Fact]
    public async Task Repository_writer_resolves_root_lazily_and_uses_root_data_directory()
    {
        using var fixture = new CaptureDirectoryFixture();
        var resolverCalls = 0;
        var writer = new RepositoryCaptureOutputWriter(() =>
        {
            resolverCalls++;
            return fixture.Root;
        });

        Assert.Equal(0, resolverCalls);
        var output = await writer.WriteAsync(Frame("fixture"));

        Assert.Equal(1, resolverCalls);
        Assert.StartsWith(fixture.OutputRoot, output.DirectoryPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(output.ManifestPath));
    }

    [Fact]
    public async Task Repository_writer_maps_missing_repository_to_write_failure()
    {
        var service = new SingleFrameCaptureService(
            new StubAdapter([Frame("fixture")]),
            new RepositoryCaptureOutputWriter(
                () => throw new InvalidOperationException("repository missing")));

        var result = await service.CaptureAsync(123);

        Assert.Equal(CaptureOperationStatus.WriteFailed, result.Status);
        Assert.Null(result.Output);
    }

    [Fact]
    public void Atomic_writer_rejects_output_outside_data_root()
    {
        using var fixture = new CaptureDirectoryFixture();

        Assert.Throws<ArgumentException>(() =>
            new AtomicCaptureOutputWriter(fixture.DataRoot, fixture.DataRoot));
        Assert.Throws<ArgumentException>(() =>
            new AtomicCaptureOutputWriter(
                Path.Combine(fixture.Root, "outside"),
                fixture.DataRoot));
    }

    [Fact]
    public void Windows_capture_support_check_does_not_open_picker()
    {
        var adapter = new WindowsGraphicsCaptureAdapter();

        Assert.Null(Record.Exception(() => _ = adapter.IsSupported));
    }

    [Fact]
    public async Task Capture_service_reports_unsupported_without_opening_picker()
    {
        var service = new SingleFrameCaptureService(
            new StubAdapter([], isSupported: false),
            new StubWriter());

        var result = await service.CaptureAsync(123);

        Assert.Equal(CaptureOperationStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task Capture_service_maps_boundaries_and_does_not_write_cancelled_capture()
    {
        var writer = new StubWriter();
        var service = new SingleFrameCaptureService(
            new StubAdapter([null]),
            writer);

        var result = await service.CaptureAsync(123);

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.Equal(0, writer.CallCount);
    }

    [Theory]
    [MemberData(nameof(CaptureFailures))]
    public async Task Capture_service_maps_capture_failures(
        Exception exception,
        CaptureOperationStatus expectedStatus)
    {
        var service = new SingleFrameCaptureService(
            new StubAdapter([exception]),
            new StubWriter());

        var result = await service.CaptureAsync(123);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Output);
    }

    public static TheoryData<Exception, CaptureOperationStatus> CaptureFailures => new()
    {
        { new UnauthorizedAccessException(), CaptureOperationStatus.AccessDenied },
        { new CaptureTargetClosedException("closed"), CaptureOperationStatus.TargetClosed },
        { new CaptureInvalidSizeException("0x0"), CaptureOperationStatus.InvalidSize },
        { new CaptureResizedException("resize"), CaptureOperationStatus.Resized },
        { new CaptureDeviceLostException("lost"), CaptureOperationStatus.DeviceLost },
    };

    [Fact]
    public async Task Capture_service_maps_writer_failure_without_success_output()
    {
        var service = new SingleFrameCaptureService(
            new StubAdapter([Frame("fixture")]),
            new StubWriter(new IOException("disk full")));

        var result = await service.CaptureAsync(123);

        Assert.Equal(CaptureOperationStatus.WriteFailed, result.Status);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task View_model_can_capture_again_after_each_explicit_operation()
    {
        var captureService = new StubCaptureService();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner(),
            captureService);

        await viewModel.CaptureOneFrameAsync(123);
        await viewModel.CaptureOneFrameAsync(123);

        Assert.Equal(2, captureService.CallCount);
        Assert.False(viewModel.IsCapturing);
        Assert.True(viewModel.HasCaptureStatus);
        Assert.Equal("1フレームを保存しました", viewModel.CaptureStatusTitle);
    }

    private static CapturedFrame Frame(string source) =>
        new(
            PngFixture,
            1280,
            720,
            12345,
            new DateTimeOffset(2026, 7, 13, 1, 2, 3, TimeSpan.Zero),
            source);

    private static bool HasUtf8Bom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }

    private sealed class StubAdapter(Queue<object?> results, bool isSupported) : IGraphicsCaptureAdapter
    {
        public StubAdapter(IEnumerable<object?> results, bool isSupported = true)
            : this(new Queue<object?>(results), isSupported)
        {
        }

        public bool IsSupported => isSupported;

        public Task<CapturedFrame?> CaptureSingleFrameAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            var result = results.Dequeue();
            return result is Exception exception
                ? Task.FromException<CapturedFrame?>(exception)
                : Task.FromResult((CapturedFrame?)result);
        }
    }

    private sealed class StubWriter(Exception? failure = null) : ICaptureOutputWriter
    {
        public int CallCount { get; private set; }

        public Task<CaptureOutput> WriteAsync(
            CapturedFrame frame,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return failure is null
                ? Task.FromResult(new CaptureOutput("capture", "frame.png", "manifest.csv", "metadata.json"))
                : Task.FromException<CaptureOutput>(failure);
        }
    }

    private sealed class StubCaptureService : ISingleFrameCaptureService
    {
        public int CallCount { get; private set; }

        public Task<CaptureOperationResult> CaptureAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new CaptureOperationResult(
                CaptureOperationStatus.Saved,
                "saved",
                new CaptureOutput("capture", "frame.png", "manifest.csv", "metadata.json")));
        }
    }

    private sealed class StubWorkflowRunner : IPersonalScoreDbWorkflowRunner
    {
        public Task<PersonalScoreDbWorkflowResult> RunAsync(
            string workflowInputPath,
            string scoreDatabasePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CaptureDirectoryFixture : IDisposable
    {
        public CaptureDirectoryFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ddrgp-capture-{Guid.NewGuid():N}");
            DataRoot = Path.Combine(Root, "data");
            OutputRoot = Path.Combine(DataRoot, "windows_capture");
        }

        public string Root { get; }
        public string DataRoot { get; }
        public string OutputRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
