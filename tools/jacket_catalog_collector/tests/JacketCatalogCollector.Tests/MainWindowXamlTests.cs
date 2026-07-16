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

    private static string GetTestSourcePath([CallerFilePath] string path = "") => path;
}
