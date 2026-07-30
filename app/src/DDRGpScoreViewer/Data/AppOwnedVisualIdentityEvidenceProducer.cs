using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Runtime;
using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Data;

/// <summary>
/// Connects the app-owned RESULT image evidence to the current master and
/// current-master-compatible confirmed jacket references. A reference from an
/// older catalog master version is eligible only when its song ID, canonical
/// title, and canonical artist exactly match the current GP master. It never
/// reads text through OCR and only returns an identity when the visual evidence
/// and chart context form one unique row.
/// </summary>
internal sealed class AppOwnedVisualIdentityEvidenceProducer
{
    private const double JacketDistanceThreshold = 0.24;
    private const double JacketAmbiguityDelta = 0.015;
    private const string JacketFeatureVersion = "m5c-jacket-rgb-grid-v1";
    private const string JacketExtractorVersion = "m5-jacket-v2";

    private static readonly (int X, int Y, int Width, int Height) JacketRoi =
        (532, 54, 216, 216);

    private static readonly (int X, int Y, int Width, int Height) StyleRoi =
        (360, 56, 100, 24);

    private static readonly (int X, int Y, int Width, int Height) DifficultyRoi =
        (378, 80, 84, 24);

    private static readonly (int X, int Y, int Width, int Height) LevelRoi =
        (380, 104, 52, 38);

    private static readonly IReadOnlyDictionary<string, double> StyleHues =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["SINGLE"] = 210.0,
            ["DOUBLE"] = 300.0,
        };

    private static readonly IReadOnlyDictionary<string, double> DifficultyHues =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["BEGINNER"] = 180.0,
            ["BASIC"] = 30.0,
            ["DIFFICULT"] = 330.0,
            ["EXPERT"] = 90.0,
            ["CHALLENGE"] = 270.0,
        };

    private readonly object cacheGate = new();
    private string? cachedReferenceKey;
    private IdentityReferenceSet? cachedReferences;
    private readonly M7aDigitRecognizer digitRecognizer;

    public AppOwnedVisualIdentityEvidenceProducer(
        M7aDigitRecognizer? digitRecognizer = null)
    {
        this.digitRecognizer = digitRecognizer ?? new M7aDigitRecognizer();
    }

    public LiveResultObservation Enrich(
        CapturedFrame frame,
        LiveResultObservation observation,
        string masterDatabasePath,
        string? catalogDatabasePath)
    {
        if (!observation.IsResultScreen || observation.FormalEvidence is null ||
            HasAdoptedIdentity(observation.FormalEvidence))
        {
            return observation;
        }

        if (string.IsNullOrWhiteSpace(catalogDatabasePath))
        {
            return WithReason(
                observation,
                "formal_evidence.identity_visual_reference_required");
        }

        try
        {
            var references = LoadReferences(masterDatabasePath, catalogDatabasePath);
            if (references.FailureReason is not null)
            {
                return WithReason(observation, references.FailureReason);
            }

            var bitmap = DecodeFrame(frame.PngBytes);
            var image = AppOwnedImageBuffer.From(bitmap);
            var context = RecognizeChartContext(bitmap, image);
            if (context.FailureReason is not null)
            {
                return WithReason(observation, context.FailureReason);
            }

            var candidates = LoadChartCandidates(
                masterDatabasePath,
                context.PlayStyle!,
                context.Difficulty!,
                context.Level!.Value);
            if (candidates.Count == 0)
            {
                return WithReason(observation, "formal_evidence.chart_visual_not_found");
            }

            var jacket = ExtractJacketFeature(image.CropScaled(JacketRoi));
            var candidateSongIds = candidates
                .Select(candidate => candidate.SongId)
                .ToHashSet(StringComparer.Ordinal);
            var orderedMatches = references.References
                .Where(reference => candidateSongIds.Contains(reference.SongId))
                .GroupBy(reference => reference.SongId, StringComparer.Ordinal)
                .Select(group => group.Min(reference =>
                    (Distance: JacketFeatureDistance(jacket, reference),
                     SongId: group.Key)))
                .OrderBy(match => match.Distance)
                .ThenBy(match => match.SongId, StringComparer.Ordinal)
                .ToArray();
            if (orderedMatches.Length == 0)
            {
                return WithReason(
                    observation,
                    "formal_evidence.identity_visual_reference_not_found");
            }

            var best = orderedMatches[0];
            if (best.Distance > JacketDistanceThreshold)
            {
                return WithReason(
                    observation,
                    "formal_evidence.identity_visual_not_found");
            }

            if (orderedMatches.Skip(1).Any(match =>
                    match.Distance - best.Distance <= JacketAmbiguityDelta))
            {
                return WithReason(
                    observation,
                    "formal_evidence.identity_visual_ambiguous");
            }

            var matchingCharts = candidates
                .Where(candidate => candidate.SongId == best.SongId)
                .ToArray();
            if (matchingCharts.Length != 1)
            {
                return WithReason(
                    observation,
                    matchingCharts.Length == 0
                        ? "formal_evidence.chart_visual_not_found"
                        : "formal_evidence.chart_visual_ambiguous");
            }

            var identityConfidence = VisualConfidence(
                best.Distance,
                orderedMatches.Length > 1
                    ? orderedMatches[1].Distance - best.Distance
                    : null);
            var evidence = observation.FormalEvidence;
            var sources = new Dictionary<string, string>(evidence.Sources, StringComparer.Ordinal)
            {
                ["master_version"] = FormalEvidenceSourceNames.MasterMetadata,
                ["song_id"] = FormalEvidenceSourceNames.ResultIdentityVisualEvidence,
                ["chart_id"] = FormalEvidenceSourceNames.ResultIdentityVisualEvidence,
            };
            var confidences = new Dictionary<string, double?>(
                evidence.Confidences,
                StringComparer.Ordinal)
            {
                ["master_version"] = 1.0,
                ["song_id"] = identityConfidence,
                ["chart_id"] = Math.Min(identityConfidence, context.Confidence!.Value),
            };
            var remainingReasons = RemoveIdentityProducerReasons(
                evidence.RecognitionReasons ?? Array.Empty<string>());
            var enriched = evidence with
            {
                MasterVersion = references.MasterVersion,
                SongId = best.SongId,
                ChartId = matchingCharts[0].ChartId,
                Sources = sources,
                Confidences = confidences,
                IdentitySignalStatus = "resolved",
                RecognitionReasons = remainingReasons,
            };
            return observation with { FormalEvidence = enriched };
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or IOException or
                FormatException or JsonException or InvalidOperationException or
                SqliteException)
        {
            return WithReason(
                observation,
                $"formal_evidence.identity_visual_producer_unavailable:{exception.GetType().Name}");
        }
    }

    private IdentityReferenceSet LoadReferences(
        string masterDatabasePath,
        string catalogDatabasePath)
    {
        string key;
        try
        {
            key = string.Join(
                "\0",
                Path.GetFullPath(masterDatabasePath),
                Path.GetFullPath(catalogDatabasePath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException)
        {
            return IdentityReferenceSet.Failed(
                $"formal_evidence.identity_visual_producer_unavailable:{exception.GetType().Name}");
        }

        lock (cacheGate)
        {
            if (string.Equals(cachedReferenceKey, key, StringComparison.Ordinal) &&
                cachedReferences is not null)
            {
                return cachedReferences;
            }

            try
            {
                using var master = OpenReadOnly(masterDatabasePath);
                var masterVersion = ReadMasterVersion(master);
                if (string.IsNullOrWhiteSpace(masterVersion))
                {
                    cachedReferenceKey = key;
                    cachedReferences = IdentityReferenceSet.Failed(
                        "formal_evidence.master_version_missing");
                    return cachedReferences;
                }

                var currentSongs = ReadCurrentSongs(master);
                using var catalog = OpenReadOnly(catalogDatabasePath);
                var references = ReadReferences(catalog, currentSongs);
                cachedReferenceKey = key;
                cachedReferences = new IdentityReferenceSet(masterVersion, references, null);
                return cachedReferences;
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidDataException or IOException or
                    FormatException or JsonException or InvalidOperationException or
                    SqliteException)
            {
                cachedReferenceKey = key;
                cachedReferences = IdentityReferenceSet.Failed(
                    $"formal_evidence.identity_visual_reference_unavailable:{exception.GetType().Name}");
                return cachedReferences;
            }
        }
    }

    private static IReadOnlyDictionary<string, CurrentMasterSong> ReadCurrentSongs(
        SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT song_id, title, artist " +
            "FROM songs " +
            "WHERE grand_prix_play_available = 1;";
        using var reader = command.ExecuteReader();
        var songs = new Dictionary<string, CurrentMasterSong>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
            {
                continue;
            }

            var songId = reader.GetString(0);
            songs[songId] = new CurrentMasterSong(
                reader.GetString(1),
                reader.GetString(2));
        }

        return songs;
    }

    private static IReadOnlyList<VisualReference> ReadReferences(
        SqliteConnection connection,
        IReadOnlyDictionary<string, CurrentMasterSong> currentSongs)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT master_version, song_id, canonical_title_snapshot, " +
            "canonical_artist_snapshot, thumbnail_rgb_json, histogram_json, " +
            "dhash_bits_json " +
            "FROM jacket_references " +
            "WHERE master_version IS NOT NULL " +
            "AND song_id IS NOT NULL " +
            "AND review_status IN ('auto_confirmed', 'manual_confirmed') " +
            "AND feature_extractor_version = $extractor_version " +
            "AND jacket_feature_version = $feature_version " +
            "AND thumbnail_rgb_json IS NOT NULL " +
            "AND histogram_json IS NOT NULL " +
            "AND dhash_bits_json IS NOT NULL;";
        command.Parameters.AddWithValue("$extractor_version", JacketExtractorVersion);
        command.Parameters.AddWithValue("$feature_version", JacketFeatureVersion);
        using var reader = command.ExecuteReader();
        var references = new List<VisualReference>();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) ||
                reader.IsDBNull(3) || reader.IsDBNull(4) || reader.IsDBNull(5) ||
                reader.IsDBNull(6))
            {
                continue;
            }

            var songId = reader.GetString(1);
            if (!currentSongs.TryGetValue(songId, out var currentSong) ||
                !string.Equals(
                    currentSong.Title,
                    reader.GetString(2),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    currentSong.Artist,
                    reader.GetString(3),
                    StringComparison.Ordinal))
            {
                continue;
            }

            var thumbnail = ReadVector(reader.GetString(4), 16 * 16 * 3, 0.0, 1.0);
            var histogram = ReadVector(reader.GetString(5), 8 * 3, 0.0, 1.0);
            var dhash = ReadVector(reader.GetString(6), 64, 0.0, 1.0);
            references.Add(new VisualReference(
                songId,
                thumbnail,
                histogram,
                dhash));
        }

        return references;
    }

    private static IReadOnlyList<ChartCandidate> LoadChartCandidates(
        string masterDatabasePath,
        string playStyle,
        string difficulty,
        int level)
    {
        using var connection = OpenReadOnly(masterDatabasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT c.song_id, c.chart_id " +
            "FROM charts c JOIN songs s ON s.song_id = c.song_id " +
            "WHERE c.play_style = $play_style " +
            "AND c.difficulty = $difficulty " +
            "AND c.level = $level " +
            "AND s.grand_prix_play_available = 1 " +
            "ORDER BY c.song_id, c.chart_id;";
        command.Parameters.AddWithValue("$play_style", playStyle);
        command.Parameters.AddWithValue("$difficulty", difficulty);
        command.Parameters.AddWithValue("$level", level);
        using var reader = command.ExecuteReader();
        var candidates = new List<ChartCandidate>();
        while (reader.Read())
        {
            candidates.Add(new ChartCandidate(reader.GetString(0), reader.GetString(1)));
        }
        return candidates;
    }

    private ChartContextResult RecognizeChartContext(
        BitmapSource bitmap,
        AppOwnedImageBuffer image)
    {
        var style = RecognizeHue(image.CropScaled(StyleRoi), StyleHues);
        if (style.Value is null)
        {
            return ChartContextResult.Failed("formal_evidence.play_style_visual_ambiguous");
        }

        var difficulty = RecognizeHue(image.CropScaled(DifficultyRoi), DifficultyHues);
        if (difficulty.Value is null)
        {
            return ChartContextResult.Failed("formal_evidence.difficulty_visual_ambiguous");
        }

        var level = digitRecognizer.RecognizeRegion(
            bitmap,
            fieldName: "level",
            roiDefinition: LevelRoi,
            segmentationRoiName: "score_digits",
            templateGroup: "score_digits",
            maximumDistance: 0.34,
            minimumMargin: 0.05,
            formalVisualAcceptance: true);
        if (level.Status != "recognized" ||
            !int.TryParse(
                level.RecognizedDigits,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedLevel))
        {
            return ChartContextResult.Failed(
                $"formal_evidence.level_visual_{level.Status}");
        }

        if (level.Confidence is null || level.Confidence < 0.98)
        {
            return ChartContextResult.Failed(
                "formal_evidence.level_visual_confidence_insufficient");
        }

        return new ChartContextResult(
            style.Value,
            difficulty.Value,
            parsedLevel,
            new[]
            {
                style.Confidence ?? 0.0,
                difficulty.Confidence ?? 0.0,
                level.Confidence.Value,
            }.Min(),
            null);
    }

    private static HueRecognition RecognizeHue(
        AppOwnedImageBuffer image,
        IReadOnlyDictionary<string, double> profiles)
    {
        var hues = new List<double>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var (hue, saturation, value) = Hsv(image.GetPixel(x, y));
                if (saturation >= 0.35 && value >= 0.25)
                {
                    hues.Add(hue);
                }
            }
        }

        if (hues.Count < Math.Max(8, image.Width * image.Height / 80))
        {
            return HueRecognition.Unknown;
        }

        var scores = profiles
            .Select(profile =>
                (Value: profile.Key,
                 Score: hues.Average(hue =>
                     Math.Max(0.0, 1.0 - CircularDistance(hue, profile.Value) / 45.0))))
            .OrderByDescending(profile => profile.Score)
            .ThenBy(profile => profile.Value, StringComparer.Ordinal)
            .ToArray();
        var best = scores[0];
        var second = scores.Length > 1 ? scores[1].Score : 0.0;
        if (best.Score < 0.45 || best.Score - second < 0.08)
        {
            return HueRecognition.Unknown;
        }

        return new HueRecognition(
            best.Value,
            Math.Min(1.0, 0.98 + Math.Min(0.02, (best.Score - second) * 0.05)));
    }

    private static BitmapSource DecodeFrame(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static bool HasAdoptedIdentity(AppOwnedFormalEvidence evidence) =>
        !string.IsNullOrWhiteSpace(evidence.MasterVersion) &&
        !string.IsNullOrWhiteSpace(evidence.SongId) &&
        !string.IsNullOrWhiteSpace(evidence.ChartId) &&
        evidence.Sources.TryGetValue("master_version", out var masterSource) &&
        masterSource == FormalEvidenceSourceNames.MasterMetadata &&
        evidence.Sources.TryGetValue("song_id", out var songSource) &&
        songSource == FormalEvidenceSourceNames.ResultIdentityVisualEvidence &&
        evidence.Sources.TryGetValue("chart_id", out var chartSource) &&
        chartSource == FormalEvidenceSourceNames.ResultIdentityVisualEvidence;

    private static LiveResultObservation WithReason(
        LiveResultObservation observation,
        string reason)
    {
        var evidence = observation.FormalEvidence;
        if (evidence is null)
        {
            return observation;
        }

        var reasons = (evidence.RecognitionReasons ?? Array.Empty<string>())
            .Append(reason)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return observation with
        {
            FormalEvidence = evidence with { RecognitionReasons = reasons },
        };
    }

    private static IReadOnlyList<string> RemoveIdentityProducerReasons(
        IReadOnlyList<string> reasons) =>
        reasons
            .Where(reason =>
                !reason.StartsWith("formal_evidence.identity_visual_", StringComparison.Ordinal) &&
                !reason.StartsWith("formal_evidence.play_style_visual_", StringComparison.Ordinal) &&
                !reason.StartsWith("formal_evidence.difficulty_visual_", StringComparison.Ordinal) &&
                !reason.StartsWith("formal_evidence.level_visual_", StringComparison.Ordinal) &&
                !reason.StartsWith("formal_evidence.chart_visual_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static double VisualConfidence(double distance, double? margin)
    {
        var distanceStrength = Math.Clamp(
            (JacketDistanceThreshold - distance) / JacketDistanceThreshold,
            0.0,
            1.0);
        var marginStrength = margin is null
            ? 1.0
            : Math.Clamp(margin.Value / JacketAmbiguityDelta, 0.0, 1.0);
        return Math.Min(1.0, 0.98 + 0.02 * Math.Min(distanceStrength, marginStrength));
    }

    private static double JacketFeatureDistance(
        JacketFeature observed,
        VisualReference reference)
    {
        var thumbnailDistance = MeanAbsoluteDifference(
            observed.Thumbnail,
            reference.Thumbnail);
        var histogramDistance = MeanAbsoluteDifference(
            observed.Histogram,
            reference.Histogram);
        var dhashDistance = MeanAbsoluteDifference(observed.Dhash, reference.Dhash);
        return 0.70 * thumbnailDistance + 0.20 * histogramDistance + 0.10 * dhashDistance;
    }

    private static double MeanAbsoluteDifference(
        IReadOnlyList<double> left,
        IReadOnlyList<double> right)
    {
        if (left.Count != right.Count || left.Count == 0)
        {
            return 1.0;
        }

        var sum = 0.0;
        for (var index = 0; index < left.Count; index++)
        {
            sum += Math.Abs(left[index] - right[index]);
        }
        return sum / left.Count;
    }

    private static JacketFeature ExtractJacketFeature(AppOwnedImageBuffer image)
    {
        var thumbnail = image.ResizeRgb(16, 16);
        var histogram = new double[24];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                histogram[Math.Clamp(pixel.Red * 8 / 256, 0, 7)]++;
                histogram[8 + Math.Clamp(pixel.Green * 8 / 256, 0, 7)]++;
                histogram[16 + Math.Clamp(pixel.Blue * 8 / 256, 0, 7)]++;
            }
        }
        var histogramSum = histogram.Sum();
        if (histogramSum > 0)
        {
            for (var index = 0; index < histogram.Length; index++)
            {
                histogram[index] /= histogramSum;
            }
        }

        var dhash = new double[64];
        var grayscale = image.ResizeRgb(9, 8);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var left = Luma(grayscale, y * 9 + x);
                var right = Luma(grayscale, y * 9 + x + 1);
                dhash[y * 8 + x] = right > left ? 1.0 : 0.0;
            }
        }

        return new JacketFeature(thumbnail, histogram, dhash);
    }

    private static double Luma(IReadOnlyList<double> rgb, int pixelIndex) =>
        0.2126 * rgb[pixelIndex * 3] +
        0.7152 * rgb[pixelIndex * 3 + 1] +
        0.0722 * rgb[pixelIndex * 3 + 2];

    private static double[] ReadVector(
        string json,
        int expectedLength,
        double minimum,
        double maximum)
    {
        var values = JsonSerializer.Deserialize<double[]>(json) ??
            throw new InvalidDataException("Visual reference vector is null.");
        if (values.Length != expectedLength || values.Any(value =>
                !double.IsFinite(value) || value < minimum || value > maximum))
        {
            throw new InvalidDataException("Visual reference vector is invalid.");
        }
        return values;
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string? ReadMasterVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT value FROM master_metadata WHERE key = 'master_version';";
        return command.ExecuteScalar()?.ToString();
    }

    private static double CircularDistance(double left, double right)
    {
        var difference = Math.Abs(left - right) % 360.0;
        return Math.Min(difference, 360.0 - difference);
    }

    private static (double Hue, double Saturation, double Value) Hsv(AppOwnedPixel pixel)
    {
        var red = pixel.Red / 255.0;
        var green = pixel.Green / 255.0;
        var blue = pixel.Blue / 255.0;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        var hue = 0.0;
        if (delta > 1e-6)
        {
            hue = maximum == red
                ? 60.0 * ((green - blue) / delta % 6.0)
                : maximum == green
                    ? 60.0 * ((blue - red) / delta + 2.0)
                    : 60.0 * ((red - green) / delta + 4.0);
            if (hue < 0) hue += 360.0;
        }
        var saturation = maximum == 0 ? 0 : delta / maximum;
        return (hue, saturation, maximum);
    }

    private sealed record IdentityReferenceSet(
        string MasterVersion,
        IReadOnlyList<VisualReference> References,
        string? FailureReason)
    {
        public static IdentityReferenceSet Failed(string reason) =>
            new(string.Empty, Array.Empty<VisualReference>(), reason);
    }

    private sealed record VisualReference(
        string SongId,
        double[] Thumbnail,
        double[] Histogram,
        double[] Dhash);

    private sealed record CurrentMasterSong(string Title, string Artist);

    private sealed record JacketFeature(
        double[] Thumbnail,
        double[] Histogram,
        double[] Dhash);

    private sealed record ChartCandidate(string SongId, string ChartId);

    private sealed record ChartContextResult(
        string? PlayStyle,
        string? Difficulty,
        int? Level,
        double? Confidence,
        string? FailureReason)
    {
        public static ChartContextResult Failed(string reason) =>
            new(null, null, null, null, reason);
    }

    private sealed record HueRecognition(string? Value, double? Confidence)
    {
        public static HueRecognition Unknown { get; } = new(null, null);
    }
}
