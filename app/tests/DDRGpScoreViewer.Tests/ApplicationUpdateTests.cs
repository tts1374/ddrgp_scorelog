using System.Diagnostics;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Updates;
using DDRGpScoreViewer.ViewModels;
using Velopack;
using Velopack.Logging;
using Velopack.Locators;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ApplicationUpdateTests
{
    [Fact]
    public async Task VeloPack_release_check_and_download_keep_the_target_version()
    {
        var manager = new FakeUpdateManager(CreateUpdateInfo(1, 2, 0));
        var service = new ApplicationUpdateService(
            manager,
            checkOperationTimeout: TimeSpan.FromSeconds(1),
            downloadOperationTimeout: TimeSpan.FromSeconds(1));

        var available = await service.CheckForUpdatesAsync();
        var downloaded = await service.DownloadAsync();

        Assert.Equal(ApplicationUpdateStatus.Available, available.Status);
        Assert.Equal("1.2.0", available.Version);
        Assert.Equal(ApplicationUpdateStatus.Downloaded, downloaded.Status);
        Assert.Equal("1.2.0", downloaded.Version);
        Assert.Equal(1, manager.CheckCount);
        Assert.Equal(1, manager.DownloadCount);
    }

    [Fact]
    public async Task Download_timeout_returns_failure_without_waiting_for_unbounded_network_work()
    {
        var manager = new FakeUpdateManager(CreateUpdateInfo(1, 2, 0))
        {
            DownloadOperation = (_, _, _) => Task.Delay(Timeout.InfiniteTimeSpan),
        };
        var service = new ApplicationUpdateService(
            manager,
            checkOperationTimeout: TimeSpan.FromSeconds(1),
            downloadOperationTimeout: TimeSpan.FromMilliseconds(50));
        await service.CheckForUpdatesAsync();

        var stopwatch = Stopwatch.StartNew();
        var result = await service.DownloadAsync();
        stopwatch.Stop();

        Assert.Equal(ApplicationUpdateStatus.Failed, result.Status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Contains("現在のversionは変更していません", result.Message);
    }

    [Fact]
    public async Task Download_longer_than_check_timeout_succeeds_when_progress_continues()
    {
        var manager = new FakeUpdateManager(CreateUpdateInfo(1, 2, 0))
        {
            DownloadOperation = async (_, progress, cancellationToken) =>
            {
                progress?.Invoke(10);
                await Task.Delay(TimeSpan.FromSeconds(31), cancellationToken);
                progress?.Invoke(100);
            },
        };
        var service = new ApplicationUpdateService(
            manager,
            checkOperationTimeout: TimeSpan.FromSeconds(1),
            downloadOperationTimeout: TimeSpan.FromSeconds(35));
        var progressValues = new List<int>();
        await service.CheckForUpdatesAsync();

        var downloaded = await service.DownloadAsync(progressValues.Add);

        Assert.Equal(ApplicationUpdateStatus.Downloaded, downloaded.Status);
        Assert.Equal([10, 100], progressValues);
    }

    [Fact]
    public async Task Apply_starts_waiting_updater_before_complete_application_exit()
    {
        var manager = new FakeUpdateManager(CreateUpdateInfo(1, 2, 0));
        var order = new List<string>();
        var service = new ApplicationUpdateService(
            manager,
            checkOperationTimeout: TimeSpan.FromSeconds(1),
            downloadOperationTimeout: TimeSpan.FromSeconds(1),
            _ => order.Add("velopack-wait"));
        await service.CheckForUpdatesAsync();
        await service.DownloadAsync();

        var result = await service.ApplyAndRestartAsync(
            () =>
            {
                order.Add("prepare-exit");
                return Task.CompletedTask;
            },
            () =>
            {
                order.Add("complete-exit");
                return Task.CompletedTask;
            },
            () => order.Add("force-exit"));

        Assert.Equal(ApplicationUpdateStatus.ReadyToRestart, result.Status);
        Assert.Equal(["prepare-exit", "velopack-wait", "complete-exit"], order);
    }

    [Fact]
    public async Task Apply_failure_keeps_the_current_version_and_does_not_start_exit()
    {
        var manager = new FakeUpdateManager(CreateUpdateInfo(1, 2, 0));
        var service = new ApplicationUpdateService(
            manager,
            checkOperationTimeout: TimeSpan.FromSeconds(1),
            downloadOperationTimeout: TimeSpan.FromSeconds(1),
            _ => throw new InvalidOperationException("invalid package"));
        await service.CheckForUpdatesAsync();
        await service.DownloadAsync();
        var completeExitCount = 0;

        var result = await service.ApplyAndRestartAsync(
            () => Task.CompletedTask,
            () =>
            {
                completeExitCount++;
                return Task.CompletedTask;
            },
            () => throw new InvalidOperationException("force exit must not run"));

        Assert.Equal(ApplicationUpdateStatus.Failed, result.Status);
        Assert.Equal(0, completeExitCount);
        Assert.Contains("通常利用を続けられます", result.Message);
    }

    [Fact]
    public async Task Complete_exit_failure_forces_final_exit_and_clears_pending_update()
    {
        var manager = new FakeUpdateManager(CreateUpdateInfo(1, 2, 0));
        var service = new ApplicationUpdateService(
            manager,
            checkOperationTimeout: TimeSpan.FromSeconds(1),
            downloadOperationTimeout: TimeSpan.FromSeconds(1),
            _ => { });
        await service.CheckForUpdatesAsync();
        await service.DownloadAsync();
        var forceExitCount = 0;

        var result = await service.ApplyAndRestartAsync(
            () => Task.CompletedTask,
            () => Task.FromException(new InvalidOperationException("exit failed")),
            () => forceExitCount++);
        var retry = await service.DownloadAsync();

        Assert.Equal(ApplicationUpdateStatus.ReadyToRestart, result.Status);
        Assert.Equal(1, forceExitCount);
        Assert.Equal(ApplicationUpdateStatus.Failed, retry.Status);
    }

    [Fact]
    public async Task Preparation_failure_does_not_start_updater_or_leave_download_ready()
    {
        var manager = new FakeUpdateManager(CreateUpdateInfo(1, 2, 0));
        var applyStarted = false;
        var service = new ApplicationUpdateService(
            manager,
            checkOperationTimeout: TimeSpan.FromSeconds(1),
            downloadOperationTimeout: TimeSpan.FromSeconds(1),
            _ => applyStarted = true);
        await service.CheckForUpdatesAsync();
        await service.DownloadAsync();

        var result = await service.ApplyAndRestartAsync(
            () => Task.FromException(new InvalidOperationException("prepare failed")),
            () => throw new InvalidOperationException("complete exit must not run"),
            () => throw new InvalidOperationException("force exit must not run"));
        var retry = await service.DownloadAsync();

        Assert.Equal(ApplicationUpdateStatus.Failed, result.Status);
        Assert.False(applyStarted);
        Assert.Equal(ApplicationUpdateStatus.Failed, retry.Status);
    }

    [Fact]
    public async Task Check_failure_does_not_disable_normal_viewer_operations()
    {
        var service = new FakeApplicationUpdateService
        {
            CheckResult = new(
                ApplicationUpdateStatus.Failed,
                "GitHub Releasesへ接続できません。現在のversionで通常利用を続けられます。"),
        };
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            applicationUpdateService: service);

        await viewModel.CheckForApplicationUpdateAsync();

        Assert.Equal(ApplicationUpdateStatus.Failed, service.LastRequestedStatus);
        Assert.Contains("通常利用", viewModel.ApplicationUpdateStatusMessage);
        Assert.True(viewModel.CanStartMonitoring);
        Assert.False(viewModel.CanDownloadAndApplyApplicationUpdate);
    }

    [Fact]
    public async Task Download_and_apply_calls_the_complete_exit_callback()
    {
        var service = new FakeApplicationUpdateService
        {
            CheckResult = new(ApplicationUpdateStatus.Available, "available", "1.2.0"),
            DownloadResult = new(ApplicationUpdateStatus.Downloaded, "downloaded", "1.2.0"),
            ApplyResult = new(ApplicationUpdateStatus.ReadyToRestart, "ready", "1.2.0"),
        };
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            applicationUpdateService: service);
        var completeExitCount = 0;

        await viewModel.CheckForApplicationUpdateAsync();
        await viewModel.DownloadAndApplyApplicationUpdateAsync(
            () => Task.CompletedTask,
            () =>
            {
                completeExitCount++;
                return Task.CompletedTask;
            },
            () => { });

        Assert.Equal(1, completeExitCount);
        Assert.Equal("1.2.0", viewModel.ApplicationUpdateVersion);
        Assert.Contains("ready", viewModel.ApplicationUpdateStatusMessage);
    }

    private static UpdateInfo CreateUpdateInfo(int major, int minor, int patch)
    {
        var asset = new VelopackAsset
        {
            PackageId = "com.tts1374.ddrgp_scorelog",
            Version = new SemanticVersion(major, minor, patch, "", ""),
            Type = VelopackAssetType.Full,
            FileName = $"com.tts1374.ddrgp_scorelog-{major}.{minor}.{patch}-full.nupkg",
        };
        return new UpdateInfo(asset, isDowngrade: false, deltaBaseRelease: asset, deltasToTarget: []);
    }

    private sealed class FakeUpdateManager(UpdateInfo update) : UpdateManager(
        Path.GetTempPath(),
        options: null,
        locator: CreateLocator())
    {
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public Func<UpdateInfo, Action<int>?, CancellationToken, Task>? DownloadOperation { get; init; }

        public override bool IsInstalled => true;

        public override Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            CheckCount++;
            return Task.FromResult<UpdateInfo?>(update);
        }

        public override Task DownloadUpdatesAsync(
            UpdateInfo updates,
            Action<int>? progress,
            CancellationToken cancelToken)
        {
            DownloadCount++;
            if (DownloadOperation is not null)
            {
                return DownloadOperation(updates, progress, cancelToken);
            }
            progress?.Invoke(100);
            return Task.CompletedTask;
        }

        private static TestVelopackLocator CreateLocator() =>
            new(
                "com.tts1374.ddrgp_scorelog",
                "1.0.0",
                Path.Combine(Path.GetTempPath(), $"ddrgp-update-test-{Guid.NewGuid():N}"),
                new NullVelopackLogger());
    }

    private sealed class FakeApplicationUpdateService : IApplicationUpdateService
    {
        public ApplicationUpdateResult CheckResult { get; init; } =
            new(ApplicationUpdateStatus.NoUpdate, "no update");
        public ApplicationUpdateResult DownloadResult { get; init; } =
            new(ApplicationUpdateStatus.Downloaded, "downloaded");
        public ApplicationUpdateResult ApplyResult { get; init; } =
            new(ApplicationUpdateStatus.ReadyToRestart, "ready");
        public ApplicationUpdateStatus? LastRequestedStatus { get; private set; }
        public bool IsSupported => true;

        public Task<ApplicationUpdateResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            LastRequestedStatus = CheckResult.Status;
            return Task.FromResult(CheckResult);
        }

        public Task<ApplicationUpdateResult> DownloadAsync(
            Action<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke(100);
            return Task.FromResult(DownloadResult);
        }

        public Task<ApplicationUpdateResult> ApplyAndRestartAsync(
            Func<Task> prepareExit,
            Func<Task> completeExit,
            Action forceExit,
            CancellationToken cancellationToken = default)
        {
            return CompleteAsync(prepareExit, completeExit);
        }

        private async Task<ApplicationUpdateResult> CompleteAsync(
            Func<Task> prepareExit,
            Func<Task> completeExit)
        {
            await prepareExit();
            await completeExit();
            return ApplyResult;
        }
    }
}
