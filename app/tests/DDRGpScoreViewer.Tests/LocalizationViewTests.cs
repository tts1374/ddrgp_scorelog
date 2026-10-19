using System.Threading;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DDRGpScoreViewer;
using DDRGpScoreViewer.Controls;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.WebBestSync;
using DDRGpScoreViewer.WebIdentity;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DDRGpScoreViewer.Tests;

[CollectionDefinition("Localized WPF views", DisableParallelization = true)]
public sealed class LocalizationViewCollection;

[Collection("Localized WPF views")]
public sealed class LocalizationViewTests(LocalizationApplicationFixture applicationFixture)
    : IClassFixture<LocalizationApplicationFixture>
{
    [Theory]
    [InlineData(true, "ja", "light", 1280, 760)]
    [InlineData(false, "en", "dark", 960, 640)]
    [InlineData(true, "ko", "light", 960, 640)]
    public void History_keeps_calendar_groups_paging_selection_and_pinned_header_reachable(
        bool dateChangesAtPage, string language, string theme, double width, double height)
    {
        using var fixture = new DatabaseFixture();
        var local = new DateTime(2026, 10, 18, 0, dateChangesAtPage ? 49 : 55, 0);
        var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        fixture.AddMasterSongAndChart("long-song", "アルストロメリア (walk with you remix) — A long title for wrapping",
            "Artist", "double-chart", "DOUBLE", "CHALLENGE", 16);
        for (var index = 0; index < 101; index++)
        {
            fixture.AddPlay($"history-{index:000}", start.AddMinutes(-index).ToString("O"),
                980_000 - index * 10, 1_500 - index,
                index % 2 == 0 ? "song-1" : "long-song", index % 2 == 0 ? "chart-1" : "double-chart");
        }

        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                PrepareStartupResources();
                Localization.Configure(language);
                ThemeManager.Apply(theme);
                window = new MainWindow(StartupPaths(fixture)) { Width = width, Height = height };
                window.ViewModel.Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath, persist: false);
                window.Show();
                window.HistoryNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                var grid = window.RecentPlayGrid;
                var scroll = FindVisualChildren<ScrollViewer>(grid).Single();
                var view = (System.ComponentModel.ICollectionView)grid.ItemsSource;
                Assert.Equal(50, window.ViewModel.Plays.Count);
                Assert.Single(view.Groups!);

                var selected = window.ViewModel.Plays[47];
                grid.SelectedItem = selected;
                DrainDispatcher(window.Dispatcher);
                var firstOffset = scroll.ScrollableHeight;
                scroll.ScrollToEnd();
                DrainDispatcher(window.Dispatcher);
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(100, window.ViewModel.Plays.Count);
                Assert.Equal(firstOffset, scroll.VerticalOffset, precision: 3);
                Assert.Same(selected, window.ViewModel.SelectedPlay);
                Assert.Same(selected, window.HistoryDetailGrid.DataContext);
                var groups = view.Groups!.Cast<CollectionViewGroup>().ToArray();
                Assert.Equal(2, groups.Length);
                Assert.Equal(dateChangesAtPage ? 50 : 56, groups[0].ItemCount);
                Assert.Equal(window.ViewModel.Plays, groups.SelectMany(group => group.Items.Cast<PlayHistoryItem>()));
                Assert.NotEqual(window.ViewModel.Plays[49].HistoryDateDisplay == window.ViewModel.Plays[50].HistoryDateDisplay,
                    dateChangesAtPage);

                var secondOffset = scroll.ScrollableHeight;
                scroll.ScrollToEnd();
                DrainDispatcher(window.Dispatcher);
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(101, window.ViewModel.Plays.Count);
                Assert.Equal(secondOffset, scroll.VerticalOffset, precision: 3);
                Assert.Equal(101, grid.Items.Count);

                grid.SelectedIndex = 100;
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(window.ViewModel.Plays[100].HistoryDateDisplay, window.HistoryStickyDateText.Text);
                var source = PresentationSource.FromVisual(window)!;
                var viewport = FindVisualChildren<ScrollContentPresenter>(grid).Single();
                for (var index = 100; index >= 0; index--)
                {
                    var play = window.ViewModel.Plays[index];
                    Assert.Same(play, window.ViewModel.SelectedPlay);
                    Assert.Same(play, window.HistoryDetailGrid.DataContext);
                    var detailTexts = FindVisualChildren<TextBlock>(window.HistoryDetailGrid).Select(text => text.Text).ToArray();
                    Assert.Contains(play.SongTitle, detailTexts);
                    Assert.Contains(play.ScoreDisplay, detailTexts);
                    Assert.Contains(play.PlayedAtDisplay, detailTexts);
                    Assert.Contains(FindVisualChildren<TextBlock>(window.HistoryDetailGrid)
                            .SelectMany(text => text.Inlines.OfType<System.Windows.Documents.Run>()),
                        run => run.Text == play.ExScoreDisplay);
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(play);
                    Assert.NotNull(row);
                    var top = row.TranslatePoint(new Point(), grid).Y;
                    var pinnedBottom = window.HistoryStickyDate.Margin.Top + window.HistoryStickyDate.ActualHeight;
                    Assert.True(top >= pinnedBottom - 1, $"{index}: row {top} hidden by date {pinnedBottom}");
                    var visibleBottom = viewport.TranslatePoint(new Point(), grid).Y + Math.Min(viewport.ActualHeight, scroll.ViewportHeight);
                    var horizontalBar = FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(grid)
                        .Single(bar => bar.Orientation == Orientation.Horizontal);
                    if (horizontalBar.IsVisible)
                    {
                        visibleBottom = Math.Min(visibleBottom, horizontalBar.TranslatePoint(new Point(), grid).Y);
                    }
                    Assert.True(top + row.ActualHeight <= visibleBottom + 1,
                        $"{index}: row bottom {top + row.ActualHeight} exceeds viewport {visibleBottom}");
                    if (index > 0)
                    {
                        grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Up)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        });
                        DrainDispatcher(window.Dispatcher);
                    }
                }
                Assert.Equal(window.ViewModel.Plays[0].HistoryDateDisplay, window.HistoryStickyDateText.Text);
                window.HistoryDetailScrollViewer.ScrollToEnd();
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(window.HistoryDetailScrollViewer.ScrollableHeight, window.HistoryDetailScrollViewer.VerticalOffset);
                Assert.Equal(0, window.HistoryDetailScrollViewer.ScrollableWidth);
                Assert.Contains(Localization.Get("判定"), FindVisualChildren<TextBlock>(window.HistoryDetailGrid).Select(text => text.Text));
                Assert.Equal(Localization.Format("{0}件を表示", 101), window.ViewModel.RecentPlayLoadMoreHintDisplay);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
                ThemeManager.Apply(UserSettings.LightTheme);
                Localization.Configure(UserSettings.JapaneseLanguage);
            }
        });
    }

    [Fact]
    public void History_keeps_the_selected_last_row_visible_after_a_page_changes_the_scrollbars()
    {
        using var fixture = new DatabaseFixture();
        var start = DateTimeOffset.Now;
        for (var index = 0; index < 101; index++)
        {
            fixture.AddPlay($"last-row-{index:000}", start.AddMinutes(-index).ToString("O"), 980_000, 1_500);
        }
        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                PrepareStartupResources();
                window = new MainWindow(StartupPaths(fixture));
                window.ViewModel.Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath, persist: false);
                window.Show();
                window.HistoryNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                foreach (var lastIndex in new[] { 49, 99, 100 })
                {
                    var grid = window.RecentPlayGrid;
                    grid.SelectedIndex = lastIndex;
                    grid.CurrentCell = new DataGridCellInfo(grid.SelectedItem, grid.Columns.Last());
                    grid.ScrollIntoView(grid.SelectedItem, grid.Columns.Last());
                    DrainDispatcher(window.Dispatcher);
                    DrainDispatcher(window.Dispatcher);
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem);
                    var viewport = FindVisualChildren<ScrollContentPresenter>(grid).Single();
                    var scroll = FindVisualChildren<ScrollViewer>(grid).Single();
                    var bottom = row.TranslatePoint(new Point(), viewport).Y + row.ActualHeight;
                    Assert.True(bottom <= Math.Min(viewport.ActualHeight, scroll.ViewportHeight) + 1,
                        $"{lastIndex}: {bottom} > {scroll.ViewportHeight}");
                    var horizontalBar = FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(grid)
                        .Single(bar => bar.Orientation == Orientation.Horizontal);
                    if (horizontalBar.IsVisible)
                    {
                        Assert.True(row.TranslatePoint(new Point(), grid).Y + row.ActualHeight <=
                            horizontalBar.TranslatePoint(new Point(), grid).Y + 1);
                    }
                    Assert.Same(window.ViewModel.Plays[lastIndex], window.HistoryDetailGrid.DataContext);
                    Assert.Equal(lastIndex == 49 ? 100 : 101, window.ViewModel.Plays.Count);
                }
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
            }
        });
    }

    [Theory]
    [InlineData("ja", "light", 1280, 760, false, "今日の記録をコピー")]
    [InlineData("en", "dark", 960, 640, true, "Copy today’s records")]
    [InlineData("ko", "light", 960, 640, true, "오늘 기록 복사")]
    public void Home_keeps_both_lists_copy_and_navigation_reachable(
        string language, string theme, double width, double height, bool longTitle, string copyButtonText)
    {
        using var fixture = new DatabaseFixture();
        var now = HomeDisplayPeriod.From(DateTimeOffset.Now).Start.AddHours(5);
        for (var index = 0; index < 6; index++)
        {
            var songId = $"home-song-{index}";
            var chartId = $"home-chart-{index}";
            fixture.AddMasterSongAndChart(songId,
                longTitle ? "アルストロメリア (walk with you remix) — A long title to check wrapping" : "POSSESSION",
                "Artist", chartId);
            fixture.AddPlay($"before-{index}", now.AddDays(-1).AddMinutes(-index).ToString("O"),
                900_000, 1_000, songId, chartId);
            fixture.AddPlay($"latest-{index}", now.AddMinutes(-index).ToString("O"),
                980_000, 1_500, songId, chartId, flareRank: "IX", ok: 4, calories: 15.3);
        }

        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                PrepareStartupResources();
                Localization.Configure(language);
                ThemeManager.Apply(theme);
                window = new MainWindow(StartupPaths(fixture)) { Width = width, Height = height };
                window.ViewModel.Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath, persist: false);
                window.Show();
                DrainDispatcher(window.Dispatcher);

                Assert.Equal(copyButtonText, window.HomeSummaryCopyButton.Content);
                Assert.Equal(5, window.ViewModel.HomeRecentPlays.Count);
                Assert.Equal(5, window.ViewModel.HomeBestUpdates.Count);
                var lists = FindVisualChildren<ItemsControl>(window.HomeListsGrid).ToArray();
                Assert.Contains(lists, list => ReferenceEquals(list.ItemsSource, window.ViewModel.HomeRecentPlays));
                Assert.Contains(lists, list => ReferenceEquals(list.ItemsSource, window.ViewModel.HomeBestUpdates));
                Assert.Equal(0, window.HomeScrollViewer.ScrollableWidth);
                // A hosted desktop can constrain native window bounds. Verify access
                // here; the 1280x760 one-screen fit is checked on the real 100% desktop.
                window.HomeScrollViewer.ScrollToEnd();
                DrainDispatcher(window.Dispatcher);
                if (longTitle)
                {
                    Assert.True(window.HomeScrollViewer.VerticalOffset > 0);
                }
                Assert.Equal(window.HomeScrollViewer.ScrollableHeight, window.HomeScrollViewer.VerticalOffset);

                window.HomeScrollViewer.ScrollToTop();
                window.HomeSummaryCopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(window.ViewModel.HomeTodaySummaryCopyText, Clipboard.GetText());
                Assert.Equal(Localization.Get("コピーしました"), window.HomeSummaryCopyButton.Content);

                var historyLink = FindVisualChildren<Button>(window.HomeHistoryPanel).Single();
                historyLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal("Selected", window.HistoryNavigation.Tag);
                window.HomeNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                var bestLink = FindVisualChildren<Button>(window.HomeUpdatesPanel).Single();
                bestLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal("Selected", window.BestNavigation.Tag);
                window.HomeNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal("Selected", window.HomeNavigation.Tag);
                Assert.NotNull(window.HomeLatestPlayCard.DataContext);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
                ThemeManager.Apply(UserSettings.LightTheme);
                Localization.Configure(UserSettings.JapaneseLanguage);
            }
        });
    }

#if DEBUG
    [Theory]
    [InlineData("not-sqlite")]
    [InlineData("invalid-timestamp")]
    [InlineData("missing-table")]
    [InlineData("locked")]
    [InlineData("blocked-parent")]
    [InlineData("read-only")]
    [InlineData("read-only-pending")]
    public async Task Sync_state_failure_preserves_local_startup_history_and_formal_save(string failure)
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("existing", "2026-10-01T10:00:00+09:00", 900_000, 1_000);
        var paths = StartupPaths(fixture);
        paths.EnsureDefaultDirectories();
        new FileWebPlayerIdentityStore(paths.WebPlayerIdentityPath, paths.WebPlayerCredentialPath)
            .SaveRegistered("p_XXXXXXXXXXXXXXXXXXXXXX", "fixture-credential", "Existing player");
        File.WriteAllText(paths.WebPlayerCredentialPath + ".authorization", "preserved authorization fixture");
        if (failure == "read-only-pending")
        {
            new FileAppAuthorizationStore(paths.WebPlayerCredentialPath + ".authorization").Save(new(
                "fixture-authorization", "fixture-secret", "login", "p_XXXXXXXXXXXXXXXXXXXXXX",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10),
                State: AppAuthorizationState.CredentialActivationPending,
                Credential: "fixture-pending-credential"));
        }
        var protectedStatePath = paths.WebBestSyncStatePath;
        FileAttributes? originalStateAttributes = null;
        if (failure == "blocked-parent")
        {
            protectedStatePath = Path.Combine(paths.DataDirectory, "web-sync");
            File.WriteAllText(protectedStatePath, "preserved blocking file");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(protectedStatePath)!);
            if (failure == "not-sqlite")
            {
                File.WriteAllText(protectedStatePath, "preserved non-SQLite fixture");
            }
            else
            {
                var stateStore = new SqliteWebBestSyncStateStore(protectedStatePath);
                stateStore.SetEnabled(true);
                if (failure is "read-only" or "read-only-pending")
                {
                    originalStateAttributes = File.GetAttributes(protectedStatePath);
                    File.SetAttributes(protectedStatePath, originalStateAttributes.Value | FileAttributes.ReadOnly);
                    // Reproduce a store whose read succeeds but whose write must be rejected.
                    Assert.True(stateStore.Load().Enabled);
                }
                if (failure is "invalid-timestamp" or "missing-table")
                {
                    using var connection = new SqliteConnection($"Data Source={protectedStatePath};Pooling=False");
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = failure == "missing-table"
                        ? "DROP TABLE web_best_sync_entries;"
                        : "UPDATE web_best_sync_metadata SET last_success_at = 'invalid timestamp';";
                    command.ExecuteNonQuery();
                }
            }
        }
        var protectedFiles = new[] { fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath,
            protectedStatePath, paths.WebPlayerIdentityPath, paths.WebPlayerCredentialPath,
            paths.WebPlayerCredentialPath + ".authorization" }
            .ToDictionary(path => path, File.ReadAllBytes);
        using var locked = failure == "locked"
            ? new FileStream(protectedStatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            : null;
        using var handler = new StartupWebHandler();

        await applicationFixture.RunAsync(async () =>
        {
            MainWindow? window = null;
            try
            {
                PrepareStartupResources();
                window = new MainWindow(paths, handler);
                await window.RestoreSavedPathsAsync();
                window.Show();
                window.HistoryNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal("existing", Assert.Single(window.ViewModel.Plays).PlayId);
                Assert.Single(window.RecentPlayGrid.Items.Cast<object>());
                Assert.True(window.ViewModel.CanRunDeveloperOperations);
                // Startup and browsing must leave the formal DB byte-for-byte unchanged.
                Assert.Equal(protectedFiles[fixture.ScorePath], File.ReadAllBytes(fixture.ScorePath));
                Assert.False(window.ViewModel.IsWebBestSyncAvailable);
                Assert.False(window.ViewModel.CanBeginWebAuthorization);
                Assert.False(window.ViewModel.CanSyncWebBestsNow);
                Assert.False(window.ViewModel.CanToggleWebSync);
                Assert.False(window.ViewModel.CanDeletePublicBests);
                Assert.False(window.ViewModel.CanReplaceWebBests);
                window.SettingsNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal("Web同期状態を読み込めません", window.WebBestSyncStatusText.Text);
                Assert.Contains("Web送信を停止", window.WebBestSyncStatusMessageText.Text);
                Assert.Contains("再起動", window.WebBestSyncStatusMessageText.Text);
                Assert.Contains(paths.WebBestSyncStatePath, window.ViewModel.WebBestSyncDetails);
                if (failure is "read-only" or "read-only-pending")
                {
                    Assert.Contains("SQLite Error 8", window.ViewModel.WebBestSyncDetails);
                }
                Assert.False(string.IsNullOrWhiteSpace(window.ViewModel.WebBestSyncDetails));
                Assert.Equal("不明", window.ViewModel.WebBestSyncLastSuccessDisplay);
                Assert.False(window.ConnectWebAccountButton.IsEnabled);

                await window.ViewModel.ResumeWebBestSyncAsync();
                await window.ViewModel.ToggleWebBestSyncAsync();
                await window.ViewModel.SyncWebBestsNowAsync();
                await window.ViewModel.DeletePublicBestsAsync();
                await window.ViewModel.RefreshWebPlayerAsync();
                await window.ViewModel.BeginWebAuthorizationAsync(false, CancellationToken.None);
                await window.ViewModel.CompleteWebAuthorizationAsync();
                await window.ViewModel.CancelWebAuthorizationAsync();
                await window.ViewModel.ForgetInvalidWebIdentityAsync();
                Assert.Null(await window.ViewModel.PrepareWebBestReplacementAsync(CancellationToken.None));
                Assert.False(await window.ViewModel.ConfirmWebBestReplacementAsync(CancellationToken.None));
                await window.ViewModel.CancelWebBestReplacementAsync(CancellationToken.None);
                Assert.Equal(0, handler.RequestCount);
                // Exercise the real app-owned writer through the existing UI save entry.
                await window.ViewModel.SaveAndReloadConfiguredAsync(WriteStartupSaveWorkflow(fixture.DirectoryPath));
                Assert.Equal("プレーを保存しました", window.ViewModel.SaveStatusTitle);
                Assert.Equal(2, window.ViewModel.Plays.Count);
                Assert.Contains(window.ViewModel.Plays, play => play.PlayId == "existing");
                Assert.Contains(window.ViewModel.Plays, play => play.PlayId == "new-local-play");
                Assert.False(protectedFiles[fixture.ScorePath].SequenceEqual(File.ReadAllBytes(fixture.ScorePath)));
                Assert.Equal(0, handler.RequestCount);
                Assert.Equal("Web同期状態を読み込めません", window.ViewModel.WebBestSyncStatusTitle);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
                if (originalStateAttributes is { } attributes)
                {
                    File.SetAttributes(protectedStatePath, attributes);
                }
            }
        });
        locked?.Dispose();
        foreach (var (path, bytes) in protectedFiles.Where(item => item.Key != fixture.ScorePath))
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }
#endif

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Healthy_sync_startup_preserves_unregistered_off_and_enabled_states(bool registered, bool enabled)
    {
        using var fixture = new DatabaseFixture();
        var paths = StartupPaths(fixture);
        paths.EnsureDefaultDirectories();
        if (registered)
        {
            new FileWebPlayerIdentityStore(paths.WebPlayerIdentityPath, paths.WebPlayerCredentialPath)
                .SaveRegistered("p_XXXXXXXXXXXXXXXXXXXXXX", "fixture-credential", "Existing player");
            new SqliteWebBestSyncStateStore(paths.WebBestSyncStatePath).SetEnabled(enabled);
        }
        var initialSyncBytes = registered ? File.ReadAllBytes(paths.WebBestSyncStatePath) : null;
        using var handler = new StartupWebHandler();
        await applicationFixture.RunAsync(async () =>
        {
            MainWindow? window = null;
            try
            {
                PrepareStartupResources();
                window = new MainWindow(paths, handler);
                await window.RestoreSavedPathsAsync();
                if (initialSyncBytes is not null)
                {
                    Assert.Equal(initialSyncBytes, File.ReadAllBytes(paths.WebBestSyncStatePath));
                }
                Assert.True(window.ViewModel.HasData);
                Assert.True(window.ViewModel.IsWebBestSyncAvailable);
                Assert.Equal(enabled, window.ViewModel.WebBestSyncEnabled);
                Assert.Equal(registered, window.ViewModel.CanToggleWebSync);
                Assert.Equal(enabled, window.ViewModel.CanSyncWebBestsNow);
                await window.ViewModel.ResumeWebBestSyncAsync();
                Assert.Equal(enabled ? 1 : 0, handler.RequestCount);
                Assert.Equal(Localization.Get(enabled ? "同期済み" : "連携停止中"), window.ViewModel.WebBestSyncStatusTitle);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
            }
        });
    }

    private static ViewerDatabasePaths StartupPaths(DatabaseFixture fixture) => new(
        ViewerDatabaseEnvironment.Development, fixture.DirectoryPath, fixture.MasterPath,
        fixture.CatalogPath, fixture.ScorePath, null, Path.Combine(fixture.DirectoryPath, "data"),
        Path.Combine(fixture.DirectoryPath, "logs"), Path.Combine(fixture.DirectoryPath, "viewer-settings.json"));

    private static void PrepareStartupResources()
    {
        foreach (var resourceName in new[] { "Theme.xaml", "Components.xaml", "Strings.xaml" })
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/DDRGpScoreViewer;component/Resources/{resourceName}", UriKind.Relative),
            });
        }
        Localization.Configure(UserSettings.JapaneseLanguage);
    }

    private sealed class StartupWebHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal("/api/v1/me", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"public_player_id":"p_XXXXXXXXXXXXXXXXXXXXXX","display_name":"Existing player"}
                    """, Encoding.UTF8, "application/json"),
            });
        }
    }

#if DEBUG
    private static string WriteStartupSaveWorkflow(string root)
    {
        var path = Path.Combine(root, "workflow.json");
        File.WriteAllText(path, """
            {"workflow_schema_version":1,"analysis_detail":null,"save_input":{
              "input_schema_version":1,"candidate_material":{},
              "capture_id":"new-capture","capture_hash":"sha256:new-capture",
              "captured_at":"2026-10-17T10:00:00+09:00","source_kind":"manual","source_path":"fixture",
              "analysis_id":"new-analysis","event_type":"confirmed","confirmed_result":true,"duplicate":false,
              "confirmation_mode":"manual","identity_signal_status":"reviewed","digit_review_status":"reviewed",
              "analysis_confidence":0.99,"analysis_summary_json":"{}","app_version":"test",
              "formal_play":{"play_id":"new-local-play","played_at":"2026-10-17T10:00:00+09:00",
                "master_version":"master-v1","song_id":"song-1","chart_id":"chart-1","score":950000,
                "max_combo":456,"marvelous":400,"perfect":40,"great":10,"good":4,"miss":2,"ex_score":1750,
                "rank":"AAA","clear_type":"CLEAR","flare_rank":null,"ok":null,"calories":null,
                "duplicate_key":"play:v1:new-local-play"},
              "exclusion":null,"manifest_image_path":"","frame_index":null,"timestamp_ms":null,
              "candidate_duration_ms":null,"log_path":""}}
            """, new UTF8Encoding(false));
        return path;
    }
#endif

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
    public void Common_control_styles_keep_keyboard_focus_visible_without_resizing()
    {
        using var databaseFixture = new DatabaseFixture();
        applicationFixture.Run(() =>
        {
            MainWindow? resourceWindow = null;
            Window? host = null;
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

                resourceWindow = new MainWindow(
                    new ViewerDatabasePaths(
                        ViewerDatabaseEnvironment.Development,
                        databaseFixture.DirectoryPath,
                        databaseFixture.MasterPath,
                        databaseFixture.CatalogPath,
                        databaseFixture.ScorePath,
                        Path.Combine(databaseFixture.DirectoryPath, "evaluation.db"),
                        Path.Combine(databaseFixture.DirectoryPath, "data"),
                        Path.Combine(databaseFixture.DirectoryPath, "logs"),
                        Path.Combine(databaseFixture.DirectoryPath, "viewer-settings.json")));

                var primaryButton = new Button
                {
                    Content = "Primary",
                    Style = (Style)resourceWindow.FindResource("PrimaryButtonStyle"),
                };
                var selectedSegment = new Button
                {
                    Content = "SP",
                    Tag = "Selected",
                    Style = (Style)resourceWindow.FindResource("BestSegmentButtonStyle"),
                };
                var disabledLink = new Button
                {
                    Content = "Disabled link",
                    IsEnabled = false,
                    Style = (Style)resourceWindow.FindResource("HomeSectionLinkStyle"),
                };
                var titleSearch = new TextBox
                {
                    Text = "Search",
                    Style = (Style)resourceWindow.FindResource("BestTitleSearchTextBoxStyle"),
                };
                var options = new ListBox
                {
                    Height = 48,
                    ItemContainerStyle = (Style)resourceWindow.FindResource("BestAxisOptionStyle"),
                    ItemsSource = new[] { "Option" },
                    SelectedIndex = 0,
                };
                host = new Window
                {
                    Width = 480,
                    Height = 220,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            primaryButton,
                            selectedSegment,
                            disabledLink,
                            titleSearch,
                            options,
                        },
                    },
                };
                host.Show();
                host.Activate();
                host.UpdateLayout();

                var focusVisualStyle = Application.Current.FindResource("ControlFocusVisualStyle");
                foreach (var styleKey in new[]
                         {
                             "PrimaryButtonStyle",
                             "SidebarButtonStyle",
                             "HomeSectionLinkStyle",
                             "HomeSummaryCopyButtonStyle",
                             "BestStyleButtonStyle",
                             "BestSegmentButtonStyle",
                             "BestBrowseTabButtonStyle",
                             "BestAxisChangeButtonStyle",
                             "BestTitleSearchTextBoxStyle",
                             "BestAxisOptionStyle",
                             "SettingsSinglePlayStyleButtonStyle",
                             "SettingsDoublePlayStyleButtonStyle",
                         })
                {
                    var style = Assert.IsType<Style>(resourceWindow.FindResource(styleKey));
                    Assert.Same(
                        focusVisualStyle,
                        FindStyleSetterValue(style, Control.FocusVisualStyleProperty));
                }

                Assert.Same(focusVisualStyle, primaryButton.FocusVisualStyle);
                Assert.Same(focusVisualStyle, titleSearch.FocusVisualStyle);

                var selectedSegmentBorder = Assert.IsType<Border>(
                    selectedSegment.Template.FindName("SegmentButtonBorder", selectedSegment));
                Assert.Equal(new Thickness(2), selectedSegmentBorder.BorderThickness);
                Assert.Equal(
                    ((SolidColorBrush)resourceWindow.FindResource("AccentPrimaryBrush")).Color,
                    Assert.IsType<SolidColorBrush>(selectedSegmentBorder.BorderBrush).Color);
                Assert.Equal(0.6, disabledLink.Opacity, precision: 3);

                var listItem = Assert.IsType<ListBoxItem>(
                    options.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.Same(focusVisualStyle, listItem.FocusVisualStyle);
                Assert.True(listItem.IsSelected);

                var primaryWidth = primaryButton.ActualWidth;
                var primaryHeight = primaryButton.ActualHeight;
                Assert.True(primaryButton.Focus());
                host.UpdateLayout();
                Assert.Equal(primaryWidth, primaryButton.ActualWidth, precision: 3);
                Assert.Equal(primaryHeight, primaryButton.ActualHeight, precision: 3);

                var searchWidth = titleSearch.ActualWidth;
                var searchHeight = titleSearch.ActualHeight;
                Assert.True(titleSearch.Focus());
                host.UpdateLayout();
                Assert.Equal(searchWidth, titleSearch.ActualWidth, precision: 3);
                Assert.Equal(searchHeight, titleSearch.ActualHeight, precision: 3);

                Assert.True(listItem.Focus());
                host.UpdateLayout();
                Assert.True(listItem.IsSelected);

                primaryButton.IsEnabled = false;
                host.UpdateLayout();
                var primaryBorder = Assert.IsType<Border>(
                    primaryButton.Template.FindName("ButtonBorder", primaryButton));
                var disabledBackground = Assert.IsType<SolidColorBrush>(
                    resourceWindow.FindResource("SurfaceSecondaryBrush"));
                var renderedBackground = Assert.IsType<SolidColorBrush>(primaryBorder.Background);
                Assert.Equal(disabledBackground.Color, renderedBackground.Color);
                Assert.Equal(
                    ((SolidColorBrush)resourceWindow.FindResource("TextMutedBrush")).Color,
                    ((SolidColorBrush)primaryButton.Foreground).Color);

                Assert.True(titleSearch.Focus());
                host.UpdateLayout();
                selectedSegmentBorder = Assert.IsType<Border>(
                    selectedSegment.Template.FindName("SegmentButtonBorder", selectedSegment));
                Assert.Equal(
                    ((SolidColorBrush)resourceWindow.FindResource("AccentPrimaryBrush")).Color,
                    Assert.IsType<SolidColorBrush>(selectedSegmentBorder.BorderBrush).Color);
            }
            finally
            {
                host?.Close();
                resourceWindow?.PrepareForApplicationExit();
                resourceWindow?.Close();
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
                Assert.Equal("自動記録を始める", window.StartMonitoringButton.Content);
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

                Assert.Equal("自動記録を停止中", window.MonitoringStateText.Text);
                Assert.Equal(Visibility.Visible, window.StartMonitoringButton.Visibility);
                Assert.Equal("自動記録を再開", window.StartMonitoringButton.Content);
                Assert.True(window.StartMonitoringButton.IsEnabled);
                Assert.Equal(Visibility.Collapsed, window.StopMonitoringButton.Visibility);
                Assert.False(window.StopMonitoringButton.IsEnabled);

                window.ViewModel.RequestApplicationExit();
                DrainDispatcher(window.Dispatcher);

                Assert.Equal("終了処理中", window.MonitoringStateText.Text);
                Assert.Contains("アプリを終了", window.MonitoringReasonText.Text, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("ja", "light", 1280, 760)]
    [InlineData("en", "dark", 960, 640)]
    [InlineData("ko", "light", 960, 640)]
    public void Best_candidates_share_codes_across_sizes_and_keep_modes_and_focus_reachable(
        string language, string theme, double width, double height)
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("goal-label", "2026-10-01T12:00:00Z", 980_000, 1_000);
        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                PrepareStartupResources();
                Localization.Configure(language);
                ThemeManager.Apply(theme);
                window = new MainWindow(StartupPaths(fixture)) { Width = width, Height = height };
                window.ViewModel.Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath, persist: false);
                window.Show();
                window.BestNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                var vm = window.ViewModel;
                Assert.Equal(window.ActualWidth <= 1100 ? 1 : 0, Grid.GetColumn(window.BestConditionsPanel));
                Assert.True(window.BestChartGrid.ActualHeight > 180);
                window.BestVersionModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (var option in vm.BestVersionOptions)
                {
                    window.BestVersionOptionsGrid.SelectedValue = option.Code;
                    DrainDispatcher(window.Dispatcher);
                    Assert.Equal(option.Code, vm.BestVersionFilter);
                    Assert.Equal(option.Code, window.BestVersionComboBox.SelectedValue);
                    Assert.Same(vm.BestVersionOptions, window.BestVersionOptionsGrid.ItemsSource);
                    Assert.Same(vm.BestVersionOptions, window.BestVersionComboBox.ItemsSource);
                    Localization.ApplyToWindow(window);
                    Assert.Equal(option.Display, ((LocalizedOption)window.BestVersionComboBox.SelectedItem).Display);
                }
                // CI desktops may be narrower than 1280; keep this WPF window at the tested logical width.
                window.MinWidth = 1280;
                window.Width = 1280;
                DrainDispatcher(window.Dispatcher);
                Assert.True(window.BestVersionOptionsGrid.IsVisible);
                var candidate = (ListBoxItem)window.BestVersionOptionsGrid.ItemContainerGenerator.ContainerFromIndex(0);
                candidate.Focus();
                window.BestVersionOptionsGrid.SelectedIndex = 0;
                DrainDispatcher(window.Dispatcher);
                Assert.True(window.BestVersionOptionsGrid.IsKeyboardFocusWithin);
                Assert.True(candidate.IsVisible);
                window.MinWidth = 960;
                window.Width = 960;
                DrainDispatcher(window.Dispatcher);
                Assert.True(window.BestVersionComboBox.IsVisible);
                window.BestVersionComboBox.SelectedIndex = vm.BestVersionOptions.Count - 1;
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(((LocalizedOption)window.BestVersionComboBox.SelectedItem).Code, vm.BestVersionFilter);
                Assert.Equal(vm.BestVersionFilter, window.BestVersionOptionsGrid.SelectedValue);
                window.BestLevelModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (var option in vm.BestLevelOptions)
                {
                    window.BestLevelComboBox.SelectedValue = option.Code;
                    DrainDispatcher(window.Dispatcher);
                    Assert.Equal(option.Code, window.BestLevelOptionsGrid.SelectedValue);
                    Assert.Equal(option.Code, vm.BestLevelFilter);
                }
                window.BestGoalModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.True(window.BestGoalComboBox.IsVisible);
                Assert.Equal(language switch { "en" => "Goal", "ko" => "목표", _ => "目標" },
                    ((TextBlock)window.BestGoalOptionsPanel.Children[0]).Text);
                var goalChart = Assert.Single(vm.ChartBests);
                var goalText = FindVisualChildren<TextBlock>(window.BestChartGrid)
                    .Single(text => text.Text == goalChart.GoalRemainingScoreDisplay);
                var goalRow = (DataGridRow)window.BestChartGrid.ItemContainerGenerator.ContainerFromItem(goalChart);
                var textTop = goalText.TranslatePoint(new Point(), goalRow).Y;
                Assert.True(textTop >= 0 && textTop + goalText.ActualHeight <= goalRow.ActualHeight);
                Assert.Equal(TextTrimming.None, goalText.TextTrimming);
                Assert.Equal(Visibility.Collapsed, window.BestProgressCard.Visibility);
                Assert.Equal(Visibility.Collapsed, window.BestResultActions.Visibility);
                Assert.Equal(Visibility.Visible, window.BestChartGrid.Columns[3].Visibility);
                window.BestGoalComboBox.SelectedValue = "AA+";
                Assert.Equal("AA+", vm.BestGoalFilter);
                window.BestTitleModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.BestTitleSearchTextBox.Text = "not-a-song";
                DrainDispatcher(window.Dispatcher);
                Assert.True(window.BestTitleSearchTextBox.IsVisible);
                Assert.Empty(vm.ChartBests);
                Assert.Equal(Visibility.Visible, window.BestEmptyStatePanel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.BestProgressCard.Visibility);
                Assert.Equal(Visibility.Visible, window.BestResultActions.Visibility);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
                Localization.Configure("ja");
                ThemeManager.Apply("light");
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
                var loaded = window.ViewModel.ChartBests;
                var selectedRow = FindVisualChildren<DataGridRow>(window.BestChartGrid)
                    .First(row => row.TranslatePoint(new Point(), window.BestChartGrid).Y >= window.BestChartGrid.ColumnHeaderHeight &&
                        row.TranslatePoint(new Point(), window.BestChartGrid).Y + row.ActualHeight < window.BestChartGrid.ActualHeight - 20);
                var selected = (ChartBestItem)selectedRow.Item;
                var offset = scrollViewer.VerticalOffset;
                var mode = window.ViewModel.BestBrowseMode;
                var sort = window.ViewModel.BestSortFilter;
                window.BestChartGrid.SelectedItem = selected;
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(2, window.ContentTabs.SelectedIndex);
                window.BestNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Reattaching the page can reset the viewer before the queued restoration.
                scrollViewer.ScrollToTop();
                DrainDispatcher(window.Dispatcher);
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(1, window.ContentTabs.SelectedIndex);
                Assert.Same(loaded, window.ViewModel.ChartBests);
                Assert.Same(selected, window.BestChartGrid.SelectedItem);
                Assert.Equal(offset, scrollViewer.VerticalOffset, precision: 3);
                Assert.Equal(mode, window.ViewModel.BestBrowseMode);
                Assert.Equal(sort, window.ViewModel.BestSortFilter);
                selectedRow = (DataGridRow)window.BestChartGrid.ItemContainerGenerator.ContainerFromItem(selected);
                Assert.NotNull(selectedRow);
                var rowTop = selectedRow.TranslatePoint(new Point(), window.BestChartGrid).Y;
                Assert.True(rowTop >= window.BestChartGrid.ColumnHeaderHeight - 1);
                var horizontalBar = FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(window.BestChartGrid)
                    .Single(bar => bar.Orientation == Orientation.Horizontal);
                var bottom = horizontalBar.IsVisible
                    ? horizontalBar.TranslatePoint(new Point(), window.BestChartGrid).Y : window.BestChartGrid.ActualHeight;
                Assert.True(rowTop + selectedRow.ActualHeight <= bottom + 1);
                window.BestSortComboBox.SelectedIndex = 2;
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(0, scrollViewer.VerticalOffset);
                Assert.Equal(50, window.ViewModel.ChartBests.Count);
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

    [Theory]
    [InlineData("ja", "light", 1280, 760)]
    [InlineData("en", "dark", 960, 640)]
    [InlineData("ko", "light", 960, 640)]
    public void Chart_detail_keeps_dates_history_and_load_more_reachable(
        string language, string theme, double width, double height)
    {
        using var fixture = new DatabaseFixture();
        fixture.ExecuteMasterSql("UPDATE songs SET title = 'アルストロメリア (walk with you remix) — A very long song title for wrapping and checking chart detail';");
        for (var index = 0; index < 101; index++)
            fixture.AddPlay($"detail-{index}", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(index).ToString("O"),
                980_000 - index * 10, 1_000 + index);
        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                PrepareStartupResources();
                Localization.Configure(language);
                ThemeManager.Apply(theme);
                window = new MainWindow(StartupPaths(fixture)) { Width = width, Height = height };
                window.ViewModel.Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath, persist: false);
                window.ViewModel.BestBrowseMode = UserSettings.TitleBrowseMode;
                window.Show();
                window.BestNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.ViewModel.SelectChartBest(window.ViewModel.ChartBests.First());
                DrainDispatcher(window.Dispatcher);
                var vm = window.ViewModel;
                Assert.Equal(TextWrapping.Wrap, window.PageTitle.TextWrapping);
                Assert.Equal(10, vm.ChartDetailDisplayedCount);
                Assert.Equal(100, vm.ChartDetailAllPlayPoints.Count);
                Assert.NotEqual(vm.ChartDetailScoreBestAtDisplay, vm.ChartDetailExScoreBestAtDisplay);
                var texts = FindVisualChildren<TextBlock>(window.ChartDetailScrollViewer).ToArray();
                Assert.Contains(texts, text => text.Text == vm.ChartDetailScoreBestAtDisplay);
                Assert.Contains(texts, text => text.Text == vm.ChartDetailExScoreBestAtDisplay);
                Assert.Contains(texts, text => text.Text == Localization.Get("スコアの自己ベスト"));
                Assert.Contains(texts, text => text.Text == Localization.Get("EX SCOREの自己ベスト"));
                var latest = vm.ChartDetailLatestPlay;
                window.ChartDetailBestProgressionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Same(latest, vm.ChartDetailLatestPlay);
                Assert.Equal(10, vm.ChartDetailHistory.Count);
                window.ChartDetailScrollViewer.ScrollToBottom();
                DrainDispatcher(window.Dispatcher);
                var button = FindVisualChildren<Button>(window.ChartDetailScrollViewer)
                    .Single(item => Equals(item.Content, Localization.Get("続きを見る")));
                Assert.Equal(language switch { "en" => "Load more", "ko" => "더 보기", _ => "続きを見る" }, button.Content);
                var point = button.TranslatePoint(new Point(), window.ChartDetailScrollViewer);
                Assert.True(point.Y >= 0 && point.Y + button.ActualHeight <= window.ChartDetailScrollViewer.ActualHeight);
                var date = (DataGridTextColumn)FindVisualChildren<DataGrid>(window.ChartDetailScrollViewer).Single().Columns[0];
                Assert.Equal(TextWrapping.Wrap, FindStyleSetterValue(date.ElementStyle, TextBlock.TextWrappingProperty));
                var offset = window.ChartDetailScrollViewer.VerticalOffset;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(20, vm.ChartDetailHistory.Count);
                Assert.Equal(offset, window.ChartDetailScrollViewer.VerticalOffset, precision: 3);
                Assert.Equal(Localization.Format("{0} / {1}プレー", 20, 101), vm.ChartDetailHistoryLoadMoreHintDisplay);
                window.ChartDetailScrollViewer.ScrollToBottom();
                DrainDispatcher(window.Dispatcher);
                point = button.TranslatePoint(new Point(), window.ChartDetailScrollViewer);
                Assert.True(point.Y + button.ActualHeight <= window.ChartDetailScrollViewer.ActualHeight);
                Assert.Equal(0, window.ChartDetailScrollViewer.ScrollableWidth);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
                Localization.Configure("ja");
                ThemeManager.Apply("light");
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

    private static object? FindStyleSetterValue(Style style, DependencyProperty property)
    {
        for (var current = style; current is not null; current = current.BasedOn)
        {
            foreach (var setter in current.Setters.OfType<Setter>())
            {
                if (setter.Property == property)
                {
                    return setter.Value;
                }
            }
        }

        return null;
    }

    [Fact]
    public void Dark_theme_resource_dictionary_keeps_the_primary_window_loadable()
    {
        using var databaseFixture = new DatabaseFixture();
        applicationFixture.Run(() =>
        {
            var light = new ResourceDictionary
            {
                Source = new Uri(
                    "/DDRGpScoreViewer;component/Resources/Theme.xaml",
                    UriKind.Relative),
            };
            var dark = new ResourceDictionary
            {
                Source = new Uri(
                    "/DDRGpScoreViewer;component/Resources/DarkTheme.xaml",
                    UriKind.Relative),
            };
            foreach (var key in new[]
                     {
                         "BackgroundPrimaryBrush",
                         "SurfacePrimaryBrush",
                         "TextPrimaryBrush",
                         "BadgeInfoBackgroundBrush",
                         "GraphBestBrush",
                         "FocusRingBrush",
                     })
            {
                Assert.Contains(key, light.Keys.Cast<object>().Select(value => value.ToString()));
                Assert.Contains(key, dark.Keys.Cast<object>().Select(value => value.ToString()));
            }

            var lightBackground = Assert.IsType<SolidColorBrush>(light["BackgroundPrimaryBrush"]);
            var darkBackground = Assert.IsType<SolidColorBrush>(dark["BackgroundPrimaryBrush"]);
            Assert.NotEqual(lightBackground.Color, darkBackground.Color);
            Application.Current.Resources.MergedDictionaries.Add(light);
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "/DDRGpScoreViewer;component/Resources/Components.xaml",
                        UriKind.Relative),
                });
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "/DDRGpScoreViewer;component/Resources/Strings.xaml",
                        UriKind.Relative),
                });
            ThemeManager.Apply(UserSettings.DarkTheme);
            Assert.Equal(
                darkBackground.Color,
                ((SolidColorBrush)Application.Current.FindResource("BackgroundPrimaryBrush")).Color);

            MainWindow? window = null;
            try
            {
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
                        Path.Combine(databaseFixture.DirectoryPath, "viewer-settings.json")));
                window.Show();
                window.UpdateLayout();
                Assert.NotNull(window.Content);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
            }

            ThemeManager.Apply(UserSettings.LightTheme);
        });
    }

    [Fact]
    public void State_panel_text_colors_follow_runtime_theme_changes()
    {
        applicationFixture.Run(() =>
        {
            var light = new ResourceDictionary
            {
                Source = new Uri(
                    "/DDRGpScoreViewer;component/Resources/Theme.xaml",
                    UriKind.Relative),
            };
            Application.Current.Resources.MergedDictionaries.Add(light);
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "/DDRGpScoreViewer;component/Resources/Components.xaml",
                        UriKind.Relative),
                });

            ThemeManager.Apply(UserSettings.LightTheme);
            var lightPrimary = Assert.IsType<SolidColorBrush>(
                Application.Current.FindResource("TextPrimaryBrush"));
            var lightSecondary = Assert.IsType<SolidColorBrush>(
                Application.Current.FindResource("TextSecondaryBrush"));
            var panel = new StatePanel
            {
                Title = "Title",
                Message = "Message",
            };
            Window? window = null;
            try
            {
                window = new Window
                {
                    Content = panel,
                    Height = 160,
                    Width = 320,
                };
                window.Show();
                window.UpdateLayout();

                var textBlocks = FindVisualChildren<TextBlock>(panel).ToArray();
                Assert.Equal(2, textBlocks.Length);
                Assert.Equal(lightPrimary.Color, Assert.IsType<SolidColorBrush>(textBlocks[0].Foreground).Color);
                Assert.Equal(lightSecondary.Color, Assert.IsType<SolidColorBrush>(textBlocks[1].Foreground).Color);

                ThemeManager.Apply(UserSettings.DarkTheme);
                window.UpdateLayout();

                var darkPrimary = Assert.IsType<SolidColorBrush>(
                    Application.Current.FindResource("TextPrimaryBrush"));
                var darkSecondary = Assert.IsType<SolidColorBrush>(
                    Application.Current.FindResource("TextSecondaryBrush"));
                Assert.NotEqual(lightPrimary.Color, darkPrimary.Color);
                Assert.NotEqual(lightSecondary.Color, darkSecondary.Color);
                Assert.Equal(darkPrimary.Color, Assert.IsType<SolidColorBrush>(textBlocks[0].Foreground).Color);
                Assert.Equal(darkSecondary.Color, Assert.IsType<SolidColorBrush>(textBlocks[1].Foreground).Color);
            }
            finally
            {
                window?.Close();
                ThemeManager.Apply(UserSettings.LightTheme);
            }
        });
    }

    [Theory]
    [InlineData(UserSettings.JapaneseLanguage, UserSettings.LightTheme, "公開ページを開く")]
    [InlineData(UserSettings.EnglishLanguage, UserSettings.DarkTheme, "Open public page")]
    [InlineData(UserSettings.KoreanLanguage, UserSettings.LightTheme, "공개 페이지 열기")]
    public void Scorebook_navigation_and_result_text_remain_usable_at_minimum_size(
        string language,
        string theme,
        string publicPageButtonText)
    {
        using var databaseFixture = new DatabaseFixture();
        databaseFixture.AddPlay("scorebook-play", "2026-08-24T20:29:00+09:00", 930430, 1514);
        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                foreach (var resourceName in new[] { "Theme.xaml", "Components.xaml", "Strings.xaml" })
                {
                    Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/DDRGpScoreViewer;component/Resources/{resourceName}", UriKind.Relative),
                    });
                }

                Localization.Configure(language);
                ThemeManager.Apply(theme);
                window = new MainWindow(new ViewerDatabasePaths(
                    ViewerDatabaseEnvironment.Development, databaseFixture.DirectoryPath,
                    databaseFixture.MasterPath, databaseFixture.CatalogPath, databaseFixture.ScorePath,
                    Path.Combine(databaseFixture.DirectoryPath, "evaluation.db"),
                    Path.Combine(databaseFixture.DirectoryPath, "data"),
                    Path.Combine(databaseFixture.DirectoryPath, "logs"),
                    Path.Combine(databaseFixture.DirectoryPath, "viewer-settings.json")))
                {
                    Width = 960,
                    Height = 640,
                };
                window.ViewModel.Load(databaseFixture.ScorePath, databaseFixture.MasterPath, databaseFixture.CatalogPath, persist: false);
                window.ViewModel.BestLevelFilter = "level_17";
                window.Show();
                DrainDispatcher(window.Dispatcher);

                var navigation = new[] { window.HomeNavigation, window.BestNavigation, window.HistoryNavigation, window.FlareSkillNavigation, window.SettingsNavigation, window.DataManagementNavigation };
                var previousRight = 0.0;
                foreach (var button in navigation)
                {
                    var position = button.TranslatePoint(new Point(0, 0), window);
                    Assert.True(
                        position.X + 0.5 >= previousRight,
                        $"Top navigation must not overlap: {button.Name} starts at {position.X} before {previousRight}.");
                    Assert.True(position.X + button.ActualWidth <= window.ActualWidth, "Top navigation must fit the smallest window.");
                    previousRight = position.X + button.ActualWidth;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    DrainDispatcher(window.Dispatcher);
                    Assert.Equal("Selected", button.Tag);
                    Assert.Single(navigation, candidate => Equals(candidate.Tag, "Selected"));
                    if (button == window.SettingsNavigation)
                    {
                        Assert.Equal(publicPageButtonText, window.OpenPublicPlayerPageButton.Content);
                    }
                }

                window.BestNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.ViewModel.SelectChartBest(window.ViewModel.ChartBests.First());
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(window.ViewModel.ChartDetailSongTitle, window.PageTitle.Text);
                Assert.True(BindingOperations.IsDataBound(window.PageTitle, TextBlock.TextProperty));
                window.HomeNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(Localization.Get("ホーム"), window.PageTitle.Text);
                Assert.False(BindingOperations.IsDataBound(window.PageTitle, TextBlock.TextProperty));

                var title = Assert.Single(FindVisualChildren<TextBlock>(window.HomeLatestPlayCard),
                    text => text.Text == window.ViewModel.HomeLatestPlay!.SongTitle);
                Assert.Equal(
                    Assert.IsType<SolidColorBrush>(window.FindResource("TextPrimaryBrush")).Color,
                    Assert.IsType<SolidColorBrush>(title.Foreground).Color);
                window.HistoryNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.All(window.ViewModel.Plays, play => Assert.False(string.IsNullOrWhiteSpace(play.CompactPlayedAtDisplay)));
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
                ThemeManager.Apply(UserSettings.LightTheme);
                Localization.Configure(UserSettings.JapaneseLanguage);
            }
        });
    }

    [Fact]
    public void Flare_skill_page_exposes_available_style_zero_and_unavailable_states()
    {
        using var databaseFixture = new DatabaseFixture();
        databaseFixture.AddMasterSongAndChart(
            "flare-song", "FLARE SONG", "Artist", "flare-chart",
            version: "DanceDanceRevolution WORLD");
        databaseFixture.AddPlay(
            "flare-play", "2026-09-17T20:29:00+09:00", 930_000, 1_514,
            "flare-song", "flare-chart", "EX");
        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                foreach (var resourceName in new[] { "Theme.xaml", "Components.xaml", "Strings.xaml" })
                {
                    Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/DDRGpScoreViewer;component/Resources/{resourceName}", UriKind.Relative),
                    });
                }

                Localization.Configure(UserSettings.JapaneseLanguage);
                window = new MainWindow(new ViewerDatabasePaths(
                    ViewerDatabaseEnvironment.Development, databaseFixture.DirectoryPath,
                    databaseFixture.MasterPath, databaseFixture.CatalogPath, databaseFixture.ScorePath,
                    Path.Combine(databaseFixture.DirectoryPath, "evaluation.db"),
                    Path.Combine(databaseFixture.DirectoryPath, "data"),
                    Path.Combine(databaseFixture.DirectoryPath, "logs"),
                    Path.Combine(databaseFixture.DirectoryPath, "viewer-settings.json")));
                window.ViewModel.Load(databaseFixture.ScorePath, databaseFixture.MasterPath, databaseFixture.CatalogPath, persist: false);
                window.Show();
                window.FlareSkillNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);

                Assert.Equal(4, window.ContentTabs.SelectedIndex);
                Assert.Equal("Selected", window.FlareSkillNavigation.Tag);
                Assert.True(window.ViewModel.IsFlareSkillAvailable);
                Assert.Equal("1,016", window.ViewModel.FlareTotalDisplay);
                Assert.Equal(Visibility.Visible, window.ViewModel.FlareSkillContentVisibility);
                Assert.Equal("なし", window.ViewModel.FlareRankJapaneseDisplay);
                Assert.Equal("NONE +++ / 1,500", window.ViewModel.FlareNextRankThresholdDisplay);
                Assert.Equal(3.2d, window.ViewModel.FlareRankProgress, precision: 5);
                Assert.IsType<FlareSkillCategoryResult>(window.FlareClassicColumn.Content);
                Assert.IsType<FlareSkillCategoryResult>(window.FlareWhiteColumn.Content);
                Assert.IsType<FlareSkillCategoryResult>(window.FlareGoldColumn.Content);
                Assert.Contains(
                    "公式値と異なる場合があります",
                    window.FlareSkillDisclaimer.Text,
                    StringComparison.Ordinal);

                var titleTop = window.PageTitle.TranslatePoint(new Point(0, 0), window).Y;
                var selectorTop = window.FlareSingleButton.TranslatePoint(new Point(0, 0), window).Y;
                Assert.True(selectorTop < titleTop + window.PageTitle.ActualHeight);
                Assert.True(selectorTop + window.FlareSingleButton.ActualHeight > titleTop);

                var categoryColors = new[] { "#475569", "#F8FAFC", "#B1842F" };
                var categoryColumns = new[] { window.FlareClassicColumn, window.FlareWhiteColumn, window.FlareGoldColumn };
                for (var index = 0; index < categoryColumns.Length; index++)
                {
                    var swatch = Assert.Single(
                        FindVisualChildren<Border>(categoryColumns[index]),
                        border => border.Width == 5d && border.Height == 36d);
                    Assert.Equal((Color)ColorConverter.ConvertFromString(categoryColors[index]),
                        Assert.IsType<SolidColorBrush>(swatch.Background).Color);
                }

                var difficultyText = Assert.Single(
                    FindVisualChildren<TextBlock>(window.FlareGoldColumn),
                    text => text.Text == "EXP");
                var difficultyBadge = Assert.IsType<Border>(VisualTreeHelper.GetParent(difficultyText));
                Assert.Equal((Color)ColorConverter.ConvertFromString("#27A85A"),
                    Assert.IsType<SolidColorBrush>(difficultyBadge.Background).Color);
                var flareText = Assert.Single(
                    FindVisualChildren<TextBlock>(window.FlareGoldColumn),
                    text => text.Text == "FLARE EX");
                var flareBadge = Assert.IsType<Border>(VisualTreeHelper.GetParent(flareText));
                var flareBrush = Assert.IsType<LinearGradientBrush>(flareBadge.Background);
                var canonicalFlareBadge = new Border
                {
                    Style = Assert.IsType<Style>(Application.Current.FindResource("FlareBadgeStyle")),
                    Tag = "EX",
                };
                var canonicalFlareBrush = Assert.IsType<LinearGradientBrush>(canonicalFlareBadge.Background);
                Assert.Equal(
                    canonicalFlareBrush.GradientStops.Select(stop => stop.Color).ToArray(),
                    flareBrush.GradientStops.Select(stop => stop.Color).ToArray());

                var jupiterBrush = Assert.IsType<LinearGradientBrush>(
                    Application.Current.FindResource("FlareRankJupiterBrush"));
                Assert.Equal(
                    new[] { "#FFFFD2B8", "#FFD7B58D", "#FFAB540B" },
                    jupiterBrush.GradientStops.Select(stop => stop.Color.ToString()).ToArray());

                window.FlareDoubleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal("DOUBLE", window.ViewModel.FlareSkillPlayStyle);
                Assert.Equal("0", window.ViewModel.FlareTotalDisplay);
                Assert.Equal("NONE", window.ViewModel.FlareRankMainDisplay);
                Assert.Equal("0 / 30 TARGET", window.ViewModel.FlareGold.CountDisplay);
                Assert.Empty(window.ViewModel.FlareGold.TopCharts);
                Assert.Null(window.ViewModel.FlareGold.RunnerUp);

                window.ViewModel.Load(
                    Path.Combine(databaseFixture.DirectoryPath, "missing.sqlite"),
                    databaseFixture.MasterPath,
                    persist: false);
                Assert.Equal(Visibility.Visible, window.ViewModel.DataVisibility);
                Assert.Equal(Visibility.Visible, window.ViewModel.FlareSkillUnavailableVisibility);
                Assert.Equal(Visibility.Collapsed, window.ViewModel.FlareSkillContentVisibility);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
            }
        });
    }

    [Theory]
    [InlineData(
        UserSettings.EnglishLanguage,
        "TARGET total across 3 categories",
        "To the next rank stage",
        "Progress within the current rank stage")]
    [InlineData(
        UserSettings.KoreanLanguage,
        "3개 카테고리 TARGET 합계",
        "다음 랭크 단계까지",
        "현재 랭크 단계 내 진행도")]
    public void Flare_skill_static_labels_are_localized(
        string language,
        string categoryTotal,
        string nextRank,
        string progress)
    {
        using var databaseFixture = new DatabaseFixture();
        applicationFixture.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                foreach (var resourceName in new[] { "Theme.xaml", "Components.xaml", "Strings.xaml" })
                {
                    Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/DDRGpScoreViewer;component/Resources/{resourceName}", UriKind.Relative),
                    });
                }

                Localization.Configure(language);
                window = new MainWindow(new ViewerDatabasePaths(
                    ViewerDatabaseEnvironment.Development, databaseFixture.DirectoryPath,
                    databaseFixture.MasterPath, databaseFixture.CatalogPath, databaseFixture.ScorePath,
                    Path.Combine(databaseFixture.DirectoryPath, "evaluation.db"),
                    Path.Combine(databaseFixture.DirectoryPath, "data"),
                    Path.Combine(databaseFixture.DirectoryPath, "logs"),
                    Path.Combine(databaseFixture.DirectoryPath, "viewer-settings.json")));
                window.ViewModel.Load(
                    databaseFixture.ScorePath,
                    databaseFixture.MasterPath,
                    databaseFixture.CatalogPath,
                    persist: false);
                window.Show();
                window.FlareSkillNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);

                var displayedTexts = FindVisualChildren<TextBlock>(window)
                    .Select(text => text.Text)
                    .ToArray();
                Assert.Contains(categoryTotal, displayedTexts);
                Assert.Contains(nextRank, displayedTexts);
                Assert.Contains(progress, displayedTexts);
                Assert.Equal(string.Empty, window.ViewModel.FlareRankJapaneseDisplay);
                Assert.DoesNotContain("なし", displayedTexts);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
                Localization.Configure(UserSettings.JapaneseLanguage);
            }
        });
    }

    [Theory]
    [InlineData(UserSettings.JapaneseLanguage, UserSettings.LightTheme, "Webに自己ベストを送る")]
    [InlineData(UserSettings.EnglishLanguage, UserSettings.DarkTheme, "Share personal bests on the Web")]
    [InlineData(UserSettings.KoreanLanguage, UserSettings.LightTheme, "웹에 개인 최고 기록 보내기")]
    public void Public_data_settings_are_compact_localized_and_use_existing_control_styles(
        string language,
        string theme,
        string heading)
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
                Localization.Configure(language);
                ThemeManager.Apply(theme);
                window = new MainWindow(new ViewerDatabasePaths(
                    ViewerDatabaseEnvironment.Development, databaseFixture.DirectoryPath,
                    databaseFixture.MasterPath, databaseFixture.CatalogPath,
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
                window.SettingsNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DrainDispatcher(window.Dispatcher);

                Assert.Equal(Visibility.Collapsed, window.WebBestSyncToggle.Visibility);
                Assert.False(window.WebBestSyncToggle.IsEnabled);
                Assert.Equal(Localization.Get("連携停止中"), window.WebBestSyncStatusText.Text);
                Assert.False(window.SyncWebBestsNowButton.IsEnabled);
                Assert.Contains(heading, FindVisualChildren<TextBlock>(window).Select(text => text.Text));
                var settingsTexts = FindVisualChildren<TextBlock>(window).Select(text => text.Text).ToArray();
                Assert.DoesNotContain(Localization.Get("起動時の監視と保存できない結果の通知を設定します"), settingsTexts);
                Assert.DoesNotContain(Localization.Get("自己ベストの初期表示と起動時の画面を設定します"), settingsTexts);
                Assert.Equal(string.Empty, window.PageSubtitle.Text);
                Assert.Equal(Visibility.Visible, window.WebBestSyncStatusMessageText.Visibility);
                window.ViewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
                    true, false, WebBestSyncStatus.Idle, null, 0, null, null, []));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(Visibility.Collapsed, window.WebBestSyncStatusMessageText.Visibility);
                window.ViewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
                    true, false, WebBestSyncStatus.ErrorRetryable, null, 0, null, "NETWORK_ERROR", []));
                DrainDispatcher(window.Dispatcher);
                Assert.Equal(Visibility.Visible, window.WebBestSyncStatusMessageText.Visibility);
                Assert.False(string.IsNullOrWhiteSpace(window.WebBestSyncStatusMessageText.Text));
                Assert.NotNull(BindingOperations.GetBinding(window.WebBestSyncToggle, Button.ContentProperty));

                var webSettingsPanel = Assert.IsType<StackPanel>(window.ConnectWebAccountButton.Parent);
                Assert.Equal(new[] { Localization.Get("アカウントを作成・引き継ぐ") },
                    FindVisualChildren<Button>(webSettingsPanel)
                        .Where(button => button.IsVisible).Select(button => button.Content?.ToString()).ToArray());

                var identityStore = new FileWebPlayerIdentityStore(
                    Path.Combine(databaseFixture.DirectoryPath, "web-player-identity.development.json"),
                    Path.Combine(databaseFixture.DirectoryPath, "web-player-credential.development.bin"));
                identityStore.SaveRegistered("p_XXXXXXXXXXXXXXXXXXXXXX", "test-credential", "Existing player");
                window.ViewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
                    false, false, WebBestSyncStatus.Disabled, null, 0, null, null, []));
                DrainDispatcher(window.Dispatcher);
                window.UpdateLayout();
                Assert.False(window.ConnectWebAccountButton.IsVisible);
                Assert.True(window.WebBestSyncToggle.IsVisible);
                Assert.True(window.WebBestSyncToggle.IsEnabled);
                Assert.Equal(Localization.Get("連携を再開する"), window.WebBestSyncToggle.Content);
                Assert.True(window.OpenWebProfileButton.IsVisible);
                Assert.False(window.SyncWebBestsNowButton.IsEnabled);
                Assert.False(window.WebConnectionExpander.IsExpanded);
                Assert.Equal(4, FindVisualChildren<Button>(webSettingsPanel).Count(button => button.IsVisible));

                window.WebConnectionExpander.IsExpanded = true;
                window.UpdateLayout();
                Assert.Contains(Localization.Get("Google連携の設定をWebで開く"),
                    FindVisualChildren<Button>(webSettingsPanel)
                        .Where(button => button.IsVisible).Select(button => button.Content?.ToString()));
                Assert.False(window.GoogleConnectButton.IsVisible);
                Assert.False(window.GoogleUnlinkButton.IsVisible);
                Assert.DoesNotContain(Localization.Get("プレイヤー情報を更新"),
                    FindVisualChildren<Button>(webSettingsPanel).Select(button => button.Content?.ToString()));
                Assert.DoesNotContain(Localization.Get("公開データを削除"),
                    FindVisualChildren<Button>(webSettingsPanel).Select(button => button.Content?.ToString()));
                Assert.DoesNotContain(Localization.Get("このPCの対象データでWebのBestを置き換える"),
                    FindVisualChildren<Button>(webSettingsPanel).Select(button => button.Content?.ToString()));
                Assert.DoesNotContain(Localization.Get("公開プレイヤー名"),
                    FindVisualChildren<TextBlock>(webSettingsPanel).Select(text => text.Text));

                identityStore.SetAuthenticationInvalid(true);
                window.ViewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
                    false, false, WebBestSyncStatus.AuthInvalid, null, 0, null, null, []));
                DrainDispatcher(window.Dispatcher);
                window.UpdateLayout();
                Assert.Equal(new[] { Localization.Get("アカウントを作成・引き継ぐ"), Localization.Get("このPCの連携情報を削除") },
                    FindVisualChildren<Button>(webSettingsPanel)
                        .Where(button => button.IsVisible).Select(button => button.Content?.ToString()).ToArray());
                Assert.Equal("p_XXXXXXXXXXXXXXXXXXXXXX", identityStore.Load().PublicPlayerId);
            }
            finally
            {
                window?.PrepareForApplicationExit();
                window?.Close();
                ThemeManager.Apply(UserSettings.LightTheme);
                Localization.Configure(UserSettings.JapaneseLanguage);
            }
        });
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

    public Task RunAsync(Func<Task> action) => application.Dispatcher.InvokeAsync(action).Task.Unwrap();

    public void Dispose()
    {
        application.Dispatcher.BeginInvoke(() => application.Shutdown());
        applicationThread.Join();
    }
}
