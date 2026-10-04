using DDRGpScoreViewer.Capture;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class AppOwnedLiveResultAnalyzerTests
{
    [Theory]
    [InlineData(0xff000000)]
    [InlineData(0xffffffff)]
    [InlineData(0xff0000ff)]
    public async Task Uniform_frame_is_not_screen_departure_evidence(uint color)
    {
        var bitmap = BitmapSource.Create(1280, 720, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Repeat(color, 1280 * 720).ToArray(), 1280 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var observation = await new AppOwnedLiveResultAnalyzer().AnalyzeAsync(
            ResultScreenLayoutTests.Frame(stream.ToArray()));
        Assert.False(observation.IsResultScreen);
        Assert.False(observation.HasResultStructure);
        Assert.Null(observation.ResultSceneFeature);
    }

    [Fact]
    public async Task Invalid_frame_fails_closed_without_external_runtime()
    {
        var analyzer = new AppOwnedLiveResultAnalyzer();
        var frame = new CapturedFrame(
            [1, 2, 3],
            1280,
            720,
            1_000,
            DateTimeOffset.UtcNow,
            "fixture");

        var observation = await analyzer.AnalyzeAsync(frame);

        Assert.False(observation.IsResultScreen);
        Assert.Equal("frame_not_decodable", observation.Reason);
        Assert.Empty(observation.Score);
    }

    [Fact]
    public async Task Preconfirmed_candidate_never_derives_a_formal_score()
    {
        var frame = new CapturedFrame(
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPgEpH7DwABpAE8k4sOtwAAAABJRU5ErkJggg=="),
            1280,
            720,
            1_000,
            DateTimeOffset.UtcNow,
            "fixture");

        var observation = await new AppOwnedLiveResultAnalyzer().AnalyzeKnownResultAsync(frame);

        Assert.True(observation.IsResultScreen);
        Assert.Empty(observation.Score);
        Assert.Equal("known-result", observation.TitleSignature);
        Assert.Contains("result_digit_", observation.Reason, StringComparison.Ordinal);
    }
}
