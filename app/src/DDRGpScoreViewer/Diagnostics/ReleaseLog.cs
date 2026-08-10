using System.IO;
using System.Text;
using System.Text.Json;
using DDRGpScoreViewer.Data;

namespace DDRGpScoreViewer.Diagnostics;

public sealed class ReleaseLog : IDisposable
{
    public const long DefaultMaximumBytes = 5L * 1024 * 1024;
    public const int DefaultFileCount = 3;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly object sync = new();
    private readonly string logPath;
    private readonly long maximumBytes;
    private readonly int fileCount;
    private bool disposed;

    public ReleaseLog(string logsDirectory, long maximumBytes = DefaultMaximumBytes, int fileCount = DefaultFileCount)
    {
        if (maximumBytes <= 0 || fileCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
        Directory.CreateDirectory(logsDirectory);
        logPath = Path.Combine(logsDirectory, "gp-score-log.log");
        this.maximumBytes = maximumBytes;
        this.fileCount = fileCount;
    }

    public void Information(string eventName, string message) => Write("INFO", eventName, message);

    public void Error(string eventName, Exception exception) =>
        Write("ERROR", eventName, $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");

    public void LevelRecognition(CaptureSaveEventResult eventResult)
    {
        var level = eventResult.LevelRecognition;
        if (level is null)
        {
            return;
        }

        var message = JsonSerializer.Serialize(new
        {
            event_id = eventResult.EventId,
            save_status = eventResult.Status,
            event_reasons = eventResult.Reasons,
            level = new
            {
                field = level.FieldName,
                roi = level.RoiName,
                status = level.Status,
                recognized_digits = level.RecognizedDigits,
                best_candidate = level.BestCandidate,
                best_candidate_distance = level.Distance,
                next_best_candidate = level.NextBestCandidate,
                candidate_margin = level.CandidateMargin,
                distance_threshold = level.DistanceThreshold,
                candidate_margin_threshold = level.CandidateMarginThreshold,
                reason = level.FailureReason,
                per_digit_distances = level.PerDigitDistances,
            },
        });
        Information("level_recognition", message);
    }

#if DEBUG
    public void Debug(string eventName, string message) => Write("DEBUG", eventName, message);
#endif

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
        }
    }

    private void Write(string level, string eventName, string message)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            try
            {
                RotateIfNeeded();
                var line = $"{DateTimeOffset.UtcNow:O}\t{level}\t{eventName}\t{message.Replace("\r", string.Empty).Replace("\n", " | ")}\n";
                File.AppendAllText(logPath, line, Utf8NoBom);
            }
            catch (IOException)
            {
                // Diagnostic output must not affect the formal save boundary.
            }
            catch (UnauthorizedAccessException)
            {
                // Diagnostic output must not affect the formal save boundary.
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(logPath) || new FileInfo(logPath).Length < maximumBytes)
        {
            return;
        }
        for (var index = fileCount - 1; index >= 1; index--)
        {
            var destination = RotatedPath(index);
            var source = index == 1 ? logPath : RotatedPath(index - 1);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
    }

    private string RotatedPath(int index) => Path.Combine(
        Path.GetDirectoryName(logPath)!,
        $"gp-score-log.{index}.log");
}

public static class TemporaryDataCleanup
{
    public static void Cleanup(ViewerDatabasePaths paths)
    {
        foreach (var directoryName in new[] { "cache", "temp" })
        {
            var path = Path.Combine(paths.DataDirectory, directoryName);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
