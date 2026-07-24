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
        Assert.DoesNotContain("ウィンドウを再検索", buttonLabels);
        Assert.DoesNotContain(
            document.Descendants().SelectMany(element => element.Attributes()),
            attribute => attribute.Value.Contains("WindowCapture.Candidates", StringComparison.Ordinal)
                || attribute.Value.Contains("WindowCapture.SelectedCandidate", StringComparison.Ordinal));
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding WindowCapture.TargetDisplay}");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding WindowCapture.ConnectionDisplay}");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Button"),
            element => element.Attribute("Content")?.Value == "catalog retry"
                && element.Attribute("IsEnabled")?.Value
                    == "{Binding Observation.CanRetryCatalog}");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            element => element.Attribute("Text")?.Value
                == "{Binding Observation.CollectionResult}");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "CheckBox"),
            element => element.Attribute("Content")?.Value
                    == "このsessionで保存前照合済み候補を自動保存する（既定OFF）"
                && element.Attribute("IsChecked")?.Value
                    == "{Binding Observation.AutoSaveEnabled, Mode=TwoWay}");
        Assert.Contains("管理・設定", tabs);
    }

    [Fact]
    public void WindowStartupBindsFixedDatabaseInitializationWithoutSelection()
    {
        var document = LoadMainWindow();
        var window = document.Root!;
        var code = File.ReadAllText(GetMainWindowCodePath());

        Assert.Equal("MainWindow_Loaded", window.Attribute("Loaded")?.Value);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding StatusMessage}");

        var buttons = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .ToList();
        Assert.DoesNotContain("master/catalogを選択", buttons);
        Assert.Contains("曲情報を更新", buttons);
        Assert.Contains("CollectorDatabasePaths.Resolve()", code, StringComparison.Ordinal);
        Assert.Contains("DetectDdrGpAsync", code, StringComparison.Ordinal);
        Assert.Contains("captureObservationController.StartAsync()", code, StringComparison.Ordinal);
        Assert.Contains("captureObservationController.ResumeAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedCandidate", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.GetCurrentDirectory()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("database-paths.v1.json", code, StringComparison.Ordinal);
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
    public void UnreviewedScreenShowsDraftFieldsAndRoiColumns()
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
        var tabs = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .ToList();
        var headers = document
            .Descendants()
            .Where(element => element.Name.LocalName is "DataGridTextColumn" or "DataGridTemplateColumn")
            .Select(element => element.Attribute("Header")?.Value)
            .ToList();
        var runTexts = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Run")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToList();
        var manualReviewGrid = Assert.Single(
            document.Descendants().Where(element => element.Name.LocalName == "DataGrid"),
            element => element.Attribute("ItemsSource")?.Value == "{Binding ManualReviewRows}");

        Assert.Contains("レビュー", tabs);
        Assert.Contains("未レビュー", tabs);
        Assert.Contains("レビュー済み", tabs);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "TabItem"),
            element => element.Attribute("Header")?.Value == "レビュー済み"
                && element.Attribute("IsEnabled") is null);
        Assert.Contains(bindings, value => value.Contains(
            "ManualReviewRows", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "ManualReviewUnreviewedCount", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "ManualReviewConfirmedCount", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "ManualReviewRejectedCount", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "ManualReviewHoldCount", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "{Binding Status,", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "SelectedSearchResult", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "{Binding Notes,", StringComparison.Ordinal));
        Assert.DoesNotContain(bindings, value => value.Contains(
            "SelectedManualReviewRow.Status", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "TitleRoiImagePath", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "ArtistRoiImagePath", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "SongSearch", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "SongChoices", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "ReviewedManualReviewRows", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "CurrentStatusDisplay", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "CurrentSongDisplay", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "DraftStatus", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "RegisteredRoute", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "ProcessedAt", StringComparison.Ordinal));
        Assert.Contains("XLSXをエクスポート", buttons);
        Assert.Contains("一括反映", buttons);
        Assert.Contains("↻", buttons);
        Assert.DoesNotContain("projection再読込", buttons);
        Assert.True(
            buttons.IndexOf("XLSXをエクスポート") < buttons.IndexOf("一括反映")
                && buttons.IndexOf("一括反映") < buttons.IndexOf("↻"));
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Button"),
            element => element.Attribute("Content")?.Value == "↻"
                && element.Attribute("ToolTip")?.Value == "projectionを更新"
                && element.Attribute("Click")?.Value == "RefreshCandidates_Click");
        var code = File.ReadAllText(GetMainWindowCodePath());
        Assert.Contains("ExportManualReview_Click", code, StringComparison.Ordinal);
        Assert.Contains("SaveFileDialog", code, StringComparison.Ordinal);
        Assert.Contains("OverwritePrompt = true", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsUnderDataDirectory", code, StringComparison.Ordinal);
        Assert.DoesNotContain("未保存の下書きを保存", buttons);
        Assert.Contains("確定  ", runTexts);
        Assert.Contains("却下  ", runTexts);
        Assert.DoesNotContain("確定予定  ", runTexts);
        Assert.DoesNotContain("却下予定  ", runTexts);
        Assert.DoesNotContain(bindings, value => value.Contains(
            "ManualReviewSummary", StringComparison.Ordinal));
        Assert.Contains(headers, header => header?.StartsWith(
            "タイトルROI", StringComparison.Ordinal) == true);
        Assert.Contains(headers, header => header?.StartsWith(
            "アーティストROI", StringComparison.Ordinal) == true);
        Assert.Contains("状態", headers);
        Assert.Contains("確定曲", headers);
        Assert.Contains("メモ", headers);
        Assert.Contains(bindings, value => value.Contains(
            "ObservationIdShort", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains(
            "ObservationIdShort, Mode=OneWay", StringComparison.Ordinal));
        Assert.Equal("False", manualReviewGrid.Attribute("IsReadOnly")?.Value);
        Assert.Equal("96", manualReviewGrid.Attribute("RowHeight")?.Value);
        Assert.DoesNotContain("確定", buttons);
        Assert.DoesNotContain("再割当", buttons);
        Assert.DoesNotContain("却下", buttons);
        Assert.DoesNotContain("再開", buttons);
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

    private static string GetMainWindowCodePath() => Path.Combine(
        Path.GetDirectoryName(GetMainWindowPath())!,
        "MainWindow.xaml.cs");

    private static string GetTestSourcePath([CallerFilePath] string path = "") => path;
}
