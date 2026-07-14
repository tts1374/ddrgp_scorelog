using System.Text.Json;
using System.IO;

namespace JacketCatalogCollector;

public sealed record MasterSummary(
    string MasterVersion,
    string SourceHash,
    int SongCount,
    int ChartCount,
    int GrandPrixSongCount);

public sealed record MasterUpdateResult(MasterSummary? Before, MasterSummary After);

public interface IMasterUpdateService
{
    Task<MasterUpdateResult> UpdateAsync(string targetPath, CancellationToken cancellationToken);
}

public interface IMasterPublisher
{
    void Publish(string stagedPath, string targetPath);
}

public sealed class AtomicMasterPublisher : IMasterPublisher
{
    public void Publish(string stagedPath, string targetPath)
    {
        var publishPath = targetPath + ".publish-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(stagedPath, publishPath, overwrite: false);
            File.Move(publishPath, targetPath, overwrite: true);
        }
        finally
        {
            File.Delete(publishPath);
        }
    }
}

public sealed class MasterUpdateService(
    IProcessRunner processRunner,
    IMasterPublisher publisher,
    string repositoryRoot,
    string pythonExecutable = "python") : IMasterUpdateService
{
    public async Task<MasterUpdateResult> UpdateAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        if (Directory.Exists(fullTarget))
        {
            throw new InvalidOperationException("Master target must not be a directory.");
        }

        MasterSummary? before = null;
        if (File.Exists(fullTarget) && new FileInfo(fullTarget).Length > 0)
        {
            before = await InspectAsync(fullTarget, cancellationToken);
        }

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            "ddrgp-jacket-collector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var stagedPath = Path.Combine(stagingDirectory, "master.sqlite");
        var targetParent = Path.GetDirectoryName(fullTarget)
            ?? throw new InvalidOperationException("Master target must have a parent directory.");
        var existingParent = FindExistingParent(targetParent);
        try
        {
            var buildResult = await processRunner.RunAsync(
                new ProcessRequest(
                    pythonExecutable,
                    ["-X", "utf8", "-m", "master", "--output", stagedPath],
                    repositoryRoot),
                cancellationToken);
            EnsureSuccess(buildResult, "Master build");
            if (!File.Exists(stagedPath))
            {
                throw new InvalidOperationException("Master build did not create the staging database.");
            }

            var after = await InspectAsync(stagedPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(targetParent);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                publisher.Publish(stagedPath, fullTarget);
            }
            catch
            {
                RemoveNewEmptyParents(targetParent, existingParent);
                throw;
            }
            return new MasterUpdateResult(before, after);
        }
        finally
        {
            Directory.Delete(stagingDirectory, recursive: true);
            RemoveNewEmptyParents(targetParent, existingParent);
        }
    }

    private async Task<MasterSummary> InspectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                pythonExecutable,
                ["-X", "utf8", "-m", "master.inspect", path],
                repositoryRoot),
            cancellationToken);
        EnsureSuccess(result, "Master inspection");
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            return new MasterSummary(
                RequiredString(root, "master_version"),
                RequiredString(root, "source_hash"),
                root.GetProperty("song_count").GetInt32(),
                root.GetProperty("chart_count").GetInt32(),
                ParseRequiredCount(root, "grand_prix_play_available_song_count"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException("Master inspection returned invalid JSON.", exception);
        }
    }

    private static int ParseRequiredCount(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Master inspection field is invalid: {name}"),
        };
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = root.GetProperty(name).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Master inspection field is empty: {name}")
            : value;
    }

    private static void EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{operation} failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }
    }

    private static string FindExistingParent(string parent)
    {
        var candidate = parent;
        while (!Directory.Exists(candidate))
        {
            candidate = Path.GetDirectoryName(candidate)
                ?? throw new InvalidOperationException("Master target has no existing ancestor.");
        }
        return candidate;
    }

    private static void RemoveNewEmptyParents(string parent, string existingParent)
    {
        var candidate = parent;
        while (!Path.GetFullPath(candidate).Equals(
                   Path.GetFullPath(existingParent),
                   StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(candidate)
               && !Directory.EnumerateFileSystemEntries(candidate).Any())
        {
            Directory.Delete(candidate);
            candidate = Path.GetDirectoryName(candidate)
                ?? throw new InvalidOperationException("Master target parent chain is invalid.");
        }
    }
}
