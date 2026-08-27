using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using DDRGpScoreViewer.Data;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;

namespace DDRGpScoreViewer;

internal static class ThemeManager
{
    private const string PersonalizeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";
    private const string LightThemeResource = "/DDRGpScoreViewer;component/Resources/Theme.xaml";
    private const string DarkThemeResource = "/DDRGpScoreViewer;component/Resources/DarkTheme.xaml";
    private static readonly object syncRoot = new();
    private static string selectedTheme = UserSettings.DefaultTheme;
    private static string resolvedTheme = UserSettings.LightTheme;
    private static bool systemThemeMonitoring;

    public static event EventHandler? ThemeChanged;

    public static string SelectedTheme
    {
        get
        {
            lock (syncRoot)
            {
                return selectedTheme;
            }
        }
    }

    public static string ResolvedTheme
    {
        get
        {
            lock (syncRoot)
            {
                return resolvedTheme;
            }
        }
    }

    internal static string ResolveTheme(string? requestedTheme, bool? appsUseLightTheme)
    {
        return UserSettings.NormalizeTheme(requestedTheme) switch
        {
            UserSettings.LightTheme => UserSettings.LightTheme,
            UserSettings.DarkTheme => UserSettings.DarkTheme,
            _ => appsUseLightTheme == false
                ? UserSettings.DarkTheme
                : UserSettings.LightTheme,
        };
    }

    public static void Apply(string? requestedTheme)
    {
        var normalizedTheme = UserSettings.NormalizeTheme(requestedTheme);
        var resolved = ResolveTheme(
            normalizedTheme,
            normalizedTheme == UserSettings.SystemTheme
                ? ReadAppsUseLightTheme()
                : null);
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplyCore(normalizedTheme, resolved));
            return;
        }

        ApplyCore(normalizedTheme, resolved);
    }

    public static void Stop()
    {
        lock (syncRoot)
        {
            if (!systemThemeMonitoring)
            {
                return;
            }

            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            systemThemeMonitoring = false;
        }
    }

    private static void ApplyCore(string normalizedTheme, string resolved)
    {
        var changed = false;
        lock (syncRoot)
        {
            changed = !string.Equals(selectedTheme, normalizedTheme, StringComparison.Ordinal) ||
                !string.Equals(resolvedTheme, resolved, StringComparison.Ordinal);
            selectedTheme = normalizedTheme;
            resolvedTheme = resolved;
            SetSystemThemeMonitoringLocked(normalizedTheme == UserSettings.SystemTheme);
        }

        var application = WpfApplication.Current;
        if (application is not null)
        {
            ReplaceThemeResource(application, resolved);
        }

        if (changed)
        {
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void SetSystemThemeMonitoringLocked(bool shouldMonitor)
    {
        if (shouldMonitor == systemThemeMonitoring)
        {
            return;
        }

        if (shouldMonitor)
        {
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }
        else
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }

        systemThemeMonitoring = shouldMonitor;
    }

    private static void SystemEvents_UserPreferenceChanged(
        object? sender,
        UserPreferenceChangedEventArgs e)
    {
        if (SelectedTheme != UserSettings.SystemTheme)
        {
            return;
        }

        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() => Apply(UserSettings.SystemTheme)));
    }

    private static bool? ReadAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
            return key?.GetValue(AppsUseLightThemeValue) switch
            {
                int value when value == 0 => false,
                int value when value == 1 => true,
                _ => null,
            };
        }
        catch (Exception exception) when (
            exception is SecurityException or UnauthorizedAccessException or
            IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static void ReplaceThemeResource(WpfApplication application, string resolved)
    {
        var dictionaries = application.Resources.MergedDictionaries;
        var currentIndex = -1;
        for (var index = 0; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source is not null &&
                (source.EndsWith("/Theme.xaml", StringComparison.OrdinalIgnoreCase) ||
                 source.EndsWith("/DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                currentIndex = index;
            }
        }

        var resource = new ResourceDictionary
        {
            Source = new Uri(
                resolved == UserSettings.DarkTheme ? DarkThemeResource : LightThemeResource,
                UriKind.Relative),
        };
        if (currentIndex >= 0)
        {
            dictionaries[currentIndex] = resource;
        }
        else
        {
            dictionaries.Insert(0, resource);
        }
    }
}
