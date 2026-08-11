using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DDRGpScoreViewer;
using DDRGpScoreViewer.Data;
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
