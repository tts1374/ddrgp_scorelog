using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DDRGpScoreViewer;
using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class LocalizationViewTests
{
    [Fact]
    public void Applying_localization_keeps_recycled_data_grid_row_display_bound()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? application = null;
            Window? window = null;
            try
            {
                application = new Application();
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
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                application?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new AggregateException(failure);
        }
    }

    [Fact]
    public void Applying_localization_keeps_combo_box_selection_display_bound()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? application = null;
            Window? window = null;
            try
            {
                application = new Application();
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
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                application?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new AggregateException(failure);
        }
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
