using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DDRGpScoreViewer.Capture;

/// <summary>
/// Groups frames from the same adopted RESULT screen. It deliberately excludes
/// capture time, frame time, and volatile pixels, and is never used as the
/// formal database duplicate key.
/// </summary>
internal static class AppOwnedResultEventFingerprint
{
    public static string? TryCreate(
        LiveResultObservation observation,
        bool requireIdentity)
    {
        if (!observation.IsResultScreen || observation.FormalEvidence is null)
        {
            return null;
        }

        var evidence = observation.FormalEvidence;
        if (requireIdentity &&
            (string.IsNullOrWhiteSpace(evidence.MasterVersion) ||
             string.IsNullOrWhiteSpace(evidence.SongId) ||
             string.IsNullOrWhiteSpace(evidence.ChartId)))
        {
            return null;
        }

        if (evidence.Score is null ||
            evidence.MaxCombo is null ||
            evidence.Marvelous is null ||
            evidence.Perfect is null ||
            evidence.Great is null ||
            evidence.Good is null ||
            evidence.Miss is null ||
            evidence.ExScore is null ||
            string.IsNullOrWhiteSpace(evidence.Rank) ||
            string.IsNullOrWhiteSpace(evidence.ClearType))
        {
            return null;
        }

        var material = string.Join(
            '\u001f',
            "formal-result-event-v1",
            evidence.MasterVersion ?? string.Empty,
            evidence.SongId ?? string.Empty,
            evidence.ChartId ?? string.Empty,
            evidence.Score.Value.ToString(CultureInfo.InvariantCulture),
            evidence.MaxCombo.Value.ToString(CultureInfo.InvariantCulture),
            evidence.Marvelous.Value.ToString(CultureInfo.InvariantCulture),
            evidence.Perfect.Value.ToString(CultureInfo.InvariantCulture),
            evidence.Great.Value.ToString(CultureInfo.InvariantCulture),
            evidence.Good.Value.ToString(CultureInfo.InvariantCulture),
            evidence.Miss.Value.ToString(CultureInfo.InvariantCulture),
            evidence.ExScore.Value.ToString(CultureInfo.InvariantCulture),
            evidence.Rank,
            evidence.ClearType);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }
}
