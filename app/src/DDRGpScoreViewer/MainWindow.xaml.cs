using System.Windows;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.ViewModels;
using Microsoft.Win32;

namespace DDRGpScoreViewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new(new ScoreViewerRepository());

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
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
