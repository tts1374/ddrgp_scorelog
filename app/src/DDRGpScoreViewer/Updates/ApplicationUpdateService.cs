using Velopack;
using Velopack.Sources;

namespace DDRGpScoreViewer.Updates;

public enum ApplicationUpdateStatus
{
    Unsupported,
    NoUpdate,
    Available,
    Downloaded,
    ReadyToRestart,
    Failed,
}

public sealed record ApplicationUpdateResult(
    ApplicationUpdateStatus Status,
    string Message,
    string? Version = null);

public interface IApplicationUpdateService
{
    bool IsSupported { get; }

    Task<ApplicationUpdateResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default);

    Task<ApplicationUpdateResult> DownloadAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ApplicationUpdateResult> ApplyAndRestartAsync(
        Func<Task> prepareExit,
        Func<Task> completeExit,
        Action forceExit,
        CancellationToken cancellationToken = default);
}

public sealed class ApplicationUpdateService : IApplicationUpdateService
{
    public const string GitHubRepositoryUrl = "https://github.com/tts1374/ddrgp_scorelog";
    public static readonly TimeSpan CheckOperationTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DownloadOperationTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DownloadRequestTimeout = TimeSpan.FromMinutes(5);

    private readonly UpdateManager updateManager;
    private readonly TimeSpan checkOperationTimeout;
    private readonly TimeSpan downloadOperationTimeout;
    private readonly Action<VelopackAsset>? applyAndRestartOverride;
    private UpdateInfo? pendingUpdate;

    public ApplicationUpdateService()
        : this(CreateUpdateManager())
    {
    }

    internal ApplicationUpdateService(
        UpdateManager updateManager,
        TimeSpan? checkOperationTimeout = null,
        TimeSpan? downloadOperationTimeout = null,
        Action<VelopackAsset>? applyAndRestartOverride = null)
    {
        this.updateManager = updateManager;
        this.checkOperationTimeout = checkOperationTimeout ?? CheckOperationTimeout;
        this.downloadOperationTimeout = downloadOperationTimeout ?? DownloadOperationTimeout;
        this.applyAndRestartOverride = applyAndRestartOverride;
    }

    public bool IsSupported => updateManager.IsInstalled;

    public async Task<ApplicationUpdateResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return new(
                ApplicationUpdateStatus.Unsupported,
                "インストール済みVeloPack packageから起動した場合だけアプリ更新を確認できます。");
        }

        try
        {
            pendingUpdate = await WaitWithTimeoutAsync(
                updateManager.CheckForUpdatesAsync(),
                checkOperationTimeout,
                cancellationToken);
            if (pendingUpdate is null)
            {
                return new(
                    ApplicationUpdateStatus.NoUpdate,
                    "利用可能なアプリ更新はありません。");
            }

            return new(
                ApplicationUpdateStatus.Available,
                $"アプリ更新 {GetVersion(pendingUpdate)} をダウンロードできます。",
                GetVersion(pendingUpdate));
        }
        catch (Exception exception)
        {
            pendingUpdate = null;
            return FailedResult("アプリ更新の確認に失敗しました。現在のversionで通常利用を続けられます。", exception);
        }
    }

    public async Task<ApplicationUpdateResult> DownloadAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return new(
                ApplicationUpdateStatus.Unsupported,
                "インストール済みVeloPack packageから起動した場合だけアプリ更新をダウンロードできます。");
        }
        if (pendingUpdate is null)
        {
            return new(
                ApplicationUpdateStatus.Failed,
                "先にアプリ更新を確認してください。現在のversionは変更していません。");
        }

        try
        {
            var update = pendingUpdate;
            await WaitWithTimeoutAsync(
                updateManager.DownloadUpdatesAsync(update, progress ?? (_ => { }), cancellationToken),
                downloadOperationTimeout,
                cancellationToken);
            return new(
                ApplicationUpdateStatus.Downloaded,
                $"アプリ更新 {GetVersion(update)} を準備しました。再起動して適用できます。",
                GetVersion(update));
        }
        catch (Exception exception)
        {
            pendingUpdate = null;
            return FailedResult("アプリ更新のdownloadに失敗しました。現在のversionは変更していません。", exception);
        }
    }

    public async Task<ApplicationUpdateResult> ApplyAndRestartAsync(
        Func<Task> prepareExit,
        Func<Task> completeExit,
        Action forceExit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepareExit);
        ArgumentNullException.ThrowIfNull(completeExit);
        ArgumentNullException.ThrowIfNull(forceExit);
        if (!IsSupported)
        {
            return new(
                ApplicationUpdateStatus.Unsupported,
                "インストール済みVeloPack packageから起動した場合だけアプリ更新を適用できます。");
        }
        if (pendingUpdate is null)
        {
            return new(
                ApplicationUpdateStatus.Failed,
                "適用するdownload済みアプリ更新がありません。現在のversionは変更していません。");
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return new(
                ApplicationUpdateStatus.Failed,
                "アプリ更新の適用をcancelしました。現在のversionは変更していません。");
        }

        var updaterStarted = false;
        try
        {
            var update = pendingUpdate;
            await prepareExit();
            if (applyAndRestartOverride is not null)
            {
                applyAndRestartOverride(update.TargetFullRelease);
            }
            else
            {
                updateManager.WaitExitThenApplyUpdates(
                    update.TargetFullRelease,
                    silent: false,
                    restart: true,
                    restartArgs: null);
            }

            updaterStarted = true;
            await completeExit();
            pendingUpdate = null;
            return new(
                ApplicationUpdateStatus.ReadyToRestart,
                $"アプリ更新 {GetVersion(update)} の適用を開始しました。終了処理後に再起動します。",
                GetVersion(update));
        }
        catch (Exception exception)
        {
            var update = pendingUpdate;
            pendingUpdate = null;
            if (updaterStarted && update is not null)
            {
                TryForceExit(forceExit);
                return new(
                    ApplicationUpdateStatus.ReadyToRestart,
                    $"アプリ更新 {GetVersion(update)} の適用を開始しました。終了処理を完了してアプリを終了します。",
                    GetVersion(update));
            }

            return FailedResult("アプリ更新の適用に失敗しました。現在のversionで通常利用を続けられます。", exception);
        }
    }

    private async Task<T> WaitWithTimeoutAsync<T>(
        Task<T> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await operation.WaitAsync(timeout, cancellationToken);

    private async Task WaitWithTimeoutAsync(
        Task operation,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await operation.WaitAsync(timeout, cancellationToken);

    private static string GetVersion(UpdateInfo update) => update.TargetFullRelease.Version.ToString();

    private static ApplicationUpdateResult FailedResult(string prefix, Exception exception) =>
        new(ApplicationUpdateStatus.Failed, $"{prefix} {exception.Message}");

    private static void TryForceExit(Action forceExit)
    {
        try
        {
            forceExit();
        }
        catch
        {
            // The updater is already waiting for this process. Do not return to normal update use.
        }
    }

    private static UpdateManager CreateUpdateManager()
    {
        var source = new GithubSource(
            GitHubRepositoryUrl,
            accessToken: null,
            prerelease: false,
            downloader: new BoundedFileDownloader(DownloadRequestTimeout));
        return new UpdateManager(source, options: null, locator: null);
    }

    private sealed class BoundedFileDownloader : HttpClientFileDownloader
    {
        private readonly double maximumTimeoutMinutes;

        public BoundedFileDownloader(TimeSpan maximumTimeout)
        {
            maximumTimeoutMinutes = maximumTimeout.TotalMinutes;
        }

        public override Task DownloadFile(
            string url,
            string targetFile,
            Action<int> progress,
            IDictionary<string, string>? headers,
            double timeout,
            CancellationToken cancelToken) =>
            base.DownloadFile(
                url,
                targetFile,
                progress,
                headers,
                ClampTimeout(timeout),
                cancelToken);

        public override Task<byte[]> DownloadBytes(
            string url,
            IDictionary<string, string>? headers,
            double timeout) =>
            base.DownloadBytes(url, headers, ClampTimeout(timeout));

        public override Task<string> DownloadString(
            string url,
            IDictionary<string, string>? headers,
            double timeout) =>
            base.DownloadString(url, headers, ClampTimeout(timeout));

        private double ClampTimeout(double timeout) =>
            timeout > 0
                ? Math.Min(timeout, maximumTimeoutMinutes)
                : maximumTimeoutMinutes;
    }
}
