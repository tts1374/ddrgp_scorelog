using System.Windows;
using System.ComponentModel;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Tray;
using DDRGpScoreViewer.ViewModels;

namespace DDRGpScoreViewer;

public partial class App : System.Windows.Application
{
    private MainWindow? mainWindow;
    private ApplicationLifecycleCoordinator? lifecycle;
    private PropertyChangedEventHandler? viewModelPropertyChanged;

    public App()
    {
        SQLitePCL.Batteries_V2.Init();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mainWindow = new MainWindow();
        var trayIcon = new WindowsTrayIconService();
        lifecycle = new ApplicationLifecycleCoordinator(
            trayIcon,
            StartMonitoringAsync,
            () => mainWindow.StopMonitoringAsync(),
            ShowMainWindow,
            ShutdownApplication,
            () => mainWindow.RequestApplicationExit());
        viewModelPropertyChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.CurrentMonitoringState) or
                nameof(MainViewModel.CanStartMonitoring) or nameof(MainViewModel.CanStopMonitoring))
            {
                UpdateTrayState();
            }
        };
        mainWindow.ViewModel.PropertyChanged += viewModelPropertyChanged;
        mainWindow.RestoreSavedPaths();
        UpdateTrayState();
        mainWindow.Show();
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
        mainWindow?.PrepareForApplicationExit();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (mainWindow is not null && viewModelPropertyChanged is not null)
        {
            mainWindow.ViewModel.PropertyChanged -= viewModelPropertyChanged;
            viewModelPropertyChanged = null;
        }
        lifecycle?.Dispose();
        base.OnExit(e);
    }
}
