using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacketCatalogCollector;

public sealed class JacketObservationDetector
{
    private const int SampleWidth = 16;
    private const int SampleHeight = 16;
    private readonly JacketDetectorOptions options;
    private readonly JacketRoi baseRoi;
    private Sample? previous;
    private int stableFrameCount;
    private DateTimeOffset stableStartedAt;
    private readonly HashSet<string> stableFeatureHashes = new(StringComparer.Ordinal);
    private DateTimeOffset? previousCapturedAtUtc;
    private long processedFrameCount;
    private long invalidFrameCount;
    private long duplicatePreviewCount;

    public JacketObservationDetector(
        JacketDetectorOptions? options = null,
        JacketRoi? baseRoi = null)
    {
        this.options = options ?? new JacketDetectorOptions();
        this.options.Validate();
        this.baseRoi = baseRoi ?? JacketRoi.Base;
    }

    public JacketDetectionResult Observe(RawCaptureFrame frame)
    {
        processedFrameCount++;
        Sample current;
        try
        {
            current = Decode(frame);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or InvalidOperationException
                or NotSupportedException or OverflowException or FormatException)
        {
            invalidFrameCount++;
            ResetSettling();
            return Result(
                JacketDetectionState.InvalidFrame,
                null,
                $"frame/ROI invalid: {exception.Message}");
        }

        if (previousCapturedAtUtc is not null
            && frame.CapturedAtUtc < previousCapturedAtUtc.Value)
        {
            invalidFrameCount++;
            ResetSettling();
            return Result(
                JacketDetectionState.InvalidFrame,
                null,
                "frame timestamp moved backwards");
        }
        previousCapturedAtUtc = frame.CapturedAtUtc;

        var firstFrame = previous is null;
        var difference = firstFrame
            ? 1d
            : MeanAbsoluteDifference(previous!.Pixels, current.Pixels);
        if (firstFrame || difference > options.ChangeThreshold)
        {
            stableFrameCount = 1;
            stableStartedAt = frame.CapturedAtUtc;
            previous = current;
            return Result(
                JacketDetectionState.ChangeCandidate,
                ToCandidate(current, frame, difference),
                firstFrame ? "first valid jacket ROI" : "jacket ROI changed");
        }

        stableFrameCount++;
        previous = current;
        var stableDuration = frame.CapturedAtUtc - stableStartedAt;
        var stableCandidate = ToCandidate(current, frame, difference, stableDuration);
        var stable = stableFrameCount >= options.StableFrameCount
            && stableDuration >= options.MinimumStableDurationValue;
        if (!stable)
        {
            return Result(
                JacketDetectionState.ChangeCandidate,
                stableCandidate,
                $"jacket ROI is settling ({stableFrameCount}/{options.StableFrameCount} frames, "
                + $"{stableDuration.TotalMilliseconds:0}ms/{options.MinimumStableDurationValue.TotalMilliseconds:0}ms)");
        }

        if (!stableFeatureHashes.Add(current.FeatureHash))
        {
            duplicatePreviewCount++;
            return Result(
                JacketDetectionState.DuplicatePreview,
                stableCandidate,
                "same stable preview; the existing stable candidate remains available for adoption");
        }

        return Result(
            JacketDetectionState.StableCandidate,
            stableCandidate,
            $"stable jacket candidate ({stableFrameCount} frames, {stableDuration.TotalMilliseconds:0}ms); explicit adoption required");
    }

    public void Reset()
    {
        ResetSettling();
        stableFeatureHashes.Clear();
        previousCapturedAtUtc = null;
        processedFrameCount = 0;
        invalidFrameCount = 0;
        duplicatePreviewCount = 0;
    }

    private void ResetSettling()
    {
        previous = null;
        stableFrameCount = 0;
        stableStartedAt = default;
    }

    public IReadOnlyList<string> StableFeatureHashes => stableFeatureHashes
        .Order(StringComparer.Ordinal)
        .ToList();

    public void RestoreStableFeatureHashes(IEnumerable<string> featureHashes)
    {
        stableFeatureHashes.Clear();
        foreach (var featureHash in featureHashes)
        {
            if (!string.IsNullOrWhiteSpace(featureHash))
            {
                stableFeatureHashes.Add(featureHash);
            }
        }
    }

    private Sample Decode(RawCaptureFrame frame)
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
        var roi = baseRoi.ScaleTo(frame.Width, frame.Height);
        if (!roi.IsInside(frame.Width, frame.Height))
        {
            throw new InvalidOperationException(
                $"scaled jacket ROI {roi.X},{roi.Y},{roi.Width},{roi.Height} is outside frame");
        }
        var sample = new byte[SampleWidth * SampleHeight * 3];
        for (var y = 0; y < SampleHeight; y++)
        {
            var sourceY = roi.Y + Math.Min(roi.Height - 1, y * roi.Height / SampleHeight);
            for (var x = 0; x < SampleWidth; x++)
            {
                var sourceX = roi.X + Math.Min(roi.Width - 1, x * roi.Width / SampleWidth);
                var sourceOffset = sourceY * stride + sourceX * 4;
                var targetOffset = (y * SampleWidth + x) * 3;
                sample[targetOffset] = pixels[sourceOffset + 2];
                sample[targetOffset + 1] = pixels[sourceOffset + 1];
                sample[targetOffset + 2] = pixels[sourceOffset];
            }
        }
        var crop = new CroppedBitmap(source, new System.Windows.Int32Rect(
            roi.X, roi.Y, roi.Width, roi.Height));
        crop.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(crop));
        using var cropStream = new MemoryStream();
        encoder.Save(cropStream);
        var hash = Convert.ToHexString(SHA256.HashData(sample)).ToLowerInvariant();
        return new Sample(sample, hash, roi, cropStream.ToArray());
    }

    private JacketObservationCandidate ToCandidate(
        Sample sample,
        RawCaptureFrame frame,
        double difference,
        TimeSpan? stableDuration = null) =>
        new(
            sample.FeatureHash,
            frame with { PngBytes = [.. frame.PngBytes] },
            [.. sample.CropPng],
            new JacketFeatureObservation(
                JacketObservationVersions.FrameFeature,
                JacketObservationVersions.Roi,
                sample.Roi,
                sample.FeatureHash,
                difference,
                SampleWidth,
                SampleHeight,
                JacketObservationVersions.Detector,
                options.ChangeThreshold,
                options.StableFrameCount,
                checked((long)options.MinimumStableDurationValue.TotalMilliseconds)),
            stableFrameCount,
            stableDuration ?? TimeSpan.Zero);

    private JacketDetectionResult Result(
        JacketDetectionState state,
        JacketObservationCandidate? value,
        string diagnostic) => new(
        state,
        value,
        diagnostic,
        processedFrameCount,
        invalidFrameCount,
        duplicatePreviewCount);

    private static double MeanAbsoluteDifference(byte[] left, byte[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return 1d;
        }
        var difference = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            difference += Math.Abs(left[index] - right[index]) / 255d;
        }
        return difference / left.Length;
    }

    private sealed record Sample(
        byte[] Pixels,
        string FeatureHash,
        JacketRoi Roi,
        byte[] CropPng);
}
