namespace JacketCatalogCollector.Tests;

public sealed class NativeWindowEnumeratorTests
{
    [Theory]
    [InlineData("ddr-konaste", "DanceDanceRevolution", true)]
    [InlineData("DDRGP", "unrelated", true)]
    [InlineData("launcher", "DDR GRAND PRIX", true)]
    [InlineData("ddr-konaste-helper", "DanceDanceRevolution", false)]
    [InlineData("launcher", "DanceDanceRevolution", false)]
    public void CandidateReasonsRecognizeOnlySupportedDdrGpIdentities(
        string processName,
        string title,
        bool expected)
    {
        var snapshot = new WindowIdentitySnapshot(
            1, 2, 3, processName, title, "class", 1280, 720, true, false);

        var reasons = NativeWindowEnumerator.CandidateReasons(snapshot);

        Assert.Equal(expected, reasons.Count > 0);
    }

    [Theory]
    [InlineData("ddr-konaste", true, false, 1280, 720, false)]
    [InlineData("other", true, false, 1280, 720, true)]
    [InlineData("other", false, false, 1280, 720, false)]
    [InlineData("other", true, true, 1280, 720, false)]
    [InlineData("other", true, false, 0, 720, false)]
    public void PreviewAttemptSkipsProtectedOrUncapturableWindows(
        string processName,
        bool visible,
        bool minimized,
        int width,
        int height,
        bool expected)
    {
        var snapshot = new WindowIdentitySnapshot(
            1, 2, 3, processName, "title", "class", width, height, visible, minimized);

        Assert.Equal(expected, NativeWindowEnumerator.ShouldAttemptPreview(snapshot));
    }
}
