using System.Globalization;

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
        _ => ClearType,
    };
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
        ? $"参照情報なし（song_id: {SongId} / chart_id: {ChartId}）"
        : "参照済み";
    public string SourceKindDisplay => SourceKind switch
    {
        "manifest" => "読み込みデータ",
        "timestamped" => "時刻付き入力",
        "capture" => "自動記録",
        "manual" => "手動入力",
        _ => "取得元不明",
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
    bool MasterReferenceMissing)
{
    public string PlayStyleDisplay => PlayStyle switch
    {
        "SINGLE" => "SP",
        "DOUBLE" => "DP",
        _ => "—",
    };
    public string LevelDisplay => Level is null ? "—" : $"Lv.{Level}";
    public string BestScoreDisplay => BestScore.ToString("N0");
    public string BestExScoreDisplay => BestExScore.ToString("N0");
    public string LastPlayedAtDisplay =>
        ViewerTimestampFormatter.Format(LastPlayedAt, "yyyy/MM/dd HH:mm");
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
}

public sealed record ViewerData(
    IReadOnlyList<PlayHistoryItem> Plays,
    IReadOnlyList<ChartBestItem> ChartBests,
    string ScoreDatabasePath,
    string MasterDatabasePath,
    string MasterVersion,
    string CatalogDatabasePath = "");
