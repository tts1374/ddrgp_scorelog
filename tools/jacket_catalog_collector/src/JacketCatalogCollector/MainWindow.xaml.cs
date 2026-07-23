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
    private readonly ProjectionService candidateProjectionService;
    private readonly string repositoryRoot;
    private readonly string evidenceRoot;
    private bool titleArtistEvaluationBusy;
    private CancellationTokenSource? operationCancellation;
    private bool captureShutdownComplete;
    private bool startupInitializationStarted;

    public MainWindow()
    {
        InitializeComponent();
        var databasePaths = CollectorDatabasePaths.Resolve();
        repositoryRoot = databasePaths.RepositoryRoot;
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
        candidateProjectionService = new ProjectionService(
            runner,
            new ProjectionJsonLoader(),
            repositoryRoot,
            artifactRoot: evidenceRoot);
        viewModel = new MainViewModel(
            new MasterUpdateService(runner, new AtomicMasterPublisher(), repositoryRoot),
            candidateProjectionService,
            new ReviewWorkflowService(runner, repositoryRoot),
            windowCapture,
            new JacketObservationViewModel(
                windowCapture,
                observationSession,
                dispatcher,
                new InformationTitleLineDetector(),
                new PythonCollectionAutoConfirmationService(
                    runner,
                    repositoryRoot,
                    evidenceRoot,
                    databasePaths.MasterPath,
                    databasePaths.CatalogPath)),
            databasePaths: databasePaths,
            catalogInitializationService: new CatalogInitializationService(
                runner,
                databasePaths.RepositoryRoot,
                databasePaths.CatalogPath),
            manualReviewDraftStore: new JsonManualReviewDraftStore(
                Path.Combine(evidenceRoot, "manual-review-drafts.v1.json")));
        captureObservationController = new CaptureObservationController(
            viewModel.StartObservationSessionAsync,
            viewModel.ResumeObservationSessionAsync,
            viewModel.StopObservationSessionAsync,
            windowCapture.StartAsync,
            windowCapture.StopAsync,
            () => windowCapture.Lifecycle.State,
            viewModel.FinalizeObservationSessionAsync,
            windowCapture.DetectDdrGpAsync);
        titleArtistEvaluationService = new TitleArtistEvaluationService(runner, repositoryRoot);
        DataContext = viewModel;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (startupInitializationStarted || operationCancellation is not null)
        {
            return;
        }
        startupInitializationStarted = true;
        try
        {
            operationCancellation = new CancellationTokenSource();
            await viewModel.InitializeDatabasesAsync(operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // The ViewModel exposes the actionable diagnostic in the status panel.
        }
        finally
        {
            operationCancellation?.Dispose();
            operationCancellation = null;
        }
    }

    private async void StartCapture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await captureObservationController.StartAsync())
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Window closing cancels detection/start without showing a late dialog.
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
        try
        {
            if (!await captureObservationController.ResumeAsync())
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Window closing cancels detection/resume without showing a late dialog.
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
            await viewModel.RetryCatalogSessionAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "catalog retry失敗");
        }
    }

    private async void EvaluateTitleArtist_Click(object sender, RoutedEventArgs e)
    {
        if (titleArtistEvaluationBusy || viewModel.IsBusy || operationCancellation is not null)
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
            operationCancellation = new CancellationTokenSource();
            var receipt = await titleArtistEvaluationService.RunAsync(
                dialog.FileName,
                evidenceRoot,
                viewModel.CurrentMasterPath,
                viewModel.CurrentCatalogPath,
                output,
                operationCancellation.Token);
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
        catch (OperationCanceledException)
        {
            MessageBox.Show(this, "title/artist評価を取り消しました。", "title/artist評価取消");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "title/artist評価失敗");
        }
        finally
        {
            operationCancellation?.Dispose();
            operationCancellation = null;
            titleArtistEvaluationBusy = false;
        }
    }

    private async void RefreshCandidates_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsBusy || operationCancellation is not null)
        {
            return;
        }
        if (viewModel.CurrentMasterPath is null || viewModel.CurrentCatalogPath is null)
        {
            MessageBox.Show(this, "masterとcatalogを先にread-only読込してください。");
            return;
        }
        await RunOperationAsync(viewModel.LoadProjectionAsync);
    }

    private async void GenerateCandidateReport_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsBusy || operationCancellation is not null)
        {
            return;
        }
        if (viewModel.CurrentMasterPath is null || viewModel.CurrentCatalogPath is null)
        {
            MessageBox.Show(this, "masterとcatalogを先にread-only読込してください。");
            return;
        }
        var output = Path.Combine(evidenceRoot, "unresolved-candidate-evaluation");
        try
        {
            operationCancellation = new CancellationTokenSource();
            await candidateProjectionService.GenerateReportAsync(
                viewModel.CurrentMasterPath,
                viewModel.CurrentCatalogPath,
                output,
                operationCancellation.Token);
            MessageBox.Show(this, $"candidate report: {output}", "candidate report完了");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "candidate report失敗");
        }
        finally
        {
            operationCancellation?.Dispose();
            operationCancellation = null;
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!captureShutdownComplete)
        {
            e.Cancel = true;
            try
            {
                await captureObservationController.AbortAsync();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "collector終了時の安全停止失敗");
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
        try
        {
            operationCancellation = new CancellationTokenSource();
            await viewModel.UpdateMasterAsync(operationCancellation.Token);
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

    private async void ApplyDrafts_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsBusy)
        {
            return;
        }
        await RunOperationAsync(token => viewModel.ApplyDraftsAsync(token));
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
