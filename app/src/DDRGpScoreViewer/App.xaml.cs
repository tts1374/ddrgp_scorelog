using System.Windows;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Diagnostics;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Tray;
using DDRGpScoreViewer.ViewModels;

namespace DDRGpScoreViewer;

public partial class App : System.Windows.Application
{
    private MainWindow? mainWindow;
    private ApplicationLifecycleCoordinator? lifecycle;
    private SingleInstanceCoordinator? singleInstance;
    private ReleaseLog? releaseLog;
    private PropertyChangedEventHandler? viewModelPropertyChanged;

    public App()
    {
        SQLitePCL.Batteries_V2.Init();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
            releaseLog?.Error("unhandled_dispatcher_exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                releaseLog?.Error("unhandled_process_exception", exception);
            }
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        singleInstance = SingleInstanceCoordinator.Acquire();
        if (!singleInstance.IsPrimary)
        {
            Shutdown();
            return;
        }

        var paths = ViewerDatabasePaths.ResolveDefault();
        ReferenceDataSetUpdateResult? referenceDataSetResult = null;
        ReferenceDataSetManager? referenceDataSetManager = null;
        try
        {
            paths.EnsureDefaultDirectories();
            releaseLog = new ReleaseLog(paths.LogsDirectory);
            releaseLog.Information(
                "application_start",
                $"app_version={Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown"}; environment={paths.Environment}");
            TemporaryDataCleanup.Cleanup(paths);

            if (paths.Environment == ViewerDatabaseEnvironment.Production)
            {
                referenceDataSetManager = new ReferenceDataSetManager();
                var packageReferenceDirectory = Path.Combine(AppContext.BaseDirectory, "ReferenceData");
                var packageResult = referenceDataSetManager.InstallPackageDataSet(
                    packageReferenceDirectory,
                    paths);
                releaseLog.Information("reference_data_set_package", $"status={packageResult.Status}; {packageResult.Message}");
                referenceDataSetResult = packageResult;
            }

            var migrationResult = new ScoreDatabaseMigrationService().MigrateIfSupported(paths.ScoreDatabasePath);
            releaseLog.Information("score_db_migration", $"succeeded={migrationResult.Succeeded}; migrated={migrationResult.Migrated}; {migrationResult.Message}");
        }
        catch (Exception exception)
        {
            releaseLog?.Error("startup_preparation_failed", exception);
        }

        mainWindow = new MainWindow(paths);
        var trayIcon = new WindowsTrayIconService();
        lifecycle = new ApplicationLifecycleCoordinator(
            trayIcon,
            StartMonitoringAsync,
            () => mainWindow.StopMonitoringAsync(),
            ShowMainWindow,
            ShutdownApplication,
            () => mainWindow.RequestApplicationExit());
        mainWindow.SetApplicationUpdateExitHandlers(
            lifecycle.PrepareForApplicationUpdateAsync,
            lifecycle.ExitAsync,
            ShutdownApplication);
        viewModelPropertyChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.CurrentMonitoringState) or
                nameof(MainViewModel.CanStartMonitoring) or nameof(MainViewModel.CanStopMonitoring))
            {
                UpdateTrayState();
            }
            if (args.PropertyName == nameof(MainViewModel.CurrentMonitoringState))
            {
                releaseLog?.Information(
                    "monitoring_state",
                    $"state={mainWindow.ViewModel.CurrentMonitoringState}; reason={mainWindow.ViewModel.MonitoringReason}");
            }
            else if (args.PropertyName == nameof(MainViewModel.MonitoringResults))
            {
                releaseLog?.Information("save_result", mainWindow.ViewModel.MonitoringResultsDisplay);
            }
            else if (args.PropertyName == nameof(MainViewModel.ApplicationUpdateStatusMessage))
            {
                releaseLog?.Information(
                    "application_update",
                    $"title={mainWindow.ViewModel.ApplicationUpdateStatusTitle}; version={mainWindow.ViewModel.ApplicationUpdateVersion}; message={mainWindow.ViewModel.ApplicationUpdateStatusMessage}");
            }
        };
        mainWindow.ViewModel.PropertyChanged += viewModelPropertyChanged;
        await mainWindow.RestoreSavedPathsAsync();
        if (referenceDataSetResult is not null)
        {
            mainWindow.ViewModel.ApplyReferenceDataSetUpdateResult(referenceDataSetResult);
        }
        releaseLog?.Information(
            "database_validation",
            $"master={mainWindow.ViewModel.MasterDatabaseStatus}; catalog={mainWindow.ViewModel.CatalogDatabaseStatus}; score_status={mainWindow.ViewModel.StatusTitle}");
        UpdateTrayState();
        mainWindow.Show();
        if (referenceDataSetManager is not null)
        {
            mainWindow.ViewModel.SetReferenceDataUpdateInProgress(true);
            StartReferenceDataSetUpdate(
                referenceDataSetManager,
                paths,
                mainWindow.ApplicationExitToken);
        }
        _ = mainWindow.CheckForApplicationUpdateAsync(mainWindow.ApplicationExitToken);
        mainWindow.StartAutomaticMonitoring();
        singleInstance.Listen(() => Dispatcher.BeginInvoke(ShowMainWindow));
    }

    private async void StartReferenceDataSetUpdate(
        ReferenceDataSetManager referenceDataSetManager,
        ViewerDatabasePaths paths,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await referenceDataSetManager.UpdateFromGitHubReleaseAsync(
                paths,
                cancellationToken);
            releaseLog?.Information(
                "reference_data_set_network",
                $"status={result.Status}; {result.Message}");
            if (mainWindow is null || mainWindow.ViewModel.IsApplicationExitRequested)
            {
                return;
            }

            mainWindow.ViewModel.ApplyReferenceDataSetUpdateResult(result);
            if ((result.Status is ReferenceDataSetUpdateStatus.Installed or ReferenceDataSetUpdateStatus.Updated) &&
                mainWindow.ViewModel.CurrentMonitoringState is not (
                    MonitoringState.Starting or
                    MonitoringState.SelectingTarget or
                    MonitoringState.Monitoring or
                    MonitoringState.Stopping))
            {
                await mainWindow.RestoreSavedPathsAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            releaseLog?.Information("reference_data_set_network", "status=cancelled; application exit requested");
        }
        catch (Exception exception)
        {
            releaseLog?.Error("reference_data_set_network_failed", exception);
        }
        finally
        {
            mainWindow?.ViewModel.SetReferenceDataUpdateInProgress(false);
        }
    }

    private async Task StartMonitoringAsync()
    {
        ShowMainWindow();
        if (mainWindow is not null)
        {
            await mainWindow.StartMonitoringFromTrayAsync();
        }
    }

    private void ShowMainWindow()
    {
        if (mainWindow is null)
        {
            return;
        }
        mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    private void UpdateTrayState()
    {
        if (mainWindow is null || lifecycle is null)
        {
            return;
        }
        var viewModel = mainWindow.ViewModel;
        lifecycle.UpdateMonitoringState(
            viewModel.CurrentMonitoringState,
            viewModel.MonitoringStateDisplay,
            viewModel.MonitoringResults,
            viewModel.MonitoringReason,
            new TrayMenuState(viewModel.CanStartMonitoring, viewModel.CanStopMonitoring));
    }

    private void ShutdownApplication()
    {
        releaseLog?.Information("application_exit", "監視と進行中処理を停止し、明示終了します。");
        mainWindow?.PrepareForApplicationExit();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        releaseLog?.Information("windows_session_ending", $"reason={e.ReasonSessionEnding}");
        mainWindow?.RequestApplicationExit();
        _ = lifecycle?.ExitAsync();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (mainWindow is not null && viewModelPropertyChanged is not null)
        {
            mainWindow.ViewModel.PropertyChanged -= viewModelPropertyChanged;
            viewModelPropertyChanged = null;
        }
        lifecycle?.Dispose();
        singleInstance?.Dispose();
        releaseLog?.Dispose();
        base.OnExit(e);
    }
}
