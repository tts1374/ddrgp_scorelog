using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace JacketCatalogCollector;

public sealed class ManualReviewRoiImageConverter : IValueConverter
{
    private const int BaseWidth = 1280;
    private const int BaseHeight = 720;
    private static readonly IReadOnlyDictionary<string, (int X, int Y, int Width, int Height)> Rois =
        new Dictionary<string, (int X, int Y, int Width, int Height)>(StringComparer.Ordinal)
        {
            ["title"] = (309, 60, 467, 32),
            ["artist"] = (309, 97, 467, 26),
        };

    public object? Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not string path || parameter is not string roiName)
        {
            return null;
        }
        if (!File.Exists(path) || !Rois.TryGetValue(roiName, out var roi))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new System.Uri(Path.GetFullPath(path), System.UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            var left = (int)Math.Round(roi.X * image.PixelWidth / (double)BaseWidth);
            var top = (int)Math.Round(roi.Y * image.PixelHeight / (double)BaseHeight);
            var right = (int)Math.Round(
                (roi.X + roi.Width) * image.PixelWidth / (double)BaseWidth);
            var bottom = (int)Math.Round(
                (roi.Y + roi.Height) * image.PixelHeight / (double)BaseHeight);
            if (left < 0 || top < 0 || right > image.PixelWidth || bottom > image.PixelHeight
                || right <= left || bottom <= top)
            {
                return null;
            }

            var crop = new CroppedBitmap(
                image,
                new Int32Rect(left, top, right - left, bottom - top));
            crop.Freeze();
            return crop;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
