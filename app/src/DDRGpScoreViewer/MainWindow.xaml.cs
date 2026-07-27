using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Tray;
using DDRGpScoreViewer.ViewModels;
using Microsoft.Win32;
using WpfFileDialog = Microsoft.Win32.FileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DDRGpScoreViewer;

public partial class MainWindow : System.Windows.Window
{
    private readonly MainViewModel viewModel;
    private readonly AsyncOperationGate monitoringStartGate = new();
    private readonly CancellationTokenSource applicationExitCancellation = new();
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
            new PythonCaptureSaveWorkflowRunner(),
            new LocalViewerPathStore());
        DataContext = viewModel;
    }

    internal MainViewModel ViewModel => viewModel;

    internal Task RestoreSavedPathsAsync() => viewModel.RestoreSavedPathsAsync();

    private async void StartContinuousCapture_Click(object sender, RoutedEventArgs e)
    {
        if (applicationExitRequested)
        {
            return;
        }
        await StartCaptureOnlyAsync();
    }

    private Task StartCaptureOnlyAsync() =>
        applicationExitRequested
            ? Task.CompletedTask
            : monitoringStartGate.RunAsync(StartCaptureOnlyCoreAsync);

    private async Task StartCaptureOnlyCoreAsync(CancellationToken cancellationToken)
    {
        if (applicationExitRequested || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        viewModel.SetMonitoringStartPending(true);
        try
        {
            await viewModel.StartContinuousCaptureAsync(
                new WindowInteropHelper(this).EnsureHandle(),
                cancellationToken);
        }
        finally
        {
            viewModel.SetMonitoringStartPending(false);
        }
    }

    private async void StopContinuousCapture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await viewModel.StopContinuousCaptureAsync();
        }
        catch (Exception)
        {
            // MainViewModel has already projected the failure and retry state.
        }
    }

    private async void StartContinuousCaptureAndSave_Click(object sender, RoutedEventArgs e)
    {
        if (applicationExitRequested)
        {
            return;
        }
        await StartMonitoringAsync();
    }

    internal Task StartMonitoringFromTrayAsync() =>
        applicationExitRequested ? Task.CompletedTask : StartMonitoringAsync();

    private Task StartMonitoringAsync() =>
        applicationExitRequested
            ? Task.CompletedTask
            : monitoringStartGate.RunAsync(StartMonitoringCoreAsync);

    private async Task StartMonitoringCoreAsync(CancellationToken cancellationToken)
    {
        if (viewModel.IsSaving || cancellationToken.IsCancellationRequested)
        {
            return;
        }
        viewModel.SetMonitoringStartPending(true);
        try
        {
            await viewModel.StartConfiguredContinuousCaptureAndSaveAsync(
                new WindowInteropHelper(this).EnsureHandle(),
                cancellationToken);
        }
        finally
        {
            viewModel.SetMonitoringStartPending(false);
        }
    }

    internal async Task StopMonitoringAsync()
    {
        monitoringStartGate.Cancel();
        Exception? failure = null;
        try
        {
            await viewModel.StopContinuousCaptureAsync();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await monitoringStartGate.WaitAsync();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is null && applicationExitRequested)
        {
            try
            {
                await viewModel.WaitForOperationsAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    internal void RequestApplicationExit()
    {
        if (applicationExitRequested)
        {
            return;
        }
        applicationExitRequested = true;
        viewModel.RequestApplicationExit();
        monitoringStartGate.Cancel();
        applicationExitCancellation.Cancel();
    }

    internal void PrepareForApplicationExit()
    {
        RequestApplicationExit();
        monitoringStartGate.Dispose();
        applicationExitCancellation.Dispose();
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
        if (applicationExitRequested || viewModel.IsCapturing)
        {
            return;
        }
        await viewModel.CaptureOneFrameAsync(
            new WindowInteropHelper(this).EnsureHandle(),
            applicationExitCancellation.Token);
    }

    private async void SaveOnePlay_Click(object sender, RoutedEventArgs e)
    {
        if (applicationExitRequested || viewModel.IsSaving)
        {
            return;
        }
        var workflowDialog = new OpenFileDialog
        {
            Title = "正式保存workflow入力JSONを選択",
            Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (ShowFileDialog(workflowDialog, applicationExitCancellation.Token) != true)
        {
            return;
        }
        await viewModel.SaveAndReloadConfiguredAsync(
            workflowDialog.FileName,
            applicationExitCancellation.Token);
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

    private bool? ShowFileDialog(WpfFileDialog dialog, CancellationToken cancellationToken)
    {
        if (applicationExitRequested ||
            cancellationToken.IsCancellationRequested ||
            applicationExitCancellation.IsCancellationRequested)
        {
            return false;
        }

        using var operationRegistration = cancellationToken.Register(
            static state => NativeFileDialogCloser.Close((string)state!),
            dialog.Title);
        using var exitRegistration = applicationExitCancellation.Token.Register(
            static state => NativeFileDialogCloser.Close((string)state!),
            dialog.Title);
        var result = dialog.ShowDialog(this);
        return cancellationToken.IsCancellationRequested ||
            applicationExitCancellation.IsCancellationRequested
            ? false
            : result;
    }
}

internal static class NativeFileDialogCloser
{
    private const uint WmClose = 0x0010;

    public static void Close(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var dialogHandle = FindWindow(null, title);
        if (dialogHandle != 0)
        {
            PostMessage(dialogHandle, WmClose, 0, 0);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern bool PostMessage(nint windowHandle, uint message, nint wParam, nint lParam);
}
