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
    public string LevelDisplay => Level is null ? "—" : $"Lv.{Level}";
    public string ScoreDisplay => Score.ToString("N0");
    public string ExScoreDisplay => ExScore.ToString("N0");
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
        DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")
            : value;
}

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
        DateTimeOffset.TryParse(LastPlayedAt, out var timestamp)
            ? timestamp.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
            : LastPlayedAt;
}

public sealed record ViewerData(
    IReadOnlyList<PlayHistoryItem> Plays,
    IReadOnlyList<ChartBestItem> ChartBests,
    string ScoreDatabasePath,
    string MasterDatabasePath,
    string MasterVersion);
