using System.IO;
using DDRGpScoreViewer.WebBestSync;
using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Data;

internal sealed class WebBestProjectionRepository
{
    public IReadOnlyList<PlayerChartBestProjectionV1> ReadAll(string scoreDatabasePath)
    {
        using var connection = Open(scoreDatabasePath);
        ScoreViewerRepository.ValidateScoreDatabaseForWrite(connection);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.chart_id, p.score, p.ex_score, p.clear_type, p.flare_rank
            FROM plays p
            JOIN source_captures source
              ON source.capture_id = p.source_capture_id
            WHERE source.source_kind = 'capture'
            ORDER BY p.chart_id, p.play_id;
            """;
        using var reader = command.ExecuteReader();
        var playsByChart = new Dictionary<string, List<EligiblePlay>>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var chartId = reader.GetString(0);
            if (!playsByChart.TryGetValue(chartId, out var plays))
            {
                plays = [];
                playsByChart.Add(chartId, plays);
            }
            plays.Add(new EligiblePlay(
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return playsByChart
            .Select(pair => Project(pair.Key, pair.Value))
            .OrderBy(item => item.ChartId, StringComparer.Ordinal)
            .ToArray();
    }

    public static string ReadMasterVersion(string masterDatabasePath)
    {
        using var connection = Open(masterDatabasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT value FROM master_metadata WHERE key = 'master_version';";
        return command.ExecuteScalar() as string
            ?? throw new InvalidDataException("master DB does not contain master_version.");
    }

    private static PlayerChartBestProjectionV1 Project(
        string chartId,
        IReadOnlyList<EligiblePlay> plays) =>
        new(
            chartId,
            plays.Max(play => play.Score),
            plays.Max(play => play.ExScore),
            WebBestProjectionContract.BestClear(plays.Select(play => play.ClearType)),
            WebBestProjectionContract.BestFlare(
                plays
                    .Where(play => !string.Equals(
                        play.ClearType,
                        "FAILED",
                        StringComparison.Ordinal))
                    .Select(play => play.FlareRank)));

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private sealed record EligiblePlay(
        int Score,
        int ExScore,
        string ClearType,
        string? FlareRank);
}
