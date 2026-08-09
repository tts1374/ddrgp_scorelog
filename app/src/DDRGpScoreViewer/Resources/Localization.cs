using System.Globalization;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Baml2006;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DDRGpScoreViewer.Data;
using WpfApplication = System.Windows.Application;
using WpfDataGrid = System.Windows.Controls.DataGrid;

namespace DDRGpScoreViewer;

public sealed record LocalizedOption(string Code, string Display);

/// <summary>
/// Small WPF ResourceDictionary based localization boundary for user-facing text.
/// Japanese strings are the resource keys and the base dictionary, so a missing
/// translation always falls back to the Japanese text instead of exposing a key.
/// </summary>
public static class Localization
{
    private static readonly object Sync = new();
    private static string currentLanguage = UserSettings.JapaneseLanguage;
    private static ResourceDictionary? fallbackDictionary;
    private static string? fallbackDictionaryLanguage;

    public static string CurrentLanguage
    {
        get
        {
            lock (Sync)
            {
                return currentLanguage;
            }
        }
    }

    public static void Configure(string? language)
    {
        var normalized = UserSettings.NormalizeLanguage(language);
        lock (Sync)
        {
            currentLanguage = normalized;
            fallbackDictionary = null;
            fallbackDictionaryLanguage = null;
        }

        if (WpfApplication.Current is { } application)
        {
            ApplyLanguageDictionary(application, normalized);
        }
    }

    public static void SetLanguage(string? language) => Configure(language);

    public static string Get(string japaneseText)
    {
        if (string.IsNullOrEmpty(japaneseText))
        {
            return japaneseText;
        }

        if (WpfApplication.Current?.TryFindResource(japaneseText) is string applicationValue &&
            !string.IsNullOrWhiteSpace(applicationValue))
        {
            return applicationValue;
        }

        lock (Sync)
        {
            var dictionary = GetFallbackDictionary(currentLanguage);
            if (dictionary?[japaneseText] is string fallbackValue &&
                !string.IsNullOrWhiteSpace(fallbackValue))
            {
                return fallbackValue;
            }
        }

        // The input is the Japanese base text, not a resource key. Returning it
        // is the final base-language fallback and cannot expose an implementation key.
        return japaneseText;
    }

    internal static string GetForLanguage(string japaneseText, string language)
    {
        if (string.IsNullOrEmpty(japaneseText))
        {
            return japaneseText;
        }

        var normalized = UserSettings.NormalizeLanguage(language);
        lock (Sync)
        {
            var dictionary = GetFallbackDictionary(normalized);
            return dictionary?[japaneseText] is string value && !string.IsNullOrWhiteSpace(value)
                ? value
                : japaneseText;
        }
    }

    public static string Format(string japaneseFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(japaneseFormat), args);

    public static LocalizedOption Option(string code, string japaneseDisplay) =>
        new(code, Get(japaneseDisplay));

    public static void ApplyToWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var visited = new HashSet<DependencyObject>();
        Visit(window, visited);
    }

    private static void Visit(DependencyObject node, ISet<DependencyObject> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        LocalizeNode(node);
        if (node is WpfDataGrid dataGrid)
        {
            foreach (var column in dataGrid.Columns)
            {
                if (column.Header is string header)
                {
                    column.Header = Get(header);
                }
            }
        }

        if (node is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            {
                Visit(VisualTreeHelper.GetChild(node, index), visited);
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            Visit(child, visited);
        }
    }

    private static void LocalizeNode(DependencyObject node)
    {
        if (node is TextBlock textBlock)
        {
            if (textBlock.ReadLocalValue(TextBlock.TextProperty) is string text)
            {
                textBlock.Text = Get(text);
            }

            foreach (var run in textBlock.Inlines.OfType<Run>().ToArray())
            {
                if (run.ReadLocalValue(Run.TextProperty) is string runText)
                {
                    run.Text = Get(runText);
                }
            }
        }

        if (node is ContentControl contentControl &&
            contentControl.ReadLocalValue(ContentControl.ContentProperty) is string content)
        {
            contentControl.Content = Get(content);
        }

        if (node is HeaderedContentControl headeredContentControl &&
            headeredContentControl.ReadLocalValue(
                HeaderedContentControl.HeaderProperty) is string header)
        {
            headeredContentControl.Header = Get(header);
        }

        if (node is FrameworkElement element &&
            element.ReadLocalValue(FrameworkElement.ToolTipProperty) is string toolTip)
        {
            element.ToolTip = Get(toolTip);
        }
    }

    private static ResourceDictionary? GetFallbackDictionary(string language)
    {
        lock (Sync)
        {
            if (fallbackDictionaryLanguage == language)
            {
                return fallbackDictionary;
            }

            fallbackDictionaryLanguage = language;
            fallbackDictionary = TryLoadDictionary($"Strings.{language}.xaml") ??
                TryLoadDictionary("Strings.xaml");
            return fallbackDictionary;
        }
    }

    private static void ApplyLanguageDictionary(WpfApplication application, string language)
    {
        var dictionaries = application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            var source = dictionaries[index].Source?.OriginalString ?? string.Empty;
            if (source.Contains("/Strings.", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("\\Strings.", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(index);
            }
        }

        if (language != UserSettings.JapaneseLanguage)
        {
            var dictionary = TryLoadDictionary($"Strings.{language}.xaml");
            if (dictionary is not null)
            {
                dictionaries.Add(dictionary);
            }
        }
    }

    private static ResourceDictionary? TryLoadDictionary(string fileName)
    {
        try
        {
            if (WpfApplication.Current is not null)
            {
                try
                {
                    return new ResourceDictionary
                    {
                        Source = new Uri(
                            $"/DDRGpScoreViewer;component/Resources/{fileName}",
                            UriKind.Relative),
                    };
                }
                catch (Exception)
                {
                    // Fall back to the compiled resource stream below.
                }
            }

            var assembly = typeof(Localization).Assembly;
            using var resources = assembly.GetManifestResourceStream(
                $"{assembly.GetName().Name}.g.resources");
            if (resources is null)
            {
                return null;
            }

            using var reader = new ResourceReader(resources);
            var resourceName =
                $"resources/{Path.GetFileNameWithoutExtension(fileName)}.baml";
            var entries = reader.GetEnumerator();
            while (entries.MoveNext())
            {
                if (!string.Equals(entries.Key as string, resourceName, StringComparison.OrdinalIgnoreCase) ||
                    entries.Value is not Stream bamlStream)
                {
                    continue;
                }

                return System.Windows.Markup.XamlReader.Load(new Baml2006Reader(bamlStream))
                    as ResourceDictionary;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
