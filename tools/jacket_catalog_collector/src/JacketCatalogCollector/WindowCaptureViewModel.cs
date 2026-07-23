using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JacketCatalogCollector;

public sealed class WindowCaptureViewModel : INotifyPropertyChanged
{
    private readonly IWindowEnumerator windowEnumerator;
    private readonly WindowCaptureCoordinator coordinator;
    private WindowCandidate? detectedCandidate;
    private CaptureLifecycleSnapshot lifecycle = CaptureLifecycleSnapshot.Idle;
    private string candidateStatus = "DDR GP未検出です。収集開始で自動検出します。";
    private BitmapImage? preview;

    public WindowCaptureViewModel(
        IWindowEnumerator windowEnumerator,
        WindowCaptureCoordinator coordinator)
    {
        this.windowEnumerator = windowEnumerator;
        this.coordinator = coordinator;
        coordinator.SnapshotChanged += (_, value) =>
        {
            Lifecycle = value;
            Preview = ToBitmap(value.LatestPreviewPng);
            LifecycleChanged?.Invoke(this, value);
        };
        coordinator.FrameReceived += (_, frame) => FrameReceived?.Invoke(this, frame);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<RawCaptureFrame>? FrameReceived;
    public event EventHandler<CaptureLifecycleSnapshot>? LifecycleChanged;

    public CaptureLifecycleSnapshot Lifecycle
    {
        get => lifecycle;
        private set
        {
            if (SetField(ref lifecycle, value))
            {
                OnPropertyChanged(nameof(CaptureCounts));
                OnPropertyChanged(nameof(CaptureSizes));
                OnPropertyChanged(nameof(CaptureState));
                OnPropertyChanged(nameof(ConnectionDisplay));
            }
        }
    }

    public string CandidateStatus
    {
        get => candidateStatus;
        private set => SetField(ref candidateStatus, value);
    }

    public BitmapImage? Preview
    {
        get => preview;
        private set => SetField(ref preview, value);
    }

    public string TargetDisplay => detectedCandidate is null
        ? "DDR GP 未検出"
        : $"DDR GP（{TargetDetails(detectedCandidate)}）";
    public string ConnectionDisplay
    {
        get
        {
            if (detectedCandidate is null)
            {
                return "DDR GP 未接続";
            }
            var details = TargetDetails(detectedCandidate);
            return Lifecycle.State switch
            {
                CaptureLifecycleState.Starting => $"DDR GP 接続中（{details}）",
                CaptureLifecycleState.Capturing => $"DDR GP 接続中（{details}）",
                CaptureLifecycleState.Stopping => $"DDR GP 停止中（{details}）",
                CaptureLifecycleState.Failed => $"DDR GP 接続停止（{Lifecycle.Message}）",
                CaptureLifecycleState.Stopped => $"DDR GP 未接続（停止済み / {details}）",
                _ => $"DDR GP 検出済み（{details}）",
            };
        }
    }
    public string CaptureState =>
        $"{Lifecycle.State} / end={Lifecycle.EndReason} / resource={Lifecycle.ResourceState}";
    public string CaptureCounts =>
        $"captured={Lifecycle.CapturedCount} / dropped={Lifecycle.DroppedCount}";
    public string CaptureSizes =>
        $"client start={Lifecycle.StartWidth}x{Lifecycle.StartHeight} / latest frame={Lifecycle.CurrentWidth}x{Lifecycle.CurrentHeight}";

    public async Task<WindowCandidate?> DetectDdrGpAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await windowEnumerator.EnumerateAsync(cancellationToken);
        var candidates = items
            .Where(item => NativeWindowEnumerator.IsDdrGpTarget(item.Identity))
            .ToList();
        if (candidates.Count == 0)
        {
            SetDetectedCandidate(null);
            CandidateStatus = "DDR GPのウィンドウが見つかりません。ゲームを起動してから再度実行してください。";
            return null;
        }
        if (candidates.Count > 1)
        {
            SetDetectedCandidate(null);
            CandidateStatus = "DDR GPのウィンドウが複数あるため開始できません。";
            return null;
        }

        var candidate = candidates[0];
        SetDetectedCandidate(candidate);
        CandidateStatus = $"DDR GPを検出しました（{TargetDetails(candidate)}）。";
        return candidate;
    }

    public Task<bool> StartAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default) =>
        coordinator.StartAsync(candidate, cancellationToken);

    public Task StopAsync() => coordinator.StopAsync();

    private void SetDetectedCandidate(WindowCandidate? value)
    {
        if (ReferenceEquals(detectedCandidate, value))
        {
            return;
        }
        detectedCandidate = value;
        Preview = ToBitmap(value?.PreviewPng);
        OnPropertyChanged(nameof(TargetDisplay));
        OnPropertyChanged(nameof(ConnectionDisplay));
    }

    private static string TargetDetails(WindowCandidate candidate) =>
        $"{candidate.Identity.ProcessName} / {candidate.Identity.ClientWidth}×{candidate.Identity.ClientHeight}";

    private static BitmapImage? ToBitmap(byte[]? pngBytes)
    {
        if (pngBytes is null || pngBytes.Length == 0)
        {
            return null;
        }
        try
        {
            using var stream = new MemoryStream(pngBytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class WpfCaptureDispatcher(Dispatcher dispatcher) : ICaptureDispatcher
{
    public Task InvokeAsync(Action action) => dispatcher.InvokeAsync(action).Task;
}
