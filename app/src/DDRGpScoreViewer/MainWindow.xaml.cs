using System.Windows;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.ViewModels;
using Microsoft.Win32;

namespace DDRGpScoreViewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new(
        new ScoreViewerRepository(),
        new PythonPersonalScoreDbWorkflowRunner());

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
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
