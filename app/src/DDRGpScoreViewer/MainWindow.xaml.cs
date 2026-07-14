using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.ViewModels;
using Microsoft.Win32;

namespace DDRGpScoreViewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool closeAfterCaptureStop;

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
            Title = "認識と再表示に使う生成済み楽曲データを選択",
            Filter = "SQLite database (*.sqlite;*.sqlite3;*.db)|*.sqlite;*.sqlite3;*.db|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (masterDialog.ShowDialog(this) != true)
        {
            return;
        }
        await viewModel.StartContinuousCaptureAndSaveAsync(
            new WindowInteropHelper(this).EnsureHandle(),
            scoreDialog.FileName,
            masterDialog.FileName);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (viewModel.IsContinuousCapturing && !closeAfterCaptureStop)
        {
            e.Cancel = true;
            await viewModel.StopContinuousCaptureAsync();
            closeAfterCaptureStop = true;
            Close();
            return;
        }
        base.OnClosing(e);
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
