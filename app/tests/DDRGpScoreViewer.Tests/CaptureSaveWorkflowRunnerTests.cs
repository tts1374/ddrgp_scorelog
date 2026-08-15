using System.Text;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class CaptureSaveWorkflowRunnerTests
{
    [Fact]
    public async Task Complete_formal_evidence_saves_a_nonzero_result_once()
    {
        using var database = new DatabaseFixture();
        var result = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
            Frame(1_000),
            FormalObservation(987650),
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("completed", result.Status);
        Assert.Equal(1, result.EventCount);
        Assert.Equal(1, result.StatusCounts["saved"]);
        Assert.Single(result.SavedPlayIds);
        using var connection = OpenReadOnly(database.ScorePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT score, source_kind FROM plays " +
            "JOIN source_captures ON source_captures.capture_id = plays.source_capture_id;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(987650, reader.GetInt32(0));
        Assert.Equal("capture", reader.GetString(1));
    }

    [Fact]
    public async Task Adopted_optional_metrics_roundtrip_and_low_confidence_calories_stays_null()
    {
        using var database = new DatabaseFixture();
        var runner = new AppOwnedCaptureSaveWorkflowRunner();
        var adoptedObservation = FormalObservation(
            987650,
            confirmedEventId: "confirmed-event-v1:metrics");
        var adoptedEvidence = adoptedObservation.FormalEvidence! with
        {
            Calories = 28.6,
            Sources = WithSource(
                adoptedObservation.FormalEvidence!.Sources,
                "calories",
                FormalEvidenceSourceNames.ResultNumericVisualEvidence),
            Confidences = WithConfidence(
                adoptedObservation.FormalEvidence!.Confidences,
                "calories",
                0.99),
        };
        var lowConfidenceObservation = FormalObservation(
            987650,
            confirmedEventId: "confirmed-event-v1:calories-low-confidence");
        var lowConfidenceEvidence = lowConfidenceObservation.FormalEvidence! with
        {
            Calories = 99.9,
            Sources = WithSource(
                lowConfidenceObservation.FormalEvidence!.Sources,
                "calories",
                FormalEvidenceSourceNames.ResultNumericVisualEvidence),
            Confidences = WithConfidence(
                lowConfidenceObservation.FormalEvidence!.Confidences,
                "calories",
                0.97),
        };

        var adopted = await runner.RunCandidateAsync(
            Frame(1_000),
            adoptedObservation with { FormalEvidence = adoptedEvidence },
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);
        var missing = await runner.RunCandidateAsync(
            Frame(2_000, DateTimeOffset.Parse("2026-07-29T12:00:01+09:00")),
            lowConfidenceObservation with { FormalEvidence = lowConfidenceEvidence },
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal(1, adopted.StatusCounts["saved"]);
        Assert.Equal(1, missing.StatusCounts["saved"]);
        using var connection = OpenReadOnly(database.ScorePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ok, calories FROM plays ORDER BY played_at;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(28.6, reader.GetDouble(1));
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public async Task Reprocessing_the_same_result_event_is_a_duplicate_without_a_second_play()
    {
        using var database = new DatabaseFixture();
        var runner = new AppOwnedCaptureSaveWorkflowRunner();
        const string confirmedEventId = "confirmed-event-v1:replayed";
        var first = await runner.RunCandidateAsync(
            Frame(1_000),
            FormalObservation(987650, confirmedEventId: confirmedEventId),
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);
        var second = await runner.RunCandidateAsync(
            Frame(2_000, DateTimeOffset.Parse("2026-07-29T12:00:01+09:00")),
            FormalObservation(987650, confirmedEventId: confirmedEventId) with
            {
                TitleSignature = "animated-frame-2",
            },
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("completed", first.Status);
        Assert.Equal("completed", second.Status);
        Assert.Equal(1, first.StatusCounts["saved"]);
        Assert.Equal(1, second.StatusCounts["duplicate"]);
        Assert.Empty(second.SavedPlayIds);
        using var connection = OpenReadOnly(database.ScorePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT COUNT(*) FROM plays), (SELECT COUNT(*) FROM source_captures), " +
            "(SELECT COUNT(*) FROM analysis_logs);";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.Equal(2L, reader.GetInt64(2));
    }

    [Fact]
    public async Task Separate_confirmed_events_with_identical_formal_values_both_save()
    {
        using var database = new DatabaseFixture();
        var runner = new AppOwnedCaptureSaveWorkflowRunner();
        var first = await runner.RunCandidateAsync(
            Frame(1_000),
            FormalObservation(987650, confirmedEventId: "confirmed-event-v1:first"),
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);
        var second = await runner.RunCandidateAsync(
            Frame(2_000, DateTimeOffset.Parse("2026-07-29T12:00:01+09:00")),
            FormalObservation(987650, confirmedEventId: "confirmed-event-v1:second"),
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("completed", first.Status);
        Assert.Equal("completed", second.Status);
        Assert.Equal(1, first.StatusCounts["saved"]);
        Assert.Equal(1, second.StatusCounts["saved"]);
        Assert.Single(first.SavedPlayIds);
        Assert.Single(second.SavedPlayIds);
        using var connection = OpenReadOnly(database.ScorePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*), COUNT(DISTINCT duplicate_key) FROM plays;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    [Fact]
    public async Task Zero_score_is_saved_as_a_valid_formal_value()
    {
        using var database = new DatabaseFixture();
        var result = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
            Frame(1_000),
            FormalObservation(0, rank: "D") with { Score = "999000" },
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("completed", result.Status);
        using var connection = OpenReadOnly(database.ScorePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT score FROM plays;";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Recognized_digits_without_formal_evidence_remain_unresolved()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-formal-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var result = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
                Frame(1_000),
                new LiveResultObservation(
                    true,
                    "987650",
                    "event-1",
                    "recognized",
                    DigitRecognitionStatus: "recognized"),
                Path.Combine(root, "score.sqlite"),
                "master.sqlite",
                null);

            Assert.Equal("completed", result.Status);
            Assert.Contains("automatic_formal_evidence_missing", result.Reasons);
            Assert.Contains("formal_play_required", result.Reasons);
            var eventResult = Assert.Single(result.EventResults!);
            Assert.Equal("unresolved", eventResult.Status);
            Assert.StartsWith("confirmed-event-v1:", eventResult.EventId);
            Assert.Contains("formal_play_required", eventResult.Reasons);
            Assert.False(File.Exists(Path.Combine(root, "score.sqlite")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("missing_reference")]
    [InlineData("failed_segmentation")]
    public async Task Digit_recognition_failures_do_not_save_formal_evidence(string status)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-formal-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var result = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
                Frame(1_000),
                FormalObservation(987650, digitStatus: status),
                Path.Combine(root, "score.sqlite"),
                "master.sqlite",
                null);

            Assert.Equal("completed", result.Status);
            Assert.Contains($"digit_recognition.{status}", result.Reasons);
            Assert.False(File.Exists(Path.Combine(root, "score.sqlite")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("rank")]
    [InlineData("clear_type")]
    [InlineData("confidence")]
    public async Task Incomplete_formal_evidence_does_not_save(string failure)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-formal-incomplete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var observation = FormalObservation(987650);
            var evidence = observation.FormalEvidence!;
            evidence = failure switch
            {
                "identity" => evidence with { SongId = null },
                "rank" => evidence with { Rank = null },
                "clear_type" => evidence with { ClearType = null },
                "confidence" => evidence with
                {
                    Confidences = WithConfidence(evidence.Confidences, "score", 0.97),
                },
                _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
            };
            var result = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
                Frame(1_000),
                observation with { FormalEvidence = evidence },
                Path.Combine(root, "score.sqlite"),
                "master.sqlite",
                null);

            Assert.Equal("completed", result.Status);
            Assert.Contains(
                failure == "identity"
                    ? "formal_evidence.song_id_missing"
                    : failure == "confidence"
                        ? "formal_evidence.score_confidence_insufficient"
                        : $"formal_evidence.{failure}_missing",
                result.Reasons);
            Assert.False(File.Exists(Path.Combine(root, "score.sqlite")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("song_id", "result_identity_ocr")]
    [InlineData("score", "result_numeric_ocr")]
    public async Task Non_visual_formal_source_is_not_promoted(
        string fieldName,
        string source)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-formal-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var observation = FormalObservation(987650);
            var originalEvidence = observation.FormalEvidence!;
            var evidence = originalEvidence with
            {
                Sources = WithSource(originalEvidence.Sources, fieldName, source),
            };
            var result = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
                Frame(1_000),
                observation with { FormalEvidence = evidence },
                Path.Combine(root, "score.sqlite"),
                "master.sqlite",
                null);

            Assert.Equal("completed", result.Status);
            Assert.Contains(
                $"formal_evidence.{fieldName}_source_not_adopted",
                result.Reasons);
            Assert.False(File.Exists(Path.Combine(root, "score.sqlite")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Live_candidate_uses_the_app_owned_workflow_without_a_checkout()
    {
        var frame = new CapturedFrame(
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPgEpH7DwABpAE8k4sOtwAAAABJRU5ErkJggg=="),
            1280,
            720,
            1_000,
            DateTimeOffset.Parse("2026-07-29T12:00:00+09:00"),
            "fixture");
        var runner = new AppOwnedCaptureSaveWorkflowRunner();

        var result = await runner.RunCandidateAsync(
            frame,
            Path.Combine(Path.GetTempPath(), "app-owned-score.sqlite"),
            "master.sqlite",
            null);

        Assert.Equal("completed", result.Status);
        Assert.Equal(1, result.EventCount);
        Assert.Equal(1, result.StatusCounts["unresolved"]);
        Assert.Contains("formal_play_required", result.Reasons);
    }

    [Fact]
    public async Task Manifest_capture_uses_a_fixed_result_key_for_different_known_result_frames()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-capture-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var frameBytes = new[]
            {
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPgEpH7DwABpAE8k4sOtwAAAABJRU5ErkJggg=="),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPQMLL5DwACsgGWiwRo7AAAAABJRU5ErkJggg=="),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNwC4j6DwADwAHw4MV2LAAAAABJRU5ErkJggg=="),
            };
            var framePaths = new[] { "frame-a.png", "frame-b.png", "frame-c.png" };
            for (var index = 0; index < framePaths.Length; index++)
            {
                File.WriteAllBytes(Path.Combine(root, framePaths[index]), frameBytes[index]);
            }
            var manifestPath = Path.Combine(root, "frame_manifest.csv");
            File.WriteAllText(
                manifestPath,
                "image_path,timestamp_ms,screen_type,capture_source,width,height,captured_at_utc\n" +
                "frame-a.png,1000,result,fixture,1280,720,2026-07-29T12:00:00+09:00\n" +
                "frame-b.png,2000,result,fixture,1280,720,2026-07-29T12:00:01+09:00\n" +
                "frame-c.png,3000,result,fixture,1280,720,2026-07-29T12:00:02+09:00\n",
                new UTF8Encoding(false));
            var runner = new AppOwnedCaptureSaveWorkflowRunner();

            var result = await runner.RunAsync(
                manifestPath,
                Path.Combine(root, "score.sqlite"),
                "master.sqlite");

            Assert.Equal("completed", result.Status);
            Assert.Equal(1, result.EventCount);
            Assert.Equal(1, result.StatusCounts["unresolved"]);
            Assert.Single(result.EventResults!);
            Assert.False(File.Exists(Path.Combine(root, "score.sqlite")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Invalid_manifest_is_reported_as_workflow_failure_without_process_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-capture-save-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "frame_manifest.csv");
            File.WriteAllText(path, "timestamp_ms\n1000\n", new UTF8Encoding(false));

            var result = await new AppOwnedCaptureSaveWorkflowRunner().RunAsync(
                path,
                Path.Combine(root, "score.sqlite"),
                "master.sqlite");

            Assert.Equal("workflow_failed", result.Status);
            Assert.NotEmpty(result.Reasons);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static CapturedFrame Frame(
        long timestampMs,
        DateTimeOffset? capturedAtUtc = null) =>
        new(
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPgEpH7DwABpAE8k4sOtwAAAABJRU5ErkJggg=="),
            1280,
            720,
            timestampMs,
            capturedAtUtc ?? DateTimeOffset.Parse("2026-07-29T12:00:00+09:00"),
            "fixture");

    private static LiveResultObservation FormalObservation(
        int score,
        string rank = "AAA",
        string digitStatus = "recognized",
        string? confirmedEventId = null) =>
        new(
            true,
            score.ToString(),
            "event-1",
            "formal-result",
            DigitRecognitionStatus: digitStatus,
            FormalEvidence: new AppOwnedFormalEvidence(
                "master-v1",
                "song-1",
                "chart-1",
                score,
                456,
                400,
                40,
                10,
                4,
                2,
                1750,
                rank,
                "CLEAR",
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["master_version"] = "master_metadata",
                    ["song_id"] = "result_identity_visual_evidence",
                    ["chart_id"] = "result_identity_visual_evidence",
                    ["score"] = "result_numeric_visual_evidence",
                    ["max_combo"] = "result_numeric_visual_evidence",
                    ["marvelous"] = "result_numeric_visual_evidence",
                    ["perfect"] = "result_numeric_visual_evidence",
                    ["great"] = "result_numeric_visual_evidence",
                    ["good"] = "result_numeric_visual_evidence",
                    ["miss"] = "result_numeric_visual_evidence",
                    ["ex_score"] = "result_numeric_visual_evidence",
                    ["ok"] = "result_numeric_visual_evidence",
                    ["rank"] = "result_rank_visual_evidence",
                    ["clear_type"] = "result_clear_type_visual_evidence",
                },
                new Dictionary<string, double?>(StringComparer.Ordinal)
                {
                    ["master_version"] = 0.99,
                    ["song_id"] = 0.99,
                    ["chart_id"] = 0.99,
                    ["score"] = 0.99,
                    ["max_combo"] = 0.99,
                    ["marvelous"] = 0.99,
                    ["perfect"] = 0.99,
                    ["great"] = 0.99,
                    ["good"] = 0.99,
                    ["miss"] = 0.99,
                    ["ex_score"] = 0.99,
                    ["ok"] = 0.99,
                    ["rank"] = 0.99,
                    ["clear_type"] = 0.99,
                },
                Ok: 0),
            ConfirmedEventId: confirmedEventId);

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

    private static IReadOnlyDictionary<string, double?> WithConfidence(
        IReadOnlyDictionary<string, double?> source,
        string fieldName,
        double value)
    {
        var result = new Dictionary<string, double?>(source, StringComparer.Ordinal)
        {
            [fieldName] = value,
        };
        return result;
    }

    private static IReadOnlyDictionary<string, string> WithSource(
        IReadOnlyDictionary<string, string> source,
        string fieldName,
        string value)
    {
        var result = new Dictionary<string, string>(source, StringComparer.Ordinal)
        {
            [fieldName] = value,
        };
        return result;
    }
}
