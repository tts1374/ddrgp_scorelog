using System.Globalization;
using DDRGpScoreViewer;

namespace DDRGpScoreViewer.Models;

public sealed record PlayHistoryItem(
    string PlayId,
    string PlayedAt,
    string SavedAt,
    string SongId,
    string ChartId,
    string SongTitle,
    string PlayStyle,
    string Difficulty,
    int? Level,
    int Score,
    int ExScore,
    string Rank,
    string ClearType,
    string? FlareRank,
    int MaxCombo,
    int Marvelous,
    int Perfect,
    int Great,
    int Good,
    int Miss,
    string SourceKind,
    bool MasterReferenceMissing)
{
    public string PlayedAtDisplay => FormatTimestamp(PlayedAt);
    public string SavedAtDisplay => FormatTimestamp(SavedAt);
    public string PlayStyleDisplay => PlayStyle switch
    {
        "SINGLE" => "SP",
        "DOUBLE" => "DP",
        _ => "—",
    };
    public string ChartDisplay => $"{PlayStyleDisplay} {Difficulty}";
    public string LevelDisplay => Level is null ? "—" : $"Lv.{Level}";
    public string ScoreDisplay => Score.ToString("N0");
    public string ExScoreDisplay => ExScore.ToString("N0");
    public string ClearDisplay => ClearType switch
    {
        "FC" or "FULL COMBO" => "FC",
        _ when string.IsNullOrWhiteSpace(ClearType) => "—",
        _ => ClearType,
    };
    public string RankDisplay => string.IsNullOrWhiteSpace(Rank) ? "—" : Rank;
    public bool HasRank => !string.IsNullOrWhiteSpace(Rank);
    public bool HasClear => !string.IsNullOrWhiteSpace(ClearType);
    public string RankBadgeGroup => Rank switch
    {
        "AAA" or "AA+" or "AA" or "AA-" or "A+" or "A" or "A-" => "Upper",
        "B+" or "B" or "B-" => "B",
        "C+" or "C" or "C-" => "C",
        "D+" or "D" => "D",
        "E" => "E",
        _ => "Neutral",
    };
    public string ClearBadgeGroup => ClearType switch
    {
        "PFC" => "Pfc",
        "GFC" => "Gfc",
        "FC" or "FULL COMBO" => "Fc",
        "CLEAR" => "Clear",
        "MFC" => "Mfc",
        "FAILED" => "Failed",
        _ => "Neutral",
    };
    public string FlareBadgeGroup => FlareRank switch
    {
        "I" => "I",
        "II" => "II",
        "III" => "III",
        "IV" => "IV",
        "V" => "V",
        "VI" => "VI",
        "VII" => "VII",
        "VIII" => "VIII",
        "IX" => "IX",
        "EX" => "EX",
        _ => "None",
    };
    public string FlareRankDisplay =>
        string.IsNullOrWhiteSpace(FlareRank) ? "—" : $"FLARE {FlareRank}";
    public IReadOnlyList<JudgementBreakdownItem> JudgementBreakdown =>
    [
        new("MARVELOUS", Marvelous),
        new("PERFECT", Perfect),
        new("GREAT", Great),
        new("GOOD", Good),
        new("MISS", Miss),
        new("MAX COMBO", MaxCombo),
    ];
    public string MasterReferenceStatus => MasterReferenceMissing
        ? Localization.Format("参照情報なし（song_id: {0} / chart_id: {1}）", SongId, ChartId)
        : Localization.Get("参照済み");
    public string SourceKindDisplay => SourceKind switch
    {
        "manifest" => Localization.Get("読み込みデータ"),
        "timestamped" => Localization.Get("時刻付き入力"),
        "capture" => Localization.Get("自動記録"),
        "manual" => Localization.Get("手動入力"),
        _ => Localization.Get("取得元不明"),
    };

    private static string FormatTimestamp(string value) =>
        ViewerTimestampFormatter.Format(value, "yyyy/MM/dd HH:mm:ss");
}

public sealed record JudgementBreakdownItem(string Label, int Value);

public sealed record ChartBestItem(
    string SongId,
    string ChartId,
    string SongTitle,
    string PlayStyle,
    string Difficulty,
    int? Level,
    int BestScore,
    int BestExScore,
    string LastPlayedAt,
    int PlayCount,
    bool MasterReferenceMissing,
    string Version = "",
    string Rank = "",
    string ClearType = "",
    string? FlareRank = null)
{
    public bool IsPlayed => PlayCount > 0;
    public string PlayStyleDisplay => PlayStyle switch
    {
        "SINGLE" => "SP",
        "DOUBLE" => "DP",
        _ => "—",
    };
    public string DifficultyDisplay => string.IsNullOrWhiteSpace(Difficulty)
        ? "—"
        : Difficulty;
    public string DifficultyShortDisplay => Difficulty switch
    {
        "BEGINNER" => "BGN",
        "BASIC" => "BAS",
        "DIFFICULT" => "DIF",
        "EXPERT" => "EXP",
        "CHALLENGE" => "CHA",
        _ => DifficultyDisplay,
    };
    public string LevelDisplay => Level is null ? "—" : $"Lv.{Level}";
    public string BestScoreDisplay => IsPlayed ? BestScore.ToString("N0") : "—";
    public string BestExScoreDisplay => IsPlayed ? BestExScore.ToString("N0") : "—";
    public string RankDisplay => string.IsNullOrWhiteSpace(Rank) ? "—" : Rank;
    public bool HasRank => !string.IsNullOrWhiteSpace(Rank);
    public string ClearDisplay => ClearType switch
    {
        "FC" or "FULL COMBO" => "FC",
        _ when string.IsNullOrWhiteSpace(ClearType) => "—",
        _ => ClearType,
    };
    public bool HasClear => !string.IsNullOrWhiteSpace(ClearType);
    public string FlareRankDisplay => string.IsNullOrWhiteSpace(FlareRank)
        ? "—"
        : FlareRank;
    public string VersionDisplay => string.IsNullOrWhiteSpace(Version) ? "—" : Version;
    public string PlayCountDisplay => Localization.Format("{0} 回", PlayCount);
    public string RankBadgeGroup => Rank switch
    {
        "AAA" or "AA+" or "AA" or "AA-" or "A+" or "A" or "A-" => "Upper",
        "B+" or "B" or "B-" => "B",
        "C+" or "C" or "C-" => "C",
        "D+" or "D" => "D",
        "E" => "E",
        _ => "Neutral",
    };
    public string ClearBadgeGroup => ClearType switch
    {
        "PFC" => "Pfc",
        "GFC" => "Gfc",
        "FC" or "FULL COMBO" => "Fc",
        "CLEAR" => "Clear",
        "MFC" => "Mfc",
        "FAILED" => "Failed",
        _ => "Neutral",
    };
    public string FlareBadgeGroup => FlareRank switch
    {
        "I" => "I",
        "II" => "II",
        "III" => "III",
        "IV" => "IV",
        "V" => "V",
        "VI" => "VI",
        "VII" => "VII",
        "VIII" => "VIII",
        "IX" => "IX",
        "EX" => "EX",
        _ => "None",
    };
    public string LastPlayedAtDisplay =>
        string.IsNullOrWhiteSpace(LastPlayedAt)
            ? "—"
            : ViewerTimestampFormatter.FormatBestTimestamp(LastPlayedAt);
}

internal static class ViewerTimestampFormatter
{
    public static string Format(string value, string format) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var timestamp)
            ? timestamp.ToLocalTime().ToString(format, CultureInfo.CurrentCulture)
            : value;

    public static string FormatBestTimestamp(string value)
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

public sealed record ViewerData(
    IReadOnlyList<PlayHistoryItem> Plays,
    IReadOnlyList<ChartBestItem> ChartBests,
    string ScoreDatabasePath,
    string MasterDatabasePath,
    string MasterVersion,
    string CatalogDatabasePath = "",
    IReadOnlyList<ChartBestItem>? ChartCatalogSource = null)
{
    public IReadOnlyList<ChartBestItem> ChartCatalog { get; } = ChartCatalogSource ?? [];
}
