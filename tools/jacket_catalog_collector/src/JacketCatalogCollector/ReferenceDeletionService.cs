using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace JacketCatalogCollector;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenceDeletionReceipt(
    [property: JsonPropertyName("reference_id")] string ReferenceId,
    [property: JsonPropertyName("deleted")] bool Deleted,
    [property: JsonPropertyName("song_id")] string SongId,
    [property: JsonPropertyName("review_status")] string ReviewStatus,
    [property: JsonPropertyName("revision")] int Revision);

public interface IReferenceDeletionService
{
    Task<ReferenceDeletionReceipt> DeleteAsync(
        string catalogPath,
        string referenceId,
        int expectedRevision,
        string expectedStatus,
        string? expectedSongId,
        CancellationToken cancellationToken);
}

public sealed class ReferenceDeletionService(
    IProcessRunner processRunner,
    string repositoryRoot,
    string pythonExecutable = "python") : IReferenceDeletionService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<ReferenceDeletionReceipt> DeleteAsync(
        string catalogPath,
        string referenceId,
        int expectedRevision,
        string expectedStatus,
        string? expectedSongId,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "delete-reference",
            "--catalog", Path.GetFullPath(catalogPath),
            "--reference-id", referenceId,
            "--expected-revision", expectedRevision.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            "--expected-status", expectedStatus,
        };
        if (expectedSongId is not null)
        {
            arguments.AddRange(["--expected-song-id", expectedSongId]);
        }

        var result = await processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                ["-X", "utf8", "-m", "tools.vision_poc.jacket_reference_catalog", .. arguments],
                repositoryRoot),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Catalog reference deletion failed (exit {result.ExitCode}): "
                + result.StandardError.Trim());
        }
        try
        {
            return JsonSerializer.Deserialize<ReferenceDeletionReceipt>(
                       result.StandardOutput,
                       Options)
                   ?? throw new InvalidOperationException(
                       "Catalog reference deletion receipt is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Catalog reference deletion receipt is invalid.", exception);
        }
    }
}
