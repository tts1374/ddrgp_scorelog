using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using Xunit;
using Xunit.Abstractions;

namespace DDRGpScoreViewer.Tests;

public sealed class LocalResultFactAttribute : FactAttribute
{
    public LocalResultFactAttribute(params string[] additionalResultNumbers)
    {
        if (ResultScreenLayoutTests.RepositoryRoot is null)
            Skip = "Local Issue 216 screenshots and reference databases are unavailable.";
        else if (additionalResultNumbers.Any(number => Directory.GetFiles(
            Path.Combine(ResultScreenLayoutTests.RepositoryRoot, "samples/screenshots/organized/result"),
            $"result_{number}_*.png").Length != 1))
            Skip = "Local FLARE badge screenshots are unavailable.";
    }
}

public sealed class ResultScreenLayoutTests(ITestOutputHelper output)
{
    internal static string? RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null; directory = directory.Parent)
            {
                var root = directory.FullName;
                if (File.Exists(Path.Combine(root, "samples/screenshots/metadata.csv")) &&
                    File.Exists(Path.Combine(root, "databases/ddrgp-master.sqlite")) &&
                    File.Exists(Path.Combine(root, "databases/jacket-catalog-release.sqlite")) &&
                    Directory.Exists(Path.Combine(root, "samples/screenshots/organized/result")) &&
                    new[] { "373", "375", "376", "377", "378", "038", "030", "078", "051", "053" }
                        .All(number => Directory.GetFiles(
                            Path.Combine(root, "samples/screenshots/organized/result"),
                            $"result_{number}_*.png").Length == 1) &&
                    Directory.Exists(Path.Combine(root, "samples/screenshots/organized/transition")) &&
                    new[] { "372", "374" }.All(number => Directory.GetFiles(
                        Path.Combine(root, "samples/screenshots/organized/transition"),
                        $"transition_countup_{number}_*.png").Length == 1))
                    return root;
            }
            return null;
        }
    }

    [Fact]
    public async Task Both_or_neither_detail_panels_fail_closed()
    {
        var pixels = new byte[1280 * 720 * 4];
        using var database = new DatabaseFixture();
        foreach (var both in new[] { false, true })
        {
            if (both)
            {
                // A second populated panel must not choose either player.
                Paint(pixels, new Int32Rect(480, 0, 320, 58), header: true);
                Paint(pixels, new Int32Rect(164, 330, 462, 288));
                Paint(pixels, new Int32Rect(662, 330, 462, 288));
            }
            var observation = await new AppOwnedLiveResultAnalyzer().AnalyzeAsync(Frame(Encode(pixels)));
            Assert.False(observation.IsResultScreen);
            Assert.Null(observation.FormalEvidence);
            var path = Path.Combine(database.DirectoryPath, "unknown.png");
            File.WriteAllBytes(path, Encode(pixels));
            Assert.Equal(ScreenshotImportItemStatus.InputError,
                (await new AppOwnedScreenshotImportService().ProcessAsync(path, database.ScorePath,
                    database.MasterPath, database.CatalogPath)).Status);
        }
        Assert.Equal(0L, PlayCount(database.ScorePath));
    }

    [LocalResultFact]
    public async Task Paired_local_results_use_image_evidence_and_import_once()
    {
        (string Second, string First, int Score, string Rank, string Clear, int Ok, double Calories)[] pairs =
        {
            ("373", "038", 999910, "AAA", "PFC", 0, 9.5),
            ("375", "030", 950700, "AA+", "FULL COMBO", 99, 16.0),
            ("376", "078", 919940, "AA", "CLEAR", 26, 28.9),
            ("377", "051", 909400, "AA", "CLEAR", 6, 16.2),
            ("378", "053", 20880, "E", "FAILED", 4, 5.4),
        };
        using var database = LocalDatabase();
        var analyzer = new AppOwnedLiveResultAnalyzer();
        var producer = new AppOwnedVisualIdentityEvidenceProducer();
        var importer = new AppOwnedScreenshotImportService();
        foreach (var pair in pairs)
        {
            var secondPath = Sample("result", pair.Second);
            var secondFrame = Frame(File.ReadAllBytes(secondPath));
            var second = await analyzer.AnalyzeAsync(secondFrame);
            var firstFrame = Frame(File.ReadAllBytes(Sample("result", pair.First)));
            var first = await analyzer.AnalyzeAsync(firstFrame);
            Assert.True(second.IsResultScreen);
            Assert.True(second.IsSecondPlayer);
            Assert.True(first.IsResultScreen);
            Assert.False(first.IsSecondPlayer);
            second = producer.Enrich(secondFrame, second, database.MasterPath, database.CatalogPath);
            first = producer.Enrich(firstFrame, first, database.MasterPath, database.CatalogPath);
            output.WriteLine(JsonSerializer.Serialize(new { Sample = pair.Second, Second = second, First = first }));
            var evidence = Assert.IsType<AppOwnedFormalEvidence>(second.FormalEvidence);
            AssertExpectedNumbers(secondPath, evidence);
            AssertExpectedNumbers(Sample("result", pair.First), first.FormalEvidence!);
            Assert.Equal(pair.Score, evidence.Score);
            Assert.Equal(pair.Rank, evidence.Rank);
            Assert.Equal(pair.Clear, evidence.ClearType);
            Assert.Equal(pair.Ok, evidence.Ok);
            Assert.Equal(pair.Calories, evidence.Calories);
            Assert.Equal(pair.Second == "376" ? "IV" : null, evidence.FlareRank);
            Assert.NotNull(evidence.ChartId);
            Assert.Equal(first.FormalEvidence!.ChartId, evidence.ChartId);
            Assert.Equal(first.FormalEvidence.SongId, evidence.SongId);
            var result = await importer.ProcessAsync(secondPath, database.ScorePath,
                database.MasterPath, database.CatalogPath);
            output.WriteLine(JsonSerializer.Serialize(result));
            Assert.Equal(ScreenshotImportItemStatus.Saved, result.Status);
            Assert.Equal(ScreenshotImportItemStatus.Duplicate,
                (await importer.ProcessAsync(secondPath, database.ScorePath,
                    database.MasterPath, database.CatalogPath)).Status);
            Assert.Equal(ScreenshotImportItemStatus.Saved,
                (await importer.ProcessAsync(Sample("result", pair.First), database.ScorePath,
                    database.MasterPath, database.CatalogPath)).Status);
        }
        Assert.Equal(10L, PlayCount(database.ScorePath));
    }

    [LocalResultFact("295", "292", "289", "286", "283", "279", "276", "268", "267", "269")]
    public async Task Two_player_flare_palette_ignores_background_outside_hexagonal_badge()
    {
        var analyzer = new AppOwnedLiveResultAnalyzer();
        (string Number, string Rank)[] badges =
        {
            ("295", "I"), ("292", "II"), ("289", "III"), ("286", "IV"), ("283", "V"),
            ("279", "VI"), ("276", "VII"), ("268", "VIII"), ("267", "IX"), ("269", "EX"),
        };
        foreach (var badge in badges)
        {
            var source = new FormatConvertedBitmap(Decode(File.ReadAllBytes(Sample("result", badge.Number))),
                PixelFormats.Bgra32, null, 0);
            var badgePixels = new byte[78 * 85 * 4];
            source.CopyPixels(new Int32Rect(404, 163, 78, 85), badgePixels, 78 * 4, 0);
            foreach (var background in new[] { (Red: 140, Green: 0, Blue: 53), (Red: 0, Green: 220, Blue: 255), (Red: 0, Green: 0, Blue: 0) })
            {
                var target = new FormatConvertedBitmap(Decode(File.ReadAllBytes(Sample("result", "376"))),
                    PixelFormats.Bgra32, null, 0);
                var pixels = new byte[1280 * 720 * 4];
                target.CopyPixels(pixels, 1280 * 4, 0);
                for (var y = 0; y < 85; y++)
                {
                    // Render the measured badge vertices (39,1), (75,22), (75,63),
                    // (39,83), (3,63), (3,22) over different animated-background colors.
                    var left = y < 22 ? 39.0 - (y - 1) * 36.0 / 21 :
                        y > 63 ? 3.0 + (y - 63) * 36.0 / 20 : 3.0;
                    for (var x = 0; x < 78; x++)
                    {
                        var offset = ((163 + y) * 1280 + 800 + x) * 4;
                        if (y >= 1 && y <= 83 && x >= left && x <= 78 - left)
                            Buffer.BlockCopy(badgePixels, (y * 78 + x) * 4, pixels, offset, 4);
                        else
                        {
                            pixels[offset] = (byte)background.Blue;
                            pixels[offset + 1] = (byte)background.Green;
                            pixels[offset + 2] = (byte)background.Red;
                            pixels[offset + 3] = 255;
                        }
                    }
                }
                var observation = await analyzer.AnalyzeAsync(Frame(Encode(pixels)));
                Assert.True(observation.IsSecondPlayer);
                Assert.Equal(badge.Rank, observation.FormalEvidence!.FlareRank);
                Assert.Equal(FormalEvidenceSourceNames.ResultFlareRankVisualEvidence,
                    observation.FormalEvidence.Sources!["flare_rank"]);
            }
        }
    }

    [LocalResultFact]
    public async Task Countups_and_missing_regions_never_save_a_play()
    {
        using var database = LocalDatabase();
        var analyzer = new AppOwnedLiveResultAnalyzer();
        var runner = new AppOwnedCaptureSaveWorkflowRunner();
        var importer = new AppOwnedScreenshotImportService();
        foreach (var number in new[] { "372", "374" })
        {
            var path = Sample("transition", number);
            var frame = Frame(File.ReadAllBytes(path));
            var observation = await analyzer.AnalyzeAsync(frame);
            Assert.True(observation.IsSecondPlayer);
            var result = await runner.RunCandidateAsync(frame, observation, database.ScorePath,
                database.MasterPath, database.CatalogPath);
            output.WriteLine(JsonSerializer.Serialize(result));
            Assert.Empty(result.SavedPlayIds);
            Assert.Equal(ScreenshotImportItemStatus.RecognitionFailed,
                (await importer.ProcessAsync(path, database.ScorePath,
                    database.MasterPath, database.CatalogPath)).Status);
        }

        var bitmap = Decode(File.ReadAllBytes(Sample("result", "373")));
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[1280 * 720 * 4];
        converted.CopyPixels(pixels, 1280 * 4, 0);
        for (var y = 404; y < 425; y++)
            Array.Clear(pixels, (y * 1280 + 398) * 4, 92 * 4);
        var incompletePath = Path.Combine(database.DirectoryPath, "incomplete.png");
        File.WriteAllBytes(incompletePath, Encode(pixels));
        Assert.Equal(ScreenshotImportItemStatus.RecognitionFailed,
            (await importer.ProcessAsync(incompletePath, database.ScorePath,
                database.MasterPath, database.CatalogPath)).Status);
        Assert.Equal(0L, PlayCount(database.ScorePath));
    }

    [LocalResultFact]
    public async Task Two_player_optional_regions_can_be_missing()
    {
        using var database = LocalDatabase();
        var bitmap = new FormatConvertedBitmap(Decode(File.ReadAllBytes(Sample("result", "376"))),
            PixelFormats.Bgra32, null, 0);
        var pixels = new byte[1280 * 720 * 4];
        bitmap.CopyPixels(pixels, 1280 * 4, 0);
        foreach (var roi in new[] { new Int32Rect(800, 163, 78, 85), new Int32Rect(903, 380, 76, 32) })
            for (var y = roi.Y; y < roi.Y + roi.Height; y++)
                Array.Clear(pixels, (y * 1280 + roi.X) * 4, roi.Width * 4);
        var bytes = Encode(pixels);
        var observation = await new AppOwnedLiveResultAnalyzer().AnalyzeAsync(Frame(bytes));
        Assert.Null(observation.FormalEvidence!.FlareRank);
        Assert.Null(observation.FormalEvidence.Calories);
        var path = Path.Combine(database.DirectoryPath, "optional-missing.png");
        File.WriteAllBytes(path, bytes);
        Assert.Equal(ScreenshotImportItemStatus.Saved,
            (await new AppOwnedScreenshotImportService().ProcessAsync(path, database.ScorePath,
                database.MasterPath, database.CatalogPath)).Status);
        Assert.Equal(1L, PlayCount(database.ScorePath));
    }

    private static void AssertExpectedNumbers(string path, AppOwnedFormalEvidence evidence)
    {
        using var parser = new TextFieldParser(Path.Combine(RepositoryRoot!, "samples/screenshots/metadata.csv"));
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()!;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()!;
            var row = headers.Zip(fields).ToDictionary(pair => pair.First, pair => pair.Second);
            if (Path.GetFileName(row["organized_file"]) != Path.GetFileName(path)) continue;
            foreach (var pair in new Dictionary<string, int?>
            {
                ["expected_score"] = evidence.Score,
                ["max_combo"] = evidence.MaxCombo,
                ["marvelous"] = evidence.Marvelous,
                ["perfect"] = evidence.Perfect,
                ["great"] = evidence.Great,
                ["good"] = evidence.Good,
                ["miss"] = evidence.Miss,
                ["ex_score"] = evidence.ExScore,
            }) Assert.Equal(int.Parse(row[pair.Key]), pair.Value);
            Assert.Equal(row["expected_rank"], evidence.Rank);
            return;
        }
        Assert.Fail($"Expected values missing for {Path.GetFileName(path)}");
    }

    internal static string Sample(string kind, string number) => Directory.GetFiles(
        Path.Combine(RepositoryRoot!, "samples/screenshots/organized", kind),
        kind == "transition" ? $"transition_countup_{number}_*.png" : $"result_{number}_*.png").Single();

    internal static DatabaseFixture LocalDatabase()
    {
        var database = new DatabaseFixture();
        File.Copy(Path.Combine(RepositoryRoot!, "databases/ddrgp-master.sqlite"), database.MasterPath, true);
        File.Copy(Path.Combine(RepositoryRoot!, "databases/jacket-catalog-release.sqlite"), database.CatalogPath, true);
        return database;
    }

    internal static CapturedFrame Frame(byte[] png, long timestamp = 1000) => new(
        png, 1280, 720, timestamp, DateTimeOffset.UtcNow, "local-result-test");

    internal static long PlayCount(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM plays";
        return (long)command.ExecuteScalar()!;
    }

    internal static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png, false);
        return new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
    }

    internal static byte[] Encode(byte[] pixels)
    {
        var bitmap = BitmapSource.Create(1280, 720, 96, 96, PixelFormats.Bgra32, null, pixels, 1280 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void Paint(byte[] pixels, Int32Rect roi, bool header = false)
    {
        for (var y = roi.Y; y < roi.Y + roi.Height; y++)
            for (var x = roi.X; x < roi.X + roi.Width; x++)
            {
                var offset = (y * 1280 + x) * 4;
                var border = x < roi.X + 11 || x >= roi.X + roi.Width - 11 ||
                    y < roi.Y + 11 || y >= roi.Y + roi.Height - 11;
                var bright = (x + y) % 2 == 0;
                pixels[offset] = pixels[offset + 1] = (byte)(header ? bright ? 255 : 0 : border ? 255 : bright ? 200 : 40);
                pixels[offset + 2] = (byte)(header ? bright ? 255 : 0 : border ? 0 : bright ? 200 : 40);
                pixels[offset + 3] = 255;
            }
    }
}
