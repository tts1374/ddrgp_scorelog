using System.IO;

namespace DDRGpScoreViewer.Capture;

public sealed class RepositoryCaptureSessionOutputWriter : ICaptureSessionOutputWriter
{
    private readonly Func<string> repositoryRootResolver;

    public RepositoryCaptureSessionOutputWriter()
        : this(RepositoryRootLocator.Find)
    {
    }

    public RepositoryCaptureSessionOutputWriter(Func<string> repositoryRootResolver)
    {
        this.repositoryRootResolver = repositoryRootResolver;
    }

    public Task<ICaptureSessionOutputTransaction> BeginAsync(
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
        return new AtomicCaptureSessionOutputWriter(
            Path.Combine(dataRoot, "windows_capture"),
            dataRoot).BeginAsync(cancellationToken);
    }
}
