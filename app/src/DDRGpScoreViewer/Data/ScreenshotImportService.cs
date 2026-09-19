using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using DDRGpScoreViewer.Capture;

namespace DDRGpScoreViewer.Data;

public enum ScreenshotImportState
{
    Idle,
    Importing,
    Completed,
    Cancelled,
}

public enum ScreenshotImportItemStatus
{
    Saved,
    Duplicate,
    RecognitionFailed,
    InputError,
}

public sealed record ScreenshotImportItemResult(
    string FilePath,
    ScreenshotImportItemStatus Status,
    string Message,
    IReadOnlyList<string> InternalReasons)
{
    public string FileName => Path.GetFileName(FilePath);

    public string StatusDisplay => Status switch
    {
        ScreenshotImportItemStatus.Saved => Localization.Get("保存"),
        ScreenshotImportItemStatus.Duplicate => Localization.Get("重複"),
        ScreenshotImportItemStatus.RecognitionFailed => Localization.Get("認識失敗"),
        _ => Localization.Get("入力エラー"),
    };
}

public interface IScreenshotImportService
{
    Task<ScreenshotImportItemResult> ProcessAsync(
        string imagePath,
        string scoreDatabasePath,
        string masterDatabasePath,
        string catalogDatabasePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Imports one screenshot through the same RESULT detection, formal-evidence,
/// adapter, and formal DB writer used by app-owned live monitoring.
/// </summary>
public sealed class AppOwnedScreenshotImportService : IScreenshotImportService
{
    private const int RequiredWidth = 1280;
    private const int RequiredHeight = 720;
    private readonly Func<CapturedFrame, CancellationToken, Task<LiveResultObservation>> analyze;
    private readonly Func<CapturedFrame, LiveResultObservation, string, string?, LiveResultObservation>
        enrich;
    private readonly Func<AppSaveAdapterInput, string, CancellationToken,
        Task<PersonalScoreDbWorkflowResult>> save;

    public AppOwnedScreenshotImportService()
    {
        var analyzer = new AppOwnedLiveResultAnalyzer();
        var identityEvidenceProducer = new AppOwnedVisualIdentityEvidenceProducer();
        var workflowRunner = new AppOwnedPersonalScoreDbWorkflowRunner();
        analyze = analyzer.AnalyzeAsync;
        enrich = identityEvidenceProducer.Enrich;
        save = workflowRunner.RunAdapterInputAsync;
    }

    internal AppOwnedScreenshotImportService(
        Func<CapturedFrame, CancellationToken, Task<LiveResultObservation>> analyze,
        Func<CapturedFrame, LiveResultObservation, string, string?, LiveResultObservation> enrich,
        Func<AppSaveAdapterInput, string, CancellationToken,
            Task<PersonalScoreDbWorkflowResult>> save)
    {
        this.analyze = analyze;
        this.enrich = enrich;
        this.save = save;
    }

    public async Task<ScreenshotImportItemResult> ProcessAsync(
        string imagePath,
        string scoreDatabasePath,
        string masterDatabasePath,
        string catalogDatabasePath,
        CancellationToken cancellationToken = default)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(imagePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return InputError(imagePath, "画像のpathを確認してください。", exception.Message);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return InputError(fullPath, "PNGファイルだけ選択できます。", "screenshot_import.extension_not_png");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(fullPath);
            var pngBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            var dimensions = DecodeDimensions(pngBytes);
            if (dimensions.Width != RequiredWidth || dimensions.Height != RequiredHeight)
            {
                return InputError(
                    fullPath,
                    "1280×720のPNG画像を選択してください。",
                    $"screenshot_import.invalid_size:{dimensions.Width}x{dimensions.Height}");
            }

            fileInfo.Refresh();
            var capturedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc);
            var frame = new CapturedFrame(
                pngBytes,
                dimensions.Width,
                dimensions.Height,
                capturedAt.ToUnixTimeMilliseconds(),
                capturedAt,
                "screenshot-import");
            var observation = await analyze(frame, cancellationToken);
            if (!observation.IsResultScreen)
            {
                return InputError(
                    fullPath,
                    "RESULT画面として確認できなかったため保存しませんでした。",
                    observation.Reason);
            }

            var pngHash = Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant();
            var attemptId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            observation = observation with
            {
                ConfirmedEventId = $"screenshot-import-v1:{pngHash}",
            };
            observation = enrich(frame, observation, masterDatabasePath, catalogDatabasePath);
            var input = AppOwnedCaptureSaveWorkflowRunner.BuildInput(
                frame,
                observation,
                sourceKind: "manual",
                sourcePath: fullPath,
                imagePath: string.Empty,
                frameIndex: null,
                candidateDurationMs: null,
                duplicate: false,
                captureId: $"screenshot-import-capture-{attemptId}",
                analysisId: $"screenshot-import-analysis-{attemptId}",
                confirmationMode: "time");
            var result = await save(input, scoreDatabasePath, cancellationToken);
            return FromWorkflow(fullPath, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                NotSupportedException or ArgumentException)
        {
            return InputError(fullPath, "PNG画像を読み込めませんでした。", exception.Message);
        }
    }

    private static (int Width, int Height) DecodeDimensions(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception exception) when (
            exception is FileFormatException or NotSupportedException or ArgumentException or
                InvalidOperationException)
        {
            throw new InvalidDataException("screenshot_import.invalid_png", exception);
        }
    }

    private static ScreenshotImportItemResult FromWorkflow(
        string fullPath,
        PersonalScoreDbWorkflowResult result)
    {
        return result.WorkflowStatus switch
        {
            "saved" => new(
                fullPath,
                ScreenshotImportItemStatus.Saved,
                Localization.Get("スコアを保存しました。"),
                result.Reasons),
            "duplicate" => new(
                fullPath,
                ScreenshotImportItemStatus.Duplicate,
                Localization.Get("同じPNGから保存済みのため、重複として保存しませんでした。"),
                result.Reasons),
            "unresolved" or "excluded" => RecognitionFailed(
                fullPath,
                "スコア情報を確定できなかったため保存しませんでした。",
                result.Reasons),
            _ => InputError(
                fullPath,
                "保存処理を完了できませんでした。",
                result.Reasons),
        };
    }

    private static ScreenshotImportItemResult RecognitionFailed(
        string path,
        string message,
        params string[] reasons) =>
        RecognitionFailed(path, message, (IReadOnlyList<string>)reasons);

    private static ScreenshotImportItemResult RecognitionFailed(
        string path,
        string message,
        IReadOnlyList<string> reasons) =>
        new(path, ScreenshotImportItemStatus.RecognitionFailed, Localization.Get(message), reasons);

    private static ScreenshotImportItemResult InputError(
        string path,
        string message,
        params string[] reasons) =>
        InputError(path, message, (IReadOnlyList<string>)reasons);

    private static ScreenshotImportItemResult InputError(
        string path,
        string message,
        IReadOnlyList<string> reasons) =>
        new(path, ScreenshotImportItemStatus.InputError, Localization.Get(message), reasons);
}
