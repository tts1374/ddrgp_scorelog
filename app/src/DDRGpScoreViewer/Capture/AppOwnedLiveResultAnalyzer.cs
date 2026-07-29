using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DDRGpScoreViewer.Capture;

/// <summary>
/// App-owned RESULT gate. Numeric result recognition remains a later runtime concern;
/// this boundary never derives formal score values from a candidate frame.
/// </summary>
public sealed class AppOwnedLiveResultAnalyzer : ILiveResultAnalyzer
{
    public Task<LiveResultObservation> AnalyzeAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Analyze(frame));
    }

    internal static LiveResultObservation CreateKnownResultObservation(CapturedFrame frame)
    {
        var signature = CreateFrameSignature(frame);
        return new LiveResultObservation(
            true,
            string.Empty,
            signature,
            "preconfirmed_result_candidate_score_recognition_pending");
    }

    internal static LiveResultObservation Analyze(CapturedFrame frame)
    {
        if (!IsPng(frame.PngBytes))
        {
            return new LiveResultObservation(
                false,
                string.Empty,
                string.Empty,
                "frame_not_decodable");
        }

        try
        {
            using var stream = new MemoryStream(frame.PngBytes, writable: false);
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames[0];
            var pixels = PixelBuffer.From(bitmap);
            var header = pixels.Measure(480, 0, 320, 58);
            var detail = pixels.Measure(662, 330, 462, 288);
            var headerScore = HeaderScore(header);
            var detailScore = DetailScore(detail);
            if (headerScore < 0.72 || detailScore < 0.74)
            {
                return new LiveResultObservation(
                    false,
                    string.Empty,
                    string.Empty,
                    "results_header_not_detected");
            }

            return new LiveResultObservation(
                true,
                string.Empty,
                CreatePixelSignature(pixels, 488, 274, 304, 32),
                "result_score_recognition_deferred");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException or
                FileFormatException or System.Runtime.InteropServices.COMException)
        {
            return new LiveResultObservation(
                false,
                string.Empty,
                string.Empty,
                $"frame_not_decodable:{exception.GetType().Name}");
        }
    }

    private static double HeaderScore(RegionMetrics metrics) =>
        0.45 * Ratio(metrics.WhiteRatio, 0.16, 0.21) +
        0.35 * Ratio(metrics.EdgeRatio, 0.20, 0.24) +
        0.20 * Ratio(metrics.StandardDeviation, 75, 95);

    private static double DetailScore(RegionMetrics metrics) =>
        0.45 * Ratio(metrics.BorderCyanRatio, 0.22, 0.31) +
        0.25 * Ratio(metrics.CyanRatio, 0.06, 0.085) +
        0.20 * Ratio(metrics.EdgeRatio, 0.09, 0.12) +
        0.10 * (1.0 - Math.Min(Math.Abs(metrics.MeanLuma - 130.0) / 55.0, 1.0));

    private static double Ratio(double value, double low, double high)
    {
        if (high <= low)
        {
            return value >= high ? 1.0 : 0.0;
        }

        return Math.Clamp((value - low) / (high - low), 0.0, 1.0);
    }

    private static string CreateFrameSignature(CapturedFrame frame)
    {
        try
        {
            using var stream = new MemoryStream(frame.PngBytes, writable: false);
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            return Convert.ToHexString(SHA256.HashData(decoder.Frames[0].CopyPixelsToArray()));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException or
                FileFormatException or System.Runtime.InteropServices.COMException)
        {
            return Convert.ToHexString(SHA256.HashData(frame.PngBytes));
        }
    }

    private static string CreatePixelSignature(
        PixelBuffer pixels,
        int x,
        int y,
        int width,
        int height)
    {
        var bytes = pixels.CopyRegion(x, y, width, height);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

    private readonly record struct RegionMetrics(
        double WhiteRatio,
        double CyanRatio,
        double BorderCyanRatio,
        double EdgeRatio,
        double MeanLuma,
        double StandardDeviation);

    private sealed class PixelBuffer
    {
        private PixelBuffer(byte[] bytes, int width, int height, int stride)
        {
            Bytes = bytes;
            Width = width;
            Height = height;
            Stride = stride;
        }

        private byte[] Bytes { get; }
        private int Width { get; }
        private int Height { get; }
        private int Stride { get; }

        public static PixelBuffer From(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var bytes = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(bytes, stride, 0);
            return new PixelBuffer(bytes, converted.PixelWidth, converted.PixelHeight, stride);
        }

        public RegionMetrics Measure(int baseX, int baseY, int baseWidth, int baseHeight)
        {
            var x = Scale(baseX, 1280, Width);
            var y = Scale(baseY, 720, Height);
            var right = Math.Clamp(Scale(baseX + baseWidth, 1280, Width), x + 1, Width);
            var bottom = Math.Clamp(Scale(baseY + baseHeight, 720, Height), y + 1, Height);
            var sampleCount = (right - x) * (bottom - y);
            var lumas = new double[sampleCount];
            var white = 0;
            var cyan = 0;
            var edge = 0;
            var index = 0;
            for (var py = y; py < bottom; py++)
            {
                for (var px = x; px < right; px++)
                {
                    var offset = py * Stride + px * 4;
                    var blue = Bytes[offset];
                    var green = Bytes[offset + 1];
                    var red = Bytes[offset + 2];
                    var luma = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
                    lumas[index++] = luma;
                    if (red > 190 && green > 190 && blue > 190 &&
                        Math.Abs(red - green) < 45 && Math.Abs(green - blue) < 45)
                    {
                        white++;
                    }
                    if (green > 110 && blue > 110 && red < 140 &&
                        Math.Abs(green - blue) < 110)
                    {
                        cyan++;
                    }

                    if (px > x &&
                        Math.Abs(luma - LumaAt(px - 1, py)) > 35)
                    {
                        edge++;
                    }
                    else if (py > y &&
                        Math.Abs(luma - LumaAt(px, py - 1)) > 35)
                    {
                        edge++;
                    }
                }
            }

            var borderThickness = Math.Max(3, Math.Min(right - x, bottom - y) / 25);
            var borderCount = 0;
            var borderCyan = 0;
            for (var py = y; py < bottom; py++)
            {
                for (var px = x; px < right; px++)
                {
                    if (px >= x + borderThickness && px < right - borderThickness &&
                        py >= y + borderThickness && py < bottom - borderThickness)
                    {
                        continue;
                    }

                    borderCount++;
                    if (IsCyan(px, py))
                    {
                        borderCyan++;
                    }
                }
            }

            var mean = lumas.Average();
            var variance = lumas.Select(value => Math.Pow(value - mean, 2)).Average();
            return new RegionMetrics(
                (double)white / sampleCount,
                (double)cyan / sampleCount,
                borderCount == 0 ? 0 : (double)borderCyan / borderCount,
                (double)edge / sampleCount,
                mean,
                Math.Sqrt(variance));
        }

        public byte[] CopyRegion(int baseX, int baseY, int baseWidth, int baseHeight)
        {
            var x = Scale(baseX, 1280, Width);
            var y = Scale(baseY, 720, Height);
            var right = Math.Clamp(Scale(baseX + baseWidth, 1280, Width), x + 1, Width);
            var bottom = Math.Clamp(Scale(baseY + baseHeight, 720, Height), y + 1, Height);
            var result = new byte[(right - x) * (bottom - y) * 4];
            var targetStride = (right - x) * 4;
            for (var row = y; row < bottom; row++)
            {
                Buffer.BlockCopy(
                    Bytes,
                    row * Stride + x * 4,
                    result,
                    (row - y) * targetStride,
                    targetStride);
            }

            return result;
        }

        private double LumaAt(int x, int y)
        {
            var offset = y * Stride + x * 4;
            return 0.2126 * Bytes[offset + 2] +
                0.7152 * Bytes[offset + 1] +
                0.0722 * Bytes[offset];
        }

        private bool IsCyan(int x, int y)
        {
            var offset = y * Stride + x * 4;
            var blue = Bytes[offset];
            var green = Bytes[offset + 1];
            var red = Bytes[offset + 2];
            return green > 110 && blue > 110 && red < 140 &&
                Math.Abs(green - blue) < 110;
        }

        private static int Scale(int value, int baseSize, int actualSize) =>
            Math.Clamp((int)Math.Round(value * (double)actualSize / baseSize), 0, actualSize - 1);
    }
}

internal static class BitmapFrameExtensions
{
    public static byte[] CopyPixelsToArray(this BitmapFrame frame)
    {
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var bytes = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(bytes, stride, 0);
        return bytes;
    }
}
