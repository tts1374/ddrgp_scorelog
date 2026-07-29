using System.IO;
using DDRGpScoreViewer.Data;

namespace DDRGpScoreViewer.Capture;

public sealed class ApplicationCaptureSessionOutputWriter : ICaptureSessionOutputWriter
{
    private readonly Func<ViewerDatabasePaths> pathsResolver;

    public ApplicationCaptureSessionOutputWriter()
        : this(ViewerDatabasePaths.ResolveDefault)
    {
    }

    public ApplicationCaptureSessionOutputWriter(Func<ViewerDatabasePaths> pathsResolver)
    {
        this.pathsResolver = pathsResolver;
    }

    public Task<ICaptureSessionOutputTransaction> BeginAsync(
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

        return new AtomicCaptureSessionOutputWriter(
            Path.Combine(paths.DataDirectory, "windows_capture"),
            paths.DataDirectory).BeginAsync(cancellationToken);
    }
}
