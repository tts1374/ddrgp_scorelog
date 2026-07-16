using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JacketCatalogCollector;

public sealed record TitleArtistEvaluationMethodReceipt(
    [property: JsonPropertyName("method_version")] string MethodVersion,
    [property: JsonPropertyName("evaluated_count")] int EvaluatedCount,
    [property: JsonPropertyName("known_false_auto_confirm_count")] int KnownFalseAutoConfirmCount,
    [property: JsonPropertyName("adoption_status")] string AdoptionStatus,
    [property: JsonPropertyName("adoption_failure_reasons")] IReadOnlyList<string> AdoptionFailureReasons);

public sealed record TitleArtistEvaluationReceipt(
    [property: JsonPropertyName("receipt_schema_version")] string ReceiptSchemaVersion,
    [property: JsonPropertyName("report_directory")] string ReportDirectory,
    [property: JsonPropertyName("adopted_methods")] IReadOnlyList<string> AdoptedMethods,
    [property: JsonPropertyName("methods")] IReadOnlyList<TitleArtistEvaluationMethodReceipt> Methods)
{
    public const string CurrentSchemaVersion = "m5c-title-artist-evaluation-receipt-v1";
}

public interface ITitleArtistEvaluationService
{
    Task<TitleArtistEvaluationReceipt> RunAsync(
        string datasetPath,
        string artifactRoot,
        string masterPath,
        string catalogPath,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class TitleArtistEvaluationService(
    IProcessRunner processRunner,
    string repositoryRoot,
    string pythonExecutable = "python") : ITitleArtistEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly string root = Path.GetFullPath(repositoryRoot);

    public async Task<TitleArtistEvaluationReceipt> RunAsync(
        string datasetPath,
        string artifactRoot,
        string masterPath,
        string catalogPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var dataset = RequireDataPath(datasetPath, "evaluation dataset", requireFile: true);
        var artifacts = RequireDataPath(artifactRoot, "artifact root", requireDirectory: true);
        var output = RequireDataPath(outputDirectory, "evaluation output");
        var master = RequireFile(masterPath, "master DB");
        var catalog = RequireFile(catalogPath, "catalog DB");
        var request = new ProcessRequest(
            pythonExecutable,
            [
                "-X", "utf8", "-m", "tools.vision_poc.title_artist_evaluation",
                "--dataset", dataset,
                "--artifact-root", artifacts,
                "--master-db", master,
                "--catalog", catalog,
                "--output-dir", output,
            ],
            root);
        var result = await processRunner.RunAsync(request, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"title/artist evaluation failed: {Diagnostic(result.StandardError)}");
        }
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException("title/artist evaluation returned empty stdout");
        }
        TitleArtistEvaluationReceipt receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<TitleArtistEvaluationReceipt>(
                result.StandardOutput,
                JsonOptions) ?? throw new InvalidOperationException(
                    "title/artist evaluation receipt is empty");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "title/artist evaluation returned invalid JSON", exception);
        }
        ValidateReceipt(receipt, output);
        return receipt;
    }

    private static void ValidateReceipt(TitleArtistEvaluationReceipt receipt, string output)
    {
        if (receipt.ReceiptSchemaVersion != TitleArtistEvaluationReceipt.CurrentSchemaVersion
            || receipt.Methods is null
            || receipt.AdoptedMethods is null
            || receipt.Methods.Count == 0
            || receipt.Methods.Any(method =>
                string.IsNullOrWhiteSpace(method.MethodVersion)
                || method.EvaluatedCount < 0
                || method.KnownFalseAutoConfirmCount < 0
                || method.AdoptionStatus is not ("adopted" or "not_adopted")
                || method.AdoptionFailureReasons is null)
            || receipt.Methods.Select(method => method.MethodVersion)
                .Distinct(StringComparer.Ordinal).Count() != receipt.Methods.Count
            || receipt.AdoptedMethods.Distinct(StringComparer.Ordinal).Count()
                != receipt.AdoptedMethods.Count
            || receipt.Methods.Any(method => method.AdoptionFailureReasons.Any(
                string.IsNullOrWhiteSpace))
            || receipt.AdoptedMethods.Any(method =>
                receipt.Methods.All(candidate => candidate.MethodVersion != method
                    || candidate.AdoptionStatus != "adopted"))
            || receipt.Methods.Any(method => method.AdoptionStatus == "adopted"
                && !receipt.AdoptedMethods.Contains(method.MethodVersion, StringComparer.Ordinal))
            || !string.Equals(
                Path.GetFullPath(receipt.ReportDirectory),
                Path.GetFullPath(output),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("title/artist evaluation receipt is invalid");
        }
    }

    private string RequireDataPath(
        string path,
        string label,
        bool requireFile = false,
        bool requireDirectory = false)
    {
        var fullPath = Path.GetFullPath(path);
        var dataRoot = Path.GetFullPath(Path.Combine(root, "data"));
        if (!fullPath.StartsWith(dataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{label} must be under the repository data directory");
        }
        if (requireFile && !File.Exists(fullPath))
        {
            throw new InvalidOperationException($"{label} does not exist");
        }
        if (requireDirectory && !Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"{label} does not exist");
        }
        return fullPath;
    }

    private static string RequireFile(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"{label} does not exist");
        }
        return fullPath;
    }

    private static string Diagnostic(string value) => string.IsNullOrWhiteSpace(value)
        ? "no diagnostic was returned"
        : value.Trim();
}
