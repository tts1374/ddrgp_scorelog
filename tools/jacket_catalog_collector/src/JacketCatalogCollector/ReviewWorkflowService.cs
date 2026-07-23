using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Text;

namespace JacketCatalogCollector;

public sealed record ReviewMutation(
    string ActionId,
    string ReferenceId,
    string Action,
    int ExpectedRevision,
    string ExpectedStatus,
    string? ExpectedSongId,
    string? SongId,
    string Reason,
    string Note,
    string? ExpectedNote = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewMutationReceipt(
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("reference_id")] string ReferenceId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("song_id")] string? SongId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("idempotent")] bool Idempotent);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewMutationBatchReceipt(
    [property: JsonPropertyName("requested_count")] int RequestedCount,
    [property: JsonPropertyName("applied_count")] int AppliedCount,
    [property: JsonPropertyName("no_op_count")] int NoOpCount,
    [property: JsonPropertyName("receipts")] List<ReviewMutationReceipt> Receipts);

public sealed class ReviewBatchPostCommitException : InvalidOperationException
{
    public ReviewBatchPostCommitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IReviewWorkflowService
{
    Task<ReviewMutationReceipt> ApplyAsync(
        string masterPath,
        string catalogPath,
        ReviewMutation mutation,
        CancellationToken cancellationToken);

    Task<ReviewMutationBatchReceipt> ApplyBatchAsync(
        string masterPath,
        string catalogPath,
        IReadOnlyCollection<ReviewMutation> mutations,
        CancellationToken cancellationToken);
}

public sealed class ReviewWorkflowService(
    IProcessRunner processRunner,
    string repositoryRoot,
    string pythonExecutable = "python") : IReviewWorkflowService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<ReviewMutationReceipt> ApplyAsync(
        string masterPath,
        string catalogPath,
        ReviewMutation mutation,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "review",
            "--catalog", Path.GetFullPath(catalogPath),
            "--master-db", Path.GetFullPath(masterPath),
            "--action-id", mutation.ActionId,
            "--reference-id", mutation.ReferenceId,
            "--action", mutation.Action,
            "--expected-revision", mutation.ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--expected-status", mutation.ExpectedStatus,
            "--reason", mutation.Reason,
            "--note", mutation.Note,
        };
        if (mutation.ExpectedSongId is not null)
        {
            arguments.AddRange(["--expected-song-id", mutation.ExpectedSongId]);
        }
        if (mutation.SongId is not null)
        {
            arguments.AddRange(["--song-id", mutation.SongId]);
        }
        var result = await RunAsync(arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Catalog review failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
        try
        {
            return JsonSerializer.Deserialize<ReviewMutationReceipt>(result.StandardOutput, Options)
                ?? throw new InvalidOperationException("Catalog review receipt is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Catalog review receipt is invalid.", exception);
        }
    }

    public async Task<ReviewMutationBatchReceipt> ApplyBatchAsync(
        string masterPath,
        string catalogPath,
        IReadOnlyCollection<ReviewMutation> mutations,
        CancellationToken cancellationToken)
    {
        if (mutations.Count == 0)
        {
            return new ReviewMutationBatchReceipt(0, 0, 0, []);
        }

        var temporaryPath = Path.Combine(
            Path.GetTempPath(), $"ddrgp-review-batch-{Guid.NewGuid():N}.json");
        ProcessResult? processResult = null;
        try
        {
            var document = new ReviewMutationBatchDocument
            {
                Requests = mutations.Select(ToBatchRequest).ToList(),
            };
            var json = JsonSerializer.Serialize(document, Options);
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            var arguments = new List<string>
            {
                "review-batch",
                "--catalog", Path.GetFullPath(catalogPath),
                "--master-db", Path.GetFullPath(masterPath),
                "--request-file", temporaryPath,
            };
            processResult = await RunAsync(arguments, cancellationToken);
            if (processResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Catalog review batch failed (exit {processResult.ExitCode}): "
                    + processResult.StandardError.Trim());
            }
            try
            {
                return JsonSerializer.Deserialize<ReviewMutationBatchReceipt>(
                        processResult.StandardOutput, Options)
                    ?? throw new InvalidOperationException("Catalog review batch receipt is null.");
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException)
            {
                throw new ReviewBatchPostCommitException(
                    "Catalog review batch receipt is invalid after the catalog transaction "
                    + "committed.", exception);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (processResult?.ExitCode == 0)
                {
                    throw new ReviewBatchPostCommitException(
                        "Catalog review batch committed, but temporary request cleanup failed.",
                        exception);
                }
                throw;
            }
        }
    }

    private static ReviewMutationBatchRequest ToBatchRequest(ReviewMutation mutation) => new()
    {
        ActionId = mutation.ActionId,
        ReferenceId = mutation.ReferenceId,
        Action = mutation.Action,
        ExpectedRevision = mutation.ExpectedRevision,
        ExpectedStatus = mutation.ExpectedStatus,
        ExpectedSongId = mutation.ExpectedSongId,
        ExpectedNote = mutation.ExpectedNote,
        SongId = mutation.SongId,
        Reason = mutation.Reason,
        Note = mutation.Note,
    };

    private Task<ProcessResult> RunAsync(
        IReadOnlyList<string> commandArguments,
        CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                ["-X", "utf8", "-m", "tools.vision_poc.jacket_reference_catalog", .. commandArguments],
                repositoryRoot),
            cancellationToken);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class ReviewMutationBatchDocument
{
    [JsonPropertyName("requests")]
    public required List<ReviewMutationBatchRequest> Requests { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class ReviewMutationBatchRequest
{
    [JsonPropertyName("action_id")]
    public required string ActionId { get; init; }
    [JsonPropertyName("reference_id")]
    public required string ReferenceId { get; init; }
    [JsonPropertyName("action")]
    public required string Action { get; init; }
    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
    [JsonPropertyName("expected_status")]
    public required string ExpectedStatus { get; init; }
    [JsonPropertyName("expected_song_id")]
    public string? ExpectedSongId { get; init; }
    [JsonPropertyName("expected_note")]
    public string? ExpectedNote { get; init; }
    [JsonPropertyName("song_id")]
    public string? SongId { get; init; }
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
    [JsonPropertyName("note")]
    public required string Note { get; init; }
}
