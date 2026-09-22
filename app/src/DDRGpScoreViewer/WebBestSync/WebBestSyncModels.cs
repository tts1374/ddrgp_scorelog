using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDRGpScoreViewer.WebBestSync;

internal enum WebBestSyncStatus
{
    Disabled,
    Idle,
    Dirty,
    Syncing,
    Reconciling,
    ErrorRetryable,
    AuthInvalid,
    PublicBestsDeleted,
}

internal sealed record PlayerChartBestProjectionV1(
    [property: JsonPropertyName("chart_id")] string ChartId,
    [property: JsonPropertyName("best_score")] int BestScore,
    [property: JsonPropertyName("best_ex_score")] int BestExScore,
    [property: JsonPropertyName("best_clear_type")] string BestClearType,
    [property: JsonPropertyName("best_flare_rank")] string? BestFlareRank);

internal static class WebBestProjectionContract
{
    private static readonly IReadOnlyDictionary<string, int> ClearPrecedence =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["FAILED"] = 0,
            ["CLEAR"] = 1,
            ["FC"] = 2,
            ["FULL COMBO"] = 2,
            ["GFC"] = 3,
            ["PFC"] = 4,
            ["MFC"] = 5,
        };

    private static readonly IReadOnlyDictionary<string, int> FlarePrecedence =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["I"] = 1,
            ["II"] = 2,
            ["III"] = 3,
            ["IV"] = 4,
            ["V"] = 5,
            ["VI"] = 6,
            ["VII"] = 7,
            ["VIII"] = 8,
            ["IX"] = 9,
            ["EX"] = 10,
        };

    public static string BestClear(IEnumerable<string> values)
    {
        var best = values
            .Select(value => new
            {
                Value = string.Equals(value, "FULL COMBO", StringComparison.Ordinal)
                    ? "FC"
                    : value,
                Rank = ClearPrecedence.TryGetValue(value, out var rank) ? rank : -1,
            })
            .OrderByDescending(item => item.Rank)
            .FirstOrDefault();
        if (best is null || best.Rank < 0)
        {
            throw new InvalidDataException("Web Best clear type is outside the V1 contract.");
        }
        return best.Value;
    }

    public static string? BestFlare(IEnumerable<string?> values) =>
        values
            .Where(value => value is not null && FlarePrecedence.ContainsKey(value))
            .OrderByDescending(value => FlarePrecedence[value!])
            .FirstOrDefault();

    public static string CanonicalJson(PlayerChartBestProjectionV1 projection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("chart_id", projection.ChartId);
            writer.WriteNumber("best_score", projection.BestScore);
            writer.WriteNumber("best_ex_score", projection.BestExScore);
            writer.WriteString("best_clear_type", projection.BestClearType);
            if (projection.BestFlareRank is null)
            {
                writer.WriteNull("best_flare_rank");
            }
            else
            {
                writer.WriteString("best_flare_rank", projection.BestFlareRank);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Hash(PlayerChartBestProjectionV1 projection) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson(projection))));
}

internal sealed record WebBestSyncEntry(
    string ChartId,
    string? DesiredProjectionHash,
    string? SyncedProjectionHash,
    string? DeferredError);

internal sealed record WebBestSyncSnapshot(
    bool Enabled,
    bool FullSnapshotRequired,
    WebBestSyncStatus Status,
    DateTimeOffset? LastSuccessfulSyncAt,
    int RetryAttempt,
    DateTimeOffset? NextRetryAt,
    string? LastErrorCode,
    IReadOnlyList<WebBestSyncEntry> Entries)
{
    public int PendingCount => Entries.Count(
        entry => !string.Equals(
            entry.DesiredProjectionHash,
            entry.SyncedProjectionHash,
            StringComparison.Ordinal));

    public int UnknownChartCount => Entries.Count(
        entry => string.Equals(entry.DeferredError, "UNKNOWN_CHART", StringComparison.Ordinal));
}

internal enum WebBestApiStatus
{
    Success,
    AuthenticationInvalid,
    RetryableError,
    PermanentError,
    SyncConflict,
}

internal sealed record WebBestDeltaItemResult(
    int Index,
    bool Accepted,
    bool Changed,
    string? ErrorCode);

internal sealed record WebBestDeltaOperation(
    string Type,
    string ChartId,
    string? SentProjectionHash,
    PlayerChartBestProjectionV1? Projection);

internal sealed record WebBestDeltaResult(
    WebBestApiStatus Status,
    IReadOnlyList<WebBestDeltaItemResult> Items,
    string? ErrorCode = null);

internal sealed record WebBestSnapshotBeginResult(
    WebBestApiStatus Status,
    string? SnapshotId,
    long? BaseRevision,
    string? ErrorCode = null);

internal sealed record WebBestApiResult(
    WebBestApiStatus Status,
    string? ErrorCode = null);
