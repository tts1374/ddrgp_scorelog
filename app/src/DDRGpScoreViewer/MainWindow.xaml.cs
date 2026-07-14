using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Tray;
using DDRGpScoreViewer.ViewModels;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace DDRGpScoreViewer;

public partial class MainWindow : System.Windows.Window
{
    private readonly MainViewModel viewModel;
    private readonly AsyncOperationGate monitoringStartGate = new();
    private bool applicationExitRequested;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            new PythonPersonalScoreDbWorkflowRunner(),
            new SingleFrameCaptureService(
                new WindowsGraphicsCaptureAdapter(),
                new RepositoryCaptureOutputWriter()),
            new ContinuousCaptureService(
                new ContinuousWindowsGraphicsCaptureAdapter(),
                new RepositoryCaptureSessionOutputWriter()),
            new PythonCaptureSaveWorkflowRunner());
        DataContext = viewModel;
    }

    internal MainViewModel ViewModel => viewModel;

    private async void StartContinuousCapture_Click(object sender, RoutedEventArgs e)
    {
        await viewModel.StartContinuousCaptureAsync(new WindowInteropHelper(this).EnsureHandle());
    }

    private async void StopContinuousCapture_Click(object sender, RoutedEventArgs e)
    {
        await viewModel.StopContinuousCaptureAsync();
    }

    private async void StartContinuousCaptureAndSave_Click(object sender, RoutedEventArgs e)
    {
        await StartMonitoringAsync();
    }

    internal Task StartMonitoringFromTrayAsync() => StartMonitoringAsync();

    private Task StartMonitoringAsync() => monitoringStartGate.RunAsync(StartMonitoringCoreAsync);

    private async Task StartMonitoringCoreAsync(CancellationToken cancellationToken)
    {
        if (viewModel.IsSaving || cancellationToken.IsCancellationRequested)
        {
            return;
        }
        viewModel.SetMonitoringStartPending(true);
        try
        {
            var scoreDialog = new SaveFileDialog
            {
                Title = "保存先の正式v1プレーデータを選択",
                Filter = "SQLite database (*.sqlite)|*.sqlite|All files (*.*)|*.*",
                AddExtension = true,
                DefaultExt = ".sqlite",
                OverwritePrompt = false,
                CreatePrompt = false,
            };
            if (scoreDialog.ShowDialog(this) != true || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            var masterDialog = new OpenFileDialog
            {
                Title = "認識と再表示に使う生成済み楽曲データを選択",
                Filter = "SQLite database (*.sqlite;*.sqlite3;*.db)|*.sqlite;*.sqlite3;*.db|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (masterDialog.ShowDialog(this) != true || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            await viewModel.StartContinuousCaptureAndSaveAsync(
                new WindowInteropHelper(this).EnsureHandle(),
                scoreDialog.FileName,
                masterDialog.FileName);
        }
        finally
        {
            viewModel.SetMonitoringStartPending(false);
        }
    }

    internal async Task StopMonitoringAsync()
    {
        monitoringStartGate.Cancel();
        await viewModel.StopContinuousCaptureAsync();
        await monitoringStartGate.WaitAsync();
    }

    internal void PrepareForApplicationExit()
    {
        applicationExitRequested = true;
        monitoringStartGate.Dispose();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (WindowLifecyclePolicy.HideOnClose(applicationExitRequested))
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized &&
            WindowLifecyclePolicy.HideOnMinimize(applicationExitRequested))
        {
            Hide();
        }
    }

    private async void CaptureOneFrame_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsCapturing)
        {
            return;
        }
        await viewModel.CaptureOneFrameAsync(new WindowInteropHelper(this).EnsureHandle());
    }

    private async void SaveOnePlay_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsSaving)
        {
            return;
        }
        var workflowDialog = new OpenFileDialog
        {
            Title = "正式保存workflow入力JSONを選択",
            Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (workflowDialog.ShowDialog(this) != true)
        {
            return;
        }
        var scoreDialog = new SaveFileDialog
        {
            Title = "保存先の正式v1プレーデータを選択",
            Filter = "SQLite database (*.sqlite)|*.sqlite|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = ".sqlite",
            OverwritePrompt = false,
            CreatePrompt = false,
        };
        if (scoreDialog.ShowDialog(this) != true)
        {
            return;
        }
        var masterDialog = new OpenFileDialog
        {
            Title = "再表示に使う生成済み楽曲データを選択",
            Filter = "SQLite database (*.sqlite;*.sqlite3;*.db)|*.sqlite;*.sqlite3;*.db|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (masterDialog.ShowDialog(this) != true)
        {
            return;
        }
        await viewModel.SaveAndReloadAsync(
            workflowDialog.FileName,
            scoreDialog.FileName,
            masterDialog.FileName);
    }

    private void SelectDatabases_Click(object sender, RoutedEventArgs e)
    {
        var scoreDialog = new OpenFileDialog
        {
            Title = "正式なプレーデータを選択",
            Filter = "SQLite database (*.sqlite;*.sqlite3;*.db)|*.sqlite;*.sqlite3;*.db|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (scoreDialog.ShowDialog(this) != true)
        {
            return;
        }

        var masterDialog = new OpenFileDialog
        {
            Title = "生成済みの楽曲データを選択",
            Filter = "SQLite database (*.sqlite;*.sqlite3;*.db)|*.sqlite;*.sqlite3;*.db|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (masterDialog.ShowDialog(this) != true)
        {
            return;
        }

        viewModel.Load(scoreDialog.FileName, masterDialog.FileName);
    }

    private void ShowBest_Click(object sender, RoutedEventArgs e)
    {
        ContentTabs.SelectedIndex = 0;
        PageTitle.Text = "自己ベスト";
        BestNavigation.Tag = "Selected";
        HistoryNavigation.Tag = null;
    }

    private void ShowHistory_Click(object sender, RoutedEventArgs e)
    {
        ContentTabs.SelectedIndex = 1;
        PageTitle.Text = "プレー履歴";
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = "Selected";
    }
}
