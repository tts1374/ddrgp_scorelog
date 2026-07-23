using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CollectionAutoConfirmationReceipt
{
    [JsonPropertyName("collection_auto_confirmation_schema_version")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("requested_count")]
    public required int RequestedCount { get; init; }

    [JsonPropertyName("applied_count")]
    public required int AppliedCount { get; init; }

    [JsonPropertyName("no_op_count")]
    public required int NoOpCount { get; init; }

    [JsonPropertyName("auto_confirmed_count")]
    public required int AutoConfirmedCount { get; init; }

    [JsonPropertyName("remaining_count")]
    public required int RemainingCount { get; init; }
}

public interface ICollectionAutoConfirmationService
{
    Task<CollectionAutoConfirmationReceipt> ApplyAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class PythonCollectionAutoConfirmationService(
    IProcessRunner processRunner,
    string repositoryRoot,
    string artifactRoot,
    string masterPath,
    string catalogPath,
    string? snapshotRootPath = null,
    string pythonExecutable = "python") : ICollectionAutoConfirmationService
{
    private const string SchemaVersion = "m5c-collection-end-auto-confirmation-v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<CollectionAutoConfirmationReceipt> ApplyAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("collection auto-confirmation session id is empty.");
        }
        var arguments = new List<string>
        {
            "-X", "utf8", "-m", "tools.vision_poc.collection_auto_confirmation",
            "--catalog", Path.GetFullPath(catalogPath),
            "--master-db", Path.GetFullPath(masterPath),
            "--artifact-root", Path.GetFullPath(artifactRoot),
            "--session-id", sessionId,
        };
        if (!string.IsNullOrWhiteSpace(snapshotRootPath))
        {
            arguments.Add("--snapshot-root");
            arguments.Add(Path.GetFullPath(snapshotRootPath));
        }
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                arguments,
                Path.GetFullPath(repositoryRoot)),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Collection auto-confirmation failed (exit {result.ExitCode}): "
                + result.StandardError.Trim());
        }
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException("Collection auto-confirmation stdout is empty.");
        }
        CollectionAutoConfirmationReceipt? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<CollectionAutoConfirmationReceipt>(
                result.StandardOutput,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Collection auto-confirmation stdout is invalid JSON.",
                exception);
        }
        if (receipt is null
            || receipt.SchemaVersion != SchemaVersion
            || receipt.SessionId != sessionId
            || receipt.RequestedCount < 0
            || receipt.AppliedCount < 0
            || receipt.NoOpCount < 0
            || receipt.AutoConfirmedCount < 0
            || receipt.RemainingCount < 0
            || receipt.AppliedCount + receipt.NoOpCount > receipt.RequestedCount)
        {
            throw new InvalidOperationException(
                "Collection auto-confirmation receipt is invalid or does not match the session.");
        }
        return receipt;
    }
}
