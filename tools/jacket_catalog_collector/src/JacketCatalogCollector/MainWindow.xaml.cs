using System.Windows;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32;

namespace JacketCatalogCollector;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly CaptureObservationController captureObservationController;
    private readonly ITitleArtistEvaluationService titleArtistEvaluationService;
    private readonly string repositoryRoot;
    private readonly string evidenceRoot;
    private bool titleArtistEvaluationBusy;
    private CancellationTokenSource? operationCancellation;
    private bool captureShutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        repositoryRoot = Directory.GetCurrentDirectory();
        var runner = new ProcessRunner();
        var windowEnumerator = new NativeWindowEnumerator();
        var dispatcher = new WpfCaptureDispatcher(Dispatcher);
        var captureCoordinator = new WindowCaptureCoordinator(
            windowEnumerator,
            new WindowsGraphicsCaptureSessionFactory(),
            dispatcher);
        var windowCapture = new WindowCaptureViewModel(windowEnumerator, captureCoordinator);
        evidenceRoot = Path.Combine(repositoryRoot, "data", "jacket_catalog_collector");
        var observationSession = new JacketObservationSession(
            new JacketObservationDetector(),
            new AtomicObservationCheckpointStore(evidenceRoot),
            new AtomicObservationArtifactPublisher(evidenceRoot),
            new PythonCatalogObservationAdapter(runner, repositoryRoot));
        viewModel = new MainViewModel(
            new MasterUpdateService(runner, new AtomicMasterPublisher(), repositoryRoot),
            new ProjectionService(runner, new ProjectionJsonLoader(), repositoryRoot),
            new ReviewWorkflowService(runner, repositoryRoot),
            windowCapture,
            new JacketObservationViewModel(windowCapture, observationSession, dispatcher));
        captureObservationController = new CaptureObservationController(
            viewModel.StartObservationSessionAsync,
            viewModel.ResumeObservationSessionAsync,
            viewModel.StopObservationSessionAsync,
            windowCapture.StartAsync,
            windowCapture.StopAsync,
            () => windowCapture.Lifecycle.State);
        titleArtistEvaluationService = new TitleArtistEvaluationService(runner, repositoryRoot);
        DataContext = viewModel;
    }

    private async void RefreshWindows_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await viewModel.WindowCapture!.RefreshCandidatesAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "window候補取得失敗");
        }
    }

    private async void StartCapture_Click(object sender, RoutedEventArgs e)
    {
        var capture = viewModel.WindowCapture!;
        var candidate = capture.SelectedCandidate;
        if (candidate is null)
        {
            MessageBox.Show(this, "候補のpreviewと根拠を確認して1件を明示選択してください。");
            return;
        }
        var identity = candidate.Identity;
        if (MessageBox.Show(
            this,
            $"次のwindowだけをcaptureします。自動再選択・disk保存は行いません。\n\n"
            + $"{candidate.DisplayName}\nreason: {candidate.CandidateReason}\n"
            + $"process start: {identity.ProcessStartTicks}\nclass: {identity.ClassName}",
            "capture開始確認",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        try
        {
            if (!await captureObservationController.StartAsync(candidate))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "capture開始失敗");
        }
    }

    private async void StopCapture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await captureObservationController.StopAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "capture停止失敗");
        }
    }

    private async void ResumeCapture_Click(object sender, RoutedEventArgs e)
    {
        var capture = viewModel.WindowCapture!;
        var candidate = capture.SelectedCandidate;
        if (candidate is null)
        {
            MessageBox.Show(this, "再開するwindow候補を明示選択してください。");
            return;
        }
        try
        {
            if (!await captureObservationController.ResumeAsync(candidate))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "session再開失敗");
        }
    }

    private async void AdoptObservation_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Observation is null || !viewModel.Observation.CanAdopt)
        {
            return;
        }
        try
        {
            await viewModel.Observation.AdoptAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "観測採用失敗");
        }
    }

    private async void RetryCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Observation is null)
        {
            return;
        }
        try
        {
            await viewModel.Observation.RetryCatalogAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "catalog retry失敗");
        }
    }

    private async void EvaluateTitleArtist_Click(object sender, RoutedEventArgs e)
    {
        if (titleArtistEvaluationBusy)
        {
            return;
        }
        if (viewModel.CurrentMasterPath is null || viewModel.CurrentCatalogPath is null)
        {
            MessageBox.Show(this, "masterとcatalogを先にread-only読込してください。");
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "local title/artist evaluation datasetを選択",
            Filter = "JSON (*.json)|*.json",
            InitialDirectory = Path.Combine(repositoryRoot, "data"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        var output = Path.Combine(evidenceRoot, "title-artist-evaluation");
        titleArtistEvaluationBusy = true;
        try
        {
            var receipt = await titleArtistEvaluationService.RunAsync(
                dialog.FileName,
                evidenceRoot,
                viewModel.CurrentMasterPath,
                viewModel.CurrentCatalogPath,
                output);
            var methods = string.Join(
                "\n",
                receipt.Methods.Select(method =>
                    $"{method.MethodVersion}: {method.AdoptionStatus}, "
                    + $"evaluated={method.EvaluatedCount}, "
                    + $"known false={method.KnownFalseAutoConfirmCount}, "
                    + $"reasons={string.Join('/', method.AdoptionFailureReasons)}"));
            MessageBox.Show(
                this,
                $"report: {receipt.ReportDirectory}\n"
                + $"adopted: {string.Join(", ", receipt.AdoptedMethods.DefaultIfEmpty("none"))}\n\n"
                + methods,
                "title/artist評価完了");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "title/artist評価失敗");
        }
        finally
        {
            titleArtistEvaluationBusy = false;
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!captureShutdownComplete)
        {
            e.Cancel = true;
            try
            {
                await captureObservationController.StopAsync();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "collector終了時のcheckpoint保存失敗");
            }
            finally
            {
                captureShutdownComplete = true;
                Close();
            }
            return;
        }
        base.OnClosing(e);
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

    private async void MigrateCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsBusy)
        {
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "新規v2 jacket catalogの保存先",
            Filter = "SQLite database (*.sqlite)|*.sqlite|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = ".sqlite",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true
            || MessageBox.Show(
                this,
                "v1 catalogは変更せず、選択した別pathへv2を作成します。実行しますか？",
                "catalog v2 migration",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }
        await RunOperationAsync(token => viewModel.MigrateCatalogAsync(dialog.FileName, token));
    }

    private async void ReviewAction_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsBusy || sender is not FrameworkElement { Tag: string action })
        {
            return;
        }
        var reference = viewModel.SelectedReference;
        if (reference is null)
        {
            MessageBox.Show(this, "review対象referenceを選択してください。");
            return;
        }
        var songText = viewModel.SelectedSong is null
            ? "song変更なし"
            : $"song={viewModel.SelectedSong.Title} ({viewModel.SelectedSong.SongId})";
        if (MessageBox.Show(
            this,
            $"{action} / reference={reference.ReferenceId} / revision={reference.Revision} / {songText}",
            "manual review確認",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        await RunOperationAsync(token => viewModel.ApplyReviewAsync(action, token));
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        try
        {
            operationCancellation = new CancellationTokenSource();
            await operation(operationCancellation.Token);
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
}
