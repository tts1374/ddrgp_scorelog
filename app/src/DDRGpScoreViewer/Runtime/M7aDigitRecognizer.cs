using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DDRGpScoreViewer.Runtime;

public sealed record M7aDigitRecognitionResult(
    string FieldName,
    string RoiName,
    string RecognizedDigits,
    string ExpectedValue,
    bool? Match,
    string Status,
    string FailureReason,
    double? Distance,
    double? Confidence,
    int SegmentCount,
    int TemplateCount,
    string PerDigitDistances)
{
    public bool HasCandidateDigits =>
        RecognizedDigits.Length > 0 &&
        Status is "recognized" or "not_evaluated";
}

/// <summary>
/// App-owned port of the existing M7a bitmap-template digit recognizer.
/// It intentionally knows only about packaged or explicitly supplied runtime data.
/// </summary>
public sealed class M7aDigitRecognizer
{
    public const string Method = "bitmap-template-nearest";
    private const double DigitMaxDistance = 0.28;
    private const double DigitMinMargin = 0.02;

    public static readonly IReadOnlyList<string> Fields =
    [
        "score",
        "max_combo",
        "marvelous",
        "perfect",
        "great",
        "good",
        "miss",
        "ex_score",
    ];

    public static readonly IReadOnlyDictionary<string, (int X, int Y, int Width, int Height)> RoiDefinitions =
        new Dictionary<string, (int X, int Y, int Width, int Height)>(StringComparer.Ordinal)
        {
            ["score_digits"] = (197, 277, 269, 45),
            ["max_combo"] = (897, 370, 91, 23),
            ["marvelous"] = (896, 404, 92, 20),
            ["perfect"] = (896, 433, 92, 21),
            ["great"] = (896, 465, 92, 21),
            ["good"] = (896, 495, 92, 21),
            ["miss"] = (897, 555, 92, 21),
            ["ex_score"] = (898, 584, 91, 23),
        };

    private static readonly IReadOnlyDictionary<string, string> FieldToRoi =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["score"] = "score_digits",
            ["max_combo"] = "max_combo",
            ["marvelous"] = "marvelous",
            ["perfect"] = "perfect",
            ["great"] = "great",
            ["good"] = "good",
            ["miss"] = "miss",
            ["ex_score"] = "ex_score",
        };

    private static readonly IReadOnlyDictionary<string, string[]> TemplateGroups =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["marvelous"] = ["judgment_counts"],
            ["perfect"] = ["judgment_counts"],
            ["great"] = ["judgment_counts"],
            ["good"] = ["judgment_counts"],
            ["miss"] = ["judgment_counts"],
            ["max_combo"] = ["combo_ex_score"],
            ["ex_score"] = ["combo_ex_score", "max_combo"],
        };

    private static readonly HashSet<string> ComponentSegmentRois =
        ["max_combo", "marvelous", "perfect", "great", "good", "miss", "ex_score"];

    private static readonly HashSet<string> RejectBrightColoredBackgroundRois =
        ["marvelous", "perfect", "great", "good", "miss"];

    private static readonly string[] RequiredLabels = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    private static readonly string[] TemplateExtensions =
        [".bmp", ".gif", ".jpeg", ".jpg", ".pbm", ".png", ".tif", ".tiff"];

    private readonly AppRuntimeResourceResolver resourceResolver;
    private readonly string? explicitTemplateRoot;
    private readonly Lazy<TemplateRootResolution> templateRoot;

    public M7aDigitRecognizer(
        AppRuntimeResourceResolver? resourceResolver = null,
        string? templateRoot = null)
    {
        this.resourceResolver = resourceResolver ?? new AppRuntimeResourceResolver();
        explicitTemplateRoot = string.IsNullOrWhiteSpace(templateRoot)
            ? null
            : Path.GetFullPath(templateRoot);
        this.templateRoot = new Lazy<TemplateRootResolution>(ResolveTemplateRoot);
    }

    public IReadOnlyDictionary<string, M7aDigitRecognitionResult> Recognize(
        BitmapSource image,
        IReadOnlyDictionary<string, string>? expectedValues = null)
        => RecognizeCore(image, expectedValues, formalVisualAcceptance: false);

    internal IReadOnlyDictionary<string, M7aDigitRecognitionResult> RecognizeForFormalEvidence(
        BitmapSource image)
        => RecognizeCore(image, expectedValues: null, formalVisualAcceptance: true);

    private IReadOnlyDictionary<string, M7aDigitRecognitionResult> RecognizeCore(
        BitmapSource image,
        IReadOnlyDictionary<string, string>? expectedValues,
        bool formalVisualAcceptance)
    {
        ArgumentNullException.ThrowIfNull(image);
        var pixels = PixelImage.From(image);
        var root = templateRoot.Value;
        var loadedByRoi = new Dictionary<string, IReadOnlyList<DigitTemplate>>(StringComparer.Ordinal);
        var results = new Dictionary<string, M7aDigitRecognitionResult>(StringComparer.Ordinal);

        foreach (var fieldName in Fields)
        {
            var roiName = FieldToRoi[fieldName];
            if (!loadedByRoi.TryGetValue(roiName, out var templates))
            {
                templates = root.Path is null
                    ? Array.Empty<DigitTemplate>()
                    : LoadTemplates(root.Path, roiName);
                loadedByRoi[roiName] = templates;
            }

            var hasExpected = false;
            var expectedValue = string.Empty;
            if (expectedValues is not null &&
                expectedValues.TryGetValue(fieldName, out var suppliedExpected) &&
                suppliedExpected is not null)
            {
                hasExpected = true;
                expectedValue = suppliedExpected;
            }
            var expected = hasExpected
                ? NormalizeDigits(expectedValue)
                : string.Empty;
            results[fieldName] = RecognizeRoi(
                pixels,
                fieldName,
                roiName,
                templates,
                hasExpected,
                expected,
                root.ErrorReason,
                formalVisualAcceptance);
        }

        return results;
    }

    public static string AggregateStatus(
        IReadOnlyDictionary<string, M7aDigitRecognitionResult> results)
    {
        foreach (var status in new[]
        {
            "missing_reference",
            "ambiguous",
            "failed_segmentation",
            "not_evaluated",
        })
        {
            if (results.Values.Any(result => result.Status == status))
            {
                return status;
            }
        }

        return results.Count == Fields.Count && results.Values.All(
            result => result.Status == "recognized")
            ? "recognized"
            : "not_evaluated";
    }

    internal M7aDigitRecognitionResult RecognizeRegion(
        BitmapSource image,
        string fieldName,
        (int X, int Y, int Width, int Height) roiDefinition,
        string segmentationRoiName,
        string templateGroup,
        double maximumDistance = DigitMaxDistance,
        double minimumMargin = DigitMinMargin,
        bool formalVisualAcceptance = false)
    {
        // The eight RESULT numeric fields keep their original M7a ROI,
        // template, segmentation, threshold, and recognition gate. This
        // overload also supports the separate chart-context image evidence
        // templates used for level recognition.
        ArgumentNullException.ThrowIfNull(image);
        var root = templateRoot.Value;
        var templates = root.Path is null
            ? Array.Empty<DigitTemplate>()
            : LoadTemplates(root.Path, templateGroup);
        var pixels = PixelImage.From(image).CropScaled(roiDefinition);
        return RecognizePixels(
            pixels,
            fieldName,
            segmentationRoiName,
            templates,
            evaluateExpected: false,
            expected: string.Empty,
            root.ErrorReason,
            maximumDistance,
            minimumMargin,
            formalVisualAcceptance);
    }

    private static M7aDigitRecognitionResult RecognizeRoi(
        PixelImage image,
        string fieldName,
        string roiName,
        IReadOnlyList<DigitTemplate> templates,
        bool evaluateExpected,
        string expected,
        string? templateRootError,
        bool formalVisualAcceptance)
    {
        var roi = image.CropScaled(RoiDefinitions[roiName]);
        return RecognizePixels(
            roi,
            fieldName,
            roiName,
            templates,
            evaluateExpected,
            expected,
            templateRootError,
            DigitMaxDistance,
            DigitMinMargin,
            formalVisualAcceptance);
    }

    private static M7aDigitRecognitionResult RecognizePixels(
        PixelImage roi,
        string fieldName,
        string roiName,
        IReadOnlyList<DigitTemplate> templates,
        bool evaluateExpected,
        string expected,
        string? templateRootError,
        double maximumDistance,
        double minimumMargin,
        bool formalVisualAcceptance)
    {
        var segments = SegmentDigitMasks(roi, roiName);
        var missingLabels = RequiredLabels
            .Where(label => templates.All(template => template.Label != label))
            .ToArray();
        if (missingLabels.Length > 0)
        {
            var reason = templateRootError ??
                "missing_digit_templates=" + string.Concat(missingLabels);
            return Result(
                fieldName,
                roiName,
                "",
                expected,
                null,
                "missing_reference",
                reason,
                null,
                null,
                segments.Count,
                templates.Count,
                "");
        }

        if (segments.Count == 0)
        {
            return Result(
                fieldName,
                roiName,
                "",
                expected,
                null,
                "failed_segmentation",
                "no_digit_segments",
                null,
                null,
                0,
                templates.Count,
                "");
        }

        var recognized = new StringBuilder();
        var distances = new List<double>();
        var margins = new List<double>();
        var distanceParts = new List<string>();
        var ambiguousReason = string.Empty;
        foreach (var segment in segments)
        {
            var vector = VectorFromMask(segment);
            var ranked = TemplateDistances(vector, templates);
            if (ranked.Count == 0)
            {
                return Result(
                    fieldName,
                    roiName,
                    "",
                    expected,
                    null,
                    "missing_reference",
                    "no_digit_templates",
                    null,
                    null,
                    segments.Count,
                    templates.Count,
                    "");
            }

            var best = ranked[0];
            var secondDistance = ranked.Count > 1 ? ranked[1].Distance : 1.0;
            var margin = secondDistance - best.Distance;
            recognized.Append(best.Label);
            distances.Add(best.Distance);
            margins.Add(margin);
            distanceParts.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{best.Label}:{best.Distance:F4}:{margin:F4}"));
            if (best.Distance > maximumDistance)
            {
                ambiguousReason = "distance_above_threshold";
            }
            else if (margin < minimumMargin)
            {
                ambiguousReason = "low_margin";
            }
        }

        var averageDistance = distances.Average();
        var confidence = formalVisualAcceptance
            ? Math.Min(
                1.0,
                0.98 + Math.Min(
                    0.02,
                    Math.Clamp(
                        (margins.Count == 0 ? 0.0 : margins.Min() - minimumMargin) / 0.5,
                        0.0,
                        1.0) * 0.02))
            : 1.0 - Math.Min(1.0, averageDistance / DigitMaxDistance);
        var status = ambiguousReason.Length == 0 ? "recognized" : "ambiguous";
        var failureReason = ambiguousReason;
        bool? match = null;
        if (status == "recognized" && evaluateExpected)
        {
            if (expected.Length == 0)
            {
                status = "not_evaluated";
                failureReason = "no_expected_value";
            }
            else
            {
                match = CanonicalDigits(recognized.ToString()) == CanonicalDigits(expected);
                if (!match.Value)
                {
                    failureReason = "mismatch";
                }
            }
        }

        return Result(
            fieldName,
            roiName,
            recognized.ToString(),
            expected,
            match,
            status,
            failureReason,
            averageDistance,
            confidence,
            segments.Count,
            templates.Count,
            string.Join(";", distanceParts));
    }

    private TemplateRootResolution ResolveTemplateRoot()
    {
        if (explicitTemplateRoot is not null)
        {
            return Directory.Exists(explicitTemplateRoot)
                ? new TemplateRootResolution(explicitTemplateRoot, null)
                : new TemplateRootResolution(
                    null,
                    "digit_templates_directory_missing=" + explicitTemplateRoot);
        }

        try
        {
            return new TemplateRootResolution(
                resourceResolver.ResolveDigitTemplatesDirectory(),
                null);
        }
        catch (RuntimeResourceException)
        {
            return new TemplateRootResolution(
                null,
                "missing_digit_templates=0123456789");
        }
    }

    private static IReadOnlyList<DigitTemplate> LoadTemplates(
        string root,
        string roiName)
    {
        var roots = new List<string> { Path.Combine(root, roiName) };
        if (TemplateGroups.TryGetValue(roiName, out var groups))
        {
            roots.AddRange(groups.Select(group => Path.Combine(root, group)));
        }
        roots.Add(root);

        var templates = new List<DigitTemplate>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidateRoot in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(candidateRoot))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(candidateRoot)
                         .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (!seenPaths.Add(path) ||
                    !TemplateExtensions.Contains(
                        Path.GetExtension(path),
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var label = Path.GetFileNameWithoutExtension(path).FirstOrDefault(
                    character => character is >= '0' and <= '9');
                if (label is not (>= '0' and <= '9'))
                {
                    continue;
                }

                try
                {
                    var mask = LoadTemplateMask(path, roiName);
                    templates.Add(new DigitTemplate(
                        label.ToString(),
                        path,
                        VectorFromMask(mask)));
                }
                catch (Exception exception) when (
                    exception is ArgumentException or IOException or InvalidDataException or
                        NotSupportedException or FileFormatException)
                {
                    // A malformed optional reference behaves like missing reference.
                }
            }
        }

        return templates;
    }

    private static bool[,] LoadTemplateMask(string path, string roiName)
    {
        return string.Equals(Path.GetExtension(path), ".pbm", StringComparison.OrdinalIgnoreCase)
            ? LoadPbmMask(path)
            : ForegroundMask(PixelImage.FromFile(path), roiName);
    }

    private static bool[,] LoadPbmMask(string path)
    {
        var tokens = new List<string>();
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var content = line.Split('#', 2)[0];
            tokens.AddRange(content.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        }

        if (tokens.Count < 3 || tokens[0] != "P1" ||
            !int.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out var height) ||
            width <= 0 || height <= 0)
        {
            throw new InvalidDataException("PBM digit template header is invalid.");
        }

        var pixelTokens = tokens
            .Skip(3)
            .SelectMany(token => token.Length > 1
                ? token.Select(character => character.ToString())
                : [token])
            .ToArray();
        if (pixelTokens.Length < width * height)
        {
            throw new InvalidDataException("PBM digit template has too few pixels.");
        }

        var mask = new bool[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var token = pixelTokens[y * width + x];
                if (token is not ("0" or "1"))
                {
                    throw new InvalidDataException("PBM digit template pixel is invalid.");
                }
                mask[y, x] = token == "1";
            }
        }
        return mask;
    }

    private static List<bool[,]> SegmentDigitMasks(PixelImage source, string roiName)
    {
        var image = source;
        var mask = ForegroundMask(image, roiName);
        List<Component> components;
        if (roiName == "score_digits")
        {
            components = Components(mask)
                .Where(component =>
                    component.Top > 0 &&
                    component.Bottom - component.Top >= Math.Max(18, (int)(image.Height * 0.45)) &&
                    component.Area >= 50)
                .OrderBy(component => component.Left)
                .ToList();
        }
        else if (ComponentSegmentRois.Contains(roiName))
        {
            var minimumComponentHeight = Math.Max(10, (int)(image.Height * 0.35));
            components = MergeDigitFragments(
                Components(mask),
                minimumComponentHeight)
                .Where(component =>
                    component.Bottom - component.Top >= minimumComponentHeight &&
                    component.Right - component.Left >= 2 &&
                    component.Area >= 20)
                .OrderBy(component => component.Left)
                .ToList();
        }
        else
        {
            components = [];
        }

        if (components.Count > 0)
        {
            return components
                .Select(component => CropMask(
                    mask,
                    component.Left,
                    component.Top,
                    component.Right,
                    component.Bottom))
                .ToList();
        }

        var bbox = ForegroundBounds(mask);
        if (bbox is null)
        {
            return [];
        }

        var content = CropMask(mask, bbox.Value.Left, bbox.Value.Top, bbox.Value.Right, bbox.Value.Bottom);
        var columns = Enumerable.Range(0, content.GetLength(1))
            .Select(x => HasAny(content, x))
            .ToArray();
        FillColumnGaps(columns, 1);
        var segments = new List<bool[,]>();
        var index = 0;
        while (index < columns.Length)
        {
            while (index < columns.Length && !columns[index]) index++;
            if (index >= columns.Length) break;
            var start = index;
            while (index < columns.Length && columns[index]) index++;
            var segment = CropMask(content, start, 0, index, content.GetLength(0));
            if (CountTrue(segment) > 0) segments.Add(segment);
        }
        return segments;
    }

    private static List<Component> MergeDigitFragments(
        IReadOnlyList<Component> components,
        int minimumComponentHeight)
    {
        var merged = components
            .OrderBy(component => component.Left)
            .ThenBy(component => component.Top)
            .ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var firstIndex = 0; firstIndex < merged.Count; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < merged.Count; secondIndex++)
                {
                    var first = merged[firstIndex];
                    var second = merged[secondIndex];
                    if (first.Bottom - first.Top >= minimumComponentHeight ||
                        second.Bottom - second.Top >= minimumComponentHeight)
                    {
                        continue;
                    }

                    var horizontalOverlap =
                        Math.Min(first.Right, second.Right) -
                        Math.Max(first.Left, second.Left);
                    var verticalGap =
                        Math.Max(first.Top, second.Top) -
                        Math.Min(first.Bottom, second.Bottom);
                    if (horizontalOverlap < 1 || verticalGap > 1)
                    {
                        continue;
                    }

                    merged[firstIndex] = new Component(
                        Math.Min(first.Left, second.Left),
                        Math.Min(first.Top, second.Top),
                        Math.Max(first.Right, second.Right),
                        Math.Max(first.Bottom, second.Bottom),
                        first.Area + second.Area);
                    merged.RemoveAt(secondIndex);
                    changed = true;
                    break;
                }

                if (changed)
                {
                    break;
                }
            }
        }

        return merged;
    }

    private static bool[,] ForegroundMask(PixelImage image, string roiName)
    {
        var luma = new double[image.Height, image.Width];
        var spread = new int[image.Height, image.Width];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                luma[y, x] = pixel.Luma;
                spread[y, x] = pixel.Spread;
            }
        }

        var mask = new bool[image.Height, image.Width];
        var dark = new bool[image.Height, image.Width];
        var bright = new bool[image.Height, image.Width];
        var borderSum = 0.0;
        var borderCount = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                dark[y, x] = luma[y, x] < 128;
                bright[y, x] = luma[y, x] > 180;
                if (y == 0 || y == image.Height - 1 || x == 0 || x == image.Width - 1)
                {
                    borderSum += luma[y, x];
                    borderCount++;
                }
            }
        }

        var borderMean = borderCount == 0 ? 0 : borderSum / borderCount;
        if (borderMean < 128)
        {
            mask = HasBothValues(bright) ? bright : new bool[image.Height, image.Width];
        }
        else if (borderMean > 160)
        {
            mask = HasBothValues(dark) ? dark : new bool[image.Height, image.Width];
        }
        else
        {
            var candidates = new[] { dark, bright }
                .Where(HasBothValues)
                .OrderBy(CountTrue)
                .ToArray();
            mask = candidates.Length == 0
                ? new bool[image.Height, image.Width]
                : candidates[0];
        }

        if (RejectBrightColoredBackgroundRois.Contains(roiName))
        {
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    if (luma[y, x] > 180 && spread[y, x] > 50)
                    {
                        mask[y, x] = false;
                    }
                }
            }
        }
        return mask;
    }

    private static List<(string Label, double Distance)> TemplateDistances(
        float[] vector,
        IReadOnlyList<DigitTemplate> templates)
    {
        var byLabel = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            var distance = 0.0;
            for (var index = 0; index < vector.Length; index++)
            {
                distance += Math.Abs(vector[index] - template.Vector[index]);
            }
            distance /= vector.Length;
            if (!byLabel.TryGetValue(template.Label, out var current) || distance < current)
            {
                byLabel[template.Label] = distance;
            }
        }
        return byLabel
            .Select(pair => (pair.Key, pair.Value))
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (Label: pair.Key, Distance: pair.Value))
            .ToList();
    }

    private static float[] VectorFromMask(bool[,] mask)
    {
        var bounds = ForegroundBounds(mask);
        var vector = new float[16 * 24];
        if (bounds is null)
        {
            return vector;
        }

        var width = bounds.Value.Right - bounds.Value.Left;
        var height = bounds.Value.Bottom - bounds.Value.Top;
        for (var y = 0; y < 24; y++)
        {
            var sourceY = Math.Min(height - 1, y * height / 24);
            for (var x = 0; x < 16; x++)
            {
                var sourceX = Math.Min(width - 1, x * width / 16);
                vector[y * 16 + x] = mask[
                    bounds.Value.Top + sourceY,
                    bounds.Value.Left + sourceX]
                    ? 1.0f
                    : 0.0f;
            }
        }
        return vector;
    }

    private static List<Component> Components(bool[,] mask)
    {
        var height = mask.GetLength(0);
        var width = mask.GetLength(1);
        var seen = new bool[height, width];
        var result = new List<Component>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!mask[y, x] || seen[y, x]) continue;
                var queue = new Queue<(int Y, int X)>();
                queue.Enqueue((y, x));
                seen[y, x] = true;
                var minX = x;
                var maxX = x;
                var minY = y;
                var maxY = y;
                var area = 0;
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    area++;
                    minX = Math.Min(minX, current.X);
                    maxX = Math.Max(maxX, current.X);
                    minY = Math.Min(minY, current.Y);
                    maxY = Math.Max(maxY, current.Y);
                    foreach (var neighbor in new[]
                    {
                        (current.Y - 1, current.X),
                        (current.Y + 1, current.X),
                        (current.Y, current.X - 1),
                        (current.Y, current.X + 1),
                    })
                    {
                        if (neighbor.Item1 < 0 || neighbor.Item1 >= height ||
                            neighbor.Item2 < 0 || neighbor.Item2 >= width ||
                            !mask[neighbor.Item1, neighbor.Item2] ||
                            seen[neighbor.Item1, neighbor.Item2]) continue;
                        seen[neighbor.Item1, neighbor.Item2] = true;
                        queue.Enqueue((neighbor.Item1, neighbor.Item2));
                    }
                }
                result.Add(new Component(minX, minY, maxX + 1, maxY + 1, area));
            }
        }
        return result;
    }

    private static bool HasAny(bool[,] mask, int x)
    {
        for (var y = 0; y < mask.GetLength(0); y++)
        {
            if (mask[y, x]) return true;
        }
        return false;
    }

    private static void FillColumnGaps(bool[] columns, int maxGap)
    {
        var index = 0;
        while (index < columns.Length)
        {
            if (columns[index])
            {
                index++;
                continue;
            }
            var start = index;
            while (index < columns.Length && !columns[index]) index++;
            if (start > 0 && index < columns.Length && index - start <= maxGap)
            {
                for (var gap = start; gap < index; gap++) columns[gap] = true;
            }
        }
    }

    private static int CountTrue(bool[,] mask)
    {
        var count = 0;
        foreach (var value in mask) if (value) count++;
        return count;
    }

    private static bool HasBothValues(bool[,] mask)
    {
        var count = CountTrue(mask);
        return count > 0 && count < mask.Length;
    }

    private static bool[,] CropMask(bool[,] mask, int left, int top, int right, int bottom)
    {
        var result = new bool[bottom - top, right - left];
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++) result[y - top, x - left] = mask[y, x];
        }
        return result;
    }

    private static (int Left, int Top, int Right, int Bottom)? ForegroundBounds(bool[,] mask)
    {
        var left = mask.GetLength(1);
        var top = mask.GetLength(0);
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < mask.GetLength(0); y++)
        {
            for (var x = 0; x < mask.GetLength(1); x++)
            {
                if (!mask[y, x]) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }
        return right < 0 ? null : (left, top, right, bottom);
    }

    private static string NormalizeDigits(string value) =>
        new(value.Where(character => character is >= '0' and <= '9').ToArray());

    private static string CanonicalDigits(string value)
    {
        var normalized = NormalizeDigits(value).TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static M7aDigitRecognitionResult Result(
        string fieldName,
        string roiName,
        string recognizedDigits,
        string expectedValue,
        bool? match,
        string status,
        string failureReason,
        double? distance,
        double? confidence,
        int segmentCount,
        int templateCount,
        string perDigitDistances) => new(
            fieldName,
            roiName,
            recognizedDigits,
            expectedValue,
            match,
            status,
            failureReason,
            distance,
            confidence,
            segmentCount,
            templateCount,
            perDigitDistances);

    private sealed record DigitTemplate(string Label, string Path, float[] Vector);

    private sealed record TemplateRootResolution(string? Path, string? ErrorReason);

    private readonly record struct Component(int Left, int Top, int Right, int Bottom, int Area);

    private readonly record struct Pixel(
        byte Blue,
        byte Green,
        byte Red)
    {
        public double Luma => 0.2126 * Red + 0.7152 * Green + 0.0722 * Blue;
        public int Spread => Math.Max(Red, Math.Max(Green, Blue)) - Math.Min(Red, Math.Min(Green, Blue));
    }

    private sealed class PixelImage
    {
        private PixelImage(byte[] bytes, int width, int height, int stride)
        {
            Bytes = bytes;
            Width = width;
            Height = height;
            Stride = stride;
        }

        private byte[] Bytes { get; }
        public int Width { get; }
        public int Height { get; }
        private int Stride { get; }

        public static PixelImage From(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var bytes = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(bytes, stride, 0);
            return new PixelImage(bytes, converted.PixelWidth, converted.PixelHeight, stride);
        }

        public static PixelImage FromFile(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".pbm", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("PBM templates are loaded as masks.");
            }
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            return From(decoder.Frames[0]);
        }

        public PixelImage CropScaled((int X, int Y, int Width, int Height) roi)
        {
            return Crop(
                Scale(roi.X, 1280, Width),
                Scale(roi.Y, 720, Height),
                Math.Clamp(Scale(roi.X + roi.Width, 1280, Width), 1, Width),
                Math.Clamp(Scale(roi.Y + roi.Height, 720, Height), 1, Height));
        }

        public PixelImage Crop(int left, int top, int right, int bottom)
        {
            left = Math.Clamp(left, 0, Width - 1);
            top = Math.Clamp(top, 0, Height - 1);
            right = Math.Clamp(right, left + 1, Width);
            bottom = Math.Clamp(bottom, top + 1, Height);
            var targetStride = (right - left) * 4;
            var bytes = new byte[targetStride * (bottom - top)];
            for (var y = top; y < bottom; y++)
            {
                Buffer.BlockCopy(
                    Bytes,
                    y * Stride + left * 4,
                    bytes,
                    (y - top) * targetStride,
                    targetStride);
            }
            return new PixelImage(bytes, right - left, bottom - top, targetStride);
        }

        public Pixel GetPixel(int x, int y)
        {
            var offset = y * Stride + x * 4;
            return new Pixel(Bytes[offset], Bytes[offset + 1], Bytes[offset + 2]);
        }

        private static int Scale(int value, int baseSize, int actualSize) =>
            Math.Clamp(
                (int)Math.Round(value * (double)actualSize / baseSize),
                0,
                actualSize - 1);
    }
}
