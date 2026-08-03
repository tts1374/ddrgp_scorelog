using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Tray;
using DDRGpScoreViewer.Updates;
using DDRGpScoreViewer.ViewModels;
using Microsoft.Win32;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfBinding = System.Windows.Data.Binding;
using WpfFileDialog = Microsoft.Win32.FileDialog;
using WpfOrientation = System.Windows.Controls.Orientation;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DDRGpScoreViewer;

public partial class MainWindow : System.Windows.Window
{
    private readonly MainViewModel viewModel;
    private readonly AsyncOperationGate monitoringStartGate = new();
    private readonly CancellationTokenSource applicationExitCancellation = new();
    private bool applicationExitRequested;
    private Func<Task>? applicationUpdatePrepareExitHandler;
    private Func<Task>? applicationUpdateExitHandler;
    private Action? applicationUpdateForceExitHandler;

    public MainWindow()
        : this(ViewerDatabasePaths.ResolveDefault())
    {
    }

    internal MainWindow(ViewerDatabasePaths databasePaths)
    {
        InitializeComponent();
        viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            workflowRunner: new AppOwnedPersonalScoreDbWorkflowRunner(),
#if DEBUG
            captureService: new SingleFrameCaptureService(
                new WindowsGraphicsCaptureAdapter(),
                new ApplicationCaptureOutputWriter()),
#endif
            continuousCaptureService: new ContinuousCaptureService(
                new ContinuousWindowsGraphicsCaptureAdapter(),
                new ApplicationCaptureSessionOutputWriter()),
            captureSaveWorkflowRunner: new AppOwnedCaptureSaveWorkflowRunner(),
            pathStore: new LocalViewerPathStore(databasePaths.SettingsPath),
            defaultDatabasePaths: databasePaths,
            liveMonitoringService: new LiveMonitoringCaptureService(
                new ContinuousWindowsGraphicsCaptureAdapter(),
                new AppOwnedLiveResultAnalyzer()),
            applicationUpdateService: databasePaths.Environment == ViewerDatabaseEnvironment.Production
                ? new ApplicationUpdateService()
                : null);
        DataContext = viewModel;
#if DEBUG
        AddDeveloperActions();
#endif
    }

#if DEBUG
    private void AddDeveloperActions()
    {
        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal };
        buttons.Children.Add(CreateDeveloperActionButton(
            "1フレーム取得",
            CaptureOneFrame_Click));
        buttons.Children.Add(CreateDeveloperActionButton(
            "連続取得を開始",
            StartContinuousCapture_Click));
        buttons.Children.Add(CreateDeveloperActionButton(
            "単発保存",
            SaveOnePlay_Click));

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Debug build / 開発者向け操作",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3),
        });
        content.Children.Add(buttons);

        var panel = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromRgb(239, 246, 255)),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(147, 197, 253)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 12, 0),
            Child = content,
        };
        DockPanel.SetDock(panel, Dock.Right);
        ActionBar.Children.Insert(0, panel);
    }

    private WpfButton CreateDeveloperActionButton(
        string content,
        RoutedEventHandler clickHandler)
    {
        var button = new WpfButton
        {
            Content = content,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("PrimaryButtonStyle"),
        };
        button.SetBinding(
            WpfButton.IsEnabledProperty,
            new WpfBinding(nameof(MainViewModel.CanRunDeveloperOperations))
            {
                Mode = BindingMode.OneWay,
            });
        button.Click += clickHandler;
        return button;
    }
#endif

    internal MainViewModel ViewModel => viewModel;

    internal Task RestoreSavedPathsAsync() => viewModel.RestoreSavedPathsAsync();

    internal CancellationToken ApplicationExitToken => applicationExitCancellation.Token;

    internal void SetApplicationUpdateExitHandlers(
        Func<Task> prepareExitHandler,
        Func<Task> exitHandler,
        Action forceExitHandler)
    {
        applicationUpdatePrepareExitHandler = prepareExitHandler;
        applicationUpdateExitHandler = exitHandler;
        applicationUpdateForceExitHandler = forceExitHandler;
    }

    internal Task CheckForApplicationUpdateAsync(CancellationToken cancellationToken) =>
        viewModel.CheckForApplicationUpdateAsync(cancellationToken);

    private async void CheckForApplicationUpdate_Click(object sender, RoutedEventArgs e) =>
        await viewModel.CheckForApplicationUpdateAsync(applicationExitCancellation.Token);

    private async void DownloadAndApplyApplicationUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (applicationUpdatePrepareExitHandler is null ||
            applicationUpdateExitHandler is null ||
            applicationUpdateForceExitHandler is null)
        {
            return;
        }

        await viewModel.DownloadAndApplyApplicationUpdateAsync(
            applicationUpdatePrepareExitHandler,
            applicationUpdateExitHandler,
            applicationUpdateForceExitHandler,
            applicationExitCancellation.Token);
    }

#if DEBUG
    private async void StartContinuousCapture_Click(object sender, RoutedEventArgs e)
    {
        if (applicationExitRequested || !viewModel.CanRunDeveloperOperations)
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
#endif

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
    }

#if DEBUG
    private async void CaptureOneFrame_Click(object sender, RoutedEventArgs e)
    {
        if (applicationExitRequested || !viewModel.CanRunDeveloperOperations)
        {
            return;
        }
        await viewModel.CaptureOneFrameAsync(
            new WindowInteropHelper(this).EnsureHandle(),
            applicationExitCancellation.Token);
    }

    private async void SaveOnePlay_Click(object sender, RoutedEventArgs e)
    {
        if (applicationExitRequested || !viewModel.CanRunDeveloperOperations)
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
#endif

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

#if DEBUG
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
#endif
}

#if DEBUG
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
#endif
