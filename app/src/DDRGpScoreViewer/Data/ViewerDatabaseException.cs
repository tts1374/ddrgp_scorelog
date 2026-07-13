namespace DDRGpScoreViewer.Data;

public sealed class ViewerDatabaseException(string userMessage, Exception? innerException = null)
    : Exception(userMessage, innerException)
{
    public string UserMessage { get; } = userMessage;
}
