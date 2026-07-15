using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

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
    string Note);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewMutationReceipt(
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("reference_id")] string ReferenceId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("song_id")] string? SongId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("idempotent")] bool Idempotent);

public interface IReviewWorkflowService
{
    Task MigrateAsync(string sourcePath, string targetPath, CancellationToken cancellationToken);
    Task<ReviewMutationReceipt> ApplyAsync(
        string masterPath,
        string catalogPath,
        ReviewMutation mutation,
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

    public async Task MigrateAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            [
                "migrate-v2",
                "--source-catalog", Path.GetFullPath(sourcePath),
                "--target-catalog", Path.GetFullPath(targetPath),
            ],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Catalog migration failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
        using var document = JsonDocument.Parse(result.StandardOutput);
        if (document.RootElement.GetProperty("status").GetString() != "migrated"
            || document.RootElement.GetProperty("schema_version").GetInt32() != 2)
        {
            throw new InvalidOperationException("Catalog migration receipt is invalid.");
        }
    }

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
