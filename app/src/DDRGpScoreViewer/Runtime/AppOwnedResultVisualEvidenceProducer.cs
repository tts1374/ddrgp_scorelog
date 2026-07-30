using System.Globalization;
using System.Windows.Media.Imaging;
using DDRGpScoreViewer.Capture;

namespace DDRGpScoreViewer.Runtime;

internal sealed class AppOwnedResultVisualEvidenceProducer
{
    private static readonly IReadOnlyDictionary<string, (int X, int Y, int Width, int Height)>
        ResultRois = new Dictionary<string, (int X, int Y, int Width, int Height)>(StringComparer.Ordinal)
        {
            ["rank"] = (170, 122, 160, 126),
            ["flare_rank"] = (385, 135, 120, 130),
            ["ok"] = (896, 524, 92, 28),
        };

    private static readonly (string Value, double ReferenceValue, double ReferenceHue)[] FlareProfiles =
    [
        ("I", 239.0, 52.5),
        ("II", 231.0, 37.5),
        ("III", 222.0, 37.5),
        ("IV", 220.0, 22.5),
        ("V", 209.0, 7.5),
        ("VI", 200.0, 352.5),
        ("VII", 156.0, 337.5),
        ("VIII", 123.0, 337.5),
        ("IX", 107.0, 307.5),
    ];

    private readonly M7aDigitRecognizer digitRecognizer;

    public AppOwnedResultVisualEvidenceProducer(M7aDigitRecognizer digitRecognizer)
    {
        this.digitRecognizer = digitRecognizer;
    }

    public AppOwnedFormalEvidence Produce(
        BitmapSource image,
        IReadOnlyDictionary<string, M7aDigitRecognitionResult> digitResults)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var confidences = new Dictionary<string, double?>(StringComparer.Ordinal);
        var reasons = new List<string>();
        var values = new Dictionary<string, int?>(StringComparer.Ordinal);

        foreach (var fieldName in M7aDigitRecognizer.Fields)
        {
            if (!digitResults.TryGetValue(fieldName, out var result) ||
                result.Status != "recognized" ||
                !int.TryParse(
                    result.RecognizedDigits,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                var status = digitResults.TryGetValue(fieldName, out result)
                    ? result.Status
                    : "missing";
                reasons.Add($"digit_recognition.{fieldName}_{status}");
                values[fieldName] = null;
                continue;
            }

            values[fieldName] = value;
            sources[fieldName] = FormalEvidenceSourceNames.ResultNumericVisualEvidence;
            confidences[fieldName] = result.Confidence;
        }

        var imageBuffer = AppOwnedImageBuffer.From(image);
        var rank = RecognizeRank(imageBuffer.CropScaled(ResultRois["rank"]));
        var flare = RecognizeFlare(imageBuffer.CropScaled(ResultRois["flare_rank"]));
        var ok = digitRecognizer.RecognizeRegion(
            image,
            "ok",
            ResultRois["ok"],
            "miss",
            "judgment_counts");

        int? okValue = null;
        if (ok.Status == "recognized" &&
            int.TryParse(ok.RecognizedDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedOk))
        {
            okValue = parsedOk;
        }
        else if (rank.Status != "failed")
        {
            reasons.Add($"digit_recognition.ok_{ok.Status}");
        }

        string? formalRank = null;
        string? formalClearType = null;
        var rankConfidence = rank.Confidence;
        var clearConfidence = rank.Confidence;
        if (rank.Status == "failed")
        {
            formalRank = "E";
            formalClearType = "FAILED";
            sources["rank"] = FormalEvidenceSourceNames.ResultRankVisualEvidence;
            confidences["rank"] = rank.Confidence;
            sources["clear_type"] = FormalEvidenceSourceNames.ResultClearTypeVisualEvidence;
            confidences["clear_type"] = rank.Confidence;
        }
        else if (rank.Status == "non_failed")
        {
            var scoreConfidence = ConfidenceOf(digitResults, "score");
            formalRank = RankFromScore(values.GetValueOrDefault("score"));
            if (formalRank is not null)
            {
                rankConfidence = MinConfidence(rank.Confidence, scoreConfidence);
                sources["rank"] = FormalEvidenceSourceNames.ResultRankVisualEvidence;
                confidences["rank"] = rankConfidence;
            }
            else
            {
                reasons.Add("formal_evidence.rank_score_invalid");
            }

            formalClearType = ClearTypeFromCounts(
                values,
                okValue,
                failed: false);
            if (formalClearType is not null)
            {
                clearConfidence = MinConfidence(
                    rank.Confidence,
                    ConfidenceOf(digitResults, "marvelous"),
                    ConfidenceOf(digitResults, "perfect"),
                    ConfidenceOf(digitResults, "great"),
                    ConfidenceOf(digitResults, "good"),
                    ConfidenceOf(digitResults, "miss"),
                    ok.Confidence);
                sources["clear_type"] = FormalEvidenceSourceNames.ResultClearTypeVisualEvidence;
                confidences["clear_type"] = clearConfidence;
            }
            else
            {
                reasons.Add("formal_evidence.clear_type_counts_incomplete");
            }
        }
        else
        {
            reasons.Add($"formal_evidence.rank_visual_{rank.Status}");
        }

        string? formalFlare = null;
        if (flare.Status == "recognized")
        {
            formalFlare = flare.Value;
            sources["flare_rank"] = FormalEvidenceSourceNames.ResultFlareRankVisualEvidence;
            confidences["flare_rank"] = flare.Confidence;
        }

        return new AppOwnedFormalEvidence(
            MasterVersion: null,
            SongId: null,
            ChartId: null,
            Score: values.GetValueOrDefault("score"),
            MaxCombo: values.GetValueOrDefault("max_combo"),
            Marvelous: values.GetValueOrDefault("marvelous"),
            Perfect: values.GetValueOrDefault("perfect"),
            Great: values.GetValueOrDefault("great"),
            Good: values.GetValueOrDefault("good"),
            Miss: values.GetValueOrDefault("miss"),
            ExScore: values.GetValueOrDefault("ex_score"),
            Rank: formalRank,
            ClearType: formalClearType,
            FlareRank: formalFlare,
            Sources: sources,
            Confidences: confidences,
            IdentitySignalStatus: "unresolved",
            Ok: okValue,
            RecognitionReasons: reasons);
    }

    internal static string? RankFromScore(int? score)
    {
        if (score is null || score < 0 || score > 1_000_000 || score % 10 != 0)
        {
            return null;
        }

        return score.Value switch
        {
            >= 990_000 => "AAA",
            >= 950_000 => "AA+",
            >= 900_000 => "AA",
            >= 890_000 => "AA-",
            >= 850_000 => "A+",
            >= 800_000 => "A",
            >= 790_000 => "A-",
            >= 750_000 => "B+",
            >= 700_000 => "B",
            >= 690_000 => "B-",
            >= 650_000 => "C+",
            >= 600_000 => "C",
            >= 590_000 => "C-",
            >= 550_000 => "D+",
            _ => "D",
        };
    }

    internal static string? ClearTypeFromCounts(
        IReadOnlyDictionary<string, int?> values,
        int? ok,
        bool failed)
    {
        if (failed)
        {
            return "FAILED";
        }

        if (ok is null ||
            !values.TryGetValue("perfect", out var perfect) || perfect is null || perfect < 0 ||
            !values.TryGetValue("great", out var great) || great is null || great < 0 ||
            !values.TryGetValue("good", out var good) || good is null || good < 0 ||
            !values.TryGetValue("miss", out var miss) || miss is null || miss < 0 ||
            ok < 0 ||
            (values.TryGetValue("marvelous", out var marvelous) &&
             (marvelous is null || marvelous < 0)))
        {
            return null;
        }

        if (perfect == 0 && great == 0 && good == 0 && miss == 0)
        {
            return "MFC";
        }
        if (great == 0 && good == 0 && miss == 0)
        {
            return "PFC";
        }
        if (good == 0 && miss == 0)
        {
            return "GFC";
        }
        return miss == 0 ? "FULL COMBO" : "CLEAR";
    }

    private static double? ConfidenceOf(
        IReadOnlyDictionary<string, M7aDigitRecognitionResult> results,
        string fieldName) =>
        results.TryGetValue(fieldName, out var result) && result.Status == "recognized"
            ? result.Confidence
            : null;

    private static double? MinConfidence(params double?[] values)
    {
        var available = values
            .Where(value => value is not null && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .ToArray();
        return available.Length == values.Length ? available.Min() : null;
    }

    private static RankVisualResult RecognizeRank(AppOwnedImageBuffer image)
    {
        if (image.Width == 0 || image.Height == 0)
        {
            return new RankVisualResult("ambiguous", null, null, "empty_rank_roi");
        }

        var white = new bool[image.Height, image.Width];
        var whiteCount = 0;
        var yellowCount = 0;
        var chromaticCount = 0;
        var foregroundCount = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                var luma = pixel.Luma;
                var maximum = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
                var minimum = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
                var delta = maximum - minimum;
                var isWhite = luma > 180 && delta < 55 &&
                    pixel.Red > 175 && pixel.Green > 175 && pixel.Blue > 175;
                var isYellow = pixel.Red > 150 && pixel.Green > 115 &&
                    pixel.Blue < 155 && pixel.Red > pixel.Blue * 1.35;
                var saturation = maximum == 0 ? 0 : (double)delta / maximum;
                var isChromatic = saturation >= 0.25 && maximum >= 64 && luma >= 50;
                white[y, x] = isWhite;
                whiteCount += Convert.ToInt32(isWhite);
                yellowCount += Convert.ToInt32(isYellow);
                chromaticCount += Convert.ToInt32(isChromatic);
                foregroundCount += Convert.ToInt32(isWhite || isYellow || isChromatic);
            }
        }

        var area = image.Width * image.Height;
        var whiteRatio = (double)whiteCount / area;
        var yellowRatio = (double)yellowCount / area;
        var chromaticRatio = (double)chromaticCount / area;
        var foregroundRatio = (double)foregroundCount / area;
        if (foregroundRatio < 0.035)
        {
            return new RankVisualResult("ambiguous", null, null, "rank_glyph_not_detected");
        }

        var eShapeScore = EShapeScore(white);
        if (eShapeScore is not null && yellowRatio <= 0.10 && chromaticRatio <= 0.20)
        {
            return new RankVisualResult(
                "failed",
                true,
                Math.Min(1.0, 0.985 + Math.Min(0.015, Math.Max(0.0, eShapeScore.Value - 0.55) * 0.05)),
                "failed_e_glyph_shape");
        }

        if ((yellowRatio >= 0.12 || chromaticRatio >= 0.20) && whiteRatio <= 0.10)
        {
            var normalStrength = Math.Max(yellowRatio / 0.12, chromaticRatio / 0.20);
            var margin = Math.Clamp(normalStrength - 1.0, 0.0, 1.0);
            return new RankVisualResult(
                "non_failed",
                false,
                Math.Min(1.0, 0.985 + margin * 0.015),
                "non_failed_rank_glyph");
        }

        return new RankVisualResult("ambiguous", null, null, "rank_glyph_ambiguous");
    }

    private static double? EShapeScore(bool[,] whiteMask)
    {
        var components = Components(whiteMask);
        var candidates = new List<(bool[,] Mask, int Top, int Left)>(components);
        if (components.Count > 1)
        {
            candidates.Add(Union(components));
        }

        var best = (double?)null;
        foreach (var (component, top, left) in candidates)
        {
            var height = component.GetLength(0);
            var width = component.GetLength(1);
            var roiHeight = whiteMask.GetLength(0);
            var roiWidth = whiteMask.GetLength(1);
            var heightRatio = (double)height / roiHeight;
            var widthRatio = (double)width / roiWidth;
            var centerX = (left + width / 2.0) / roiWidth;
            var centerY = (top + height / 2.0) / roiHeight;
            if (heightRatio < 0.45 || widthRatio < 0.10 ||
                centerX is < 0.15 or > 0.85 || centerY is < 0.20 or > 0.80 ||
                (double)width / height is < 0.20 or > 1.35)
            {
                continue;
            }

            var occupancy = CountTrue(component) / (double)(width * height);
            if (occupancy is < 0.12 or > 0.78)
            {
                continue;
            }

            var normalized = ResizeMask(component, 32, 48);
            var iou = Math.Max(Iou(normalized, ETemplate()), Iou(normalized, DisconnectedETemplate()));
            var rowOccupancy = Enumerable.Range(0, 48)
                .Select(y => Enumerable.Range(0, 32).Count(x => normalized[y, x]) / 32.0)
                .ToArray();
            var barPresence = new[]
            {
                rowOccupancy.Take(12).Max(),
                rowOccupancy.Skip(16).Take(16).Max(),
                rowOccupancy.Skip(36).Take(12).Max(),
            };
            var stemPresence = Enumerable.Range(0, 48)
                .SelectMany(y => Enumerable.Range(2, 8).Select(x => normalized[y, x]))
                .Count(value => value) / (48.0 * 8.0);
            if (iou < 0.55 || barPresence.Min() < 0.55 || stemPresence < 0.55)
            {
                continue;
            }

            var score = 0.70 * iou + 0.20 * barPresence.Min() + 0.10 * stemPresence;
            best = best is null ? score : Math.Max(best.Value, score);
        }

        return best;
    }

    private static FlareVisualResult RecognizeFlare(AppOwnedImageBuffer image)
    {
        var colored = new List<(double Hue, double Value)>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                var (hue, saturation, value) = Hsv(pixel);
                if (saturation >= 0.45 && value >= 0.35)
                {
                    colored.Add((hue, value));
                }
            }
        }

        var areaRatio = (double)colored.Count / (image.Width * image.Height);
        if (areaRatio < 0.095)
        {
            return new FlareVisualResult("unrecognized", null, null, "flare_badge_not_detected");
        }

        var bins = new int[24];
        foreach (var (hue, _) in colored)
        {
            bins[Math.Clamp((int)(hue / 15.0), 0, 23)]++;
        }
        var activeBins = bins.Count(count => count >= Math.Max(8, (int)(colored.Count * 0.01)));
        var dominantBin = Array.IndexOf(bins, bins.Max());
        var dominantHue = dominantBin * 15.0 + 7.5;
        var dominantRatio = (double)bins[dominantBin] / colored.Count;
        var orderedValues = colored.Select(item => item.Value).OrderBy(value => value).ToArray();
        var medianValue = orderedValues[orderedValues.Length / 2] * 255.0;
        if (medianValue >= 245.0 && activeBins >= 8)
        {
            return new FlareVisualResult("recognized", "EX", 0.99, "flare_ex_palette");
        }

        var distances = FlareProfiles
            .Select(profile =>
            {
                var valueDistance = (medianValue - profile.ReferenceValue) / 18.0;
                var hueDelta = Math.Abs(dominantHue - profile.ReferenceHue) % 360.0;
                var hueDistance = Math.Min(hueDelta, 360.0 - hueDelta) / 18.0;
                return (profile.Value, Distance: Math.Sqrt(valueDistance * valueDistance + hueDistance * hueDistance));
            })
            .OrderBy(item => item.Distance)
            .ToArray();
        var best = distances[0];
        var margin = distances[1].Distance - best.Distance;
        if (best.Distance > 1.75 || margin < 0.10 || dominantRatio < 0.20)
        {
            return new FlareVisualResult("unrecognized", null, null, "flare_palette_ambiguous");
        }

        return new FlareVisualResult(
            "recognized",
            best.Value,
            Math.Min(1.0, 0.985 + Math.Min(0.015, margin / 6.0)),
            "flare_palette_recognized");
    }

    private static (double Hue, double Saturation, double Value) Hsv(AppOwnedPixel pixel)
    {
        var red = pixel.Red / 255.0;
        var green = pixel.Green / 255.0;
        var blue = pixel.Blue / 255.0;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        if (delta <= 1e-6)
        {
            return (0.0, maximum <= 1e-6 ? 0.0 : delta / maximum, maximum);
        }

        var hue = maximum == red
            ? ((green - blue) / delta) % 6.0
            : maximum == green
                ? (blue - red) / delta + 2.0
                : (red - green) / delta + 4.0;
        if (hue < 0) hue += 6.0;
        return (hue * 60.0, delta / maximum, maximum);
    }

    private static List<(bool[,] Mask, int Top, int Left)> Components(bool[,] mask)
    {
        var height = mask.GetLength(0);
        var width = mask.GetLength(1);
        var seen = new bool[height, width];
        var result = new List<(bool[,] Mask, int Top, int Left)>();
        for (var startY = 0; startY < height; startY++)
        {
            for (var startX = 0; startX < width; startX++)
            {
                if (!mask[startY, startX] || seen[startY, startX]) continue;
                var points = new List<(int Y, int X)>();
                var queue = new Queue<(int Y, int X)>();
                queue.Enqueue((startY, startX));
                seen[startY, startX] = true;
                while (queue.Count > 0)
                {
                    var (y, x) = queue.Dequeue();
                    points.Add((y, x));
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dy == 0 && dx == 0) continue;
                            var nextY = y + dy;
                            var nextX = x + dx;
                            if (nextY < 0 || nextY >= height || nextX < 0 || nextX >= width ||
                                !mask[nextY, nextX] || seen[nextY, nextX]) continue;
                            seen[nextY, nextX] = true;
                            queue.Enqueue((nextY, nextX));
                        }
                    }
                }
                if (points.Count < 24) continue;
                var top = points.Min(point => point.Y);
                var left = points.Min(point => point.X);
                var bottom = points.Max(point => point.Y);
                var right = points.Max(point => point.X);
                var component = new bool[bottom - top + 1, right - left + 1];
                foreach (var (y, x) in points) component[y - top, x - left] = true;
                result.Add((component, top, left));
            }
        }
        return result;
    }

    private static (bool[,] Mask, int Top, int Left) Union(
        IReadOnlyList<(bool[,] Mask, int Top, int Left)> components)
    {
        var top = components.Min(component => component.Top);
        var left = components.Min(component => component.Left);
        var bottom = components.Max(component => component.Top + component.Mask.GetLength(0) - 1);
        var right = components.Max(component => component.Left + component.Mask.GetLength(1) - 1);
        var union = new bool[bottom - top + 1, right - left + 1];
        foreach (var (component, componentTop, componentLeft) in components)
        {
            for (var y = 0; y < component.GetLength(0); y++)
            {
                for (var x = 0; x < component.GetLength(1); x++)
                {
                    if (component[y, x]) union[componentTop - top + y, componentLeft - left + x] = true;
                }
            }
        }
        return (union, top, left);
    }

    private static bool[,] ResizeMask(bool[,] source, int width, int height)
    {
        var target = new bool[height, width];
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.GetLength(0) - 1, y * source.GetLength(0) / height);
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.GetLength(1) - 1, x * source.GetLength(1) / width);
                target[y, x] = source[sourceY, sourceX];
            }
        }
        return target;
    }

    private static double Iou(bool[,] left, bool[,] right)
    {
        var intersection = 0;
        var union = 0;
        for (var y = 0; y < left.GetLength(0); y++)
        {
            for (var x = 0; x < left.GetLength(1); x++)
            {
                var either = left[y, x] || right[y, x];
                if (either) union++;
                if (left[y, x] && right[y, x]) intersection++;
            }
        }
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static bool[,] ETemplate()
    {
        var template = new bool[48, 32];
        for (var y = 0; y < 48; y++) for (var x = 2; x < 10; x++) template[y, x] = true;
        for (var y = 0; y < 9; y++) for (var x = 2; x < 30; x++) template[y, x] = true;
        for (var y = 19; y < 29; y++) for (var x = 2; x < 26; x++) template[y, x] = true;
        for (var y = 39; y < 48; y++) for (var x = 2; x < 30; x++) template[y, x] = true;
        return template;
    }

    private static bool[,] DisconnectedETemplate()
    {
        var template = new bool[48, 32];
        for (var y = 0; y < 48; y++) for (var x = 0; x < 10; x++) template[y, x] = true;
        for (var y = 0; y < 12; y++) for (var x = 14; x < 32; x++) template[y, x] = true;
        for (var y = 16; y < 32; y++) for (var x = 14; x < 32; x++) template[y, x] = true;
        for (var y = 36; y < 48; y++) for (var x = 14; x < 32; x++) template[y, x] = true;
        return template;
    }

    private static int CountTrue(bool[,] values)
    {
        var count = 0;
        foreach (var value in values) count += Convert.ToInt32(value);
        return count;
    }

    private sealed record RankVisualResult(string Status, bool? IsFailed, double? Confidence, string Reason);

    private sealed record FlareVisualResult(string Status, string? Value, double? Confidence, string Reason);
}
