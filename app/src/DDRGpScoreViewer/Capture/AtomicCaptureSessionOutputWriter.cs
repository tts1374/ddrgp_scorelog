using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DDRGpScoreViewer.Capture;

public sealed class AtomicCaptureSessionOutputWriter : ICaptureSessionOutputWriter
{
    private readonly string outputRoot;
    private readonly string dataRoot;

    public AtomicCaptureSessionOutputWriter(string outputRoot, string dataRoot)
    {
        this.outputRoot = Path.GetFullPath(outputRoot);
        this.dataRoot = Path.GetFullPath(dataRoot);
        if (!IsWithin(this.outputRoot, this.dataRoot) ||
            Path.GetRelativePath(this.dataRoot, this.outputRoot) == ".")
        {
            throw new ArgumentException("Capture output must be a child directory of data/.", nameof(outputRoot));
        }
    }

    public Task<ICaptureSessionOutputTransaction> BeginAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(dataRoot);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var directoryName = $"session-{startedAtUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        var stagingDirectory = Path.Combine(dataRoot, $".{directoryName}.tmp");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            Directory.CreateDirectory(Path.Combine(stagingDirectory, "frames"));
            return Task.FromResult<ICaptureSessionOutputTransaction>(new Transaction(
                outputRoot,
                directoryName,
                stagingDirectory,
                startedAtUtc));
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
            throw;
        }
    }

    private static bool IsWithin(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private sealed class Transaction(
        string outputRoot,
        string directoryName,
        string stagingDirectory,
        DateTimeOffset startedAtUtc) : ICaptureSessionOutputTransaction
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);
        private readonly List<ManifestRow> rows = [];
        private bool completed;
        private long lastTimestamp = -1;

        public int FrameCount => rows.Count;

        public async Task WriteFrameAsync(
            CapturedFrame frame,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(completed, this);
            Validate(frame);
            if (frame.TimestampMs <= lastTimestamp)
            {
                throw new InvalidDataException("Capture session timestamps must be strictly increasing.");
            }

            var fileName = $"frame-{rows.Count + 1:D6}.png";
            var relativePath = $"frames/{fileName}";
            await File.WriteAllBytesAsync(
                Path.Combine(stagingDirectory, "frames", fileName),
                frame.PngBytes,
                cancellationToken);
            rows.Add(new ManifestRow(relativePath, frame));
            lastTimestamp = frame.TimestampMs;
        }

        public async Task<CaptureSessionOutput> CompleteAsync(
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(completed, this);
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("A capture session without frames cannot be published.");
            }

            var manifestPath = Path.Combine(stagingDirectory, "frame_manifest.csv");
            var metadataPath = Path.Combine(stagingDirectory, "capture_session_metadata.json");
            await File.WriteAllTextAsync(
                manifestPath,
                BuildManifest(rows),
                Utf8NoBom,
                cancellationToken);
            await File.WriteAllTextAsync(
                metadataPath,
                BuildMetadata(rows, startedAtUtc),
                Utf8NoBom,
                cancellationToken);

            var finalDirectory = Path.Combine(outputRoot, directoryName);
            var outputRootCreated = false;
            try
            {
                if (!Directory.Exists(outputRoot))
                {
                    Directory.CreateDirectory(outputRoot);
                    outputRootCreated = true;
                }
                Directory.Move(stagingDirectory, finalDirectory);
                completed = true;
                return new CaptureSessionOutput(
                    finalDirectory,
                    Path.Combine(finalDirectory, "frame_manifest.csv"),
                    Path.Combine(finalDirectory, "capture_session_metadata.json"),
                    rows.Count);
            }
            catch
            {
                if (outputRootCreated && Directory.Exists(outputRoot) &&
                    !Directory.EnumerateFileSystemEntries(outputRoot).Any())
                {
                    Directory.Delete(outputRoot);
                }
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (!completed && Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Capture session staging output could not be removed.", exception);
            }
            completed = true;
            return ValueTask.CompletedTask;
        }

        private static string BuildManifest(IEnumerable<ManifestRow> manifestRows)
        {
            var builder = new StringBuilder(
                "image_path,timestamp_ms,screen_type,capture_source,width,height,captured_at_utc\n");
            foreach (var row in manifestRows)
            {
                builder.Append(Csv(row.ImagePath)).Append(',')
                    .Append(row.Frame.TimestampMs).Append(",unknown,")
                    .Append(Csv(row.Frame.CaptureSource)).Append(',')
                    .Append(row.Frame.Width).Append(',')
                    .Append(row.Frame.Height).Append(',')
                    .Append(row.Frame.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture))
                    .Append('\n');
            }
            return builder.ToString();
        }

        private static string BuildMetadata(
            IReadOnlyList<ManifestRow> manifestRows,
            DateTimeOffset sessionStartedAtUtc)
        {
            var last = manifestRows[^1].Frame;
            return JsonSerializer.Serialize(
                new
                {
                    metadata_schema_version = 1,
                    capture_kind = "continuous_session",
                    manifest_path = "frame_manifest.csv",
                    frame_count = manifestRows.Count,
                    started_at_utc = sessionStartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    stopped_at_utc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    first_timestamp_ms = manifestRows[0].Frame.TimestampMs,
                    last_timestamp_ms = last.TimestampMs,
                    capture_source = last.CaptureSource,
                },
                new JsonSerializerOptions { WriteIndented = true }) + "\n";
        }

        private static string Csv(string value) =>
            $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

        private static void Validate(CapturedFrame frame)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.TimestampMs < 0)
            {
                throw new ArgumentException("Captured frame dimensions and timestamp are invalid.", nameof(frame));
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

        private sealed record ManifestRow(string ImagePath, CapturedFrame Frame);
    }
}
