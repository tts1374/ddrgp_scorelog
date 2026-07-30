using System.IO;

namespace DDRGpScoreViewer.Runtime;

public sealed class RuntimeResourceException : InvalidOperationException
{
    public RuntimeResourceException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Resolves resources owned by the installed application package.
/// Repository paths are deliberately not part of this resolver's search contract.
/// </summary>
public sealed class AppRuntimeResourceResolver
{
    public const string ExplicitDataRootEnvironmentVariable =
        "DDRGP_SCORE_VIEWER_RUNTIME_DATA";

    private readonly string packageRoot;
    private readonly string? explicitDataRoot;

    public AppRuntimeResourceResolver(
        string? packageRoot = null,
        string? explicitDataRoot = null)
    {
        this.packageRoot = Path.GetFullPath(packageRoot ?? AppContext.BaseDirectory);
        this.explicitDataRoot = FullPathOrNull(
            explicitDataRoot ?? Environment.GetEnvironmentVariable(ExplicitDataRootEnvironmentVariable));
    }

    public string PackageRoot => packageRoot;

    public string? ExplicitDataRoot => explicitDataRoot;

    public string ResolveDigitTemplatesDirectory()
    {
        foreach (var candidate in CandidateRoots("digit_templates"))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new RuntimeResourceException(
            "Required app runtime resource 'digit_templates' was not found. " +
            $"Place it under the application package or set {ExplicitDataRootEnvironmentVariable} " +
            "to an explicit runtime data directory.");
    }

    public string ResolveRequiredFile(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        foreach (var root in CandidateRoots())
        {
            var candidate = Path.Combine(root, normalized);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new RuntimeResourceException(
            $"Required app runtime resource '{relativePath}' was not found in the " +
            "application package or the explicit runtime data directory.");
    }

    private IEnumerable<string> CandidateRoots(string? child = null)
    {
        var roots = new List<string>();
        if (explicitDataRoot is not null)
        {
            roots.Add(explicitDataRoot);
        }

        roots.Add(Path.Combine(packageRoot, "RuntimeAssets"));
        roots.Add(Path.Combine(packageRoot, "runtime"));

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return child is null ? root : Path.Combine(root, child);
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException(
                "A non-empty relative runtime resource path is required.",
                nameof(relativePath));
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine("runtime-root", normalized));
        var relative = Path.GetRelativePath("runtime-root", full);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new ArgumentException(
                "Runtime resource paths must not escape the runtime data directory.",
                nameof(relativePath));
        }

        return relative;
    }

    private static string? FullPathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
