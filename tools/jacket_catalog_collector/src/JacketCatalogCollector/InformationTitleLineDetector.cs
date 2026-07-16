using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacketCatalogCollector;

public readonly record struct InformationRegion(int X, int Y, int Width, int Height)
{
    public static InformationRegion PanelMarker { get; } = new(286, 35, 134, 23);
    public static InformationRegion TitleLine { get; } = new(286, 64, 504, 25);

    public InformationRegion ScaleTo(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return new InformationRegion(0, 0, 0, 0);
        }
        var scaleX = frameWidth / 1280d;
        var scaleY = frameHeight / 720d;
        return new InformationRegion(
            (int)Math.Round(X * scaleX),
            (int)Math.Round(Y * scaleY),
            Math.Max(1, (int)Math.Round(Width * scaleX)),
            Math.Max(1, (int)Math.Round(Height * scaleY)));
    }

    public bool IsInside(int frameWidth, int frameHeight) =>
        X >= 0 && Y >= 0 && Width > 0 && Height > 0
        && X <= frameWidth - Width
        && Y <= frameHeight - Height;
}

public sealed record InformationTitleLineDetectorOptions(
    byte WhiteChannelThreshold = 170,
    byte MaximumChannelSpread = 45,
    int MinimumMarkerPixelCount = 100,
    int MinimumTitlePixelCount = 32,
    int StableFrameCount = 3,
    TimeSpan? MinimumStableDuration = null)
{
    public TimeSpan MinimumStableDurationValue =>
        MinimumStableDuration ?? TimeSpan.FromMilliseconds(100);

    public void Validate()
    {
        if (MinimumMarkerPixelCount < 1
            || MinimumMarkerPixelCount > InformationRegion.PanelMarker.Width
                * InformationRegion.PanelMarker.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumMarkerPixelCount));
        }
        if (MinimumTitlePixelCount < 1
            || MinimumTitlePixelCount > InformationRegion.TitleLine.Width
                * InformationRegion.TitleLine.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumTitlePixelCount));
        }
        if (StableFrameCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(StableFrameCount));
        }
        if (MinimumStableDurationValue < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumStableDuration));
        }
    }
}

public enum InformationTitleLineState
{
    NoFrame,
    InvalidFrame,
    NotDisplayed,
    Settling,
    Stable,
}

public sealed record InformationTitleLineDetectionResult(
    InformationTitleLineState State,
    string? TitleLineHash,
    int ConsecutiveFrameCount,
    TimeSpan StableDuration,
    string Diagnostic,
    long ProcessedFrameCount,
    long InvalidFrameCount,
    string DetectorVersion = JacketObservationVersions.InformationDetector,
    string RoiVersion = JacketObservationVersions.InformationPanelRoi,
    string FeatureVersion = JacketObservationVersions.InformationTitleLineFeature,
    long SourceSequence = -1,
    DateTimeOffset CapturedAtUtc = default)
{
    public bool IsDisplayed => State is InformationTitleLineState.Settling
        or InformationTitleLineState.Stable;
    public bool IsStable => State == InformationTitleLineState.Stable;

    public static InformationTitleLineDetectionResult Empty(string diagnostic) => new(
        InformationTitleLineState.NoFrame,
        null,
        0,
        TimeSpan.Zero,
        diagnostic,
        0,
        0);
}

public sealed class InformationTitleLineDetector
{
    private readonly InformationTitleLineDetectorOptions options;
    private string? previousHash;
    private int consecutiveFrameCount;
    private DateTimeOffset stableStartedAt;
    private DateTimeOffset? previousCapturedAtUtc;
    private long processedFrameCount;
    private long invalidFrameCount;

    public InformationTitleLineDetector(InformationTitleLineDetectorOptions? options = null)
    {
        this.options = options ?? new InformationTitleLineDetectorOptions();
        this.options.Validate();
    }

    public InformationTitleLineDetectionResult Observe(RawCaptureFrame frame)
    {
        processedFrameCount++;
        MaskSample sample;
        try
        {
            sample = Decode(frame);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or InvalidOperationException
                or NotSupportedException or OverflowException or FormatException)
        {
            invalidFrameCount++;
            ResetSettling();
            return Result(
                InformationTitleLineState.InvalidFrame,
                null,
                TimeSpan.Zero,
                $"frame/INFORMATION ROI invalid: {exception.Message}", frame);
        }

        if (previousCapturedAtUtc is not null
            && frame.CapturedAtUtc < previousCapturedAtUtc.Value)
        {
            invalidFrameCount++;
            ResetSettling();
            return Result(
                InformationTitleLineState.InvalidFrame,
                null,
                TimeSpan.Zero,
                "frame timestamp moved backwards", frame);
        }
        previousCapturedAtUtc = frame.CapturedAtUtc;

        if (sample.MarkerPixelCount < options.MinimumMarkerPixelCount
            || sample.TitlePixelCount < options.MinimumTitlePixelCount)
        {
            ResetSettling();
            return Result(
                InformationTitleLineState.NotDisplayed,
                null,
                TimeSpan.Zero,
                "INFORMATION panel/title line is not displayed", frame);
        }

        if (!string.Equals(previousHash, sample.TitleLineHash, StringComparison.Ordinal))
        {
            previousHash = sample.TitleLineHash;
            consecutiveFrameCount = 1;
            stableStartedAt = frame.CapturedAtUtc;
        }
        else
        {
            consecutiveFrameCount++;
        }
        var stableDuration = frame.CapturedAtUtc - stableStartedAt;
        var stable = consecutiveFrameCount >= options.StableFrameCount
            && stableDuration >= options.MinimumStableDurationValue;
        return Result(
            stable ? InformationTitleLineState.Stable : InformationTitleLineState.Settling,
            sample.TitleLineHash,
            stableDuration,
            stable
                ? $"INFORMATION title line stable ({consecutiveFrameCount} frames, "
                    + $"{stableDuration.TotalMilliseconds:0}ms)"
                : $"INFORMATION title line settling ({consecutiveFrameCount}/{options.StableFrameCount} "
                    + $"frames, {stableDuration.TotalMilliseconds:0}ms/"
                    + $"{options.MinimumStableDurationValue.TotalMilliseconds:0}ms)",
            frame);
    }

    public void Reset()
    {
        ResetSettling();
        previousCapturedAtUtc = null;
        processedFrameCount = 0;
        invalidFrameCount = 0;
    }

    private void ResetSettling()
    {
        previousHash = null;
        consecutiveFrameCount = 0;
        stableStartedAt = default;
    }

    private MaskSample Decode(RawCaptureFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.PngBytes.Length == 0)
        {
            throw new InvalidOperationException("frame size or PNG is empty");
        }
        using var stream = new MemoryStream(frame.PngBytes, writable: false);
        var decoded = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoded.Frames.Count != 1)
        {
            throw new InvalidOperationException("PNG must contain exactly one frame");
        }
        var source = decoded.Frames[0];
        if (source.PixelWidth != frame.Width || source.PixelHeight != frame.Height)
        {
            throw new InvalidOperationException(
                $"frame metadata size {frame.Width}x{frame.Height} does not match PNG "
                + $"{source.PixelWidth}x{source.PixelHeight}");
        }
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        var marker = BuildMask(
            pixels,
            stride,
            frame.Width,
            frame.Height,
            InformationRegion.PanelMarker);
        var title = BuildMask(
            pixels,
            stride,
            frame.Width,
            frame.Height,
            InformationRegion.TitleLine);
        return new MaskSample(
            marker.PixelCount,
            title.PixelCount,
            Convert.ToHexString(SHA256.HashData(title.PackedBits)).ToLowerInvariant());
    }

    private BinaryMask BuildMask(
        byte[] pixels,
        int stride,
        int frameWidth,
        int frameHeight,
        InformationRegion baseRegion)
    {
        var scaled = baseRegion.ScaleTo(frameWidth, frameHeight);
        if (!scaled.IsInside(frameWidth, frameHeight))
        {
            throw new InvalidOperationException(
                $"scaled INFORMATION ROI {scaled.X},{scaled.Y},{scaled.Width},{scaled.Height} "
                + "is outside frame");
        }
        var bitCount = checked(baseRegion.Width * baseRegion.Height);
        var packed = new byte[checked((bitCount + 7) / 8)];
        var whitePixelCount = 0;
        var bitIndex = 0;
        for (var y = 0; y < baseRegion.Height; y++)
        {
            var sourceY = scaled.Y + Math.Min(
                scaled.Height - 1,
                y * scaled.Height / baseRegion.Height);
            for (var x = 0; x < baseRegion.Width; x++)
            {
                var sourceX = scaled.X + Math.Min(
                    scaled.Width - 1,
                    x * scaled.Width / baseRegion.Width);
                var offset = sourceY * stride + sourceX * 4;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                if (IsWhite(red, green, blue))
                {
                    packed[bitIndex / 8] |= (byte)(1 << (7 - bitIndex % 8));
                    whitePixelCount++;
                }
                bitIndex++;
            }
        }
        return new BinaryMask(packed, whitePixelCount);
    }

    private bool IsWhite(byte red, byte green, byte blue)
    {
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        return minimum >= options.WhiteChannelThreshold
            && maximum - minimum <= options.MaximumChannelSpread;
    }

    private InformationTitleLineDetectionResult Result(
        InformationTitleLineState state,
        string? titleLineHash,
        TimeSpan stableDuration,
        string diagnostic,
        RawCaptureFrame frame) => new(
        state,
        titleLineHash,
        consecutiveFrameCount,
        stableDuration,
        diagnostic,
        processedFrameCount,
        invalidFrameCount,
        SourceSequence: frame.Sequence,
        CapturedAtUtc: frame.CapturedAtUtc);

    private sealed record BinaryMask(byte[] PackedBits, int PixelCount);
    private sealed record MaskSample(
        int MarkerPixelCount,
        int TitlePixelCount,
        string TitleLineHash);
}
