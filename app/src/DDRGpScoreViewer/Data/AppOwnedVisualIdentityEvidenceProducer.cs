using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
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
    private const double ResultTextAmbiguityDelta = 0.01;
    private const string ResultTextFeatureSchemaVersion =
        "m7-result-text-feature-master-v1";
    private const string ResultTextFeatureVersion = "m7-result-text-image-v1";
    private const string ResultTextRoiVersion = "m7-result-title-artist-roi-v1";

    private static readonly (int X, int Y, int Width, int Height) JacketRoi =
        (532, 54, 216, 216);

    private static readonly (int X, int Y, int Width, int Height) StyleRoi =
        (360, 56, 100, 24);

    private static readonly (int X, int Y, int Width, int Height) DifficultyRoi =
        (378, 80, 84, 24);

    private static readonly (int X, int Y, int Width, int Height) LevelRoi =
        (392, 104, 38, 31);

    private static readonly (int X, int Y, int Width, int Height) ResultTitleRoi =
        (488, 274, 304, 32);

    private static readonly (int X, int Y, int Width, int Height) ResultArtistRoi =
        (548, 306, 184, 26);

    private static readonly IReadOnlyDictionary<string, double> StyleHues =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["SINGLE"] = 210.0,
            ["DOUBLE"] = 300.0,
        };

    private static readonly IReadOnlyDictionary<string, double> DifficultyHues =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["BEGINNER"] = 190.0,
            ["BASIC"] = 35.0,
            ["DIFFICULT"] = 350.0,
            ["EXPERT"] = 112.0,
            ["CHALLENGE"] = 290.0,
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

            var jacketAmbiguous = orderedMatches.Skip(1).Any(match =>
                match.Distance - best.Distance <= JacketAmbiguityDelta);
            var selectedSongId = best.SongId;
            var identityConfidence = VisualConfidence(
                best.Distance,
                orderedMatches.Length > 1
                    ? orderedMatches[1].Distance - best.Distance
                    : null);
            if (jacketAmbiguous)
            {
                var ambiguousSongIds = orderedMatches
                    .Where(match =>
                        match.Distance - best.Distance <= JacketAmbiguityDelta)
                    .Select(match => match.SongId)
                    .ToHashSet(StringComparer.Ordinal);
                var textReferences = LoadResultTextFeatures(
                    catalogDatabasePath,
                    references.CurrentSongs);
                var titleResolution = ResolveResultTextFeature(
                    image.CropScaled(ResultTitleRoi),
                    "title",
                    candidates,
                    ambiguousSongIds,
                    textReferences);
                var resolutionReasons = new List<string>();
                if (titleResolution.SongId is not null)
                {
                    selectedSongId = titleResolution.SongId;
                    identityConfidence = ResultTextConfidence(
                        titleResolution.Distance!.Value,
                        titleResolution.Margin);
                }
                else
                {
                    resolutionReasons.Add(titleResolution.Reason);
                    var artistResolution = ResolveResultTextFeature(
                        image.CropScaled(ResultArtistRoi),
                        "artist",
                        candidates,
                        ambiguousSongIds,
                        textReferences);
                    if (artistResolution.SongId is not null)
                    {
                        selectedSongId = artistResolution.SongId;
                        identityConfidence = ResultTextConfidence(
                            artistResolution.Distance!.Value,
                            artistResolution.Margin);
                    }
                    else
                    {
                        resolutionReasons.Add(artistResolution.Reason);
                        return WithReasons(
                            observation,
                            resolutionReasons.Append(
                                "formal_evidence.identity_visual_ambiguous"));
                    }
                }
            }

            var matchingCharts = candidates
                .Where(candidate => candidate.SongId == selectedSongId)
                .ToArray();
            if (matchingCharts.Length != 1)
            {
                return WithReason(
                    observation,
                    matchingCharts.Length == 0
                        ? "formal_evidence.chart_visual_not_found"
                        : "formal_evidence.chart_visual_ambiguous");
            }

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
                SongId = selectedSongId,
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
                cachedReferences = new IdentityReferenceSet(
                    masterVersion,
                    currentSongs,
                    references,
                    null);
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

    private static IReadOnlyList<ResultTextReference> LoadResultTextFeatures(
        string? catalogDatabasePath,
        IReadOnlyDictionary<string, CurrentMasterSong> currentSongs)
    {
        if (string.IsNullOrWhiteSpace(catalogDatabasePath))
        {
            return Array.Empty<ResultTextReference>();
        }

        using var catalog = OpenReadOnly(catalogDatabasePath);
        return ReadResultTextFeatures(catalog, currentSongs);
    }

    private static IReadOnlyList<ResultTextReference> ReadResultTextFeatures(
        SqliteConnection connection,
        IReadOnlyDictionary<string, CurrentMasterSong> currentSongs)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT feature_id, song_id, field_name, feature_version, roi_version, " +
            "feature_hash, payload_json, source_label, master_version, " +
            "canonical_title_snapshot, canonical_artist_snapshot " +
            "FROM result_text_features;";
        using var reader = command.ExecuteReader();
        var features = new List<ResultTextReference>();
        while (reader.Read())
        {
            if (Enumerable.Range(0, 11).Any(reader.IsDBNull))
            {
                continue;
            }

            var featureId = reader.GetString(0);
            var songId = reader.GetString(1);
            var fieldName = reader.GetString(2);
            var featureVersion = reader.GetString(3);
            var roiVersion = reader.GetString(4);
            var featureHash = reader.GetString(5);
            var payloadJson = reader.GetString(6);
            var sourceLabel = reader.GetString(7);
            var masterVersion = reader.GetString(8);
            var canonicalTitle = reader.GetString(9);
            var canonicalArtist = reader.GetString(10);
            if (fieldName is not ("title" or "artist") ||
                string.IsNullOrWhiteSpace(featureId) ||
                string.IsNullOrWhiteSpace(sourceLabel) ||
                string.IsNullOrWhiteSpace(masterVersion) ||
                !currentSongs.TryGetValue(songId, out var currentSong) ||
                !string.Equals(
                    currentSong.Title,
                    canonicalTitle,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    currentSong.Artist,
                    canonicalArtist,
                    StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var feature = ReadResultTextFeature(
                    featureVersion,
                    roiVersion,
                    featureHash,
                    payloadJson);
                var expectedFeatureId = BuildResultTextFeatureId(
                    masterVersion,
                    songId,
                    fieldName,
                    featureVersion,
                    roiVersion,
                    featureHash);
                if (!string.Equals(
                        expectedFeatureId,
                        featureId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                features.Add(new ResultTextReference(songId, fieldName, feature));
            }
            catch (Exception exception) when (
                exception is FormatException or InvalidDataException or JsonException)
            {
                // A malformed feature row is unavailable evidence, not a reason
                // to reject otherwise usable jacket references.
            }
        }

        return features;
    }

    private static ResultTextResolution ResolveResultTextFeature(
        AppOwnedImageBuffer roi,
        string fieldName,
        IReadOnlyList<ChartCandidate> candidates,
        IReadOnlySet<string> ambiguousSongIds,
        IReadOnlyList<ResultTextReference> references)
    {
        var observed = ExtractResultTextFeature(roi);
        var candidateSongIds = candidates
            .Where(candidate => ambiguousSongIds.Contains(candidate.SongId))
            .Select(candidate => candidate.SongId)
            .ToHashSet(StringComparer.Ordinal);
        var fieldReferences = references
            .Where(reference =>
                reference.FieldName == fieldName &&
                candidateSongIds.Contains(reference.SongId))
            .ToArray();
        if (candidateSongIds.Count == 0 || candidateSongIds.Any(songId =>
                !fieldReferences.Any(reference => reference.SongId == songId)))
        {
            return ResultTextResolution.Unavailable(fieldName);
        }

        var scored = fieldReferences
            .GroupBy(reference => reference.SongId, StringComparer.Ordinal)
            .Select(group =>
            {
                var distance = group.Min(reference =>
                    ResultTextFeatureDistance(observed, reference.Feature));
                return (Distance: distance, SongId: group.Key);
            })
            .OrderBy(match => match.Distance)
            .ThenBy(match => match.SongId, StringComparer.Ordinal)
            .ToArray();
        if (scored.Length == 0)
        {
            return ResultTextResolution.Unavailable(fieldName);
        }

        var best = scored[0];
        var margin = scored.Length > 1
            ? scored[1].Distance - best.Distance
            : (double?)null;
        if (scored.Skip(1).Any(match =>
                match.Distance - best.Distance <= ResultTextAmbiguityDelta))
        {
            return ResultTextResolution.Ambiguous(fieldName, best.Distance, margin);
        }

        return ResultTextResolution.Resolved(best.SongId, best.Distance, margin);
    }

    private static ResultTextFeature ReadResultTextFeature(
        string featureVersion,
        string roiVersion,
        string featureHash,
        string payloadJson)
    {
        if (featureVersion != ResultTextFeatureVersion ||
            roiVersion != ResultTextRoiVersion ||
            !IsSha256(featureHash))
        {
            throw new InvalidDataException("Result text feature identity is invalid.");
        }

        using var document = JsonDocument.Parse(payloadJson);
        var payload = document.RootElement;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Result text feature payload is not an object.");
        }

        var canonicalPayload = CanonicalJson(payload);
        var actualHash = Convert.ToHexString(
                SHA256.HashData(canonicalPayload))
            .ToLowerInvariant();
        if (!string.Equals(actualHash, featureHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Result text feature payload hash mismatch.");
        }

        if (RequiredString(payload, "feature_version") != ResultTextFeatureVersion ||
            RequiredString(payload, "roi_version") != ResultTextRoiVersion ||
            RequiredString(payload, "vector_encoding") != "uint8_0_255")
        {
            throw new InvalidDataException("Result text feature payload identity is invalid.");
        }

        var luma = ReadResultTextVector(payload, "luma", 96 * 16, [96, 16]);
        var edge = ReadResultTextVector(payload, "edge", 96 * 16, [96, 16]);
        var suffixLuma = ReadResultTextVector(
            payload,
            "suffix_luma",
            40 * 16,
            [40, 16]);
        var suffixEdge = ReadResultTextVector(
            payload,
            "suffix_edge",
            40 * 16,
            [40, 16]);
        var dhash = ReadHexBits(RequiredString(payload, "dhash_hex"), 64);
        if (!payload.TryGetProperty("linehash_rows", out var linehashRows) ||
            linehashRows.ValueKind != JsonValueKind.Array ||
            linehashRows.GetArrayLength() != 28)
        {
            throw new InvalidDataException("Result text feature linehash rows are invalid.");
        }

        foreach (var row in linehashRows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.String ||
                !IsHex(row.GetString() ?? string.Empty, 76))
            {
                throw new InvalidDataException("Result text feature linehash row is invalid.");
            }
        }

        return new ResultTextFeature(luma, edge, suffixLuma, suffixEdge, dhash);
    }

    private static double[] ReadResultTextVector(
        JsonElement payload,
        string fieldName,
        int expectedLength,
        int[] expectedShape)
    {
        if (!payload.TryGetProperty($"{fieldName}_shape", out var shape) ||
            shape.ValueKind != JsonValueKind.Array ||
            shape.GetArrayLength() != expectedShape.Length)
        {
            throw new InvalidDataException($"Result text feature {fieldName} shape is invalid.");
        }

        var shapeValues = shape.EnumerateArray()
            .Select(value =>
            {
                if (value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetInt32(out var parsed) ||
                    parsed <= 0)
                {
                    throw new InvalidDataException(
                        $"Result text feature {fieldName} shape is invalid.");
                }
                return parsed;
            })
            .ToArray();
        if (!shapeValues.SequenceEqual(expectedShape) ||
            !payload.TryGetProperty(fieldName, out var values) ||
            values.ValueKind != JsonValueKind.Array ||
            values.GetArrayLength() != expectedLength)
        {
            throw new InvalidDataException($"Result text feature {fieldName} is invalid.");
        }

        var result = new double[expectedLength];
        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number ||
                !value.TryGetInt32(out var parsed) || parsed is < 0 or > 255)
            {
                throw new InvalidDataException($"Result text feature {fieldName} value is invalid.");
            }
            result[index++] = parsed / 255.0;
        }
        return result;
    }

    private static double[] ReadHexBits(string value, int bitCount)
    {
        if (!IsHex(value, (bitCount + 3) / 4))
        {
            throw new InvalidDataException("Result text feature bit payload is invalid.");
        }

        var bits = new double[bitCount];
        var index = 0;
        foreach (var character in value)
        {
            var nibble = HexValue(character);
            for (var bit = 3; bit >= 0 && index < bitCount; bit--)
            {
                bits[index++] = (nibble >> bit) & 1;
            }
        }
        return bits;
    }

    private static string RequiredString(JsonElement objectValue, string propertyName)
    {
        if (!objectValue.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException(
                $"Result text feature payload {propertyName} is invalid.");
        }
        return value.GetString()!;
    }

    private static byte[] CanonicalJson(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions
                   {
                       Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                   }))
        {
            WriteCanonicalJson(writer, value);
        }
        return stream.ToArray();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(
                             property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("Result text feature payload value is invalid.");
        }
    }

    private static string BuildResultTextFeatureId(
        string masterVersion,
        string songId,
        string fieldName,
        string featureVersion,
        string roiVersion,
        string featureHash)
    {
        var material = string.Join(
            "\0",
            ResultTextFeatureSchemaVersion,
            masterVersion,
            songId,
            fieldName,
            featureVersion,
            roiVersion,
            featureHash);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }

    private static bool IsSha256(string value) => IsHex(value, 64);

    private static bool IsHex(string value, int length) =>
        value.Length == length && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static int HexValue(char character) =>
        character is >= '0' and <= '9'
            ? character - '0'
            : character is >= 'a' and <= 'f'
                ? character - 'a' + 10
                : character - 'A' + 10;

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
            segmentationRoiName: "chart_level",
            templateGroup: "chart_level",
            maximumDistance: 0.28,
            minimumMargin: 0.02,
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
        return WithReasons(observation, [reason]);
    }

    private static LiveResultObservation WithReasons(
        LiveResultObservation observation,
        IEnumerable<string> reasonsToAdd)
    {
        var evidence = observation.FormalEvidence;
        if (evidence is null)
        {
            return observation;
        }

        var reasons = (evidence.RecognitionReasons ?? Array.Empty<string>())
            .Concat(reasonsToAdd)
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

    private static double ResultTextConfidence(double distance, double? margin)
    {
        var distanceStrength = Math.Clamp(1.0 - distance, 0.0, 1.0);
        var marginStrength = margin is null
            ? 1.0
            : Math.Clamp(margin.Value / ResultTextAmbiguityDelta, 0.0, 1.0);
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

    private static double ResultTextFeatureDistance(
        ResultTextFeature observed,
        ResultTextFeature reference)
    {
        var lumaDistance = MeanAbsoluteDifference(observed.Luma, reference.Luma);
        var edgeDistance = MeanAbsoluteDifference(observed.Edge, reference.Edge);
        var suffixLumaDistance = MeanAbsoluteDifference(
            observed.SuffixLuma,
            reference.SuffixLuma);
        var suffixEdgeDistance = MeanAbsoluteDifference(
            observed.SuffixEdge,
            reference.SuffixEdge);
        var dhashDistance = MeanAbsoluteDifference(observed.Dhash, reference.Dhash);
        return (0.35 * lumaDistance) +
            (0.15 * edgeDistance) +
            (0.30 * suffixLumaDistance) +
            (0.10 * suffixEdgeDistance) +
            (0.10 * dhashDistance);
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

    private static ResultTextFeature ExtractResultTextFeature(AppOwnedImageBuffer image)
    {
        var grayscale = ToGrayscale(image);
        var contrasted = AutoContrast(grayscale);
        var resized = ResizeGrayscale(
            contrasted,
            image.Width,
            image.Height,
            96,
            16);
        var resizedPixels = QuantizeResultTextPixels(resized);
        var suffixLeft = Math.Clamp(
            (int)Math.Round(image.Width * 0.62, MidpointRounding.ToEven),
            0,
            Math.Max(0, image.Width - 1));
        var suffixWidth = Math.Max(1, image.Width - suffixLeft);
        var suffix = CropGrayscale(
            contrasted,
            image.Width,
            image.Height,
            suffixLeft,
            suffixWidth);
        var suffixResized = ResizeGrayscale(
            suffix,
            suffixWidth,
            image.Height,
            40,
            16);
        var suffixPixels = QuantizeResultTextPixels(suffixResized);
        var dhashPixels = ResizeGrayscale(
            contrasted,
            image.Width,
            image.Height,
            9,
            8);
        dhashPixels = QuantizeResultTextPixels(dhashPixels);
        var dhash = new double[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                dhash[y * 8 + x] = dhashPixels[y * 9 + x + 1] >
                    dhashPixels[y * 9 + x]
                        ? 1.0
                        : 0.0;
            }
        }

        return new ResultTextFeature(
            QuantizeResultTextVector(resizedPixels),
            QuantizeResultTextVector(FindEdges(resizedPixels, 96, 16)),
            QuantizeResultTextVector(suffixPixels),
            QuantizeResultTextVector(FindEdges(suffixPixels, 40, 16)),
            dhash);
    }

    private static double[] ToGrayscale(AppOwnedImageBuffer image)
    {
        var result = new double[image.Width * image.Height];
        var index = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                result[index++] = Math.Clamp(
                    Math.Round(
                        0.299 * pixel.Red +
                        0.587 * pixel.Green +
                        0.114 * pixel.Blue,
                        MidpointRounding.AwayFromZero),
                    0.0,
                    255.0);
            }
        }
        return result;
    }

    private static double[] AutoContrast(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return Array.Empty<double>();
        }

        var minimum = values.Min();
        var maximum = values.Max();
        if (maximum <= minimum)
        {
            return values.ToArray();
        }

        var result = new double[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = Math.Clamp(
                Math.Round(
                    (values[index] - minimum) * 255.0 / (maximum - minimum),
                    MidpointRounding.AwayFromZero),
                0.0,
                255.0);
        }
        return result;
    }

    private static double[] CropGrayscale(
        IReadOnlyList<double> source,
        int sourceWidth,
        int sourceHeight,
        int left,
        int width)
    {
        var result = new double[width * sourceHeight];
        for (var y = 0; y < sourceHeight; y++)
        {
            for (var x = 0; x < width; x++)
            {
                result[y * width + x] = source[y * sourceWidth + left + x];
            }
        }
        return result;
    }

    private static double[] ResizeGrayscale(
        IReadOnlyList<double> source,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        var horizontal = new double[targetWidth * sourceHeight];
        for (var y = 0; y < sourceHeight; y++)
        {
            for (var x = 0; x < targetWidth; x++)
            {
                var center = (x + 0.5) * sourceWidth / targetWidth - 0.5;
                horizontal[y * targetWidth + x] = ResampleLanczos(
                    source,
                    y * sourceWidth,
                    sourceWidth,
                    center,
                    sourceWidth / (double)targetWidth);
            }
        }

        var result = new double[targetWidth * targetHeight];
        for (var y = 0; y < targetHeight; y++)
        {
            var center = (y + 0.5) * sourceHeight / targetHeight - 0.5;
            for (var x = 0; x < targetWidth; x++)
            {
                var column = new double[sourceHeight];
                for (var sourceY = 0; sourceY < sourceHeight; sourceY++)
                {
                    column[sourceY] = horizontal[sourceY * targetWidth + x];
                }
                result[y * targetWidth + x] = ResampleLanczos(
                    column,
                    0,
                    sourceHeight,
                    center,
                    sourceHeight / (double)targetHeight);
            }
        }
        return result;
    }

    private static double ResampleLanczos(
        IReadOnlyList<double> source,
        int offset,
        int sourceLength,
        double center,
        double scale)
    {
        scale = Math.Max(1.0, scale);
        var radius = 3.0 * scale;
        var first = Math.Max(0, (int)Math.Ceiling(center - radius));
        var last = Math.Min(sourceLength - 1, (int)Math.Floor(center + radius));
        var weighted = 0.0;
        var total = 0.0;
        for (var index = first; index <= last; index++)
        {
            var distance = (index - center) / scale;
            var weight = Lanczos(distance);
            weighted += source[offset + index] * weight;
            total += weight;
        }
        return total == 0.0 ? source[offset + Math.Clamp((int)Math.Round(center), 0, sourceLength - 1)] : weighted / total;
    }

    private static double Lanczos(double value)
    {
        var absolute = Math.Abs(value);
        if (absolute >= 3.0)
        {
            return 0.0;
        }
        if (absolute < 1e-12)
        {
            return 1.0;
        }
        var piValue = Math.PI * value;
        return (Math.Sin(piValue) / piValue) *
            (Math.Sin(piValue / 3.0) / (piValue / 3.0));
    }

    private static double[] FindEdges(
        IReadOnlyList<double> source,
        int width,
        int height)
    {
        var result = new double[source.Count];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    result[y * width + x] = source[y * width + x];
                    continue;
                }

                var center = source[y * width + x] * 8.0;
                var sum = center;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }
                        sum -= source[(y + dy) * width + x + dx];
                    }
                }
                result[y * width + x] = Math.Clamp(
                    Math.Round(sum, MidpointRounding.AwayFromZero),
                    0.0,
                    255.0);
            }
        }
        return result;
    }

    private static double[] QuantizeResultTextPixels(IReadOnlyList<double> values) =>
        values.Select(value =>
            Math.Clamp(
                Math.Round(value, MidpointRounding.AwayFromZero),
                0.0,
                255.0))
            .ToArray();

    private static double[] QuantizeResultTextVector(IReadOnlyList<double> values) =>
        values.Select(value =>
            Math.Clamp(
                Math.Round(value, MidpointRounding.AwayFromZero),
                0.0,
                255.0) / 255.0)
            .ToArray();

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
        IReadOnlyDictionary<string, CurrentMasterSong> CurrentSongs,
        IReadOnlyList<VisualReference> References,
        string? FailureReason)
    {
        public static IdentityReferenceSet Failed(string reason) =>
            new(
                string.Empty,
                new Dictionary<string, CurrentMasterSong>(StringComparer.Ordinal),
                Array.Empty<VisualReference>(),
                reason);
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

    private sealed record ResultTextFeature(
        double[] Luma,
        double[] Edge,
        double[] SuffixLuma,
        double[] SuffixEdge,
        double[] Dhash);

    private sealed record ResultTextReference(
        string SongId,
        string FieldName,
        ResultTextFeature Feature);

    private sealed record ResultTextResolution(
        string? SongId,
        double? Distance,
        double? Margin,
        string Reason)
    {
        public static ResultTextResolution Resolved(
            string songId,
            double distance,
            double? margin) =>
            new(songId, distance, margin, string.Empty);

        public static ResultTextResolution Unavailable(string fieldName) =>
            new(
                null,
                null,
                null,
                $"formal_evidence.identity_visual_{fieldName}_feature_unavailable");

        public static ResultTextResolution Ambiguous(
            string fieldName,
            double distance,
            double? margin) =>
            new(
                null,
                distance,
                margin,
                $"formal_evidence.identity_visual_{fieldName}_feature_ambiguous");
    }

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
