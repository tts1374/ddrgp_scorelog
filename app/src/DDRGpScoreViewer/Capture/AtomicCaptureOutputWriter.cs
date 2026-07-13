using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DDRGpScoreViewer.Capture;

public sealed class AtomicCaptureOutputWriter : ICaptureOutputWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string outputRoot;
    private readonly string dataRoot;

    public AtomicCaptureOutputWriter(string outputRoot, string dataRoot)
    {
        this.outputRoot = Path.GetFullPath(outputRoot);
        this.dataRoot = Path.GetFullPath(dataRoot);
        if (!IsWithin(this.outputRoot, this.dataRoot) ||
            Path.GetRelativePath(this.dataRoot, this.outputRoot) == ".")
        {
            throw new ArgumentException("Capture output must be a child directory of data/.", nameof(outputRoot));
        }
    }

    public async Task<CaptureOutput> WriteAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        Validate(frame);
        var directoryName =
            $"capture-{frame.CapturedAtUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        var finalDirectory = Path.Combine(outputRoot, directoryName);
        var stagingDirectory = Path.Combine(dataRoot, $".{directoryName}.tmp");
        var outputRootCreated = false;

        try
        {
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(stagingDirectory);

            var imagePath = Path.Combine(stagingDirectory, "frame.png");
            var manifestPath = Path.Combine(stagingDirectory, "frame_manifest.csv");
            var metadataPath = Path.Combine(stagingDirectory, "capture_metadata.json");

            await File.WriteAllBytesAsync(imagePath, frame.PngBytes, cancellationToken);
            await WriteUtf8Async(manifestPath, BuildManifest(frame), cancellationToken);
            await WriteUtf8Async(metadataPath, BuildMetadata(frame), cancellationToken);

            if (!Directory.Exists(outputRoot))
            {
                Directory.CreateDirectory(outputRoot);
                outputRootCreated = true;
            }
            Directory.Move(stagingDirectory, finalDirectory);

            return new CaptureOutput(
                finalDirectory,
                Path.Combine(finalDirectory, "frame.png"),
                Path.Combine(finalDirectory, "frame_manifest.csv"),
                Path.Combine(finalDirectory, "capture_metadata.json"));
        }
        catch
        {
            DeleteStagingDirectory(stagingDirectory);
            if (outputRootCreated && Directory.Exists(outputRoot) && !Directory.EnumerateFileSystemEntries(outputRoot).Any())
            {
                Directory.Delete(outputRoot);
            }
            throw;
        }
    }

    private static string BuildManifest(CapturedFrame frame) =>
        "image_path,timestamp_ms,screen_type,capture_source,width,height,captured_at_utc\n" +
        $"frame.png,{frame.TimestampMs},unknown,{Csv(frame.CaptureSource)},{frame.Width},{frame.Height}," +
        $"{frame.CapturedAtUtc:O}\n";

    private static string BuildMetadata(CapturedFrame frame)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                metadata_schema_version = 1,
                image_path = "frame.png",
                timestamp_ms = frame.TimestampMs,
                captured_at_utc = frame.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                width = frame.Width,
                height = frame.Height,
                capture_source = frame.CaptureSource,
            },
            new JsonSerializerOptions { WriteIndented = true });
        return json + "\n";
    }

    private static async Task WriteUtf8Async(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken);
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static bool IsWithin(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static void Validate(CapturedFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentException("Captured frame dimensions must be positive.", nameof(frame));
        }
        if (frame.TimestampMs < 0)
        {
            throw new ArgumentException("Captured frame timestamp must be non-negative.", nameof(frame));
        }
        if (frame.PngBytes.Length < 8 ||
            !frame.PngBytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new ArgumentException("Captured frame must contain PNG bytes.", nameof(frame));
        }
        if (string.IsNullOrWhiteSpace(frame.CaptureSource))
        {
            throw new ArgumentException("Capture source is required.", nameof(frame));
        }
    }

    private static void DeleteStagingDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
