using System.Globalization;
using DDRGpScoreViewer;

namespace DDRGpScoreViewer.Models;

public sealed record HomeSummaryData(
    DateTime DisplayDate,
    int PlayCount,
    long? TotalNotes,
    double? Calories)
{
    public string DateDisplay => DisplayDate.ToString(
        "yyyy/MM/dd",
        CultureInfo.CurrentCulture);

    public string PlayCountDisplay => PlayCount.ToString(
        "N0",
        CultureInfo.CurrentCulture);

    public string TotalNotesDisplay => TotalNotes is long totalNotes
        ? totalNotes.ToString("N0", CultureInfo.CurrentCulture)
        : "—";

    public string CaloriesDisplay => Calories is double calories && double.IsFinite(calories)
        ? $"{calories.ToString("0.0", CultureInfo.CurrentCulture)} kcal"
        : "—";

    public string CopyText =>
        $"{DisplayDate.ToString("M月d日", CultureInfo.CurrentCulture)}のDDR GRAND PRIX\n\n" +
        $"プレー数：{PlayCountDisplay}\n" +
        $"総ノーツ数：{TotalNotesDisplay}\n" +
        $"消費カロリー：{CaloriesDisplay}";

    public static HomeSummaryData Empty(DateTimeOffset now) =>
        new(HomeDisplayPeriod.From(now).DisplayDate, 0, null, null);
}

internal readonly record struct HomeDisplayPeriod(
    DateTime DisplayDate,
    DateTimeOffset Start,
    DateTimeOffset End)
{
    private static readonly TimeSpan Boundary = TimeSpan.FromHours(7);

    public static HomeDisplayPeriod From(DateTimeOffset now)
    {
        var displayDate = now.TimeOfDay < Boundary
            ? now.Date.AddDays(-1)
            : now.Date;
        displayDate = DateTime.SpecifyKind(displayDate, DateTimeKind.Unspecified);
        var start = new DateTimeOffset(displayDate, now.Offset).AddHours(7);
        return new HomeDisplayPeriod(displayDate, start, start.AddDays(1));
    }

    public bool Contains(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var timestamp))
        {
            return false;
        }

        return timestamp >= Start && timestamp < End;
    }
}

/// <summary>
/// Home-only projection of a saved play with the previous best values needed by
/// the summary, recent-play rows, and best-update rows.
/// </summary>
public sealed class HomePlayItem
{
    public HomePlayItem(
        PlayHistoryItem play,
        int? previousScore,
        int? previousExScore)
    {
        Play = play;
        PreviousScore = previousScore;
        PreviousExScore = previousExScore;
    }

    public PlayHistoryItem Play { get; }

    public int? PreviousScore { get; }

    public int? PreviousExScore { get; }

    public string SongTitle => Play.SongTitle;

    public string PlayStyleDisplay => Play.PlayStyleDisplay;

    public string Difficulty => Play.Difficulty;

    public string ChartDisplay => Play.ChartDisplay;

    public string LevelDisplay => Play.LevelDisplay;

    public string ScoreDisplay => Play.ScoreDisplay;

    public string ExScoreDisplay => Play.ExScoreDisplay;

    public string Rank => Play.Rank;

    public string RankDisplay => Play.RankDisplay;

    public bool HasRank => Play.HasRank;

    public string RankBadgeGroup => Play.RankBadgeGroup;

    public string ClearDisplay => Play.ClearDisplay;

    public bool HasClear => Play.HasClear;

    public string ClearBadgeGroup => Play.ClearBadgeGroup;

    public string FlareBadgeGroup => Play.FlareBadgeGroup;

    public string FlareRankDisplay => Play.FlareRankDisplay;

    public string LatestPlayedAtDisplay => Play.PlayedAtDisplay;

    public string HomePlayedAtDisplay => HomeTimestampFormatter.Format(Play.PlayedAt);

    public string PreviousScoreDisplay => PreviousScore is int score
        ? Localization.Format("前回 {0:N0}", score)
        : Localization.Get("前回 —");

    public string PreviousExScoreDisplay => PreviousExScore is int exScore
        ? $"EX {exScore:N0}"
        : "EX —";

    public bool IsScoreBestUpdate =>
        PreviousScore is int previous && Play.Score > previous;

    public bool IsExScoreBestUpdate =>
        PreviousExScore is int previous && Play.ExScore > previous;

    public string ScoreBestDeltaDisplay =>
        FormatDelta(Play.Score, PreviousScore);

    public string ExScoreBestDeltaDisplay =>
        FormatDelta(Play.ExScore, PreviousExScore);

    public string ScoreUpdateAmountDisplay => IsScoreBestUpdate
        ? FormatPositiveDelta(Play.Score - PreviousScore!.Value)
        : "—";

    public string ExScoreUpdateAmountDisplay => IsExScoreBestUpdate
        ? FormatPositiveDelta(Play.ExScore - PreviousExScore!.Value)
        : "—";

    public string ScoreBestDeltaGroup =>
        GetDeltaGroup(Play.Score, PreviousScore);

    public string ExScoreBestDeltaGroup =>
        GetDeltaGroup(Play.ExScore, PreviousExScore);

    private static string FormatDelta(int current, int? previous)
    {
        if (previous is null)
        {
            return Localization.Get("初プレー");
        }

        var delta = current - previous.Value;
        return delta switch
        {
            > 0 => FormatPositiveDelta(delta),
            < 0 => Localization.Format("↓ {0:N0}", delta),
            _ => Localization.Get("＝ ±0"),
        };
    }

    private static string FormatPositiveDelta(int delta) =>
        Localization.Format("↑ +{0:N0}", delta);

    private static string GetDeltaGroup(int current, int? previous)
    {
        if (previous is null)
        {
            return "First";
        }

        return current.CompareTo(previous.Value) switch
        {
            > 0 => "Up",
            < 0 => "Down",
            _ => "Tie",
        };
    }
}

internal static class HomeTimestampFormatter
{
    public static string Format(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var timestamp))
        {
            return value;
        }

        var localTimestamp = timestamp.ToLocalTime();
        var format = localTimestamp.Year == DateTimeOffset.Now.Year
            ? "MM/dd HH:mm"
            : "yyyy/MM/dd HH:mm";
        return localTimestamp.ToString(format, CultureInfo.CurrentCulture);
    }
}
