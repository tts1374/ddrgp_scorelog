using System.Collections.ObjectModel;
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
    private WindowCandidate? selectedCandidate;
    private CaptureLifecycleSnapshot lifecycle = CaptureLifecycleSnapshot.Idle;
    private string candidateStatus = "候補は未取得です。";
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

    public ObservableCollection<WindowCandidate> Candidates { get; } = [];

    public WindowCandidate? SelectedCandidate
    {
        get => selectedCandidate;
        set
        {
            if (SetField(ref selectedCandidate, value))
            {
                Preview = ToBitmap(value?.PreviewPng);
                OnPropertyChanged(nameof(SelectedIdentity));
                OnPropertyChanged(nameof(SelectedReason));
            }
        }
    }

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

    public string SelectedIdentity => SelectedCandidate?.DisplayName ?? "未選択";
    public string SelectedReason => SelectedCandidate?.CandidateReason ?? "—";
    public string CaptureState =>
        $"{Lifecycle.State} / end={Lifecycle.EndReason} / resource={Lifecycle.ResourceState}";
    public string CaptureCounts =>
        $"captured={Lifecycle.CapturedCount} / dropped={Lifecycle.DroppedCount}";
    public string CaptureSizes =>
        $"client start={Lifecycle.StartWidth}x{Lifecycle.StartHeight} / latest frame={Lifecycle.CurrentWidth}x{Lifecycle.CurrentHeight}";

    public async Task RefreshCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var items = await windowEnumerator.EnumerateAsync(cancellationToken);
        Candidates.Clear();
        SelectedCandidate = null;
        foreach (var item in items)
        {
            Candidates.Add(item);
        }
        CandidateStatus = items.Count == 0
            ? "DDR GRAND PRIX候補は0件です。capture resourceは作成していません。"
            : $"候補{items.Count}件。previewと根拠を確認し、1件を明示選択してください。自動開始しません。";
    }

    public Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        var selected = SelectedCandidate
            ?? throw new InvalidOperationException("previewと根拠を確認した候補を明示選択してください。");
        return StartAsync(selected, cancellationToken);
    }

    public Task<bool> StartAsync(
        WindowCandidate candidate,
        CancellationToken cancellationToken = default) =>
        coordinator.StartAsync(candidate, cancellationToken);

    public Task StopAsync() => coordinator.StopAsync();

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
