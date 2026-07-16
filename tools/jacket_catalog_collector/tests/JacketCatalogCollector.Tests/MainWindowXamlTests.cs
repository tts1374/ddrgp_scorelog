using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace JacketCatalogCollector.Tests;

public sealed class MainWindowXamlTests
{
    [Fact]
    public void RunTextBindingsAreExplicitlyOneWay()
    {
        var testSourcePath = GetTestSourcePath();
        var xamlPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testSourcePath)!,
            "..",
            "..",
            "src",
            "JacketCatalogCollector",
            "MainWindow.xaml"));
        var document = XDocument.Load(xamlPath);
        var runBindings = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Run")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value?.StartsWith("{Binding ", StringComparison.Ordinal) == true)
            .Cast<string>()
            .ToList();

        Assert.NotEmpty(runBindings);
        Assert.All(runBindings, binding => Assert.Contains("Mode=OneWay", binding));
    }

    [Fact]
    public void CollectionWorkflowIsThePrimaryUserFacingScreen()
    {
        var document = LoadMainWindow();
        var tabs = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .ToList();
        var buttonLabels = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .ToList();

        Assert.Equal("ジャケット収集", tabs.First());
        Assert.Contains("このジャケットを保存", buttonLabels);
        Assert.Contains("収集を開始", buttonLabels);
        Assert.Contains("収集を終了", buttonLabels);
        Assert.Contains("管理・設定", tabs);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "DataGridCheckBoxColumn"),
            element => element.Attribute("Header")?.Value == "最小化"
                && element.Attributes().Any(attribute =>
                    attribute.Value.Contains("Identity.IsMinimized", StringComparison.Ordinal)));
    }

    [Fact]
    public void InternalDetectorStateIsNotShownInThePrimaryLayout()
    {
        var xaml = File.ReadAllText(GetMainWindowPath());

        Assert.DoesNotContain("Window capture (memory only)", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Observation.DetectorState", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DuplicatePreview", xaml, StringComparison.Ordinal);
    }

    private static XDocument LoadMainWindow() => XDocument.Load(GetMainWindowPath());

    private static string GetMainWindowPath()
    {
        var testSourcePath = GetTestSourcePath();
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testSourcePath)!,
            "..",
            "..",
            "src",
            "JacketCatalogCollector",
            "MainWindow.xaml"));
    }

    private static string GetTestSourcePath([CallerFilePath] string path = "") => path;
}
