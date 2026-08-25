using DDRGpScoreViewer;
using DDRGpScoreViewer.Data;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ThemeTests
{
    [Theory]
    [InlineData(UserSettings.SystemTheme, true, UserSettings.LightTheme)]
    [InlineData(UserSettings.SystemTheme, false, UserSettings.DarkTheme)]
    [InlineData(UserSettings.SystemTheme, null, UserSettings.LightTheme)]
    [InlineData(UserSettings.LightTheme, false, UserSettings.LightTheme)]
    [InlineData(UserSettings.DarkTheme, true, UserSettings.DarkTheme)]
    [InlineData("unsupported", false, UserSettings.DarkTheme)]
    public void Theme_resolution_uses_system_app_mode_only_for_system_selection(
        string requestedTheme,
        bool? appsUseLightTheme,
        string expectedTheme)
    {
        Assert.Equal(
            expectedTheme,
            ThemeManager.ResolveTheme(requestedTheme, appsUseLightTheme));
    }
}
