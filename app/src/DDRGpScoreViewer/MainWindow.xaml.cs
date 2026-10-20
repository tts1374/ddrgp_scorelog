using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Tray;
using DDRGpScoreViewer.Updates;
using DDRGpScoreViewer.ViewModels;
using DDRGpScoreViewer.WebBestSync;
using DDRGpScoreViewer.WebIdentity;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfBinding = System.Windows.Data.Binding;
using WpfFileDialog = Microsoft.Win32.FileDialog;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DDRGpScoreViewer;

public partial class MainWindow : System.Windows.Window
{
    private const double HomeSingleColumnThreshold = 1100;
    internal const string ProductionWebApiOrigin =
        "https://ddrgp-scorelog.tts1374.workers.dev/";
    internal const string DevelopmentWebApiOrigin = "https://ddrgp-scorelog-dev.tts1374.workers.dev/";
    private readonly MainViewModel viewModel;
    private readonly AsyncOperationGate monitoringStartGate = new();
    private readonly BestChartPageRequestGate bestChartPageRequestGate = new();
    private readonly BestChartPageRequestGate recentPlayPageRequestGate = new();
    private readonly CancellationTokenSource applicationExitCancellation = new();
    private readonly DispatcherTimer homePeriodRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(15),
    };
    private DispatcherTimer? homeSummaryCopyFeedbackTimer;
    private bool applicationExitRequested;
    private bool restoringBestChartListState;
    private bool resettingBestChartListPosition;
    private double bestChartScrollOffset;
    private Func<Task>? applicationUpdatePrepareExitHandler;
    private Func<Task>? applicationUpdateExitHandler;
    private Action? applicationUpdateForceExitHandler;
    private Func<Task<bool>>? languageChangeRestartHandler;
    private bool languageChangeRestartRequested;
    private readonly HttpClient webApiHttpClient;

    public MainWindow()
        : this(ViewerDatabasePaths.ResolveDefault())
    {
    }

    internal MainWindow(ViewerDatabasePaths databasePaths, HttpMessageHandler? webApiHandler = null)
    {
        InitializeComponent();
        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        homePeriodRefreshTimer.Tick += HomePeriodRefreshTimer_Tick;
        var webApiUri = ResolveWebApiOrigin(
            databasePaths.Environment,
            Environment.GetEnvironmentVariable("DDRGP_WEB_API_ORIGIN"));
        webApiHttpClient = new HttpClient(webApiHandler ?? new HttpClientHandler())
        {
            BaseAddress = webApiUri,
            Timeout = TimeSpan.FromSeconds(30),
        };
        var identityStore = new FileWebPlayerIdentityStore(
            databasePaths.WebPlayerIdentityPath,
            databasePaths.WebPlayerCredentialPath);
        var webPlayerIdentityService = new WebPlayerIdentityService(
            webApiHttpClient,
            identityStore,
            allowLoopbackHttp: databasePaths.Environment == ViewerDatabaseEnvironment.Development,
            authorizationStore: new FileAppAuthorizationStore(databasePaths.WebPlayerCredentialPath + ".authorization"));
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
        WebBestSyncCoordinator? webBestSyncCoordinator = null;
        WebBestSyncSnapshot? initialWebBestSyncState = null;
        try
        {
            var coordinator = new WebBestSyncCoordinator(
                new SqliteWebBestSyncStateStore(databasePaths.WebBestSyncStatePath),
                new WebBestProjectionRepository(),
                new WebBestSyncApiClient(
                    webApiHttpClient,
                    identityStore,
                    allowLoopbackHttp: databasePaths.Environment == ViewerDatabaseEnvironment.Development),
                databasePaths.ScoreDatabasePath,
                databasePaths.MasterDatabasePath);
            initialWebBestSyncState = coordinator.State;
            webBestSyncCoordinator = coordinator;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or
            UnauthorizedAccessException or InvalidDataException or FormatException or
            ArgumentException or InvalidCastException or OverflowException)
        {
            viewModel.ReportWebBestSyncUnavailable(databasePaths.WebBestSyncStatePath, exception);
        }
        if (webBestSyncCoordinator is not null)
        {
            viewModel.ConfigureWebBestSync(
                webBestSyncCoordinator, webPlayerIdentityService, initialWebBestSyncState);
        }
        DataContext = viewModel;
        ApplyHomeResponsiveLayout(Width);
        ApplyBestResponsiveLayout(Width);
        ApplyHistoryResponsiveLayout(Width);
        viewModel.ChartBestListReset += ViewModel_ChartBestListReset;
        viewModel.ChartBestSelectionRequested += ViewModel_ChartBestSelectionRequested;
        viewModel.ChartDetailUpdated += ViewModel_ChartDetailUpdated;
#if DEBUG
        AddDeveloperActions();
#endif
        Localization.ApplyToWindow(this);
    }

    internal static Uri ResolveWebApiOrigin(
        ViewerDatabaseEnvironment environment,
        string? overrideOrigin)
    {
        var development = environment == ViewerDatabaseEnvironment.Development;
        var value = new Uri(development ? DevelopmentWebApiOrigin : ProductionWebApiOrigin);
        if (Uri.TryCreate(overrideOrigin, UriKind.Absolute, out var configured) &&
            (configured.Scheme == Uri.UriSchemeHttps ||
             development && configured.Scheme == Uri.UriSchemeHttp && configured.IsLoopback) &&
            (!development ||
             !string.Equals(configured.Host, new Uri(ProductionWebApiOrigin).Host,
                 StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(configured.Host, "ddrgp-scorelog-identity-api.tts1374.workers.dev",
                 StringComparison.OrdinalIgnoreCase)))
        {
            value = configured;
        }
        return new Uri(value.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    internal static Uri ResolvePublicPlayerPageUri(Uri origin, string publicPlayerId) =>
        new(origin, "/player/" + Uri.EscapeDataString(publicPlayerId));

#if DEBUG
    private void AddDeveloperActions()
    {
        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal };
        buttons.Children.Add(CreateDeveloperActionButton(
            Localization.Get("1フレーム取得"),
            CaptureOneFrame_Click));
        buttons.Children.Add(CreateDeveloperActionButton(
            Localization.Get("連続取得を開始"),
            StartContinuousCapture_Click));
        buttons.Children.Add(CreateDeveloperActionButton(
            Localization.Get("単発保存"),
            SaveOnePlay_Click));

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = Localization.Get("Debug build / 開発者向け操作"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3),
        });
        content.Children.Add(buttons);

        var panel = new Border
        {
            Background = (WpfBrush)FindResource("DeveloperPanelBackgroundBrush"),
            BorderBrush = (WpfBrush)FindResource("DeveloperPanelBorderBrush"),
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

    internal async Task RestoreSavedPathsAsync()
    {
        await viewModel.RestoreSavedPathsAsync();
        ThemeManager.Apply(viewModel.AppliedTheme);
    }

    internal void ApplyConfiguredStartupPage()
    {
        switch (viewModel.AppliedStartupPage)
        {
            case UserSettings.BestStartupPage:
                ShowBestPage();
                break;
            case UserSettings.HistoryStartupPage:
                ShowHistoryPage();
                break;
            default:
                ShowHomePage();
                break;
        }
    }

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

    internal void SetLanguageChangeRestartHandler(Func<Task<bool>> restartHandler) =>
        languageChangeRestartHandler = restartHandler;

    internal Task CheckForApplicationUpdateAsync(CancellationToken cancellationToken)
    {
        if (applicationUpdatePrepareExitHandler is null ||
            applicationUpdateExitHandler is null ||
            applicationUpdateForceExitHandler is null)
        {
            return viewModel.CheckForApplicationUpdateAsync(cancellationToken);
        }

        return viewModel.CheckAndApplyApplicationUpdateAsync(
            applicationUpdatePrepareExitHandler,
            applicationUpdateExitHandler,
            applicationUpdateForceExitHandler,
            cancellationToken);
    }

    internal void StartAutomaticMonitoring()
    {
        if (applicationExitRequested)
        {
            return;
        }

        viewModel.StartAutomaticMonitoring(new WindowInteropHelper(this).EnsureHandle());
    }

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
        homePeriodRefreshTimer.Stop();
        homeSummaryCopyFeedbackTimer?.Stop();
        viewModel.RequestApplicationExit();
        monitoringStartGate.Cancel();
        applicationExitCancellation.Cancel();
    }

    internal void PrepareForApplicationExit()
    {
        RequestApplicationExit();
        monitoringStartGate.Dispose();
        applicationExitCancellation.Dispose();
        webApiHttpClient.Dispose();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!applicationExitRequested)
        {
            homePeriodRefreshTimer.Start();
        }
    }

    private void HomePeriodRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!applicationExitRequested)
        {
            viewModel.RefreshHome(DateTimeOffset.Now);
        }
    }

    private void CopyHomeSummary_Click(object sender, RoutedEventArgs e)
    {
        if (applicationExitRequested)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(viewModel.HomeTodaySummaryCopyText);
            ShowHomeSummaryCopyFeedback("コピーしました");
        }
        catch (Exception exception) when (
            exception is ExternalException or InvalidOperationException)
        {
            ShowHomeSummaryCopyFeedback("コピーできませんでした");
        }
    }

    private void ShowHomeSummaryCopyFeedback(string message)
    {
        HomeSummaryCopyButton.Content = Localization.Get(message);
        homeSummaryCopyFeedbackTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.6),
        };
        homeSummaryCopyFeedbackTimer.Stop();
        homeSummaryCopyFeedbackTimer.Tick -= HomeSummaryCopyFeedbackTimer_Tick;
        homeSummaryCopyFeedbackTimer.Tick += HomeSummaryCopyFeedbackTimer_Tick;
        homeSummaryCopyFeedbackTimer.Start();
    }

    private void HomeSummaryCopyFeedbackTimer_Tick(object? sender, EventArgs e)
    {
        if (homeSummaryCopyFeedbackTimer is null)
        {
            return;
        }

        homeSummaryCopyFeedbackTimer.Stop();
        homeSummaryCopyFeedbackTimer.Tick -= HomeSummaryCopyFeedbackTimer_Tick;
        if (!applicationExitRequested)
        {
            HomeSummaryCopyButton.Content = Localization.Get("振り返りをコピー");
        }
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

    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
        base.OnClosed(e);
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
            Title = Localization.Get("正式保存workflow入力JSONを選択"),
            Filter = Localization.Get("JSON file (*.json)|*.json|All files (*.*)|*.*"),
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

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyHomeResponsiveLayout(e.NewSize.Width);
        ApplyBestResponsiveLayout(e.NewSize.Width);
        ApplyHistoryResponsiveLayout(e.NewSize.Width);
        RenderChartDetailGraph();
    }

    private void ApplyBestResponsiveLayout(double windowWidth)
    {
        if (BestChartGrid is null || MainContentGrid is null)
        {
            return;
        }

        var compact = windowWidth <= 1100;
        MainContentGrid.Margin = compact
            ? new Thickness(20)
            : new Thickness(32, ContentTabs.SelectedIndex == 0 ? 14 : 22, 32, ContentTabs.SelectedIndex == 0 ? 14 : 22);
        PageNoticeRow.MinHeight = ContentTabs.SelectedIndex is 0 or 3 ? 4 : 12;
        BestChartGrid.Tag = compact ? "Compact" : "Wide";
        BestChartGrid.RowHeight = compact ? 40 : 52;
        BestLayoutGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 232);
        Grid.SetColumn(BestConditionsPanel, compact ? 1 : 0);
        Grid.SetRowSpan(BestConditionsPanel, compact ? 1 : 4);
        BestConditionsPanel.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 24, 0);
        BestConditionsPanel.Padding = compact ? new Thickness(0) : new Thickness(0, 0, 20, 0);
        BestConditionsPanel.BorderThickness = compact ? new Thickness(0) : new Thickness(0, 0, 1, 0);
        BestModeButtons.Width = compact ? double.NaN : 188;
        BestProgressMetrics.Columns = 6;
        foreach (var panel in new[] { BestLevelOptionsPanel, BestVersionOptionsPanel, BestGoalOptionsPanel, BestTitleSearchPanel })
        {
            panel.Orientation = compact ? WpfOrientation.Horizontal : WpfOrientation.Vertical;
            if (panel.Children[0] is TextBlock label)
            {
                label.VerticalAlignment = VerticalAlignment.Center;
                label.Margin = compact ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 8);
            }
        }
        BestTitleSearchTextBox.Width = compact ? 300 : double.NaN;
        UpdateBestBrowseModeUi();

        if (BestChartGrid.Columns.Count != 9)
        {
            return;
        }

        var compactWidths = new[] { 120.0, 62, 96, 110, 56, 70, 88, 72, 100 };
        var minimums = new[] { 145.0, 85, 115, 110, 74, 92, 100, 20, 20 };
        var weights = new[] { 2.4, 1.35, 1.55, 1.2, 0.8, 1.0, 1.0, 1.05, 1.3 };
        for (var index = 0; index < weights.Length; index++)
        {
            BestChartGrid.Columns[index].MinWidth = compact ? compactWidths[index] : minimums[index];
            BestChartGrid.Columns[index].Width = compact
                ? index == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(compactWidths[index])
                : new DataGridLength(weights[index], DataGridLengthUnitType.Star);
        }
    }

    private void ApplyHomeResponsiveLayout(double windowWidth)
    {
        if (LatestFeaturedBody is null || LatestInfoBorder is null || LatestScorePanel is null ||
            HomeListsGrid is null)
        {
            return;
        }

        var compact = windowWidth <= HomeSingleColumnThreshold;
        LatestDeltaGrid.Columns = compact ? 2 : 1;
        LatestFeaturedBody.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(0.8, GridUnitType.Star);
        Grid.SetColumn(LatestInfoBorder, compact ? 0 : 2);
        Grid.SetRow(LatestInfoBorder, compact ? 1 : 0);
        Grid.SetColumnSpan(LatestInfoBorder, compact ? 2 : 1);
        LatestInfoBorder.Margin = compact ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        LatestInfoBorder.Padding = compact ? new Thickness(0, 12, 0, 0) : new Thickness(20, 0, 0, 0);
        LatestInfoBorder.BorderThickness = compact ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);

        HomeHistoryPanel.Margin = new Thickness(0, 0, 16, 0);
        HomeUpdatesPanel.Margin = new Thickness(16, 0, 0, 0);
    }

    private void ApplyHistoryResponsiveLayout(double windowWidth)
    {
        if (HistoryLayoutGrid is null)
        {
            return;
        }
        var compact = windowWidth <= 1200;
        HistoryLayoutGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 280);
        HistoryLayoutGrid.RowDefinitions[0].Height = new GridLength(compact ? 1.4 : 1, GridUnitType.Star);
        HistoryLayoutGrid.RowDefinitions[1].Height = compact
            ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(HistoryDetailPanel, compact ? 0 : 1);
        Grid.SetRow(HistoryDetailPanel, compact ? 1 : 0);
        HistoryDetailPanel.Margin = compact ? new Thickness(0, 14, 0, 0) : new Thickness(24, 0, 0, 0);
        HistoryDetailPanel.Padding = compact ? new Thickness(0, 12, 0, 0) : new Thickness(24, 0, 0, 0);
        HistoryDetailPanel.BorderThickness = compact ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        RecentPlayGrid.Tag = compact ? "Compact" : "Wide";
        RecentPlayGrid.RowHeight = compact ? 40 : 48;
        HistoryDetailGrid.ColumnDefinitions[0].Width = new GridLength(1.1, GridUnitType.Star);
        HistoryDetailGrid.ColumnDefinitions[1].Width = compact ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
        HistoryDetailGrid.ColumnDefinitions[2].Width = compact ? new GridLength(0.9, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(HistoryJudgmentPanel, compact ? 1 : 0);
        Grid.SetRow(HistoryJudgmentPanel, compact ? 0 : 1);
        Grid.SetColumn(HistoryTimestampPanel, compact ? 2 : 0);
        Grid.SetRow(HistoryTimestampPanel, compact ? 0 : 2);
        HistorySummaryPanel.Margin = compact ? new Thickness(0, 0, 20, 0) : new Thickness(0);
        HistoryJudgmentPanel.Margin = compact ? new Thickness(0, 0, 20, 0) : new Thickness(0, 24, 0, 0);
        HistoryTimestampPanel.Margin = compact ? new Thickness(0) : new Thickness(0, 18, 0, 0);
        HistoryJudgments.Tag = compact ? 2 : 1;
        HistoryScorePanel.Orientation = compact ? WpfOrientation.Horizontal : WpfOrientation.Vertical;
        HistoryScoreText.FontSize = compact ? 32 : 36;
        HistoryExScoreText.Margin = compact ? new Thickness(8, 0, 0, 4) : new Thickness(0, 2, 0, 0);
    }

    private void ShowHome_Click(object sender, RoutedEventArgs e) => ShowHomePage();

    private void ShowHomePage()
    {
        viewModel.SetFlareSkillPage(false);
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(false);
        ContentTabs.SelectedIndex = 0;
        ApplyBestResponsiveLayout(ActualWidth);
        BindingOperations.ClearBinding(PageTitle, TextBlock.TextProperty);
        PageTitle.Text = Localization.Get("ホーム");
        PageSubtitle.Text = Localization.Get("今日のプレー状況と最近の記録を確認できます");
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = "Selected";
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = null;
        FlareSkillNavigation.Tag = null;
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = null;
    }

    private void ShowBest_Click(object sender, RoutedEventArgs e) => ShowBestPage();

    private void ShowBestPage()
    {
        restoringBestChartListState = true;
        viewModel.SetFlareSkillPage(false);
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(false);
        ContentTabs.SelectedIndex = 1;
        ApplyBestResponsiveLayout(ActualWidth);
        BindingOperations.ClearBinding(PageTitle, TextBlock.TextProperty);
        PageTitle.Text = Localization.Get("自己ベスト");
        PageSubtitle.Text = Localization.Get("保存済み全履歴から算出した譜面別ベスト");
        Localization.ApplyToWindow(this);
        UpdateBestPlayStyleButtons();
        UpdateBestBrowseModeButtons();
        UpdateBestBrowseModeUi();
        RestoreBestChartListState();
        HomeNavigation.Tag = null;
        BestNavigation.Tag = "Selected";
        HistoryNavigation.Tag = null;
        FlareSkillNavigation.Tag = null;
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = null;
    }

    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowSettingsPage();

    private void ShowSettingsPage()
    {
        viewModel.SetFlareSkillPage(false);
        viewModel.SetDataManagementPage(false);
        viewModel.SetSettingsPage(true);
        ContentTabs.SelectedIndex = 5;
        ApplyBestResponsiveLayout(ActualWidth);
        BindingOperations.ClearBinding(PageTitle, TextBlock.TextProperty);
        PageTitle.Text = Localization.Get("設定");
        PageSubtitle.Text = "";
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = null;
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = null;
        FlareSkillNavigation.Tag = null;
        SettingsNavigation.Tag = "Selected";
        DataManagementNavigation.Tag = null;
    }

    private async void SyncWebBestsNow_Click(object sender, RoutedEventArgs e)
    {
        await viewModel.SyncWebBestsNowAsync(applicationExitCancellation.Token);
    }

    private async void ToggleWebSync_Click(object sender, RoutedEventArgs e) =>
        await viewModel.ToggleWebBestSyncAsync(applicationExitCancellation.Token);

    private async void RefreshWebPlayer_Click(object sender, RoutedEventArgs e) =>
        await viewModel.RefreshWebPlayerAsync(applicationExitCancellation.Token);

    private void OpenWebProfile_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.GetWebProfileUri() is { } uri)
        {
            OpenAccountBrowser(uri);
        }
    }

    private async void ConnectWebAccount_Click(object sender, RoutedEventArgs e)
    {
        var result = await viewModel.BeginWebAuthorizationAsync(false, applicationExitCancellation.Token);
        if (result.BrowserUri is not null)
        {
            OpenAccountBrowser(result.BrowserUri);
            await viewModel.CompleteWebAuthorizationAsync(applicationExitCancellation.Token);
        }
    }

    private async void UnlinkGoogle_Click(object sender, RoutedEventArgs e)
    {
        var result = await viewModel.BeginWebAuthorizationAsync(true, applicationExitCancellation.Token);
        if (result.BrowserUri is not null)
        {
            OpenAccountBrowser(result.BrowserUri);
            await viewModel.CompleteWebAuthorizationAsync(applicationExitCancellation.Token);
        }
    }

    private async void ConfirmWebAuthorizationResult_Click(object sender, RoutedEventArgs e) =>
        await viewModel.CompleteWebAuthorizationAsync(applicationExitCancellation.Token);

    private async void CancelWebAuthorization_Click(object sender, RoutedEventArgs e) =>
        await viewModel.CancelWebAuthorizationAsync(applicationExitCancellation.Token);

    private static void OpenAccountBrowser(Uri uri)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(Localization.Get("ブラウザーを開けませんでした。既定のブラウザーを確認してください。"),
                Localization.Get("Web連携"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenPublicPlayerPage_Click(object sender, RoutedEventArgs e)
    {
        var publicPlayerId = viewModel.GetPublicPlayerId();
        if (publicPlayerId is null)
        {
            return;
        }

        var pageUri = ResolvePublicPlayerPageUri(webApiHttpClient.BaseAddress!, publicPlayerId);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(pageUri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(
                Localization.Get("公開ページを開けませんでした。既定のブラウザーを確認してください。"),
                Localization.Get("公開ページ"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void CheckWebBestIdentity_Click(object sender, RoutedEventArgs e)
    {
        var publicPlayerId = viewModel.GetPublicPlayerId();
        var publicUrl = publicPlayerId is null ? "" :
            "\n" + ResolvePublicPlayerPageUri(webApiHttpClient.BaseAddress!, publicPlayerId).AbsoluteUri;
        var confirmed = System.Windows.MessageBox.Show(
            Localization.Get(
                "このPCの認証情報だけを消します。Webの公開ユーザー・公開BestとPC内のスコア履歴は削除しません。") + publicUrl,
            Localization.Get("認証情報を削除しますか？"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmed == MessageBoxResult.Yes)
        {
            await viewModel.ForgetInvalidWebIdentityAsync(applicationExitCancellation.Token);
        }
    }

    private async void DeletePublicBests_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = System.Windows.MessageBox.Show(
            Localization.Get(
                "Web上の公開データを削除し、同期をOFFにします。プレイヤー情報、公開URL、認証情報、ローカルの保存データは残ります。"),
            Localization.Get("公開データを削除しますか？"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }
        await viewModel.DeletePublicBestsAsync(applicationExitCancellation.Token);
    }

    private async void ReplaceWebBests_Click(object sender, RoutedEventArgs e)
    {
        var review = await viewModel.PrepareWebBestReplacementAsync(applicationExitCancellation.Token);
        if (review is null)
        {
            return;
        }
        var confirmed = false;
        try
        {
            var playerId = viewModel.GetPublicPlayerId();
            var publicUrl = playerId is null ? "—" :
                ResolvePublicPlayerPageUri(webApiHttpClient.BaseAddress!, playerId).AbsoluteUri;
            var panel = new StackPanel { Margin = new Thickness(20) };
            var details = $"{viewModel.WebPlayerDisplayName}\n{publicUrl}\n\n" +
                Localization.Format("Web公開: {0}譜面 / このPCの対象: {1}譜面", review.PublicCount, review.EligibleCount) + "\n" +
                Localization.Format("削除: {0}譜面 / 低下: {1}譜面", review.Removed.Count, review.Lowered.Count) + "\n\n" +
                Localization.Get("自動記録の対象プレーだけで置き換えます。画像から追加・バックアップから復元したプレーは含みません。") + "\n" +
                Localization.Get("このPCにない公開Bestも削除されます。") + "\n" +
                (review.EligibleCount == 0 ? Localization.Get("対象が0件のため、WebのBestを全削除します。") + "\n" : "") +
                Localization.Get("削除される譜面: ") + string.Join(", ", review.Removed.Take(5)) + "\n" +
                Localization.Get("低下する項目: ") + string.Join(", ", review.Lowered.Take(5).Select(
                    item => $"{item.ChartId}: {string.Join(", ", item.Fields.Select(ReplacementFieldLabel))}"));
            panel.Children.Add(new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap });
            var consent = new System.Windows.Controls.CheckBox
            {
                Content = Localization.Get("削除・低下を確認し、このPCの対象データで置き換える"),
                Margin = new Thickness(0, 18, 0, 12),
                IsChecked = false,
            };
            panel.Children.Add(consent);
            var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal };
            var commit = new WpfButton { Content = Localization.Get("確認した内容で置き換える"), IsEnabled = false, Padding = new Thickness(12, 6, 12, 6) };
            var cancel = new WpfButton { Content = Localization.Get("キャンセル"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6), IsCancel = true };
            buttons.Children.Add(commit);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            var dialog = new System.Windows.Window
            {
                Title = Localization.Get("WebのBestを置き換える"),
                Owner = this,
                Width = 620,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 650,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ResizeMode = ResizeMode.NoResize,
            };
            consent.Checked += (_, _) => commit.IsEnabled = true;
            consent.Unchecked += (_, _) => commit.IsEnabled = false;
            commit.Click += (_, _) => dialog.DialogResult = true;
            if (dialog.ShowDialog() == true && consent.IsChecked == true)
            {
                confirmed = true;
                await viewModel.ConfirmWebBestReplacementAsync(applicationExitCancellation.Token);
            }
        }
        finally
        {
            if (!confirmed)
            {
                await viewModel.CancelWebBestReplacementAsync(CancellationToken.None);
            }
        }
    }

    private static string ReplacementFieldLabel(string field) => field switch
    {
        "best_score" => "SCORE",
        "best_ex_score" => "EX SCORE",
        "best_clear_type" => Localization.Get("クリア状況"),
        "best_flare_rank" => Localization.Get("フレアランク"),
        _ => field,
    };

    private void ShowDataManagement_Click(object sender, RoutedEventArgs e) =>
        ShowDataManagementPage();

    private void ShowDataManagementPage()
    {
        viewModel.SetFlareSkillPage(false);
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(true);
        ContentTabs.SelectedIndex = 6;
        ApplyBestResponsiveLayout(ActualWidth);
        BindingOperations.ClearBinding(PageTitle, TextBlock.TextProperty);
        PageTitle.Text = Localization.Get("データ管理");
        PageSubtitle.Text = Localization.Get("保存済みプレーと楽曲・譜面データの状態を確認できます");
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = null;
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = null;
        FlareSkillNavigation.Tag = null;
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = "Selected";
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (languageChangeRestartRequested)
        {
            return;
        }

        var languageChanged = !string.Equals(
            viewModel.Language,
            Localization.CurrentLanguage,
            StringComparison.Ordinal);
        if (!viewModel.SaveUserSettings())
        {
            return;
        }

        ThemeManager.Apply(viewModel.Theme);
        if (!languageChanged ||
            languageChangeRestartHandler is null)
        {
            return;
        }

        languageChangeRestartRequested = true;
        try
        {
            if (!await languageChangeRestartHandler())
            {
                languageChangeRestartRequested = false;
                viewModel.SetLanguageChangeRestartFailureStatus();
            }
        }
        catch (Exception exception)
        {
            languageChangeRestartRequested = false;
            viewModel.SetLanguageChangeRestartFailureStatus(exception.Message);
        }
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e) =>
        viewModel.ResetUserSettings();

    private async void SelectScreenshots_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = Localization.Get("PNG画像 (*.png)|*.png"),
            Multiselect = true,
            Title = Localization.Get("インポートするスクリーンショットを選択"),
        };
        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.ImportScreenshotsAsync(dialog.FileNames);
        }
    }

    private void ScreenshotImport_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = viewModel.CanStartScreenshotImport &&
            e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? System.Windows.DragDropEffects.Copy
                : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void ScreenshotImport_Drop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (!viewModel.CanStartScreenshotImport ||
            !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
            e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        await viewModel.ImportScreenshotsAsync(paths);
    }

    private void CancelScreenshotImport_Click(object sender, RoutedEventArgs e) =>
        viewModel.CancelScreenshotImport();

    private void CreatePersonalScoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".json",
            FileName = $"personal-score-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Filter = Localization.Get("個人スコアバックアップ (*.json)|*.json"),
            OverwritePrompt = true,
            Title = Localization.Get("個人スコアデータのバックアップ先を選択"),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = viewModel.CreatePersonalScoreBackup(dialog.FileName);
        if (!result.Succeeded)
        {
            System.Windows.MessageBox.Show(
                this,
                result.Message,
                Localization.Get("バックアップを作成できませんでした"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RestorePersonalScoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = Localization.Get("個人スコアバックアップ (*.json)|*.json"),
            Multiselect = false,
            Title = Localization.Get("復元する個人スコアバックアップを選択"),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            Localization.Get("現在の個人スコアデータを、選択したバックアップで置き換えます。") +
                Environment.NewLine +
                Localization.Get("この操作は取り消せません。続行しますか？"),
            Localization.Get("個人スコアデータの復元確認"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var result = viewModel.RestorePersonalScoreBackup(dialog.FileName);
        if (!result.Succeeded)
        {
            System.Windows.MessageBox.Show(
                this,
                result.Message,
                Localization.Get("バックアップを復元できませんでした"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SettingsSinglePlayStyle_Click(object sender, RoutedEventArgs e) =>
        viewModel.DefaultPlayStyle = UserSettings.SinglePlayStyle;

    private void SettingsDoublePlayStyle_Click(object sender, RoutedEventArgs e) =>
        viewModel.DefaultPlayStyle = UserSettings.DoublePlayStyle;

    private void BestPlayStyle_Click(object sender, RoutedEventArgs e)
    {
        viewModel.BestPlayStyleFilter = ReferenceEquals(sender, BestDoubleButton)
            ? "DOUBLE"
            : "SINGLE";
        UpdateBestPlayStyleButtons();
        UpdateBestBrowseModeUi();
    }

    private void BestBrowseMode_Click(object sender, RoutedEventArgs e)
    {
        viewModel.BestBrowseMode = ReferenceEquals(sender, BestVersionModeButton)
            ? UserSettings.VersionBrowseMode
            : ReferenceEquals(sender, BestTitleModeButton)
                ? UserSettings.TitleBrowseMode
                : ReferenceEquals(sender, BestGoalModeButton)
                    ? UserSettings.GoalBrowseMode
                    : UserSettings.LevelBrowseMode;
        UpdateBestBrowseModeButtons();
        UpdateBestBrowseModeUi();
    }

    private void UpdateBestBrowseModeButtons()
    {
        if (BestLevelModeButton is null || BestVersionModeButton is null ||
            BestTitleModeButton is null || BestGoalModeButton is null)
        {
            return;
        }

        BestLevelModeButton.Tag = viewModel.BestBrowseMode == UserSettings.LevelBrowseMode
            ? "Selected"
            : null;
        BestVersionModeButton.Tag = viewModel.BestBrowseMode == UserSettings.VersionBrowseMode
            ? "Selected"
            : null;
        BestTitleModeButton.Tag = viewModel.BestBrowseMode == UserSettings.TitleBrowseMode
            ? "Selected"
            : null;
        BestGoalModeButton.Tag = viewModel.BestBrowseMode == UserSettings.GoalBrowseMode
            ? "Selected"
            : null;
    }

    private void UpdateBestBrowseModeUi()
    {
        if (BestAxisPanel is null) return;
        var titleMode = viewModel.BestBrowseMode == UserSettings.TitleBrowseMode;
        var versionMode = viewModel.BestBrowseMode == UserSettings.VersionBrowseMode;
        var goalMode = viewModel.BestBrowseMode == UserSettings.GoalBrowseMode;
        var compact = (ActualWidth > 0 ? ActualWidth : Width) <= 1100;
        BestTitleSearchPanel.Visibility = titleMode ? Visibility.Visible : Visibility.Collapsed;
        BestLevelOptionsPanel.Visibility = titleMode || versionMode || goalMode ? Visibility.Collapsed : Visibility.Visible;
        BestVersionOptionsPanel.Visibility = versionMode ? Visibility.Visible : Visibility.Collapsed;
        BestGoalOptionsPanel.Visibility = goalMode ? Visibility.Visible : Visibility.Collapsed;
        foreach (var element in new FrameworkElement[] { BestLevelOptionsGrid, BestVersionOptionsGrid, BestGoalOptionsGrid })
            element.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var element in new FrameworkElement[] { BestLevelComboBox, BestVersionComboBox, BestGoalComboBox })
            element.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        if (BestChartGrid is not null && BestChartGrid.Columns.Count > 3)
        {
            BestChartGrid.Columns[3].Visibility = goalMode
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void BestChartGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!restoringBestChartListState && BestChartGrid.SelectedItem is ChartBestItem chartBest)
        {
            viewModel.SelectChartBest(chartBest);
        }
    }

    private void BestChartGrid_PreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not DataGridRow)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        if (current is DataGridRow row &&
            row.Item is ChartBestItem chartBest &&
            ReferenceEquals(BestChartGrid.SelectedItem, chartBest))
        {
            viewModel.SelectChartBest(chartBest);
            e.Handled = true;
        }
    }

    private void BestChartGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (restoringBestChartListState || resettingBestChartListPosition || !BestChartGrid.IsVisible ||
            e.ExtentHeight <= 0 || e.ViewportHeight <= 0)
        {
            return;
        }

        var scrollOffsetBeforeLoad = e.VerticalOffset;
        bestChartScrollOffset = scrollOffsetBeforeLoad;

        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 1 ||
            !viewModel.CanLoadMoreChartBests ||
            !bestChartPageRequestGate.TryBegin())
        {
            return;
        }

        try
        {
            viewModel.LoadMoreChartBests();
        }
        finally
        {
            // Adding rows causes follow-up ScrollChanged events before the UI settles.
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    BestChartGrid.UpdateLayout();
                    FindVisualChild<ScrollViewer>(BestChartGrid)?.ScrollToVerticalOffset(
                        scrollOffsetBeforeLoad);
                    bestChartScrollOffset = scrollOffsetBeforeLoad;
                    bestChartPageRequestGate.Complete();
                }));
        }
    }

    private void RecentPlayGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateHistoryStickyDate();
        if (e.ExtentHeight <= 0 || e.ViewportHeight <= 0 ||
            e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 1 ||
            !viewModel.CanLoadMorePlays ||
            !recentPlayPageRequestGate.TryBegin())
        {
            return;
        }

        var offset = e.VerticalOffset;
        var selectedRow = RecentPlayGrid.SelectedItem is { } selected
            ? RecentPlayGrid.ItemContainerGenerator.ContainerFromItem(selected) as DataGridRow : null;
        var selectedTop = selectedRow?.TranslatePoint(new WpfPoint(), RecentPlayGrid).Y;
        var selectionWasVisible = selectedTop >= HistoryStickyDate.Margin.Top + HistoryStickyDate.ActualHeight &&
            selectedTop < RecentPlayGrid.ActualHeight;
        var previousCount = viewModel.Plays.Count;
        try
        {
            viewModel.LoadMorePlays();
        }
        finally
        {
            // Adding rows causes follow-up ScrollChanged events before the UI settles.
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    RecentPlayGrid.UpdateLayout();
                    FindVisualChild<ScrollViewer>(RecentPlayGrid)?.ScrollToVerticalOffset(offset);
                    UpdateHistoryStickyDate();
                    recentPlayPageRequestGate.Complete();
                    if (selectionWasVisible || RecentPlayGrid.SelectedIndex == previousCount - 1)
                    {
                        QueueHistorySelectionVisibility();
                    }
                }));
        }
    }

    private void RecentPlayGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || RecentPlayGrid.Items.Count == 0)
        {
            return;
        }
        var index = Math.Clamp(RecentPlayGrid.SelectedIndex + (e.Key == Key.Down ? 1 : -1),
            0, RecentPlayGrid.Items.Count - 1);
        if (RecentPlayGrid.SelectedIndex == index)
        {
            QueueHistorySelectionVisibility();
        }
        else
        {
            RecentPlayGrid.SelectedIndex = index;
        }
        e.Handled = true;
    }

    private void RecentPlayGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        QueueHistorySelectionVisibility();

    private void QueueHistorySelectionVisibility()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!RecentPlayGrid.IsVisible || RecentPlayGrid.SelectedItem is not PlayHistoryItem play)
            {
                return;
            }
            RecentPlayGrid.ScrollIntoView(play);
            RecentPlayGrid.UpdateLayout();
            var scrollViewer = FindVisualChild<ScrollViewer>(RecentPlayGrid);
            var viewport = FindVisualChild<ScrollContentPresenter>(RecentPlayGrid);
            if (scrollViewer is not null && RecentPlayGrid.ItemContainerGenerator.ContainerFromItem(play) is DataGridRow row)
            {
                // ScrollIntoView knows the column header, but not the pinned date.
                var top = row.TranslatePoint(new WpfPoint(), RecentPlayGrid).Y;
                var visibleTop = HistoryStickyDate.Margin.Top + HistoryStickyDate.ActualHeight;
                if (top < visibleTop)
                {
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + top - visibleTop);
                }
                else if (viewport is not null)
                {
                    var visibleBottom = viewport.TranslatePoint(new WpfPoint(), RecentPlayGrid).Y +
                        Math.Min(viewport.ActualHeight, scrollViewer.ViewportHeight);
                    visibleBottom = Math.Min(visibleBottom, RecentPlayGrid.ActualHeight -
                        RecentPlayGrid.BorderThickness.Bottom -
                        (scrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                            ? SystemParameters.HorizontalScrollBarHeight : 0));
                    if (top + row.ActualHeight > visibleBottom)
                    {
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + top + row.ActualHeight - visibleBottom);
                    }
                }
            }
            UpdateHistoryStickyDate();
        }));
    }

    private void UpdateHistoryStickyDate()
    {
        if (HistoryStickyDateText is null || !RecentPlayGrid.IsVisible)
        {
            return;
        }
        var date = viewModel.Plays.FirstOrDefault()?.HistoryDateDisplay ?? "";
        var pinnedBottom = HistoryStickyDate.Margin.Top + HistoryStickyDate.ActualHeight;
        foreach (var group in FindHistoryGroups(RecentPlayGrid))
        {
            if (group.DataContext is CollectionViewGroup data &&
                group.TranslatePoint(new WpfPoint(), RecentPlayGrid).Y <= pinnedBottom)
            {
                date = data.Name?.ToString() ?? date;
            }
        }
        HistoryStickyDateText.Text = date;
        HistoryStickyDate.Visibility = date.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static IEnumerable<GroupItem> FindHistoryGroups(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is GroupItem group)
            {
                yield return group;
            }
            foreach (var descendant in FindHistoryGroups(child))
            {
                yield return descendant;
            }
        }
    }

    private void ViewModel_ChartBestSelectionRequested(ChartBestItem chartBest)
    {
        bestChartScrollOffset = FindVisualChild<ScrollViewer>(BestChartGrid)?.VerticalOffset
            ?? bestChartScrollOffset;
        ShowChartDetail();
    }

    private void ViewModel_ChartDetailUpdated(object? sender, EventArgs e)
    {
        UpdateChartDetailGraphModeButtons();
        if (ContentTabs.SelectedIndex == 2)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(RenderChartDetailGraph));
        }
    }

    private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() =>
            {
                UpdateChartDetailGraphModeButtons();
                RenderChartDetailGraph();
            }));
    }

    private void ShowChartDetail()
    {
        viewModel.SetFlareSkillPage(false);
        viewModel.SetSettingsPage(false);
        ContentTabs.SelectedIndex = 2;
        ApplyBestResponsiveLayout(ActualWidth);
        PageTitle.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(MainViewModel.ChartDetailSongTitle))
        {
            Mode = BindingMode.OneWay,
        });
        PageSubtitle.Text = "";
        ChartDetailScrollViewer.ScrollToTop();
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = null;
        BestNavigation.Tag = "Selected";
        HistoryNavigation.Tag = null;
        FlareSkillNavigation.Tag = null;
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = null;
        UpdateChartDetailGraphModeButtons();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(RenderChartDetailGraph));
    }

    private void ChartDetailGraphMode_Click(object sender, RoutedEventArgs e)
    {
        viewModel.SetChartDetailGraphMode(
            ReferenceEquals(sender, ChartDetailBestProgressionButton)
                ? MainViewModel.ChartDetailBestProgressionMode
                : MainViewModel.ChartDetailAllPlaysMode);
        UpdateChartDetailGraphModeButtons();
        RenderChartDetailGraph();
    }

    private void ChartDetailLoadMore_Click(object sender, RoutedEventArgs e)
    {
        var offset = ChartDetailScrollViewer.VerticalOffset;
        viewModel.LoadMoreChartDetailHistory();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            ChartDetailScrollViewer.UpdateLayout();
            ChartDetailScrollViewer.ScrollToVerticalOffset(offset);
        }));
    }

    private void ChartDetailGraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderChartDetailGraph();

    private void UpdateChartDetailGraphModeButtons()
    {
        if (ChartDetailAllPlaysButton is null || ChartDetailBestProgressionButton is null)
        {
            return;
        }

        var allPlaysSelected = viewModel.ChartDetailGraphMode == MainViewModel.ChartDetailAllPlaysMode;
        ChartDetailAllPlaysButton.Tag = allPlaysSelected ? "Selected" : null;
        ChartDetailBestProgressionButton.Tag = allPlaysSelected ? null : "Selected";
    }

    private void RestoreBestChartListState()
    {
        var offset = bestChartScrollOffset;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(BestChartGrid);
                scrollViewer?.ScrollToVerticalOffset(offset);
                BestChartGrid.UpdateLayout();
                restoringBestChartListState = true;
                try
                {
                    if (viewModel.SelectedChartBest is not null)
                    {
                        BestChartGrid.SelectedItem = viewModel.SelectedChartBest;
                        if (BestChartGrid.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedChartBest) is DataGridRow row)
                        {
                            row.Focus();
                        }
                    }
                }
                finally
                {
                    restoringBestChartListState = false;
                }
            }));
    }

    private void UpdateBestPlayStyleButtons()
    {
        var singleSelected = viewModel.BestPlayStyleFilter == "SINGLE";
        BestSingleButton.Tag = singleSelected ? "Selected" : null;
        BestDoubleButton.Tag = singleSelected ? null : "Selected";
    }

    private void ViewModel_ChartBestListReset(object? sender, EventArgs e)
    {
        resettingBestChartListPosition = true;
        bestChartScrollOffset = 0;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                BestChartGrid.UpdateLayout();
                FindVisualChild<ScrollViewer>(BestChartGrid)?.ScrollToTop();
                BestChartGrid.UpdateLayout();
                resettingBestChartListPosition = false;
            }));
    }

    private void ShowHistory_Click(object sender, RoutedEventArgs e) => ShowHistoryPage();

    private void ShowHistoryPage()
    {
        viewModel.SetFlareSkillPage(false);
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(false);
        ContentTabs.SelectedIndex = 3;
        ApplyBestResponsiveLayout(ActualWidth);
        BindingOperations.ClearBinding(PageTitle, TextBlock.TextProperty);
        PageTitle.Text = Localization.Get("直近プレー履歴");
        PageSubtitle.Text = Localization.Get("保存済みのプレーを新しい順に表示します");
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = null;
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = "Selected";
        FlareSkillNavigation.Tag = null;
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = null;
    }

    private void ShowFlareSkill_Click(object sender, RoutedEventArgs e) => ShowFlareSkillPage();

    private void ShowFlareSkillPage()
    {
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(false);
        viewModel.SetFlareSkillPage(true);
        viewModel.RefreshFlareSkill();
        ContentTabs.SelectedIndex = 4;
        ApplyBestResponsiveLayout(ActualWidth);
        BindingOperations.ClearBinding(PageTitle, TextBlock.TextProperty);
        PageTitle.Text = Localization.Get("フレアスキル");
        PageSubtitle.Text = Localization.Get("保存済みFLARE実績から現在のフレアスキルを確認できます");
        Localization.ApplyToWindow(this);
        UpdateFlarePlayStyleButtons();
        HomeNavigation.Tag = null;
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = null;
        FlareSkillNavigation.Tag = "Selected";
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = null;
    }

    private void FlarePlayStyle_Click(object sender, RoutedEventArgs e)
    {
        viewModel.SetFlareSkillPlayStyle(
            ReferenceEquals(sender, FlareDoubleButton) ? "DOUBLE" : "SINGLE");
        UpdateFlarePlayStyleButtons();
    }

    private void UpdateFlarePlayStyleButtons()
    {
        var singleSelected = viewModel.FlareSkillPlayStyle == "SINGLE";
        FlareSingleButton.Tag = singleSelected ? "Selected" : null;
        FlareDoubleButton.Tag = singleSelected ? null : "Selected";
    }

    private void RenderChartDetailGraph()
    {
        if (ChartDetailGraphCanvas is null)
        {
            return;
        }

        ChartDetailGraphCanvas.Children.Clear();
        var points = viewModel.ChartDetailGraphPlays;
        var width = ChartDetailGraphCanvas.ActualWidth;
        var height = ChartDetailGraphCanvas.ActualHeight;
        if (points.Count == 0 || width <= 1 || height <= 1)
        {
            return;
        }

        const double left = 54;
        const double right = 12;
        const double top = 14;
        const double bottom = 28;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);
        var minimumScore = points.Min(point => point.Play.Score);
        var maximumScore = points.Max(point => point.Play.Score);
        var padding = Math.Max(1_000, (maximumScore - minimumScore) * 0.1);
        var lowerScore = Math.Max(0, minimumScore - padding);
        var upperScore = Math.Min(1_000_000, maximumScore + padding);
        if (upperScore <= lowerScore)
        {
            upperScore = lowerScore + 1_000;
        }

        for (var index = 0; index <= 4; index++)
        {
            var fraction = index / 4d;
            var y = top + plotHeight * fraction;
            ChartDetailGraphCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = (WpfBrush)FindResource("BorderDefaultBrush"),
                StrokeThickness = 1,
            });
            var score = upperScore - (upperScore - lowerScore) * fraction;
            var label = new TextBlock
            {
                Text = $"{score / 1_000:N0}k",
                Foreground = (WpfBrush)FindResource("TextSecondaryBrush"),
                FontSize = 12,
            };
            ChartDetailGraphCanvas.Children.Add(label);
            Canvas.SetLeft(label, 5);
            Canvas.SetTop(label, Math.Max(0, y - 8));
        }

        var renderedPoints = points
            .Select((point, index) =>
            {
                var x = points.Count == 1
                    ? left + plotWidth / 2
                    : left + plotWidth * index / (points.Count - 1d);
                var normalized = (point.Play.Score - lowerScore) / (upperScore - lowerScore);
                var y = top + plotHeight * (1 - normalized);
                return (point, location: new WpfPoint(x, y));
            })
            .ToArray();
        var line = new Polyline
        {
            Points = new PointCollection(renderedPoints.Select(item => item.location)),
            Stroke = viewModel.ChartDetailGraphMode == MainViewModel.ChartDetailBestProgressionMode
                ? (WpfBrush)FindResource("GraphBestBrush")
                : (WpfBrush)FindResource("AccentPrimaryBrush"),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        ChartDetailGraphCanvas.Children.Add(line);

        var bestBrush = (WpfBrush)FindResource("GraphBestBrush");
        foreach (var item in renderedPoints)
        {
            var isBestPoint = item.point.PreviousScore is null || item.point.IsScoreBestUpdate;
            var marker = new Ellipse
            {
                Width = isBestPoint ? 9 : 7,
                Height = isBestPoint ? 9 : 7,
                Fill = viewModel.ChartDetailGraphMode == MainViewModel.ChartDetailBestProgressionMode || isBestPoint
                    ? bestBrush
                    : (WpfBrush)FindResource("AccentPrimaryBrush"),
                Stroke = (WpfBrush)FindResource("GraphMarkerOutlineBrush"),
                StrokeThickness = 1.5,
                ToolTip = $"{item.point.Play.PlayedAtDisplay} / {item.point.Play.ScoreDisplay}",
            };
            ChartDetailGraphCanvas.Children.Add(marker);
            Canvas.SetLeft(marker, item.location.X - marker.Width / 2);
            Canvas.SetTop(marker, item.location.Y - marker.Height / 2);
        }

        var firstDate = new TextBlock
        {
            Text = points[0].HomePlayedAtDisplay,
            Foreground = (WpfBrush)FindResource("TextSecondaryBrush"),
            FontSize = 12,
        };
        ChartDetailGraphCanvas.Children.Add(firstDate);
        Canvas.SetLeft(firstDate, left);
        Canvas.SetTop(firstDate, height - bottom + 6);
        if (points.Count > 1)
        {
            var latestDate = new TextBlock
            {
                Text = points[^1].HomePlayedAtDisplay,
                Foreground = (WpfBrush)FindResource("TextSecondaryBrush"),
                FontSize = 12,
            };
            ChartDetailGraphCanvas.Children.Add(latestDate);
            Canvas.SetRight(latestDate, right);
            Canvas.SetTop(latestDate, height - bottom + 6);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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

internal sealed class BestChartPageRequestGate
{
    private bool requestPending;

    public bool TryBegin()
    {
        if (requestPending)
        {
            return false;
        }

        requestPending = true;
        return true;
    }

    public void Complete() => requestPending = false;
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
