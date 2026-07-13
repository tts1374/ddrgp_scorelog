using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;

namespace DDRGpScoreViewer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ScoreViewerRepository repository;
    private readonly IPersonalScoreDbWorkflowRunner workflowRunner;
    private readonly ISingleFrameCaptureService? captureService;
    private readonly IContinuousCaptureService? continuousCaptureService;
    private PlayHistoryItem? selectedPlay;
    private string statusTitle = "プレーデータを選択してください";
    private string statusMessage =
        "正式なプレーデータと生成済みの楽曲データを選ぶと、履歴と自己ベストを表示します。";
    private bool hasData;
    private string masterVersion = "—";
    private string saveStatusTitle = "";
    private string saveStatusMessage = "";
    private bool hasSaveStatus;
    private bool isSaving;
    private string captureStatusTitle = "";
    private string captureStatusMessage = "";
    private bool hasCaptureStatus;
    private bool isCapturing;
    private bool isContinuousCapturing;
    private bool isStoppingCapture;
    private TaskCompletionSource? continuousCaptureFinished;

    public MainViewModel(
        ScoreViewerRepository repository,
        IPersonalScoreDbWorkflowRunner workflowRunner,
        ISingleFrameCaptureService? captureService = null,
        IContinuousCaptureService? continuousCaptureService = null)
    {
        this.repository = repository;
        this.workflowRunner = workflowRunner;
        this.captureService = captureService;
        this.continuousCaptureService = continuousCaptureService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlayHistoryItem> Plays { get; } = [];
    public ObservableCollection<ChartBestItem> ChartBests { get; } = [];

    public PlayHistoryItem? SelectedPlay
    {
        get => selectedPlay;
        set => SetProperty(ref selectedPlay, value);
    }

    public string StatusTitle
    {
        get => statusTitle;
        private set => SetProperty(ref statusTitle, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public bool HasData
    {
        get => hasData;
        private set
        {
            if (SetProperty(ref hasData, value))
            {
                OnPropertyChanged(nameof(StatusVisibility));
                OnPropertyChanged(nameof(DataVisibility));
            }
        }
    }

    public System.Windows.Visibility StatusVisibility =>
        HasData ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public System.Windows.Visibility DataVisibility =>
        HasData ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string MasterVersion
    {
        get => masterVersion;
        private set => SetProperty(ref masterVersion, value);
    }

    public string SaveStatusTitle
    {
        get => saveStatusTitle;
        private set => SetProperty(ref saveStatusTitle, value);
    }

    public string SaveStatusMessage
    {
        get => saveStatusMessage;
        private set => SetProperty(ref saveStatusMessage, value);
    }

    public bool HasSaveStatus
    {
        get => hasSaveStatus;
        private set
        {
            if (SetProperty(ref hasSaveStatus, value))
            {
                OnPropertyChanged(nameof(SaveStatusVisibility));
            }
        }
    }

    public bool IsSaving
    {
        get => isSaving;
        private set => SetProperty(ref isSaving, value);
    }

    public System.Windows.Visibility SaveStatusVisibility =>
        HasSaveStatus ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string CaptureStatusTitle
    {
        get => captureStatusTitle;
        private set => SetProperty(ref captureStatusTitle, value);
    }

    public string CaptureStatusMessage
    {
        get => captureStatusMessage;
        private set => SetProperty(ref captureStatusMessage, value);
    }

    public bool HasCaptureStatus
    {
        get => hasCaptureStatus;
        private set
        {
            if (SetProperty(ref hasCaptureStatus, value))
            {
                OnPropertyChanged(nameof(CaptureStatusVisibility));
            }
        }
    }

    public bool IsCapturing
    {
        get => isCapturing;
        private set => SetProperty(ref isCapturing, value);
    }

    public bool IsContinuousCapturing
    {
        get => isContinuousCapturing;
        private set => SetProperty(ref isContinuousCapturing, value);
    }

    public bool IsStoppingCapture
    {
        get => isStoppingCapture;
        private set => SetProperty(ref isStoppingCapture, value);
    }

    public System.Windows.Visibility CaptureStatusVisibility =>
        HasCaptureStatus ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public async Task CaptureOneFrameAsync(nint ownerWindowHandle)
    {
        if (IsCapturing || IsContinuousCapturing)
        {
            return;
        }
        if (captureService is null)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "画面キャプチャを利用できません";
            CaptureStatusMessage = "capture serviceが構成されていません。";
            return;
        }

        IsCapturing = true;
        HasCaptureStatus = true;
        CaptureStatusTitle = "対象windowを選択してください";
        CaptureStatusMessage = "選択したwindowから1フレームだけ取得します。解析やDB保存は実行しません。";
        try
        {
            var result = await captureService.CaptureAsync(ownerWindowHandle);
            CaptureStatusTitle = result.Status switch
            {
                CaptureOperationStatus.Saved => "1フレームを保存しました",
                CaptureOperationStatus.Cancelled => "画面キャプチャをキャンセルしました",
                CaptureOperationStatus.Unsupported => "画面キャプチャを利用できません",
                CaptureOperationStatus.AccessDenied => "画面キャプチャが拒否されました",
                CaptureOperationStatus.TargetClosed => "対象windowが終了しました",
                CaptureOperationStatus.InvalidSize => "対象windowを取得できません",
                CaptureOperationStatus.Resized => "対象windowのサイズが変わりました",
                CaptureOperationStatus.DeviceLost => "GPU deviceが失われました",
                CaptureOperationStatus.WriteFailed => "キャプチャ出力に失敗しました",
                _ => "1フレーム取得に失敗しました",
            };
            CaptureStatusMessage = result.UserMessage;
        }
        finally
        {
            IsCapturing = false;
        }
    }

    public async Task StartContinuousCaptureAsync(nint ownerWindowHandle)
    {
        if (IsStoppingCapture)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "連続キャプチャを停止しています";
            CaptureStatusMessage = "停止完了後にもう一度開始してください。";
            return;
        }
        if (IsContinuousCapturing)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "連続キャプチャは開始済みです";
            CaptureStatusMessage = "現在のsessionを停止してから再選択してください。";
            return;
        }
        if (IsCapturing)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "1フレーム取得中です";
            CaptureStatusMessage = "取得完了後に連続キャプチャを開始してください。";
            return;
        }
        if (continuousCaptureService is null)
        {
            HasCaptureStatus = true;
            CaptureStatusTitle = "連続キャプチャを利用できません";
            CaptureStatusMessage = "continuous capture serviceが構成されていません。";
            return;
        }

        IsContinuousCapturing = true;
        continuousCaptureFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HasCaptureStatus = true;
        CaptureStatusTitle = "対象windowを選択してください";
        CaptureStatusMessage =
            "選択したwindowを明示停止まで取得します。解析やDB保存は実行しません。";
        try
        {
            var result = await continuousCaptureService.RunAsync(ownerWindowHandle);
            CaptureStatusTitle = result.Status switch
            {
                CaptureOperationStatus.Saved => "連続キャプチャを保存しました",
                CaptureOperationStatus.Cancelled => "連続キャプチャをキャンセルしました",
                CaptureOperationStatus.Unsupported => "画面キャプチャを利用できません",
                CaptureOperationStatus.AccessDenied => "画面キャプチャが拒否されました",
                CaptureOperationStatus.TargetClosed => "対象windowが終了しました",
                CaptureOperationStatus.InvalidSize => "対象windowを取得できません",
                CaptureOperationStatus.Resized => "対象windowのサイズが変わりました",
                CaptureOperationStatus.DeviceLost => "GPU deviceが失われました",
                CaptureOperationStatus.WriteFailed => "session outputに失敗しました",
                CaptureOperationStatus.AlreadyRunning => "連続キャプチャは開始済みです",
                _ => "連続キャプチャに失敗しました",
            };
            CaptureStatusMessage = result.UserMessage;
        }
        finally
        {
            IsContinuousCapturing = false;
            IsStoppingCapture = false;
            continuousCaptureFinished?.TrySetResult();
            continuousCaptureFinished = null;
        }
    }

    public async Task StopContinuousCaptureAsync()
    {
        if (!IsContinuousCapturing || continuousCaptureService is null)
        {
            return;
        }

        var captureFinished = continuousCaptureFinished;
        if (!IsStoppingCapture)
        {
            IsStoppingCapture = true;
            CaptureStatusTitle = "連続キャプチャを停止しています";
            CaptureStatusMessage = "取得済みフレームのmanifestを完成させて安全に公開します。";
            await continuousCaptureService.StopAsync();
        }
        if (captureFinished is not null)
        {
            await captureFinished.Task;
        }
    }

    public void Load(string scoreDatabasePath, string masterDatabasePath)
    {
        try
        {
            var data = repository.Load(scoreDatabasePath, masterDatabasePath);
            Replace(Plays, data.Plays);
            Replace(ChartBests, data.ChartBests);
            MasterVersion = data.MasterVersion;
            SelectedPlay = Plays.FirstOrDefault();
            if (Plays.Count == 0)
            {
                HasData = false;
                StatusTitle = "まだプレーデータがありません";
                StatusMessage =
                    "DDR GRAND PRIXをプレーするか、データを読み込むとここに表示されます。";
                return;
            }
            HasData = true;
        }
        catch (ViewerDatabaseException exception)
        {
            Plays.Clear();
            ChartBests.Clear();
            SelectedPlay = null;
            MasterVersion = "—";
            HasData = false;
            StatusTitle = "データを読み込めませんでした";
            StatusMessage = exception.UserMessage;
        }
    }

    public async Task SaveAndReloadAsync(
        string workflowInputPath,
        string scoreDatabasePath,
        string masterDatabasePath)
    {
        IsSaving = true;
        HasSaveStatus = true;
        SaveStatusTitle = "保存処理を実行しています";
        SaveStatusMessage = "選択したworkflow入力を既存の正式保存境界で1回だけ処理しています。";
        try
        {
            var result = await workflowRunner.RunAsync(workflowInputPath, scoreDatabasePath);
            if (result.WorkflowStatus == "saved" && result.Written && result.PlayId is not null)
            {
                var data = repository.Load(scoreDatabasePath, masterDatabasePath);
                if (!data.Plays.Any(play => play.PlayId == result.PlayId))
                {
                    SaveStatusTitle = "保存結果を確認できませんでした";
                    SaveStatusMessage = "DBへの保存結果をread-only再読込した履歴で確認できませんでした。";
                    return;
                }
                ApplyData(data);
                SaveStatusTitle = "プレーを保存しました";
                SaveStatusMessage = "正式v1 DBをread-onlyで再読込し、履歴と自己ベストへ反映しました。";
                return;
            }
            (SaveStatusTitle, SaveStatusMessage) = Present(result);
        }
        catch (ViewerDatabaseException exception)
        {
            SaveStatusTitle = "保存後の再読込に失敗しました";
            SaveStatusMessage = $"保存処理は完了しましたが、{exception.UserMessage}";
        }
        catch (Exception exception)
        {
            SaveStatusTitle = "保存workflowに失敗しました";
            SaveStatusMessage = exception.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ApplyData(ViewerData data)
    {
        Replace(Plays, data.Plays);
        Replace(ChartBests, data.ChartBests);
        MasterVersion = data.MasterVersion;
        SelectedPlay = Plays.FirstOrDefault();
        HasData = Plays.Count > 0;
    }

    private static (string Title, string Message) Present(PersonalScoreDbWorkflowResult result)
    {
        var reason = result.Reasons.Count == 0 ? "理由はありません。" : string.Join(" / ", result.Reasons);
        return result.WorkflowStatus switch
        {
            "excluded" => ("保存対象外です", $"play履歴には追加していません。{reason}"),
            "duplicate" => ("重複するプレーです", $"play履歴には追加していません。{reason}"),
            "unresolved" => ("正式保存値が未解決です", $"DBやartifactは変更していません。{reason}"),
            "invalid" => ("workflow入力が不正です", $"DBやartifactは変更していません。{reason}"),
            "db_rejected" => ("保存先DBを使用できません", $"DBは変更していません。{reason}"),
            "artifact_failed" or "artifact_conflict" =>
                ("解析artifactを保存できません", $"DBは変更していません。{reason}"),
            "artifact_created_db_failed" =>
                ("DB保存に失敗しました", $"解析artifactは作成済みですが、play保存は成功していません。{reason}"),
            _ => ("保存workflowに失敗しました", reason),
        };
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
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
