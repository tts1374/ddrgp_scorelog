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
                ["max_combo"] = 0.99,
                ["marvelous"] = 0.99,
                ["perfect"] = 0.99,
                ["great"] = 0.99,
                ["good"] = 0.99,
                ["miss"] = 0.99,
                ["ex_score"] = 0.99,
                ["score"] = 0.99,
                ["ok"] = 0.99,
                ["rank"] = 0.99,
                ["clear_type"] = 0.99,
            },
            Ok: 0);
        var observation = new LiveResultObservation(
            true,
            "987650",
            "event-1",
            "formal-result",
            DigitRecognitionStatus: "recognized",
            FormalEvidence: evidence);

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
    }

    private static CapturedFrame BuildFrame()
    {
        const int width = 1280;
        const int height = 720;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        Fill(pixels, stride, 532, 54, 216, 216, 255, 0, 0);
        Fill(pixels, stride, 360, 56, 100, 24, 0, 128, 255);
        Fill(pixels, stride, 378, 80, 84, 24, 128, 255, 0);
        DrawTemplate(pixels, stride, 386, 107, "1.pbm");
        DrawTemplate(pixels, stride, 405, 107, "7.pbm");
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
        string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "RuntimeAssets",
            "digit_templates",
            "score_digits",
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
