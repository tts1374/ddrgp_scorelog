using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacketCatalogCollector.Tests;

public sealed class ManualReviewRoiImageConverterTests
{
    [Fact]
    public void UsesTrimmedTitleAndArtistRois()
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"ddrgp-review-roi-{Guid.NewGuid():N}.png");
        try
        {
            WritePng(path, 1280, 720);
            var converter = new ManualReviewRoiImageConverter();

            var title = Assert.IsType<CroppedBitmap>(
                converter.Convert(path, typeof(BitmapSource), "title", null!));
            var artist = Assert.IsType<CroppedBitmap>(
                converter.Convert(path, typeof(BitmapSource), "artist", null!));

            Assert.Equal(new Int32Rect(309, 60, 467, 32), title.SourceRect);
            Assert.Equal(new Int32Rect(309, 97, 467, 23), artist.SourceRect);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }
        var source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
