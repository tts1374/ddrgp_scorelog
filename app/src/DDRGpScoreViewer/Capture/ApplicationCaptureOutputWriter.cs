using System.IO;
using DDRGpScoreViewer.Data;

namespace DDRGpScoreViewer.Capture;

public sealed class ApplicationCaptureOutputWriter : ICaptureOutputWriter
{
    private readonly Func<ViewerDatabasePaths> pathsResolver;

    public ApplicationCaptureOutputWriter()
        : this(ViewerDatabasePaths.ResolveDefault)
    {
    }

    public ApplicationCaptureOutputWriter(Func<ViewerDatabasePaths> pathsResolver)
    {
        this.pathsResolver = pathsResolver;
    }

    public Task<CaptureOutput> WriteAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        ViewerDatabasePaths paths;
        try
        {
            paths = pathsResolver();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            throw new IOException(
                "Application data directory could not be resolved for capture output.",
                exception);
        }

        var writer = new AtomicCaptureOutputWriter(
            Path.Combine(paths.DataDirectory, "windows_capture"),
            paths.DataDirectory);
        return writer.WriteAsync(frame, cancellationToken);
    }
}
