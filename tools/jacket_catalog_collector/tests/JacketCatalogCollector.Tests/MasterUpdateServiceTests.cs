using System.Security.Cryptography;
using System.Text;

namespace JacketCatalogCollector.Tests;

public sealed class MasterUpdateServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "collector-tests-" + Guid.NewGuid().ToString("N"));

    public MasterUpdateServiceTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task PublishesOnlyAnInspectedStagingDatabaseAndCleansTemporaryFiles()
    {
        var target = Path.Combine(root, "new-parent", "master.sqlite");
        var runner = SuccessfulRunner();
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal("master-v2", result.After.MasterVersion);
        Assert.Equal("staged-master", File.ReadAllText(target));
        Assert.Equal(2, runner.Requests.Count);
        Assert.All(
            runner.Requests.SelectMany(request => request.Arguments)
                .Where(argument => argument.Contains("ddrgp-jacket-collector-", StringComparison.Ordinal)),
            path => Assert.False(Directory.Exists(Path.GetDirectoryName(path))));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(target)!, "*.publish-*"));
    }

    [Fact]
    public async Task ReplacesExplicitZeroBytePlaceholderOnlyAfterInspection()
    {
        var target = Path.Combine(root, "placeholder.sqlite");
        File.WriteAllBytes(target, []);
        var runner = SuccessfulRunner();
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal("staged-master", File.ReadAllText(target));
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task ReplacesCompatibleExistingTargetAfterSuccessfulBuildAndInspection()
    {
        var target = Path.Combine(root, "existing.sqlite");
        File.WriteAllText(target, "existing-master");
        var runner = SuccessfulRunner(includeExistingInspection: true);
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        var result = await service.UpdateAsync(target, CancellationToken.None);

        Assert.Equal("master-v1", result.Before!.MasterVersion);
        Assert.Equal("master-v2", result.After.MasterVersion);
        Assert.Equal("staged-master", File.ReadAllText(target));
        Assert.Equal(3, runner.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildFailurePreservesMissingOrExistingTarget(bool existingZeroByte)
    {
        var target = Path.Combine(root, Guid.NewGuid().ToString("N"), "master.sqlite");
        if (existingZeroByte)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, []);
        }
        var runner = new StubProcessRunner((_, _) => Task.FromResult(new ProcessResult(1, "", "download failed")));
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(target, CancellationToken.None));

        Assert.Equal(existingZeroByte, File.Exists(target));
        if (existingZeroByte)
        {
            Assert.Equal(0, new FileInfo(target).Length);
        }
        else
        {
            Assert.False(Directory.Exists(Path.GetDirectoryName(target)!));
        }
    }

    [Fact]
    public async Task IncompatibleExistingFileIsRejectedBeforeBuild()
    {
        var target = Path.Combine(root, "invalid.sqlite");
        File.WriteAllText(target, "not sqlite");
        var before = Hash(target);
        var runner = new StubProcessRunner((request, _) => Task.FromResult(
            request.Arguments.Contains("master.inspect")
                ? new ProcessResult(1, "", "invalid schema")
                : throw new Xunit.Sdk.XunitException("build must not start")));
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(target, CancellationToken.None));

        Assert.Equal(before, Hash(target));
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task CompatibleExistingTargetSurvivesBuildFailure()
    {
        var target = Path.Combine(root, "compatible.sqlite");
        File.WriteAllText(target, "existing-master");
        var before = Hash(target);
        var call = 0;
        var runner = new StubProcessRunner((_, _) => Task.FromResult(
            ++call == 1
                ? new ProcessResult(0, SummaryJson("master-v1"), "")
                : new ProcessResult(1, "", "build failed")));
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(target, CancellationToken.None));

        Assert.Equal(before, Hash(target));
        Assert.Equal(2, runner.Requests.Count);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("zero")]
    [InlineData("existing")]
    public async Task StagingInspectionFailurePreservesEveryTargetState(string state)
    {
        var target = Path.Combine(root, state, "master.sqlite");
        string? before = null;
        if (state != "new")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, state == "zero" ? "" : "existing-master");
            before = Hash(target);
        }
        var inspectionCount = 0;
        var runner = new StubProcessRunner((request, _) =>
        {
            if (request.Arguments.Contains("master.inspect"))
            {
                inspectionCount++;
                if (state == "existing" && inspectionCount == 1)
                {
                    return Task.FromResult(new ProcessResult(0, SummaryJson("master-v1"), ""));
                }
                return Task.FromResult(new ProcessResult(1, "", "inspection failed"));
            }
            File.WriteAllText(request.Arguments[^1], "staged-master");
            return Task.FromResult(new ProcessResult(0, "built", ""));
        });
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(target, CancellationToken.None));

        if (state == "new")
        {
            Assert.False(File.Exists(target));
            Assert.False(Directory.Exists(Path.GetDirectoryName(target)!));
        }
        else
        {
            Assert.Equal(before, Hash(target));
        }
    }

    [Fact]
    public async Task BuildCancellationLeavesNewTargetAndParentAbsent()
    {
        var target = Path.Combine(root, "cancel", "master.sqlite");
        var runner = new StubProcessRunner((_, _) => Task.FromException<ProcessResult>(
            new OperationCanceledException()));
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.UpdateAsync(target, CancellationToken.None));

        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(Path.GetDirectoryName(target)!));
    }

    [Fact]
    public async Task InspectionCancellationDoesNotPublishOrChangeExistingTarget()
    {
        var target = Path.Combine(root, "master.sqlite");
        File.WriteAllText(target, "existing-master");
        var before = Hash(target);
        var call = 0;
        var runner = new StubProcessRunner((request, _) =>
        {
            call++;
            if (call == 1)
            {
                return Task.FromResult(new ProcessResult(0, SummaryJson("master-v1"), ""));
            }
            if (request.Arguments.Contains("master"))
            {
                var output = request.Arguments[^1];
                File.WriteAllText(output, "staged-master");
                return Task.FromResult(new ProcessResult(0, "built", ""));
            }
            throw new OperationCanceledException();
        });
        var service = new MasterUpdateService(runner, new AtomicMasterPublisher(), root);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.UpdateAsync(target, CancellationToken.None));

        Assert.Equal(before, Hash(target));
    }

    [Fact]
    public async Task PublishFailurePreservesExistingTargetAndCleansPublishFile()
    {
        var target = Path.Combine(root, "master.sqlite");
        File.WriteAllText(target, "existing-master");
        var before = Hash(target);
        var service = new MasterUpdateService(
            SuccessfulRunner(includeExistingInspection: true),
            new FailingPublisher(),
            root);

        await Assert.ThrowsAsync<IOException>(() => service.UpdateAsync(target, CancellationToken.None));

        Assert.Equal(before, Hash(target));
        Assert.Empty(Directory.EnumerateFiles(root, "*.publish-*"));
    }

    [Fact]
    public async Task PublishFailureRemovesEntireNewParentChain()
    {
        var target = Path.Combine(root, "new", "nested", "master.sqlite");
        var service = new MasterUpdateService(SuccessfulRunner(), new FailingPublisher(), root);

        await Assert.ThrowsAsync<IOException>(() => service.UpdateAsync(target, CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(root, "new")));
        Assert.False(File.Exists(target));
    }

    private StubProcessRunner SuccessfulRunner(bool includeExistingInspection = false)
    {
        var inspectionCount = 0;
        return new StubProcessRunner((request, _) =>
        {
            if (request.Arguments.Contains("master.inspect"))
            {
                inspectionCount++;
                return Task.FromResult(new ProcessResult(
                    0,
                    SummaryJson(includeExistingInspection && inspectionCount == 1 ? "master-v1" : "master-v2"),
                    ""));
            }
            var output = request.Arguments[^1];
            File.WriteAllText(output, "staged-master");
            return Task.FromResult(new ProcessResult(0, "built", ""));
        });
    }

    private static string SummaryJson(string version) => $$"""
        {"master_version":"{{version}}","source_hash":"hash-{{version}}","song_count":10,"chart_count":20,"grand_prix_play_available_song_count":"8"}
        """;

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class FailingPublisher : IMasterPublisher
    {
        public void Publish(string stagedPath, string targetPath) => throw new IOException("publish failed");
    }
}
