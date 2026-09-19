using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ScreenshotImportViewModelTests
{
    [Fact]
    public async Task Mixed_batch_is_sequential_keeps_results_and_reloads_once_at_batch_end()
    {
        using var fixture = new DatabaseFixture();
        var call = 0;
        var service = new FakeScreenshotImportService(async (path, _) =>
        {
            await Task.Yield();
            call++;
            if (call == 1)
            {
                fixture.AddPlay("imported", "2026-09-16T12:00:00+00:00", 900_000, 1_000);
                // This represents a live-monitoring commit that lands while the batch runs.
                fixture.AddPlay("live-concurrent", "2026-09-16T12:00:01+00:00", 910_000, 1_010);
                return Result(path, ScreenshotImportItemStatus.Saved);
            }
            return call switch
            {
                2 => Result(path, ScreenshotImportItemStatus.Duplicate),
                3 => Result(path, ScreenshotImportItemStatus.RecognitionFailed),
                4 => throw new InvalidOperationException("fixture write failure"),
                _ => Result(path, ScreenshotImportItemStatus.InputError),
            };
        });
        var viewModel = CreateViewModel(fixture, service);
        var reloadCount = 0;
        viewModel.ChartBestListReset += (_, _) => reloadCount++;

        var started = await viewModel.ImportScreenshotsAsync(
            ["one.png", "two.png", "three.png", "four.png", "five.png"]);

        Assert.True(started);
        Assert.Equal(ScreenshotImportState.Completed, viewModel.CurrentScreenshotImportState);
        Assert.Equal(5, viewModel.ScreenshotImportCompletedCount);
        Assert.Equal(5, viewModel.ScreenshotImportTotalCount);
        Assert.Equal(1, service.MaxConcurrency);
        Assert.Equal(1, reloadCount);
        Assert.Equal(
            [
                ScreenshotImportItemStatus.Saved,
                ScreenshotImportItemStatus.Duplicate,
                ScreenshotImportItemStatus.RecognitionFailed,
                ScreenshotImportItemStatus.InputError,
                ScreenshotImportItemStatus.InputError,
            ],
            viewModel.ScreenshotImportResults.Select(result => result.Status));
        Assert.Contains(viewModel.Plays, play => play.PlayId == "imported");
        Assert.Contains(viewModel.Plays, play => play.PlayId == "live-concurrent");
        Assert.Contains("保存 1件", viewModel.ScreenshotImportSummaryDisplay);

        viewModel.SetDataManagementPage(false);
        viewModel.SetDataManagementPage(true);
        Assert.Equal(ScreenshotImportState.Completed, viewModel.CurrentScreenshotImportState);
        Assert.Equal(5, viewModel.ScreenshotImportResults.Count);

        var restarted = CreateViewModel(fixture, service);
        Assert.Equal(ScreenshotImportState.Idle, restarted.CurrentScreenshotImportState);
        Assert.Empty(restarted.ScreenshotImportResults);
    }

    [Fact]
    public async Task Cancel_stops_new_images_disables_queuing_and_preserves_completed_results()
    {
        using var fixture = new DatabaseFixture();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeScreenshotImportService(async (path, _) =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task;
            return Result(path, ScreenshotImportItemStatus.Saved);
        });
        var viewModel = CreateViewModel(fixture, service);

        var batch = viewModel.ImportScreenshotsAsync(["one.png", "two.png"]);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsScreenshotImporting);
        Assert.False(viewModel.CanStartScreenshotImport);
        Assert.True(viewModel.CanStartMonitoring);
        Assert.False(await viewModel.ImportScreenshotsAsync(["queued.png"]));
        viewModel.CancelScreenshotImport();
        releaseFirst.TrySetResult();
        await batch;

        Assert.Equal(ScreenshotImportState.Cancelled, viewModel.CurrentScreenshotImportState);
        Assert.Single(viewModel.ScreenshotImportResults);
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task Application_exit_cancels_batch_and_waits_for_in_flight_item_to_finish()
    {
        using var fixture = new DatabaseFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeScreenshotImportService(async (path, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return Result(path, ScreenshotImportItemStatus.Duplicate);
        });
        var viewModel = CreateViewModel(fixture, service);
        var batch = viewModel.ImportScreenshotsAsync(["one.png", "two.png"]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.RequestApplicationExit();
        var wait = viewModel.WaitForOperationsAsync();

        Assert.False(wait.IsCompleted);
        release.TrySetResult();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        await batch;
        Assert.Equal(ScreenshotImportState.Cancelled, viewModel.CurrentScreenshotImportState);
        Assert.Single(viewModel.ScreenshotImportResults);
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task Import_remains_available_while_monitoring_is_active()
    {
        using var fixture = new DatabaseFixture();
        var monitoring = new BlockingMonitoringService();
        var import = new FakeScreenshotImportService((path, _) =>
            Task.FromResult(Result(path, ScreenshotImportItemStatus.RecognitionFailed)));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            continuousCaptureService: monitoring,
            userSettingsStore: new MemoryUserSettingsStore(null),
            screenshotImportService: import);
        viewModel.Load(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath,
            persist: false);
        var reloadCount = 0;
        viewModel.ChartBestListReset += (_, _) => reloadCount++;
        var monitoringTask = viewModel.StartContinuousCaptureAndSaveAsync(
            123,
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath);
        await monitoring.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MonitoringState.Monitoring, viewModel.CurrentMonitoringState);
        Assert.True(viewModel.CanStartScreenshotImport);
        Assert.True(await viewModel.ImportScreenshotsAsync(["during-monitoring.png"]));
        Assert.Equal(ScreenshotImportState.Completed, viewModel.CurrentScreenshotImportState);
        Assert.Equal(0, reloadCount);

        await viewModel.StopContinuousCaptureAsync();
        await monitoringTask;
    }

    private static MainViewModel CreateViewModel(
        DatabaseFixture fixture,
        IScreenshotImportService service)
    {
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: new MemoryUserSettingsStore(null),
            screenshotImportService: service);
        viewModel.Load(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath,
            persist: false);
        return viewModel;
    }

    private static ScreenshotImportItemResult Result(
        string path,
        ScreenshotImportItemStatus status) =>
        new(path, status, status.ToString(), []);

    private sealed class FakeScreenshotImportService(
        Func<string, CancellationToken, Task<ScreenshotImportItemResult>> process)
        : IScreenshotImportService
    {
        private int active;

        public int CallCount { get; private set; }
        public int MaxConcurrency { get; private set; }

        public async Task<ScreenshotImportItemResult> ProcessAsync(
            string imagePath,
            string scoreDatabasePath,
            string masterDatabasePath,
            string catalogDatabasePath,
            CancellationToken cancellationToken = default)
        {
            _ = scoreDatabasePath;
            _ = masterDatabasePath;
            _ = catalogDatabasePath;
            CallCount++;
            var current = Interlocked.Increment(ref active);
            MaxConcurrency = Math.Max(MaxConcurrency, current);
            try
            {
                return await process(imagePath, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
    }

    private sealed class BlockingMonitoringService : IMonitoringContinuousCaptureService
    {
        private readonly TaskCompletionSource<CaptureSessionOperationResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRunning { get; private set; }

        public Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            RunAsync(ownerWindowHandle, new Progress<CaptureSessionProgress>(), cancellationToken);

        public async Task<CaptureSessionOperationResult> RunAsync(
            nint ownerWindowHandle,
            IProgress<CaptureSessionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            _ = ownerWindowHandle;
            IsRunning = true;
            var now = DateTimeOffset.UtcNow;
            progress.Report(new CaptureSessionProgress(
                new CaptureTargetInfo("DDR GRAND PRIX", 1280, 720),
                1,
                now,
                now));
            Started.TrySetResult();
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
            completion.TrySetResult(new CaptureSessionOperationResult(
                CaptureOperationStatus.Cancelled,
                "fixture stopped"));
            return Task.CompletedTask;
        }
    }
}
