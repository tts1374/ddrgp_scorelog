using DDRGpScoreViewer.Runtime;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class AppRuntimeResourceResolverTests
{
    [Fact]
    public void Resolves_digit_templates_from_the_application_package()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-runtime-{Guid.NewGuid():N}");
        var packageTemplates = Path.Combine(root, "RuntimeAssets", "digit_templates");
        Directory.CreateDirectory(packageTemplates);
        try
        {
            var resolver = new AppRuntimeResourceResolver(packageRoot: root);

            Assert.Equal(
                Path.GetFullPath(packageTemplates),
                resolver.ResolveDigitTemplatesDirectory());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Explicit_runtime_data_overrides_package_lookup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-runtime-{Guid.NewGuid():N}");
        var explicitTemplates = Path.Combine(root, "data", "digit_templates");
        Directory.CreateDirectory(explicitTemplates);
        try
        {
            var resolver = new AppRuntimeResourceResolver(
                packageRoot: Path.Combine(root, "package"),
                explicitDataRoot: Path.Combine(root, "data"));

            Assert.Equal(
                Path.GetFullPath(explicitTemplates),
                resolver.ResolveDigitTemplatesDirectory());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_resource_fails_without_searching_parent_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ddrgp-runtime-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(root, "package", "nested");
        Directory.CreateDirectory(Path.Combine(root, "package", "RuntimeAssets", "digit_templates"));
        try
        {
            var resolver = new AppRuntimeResourceResolver(packageRoot: packageRoot);

            var exception = Assert.Throws<RuntimeResourceException>(
                resolver.ResolveDigitTemplatesDirectory);

            Assert.Contains("digit_templates", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
