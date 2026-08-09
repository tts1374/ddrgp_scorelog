using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DDRGpScoreViewer;
using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class LocalizationViewTests
{
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
}
