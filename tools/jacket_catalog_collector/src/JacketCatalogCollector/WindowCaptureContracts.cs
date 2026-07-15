using System.Collections.ObjectModel;

namespace JacketCatalogCollector;

public sealed record WindowIdentitySnapshot(
    nint Handle,
    int ProcessId,
    long ProcessStartTicks,
    string ProcessName,
    string Title,
    string ClassName,
    int ClientWidth,
    int ClientHeight,
    bool IsVisible,
    bool IsMinimized)
{
    public bool IsSameTarget(WindowIdentitySnapshot other) =>
        Handle == other.Handle
        && ProcessId == other.ProcessId
        && ProcessStartTicks == other.ProcessStartTicks
        && string.Equals(ProcessName, other.ProcessName, StringComparison.Ordinal)
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
        && ClientWidth == other.ClientWidth
        && ClientHeight == other.ClientHeight
        && IsVisible == other.IsVisible
        && IsMinimized == other.IsMinimized;
}

public sealed record WindowCandidate(
    WindowIdentitySnapshot Identity,
    string CandidateReason,
    byte[]? PreviewPng)
{
    public string DisplayName =>
        $"0x{Identity.Handle:X} / {Identity.ProcessName} ({Identity.ProcessId}) / "
        + $"{Identity.ClientWidth}x{Identity.ClientHeight} / {Identity.Title}";
}

public enum CaptureLifecycleState
{
    Idle,
    Starting,
    Capturing,
    Stopping,
    Stopped,
    Failed,
}

public enum CaptureEndReason
{
    None,
    ExplicitStop,
    Cancelled,
    TargetClosed,
    Resized,
    IdentityChanged,
    Minimized,
    DeviceLost,
    CaptureFailed,
}

public sealed record RawCaptureFrame(
    byte[] PngBytes,
    int Width,
    int Height,
    long Sequence,
    DateTimeOffset CapturedAtUtc);

public sealed record CaptureLifecycleSnapshot(
    CaptureLifecycleState State,
    CaptureEndReason EndReason,
    long CapturedCount,
    long DroppedCount,
    int StartWidth,
    int StartHeight,
    int CurrentWidth,
    int CurrentHeight,
    string ResourceState,
    byte[]? LatestPreviewPng,
    string Message)
{
    public static CaptureLifecycleSnapshot Idle { get; } = new(
        CaptureLifecycleState.Idle,
        CaptureEndReason.None,
        0,
        0,
        0,
        0,
        0,
        0,
        "not_created",
        null,
        "候補を更新し、previewと根拠を確認して明示選択してください。");
}

public interface IWindowEnumerator
{
    Task<IReadOnlyList<WindowCandidate>> EnumerateAsync(CancellationToken cancellationToken = default);

    WindowIdentitySnapshot? TryGetSnapshot(nint handle);
}

public interface IWindowCaptureSessionFactory
{
    bool IsSupported { get; }

    Task<IWindowCaptureFrameSource> StartAsync(
        WindowIdentitySnapshot target,
        CancellationToken cancellationToken = default);
}

public interface IWindowCaptureFrameSource : IAsyncDisposable
{
    long DroppedCount { get; }

    IAsyncEnumerable<RawCaptureFrame> ReadFramesAsync(
        CancellationToken cancellationToken = default);

    Task<CaptureEndReason> Completion { get; }

    Task StopAsync();
}

public interface ICaptureDispatcher
{
    Task InvokeAsync(Action action);
}

public sealed class ImmediateCaptureDispatcher : ICaptureDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

public sealed class InFlightCallbackDrain
{
    private readonly Lock sync = new();
    private TaskCompletionSource? drained;
    private bool closing;
    private int activeCount;

    public bool TryEnter()
    {
        lock (sync)
        {
            if (closing)
            {
                return false;
            }
            activeCount++;
            return true;
        }
    }

    public void Exit()
    {
        TaskCompletionSource? completion = null;
        lock (sync)
        {
            if (activeCount <= 0)
            {
                throw new InvalidOperationException("No callback is active.");
            }
            activeCount--;
            if (closing && activeCount == 0)
            {
                completion = drained;
            }
        }
        completion?.TrySetResult();
    }

    public Task CloseAndWaitAsync()
    {
        lock (sync)
        {
            closing = true;
            if (activeCount == 0)
            {
                return Task.CompletedTask;
            }
            drained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return drained.Task;
        }
    }
}

public sealed class BoundedFrameRingBuffer
{
    private readonly Queue<RawCaptureFrame> frames;

    public BoundedFrameRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        Capacity = capacity;
        frames = new Queue<RawCaptureFrame>(capacity);
    }

    public int Capacity { get; }
    public long DroppedCount { get; private set; }
    public int Count => frames.Count;
    public RawCaptureFrame? Latest => frames.Count == 0 ? null : frames.Last();
    public ReadOnlyCollection<RawCaptureFrame> Snapshot => frames.ToList().AsReadOnly();

    public void Add(RawCaptureFrame frame)
    {
        if (frames.Count == Capacity)
        {
            frames.Dequeue();
            DroppedCount++;
        }
        frames.Enqueue(frame);
    }
}
