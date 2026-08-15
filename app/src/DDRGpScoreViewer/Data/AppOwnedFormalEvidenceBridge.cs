using System.Globalization;
using DDRGpScoreViewer.Capture;

namespace DDRGpScoreViewer.Data;

internal sealed record AppFormalEvidencePromotion(
    string Status,
    AppFormalPlay? FormalPlay,
    double? AnalysisConfidence,
    string IdentitySignalStatus,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyList<string> Reasons)
{
    public static AppFormalEvidencePromotion Excluded { get; } = new(
        "excluded",
        null,
        null,
        "unresolved",
        new Dictionary<string, string>(StringComparer.Ordinal),
        Array.Empty<string>());
}

internal static class AppOwnedFormalEvidenceBridge
{
    private const double MinimumConfidence = 0.98;
    private static readonly IReadOnlyDictionary<string, string> RequiredSources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["master_version"] = FormalEvidenceSourceNames.MasterMetadata,
            ["song_id"] = FormalEvidenceSourceNames.ResultIdentityVisualEvidence,
            ["chart_id"] = FormalEvidenceSourceNames.ResultIdentityVisualEvidence,
            ["score"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["max_combo"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["marvelous"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["perfect"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["great"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["good"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["miss"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["ex_score"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["ok"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            ["rank"] = FormalEvidenceSourceNames.ResultRankVisualEvidence,
            ["clear_type"] = FormalEvidenceSourceNames.ResultClearTypeVisualEvidence,
            ["flare_rank"] = FormalEvidenceSourceNames.ResultFlareRankVisualEvidence,
        };

    private static readonly HashSet<string> RankValues =
        ["AAA", "AA+", "AA", "AA-", "A+", "A", "A-", "B+", "B", "B-",
         "C+", "C", "C-", "D+", "D", "E"];

    private static readonly HashSet<string> ClearValues =
        ["FAILED", "MFC", "PFC", "GFC", "FULL COMBO", "CLEAR"];

    private static readonly HashSet<string> FlareValues =
        ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"];

    public static AppFormalEvidencePromotion Promote(
        LiveResultObservation observation,
        string captureId,
        string captureHash,
        DateTimeOffset capturedAtUtc)
    {
        if (!observation.IsResultScreen)
        {
            return Unresolved("formal_evidence.result_screen_required");
        }

        var evidence = observation.FormalEvidence;
        if (evidence is null)
        {
            var reasons = new List<string> { "automatic_formal_evidence_missing" };
            AddDigitStatusReason(reasons, observation.DigitRecognitionStatus);
            return Unresolved(reasons);
        }

        var sources = evidence.Sources ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        var confidences = evidence.Confidences ??
            new Dictionary<string, double?>(StringComparer.Ordinal);
        var failedResult = string.Equals(evidence.Rank, "E", StringComparison.Ordinal) &&
            string.Equals(evidence.ClearType, "FAILED", StringComparison.Ordinal);
        var reasonsForEvidence = new List<string>(
            evidence.RecognitionReasons ?? Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(observation.ConfirmedEventId))
        {
            reasonsForEvidence.Add("formal_evidence.confirmed_event_id_missing");
        }
        foreach (var (fieldName, requiredSource) in RequiredSources)
        {
            if (fieldName == "flare_rank" && evidence.FlareRank is null)
            {
                continue;
            }

            if (fieldName == "ok" && failedResult && evidence.Ok is null)
            {
                continue;
            }

            if (!sources.TryGetValue(fieldName, out var source) ||
                !string.Equals(source, requiredSource, StringComparison.Ordinal))
            {
                reasonsForEvidence.Add(
                    $"formal_evidence.{fieldName}_source_not_adopted");
            }

            if (!confidences.TryGetValue(fieldName, out var confidence) ||
                confidence is null ||
                !double.IsFinite(confidence.Value) ||
                confidence.Value < MinimumConfidence ||
                confidence.Value > 1.0)
            {
                reasonsForEvidence.Add(
                    $"formal_evidence.{fieldName}_confidence_insufficient");
            }
        }

        RequireText(reasonsForEvidence, "master_version", evidence.MasterVersion);
        RequireText(reasonsForEvidence, "song_id", evidence.SongId);
        RequireText(reasonsForEvidence, "chart_id", evidence.ChartId);
        RequireText(reasonsForEvidence, "rank", evidence.Rank);
        RequireText(reasonsForEvidence, "clear_type", evidence.ClearType);
        RequireDigit(reasonsForEvidence, "score", evidence.Score);
        RequireDigit(reasonsForEvidence, "max_combo", evidence.MaxCombo);
        RequireDigit(reasonsForEvidence, "marvelous", evidence.Marvelous);
        RequireDigit(reasonsForEvidence, "perfect", evidence.Perfect);
        RequireDigit(reasonsForEvidence, "great", evidence.Great);
        RequireDigit(reasonsForEvidence, "good", evidence.Good);
        RequireDigit(reasonsForEvidence, "miss", evidence.Miss);
        RequireDigit(reasonsForEvidence, "ex_score", evidence.ExScore);
        if (!failedResult)
        {
            RequireDigit(reasonsForEvidence, "ok", evidence.Ok);
        }

        if (evidence.Score is < 0 or > 1_000_000 ||
            evidence.Score is not null && evidence.Score.Value % 10 != 0)
        {
            reasonsForEvidence.Add("formal_evidence.score_invalid");
        }
        foreach (var (fieldName, value) in new[]
        {
            ("max_combo", evidence.MaxCombo),
            ("marvelous", evidence.Marvelous),
            ("perfect", evidence.Perfect),
            ("great", evidence.Great),
            ("good", evidence.Good),
            ("miss", evidence.Miss),
            ("ex_score", evidence.ExScore),
        })
        {
            if (value < 0)
            {
                reasonsForEvidence.Add($"formal_evidence.{fieldName}_negative");
            }
        }

        if (evidence.Rank is not null && !RankValues.Contains(evidence.Rank))
        {
            reasonsForEvidence.Add("formal_evidence.rank_invalid");
        }
        if (evidence.ClearType is not null && !ClearValues.Contains(evidence.ClearType))
        {
            reasonsForEvidence.Add("formal_evidence.clear_type_invalid");
        }
        if (evidence.Ok < 0)
        {
            reasonsForEvidence.Add("formal_evidence.ok_negative");
        }
        if (evidence.FlareRank is not null &&
            !FlareValues.Contains(evidence.FlareRank))
        {
            reasonsForEvidence.Add("formal_evidence.flare_rank_invalid");
        }
        AddDigitStatusReason(reasonsForEvidence, observation.DigitRecognitionStatus);

        if (reasonsForEvidence.Count > 0)
        {
            return Unresolved(reasonsForEvidence, evidence.IdentitySignalStatus);
        }

        var adoptedCalories = AdoptOptionalCalories(evidence, sources, confidences);

        var requiredConfidence = RequiredSources.Keys
            .Where(fieldName =>
                (fieldName != "flare_rank" || evidence.FlareRank is not null) &&
                (fieldName != "ok" || !failedResult || evidence.Ok is not null))
            .Select(fieldName => confidences[fieldName]!.Value)
            .Append(adoptedCalories is null ? 1.0 : confidences["calories"]!.Value)
            .Append(1.0)
            .Min();
        var formalSources = new Dictionary<string, string>(sources, StringComparer.Ordinal)
        {
            ["play_id"] = FormalEvidenceSourceNames.CaptureEventV1,
            ["played_at"] = FormalEvidenceSourceNames.CaptureUtc,
            ["duplicate_key"] = FormalEvidenceSourceNames.CaptureEventV1,
        };
        if (adoptedCalories is null)
        {
            formalSources.Remove("calories");
        }
        var formalPlay = new AppFormalPlay(
            $"play-{captureId}",
            capturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            evidence.MasterVersion!,
            evidence.SongId!,
            evidence.ChartId!,
            evidence.Score,
            evidence.MaxCombo,
            evidence.Marvelous,
            evidence.Perfect,
            evidence.Great,
            evidence.Good,
            evidence.Miss,
            evidence.ExScore,
            evidence.Rank!,
            evidence.ClearType!,
            evidence.FlareRank,
            evidence.Ok,
            adoptedCalories,
            observation.ConfirmedEventId!);
        return new AppFormalEvidencePromotion(
            "ready",
            formalPlay,
            requiredConfidence,
            string.IsNullOrWhiteSpace(evidence.IdentitySignalStatus)
                ? "resolved"
                : evidence.IdentitySignalStatus,
            formalSources,
            Array.Empty<string>());
    }

    private static double? AdoptOptionalCalories(
        AppOwnedFormalEvidence evidence,
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyDictionary<string, double?> confidences)
    {
        if (evidence.Calories is null ||
            !double.IsFinite(evidence.Calories.Value) ||
            evidence.Calories.Value < 0.0 ||
            !sources.TryGetValue("calories", out var source) ||
            !string.Equals(
                source,
                FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                StringComparison.Ordinal) ||
            !confidences.TryGetValue("calories", out var confidence) ||
            confidence is null ||
            !double.IsFinite(confidence.Value) ||
            confidence.Value < MinimumConfidence ||
            confidence.Value > 1.0)
        {
            return null;
        }

        return evidence.Calories;
    }

    private static void RequireText(
        List<string> reasons,
        string fieldName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reasons.Add($"formal_evidence.{fieldName}_missing");
        }
    }

    private static void RequireDigit(
        List<string> reasons,
        string fieldName,
        int? value)
    {
        if (value is null)
        {
            reasons.Add($"formal_evidence.{fieldName}_missing");
        }
    }

    private static void AddDigitStatusReason(List<string> reasons, string status)
    {
        if (status is "recognized")
        {
            return;
        }
        if (status is "ambiguous" or "missing_reference" or "failed_segmentation" or
            "not_evaluated")
        {
            reasons.Add($"digit_recognition.{status}");
        }
    }

    private static AppFormalEvidencePromotion Unresolved(
        string reason,
        string identitySignalStatus = "unresolved") =>
        Unresolved([reason], identitySignalStatus);

    private static AppFormalEvidencePromotion Unresolved(
        IReadOnlyList<string> reasons,
        string? identitySignalStatus = "unresolved") =>
        new(
            "unresolved",
            null,
            null,
            string.IsNullOrWhiteSpace(identitySignalStatus)
                ? "unresolved"
                : identitySignalStatus,
            new Dictionary<string, string>(StringComparer.Ordinal),
            reasons.Distinct(StringComparer.Ordinal).ToArray());
}
