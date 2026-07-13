using System.IO;
using System.Runtime.CompilerServices;
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

    [Fact]
    public async Task Session_writer_publishes_ordered_manifest_only_after_completion()
    {
        using var fixture = new CaptureDirectoryFixture();
        var writer = new AtomicCaptureSessionOutputWriter(fixture.OutputRoot, fixture.DataRoot);
        await using var transaction = await writer.BeginAsync();

        await transaction.WriteFrameAsync(Frame("DDR GRAND PRIX", 100));
        await transaction.WriteFrameAsync(Frame("DDR GRAND PRIX", 101));

        Assert.False(Directory.Exists(fixture.OutputRoot));
        var output = await transaction.CompleteAsync();
        var manifest = await File.ReadAllTextAsync(output.ManifestPath);
        Assert.Equal(2, output.FrameCount);
        Assert.Contains("\"frames/frame-000001.png\",100", manifest);
        Assert.Contains("\"frames/frame-000002.png\",101", manifest);
        Assert.DoesNotContain('\r', manifest);
        Assert.False(HasUtf8Bom(output.ManifestPath));
        Assert.False(HasUtf8Bom(output.MetadataPath));
        Assert.Equal(2, Directory.EnumerateFiles(
            Path.Combine(output.DirectoryPath, "frames"), "*.png").Count());
    }

    [Fact]
    public async Task Session_writer_rejects_non_increasing_timestamps_and_removes_staging()
    {
        using var fixture = new CaptureDirectoryFixture();
        var writer = new AtomicCaptureSessionOutputWriter(fixture.OutputRoot, fixture.DataRoot);
        await using (var transaction = await writer.BeginAsync())
        {
            await transaction.WriteFrameAsync(Frame("fixture", 100));
            await Assert.ThrowsAsync<InvalidDataException>(
                () => transaction.WriteFrameAsync(Frame("fixture", 100)));
        }

        Assert.False(Directory.Exists(fixture.OutputRoot));
        Assert.Empty(Directory.EnumerateDirectories(fixture.DataRoot, ".*.tmp"));
    }

    [Theory]
    [InlineData(CaptureSessionEndReason.TargetClosed, CaptureOperationStatus.TargetClosed)]
    [InlineData(CaptureSessionEndReason.Resized, CaptureOperationStatus.Resized)]
    [InlineData(CaptureSessionEndReason.DeviceLost, CaptureOperationStatus.DeviceLost)]
    public async Task Continuous_service_discards_abnormal_sessions(
        CaptureSessionEndReason endReason,
        CaptureOperationStatus expectedStatus)
    {
        using var fixture = new CaptureDirectoryFixture();
        var source = new StubFrameSource([Frame("fixture", 100)], endReason);
        var service = new ContinuousCaptureService(
            new StubContinuousAdapter(source),
            new AtomicCaptureSessionOutputWriter(fixture.OutputRoot, fixture.DataRoot));

        var result = await service.RunAsync(123);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Output);
        Assert.Equal(1, source.DisposeCount);
        Assert.False(Directory.Exists(fixture.OutputRoot));
        Assert.Empty(Directory.EnumerateDirectories(fixture.DataRoot, ".*.tmp"));
    }

    [Fact]
    public async Task Continuous_service_stops_idempotently_and_publishes_multiple_frames()
    {
        using var fixture = new CaptureDirectoryFixture();
        var source = new StubFrameSource([Frame("fixture", 100), Frame("fixture", 101)]);
        var service = new ContinuousCaptureService(
            new StubContinuousAdapter(source),
            new AtomicCaptureSessionOutputWriter(fixture.OutputRoot, fixture.DataRoot));

        var run = service.RunAsync(123);
        await source.ReaderStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var duplicateStart = await service.RunAsync(123);
        await service.StopAsync();
        await service.StopAsync();
        var result = await run;

        Assert.Equal(CaptureOperationStatus.AlreadyRunning, duplicateStart.Status);
        Assert.Equal(CaptureOperationStatus.Saved, result.Status);
        Assert.Equal(2, result.Output?.FrameCount);
        Assert.Equal(1, source.StopCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Continuous_service_does_not_publish_when_stopped_before_first_frame()
    {
        using var fixture = new CaptureDirectoryFixture();
        var source = new StubFrameSource([]);
        var service = new ContinuousCaptureService(
            new StubContinuousAdapter(source),
            new AtomicCaptureSessionOutputWriter(fixture.OutputRoot, fixture.DataRoot));

        var run = service.RunAsync(123);
        await source.ReaderStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();
        var result = await run;

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public async Task Continuous_service_maps_picker_cancel_and_writer_failure()
    {
        var cancelled = new ContinuousCaptureService(
            new StubContinuousAdapter(null),
            new FailingSessionWriter());
        var cancelledResult = await cancelled.RunAsync(123);
        Assert.Equal(CaptureOperationStatus.Cancelled, cancelledResult.Status);

        var failed = new ContinuousCaptureService(
            new StubContinuousAdapter(new StubFrameSource(
                [Frame("fixture", 100)],
                CaptureSessionEndReason.Stopped)),
            new FailingSessionWriter(new IOException("disk full")));
        var failedResult = await failed.RunAsync(123);
        Assert.Equal(CaptureOperationStatus.WriteFailed, failedResult.Status);
        Assert.Null(failedResult.Output);
    }

    [Theory]
    [InlineData(nameof(SessionWriterFailureStage.Begin))]
    [InlineData(nameof(SessionWriterFailureStage.Write))]
    [InlineData(nameof(SessionWriterFailureStage.Complete))]
    [InlineData(nameof(SessionWriterFailureStage.Dispose))]
    public async Task Continuous_service_maps_output_permission_failures_as_write_failures(
        string failureStageName)
    {
        var failureStage = Enum.Parse<SessionWriterFailureStage>(failureStageName);
        var service = new ContinuousCaptureService(
            new StubContinuousAdapter(new StubFrameSource(
                [Frame("fixture", 100)],
                CaptureSessionEndReason.Stopped)),
            new FailingSessionWriter(
                new UnauthorizedAccessException("denied"),
                failureStage));

        var result = await service.RunAsync(123);

        Assert.Equal(CaptureOperationStatus.WriteFailed, result.Status);
        Assert.Null(result.Output);
    }

    [Theory]
    [InlineData(true, CaptureOperationStatus.AccessDenied)]
    [InlineData(false, CaptureOperationStatus.InvalidSize)]
    public async Task Continuous_service_maps_start_failures(
        bool accessDenied,
        CaptureOperationStatus expectedStatus)
    {
        var exception = accessDenied
            ? (Exception)new UnauthorizedAccessException("denied")
            : new CaptureInvalidSizeException("0x0");
        var service = new ContinuousCaptureService(
            new StubThrowingContinuousAdapter(exception),
            new FailingSessionWriter());

        var result = await service.RunAsync(123);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Continuous_service_can_stop_while_picker_is_pending()
    {
        var adapter = new BlockingStartAdapter();
        var service = new ContinuousCaptureService(adapter, new FailingSessionWriter());

        var run = service.RunAsync(123);
        await adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();
        var result = await run;

        Assert.Equal(CaptureOperationStatus.Cancelled, result.Status);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task View_model_rejects_double_start_and_waits_for_stop_completion()
    {
        var captureService = new StubContinuousCaptureService();
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner(),
            continuousCaptureService: captureService);

        var run = viewModel.StartContinuousCaptureAsync(123);
        await captureService.RunStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.StartContinuousCaptureAsync(123);
        var stop = viewModel.StopContinuousCaptureAsync();
        await viewModel.StopContinuousCaptureAsync();
        await stop;
        await run;

        Assert.Equal(1, captureService.RunCount);
        Assert.Equal(1, captureService.StopCount);
        Assert.False(viewModel.IsContinuousCapturing);
        Assert.False(viewModel.IsStoppingCapture);
        Assert.Equal("連続キャプチャを保存しました", viewModel.CaptureStatusTitle);
    }

    [Fact]
    public async Task View_model_repeated_stop_waits_for_in_flight_completion()
    {
        var captureService = new StubContinuousCaptureService(completeOnStop: false);
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new StubWorkflowRunner(),
            continuousCaptureService: captureService);

        var run = viewModel.StartContinuousCaptureAsync(123);
        await captureService.RunStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var firstStop = viewModel.StopContinuousCaptureAsync();
        await captureService.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var repeatedStop = viewModel.StopContinuousCaptureAsync();

        Assert.False(firstStop.IsCompleted);
        Assert.False(repeatedStop.IsCompleted);
        Assert.Equal(1, captureService.StopCount);

        captureService.Complete();
        await Task.WhenAll(firstStop, repeatedStop, run);

        Assert.False(viewModel.IsContinuousCapturing);
        Assert.False(viewModel.IsStoppingCapture);
    }

    private static CapturedFrame Frame(string source, long timestampMs = 12345) =>
        new(
            PngFixture,
            1280,
            720,
            timestampMs,
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

    private sealed class StubContinuousAdapter(IContinuousFrameSource? source)
        : IContinuousGraphicsCaptureAdapter
    {
        public bool IsSupported => true;

        public Task<IContinuousFrameSource?> StartSessionAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) => Task.FromResult(source);
    }

    private sealed class StubThrowingContinuousAdapter(Exception exception)
        : IContinuousGraphicsCaptureAdapter
    {
        public bool IsSupported => true;

        public Task<IContinuousFrameSource?> StartSessionAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IContinuousFrameSource?>(exception);
    }

    private sealed class BlockingStartAdapter : IContinuousGraphicsCaptureAdapter
    {
        public bool IsSupported => true;
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IContinuousFrameSource?> StartSessionAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class StubContinuousCaptureService(bool completeOnStop = true)
        : IContinuousCaptureService
    {
        private readonly TaskCompletionSource<CaptureSessionOperationResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RunStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning { get; private set; }
        public int RunCount { get; private set; }
        public int StopCount { get; private set; }

        public async Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            IsRunning = true;
            RunStarted.TrySetResult();
            try
            {
                return await completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                IsRunning = false;
            }
        }

        public Task StopAsync()
        {
            StopCount++;
            StopRequested.TrySetResult();
            if (completeOnStop)
            {
                Complete();
            }
            return Task.CompletedTask;
        }

        public void Complete()
        {
            completion.TrySetResult(new CaptureSessionOperationResult(
                CaptureOperationStatus.Saved,
                "saved",
                new CaptureSessionOutput("session", "manifest", "metadata", 2)));
        }
    }

    private sealed class StubFrameSource : IContinuousFrameSource
    {
        private readonly IReadOnlyList<CapturedFrame> frames;
        private readonly TaskCompletionSource<CaptureSessionEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int stopped;

        public StubFrameSource(
            IReadOnlyList<CapturedFrame> frames,
            CaptureSessionEndReason? immediateEndReason = null)
        {
            this.frames = frames;
            if (immediateEndReason is not null)
            {
                completion.SetResult(immediateEndReason.Value);
            }
        }

        public TaskCompletionSource ReaderStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CaptureSessionEndReason> Completion => completion.Task;
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReaderStarted.TrySetResult();
            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return frame;
            }
            await completion.Task.WaitAsync(cancellationToken);
        }

        public Task StopAsync()
        {
            if (Interlocked.Exchange(ref stopped, 1) == 0)
            {
                StopCount++;
                completion.TrySetResult(CaptureSessionEndReason.Stopped);
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            completion.TrySetResult(CaptureSessionEndReason.Stopped);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingSessionWriter(
        Exception? failure = null,
        SessionWriterFailureStage failureStage = SessionWriterFailureStage.Begin)
        : ICaptureSessionOutputWriter
    {
        public Task<ICaptureSessionOutputTransaction> BeginAsync(
            CancellationToken cancellationToken = default) =>
            failure is not null && failureStage == SessionWriterFailureStage.Begin
                ? Task.FromException<ICaptureSessionOutputTransaction>(failure)
                : Task.FromResult<ICaptureSessionOutputTransaction>(
                    new FailingTransaction(failure, failureStage));

        private sealed class FailingTransaction(
            Exception? failure,
            SessionWriterFailureStage failureStage) : ICaptureSessionOutputTransaction
        {
            public int FrameCount { get; private set; }

            public Task WriteFrameAsync(CapturedFrame frame, CancellationToken cancellationToken = default)
            {
                if (failure is not null && failureStage == SessionWriterFailureStage.Write)
                {
                    return Task.FromException(failure);
                }
                FrameCount++;
                return Task.CompletedTask;
            }

            public Task<CaptureSessionOutput> CompleteAsync(CancellationToken cancellationToken = default) =>
                failure is not null && failureStage == SessionWriterFailureStage.Complete
                    ? Task.FromException<CaptureSessionOutput>(failure)
                    : Task.FromResult(new CaptureSessionOutput("output", "manifest", "metadata", FrameCount));

            public ValueTask DisposeAsync() =>
                failure is not null && failureStage == SessionWriterFailureStage.Dispose
                    ? ValueTask.FromException(failure)
                    : ValueTask.CompletedTask;
        }
    }

    private enum SessionWriterFailureStage
    {
        Begin,
        Write,
        Complete,
        Dispose,
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
