using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using DDRGpScoreViewer;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class LocalizationViewTests(LocalizationApplicationFixture applicationFixture)
    : IClassFixture<LocalizationApplicationFixture>
{
    [Fact]
    public void Applying_localization_keeps_recycled_data_grid_row_display_bound()
    {
        applicationFixture.Run(() =>
        {
            Window? window = null;
            try
            {
                var items = Enumerable.Range(0, 20)
                    .Select(index => new GridItem($"song-{index:D2}"))
                    .ToArray();
                var dataGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    EnableRowVirtualization = true,
                    Height = 80,
                    ItemsSource = items,
                    RowHeight = 40,
                };
                VirtualizingPanel.SetIsVirtualizing(dataGrid, true);
                VirtualizingPanel.SetVirtualizationMode(dataGrid, VirtualizationMode.Recycling);
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Binding = new Binding(nameof(GridItem.Display)),
                });
                window = new Window
                {
                    Content = dataGrid,
                    Height = 120,
                    Width = 240,
                };
                window.Show();
                window.UpdateLayout();

                var realizedRows = FindVisualChildren<DataGridRow>(dataGrid).ToHashSet();
                var realizedDisplays = realizedRows
                    .Select(row => FindVisualChildren<TextBlock>(row).Single())
                    .ToHashSet();
                Assert.Contains(
                    realizedDisplays,
                    display => display.Text == items[0].Display);

                Localization.ApplyToWindow(window);
                var scrollViewer = FindVisualChildren<ScrollViewer>(dataGrid).Single();
                scrollViewer.ScrollToEnd();
                window.UpdateLayout();

                var recycledRow = (DataGridRow)dataGrid.ItemContainerGenerator.ContainerFromItem(items[^1]);
                var recycledDisplay = FindVisualChildren<TextBlock>(recycledRow).Single();
                Assert.Contains(recycledRow, realizedRows);
                Assert.Contains(recycledDisplay, realizedDisplays);
                Assert.Same(items[^1], recycledRow.DataContext);
                Assert.Equal(items[^1].Display, recycledDisplay.Text);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void Applying_localization_keeps_combo_box_selection_display_bound()
    {
        applicationFixture.Run(() =>
        {
            Window? window = null;
            try
            {
                window = new Window
                {
                    Width = 240,
                    Height = 120,
                };
                var comboBox = new ComboBox
                {
                    ItemsSource = new[]
                    {
                        new LocalizedOption(UserSettings.HomeStartupPage, "ホーム"),
                        new LocalizedOption(UserSettings.BestStartupPage, "自己ベスト"),
                    },
                    DisplayMemberPath = nameof(LocalizedOption.Display),
                };
                window.Content = comboBox;
                window.Show();
                comboBox.SelectedIndex = 0;
                window.UpdateLayout();

                Localization.ApplyToWindow(window);
                comboBox.SelectedIndex = 1;
                window.UpdateLayout();

                var display = FindVisualChildren<TextBlock>(comboBox)
                    .Single(textBlock => !string.IsNullOrWhiteSpace(textBlock.Text));
                Assert.Equal("自己ベスト", display.Text);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void Monitoring_status_card_tracks_state_reason_and_available_actions()
    {
        using var databaseFixture = new DatabaseFixture();
        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                Localization.Configure(UserSettings.JapaneseLanguage);
                foreach (var resourceName in new[] { "Theme.xaml", "Components.xaml", "Strings.xaml" })
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary
                        {
                            Source = new Uri(
                                $"/DDRGpScoreViewer;component/Resources/{resourceName}",
                                UriKind.Relative),
                        });
                }

                window = new MainWindow(
                    new ViewerDatabasePaths(
                        ViewerDatabaseEnvironment.Development,
                        databaseFixture.DirectoryPath,
                        databaseFixture.MasterPath,
                        databaseFixture.CatalogPath,
                        databaseFixture.ScorePath,
                        Path.Combine(databaseFixture.DirectoryPath, "evaluation.db"),
                        Path.Combine(databaseFixture.DirectoryPath, "data"),
                        Path.Combine(databaseFixture.DirectoryPath, "logs"),
                        Path.Combine(databaseFixture.DirectoryPath, "viewer-settings.json")))
                {
                    Width = 960,
                    Height = 640,
                };
                window.Show();
                window.UpdateLayout();

                Assert.Equal(960, window.MinWidth);
                Assert.Equal(640, window.MinHeight);
                Assert.Equal("待機中", window.MonitoringStateText.Text);
                Assert.Equal("—", window.MonitoringReasonText.Text);
                Assert.Equal(TextWrapping.Wrap, window.MonitoringReasonText.TextWrapping);
                Assert.Equal(Visibility.Collapsed, window.StartMonitoringButton.Visibility);
                Assert.Equal("監視を開始する", window.StartMonitoringButton.Content);
                Assert.True(window.StartMonitoringButton.IsEnabled);
                Assert.Equal(Visibility.Collapsed, window.StopMonitoringButton.Visibility);
                Assert.False(window.StopMonitoringButton.IsEnabled);
                Assert.Equal(
                    Color.FromRgb(56, 189, 248),
                    Assert.IsType<SolidColorBrush>(window.MonitoringStateIndicator.Fill).Color);
                Assert.Equal(
                    Color.FromRgb(56, 189, 248),
                    Assert.IsType<SolidColorBrush>(window.MonitoringStateText.Foreground).Color);

                window.ViewModel.StopContinuousCaptureAsync().GetAwaiter().GetResult();
                DrainDispatcher(window.Dispatcher);

                Assert.Equal("手動停止済み", window.MonitoringStateText.Text);
                Assert.Equal(Visibility.Visible, window.StartMonitoringButton.Visibility);
                Assert.Equal("監視を再開", window.StartMonitoringButton.Content);
                Assert.True(window.StartMonitoringButton.IsEnabled);
                Assert.Equal(Visibility.Collapsed, window.StopMonitoringButton.Visibility);
                Assert.False(window.StopMonitoringButton.IsEnabled);

                window.ViewModel.RequestApplicationExit();
                DrainDispatcher(window.Dispatcher);

                Assert.Equal("終了処理中", window.MonitoringStateText.Text);
                Assert.Contains("終了処理中", window.MonitoringReasonText.Text, StringComparison.Ordinal);
                Assert.Equal(Visibility.Collapsed, window.StartMonitoringButton.Visibility);
                Assert.False(window.StartMonitoringButton.IsEnabled);
                Assert.Equal(Visibility.Collapsed, window.StopMonitoringButton.Visibility);
                Assert.False(window.StopMonitoringButton.IsEnabled);
                Assert.Equal(
                    Color.FromRgb(100, 116, 139),
                    Assert.IsType<SolidColorBrush>(window.MonitoringStateIndicator.Fill).Color);
                Assert.Equal(
                    Color.FromRgb(100, 116, 139),
                    Assert.IsType<SolidColorBrush>(window.MonitoringStateText.Foreground).Color);
            }
            finally
            {
                if (window is not null)
                {
                    window.PrepareForApplicationExit();
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void Best_screen_switches_between_four_exclusive_modes_and_closes_choice_grid()
    {
        using var databaseFixture = new DatabaseFixture();
        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                foreach (var resourceName in new[] { "Theme.xaml", "Components.xaml", "Strings.xaml" })
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary
                        {
                            Source = new Uri(
                                $"/DDRGpScoreViewer;component/Resources/{resourceName}",
                                UriKind.Relative),
                        });
                }

                window = new MainWindow(
                    new ViewerDatabasePaths(
                        ViewerDatabaseEnvironment.Development,
                        databaseFixture.DirectoryPath,
                        databaseFixture.MasterPath,
                        databaseFixture.CatalogPath,
                        databaseFixture.ScorePath,
                        Path.Combine(databaseFixture.DirectoryPath, "evaluation.db"),
                        Path.Combine(databaseFixture.DirectoryPath, "data"),
                        Path.Combine(databaseFixture.DirectoryPath, "logs"),
                        Path.Combine(databaseFixture.DirectoryPath, "viewer-settings.json")))
                {
                    Width = 960,
                    Height = 640,
                };

                Assert.Equal(UserSettings.LevelBrowseMode, window.ViewModel.BestBrowseMode);
                Assert.Equal(UserSettings.DefaultBestLevel, window.ViewModel.BestLevelFilter);
                Assert.Equal(UserSettings.DefaultBestVersion, window.ViewModel.BestVersionFilter);

                window.ViewModel.BestLevelFilter = "level_17";
                window.ViewModel.Load(
                    databaseFixture.ScorePath,
                    databaseFixture.MasterPath,
                    databaseFixture.CatalogPath,
                    persist: false);
                window.Show();
                window.BestNavigation.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                DrainDispatcher(window.Dispatcher);

                Assert.Equal(UserSettings.LevelBrowseMode, window.ViewModel.BestBrowseMode);
                Assert.Equal(Visibility.Collapsed, window.BestAxisPanel.Visibility);
                Assert.Equal("レベルを変更", window.BestAxisChangeButton.Content);
                Assert.Equal(1, Grid.GetColumn(window.BestAxisChangeButton));
                Assert.Equal(new Thickness(0, 1, 0, 0), window.BestSelectionSummaryPanel.BorderThickness);
                Assert.Equal(2, Grid.GetRow(window.BestResultSummary));
                Assert.Equal(Visibility.Visible, window.BestProgressCard.Visibility);

                window.BestAxisChangeButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.BestAxisPanel.Visibility);

                window.BestLevelOptionsGrid.SelectedValue = "level_18";
                DrainDispatcher(window.Dispatcher);
                Assert.Equal("level_18", window.ViewModel.BestLevelFilter);
                Assert.Equal(Visibility.Collapsed, window.BestAxisPanel.Visibility);

                window.BestVersionModeButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(UserSettings.VersionBrowseMode, window.ViewModel.BestBrowseMode);
                Assert.Equal("バージョンを変更", window.BestAxisChangeButton.Content);
                Assert.Equal(Visibility.Visible, window.BestVersionOptionsPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.BestProgressCard.Visibility);

                window.BestGoalModeButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(UserSettings.GoalBrowseMode, window.ViewModel.BestBrowseMode);
                Assert.Equal("AAA", window.ViewModel.BestGoalFilter);
                var selectionValue = Assert.Single(
                    FindVisualChildren<TextBlock>(window.BestSelectionSummaryPanel),
                    textBlock => textBlock.Text == "AAAを目指す");
                Assert.Equal(8, selectionValue.Margin.Left);
                Assert.Equal("目標を変更", window.BestAxisChangeButton.Content);
                Assert.Equal(Visibility.Collapsed, window.BestProgressCard.Visibility);
                Assert.Equal(Visibility.Collapsed, window.BestResultActions.Visibility);
                Assert.Equal(Visibility.Collapsed, window.BestAxisPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.BestGoalOptionsPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.BestChartGrid.Columns[3].Visibility);
                Assert.Equal(Visibility.Visible, window.BestEmptyStatePanel.Visibility);

                window.BestAxisChangeButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.BestAxisPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.BestGoalOptionsPanel.Visibility);
                window.BestGoalOptionsGrid.SelectedValue = "AA+";
                DrainDispatcher(window.Dispatcher);
                Assert.Equal("AA+", window.ViewModel.BestGoalFilter);
                Assert.Equal(Visibility.Collapsed, window.BestAxisPanel.Visibility);

                window.BestTitleModeButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(UserSettings.TitleBrowseMode, window.ViewModel.BestBrowseMode);
                Assert.Equal(Visibility.Visible, window.BestTitleSearchPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.BestAxisPanel.Visibility);
                Assert.Equal(36, window.BestTitleSearchTextBox.MinHeight);
                Assert.Equal(Visibility.Collapsed, window.BestProgressCard.Visibility);
            }
            finally
            {
                if (window is not null)
                {
                    window.PrepareForApplicationExit();
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void Loading_the_next_best_page_keeps_the_previous_scroll_offset()
    {
        using var databaseFixture = new DatabaseFixture();
        for (var index = 2; index <= 101; index++)
        {
            databaseFixture.AddMasterSongAndChart(
                $"song-{index}",
                $"SONG {index:00}",
                "Artist",
                $"chart-{index}");
        }

        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                foreach (var resourceName in new[] { "Theme.xaml", "Components.xaml", "Strings.xaml" })
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary
                        {
                            Source = new Uri(
                                $"/DDRGpScoreViewer;component/Resources/{resourceName}",
                                UriKind.Relative),
                        });
                }
                window = new MainWindow(
                    new ViewerDatabasePaths(
                        ViewerDatabaseEnvironment.Development,
                        databaseFixture.DirectoryPath,
                        databaseFixture.MasterPath,
                        databaseFixture.CatalogPath,
                        databaseFixture.ScorePath,
                        Path.Combine(databaseFixture.DirectoryPath, "evaluation.db"),
                        Path.Combine(databaseFixture.DirectoryPath, "data"),
                        Path.Combine(databaseFixture.DirectoryPath, "logs"),
                        Path.Combine(databaseFixture.DirectoryPath, "viewer-paths.json")))
                {
                    Width = 960,
                    Height = 640,
                };
                window.ViewModel.BestBrowseMode = UserSettings.TitleBrowseMode;
                window.ViewModel.Load(
                    databaseFixture.ScorePath,
                    databaseFixture.MasterPath,
                    databaseFixture.CatalogPath,
                    persist: false);
                window.Show();
                var emptyButtonWasVisible = false;
                window.BestChartGrid.LayoutUpdated += (_, _) =>
                {
                    if (FindVisualChildren<Button>(window.BestChartGrid)
                        .Any(button =>
                            button.Content is null &&
                            button.Visibility == Visibility.Visible &&
                            button.Opacity > 0))
                    {
                        emptyButtonWasVisible = true;
                    }
                };
                window.BestNavigation.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                DrainDispatcher(window.Dispatcher);

                var scrollViewer = FindVisualChildren<ScrollViewer>(window.BestChartGrid).Single();
                var firstBottomOffset = scrollViewer.ScrollableHeight;
                scrollViewer.ScrollToVerticalOffset(firstBottomOffset);
                DrainDispatcher(window.Dispatcher);
                DrainDispatcher(window.Dispatcher);

                Assert.Equal(100, window.ViewModel.ChartBests.Count);
                Assert.Equal(firstBottomOffset, scrollViewer.VerticalOffset, precision: 3);

                var secondBottomOffset = scrollViewer.ScrollableHeight;
                scrollViewer.ScrollToVerticalOffset(secondBottomOffset);
                DrainDispatcher(window.Dispatcher);
                DrainDispatcher(window.Dispatcher);

                Assert.Equal(101, window.ViewModel.ChartBests.Count);
                Assert.Equal(secondBottomOffset, scrollViewer.VerticalOffset, precision: 3);
                Assert.False(emptyButtonWasVisible);
            }
            finally
            {
                if (window is not null)
                {
                    window.PrepareForApplicationExit();
                    window.Close();
                }
            }
        });
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record GridItem(string Display);
}

public sealed class LocalizationApplicationFixture : IDisposable
{
    private readonly Application application;
    private readonly Thread applicationThread;

    public LocalizationApplicationFixture()
    {
        using var ready = new ManualResetEventSlim();
        Application? createdApplication = null;
        Exception? startupFailure = null;
        applicationThread = new Thread(() =>
        {
            try
            {
                createdApplication = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                ready.Set();
                createdApplication.Run();
            }
            catch (Exception exception)
            {
                startupFailure = exception;
                ready.Set();
            }
        })
        {
            IsBackground = true,
        };
        applicationThread.SetApartmentState(ApartmentState.STA);
        applicationThread.Start();
        ready.Wait();

        if (startupFailure is not null)
        {
            throw new AggregateException(startupFailure);
        }

        application = createdApplication
            ?? throw new InvalidOperationException("WPF Application was not created.");
    }

    public void Run(Action action) => application.Dispatcher.Invoke(action);

    public void Dispose()
    {
        application.Dispatcher.BeginInvoke(() => application.Shutdown());
        applicationThread.Join();
    }
}
