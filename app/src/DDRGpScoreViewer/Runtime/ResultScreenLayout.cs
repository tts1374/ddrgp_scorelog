namespace DDRGpScoreViewer.Runtime;

/// <summary>Measured RESULT translations; glyphs and the central song regions are not mirrored.</summary>
internal static class ResultScreenLayout
{
    public static (int X, int Y, int Width, int Height) Map(
        (int X, int Y, int Width, int Height) roi,
        bool secondPlayer)
    {
        if (!secondPlayer) return roi;
        if (roi.X == 170 && roi.Y == 122)
            return (902, 128, 91, 114); // Inner rank region excludes the 2P background stripes.
        if (roi.X == 385 && roi.Y == 135)
            return (800, 163, 78, 85); // Badge bounds exclude the adjacent rank and bright background.
        var offset = roi switch
        {
            { Y: >= 330 and < 654, X: >= 662 } => -498, // Detailed RESULT panel.
            { Y: >= 330 and < 654, X: < 662 } => 498, // Calorie panel.
            { Y: >= 56 and <= 104, X: >= 360 and <= 392 } => 460,
            { Y: >= 250 and <= 278, X: < 488 } => 640,
            _ => 0,
        };
        return (roi.X + offset, roi.Y, roi.Width, roi.Height);
    }
}
