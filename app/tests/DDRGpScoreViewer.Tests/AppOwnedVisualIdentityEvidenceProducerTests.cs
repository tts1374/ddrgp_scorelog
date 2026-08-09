using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Runtime;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class AppOwnedVisualIdentityEvidenceProducerTests
{
    [Fact]
    public async Task Compatible_jacket_reference_and_chart_context_are_adopted_without_ocr()
    {
        using var database = new DatabaseFixture();
        database.AddJacketReference(
            "song-1",
            Enumerable.Repeat(0.0, 16 * 16 * 3)
                .Select((value, index) => index % 3 == 0 ? 1.0 : value)
                .ToArray(),
            Enumerable.Range(0, 24).Select(index => index == 7 ? 1.0 : 0.0).ToArray(),
            new double[64]);
        database.ExecuteCatalogSql(
            "UPDATE jacket_references " +
            "SET master_version = 'previous-master' " +
            "WHERE song_id = 'song-1';");

        var frame = BuildFrame();
        var observation = CreateObservation();

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            frame,
            observation,
            database.MasterPath,
            database.CatalogPath);

        Assert.NotNull(enriched.FormalEvidence);
        Assert.Equal("master-v1", enriched.FormalEvidence!.MasterVersion);
        Assert.Equal("song-1", enriched.FormalEvidence.SongId);
        Assert.Equal("chart-1", enriched.FormalEvidence.ChartId);
        Assert.Equal(
            FormalEvidenceSourceNames.ResultIdentityVisualEvidence,
            enriched.FormalEvidence.Sources["song_id"]);
        Assert.True(enriched.FormalEvidence.Confidences["chart_id"] >= 0.98);
        Assert.NotNull(enriched.LevelRecognition);
        Assert.Equal("recognized", enriched.LevelRecognition!.Status);
        Assert.Equal("17", enriched.LevelRecognition.RecognizedDigits);
        Assert.Equal("17", enriched.LevelRecognition.BestCandidate);
        Assert.Equal(0.28, enriched.LevelRecognition.DistanceThreshold);
        Assert.Equal(0.02, enriched.LevelRecognition.CandidateMarginThreshold);
        Assert.DoesNotContain(
            enriched.FormalEvidence.RecognitionReasons ?? Array.Empty<string>(),
            reason => reason.Contains("ocr", StringComparison.OrdinalIgnoreCase));

        var saved = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
            frame,
            observation,
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("completed", saved.Status);
        Assert.Equal(1, saved.StatusCounts["saved"]);
        Assert.Single(saved.SavedPlayIds);
        Assert.Equal("17", Assert.Single(saved.EventResults!).LevelRecognition!.RecognizedDigits);
    }

    [Fact]
    public async Task Ambiguous_level_keeps_diagnostics_and_stays_out_of_formal_db()
    {
        using var database = new DatabaseFixture();
        AddCompatibleJacketReference(database, "song-1");
        var templateRoot = Path.Combine(
            Path.GetTempPath(),
            $"ddrgp-level-ambiguous-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(templateRoot, "chart_level"));
        try
        {
            var packagedRoot = new AppRuntimeResourceResolver()
                .ResolveDigitTemplatesDirectory();
            foreach (var label in Enumerable.Range(0, 10).Select(value => value.ToString()))
            {
                File.Copy(
                    Path.Combine(packagedRoot, "chart_level", $"{label}.pbm"),
                    Path.Combine(templateRoot, "chart_level", $"{label}.pbm"));
            }
            File.Copy(
                Path.Combine(templateRoot, "chart_level", "0.pbm"),
                Path.Combine(templateRoot, "chart_level", "1.pbm"),
                overwrite: true);

            var producer = new AppOwnedVisualIdentityEvidenceProducer(
                new M7aDigitRecognizer(templateRoot: templateRoot));
            var enriched = producer.Enrich(
                BuildFrame(),
                CreateObservation(),
                database.MasterPath,
                database.CatalogPath);

            Assert.NotNull(enriched.LevelRecognition);
            Assert.Equal("ambiguous", enriched.LevelRecognition!.Status);
            Assert.True(
                enriched.LevelRecognition.FailureReason is
                    "low_margin" or "distance_above_threshold");
            Assert.True(
                enriched.LevelRecognition.CandidateMargin <=
                enriched.LevelRecognition.CandidateMarginThreshold);
            Assert.Equal(
                enriched.LevelRecognition.RecognizedDigits,
                enriched.LevelRecognition.BestCandidate);
            Assert.NotEmpty(enriched.LevelRecognition.NextBestCandidate);
            Assert.Contains(
                "formal_evidence.level_visual_ambiguous",
                enriched.FormalEvidence!.RecognitionReasons!);

            var result = await new AppOwnedCaptureSaveWorkflowRunner()
                .RunPreparedCandidateAsync(
                    frame: BuildFrame(),
                    observation: enriched,
                    scoreDatabasePath: database.ScorePath);

            Assert.Equal("completed", result.Status);
            Assert.Equal(1, result.StatusCounts["unresolved"]);
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = database.ScorePath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM plays;";
            Assert.Equal(0L, command.ExecuteScalar());
            Assert.Equal(
                "ambiguous",
                Assert.Single(result.EventResults!).LevelRecognition!.Status);
        }
        finally
        {
            if (Directory.Exists(templateRoot))
            {
                Directory.Delete(templateRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Ambiguous_jacket_is_resolved_by_title_feature_and_saved_once()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "title", 0, "MAX 300", "Artist");
        database.AddResultTextFeature("song-2", "title", 255, "TITLE TWO", "Artist Two");

        var observation = CreateObservation();
        var frame = BuildFrame(titleValue: 0);
        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            frame,
            observation,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("song-1", enriched.FormalEvidence!.SongId);
        Assert.Equal("chart-1", enriched.FormalEvidence.ChartId);

        var saved = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
            frame,
            observation,
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("completed", saved.Status);
        Assert.Equal(1, saved.StatusCounts["saved"]);
        Assert.Single(saved.SavedPlayIds);
    }

    [Fact]
    public void Title_feature_unavailable_falls_back_to_artist_feature()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "artist", 0, "MAX 300", "Artist");
        database.AddResultTextFeature("song-2", "artist", 255, "TITLE TWO", "Artist Two");

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(artistValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("song-1", enriched.FormalEvidence!.SongId);
        Assert.Equal("chart-1", enriched.FormalEvidence.ChartId);
    }

    [Fact]
    public void Ambiguous_title_feature_falls_back_to_artist_feature()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "title", 0, "MAX 300", "Artist");
        database.AddResultTextFeature("song-2", "title", 0, "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "artist", 0, "MAX 300", "Artist");
        database.AddResultTextFeature("song-2", "artist", 255, "TITLE TWO", "Artist Two");

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 0, artistValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("song-1", enriched.FormalEvidence!.SongId);
        Assert.Equal("chart-1", enriched.FormalEvidence.ChartId);
    }

    [Fact]
    public void Ambiguous_title_feature_uses_linehash_as_title_tiebreaker()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist", "chart-2");
        AddCompatibleJacketReference(database, "song-1", "MAX 300", "Artist");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist");
        database.AddResultTextFeature(
            "song-1",
            "title",
            0,
            "MAX 300",
            "Artist",
            linehashRows: Enumerable.Repeat(new string('0', 76), 28).ToArray());
        database.AddResultTextFeature(
            "song-2",
            "title",
            0,
            "TITLE TWO",
            "Artist",
            linehashRows: Enumerable.Repeat(new string('f', 76), 28).ToArray());

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("song-1", enriched.FormalEvidence!.SongId);
        Assert.Equal("chart-1", enriched.FormalEvidence.ChartId);
    }

    [Fact]
    public void Resolved_title_feature_is_not_overridden_by_linehash()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist", "chart-2");
        AddCompatibleJacketReference(database, "song-1", "MAX 300", "Artist");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist");
        database.AddResultTextFeature(
            "song-1",
            "title",
            0,
            "MAX 300",
            "Artist",
            linehashRows: Enumerable.Repeat(new string('f', 76), 28).ToArray());
        database.AddResultTextFeature(
            "song-2",
            "title",
            255,
            "TITLE TWO",
            "Artist",
            linehashRows: Enumerable.Repeat(new string('0', 76), 28).ToArray());

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("song-1", enriched.FormalEvidence!.SongId);
        Assert.Equal("chart-1", enriched.FormalEvidence.ChartId);
    }

    [Fact]
    public void Linehash_only_reorders_normally_ambiguous_candidates()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist", "chart-2");
        database.AddMasterSongAndChart("song-3", "TITLE THREE", "Artist", "chart-3");
        AddCompatibleJacketReference(database, "song-1", "MAX 300", "Artist");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist");
        AddCompatibleJacketReference(database, "song-3", "TITLE THREE", "Artist");
        database.AddResultTextFeature(
            "song-1",
            "title",
            0,
            "MAX 300",
            "Artist",
            linehashRows: Enumerable.Repeat(new string('f', 76), 28).ToArray());
        database.AddResultTextFeature(
            "song-2",
            "title",
            1,
            "TITLE TWO",
            "Artist",
            linehashRows: Enumerable.Repeat(new string('f', 76), 28).ToArray());
        database.AddResultTextFeature(
            "song-3",
            "title",
            128,
            "TITLE THREE",
            "Artist",
            linehashRows: Enumerable.Repeat(new string('0', 76), 28).ToArray());

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Null(enriched.FormalEvidence!.SongId);
        Assert.Null(enriched.FormalEvidence.ChartId);
        Assert.DoesNotContain(
            enriched.FormalEvidence.RecognitionReasons!,
            reason => reason.Contains("song-3", StringComparison.Ordinal));
    }

    [Fact]
    public void Ambiguous_title_and_artist_features_remain_unresolved()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "title", 0, "MAX 300", "Artist");
        database.AddResultTextFeature("song-2", "title", 0, "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "artist", 0, "MAX 300", "Artist");
        database.AddResultTextFeature("song-2", "artist", 0, "TITLE TWO", "Artist Two");

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 0, artistValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Null(enriched.FormalEvidence!.SongId);
        Assert.Null(enriched.FormalEvidence.ChartId);
        Assert.Contains(
            enriched.FormalEvidence.RecognitionReasons!,
            reason => reason.Contains("title", StringComparison.Ordinal));
        Assert.Contains(
            enriched.FormalEvidence.RecognitionReasons!,
            reason => reason.Contains("artist", StringComparison.Ordinal));
    }

    [Fact]
    public void Text_feature_candidate_outside_jacket_ambiguity_is_rejected()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        database.AddMasterSongAndChart("song-3", "TITLE THREE", "Artist Three", "chart-3");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-3", "title", 0, "TITLE THREE", "Artist Three");

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Null(enriched.FormalEvidence!.SongId);
        Assert.Null(enriched.FormalEvidence.ChartId);
        Assert.DoesNotContain(
            enriched.FormalEvidence.RecognitionReasons!,
            reason => reason.Contains("song-3", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_or_drifted_text_features_are_not_used()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "title", 0, "DRIFTED TITLE", "Artist");
        database.ExecuteCatalogSql(
            "INSERT INTO result_text_features (" +
            "feature_id, song_id, field_name, feature_version, roi_version, feature_hash, " +
            "payload_json, source_label, master_version, canonical_title_snapshot, " +
            "canonical_artist_snapshot, created_at) VALUES (" +
            "'invalid-feature', 'song-2', 'title', 'old-version', 'old-roi', 'bad-hash', " +
            "'{}', 'fixture', 'master-v1', 'TITLE TWO', 'Artist Two', " +
            "'2026-07-30T00:00:00+00:00');");

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Null(enriched.FormalEvidence!.SongId);
        Assert.Null(enriched.FormalEvidence.ChartId);
    }

    [Fact]
    public void Nested_result_text_feature_shape_is_ignored()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "title", 0, "MAX 300", "Artist");
        database.AddResultTextFeature(
            "song-2",
            "title",
            255,
            "TITLE TWO",
            "Artist Two",
            nestedVectors: true);

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 255),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Null(enriched.FormalEvidence!.SongId);
        Assert.Null(enriched.FormalEvidence.ChartId);
    }

    [Fact]
    public void Missing_text_feature_for_one_ambiguous_candidate_is_rejected()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "title", 0, "MAX 300", "Artist");

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(titleValue: 0),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Null(enriched.FormalEvidence!.SongId);
        Assert.Null(enriched.FormalEvidence.ChartId);
    }

    [Fact]
    public async Task Distant_text_feature_match_is_unresolved_and_not_saved()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        AddCompatibleJacketReference(database, "song-1");
        AddCompatibleJacketReference(database, "song-2", "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "title", 0, "MAX 300", "Artist");
        database.AddResultTextFeature("song-2", "title", 32, "TITLE TWO", "Artist Two");

        var frame = BuildFrame(titleValue: 255);
        var observation = CreateObservation();
        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            frame,
            observation,
            database.MasterPath,
            database.CatalogPath);

        Assert.Null(enriched.FormalEvidence!.SongId);
        Assert.Contains(
            enriched.FormalEvidence.RecognitionReasons!,
            reason => reason.Contains("confidence_insufficient", StringComparison.Ordinal));

        var saved = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
            frame,
            observation,
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("completed", saved.Status);
        Assert.DoesNotContain("saved", saved.StatusCounts.Keys);
        Assert.Equal(1, saved.StatusCounts["unresolved"]);
        Assert.Empty(saved.SavedPlayIds);
    }

    [Fact]
    public void Unique_jacket_does_not_require_result_text_feature_rows()
    {
        using var database = new DatabaseFixture();
        database.AddJacketReference(
            "song-1",
            Enumerable.Repeat(0.0, 16 * 16 * 3)
                .Select((value, index) => index % 3 == 0 ? 1.0 : value)
                .ToArray(),
            Enumerable.Range(0, 24).Select(index => index == 7 ? 1.0 : 0.0).ToArray(),
            new double[64]);
        database.ExecuteCatalogSql(
            "INSERT INTO result_text_features (" +
            "feature_id, song_id, field_name, feature_version, roi_version, feature_hash, " +
            "payload_json, source_label, master_version, canonical_title_snapshot, " +
            "canonical_artist_snapshot, created_at) VALUES (" +
            "'invalid-feature', 'song-1', 'title', 'old-version', 'old-roi', 'bad-hash', " +
            "'{}', 'fixture', 'master-v1', 'MAX 300', 'Artist', " +
            "'2026-07-30T00:00:00+00:00');");

        var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
            BuildFrame(),
            CreateObservation(),
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("song-1", enriched.FormalEvidence!.SongId);
        Assert.Equal("chart-1", enriched.FormalEvidence.ChartId);
    }

    [Fact]
    public async Task Resolved_text_identity_with_low_formal_confidence_is_not_saved()
    {
        using var database = new DatabaseFixture();
        database.AddMasterSongAndChart("song-2", "TITLE TWO", "Artist Two", "chart-2");
        var jacket = new double[16 * 16 * 3];
        database.AddJacketReference("song-1", jacket, new double[24], new double[64]);
        database.AddJacketReference("song-2", jacket, new double[24], new double[64], "TITLE TWO", "Artist Two");
        database.AddResultTextFeature("song-1", "title", 0, "MAX 300", "Artist");
        database.AddResultTextFeature("song-2", "title", 255, "TITLE TWO", "Artist Two");

        var saved = await new AppOwnedCaptureSaveWorkflowRunner().RunCandidateAsync(
            BuildFrame(titleValue: 0),
            CreateObservation(confidence: 0.97),
            database.ScorePath,
            database.MasterPath,
            database.CatalogPath);

        Assert.Equal("completed", saved.Status);
        Assert.DoesNotContain("saved", saved.StatusCounts.Keys);
        Assert.Equal(1, saved.StatusCounts["unresolved"]);
        Assert.Empty(saved.SavedPlayIds);
    }

    [Fact]
    public void Osaka_evolved_type_titles_are_separated_by_title_feature()
    {
        using var database = new DatabaseFixture();
        var songs = new[]
        {
            ("song-osaka-1", "OSAKA EVOLVED TYPE1", "Artist 1", "chart-osaka-1", (byte)0),
            ("song-osaka-2", "OSAKA EVOLVED TYPE2", "Artist 2", "chart-osaka-2", (byte)128),
            ("song-osaka-3", "OSAKA EVOLVED TYPE3", "Artist 3", "chart-osaka-3", (byte)255),
        };
        foreach (var song in songs)
        {
            database.AddMasterSongAndChart(song.Item1, song.Item2, song.Item3, song.Item4);
            AddCompatibleJacketReference(database, song.Item1, song.Item2, song.Item3);
            database.AddResultTextFeature(song.Item1, "title", song.Item5, song.Item2, song.Item3);
        }

        foreach (var song in songs)
        {
            var enriched = new AppOwnedVisualIdentityEvidenceProducer().Enrich(
                BuildFrame(titleValue: song.Item5),
                CreateObservation(),
                database.MasterPath,
                database.CatalogPath);

            Assert.Equal(song.Item1, enriched.FormalEvidence!.SongId);
            Assert.Equal(song.Item4, enriched.FormalEvidence.ChartId);
        }
    }

    private static LiveResultObservation CreateObservation(double confidence = 0.99)
    {
        var evidence = new AppOwnedFormalEvidence(
            null,
            null,
            null,
            987650,
            456,
            400,
            40,
            10,
            4,
            2,
            1750,
            "AAA",
            "CLEAR",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["max_combo"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["marvelous"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["perfect"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["great"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["good"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["miss"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["ex_score"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["score"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["ok"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                ["rank"] = FormalEvidenceSourceNames.ResultRankVisualEvidence,
                ["clear_type"] = FormalEvidenceSourceNames.ResultClearTypeVisualEvidence,
            },
            new Dictionary<string, double?>(StringComparer.Ordinal)
            {
                ["max_combo"] = confidence,
                ["marvelous"] = confidence,
                ["perfect"] = confidence,
                ["great"] = confidence,
                ["good"] = confidence,
                ["miss"] = confidence,
                ["ex_score"] = confidence,
                ["score"] = confidence,
                ["ok"] = confidence,
                ["rank"] = confidence,
                ["clear_type"] = confidence,
            },
            Ok: 0);
        return new LiveResultObservation(
            true,
            "987650",
            "event-1",
            "formal-result",
            DigitRecognitionStatus: "recognized",
            FormalEvidence: evidence);
    }

    private static void AddCompatibleJacketReference(
        DatabaseFixture database,
        string songId,
        string title = "MAX 300",
        string artist = "Artist")
    {
        database.AddJacketReference(
            songId,
            Enumerable.Repeat(0.0, 16 * 16 * 3)
                .Select((value, index) => index % 3 == 0 ? 1.0 : value)
                .ToArray(),
            Enumerable.Range(0, 24)
                .Select(index => index == 7 ? 1.0 : 0.0)
                .ToArray(),
            new double[64],
            title,
            artist);
    }

    private static CapturedFrame BuildFrame(byte titleValue = 0, byte artistValue = 0)
    {
        const int width = 1280;
        const int height = 720;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        Fill(pixels, stride, 532, 54, 216, 216, 255, 0, 0);
        Fill(pixels, stride, 360, 56, 100, 24, 0, 128, 255);
        Fill(pixels, stride, 378, 80, 84, 24, 0, 255, 34);
        Fill(pixels, stride, 488, 274, 304, 32, titleValue, titleValue, titleValue);
        Fill(pixels, stride, 548, 306, 184, 26, artistValue, artistValue, artistValue);
        DrawTemplate(pixels, stride, 394, 105, "chart_level", "1.pbm");
        DrawTemplate(pixels, stride, 413, 105, "chart_level", "7.pbm");
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return new CapturedFrame(
            EncodePng(bitmap),
            width,
            height,
            1_000,
            DateTimeOffset.Parse("2026-07-30T12:00:00+09:00"),
            "fixture");
    }

    private static void Fill(
        byte[] pixels,
        int stride,
        int left,
        int top,
        int width,
        int height,
        byte red,
        byte green,
        byte blue)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                SetPixel(pixels, stride, x, y, red, green, blue);
            }
        }
    }

    private static void DrawTemplate(
        byte[] pixels,
        int stride,
        int left,
        int top,
        string group,
        string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "RuntimeAssets",
            "digit_templates",
            group,
            fileName);
        var tokens = File.ReadAllText(path, Encoding.UTF8)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var width = int.Parse(tokens[1]);
        var height = int.Parse(tokens[2]);
        var pixelTokens = tokens
            .Skip(3)
            .SelectMany(token => token.Select(character => character.ToString()))
            .ToArray();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixelTokens[y * width + x] == "1")
                {
                    SetPixel(pixels, stride, left + x, top + y, 255, 255, 255);
                }
            }
        }
    }

    private static void SetPixel(
        byte[] pixels,
        int stride,
        int x,
        int y,
        byte red,
        byte green,
        byte blue)
    {
        var offset = y * stride + x * 4;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = 255;
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
