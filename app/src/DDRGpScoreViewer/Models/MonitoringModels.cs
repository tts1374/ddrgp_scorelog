namespace DDRGpScoreViewer.Models;

public enum MonitoringState
{
    Idle,
    Starting,
    WaitingForGame,
    SelectingTarget,
    Monitoring,
    Stopping,
    Stopped,
    ManuallyStopped,
    Blocked,
    ShuttingDown,
    TargetClosed,
    Resized,
    DeviceLost,
    CaptureFailed,
    WorkflowFailed,
}

public sealed record AutomaticMonitoringOptions
{
    public bool Enabled { get; init; } = true;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public int RequiredConsecutiveDetections { get; init; } = 2;
    public int RequiredConsecutiveMisses { get; init; } = 2;

    public void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollInterval),
                "Automatic monitoring polling must be greater than zero.");
        }
        if (RequiredConsecutiveDetections < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequiredConsecutiveDetections),
                "Automatic monitoring detection debounce must be at least one.");
        }
        if (RequiredConsecutiveMisses < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequiredConsecutiveMisses),
                "Automatic monitoring disappearance debounce must be at least one.");
        }
    }
}

public sealed record MonitoringResultSummary(
    int Saved,
    int Duplicate,
    int Excluded,
    int Unresolved,
    int AnalysisFailed,
    int DbRejected,
    int WorkflowFailed,
    DateTimeOffset RecordedAtUtc,
    string Reason)
{
    public static MonitoringResultSummary Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, DateTimeOffset.MinValue, "—");

    public static MonitoringResultSummary FromWorkflow(
        IReadOnlyDictionary<string, int> counts,
        bool workflowFailed,
        DateTimeOffset recordedAtUtc,
        IReadOnlyList<string> reasons) => new(
            Count(counts, "saved"),
            Count(counts, "duplicate"),
            Count(counts, "excluded") + Count(counts, "policy_excluded"),
            Count(counts, "unresolved"),
            Count(counts, "analysis_failed"),
            Count(counts, "db_rejected"),
            workflowFailed
                ? Math.Max(1, Count(counts, "workflow_failed"))
                : Count(counts, "workflow_failed"),
            recordedAtUtc,
            reasons.Count == 0 ? "—" : string.Join(" / ", reasons));

    private static int Count(IReadOnlyDictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out var value) ? value : 0;
}

public sealed record TrayMenuState(bool CanStart, bool CanStop)
{
    public static TrayMenuState FromMonitoringState(MonitoringState state) => state switch
    {
        MonitoringState.Starting or MonitoringState.SelectingTarget or
            MonitoringState.Monitoring => new(false, true),
        MonitoringState.Stopping or MonitoringState.ShuttingDown => new(false, false),
        _ => new(true, false),
    };
}

public sealed class AsyncOperationGate : IDisposable
{
    private readonly object stateLock = new();
    private CancellationTokenSource? cancellation;
    private Task? activeTask;
    private bool disposed;

    public Task RunAsync(Func<CancellationToken, Task> operation)
    {
        TaskCompletionSource completion;
        CancellationTokenSource operationCancellation;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (activeTask is not null)
            {
                return activeTask;
            }
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            operationCancellation = new CancellationTokenSource();
            cancellation = operationCancellation;
            activeTask = completion.Task;
        }
        _ = ExecuteAsync(operation, operationCancellation, completion);
        return completion.Task;
    }

    public void Cancel()
    {
        CancellationTokenSource? toCancel;
        lock (stateLock)
        {
            toCancel = cancellation;
        }
        toCancel?.Cancel();
    }

    public Task WaitAsync()
    {
        lock (stateLock)
        {
            return activeTask ?? Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? toCancel;
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            toCancel = cancellation;
        }
        toCancel?.Cancel();
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationTokenSource operationCancellation,
        TaskCompletionSource completion)
    {
        try
        {
            await operation(operationCancellation.Token);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (stateLock)
            {
                if (ReferenceEquals(cancellation, operationCancellation))
                {
                    cancellation = null;
                    activeTask = null;
                }
            }
            operationCancellation.Dispose();
        }
    }
}
