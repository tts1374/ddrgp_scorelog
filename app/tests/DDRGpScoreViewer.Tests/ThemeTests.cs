using DDRGpScoreViewer;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
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
    [InlineData(null, false, UserSettings.DarkTheme)]
    [InlineData("", true, UserSettings.LightTheme)]
    public void Theme_resolution_uses_system_app_mode_only_for_system_selection(
        string? requestedTheme,
        bool? appsUseLightTheme,
        string expectedTheme)
    {
        Assert.Equal(
            expectedTheme,
            ThemeManager.ResolveTheme(requestedTheme, appsUseLightTheme));
    }

    [Theory]
    [InlineData("system", "system")]
    [InlineData(" LIGHT ", "light")]
    [InlineData("Dark", "dark")]
    [InlineData("unknown", "system")]
    [InlineData("", "system")]
    [InlineData(null, "system")]
    public void Theme_selection_normalizes_saved_values(string? input, string expected) =>
        Assert.Equal(expected, UserSettings.NormalizeTheme(input));

    [Theory]
    [InlineData("AAA", "Upper")]
    [InlineData("AA+", "Upper")]
    [InlineData("AA", "Upper")]
    [InlineData("AA-", "Upper")]
    [InlineData("A+", "Upper")]
    [InlineData("A", "Upper")]
    [InlineData("A-", "Upper")]
    [InlineData("B+", "B")]
    [InlineData("B", "B")]
    [InlineData("B-", "B")]
    [InlineData("C+", "C")]
    [InlineData("C", "C")]
    [InlineData("C-", "C")]
    [InlineData("D+", "D")]
    [InlineData("D", "D")]
    [InlineData("E", "E")]
    [InlineData("", "Neutral")]
    public void Rank_color_group_is_shared_by_bests_and_history(string rank, string expected)
    {
        Assert.Equal(expected, (Best with { Rank = rank }).RankBadgeGroup);
        Assert.Equal(expected, (Play with { Rank = rank }).RankBadgeGroup);
    }

    [Theory]
    [InlineData("MFC", "Mfc", "MFC")]
    [InlineData("PFC", "Pfc", "PFC")]
    [InlineData("GFC", "Gfc", "GFC")]
    [InlineData("FC", "Fc", "FC")]
    [InlineData("FULL COMBO", "Fc", "FC")]
    [InlineData("CLEAR", "Clear", "CLEAR")]
    [InlineData("FAILED", "Failed", "FAILED")]
    public void Clear_color_group_and_label_are_shared_by_bests_and_history(string clear, string group, string label)
    {
        var best = Best with { ClearType = clear };
        var play = Play with { ClearType = clear };
        Assert.Equal((group, label), (best.ClearBadgeGroup, best.ClearDisplay));
        Assert.Equal((group, label), (play.ClearBadgeGroup, play.ClearDisplay));
    }

    [Theory]
    [InlineData("I", "I")]
    [InlineData("II", "II")]
    [InlineData("III", "III")]
    [InlineData("IV", "IV")]
    [InlineData("V", "V")]
    [InlineData("VI", "VI")]
    [InlineData("VII", "VII")]
    [InlineData("VIII", "VIII")]
    [InlineData("IX", "IX")]
    [InlineData("EX", "EX")]
    [InlineData(null, "None")]
    public void Flare_color_group_is_shared_by_bests_and_history(string? flare, string expected)
    {
        Assert.Equal(expected, (Best with { FlareRank = flare }).FlareBadgeGroup);
        Assert.Equal(expected, (Play with { FlareRank = flare }).FlareBadgeGroup);
    }

    private static readonly ChartBestItem Best = new("song", "chart", "Title", "SINGLE", "EXPERT",
        17, 990_000, 1250, "", 1, false);

    private static readonly PlayHistoryItem Play = new("play", "", "", "song", "chart", "Title", "SINGLE",
        "EXPERT", 17, 990_000, 1250, "", "", null, 0, 0, 0, 0, 0, 0, null, null, "manual", false);
}
