using System.IO;
using DDRGpScoreViewer.Data;

namespace DDRGpScoreViewer.WebBestSync;

internal sealed class WebBestSyncCoordinator
{
    private const int BatchSize = 50;
    private const int MaximumAutomaticRetries = 5;
    private static readonly TimeSpan[] RetrySchedule =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
    ];

    private readonly IWebBestSyncStateStore stateStore;
    private readonly WebBestProjectionRepository projectionRepository;
    private readonly IWebBestSyncApiClient apiClient;
    private readonly string scoreDatabasePath;
    private readonly string masterDatabasePath;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Func<double> jitter;
    private readonly Func<DateTimeOffset> utcNow;

    public WebBestSyncCoordinator(
        IWebBestSyncStateStore stateStore,
        WebBestProjectionRepository projectionRepository,
        IWebBestSyncApiClient apiClient,
        string scoreDatabasePath,
        string masterDatabasePath,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<double>? jitter = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.stateStore = stateStore;
        this.projectionRepository = projectionRepository;
        this.apiClient = apiClient;
        this.scoreDatabasePath = scoreDatabasePath;
        this.masterDatabasePath = masterDatabasePath;
        this.delay = delay ?? Task.Delay;
        this.jitter = jitter ?? Random.Shared.NextDouble;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public event Action<WebBestSyncSnapshot>? StateChanged;

    public WebBestSyncSnapshot State => stateStore.Load();

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var current = stateStore.Load();
        if (current.Enabled == enabled)
        {
            Publish(current);
            return;
        }
        Publish(stateStore.SetEnabled(enabled));
        if (enabled)
        {
            await SynchronizeAsync(cancellationToken);
        }
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var state = stateStore.Load();
            if (!state.Enabled)
            {
                Publish(state);
                return;
            }

            var projections = projectionRepository.ReadAll(scoreDatabasePath);
            state = stateStore.Reconcile(projections);
            Publish(state);
            if (state.FullSnapshotRequired)
            {
                await SynchronizeSnapshotAsync(projections, cancellationToken);
            }
            else
            {
                await SynchronizeDeltaAsync(projections, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            Publish(stateStore.RecordStatus(
                WebBestSyncStatus.AuthInvalid,
                errorCode: "AUTH_INVALID"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Publish(stateStore.RecordStatus(
                WebBestSyncStatus.ErrorRetryable,
                errorCode: "LOCAL_STATE_ERROR"));
        }
        finally
        {
            operationGate.Release();
        }
    }

    public void RequireReconciliation()
    {
        var state = stateStore.Load();
        if (state.Enabled)
        {
            Publish(stateStore.RequestFullSnapshot());
        }
    }

    public async Task DeletePublicBestsAsync(CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await ExecuteWithRetryAsync(
                token => apiClient.DeletePublicBestsAsync(token),
                item => item.Status,
                cancellationToken);
            if (result.Status == WebBestApiStatus.Success)
            {
                Publish(stateStore.ClearAfterPublicDelete());
                return;
            }
            PublishFailure(result.Status, result.ErrorCode);
        }
        finally
        {
            operationGate.Release();
        }
    }

    internal static TimeSpan RetryDelay(int attempt, double jitterValue)
    {
        var scheduleIndex = Math.Clamp(attempt, 0, RetrySchedule.Length - 1);
        var baseDelay = RetrySchedule[scheduleIndex];
        var multiplier = 0.8 + Math.Clamp(jitterValue, 0.0, 1.0) * 0.4;
        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * multiplier);
    }

    private async Task SynchronizeDeltaAsync(
        IReadOnlyList<PlayerChartBestProjectionV1> projections,
        CancellationToken cancellationToken)
    {
        var projectionByChart = projections.ToDictionary(
            item => item.ChartId,
            StringComparer.Ordinal);
        var masterVersion = WebBestProjectionRepository.ReadMasterVersion(masterDatabasePath);
        var anyAccepted = false;
        while (true)
        {
            var state = stateStore.Load();
            var pending = state.Entries
                .Where(entry => !string.Equals(
                    entry.DesiredProjectionHash,
                    entry.SyncedProjectionHash,
                    StringComparison.Ordinal))
                .Take(BatchSize)
                .ToArray();
            if (pending.Length == 0)
            {
                var status = stateStore.RecordStatus(
                    WebBestSyncStatus.Idle,
                    lastSuccessfulSyncAt: anyAccepted ? utcNow() : null,
                    retryAttempt: 0);
                Publish(status);
                return;
            }

            var operations = pending.Select(entry =>
            {
                if (entry.DesiredProjectionHash is null)
                {
                    return new WebBestDeltaOperation("delete", entry.ChartId, null, null);
                }
                if (!projectionByChart.TryGetValue(entry.ChartId, out var projection) ||
                    !string.Equals(
                        WebBestProjectionContract.Hash(projection),
                        entry.DesiredProjectionHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Web Best projection changed while preparing a delta batch.");
                }
                return new WebBestDeltaOperation(
                    "upsert",
                    entry.ChartId,
                    entry.DesiredProjectionHash,
                    projection);
            }).ToArray();
            Publish(stateStore.RecordStatus(WebBestSyncStatus.Syncing));
            var result = await ExecuteWithRetryAsync(
                token => apiClient.SendDeltaAsync(masterVersion, operations, token),
                item => item.Status,
                cancellationToken);
            if (result.Status != WebBestApiStatus.Success)
            {
                PublishFailure(result.Status, result.ErrorCode);
                return;
            }

            foreach (var item in result.Items.OrderBy(item => item.Index))
            {
                if (item.Index < 0 || item.Index >= operations.Length)
                {
                    Publish(stateStore.RecordStatus(
                        WebBestSyncStatus.Dirty,
                        errorCode: "INVALID_RESPONSE"));
                    return;
                }
                var operation = operations[item.Index];
                if (item.Accepted)
                {
                    anyAccepted = true;
                    Publish(stateStore.MarkSynced(
                        operation.ChartId,
                        operation.SentProjectionHash));
                }
                else if (string.Equals(item.ErrorCode, "UNKNOWN_CHART", StringComparison.Ordinal))
                {
                    Publish(stateStore.MarkDeferred(operation.ChartId, "UNKNOWN_CHART"));
                }
                else
                {
                    Publish(stateStore.RecordStatus(
                        WebBestSyncStatus.Dirty,
                        errorCode: item.ErrorCode ?? "INVALID_PROJECTION"));
                    return;
                }
            }

            if (stateStore.Load().Entries.All(entry =>
                    string.Equals(entry.DeferredError, "UNKNOWN_CHART", StringComparison.Ordinal) ||
                    string.Equals(
                        entry.DesiredProjectionHash,
                        entry.SyncedProjectionHash,
                        StringComparison.Ordinal)))
            {
                Publish(stateStore.RecordStatus(
                    WebBestSyncStatus.Dirty,
                    lastSuccessfulSyncAt: anyAccepted ? utcNow() : null,
                    retryAttempt: 0,
                    errorCode: "UNKNOWN_CHART"));
                return;
            }
        }
    }

    private async Task SynchronizeSnapshotAsync(
        IReadOnlyList<PlayerChartBestProjectionV1> projections,
        CancellationToken cancellationToken)
    {
        Publish(stateStore.RecordStatus(WebBestSyncStatus.Reconciling));
        var masterVersion = WebBestProjectionRepository.ReadMasterVersion(masterDatabasePath);
        var begin = await ExecuteWithRetryAsync(
            token => apiClient.BeginSnapshotAsync(
                masterVersion,
                projections.Count,
                token),
            item => item.Status,
            cancellationToken);
        if (begin.Status != WebBestApiStatus.Success || begin.SnapshotId is null)
        {
            PublishFailure(begin.Status, begin.ErrorCode);
            return;
        }

        var snapshotId = begin.SnapshotId;
        for (var offset = 0; offset < projections.Count; offset += BatchSize)
        {
            var chunk = projections.Skip(offset).Take(BatchSize).ToArray();
            var upload = await ExecuteWithRetryAsync(
                token => apiClient.UploadSnapshotChunkAsync(
                    snapshotId,
                    $"chunk-{offset / BatchSize:D4}",
                    chunk,
                    token),
                item => item.Status,
                cancellationToken);
            if (upload.Status != WebBestApiStatus.Success)
            {
                await apiClient.AbortSnapshotAsync(snapshotId, cancellationToken);
                PublishFailure(upload.Status, upload.ErrorCode);
                return;
            }
        }

        var commit = await ExecuteWithRetryAsync(
            token => apiClient.CommitSnapshotAsync(snapshotId, token),
            item => item.Status,
            cancellationToken);
        if (commit.Status != WebBestApiStatus.Success)
        {
            await apiClient.AbortSnapshotAsync(snapshotId, cancellationToken);
            PublishFailure(commit.Status, commit.ErrorCode);
            return;
        }

        Publish(stateStore.CompleteSnapshot(projections, utcNow()));
        var current = projectionRepository.ReadAll(scoreDatabasePath);
        var reconciled = stateStore.Reconcile(current);
        Publish(reconciled);
        if (reconciled.PendingCount > 0)
        {
            await SynchronizeDeltaAsync(current, cancellationToken);
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<T, WebBestApiStatus> status,
        CancellationToken cancellationToken)
    {
        T result = await operation(cancellationToken);
        var attempt = 0;
        while (status(result) == WebBestApiStatus.RetryableError &&
               attempt < MaximumAutomaticRetries)
        {
            var retryDelay = RetryDelay(attempt, jitter());
            var nextRetry = utcNow() + retryDelay;
            Publish(stateStore.RecordStatus(
                WebBestSyncStatus.ErrorRetryable,
                retryAttempt: attempt + 1,
                nextRetryAt: nextRetry,
                errorCode: "NETWORK_ERROR"));
            await delay(retryDelay, cancellationToken);
            result = await operation(cancellationToken);
            attempt += 1;
        }
        return result;
    }

    private void PublishFailure(WebBestApiStatus status, string? errorCode)
    {
        var localStatus = status switch
        {
            WebBestApiStatus.AuthenticationInvalid => WebBestSyncStatus.AuthInvalid,
            WebBestApiStatus.RetryableError => WebBestSyncStatus.ErrorRetryable,
            _ => WebBestSyncStatus.Dirty,
        };
        Publish(stateStore.RecordStatus(
            localStatus,
            errorCode: errorCode ?? status.ToString().ToUpperInvariant()));
    }

    private void Publish(WebBestSyncSnapshot state) => StateChanged?.Invoke(state);
}
