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
    public void Score_ignores_a_non_digit_component_touching_the_roi_top_edge()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates("score_digits");

        var result = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(
                new Dictionary<string, string> { ["score"] = "995880" },
                addScoreTopNoise: true),
            new Dictionary<string, string> { ["score"] = "995880" })["score"];

        Assert.Equal("recognized", result.Status);
        Assert.Equal("995880", result.RecognizedDigits);
        Assert.True(result.Match);
    }

    [Fact]
    public void Uses_roi_specific_shared_and_ex_score_fallback_template_groups()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates(
            "score_digits",
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
    public void Result_digit_rois_match_numeric_display_bounds()
    {
        var expected = new Dictionary<string, (int X, int Y, int Width, int Height)>
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

        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, M7aDigitRecognizer.RoiDefinitions[pair.Key]);
        }
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
    public void Miss_equal_nearest_templates_remain_ambiguous()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates("judgment_counts", "0123456789", duplicateLabel: '1');

        var result = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(new Dictionary<string, string> { ["miss"] = "0" }))["miss"];

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
    public void Miss_uses_shared_judgment_templates_with_the_common_mask()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates("judgment_counts");

        var result = new M7aDigitRecognizer(templateRoot: fixture.Root).Recognize(
            fixture.Render(new Dictionary<string, string> { ["miss"] = "0" }),
            new Dictionary<string, string> { ["miss"] = "0" })["miss"];

        Assert.Equal("recognized", result.Status);
        Assert.Equal("0", result.RecognizedDigits);
        Assert.True(result.Match);
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
    public void Formal_image_evidence_uses_margin_confidence_and_connects_ok_source()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates(
            "score_digits",
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
            ["ok"] = "21",
        };
        var recognizer = new M7aDigitRecognizer(templateRoot: fixture.Root);
        var image = fixture.Render(values);
        var digitResults = recognizer.RecognizeForFormalEvidence(image);
        var evidence = new AppOwnedResultVisualEvidenceProducer(recognizer).Produce(
            image,
            digitResults);

        foreach (var fieldName in M7aDigitRecognizer.Fields)
        {
            Assert.Equal("recognized", digitResults[fieldName].Status);
            Assert.True(digitResults[fieldName].Confidence >= 0.98);
        }
        Assert.Equal(21, evidence.Ok);
        Assert.Equal(
            FormalEvidenceSourceNames.ResultNumericVisualEvidence,
            evidence.Sources["ok"]);
        Assert.True(evidence.Confidences["ok"] >= 0.98);
    }

    [Fact]
    public void Package_contains_required_runtime_template_sets()
    {
        var root = new AppRuntimeResourceResolver().ResolveDigitTemplatesDirectory();
        foreach (var group in new[]
        {
            "score_digits",
            "judgment_counts",
            "combo_ex_score",
            "chart_level",
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
        foreach (var group in new[] { "max_combo", "marvelous", "perfect", "miss" })
        {
            var path = Path.Combine(root, group);
            Assert.False(
                Directory.Exists(path) &&
                Directory.EnumerateFiles(path, "*.pbm", SearchOption.TopDirectoryOnly).Any());
        }
    }

    [Fact]
    public async Task Analyzer_passes_zero_candidate_without_formal_promotion()
    {
        using var fixture = new TemplateFixture();
        fixture.WriteTemplates(
            "score_digits",
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

        public BitmapSource Render(
            IReadOnlyDictionary<string, string> values,
            bool addScoreTopNoise = false)
        {
            const int width = 1280;
            const int height = 720;
            const int stride = width * 4;
            var pixels = new byte[stride * height];
            for (var index = 3; index < pixels.Length; index += 4)
            {
                pixels[index] = 255;
            }

            var rois = new Dictionary<string, (int X, int Y, int Width, int Height)>
            {
                ["score"] = (197, 277, 269, 45),
                ["max_combo"] = (897, 370, 91, 23),
                ["marvelous"] = (896, 404, 92, 20),
                ["perfect"] = (896, 433, 92, 21),
                ["great"] = (896, 465, 92, 21),
                ["good"] = (896, 495, 92, 21),
                ["miss"] = (897, 555, 92, 21),
                ["ex_score"] = (898, 584, 91, 23),
                ["ok"] = (896, 524, 92, 28),
            };

            foreach (var pair in values)
            {
                if (!rois.TryGetValue(pair.Key, out var roi) || pair.Value.Length == 0)
                {
                    continue;
                }
                var scale = pair.Key == "score" ? 6 : 4;
                var renderedWidth = pair.Value.Length * 3 * scale +
                    Math.Max(0, pair.Value.Length - 1) * 3;
                var x = roi.X + roi.Width - renderedWidth - 4;
                var y = roi.Y + (roi.Height - Patterns['0'].Length * scale) / 2;
                foreach (var digit in pair.Value)
                {
                    DrawGlyph(pixels, stride, x, y, scale, Patterns[digit]);
                    x += 3 * scale + 3;
                }
            }

            if (addScoreTopNoise)
            {
                DrawGlyph(pixels, stride, 245, 277, 6, Patterns['4']);
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
