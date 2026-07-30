using DDRGpScoreViewer.Capture;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class AppOwnedLiveResultAnalyzerTests
{
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
    public void Preconfirmed_candidate_never_derives_a_formal_score()
    {
        var frame = new CapturedFrame(
            [137, 80, 78, 71, 13, 10, 26, 10],
            1280,
            720,
            1_000,
            DateTimeOffset.UtcNow,
            "fixture");

        var observation = AppOwnedLiveResultAnalyzer.CreateKnownResultObservation(frame);

        Assert.True(observation.IsResultScreen);
        Assert.Empty(observation.Score);
        Assert.Equal("known-result", observation.TitleSignature);
        Assert.Contains("pending", observation.Reason, StringComparison.Ordinal);
    }
}
