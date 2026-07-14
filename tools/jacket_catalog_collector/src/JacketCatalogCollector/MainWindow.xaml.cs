using System.Windows;
using System.IO;
using Microsoft.Win32;

namespace JacketCatalogCollector;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private CancellationTokenSource? operationCancellation;

    public MainWindow()
    {
        InitializeComponent();
        var repositoryRoot = Directory.GetCurrentDirectory();
        var runner = new ProcessRunner();
        viewModel = new MainViewModel(
            new MasterUpdateService(runner, new AtomicMasterPublisher(), repositoryRoot),
            new ProjectionService(runner, new ProjectionJsonLoader(), repositoryRoot));
        DataContext = viewModel;
    }

    private async void UpdateMaster_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsBusy)
        {
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "更新先master DBを選択",
            Filter = "SQLite database (*.sqlite)|*.sqlite|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = ".sqlite",
            OverwritePrompt = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            operationCancellation = new CancellationTokenSource();
            await viewModel.UpdateMasterAsync(dialog.FileName, operationCancellation.Token);
        }
        catch (Exception)
        {
            // The ViewModel exposes the actionable diagnostic in the status panel.
        }
        finally
        {
            operationCancellation?.Dispose();
            operationCancellation = null;
        }
    }

    private async void SelectDatabases_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsBusy)
        {
            return;
        }
        var masterDialog = new OpenFileDialog
        {
            Title = "M4 master DBを選択",
            Filter = "SQLite database (*.sqlite;*.sqlite3;*.db)|*.sqlite;*.sqlite3;*.db|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (masterDialog.ShowDialog(this) != true)
        {
            return;
        }
        var catalogDialog = new OpenFileDialog
        {
            Title = "M5b jacket catalogを選択",
            Filter = "SQLite database (*.sqlite;*.sqlite3;*.db)|*.sqlite;*.sqlite3;*.db|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (catalogDialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            operationCancellation = new CancellationTokenSource();
            await viewModel.LoadProjectionAsync(
                masterDialog.FileName,
                catalogDialog.FileName,
                operationCancellation.Token);
        }
        catch (Exception)
        {
            // The ViewModel exposes the actionable diagnostic in the status panel.
        }
        finally
        {
            operationCancellation?.Dispose();
            operationCancellation = null;
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e) =>
        operationCancellation?.Cancel();
}
