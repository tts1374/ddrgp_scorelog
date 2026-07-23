namespace JacketCatalogCollector;

public sealed class CaptureObservationController(
    Func<WindowCandidate, CancellationToken, Task> startObservation,
    Func<WindowCandidate, CancellationToken, Task> resumeObservation,
    Func<CancellationToken, Task> stopObservation,
    Func<WindowCandidate, CancellationToken, Task<bool>> startCapture,
    Func<Task> stopCapture,
    Func<CaptureLifecycleState> captureState,
    Func<CancellationToken, Task>? finalizeObservation = null,
    Func<CancellationToken, Task<WindowCandidate?>>? detectCandidate = null)
{
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Lock operationSync = new();
    private CancellationTokenSource? operationCancellation;
    private int stopRequestCount;
    private bool shutdownRequested;

    public async Task<bool> StartAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        var operation = BeginOperation(cancellationToken);
        try
        {
            return await StartCoreAsync(candidate, operation.Token);
        }
        finally
        {
            EndOperation(operation);
            operationGate.Release();
        }
    }

    public Task<bool> StartAsync(CancellationToken cancellationToken = default) =>
        StartWithDetectionAsync(StartCoreAsync, cancellationToken);

    public async Task<bool> ResumeAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        var operation = BeginOperation(cancellationToken);
        try
        {
            return await ResumeCoreAsync(candidate, operation.Token);
        }
        finally
        {
            EndOperation(operation);
            operationGate.Release();
        }
    }

    public Task<bool> ResumeAsync(CancellationToken cancellationToken = default) =>
        StartWithDetectionAsync(ResumeCoreAsync, cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopCoreAsync(cancellationToken, finalize: true);
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        lock (operationSync)
        {
            shutdownRequested = true;
        }
        await StopCoreAsync(cancellationToken, finalize: false);
    }

    private async Task<bool> StartWithDetectionAsync(
        Func<WindowCandidate, CancellationToken, Task<bool>> start,
        CancellationToken cancellationToken)
    {
        if (detectCandidate is null)
        {
            throw new InvalidOperationException("DDR GP自動検出が設定されていません。");
        }

        await operationGate.WaitAsync(cancellationToken);
        var operation = BeginOperation(cancellationToken);
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            if (IsShutdownRequested()
                || captureState() is CaptureLifecycleState.Starting
                    or CaptureLifecycleState.Capturing
                    or CaptureLifecycleState.Stopping)
            {
                return false;
            }

            var candidate = await detectCandidate(operation.Token);
            if (candidate is null)
            {
                return false;
            }
            operation.Token.ThrowIfCancellationRequested();
            return await start(candidate, operation.Token);
        }
        finally
        {
            EndOperation(operation);
            operationGate.Release();
        }
    }

    private async Task StopCoreAsync(
        CancellationToken cancellationToken,
        bool finalize)
    {
        RequestStop();
        try
        {
            await operationGate.WaitAsync(cancellationToken);
        }
        catch
        {
            CompleteStopRequest();
            throw;
        }
        try
        {
            await StopResourcesAsync(finalize);
        }
        finally
        {
            CompleteStopRequest();
            operationGate.Release();
        }
    }

    private async Task<bool> StartCoreAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken)
    {
        var observationOwned = false;
        var captureOwned = false;
        try
        {
            await startObservation(candidate, cancellationToken);
            observationOwned = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (!await startCapture(candidate, cancellationToken))
            {
                await StopObservationIfOwnedAsync(observationOwned);
                return false;
            }
            captureOwned = true;
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch
        {
            await CleanupOwnedResourcesAsync(captureOwned, observationOwned);
            throw;
        }
    }

    private async Task<bool> ResumeCoreAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken)
    {
        var observationOwned = false;
        var captureOwned = false;
        var completed = false;
        try
        {
            await resumeObservation(candidate, cancellationToken);
            observationOwned = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (!await startCapture(candidate, cancellationToken))
            {
                await StopObservationIfOwnedAsync(observationOwned);
                completed = true;
                return false;
            }
            captureOwned = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (captureState() is CaptureLifecycleState.Stopped
                or CaptureLifecycleState.Failed)
            {
                throw new InvalidOperationException(
                    "capture ended while the observation checkpoint was being resumed");
            }
            completed = true;
            return true;
        }
        finally
        {
            if (!completed)
            {
                await CleanupOwnedResourcesAsync(captureOwned, observationOwned);
            }
        }
    }

    private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (operationSync)
        {
            operationCancellation = operation;
            if (stopRequestCount > 0)
            {
                operation.Cancel();
            }
        }
        return operation;
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        lock (operationSync)
        {
            if (ReferenceEquals(operationCancellation, operation))
            {
                operationCancellation = null;
            }
            operation.Dispose();
        }
    }

    private void RequestStop()
    {
        lock (operationSync)
        {
            stopRequestCount++;
            operationCancellation?.Cancel();
        }
    }

    private void CompleteStopRequest()
    {
        lock (operationSync)
        {
            stopRequestCount--;
        }
    }

    private bool IsShutdownRequested()
    {
        lock (operationSync)
        {
            return shutdownRequested;
        }
    }

    private Task StopObservationIfOwnedAsync(bool observationOwned)
    {
        if (!observationOwned)
        {
            return Task.CompletedTask;
        }
        return stopObservation(CancellationToken.None);
    }

    private async Task CleanupOwnedResourcesAsync(
        bool captureOwned,
        bool observationOwned)
    {
        try
        {
            if (captureOwned)
            {
                await stopCapture();
            }
        }
        finally
        {
            await StopObservationIfOwnedAsync(observationOwned);
        }
    }

    private async Task StopResourcesAsync(bool finalize)
    {
        Exception? captureStopException = null;
        try
        {
            await stopCapture();
        }
        catch (Exception exception)
        {
            captureStopException = exception;
        }
        finally
        {
            await stopObservation(CancellationToken.None);
        }
        if (captureStopException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                captureStopException).Throw();
        }
        if (finalize && finalizeObservation is not null)
        {
            await finalizeObservation(CancellationToken.None);
        }
    }
}
