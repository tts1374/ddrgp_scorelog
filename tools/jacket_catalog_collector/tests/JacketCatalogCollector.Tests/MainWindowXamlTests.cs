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
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "CheckBox"),
            element => element.Attribute("Content")?.Value
                    == "このsessionで保存前照合済み候補を自動保存する（既定OFF）"
                && element.Attribute("IsChecked")?.Value
                    == "{Binding Observation.AutoSaveEnabled, Mode=TwoWay}");
        Assert.Contains("管理・設定", tabs);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "DataGridCheckBoxColumn"),
            element => element.Attribute("Header")?.Value == "最小化"
                && element.Attributes().Any(attribute =>
                    attribute.Value.Contains("Identity.IsMinimized", StringComparison.Ordinal)));
    }

    [Fact]
    public void WindowStartupBindsRememberedDatabaseReload()
    {
        var document = LoadMainWindow();
        var window = document.Root!;

        Assert.Equal("MainWindow_Loaded", window.Attribute("Loaded")?.Value);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding StatusMessage}");
    }

    [Fact]
    public void InternalDetectorStateIsNotShownInThePrimaryLayout()
    {
        var xaml = File.ReadAllText(GetMainWindowPath());

        Assert.DoesNotContain("Window capture (memory only)", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Observation.DetectorState", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DuplicatePreview", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void InformationTitleLineObservationIsVisibleButReadOnly()
    {
        var document = LoadMainWindow();
        var textValues = document
            .Descendants()
            .Where(element => element.Name.LocalName is "TextBlock" or "Run")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToList();

        Assert.Contains("{Binding Observation.InformationPanelDisplay, Mode=OneWay}", textValues);
        Assert.Contains("{Binding Observation.InformationTitleLineStability, Mode=OneWay}", textValues);
        Assert.Contains("{Binding Observation.InformationTitleLineHash}", textValues);
        Assert.DoesNotContain(
            document.Descendants().Where(element => element.Name.LocalName == "Button"),
            element => element.Attributes().Any(attribute =>
                attribute.Value.Contains("InformationTitleLine", StringComparison.Ordinal)));
    }

    [Fact]
    public void ManualReviewShowsReadOnlyCandidateEvidenceAndExplicitActions()
    {
        var document = LoadMainWindow();
        var bindings = document.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToList();
        var buttons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .ToList();

        Assert.Contains(bindings, value => value.Contains(
            "CandidateEvaluation.JacketPreviewPath", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "CandidateEvaluation.Title.Display", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "CandidateEvaluation.Classification", StringComparison.Ordinal));
        Assert.Contains("候補を再評価", buttons);
        Assert.Contains("候補report", buttons);
        Assert.Contains("確定", buttons);
        Assert.Contains("再割当", buttons);
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
