using System.IO;

namespace DDRGpScoreViewer.Capture;

public sealed class RepositoryCaptureOutputWriter : ICaptureOutputWriter
{
    private readonly Func<string> repositoryRootResolver;

    public RepositoryCaptureOutputWriter()
        : this(RepositoryRootLocator.Find)
    {
    }

    public RepositoryCaptureOutputWriter(Func<string> repositoryRootResolver)
    {
        this.repositoryRootResolver = repositoryRootResolver;
    }

    public Task<CaptureOutput> WriteAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        string repositoryRoot;
        try
        {
            repositoryRoot = Path.GetFullPath(repositoryRootResolver());
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new IOException(
                "Repository data directory could not be resolved for capture output.",
                exception);
        }

        var dataRoot = Path.Combine(repositoryRoot, "data");
        var writer = new AtomicCaptureOutputWriter(
            Path.Combine(dataRoot, "windows_capture"),
            dataRoot);
        return writer.WriteAsync(frame, cancellationToken);
    }
}
