using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.Tray;
using DDRGpScoreViewer.Updates;
using DDRGpScoreViewer.ViewModels;
using Microsoft.Win32;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfBinding = System.Windows.Data.Binding;
using WpfFileDialog = Microsoft.Win32.FileDialog;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DDRGpScoreViewer;

public partial class MainWindow : System.Windows.Window
{
    private const double HomeSingleColumnThreshold = 1100;
    private readonly MainViewModel viewModel;
    private readonly AsyncOperationGate monitoringStartGate = new();
    private readonly CancellationTokenSource applicationExitCancellation = new();
    private bool applicationExitRequested;
    private bool restoringBestChartListState;
    private double bestChartScrollOffset;
    private Func<Task>? applicationUpdatePrepareExitHandler;
    private Func<Task>? applicationUpdateExitHandler;
    private Action? applicationUpdateForceExitHandler;
    private Func<Task<bool>>? languageChangeRestartHandler;
    private bool languageChangeRestartRequested;

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
        ApplyHomeResponsiveLayout(Width);
        ApplyBestResponsiveLayout(Width);
        viewModel.ChartBestListReset += ViewModel_ChartBestListReset;
        viewModel.ChartBestSelectionRequested += ViewModel_ChartBestSelectionRequested;
        viewModel.ChartDetailUpdated += ViewModel_ChartDetailUpdated;
#if DEBUG
        AddDeveloperActions();
#endif
        Localization.ApplyToWindow(this);
    }

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
        RenderChartDetailGraph();
    }

    private void ApplyBestResponsiveLayout(double windowWidth)
    {
        if (BestChartGrid is null || NavigationColumn is null || MainContentGrid is null)
        {
            return;
        }

        var compact = windowWidth <= 1100;
        NavigationColumn.Width = new GridLength(windowWidth <= 760 ? 170 : compact ? 190 : 230);
        MainContentGrid.Margin = compact
            ? new Thickness(20)
            : new Thickness(28, 24, 28, 24);
        BestChartGrid.Tag = compact ? "Compact" : "Wide";

        if (BestChartGrid.Columns.Count != 8)
        {
            return;
        }

        var weights = compact
            ? new[] { 2.1, 1.05, 1.3, 0.65, 0.85, 0.85, 0.95, 1.15 }
            : new[] { 2.4, 1.35, 1.55, 0.8, 1.0, 1.0, 1.05, 1.3 };
        for (var index = 0; index < weights.Length; index++)
        {
            BestChartGrid.Columns[index].Width =
                new DataGridLength(weights[index], DataGridLengthUnitType.Star);
        }
    }

    private void ApplyHomeResponsiveLayout(double windowWidth)
    {
        if (LatestFeaturedBody is null || LatestInfoBorder is null || LatestMain is null)
        {
            return;
        }

        var singleColumn = windowWidth <= HomeSingleColumnThreshold;
        LatestFeaturedBody.ColumnDefinitions[1].Width = singleColumn
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        LatestFeaturedBody.ColumnDefinitions[1].MinWidth = singleColumn ? 0 : 250;
        Grid.SetColumn(LatestInfoBorder, singleColumn ? 0 : 1);
        Grid.SetRow(LatestInfoBorder, singleColumn ? 1 : 0);
        LatestMain.Margin = singleColumn
            ? new Thickness(0)
            : new Thickness(0, 0, 24, 0);
        LatestInfoBorder.Margin = singleColumn
            ? new Thickness(0, 12, 0, 0)
            : new Thickness(0);
        LatestInfoBorder.Padding = singleColumn
            ? new Thickness(0, 12, 0, 0)
            : new Thickness(18, 0, 0, 0);
        LatestInfoBorder.BorderThickness = singleColumn
            ? new Thickness(0, 1, 0, 0)
            : new Thickness(1, 0, 0, 0);
    }

    private void ShowHome_Click(object sender, RoutedEventArgs e) => ShowHomePage();

    private void ShowHomePage()
    {
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(false);
        ContentTabs.SelectedIndex = 0;
        PageTitle.Text = Localization.Get("ホーム");
        PageSubtitle.Text = Localization.Get("今日のプレー状況と最近の記録を確認できます");
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = "Selected";
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = null;
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = null;
    }

    private void ShowBest_Click(object sender, RoutedEventArgs e) => ShowBestPage();

    private void ShowBestPage()
    {
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(false);
        ContentTabs.SelectedIndex = 1;
        PageTitle.Text = Localization.Get("自己ベスト");
        PageSubtitle.Text = Localization.Get("保存済み全履歴から算出した譜面別ベスト");
        Localization.ApplyToWindow(this);
        UpdateBestPlayStyleButtons();
        RestoreBestChartListState();
        HomeNavigation.Tag = null;
        BestNavigation.Tag = "Selected";
        HistoryNavigation.Tag = null;
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = null;
    }

    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowSettingsPage();

    private void ShowSettingsPage()
    {
        viewModel.SetDataManagementPage(false);
        viewModel.SetSettingsPage(true);
        ContentTabs.SelectedIndex = 4;
        PageTitle.Text = Localization.Get("設定");
        PageSubtitle.Text = Localization.Get("自動記録と表示に関する設定を変更できます");
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = null;
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = null;
        SettingsNavigation.Tag = "Selected";
        DataManagementNavigation.Tag = null;
    }

    private void ShowDataManagement_Click(object sender, RoutedEventArgs e) =>
        ShowDataManagementPage();

    private void ShowDataManagementPage()
    {
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(true);
        ContentTabs.SelectedIndex = 5;
        PageTitle.Text = Localization.Get("データ管理");
        PageSubtitle.Text = Localization.Get("保存済みプレーと楽曲・譜面データの状態を確認できます");
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = null;
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = null;
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
        if (!viewModel.SaveUserSettings() ||
            !languageChanged ||
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
    }

    private void ResetBestFilters_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ResetBestFilters();
        UpdateBestPlayStyleButtons();
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
            current = VisualTreeHelper.GetParent(current);
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
        if (e.ExtentHeight <= 0 || e.ViewportHeight <= 0)
        {
            return;
        }

        bestChartScrollOffset = e.VerticalOffset;

        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1)
        {
            viewModel.LoadMoreChartBests();
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

    private void ShowChartDetail()
    {
        viewModel.SetSettingsPage(false);
        ContentTabs.SelectedIndex = 2;
        PageTitle.Text = Localization.Get("楽曲・譜面詳細");
        PageSubtitle.Text = Localization.Get("自己ベストから選択した1譜面の記録とプレー推移を確認できます");
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = null;
        BestNavigation.Tag = "Selected";
        HistoryNavigation.Tag = null;
        SettingsNavigation.Tag = null;
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
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(BestChartGrid);
                scrollViewer?.ScrollToVerticalOffset(bestChartScrollOffset);
                restoringBestChartListState = true;
                try
                {
                    if (viewModel.SelectedChartBest is not null)
                    {
                        BestChartGrid.SelectedItem = viewModel.SelectedChartBest;
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
        bestChartScrollOffset = 0;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (BestChartGrid.Items.Count > 0)
                {
                    BestChartGrid.ScrollIntoView(BestChartGrid.Items[0]);
                }
            }));
    }

    private void ShowHistory_Click(object sender, RoutedEventArgs e) => ShowHistoryPage();

    private void ShowHistoryPage()
    {
        viewModel.SetSettingsPage(false);
        viewModel.SetDataManagementPage(false);
        ContentTabs.SelectedIndex = 3;
        PageTitle.Text = Localization.Get("直近プレー履歴");
        PageSubtitle.Text = Localization.Get("保存済みのプレーを新しい順に表示します");
        Localization.ApplyToWindow(this);
        HomeNavigation.Tag = null;
        BestNavigation.Tag = null;
        HistoryNavigation.Tag = "Selected";
        SettingsNavigation.Tag = null;
        DataManagementNavigation.Tag = null;
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
                FontSize = 10,
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
                ? new SolidColorBrush(WpfColor.FromRgb(5, 150, 105))
                : (WpfBrush)FindResource("AccentPrimaryBrush"),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        ChartDetailGraphCanvas.Children.Add(line);

        var bestBrush = new SolidColorBrush(WpfColor.FromRgb(5, 150, 105));
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
                Stroke = WpfBrushes.White,
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
            FontSize = 10,
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
                FontSize = 10,
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
