using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DDRGpScoreViewer.Runtime;

internal readonly record struct AppOwnedPixel(byte Blue, byte Green, byte Red)
{
    public double Luma => 0.2126 * Red + 0.7152 * Green + 0.0722 * Blue;

    public int Spread => Math.Max(Red, Math.Max(Green, Blue)) -
        Math.Min(Red, Math.Min(Green, Blue));
}

/// <summary>
/// Small app-owned bitmap projection shared by the visual evidence producers.
/// It performs only in-memory pixel access and never writes image artifacts.
/// </summary>
internal sealed class AppOwnedImageBuffer
{
    private AppOwnedImageBuffer(byte[] bytes, int width, int height, int stride)
    {
        Bytes = bytes;
        Width = width;
        Height = height;
        Stride = stride;
    }

    private byte[] Bytes { get; }

    public int Width { get; }

    public int Height { get; }

    private int Stride { get; }

    public static AppOwnedImageBuffer From(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var bytes = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(bytes, stride, 0);
        return new AppOwnedImageBuffer(bytes, converted.PixelWidth, converted.PixelHeight, stride);
    }

    public AppOwnedImageBuffer CropScaled((int X, int Y, int Width, int Height) roi)
    {
        return Crop(
            Scale(roi.X, 1280, Width),
            Scale(roi.Y, 720, Height),
            Math.Clamp(Scale(roi.X + roi.Width, 1280, Width), 1, Width),
            Math.Clamp(Scale(roi.Y + roi.Height, 720, Height), 1, Height));
    }

    public AppOwnedImageBuffer Crop(int left, int top, int right, int bottom)
    {
        left = Math.Clamp(left, 0, Width - 1);
        top = Math.Clamp(top, 0, Height - 1);
        right = Math.Clamp(right, left + 1, Width);
        bottom = Math.Clamp(bottom, top + 1, Height);
        var targetStride = (right - left) * 4;
        var bytes = new byte[targetStride * (bottom - top)];
        for (var y = top; y < bottom; y++)
        {
            Buffer.BlockCopy(
                Bytes,
                y * Stride + left * 4,
                bytes,
                (y - top) * targetStride,
                targetStride);
        }

        return new AppOwnedImageBuffer(bytes, right - left, bottom - top, targetStride);
    }

    public AppOwnedPixel GetPixel(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var offset = y * Stride + x * 4;
        return new AppOwnedPixel(Bytes[offset], Bytes[offset + 1], Bytes[offset + 2]);
    }

    public double[] ResizeRgb(int width, int height)
    {
        var values = new double[width * height * 3];
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Clamp(
                (int)Math.Round((y + 0.5) * Height / height - 0.5),
                0,
                Height - 1);
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Clamp(
                    (int)Math.Round((x + 0.5) * Width / width - 0.5),
                    0,
                    Width - 1);
                var pixel = GetPixel(sourceX, sourceY);
                values[index++] = pixel.Red / 255.0;
                values[index++] = pixel.Green / 255.0;
                values[index++] = pixel.Blue / 255.0;
            }
        }

        return values;
    }

    private static int Scale(int value, int baseSize, int actualSize) =>
        Math.Clamp(
            (int)Math.Round(value * (double)actualSize / baseSize),
            0,
            actualSize - 1);
}
