using System.IO;
using System.Text;
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
            RotateIfNeeded();
            var line = $"{DateTimeOffset.UtcNow:O}\t{level}\t{eventName}\t{message.Replace("\r", string.Empty).Replace("\n", " | ")}\n";
            File.AppendAllText(logPath, line, Utf8NoBom);
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
