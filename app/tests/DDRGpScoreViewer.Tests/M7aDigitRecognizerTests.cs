using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Runtime;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class M7aDigitRecognizerTests
{
    [Fact]
    public void Recognizes_zero_as_a_candidate_and_keeps_score_variable_length()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates("score_digits");
        var recognizer = new M7aDigitRecognizer(templateRoot: fixture.Root);

        var zero = recognizer.Recognize(
            fixture.Render(new Dictionary<string, string> { ["score"] = "0" }),
            new Dictionary<string, string> { ["score"] = "0" })["score"];
        var variable = recognizer.Recognize(
            fixture.Render(new Dictionary<string, string> { ["score"] = "1234567" }),
            new Dictionary<string, string> { ["score"] = "1234567" })["score"];

        Assert.Equal("recognized", zero.Status);
        Assert.Equal("0", zero.RecognizedDigits);
        Assert.True(zero.Match);
        Assert.True(zero.HasCandidateDigits);
        Assert.Equal("recognized", variable.Status);
        Assert.Equal("1234567", variable.RecognizedDigits);
    }

    [Fact]
    public void Uses_roi_specific_shared_and_ex_score_fallback_template_groups()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates(
            "score_digits",
            "max_combo",
            "marvelous",
            "perfect",
            "miss",
            "judgment_counts",
            "combo_ex_score");
        var values = new Dictionary<string, string>
        {
            ["score"] = "0",
            ["max_combo"] = "1234",
            ["marvelous"] = "1234",
            ["perfect"] = "1234",
            ["great"] = "1234",
            ["good"] = "1234",
            ["miss"] = "1234",
            ["ex_score"] = "1234",
        };

        var results = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(values),
            values);

        foreach (var field in M7aDigitRecognizer.Fields)
        {
            Assert.Equal("recognized", results[field].Status);
            Assert.Equal(values[field], results[field].RecognizedDigits);
            Assert.True(results[field].TemplateCount >= 10);
        }
        Assert.Equal("great", results["great"].RoiName);
        Assert.Equal("ex_score", results["ex_score"].RoiName);
    }

    [Fact]
    public void Missing_template_label_is_reported_without_candidate_digits()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates("score_digits", labels: "012345678");

        var result = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(new Dictionary<string, string> { ["score"] = "0" }))[
                "score"];

        Assert.Equal("missing_reference", result.Status);
        Assert.Contains("missing_digit_templates=9", result.FailureReason, StringComparison.Ordinal);
        Assert.False(result.HasCandidateDigits);
    }

    [Fact]
    public void Equal_nearest_templates_are_ambiguous()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates("score_digits", "0123456789", duplicateLabel: '1');

        var result = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(new Dictionary<string, string> { ["score"] = "0" }))[
                "score"];

        Assert.Equal("ambiguous", result.Status);
        Assert.Equal("low_margin", result.FailureReason);
        Assert.Equal("0", result.RecognizedDigits);
    }

    [Fact]
    public void Blank_roi_is_a_segmentation_failure()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates("score_digits");

        var result = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(new Dictionary<string, string>()))["score"];

        Assert.Equal("failed_segmentation", result.Status);
        Assert.Equal("no_digit_segments", result.FailureReason);
    }

    [Fact]
    public void Candidate_digits_remain_available_when_expected_value_is_not_evaluated()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates("score_digits");

        var result = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(new Dictionary<string, string> { ["score"] = "0" }),
            new Dictionary<string, string> { ["score"] = string.Empty })["score"];

        Assert.Equal("not_evaluated", result.Status);
        Assert.Equal("no_expected_value", result.FailureReason);
        Assert.Equal("0", result.RecognizedDigits);
        Assert.True(result.HasCandidateDigits);
        Assert.Null(result.Match);
    }

    [Fact]
    public void Resolves_templates_from_explicit_runtime_data_path()
    {
        using var fixture = new TemplateFixture();
        var dataRoot = Path.Combine(fixture.Root, "explicit-data");
        fixture.WriteTemplateDirectory(Path.Combine(dataRoot, "digit_templates", "score_digits"));
        var resolver = new AppRuntimeResourceResolver(
            packageRoot: Path.Combine(fixture.Root, "package"),
            explicitDataRoot: dataRoot);

        var result = new M7aDigitRecognizer(resourceResolver: resolver).Recognize(
            fixture.Render(new Dictionary<string, string> { ["score"] = "0" }))[
                "score"];

        Assert.Equal("recognized", result.Status);
        Assert.Equal("0", result.RecognizedDigits);
    }

    [Fact]
    public void Normal_runtime_keeps_full_recognition_as_recognized_without_expected_values()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates(
            "score_digits",
            "max_combo",
            "marvelous",
            "perfect",
            "miss",
            "judgment_counts",
            "combo_ex_score");
        var values = new Dictionary<string, string>
        {
            ["score"] = "0",
            ["max_combo"] = "1234",
            ["marvelous"] = "1234",
            ["perfect"] = "1234",
            ["great"] = "1234",
            ["good"] = "1234",
            ["miss"] = "1234",
            ["ex_score"] = "1234",
        };

        var results = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(values));

        foreach (var field in M7aDigitRecognizer.Fields)
        {
            Assert.Equal("recognized", results[field].Status);
            Assert.Null(results[field].Match);
        }
        Assert.Equal("recognized", M7aDigitRecognizer.AggregateStatus(results));
    }

    [Fact]
    public void Package_contains_required_runtime_template_sets()
    {
        var root = new AppRuntimeResourceResolver().ResolveDigitTemplatesDirectory();
        foreach (var group in new[]
        {
            "score_digits",
            "max_combo",
            "marvelous",
            "perfect",
            "miss",
            "judgment_counts",
            "combo_ex_score",
        })
        {
            var labels = Directory.EnumerateFiles(
                    Path.Combine(root, group),
                    "*.pbm",
                    SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .OrderBy(label => label, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(
                Enumerable.Range(0, 10).Select(value => value.ToString()).ToArray(),
                labels);
        }
    }

    [Fact]
    public async Task Analyzer_passes_zero_candidate_without_formal_promotion()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates(
            "score_digits",
            "max_combo",
            "marvelous",
            "perfect",
            "miss",
            "judgment_counts",
            "combo_ex_score");
        var analyzer = new AppOwnedLiveResultAnalyzer(
            new M7aDigitRecognizer(templateRoot: fixture.Root));
        var bytes = EncodePng(fixture.Render(
            new Dictionary<string, string>
            {
                ["score"] = "0",
                ["max_combo"] = "1",
                ["marvelous"] = "1",
                ["perfect"] = "1",
                ["great"] = "1",
                ["good"] = "1",
                ["miss"] = "1",
                ["ex_score"] = "1",
            }));

        var observation = await analyzer.AnalyzeKnownResultAsync(
            new CapturedFrame(
                bytes,
                1280,
                720,
                1_000,
                DateTimeOffset.UtcNow,
                "fixture"));

        Assert.True(observation.IsResultScreen);
        Assert.Equal("0", observation.Score);
        Assert.Equal("recognized", observation.DigitRecognitionStatus);
        Assert.Equal("0", observation.DigitRecognitions!["score"].RecognizedDigits);
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class TemplateFixture : IDisposable
    {
        private static readonly IReadOnlyDictionary<char, string[]> Patterns =
            new Dictionary<char, string[]>
            {
                ['0'] = ["111", "101", "101", "101", "111"],
                ['1'] = ["010", "110", "010", "010", "111"],
                ['2'] = ["111", "001", "111", "100", "111"],
                ['3'] = ["111", "001", "111", "001", "111"],
                ['4'] = ["101", "101", "111", "001", "001"],
                ['5'] = ["111", "100", "111", "001", "111"],
                ['6'] = ["111", "100", "111", "101", "111"],
                ['7'] = ["111", "001", "001", "001", "001"],
                ['8'] = ["111", "101", "111", "101", "111"],
                ['9'] = ["111", "101", "111", "001", "111"],
            };

        public TemplateFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ddrgp-m7a-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void WriteTemplates(
            params string[] groups)
        {
            WriteTemplates(Root, groups, "0123456789", null);
        }

        public void WriteTemplates(
            string group,
            string labels,
            char? duplicateLabel = null)
        {
            WriteTemplates(Root, [group], labels, duplicateLabel);
        }

        public void WriteTemplateDirectory(string directory)
        {
            WriteTemplatesToDirectory(directory, "0123456789", null);
        }

        private static void WriteTemplates(
            string root,
            IReadOnlyList<string> groups,
            string labels,
            char? duplicateLabel)
        {
            foreach (var group in groups)
            {
                WriteTemplatesToDirectory(
                    Path.IsPathRooted(group) ? group : Path.Combine(root, group),
                    labels,
                    duplicateLabel);
            }
        }

        private static void WriteTemplatesToDirectory(
            string directory,
            string labels,
            char? duplicateLabel)
        {
            Directory.CreateDirectory(directory);
            foreach (var digit in labels)
            {
                var pattern = duplicateLabel == digit ? Patterns['0'] : Patterns[digit];
                var content = new StringBuilder()
                    .AppendLine("P1")
                    .AppendLine("3 5")
                    .AppendLine(string.Join('\n', pattern))
                    .ToString();
                File.WriteAllText(
                    Path.Combine(directory, $"{digit}.pbm"),
                    content,
                    new UTF8Encoding(false));
            }
        }

        public BitmapSource Render(IReadOnlyDictionary<string, string> values)
        {
            const int width = 1280;
            const int height = 720;
            const int stride = width * 4;
            var pixels = new byte[stride * height];
            for (var index = 3; index < pixels.Length; index += 4)
            {
                pixels[index] = 255;
            }

            var rois = new Dictionary<string, (int X, int Y, int Width, int Height, double Focus)>
            {
                ["score"] = (250, 278, 210, 48, 0),
                ["max_combo"] = (714, 368, 284, 32, 0.65),
                ["marvelous"] = (766, 404, 232, 28, 0.52),
                ["perfect"] = (766, 434, 232, 28, 0.52),
                ["great"] = (766, 464, 232, 28, 0.52),
                ["good"] = (766, 494, 232, 28, 0.55),
                ["miss"] = (766, 554, 232, 28, 0.55),
                ["ex_score"] = (748, 580, 250, 34, 0.55),
            };

            foreach (var pair in values)
            {
                if (!rois.TryGetValue(pair.Key, out var roi) || pair.Value.Length == 0)
                {
                    continue;
                }
                var scale = pair.Key == "score" ? 6 : 4;
                var x = roi.X + (int)Math.Round(roi.Width * roi.Focus) + 8;
                var y = roi.Y + (roi.Height - Patterns['0'].Length * scale) / 2;
                foreach (var digit in pair.Value)
                {
                    DrawGlyph(pixels, stride, x, y, scale, Patterns[digit]);
                    x += 3 * scale + 3;
                }
            }

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
            return bitmap;
        }

        private static void DrawGlyph(
            byte[] pixels,
            int stride,
            int left,
            int top,
            int scale,
            IReadOnlyList<string> pattern)
        {
            for (var y = 0; y < pattern.Count; y++)
            {
                for (var x = 0; x < pattern[y].Length; x++)
                {
                    if (pattern[y][x] != '1') continue;
                    for (var dy = 0; dy < scale; dy++)
                    {
                        for (var dx = 0; dx < scale; dx++)
                        {
                            var offset = (top + y * scale + dy) * stride +
                                (left + x * scale + dx) * 4;
                            pixels[offset] = 255;
                            pixels[offset + 1] = 255;
                            pixels[offset + 2] = 255;
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
