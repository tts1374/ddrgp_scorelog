using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DDRGpScoreViewer.Capture;
using DDRGpScoreViewer.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class ScreenshotImportServiceTests
{
    [Fact]
    public async Task Valid_png_uses_normal_result_detection_and_preserves_source_metadata()
    {
        using var fixture = new DatabaseFixture();
        var path = WritePng(fixture.DirectoryPath, "result.png", 1280, 720, 0x22);
        var modifiedAt = new DateTimeOffset(2026, 9, 16, 12, 34, 56, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(path, modifiedAt.UtcDateTime);
        CapturedFrame? analyzedFrame = null;
        AppSaveAdapterInput? savedInput = null;
        var service = new AppOwnedScreenshotImportService(
            (frame, _) =>
            {
                analyzedFrame = frame;
                return Task.FromResult(FormalObservation());
            },
            (_, observation, _, _) => observation,
            (input, _, _) =>
            {
                savedInput = input;
                return Task.FromResult(WorkflowResult("saved", true, input));
            });

        var result = await service.ProcessAsync(
            path,
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath);

        Assert.Equal(ScreenshotImportItemStatus.Saved, result.Status);
        Assert.NotNull(analyzedFrame);
        Assert.Equal("screenshot-import", analyzedFrame!.CaptureSource);
        Assert.NotNull(savedInput);
        Assert.Equal("manual", savedInput!.SourceKind);
        Assert.Equal(Path.GetFullPath(path), savedInput.SourcePath);
        Assert.Equal("", savedInput.ManifestImagePath);
        Assert.Equal("time", savedInput.ConfirmationMode);
        Assert.Equal(modifiedAt, DateTimeOffset.Parse(savedInput.CapturedAt));
        Assert.Equal(modifiedAt, DateTimeOffset.Parse(savedInput.FormalPlay!.PlayedAt));
        var expectedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)))
            .ToLowerInvariant();
        Assert.Equal($"screenshot-import-v1:{expectedHash}", savedInput.FormalPlay.DuplicateKey);
        Assert.Single(Directory.GetFiles(
            fixture.DirectoryPath,
            "*.png",
            SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Invalid_extension_corrupt_png_and_wrong_size_are_input_errors_before_analysis()
    {
        using var fixture = new DatabaseFixture();
        var analyzeCalls = 0;
        var service = Service(
            (frame, token) =>
            {
                analyzeCalls++;
                return Task.FromResult(FormalObservation());
            });
        var jpg = Path.Combine(fixture.DirectoryPath, "result.jpg");
        await File.WriteAllBytesAsync(jpg, [1, 2, 3]);
        var corrupt = Path.Combine(fixture.DirectoryPath, "corrupt.png");
        await File.WriteAllBytesAsync(corrupt, [1, 2, 3]);
        var wrongSize = WritePng(fixture.DirectoryPath, "small.png", 64, 64, 0x11);

        var results = new[]
        {
            await service.ProcessAsync(jpg, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath),
            await service.ProcessAsync(corrupt, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath),
            await service.ProcessAsync(wrongSize, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath),
        };

        Assert.All(results, result => Assert.Equal(ScreenshotImportItemStatus.InputError, result.Status));
        Assert.Equal(0, analyzeCalls);
    }

    [Fact]
    public async Task Non_result_and_incomplete_formal_evidence_are_not_saved()
    {
        using var fixture = new DatabaseFixture();
        var path = WritePng(fixture.DirectoryPath, "result.png", 1280, 720, 0x33);
        var saveCalls = 0;
        var nonResult = new AppOwnedScreenshotImportService(
            (_, _) => Task.FromResult(new LiveResultObservation(
                false, "", "", "results_header_not_detected")),
            (_, observation, _, _) => observation,
            (_, _, _) =>
            {
                saveCalls++;
                throw new InvalidOperationException();
            });

        var nonResultOutcome = await nonResult.ProcessAsync(
            path, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath);

        Assert.Equal(ScreenshotImportItemStatus.InputError, nonResultOutcome.Status);
        Assert.Equal(0, saveCalls);

        var workflowRunner = new AppOwnedPersonalScoreDbWorkflowRunner(
            () => ViewerDatabasePaths.ForDevelopment(fixture.DirectoryPath));
        var unresolved = new AppOwnedScreenshotImportService(
            (_, _) => Task.FromResult(new LiveResultObservation(
                true, "987650", "event", "result", DigitRecognitionStatus: "recognized")),
            (_, observation, _, _) => observation,
            workflowRunner.RunAdapterInputAsync);

        var unresolvedOutcome = await unresolved.ProcessAsync(
            path, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath);

        Assert.Equal(ScreenshotImportItemStatus.RecognitionFailed, unresolvedOutcome.Status);
        Assert.Equal(0L, Scalar(fixture.ScorePath, "SELECT COUNT(*) FROM plays;"));
        Assert.Equal(0L, Scalar(fixture.ScorePath, "SELECT COUNT(*) FROM source_captures;"));
    }

    [Fact]
    public async Task Same_bytes_are_duplicate_but_unresolved_can_retry_and_other_sources_do_not_collide()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay(
            "live-existing",
            "2026-09-16T11:00:00+00:00",
            900_000,
            1_000);
        fixture.ExecuteScoreSql(
            "UPDATE source_captures SET source_kind = 'capture' " +
            "WHERE capture_id = 'capture-live-existing';");
        var firstPath = WritePng(fixture.DirectoryPath, "first.png", 1280, 720, 0x44);
        var copyPath = Path.Combine(fixture.DirectoryPath, "copy.png");
        File.Copy(firstPath, copyPath);
        var differentPath = WritePng(fixture.DirectoryPath, "different.png", 1280, 720, 0x45);
        var ready = false;
        var workflowRunner = new AppOwnedPersonalScoreDbWorkflowRunner(
            () => ViewerDatabasePaths.ForDevelopment(fixture.DirectoryPath));
        var service = new AppOwnedScreenshotImportService(
            (_, _) => Task.FromResult(ready
                ? FormalObservation()
                : new LiveResultObservation(
                    true, "987650", "event", "result", DigitRecognitionStatus: "recognized")),
            (_, observation, _, _) => observation,
            workflowRunner.RunAdapterInputAsync);

        var unresolved = await service.ProcessAsync(
            firstPath, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath);
        ready = true;
        var saved = await service.ProcessAsync(
            firstPath, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath);
        var duplicate = await service.ProcessAsync(
            copyPath, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath);
        var different = await service.ProcessAsync(
            differentPath, fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath);

        Assert.Equal(ScreenshotImportItemStatus.RecognitionFailed, unresolved.Status);
        Assert.Equal(ScreenshotImportItemStatus.Saved, saved.Status);
        Assert.Equal(ScreenshotImportItemStatus.Duplicate, duplicate.Status);
        Assert.Equal(ScreenshotImportItemStatus.Saved, different.Status);
        Assert.Equal(3L, Scalar(fixture.ScorePath, "SELECT COUNT(*) FROM plays;"));
        Assert.Equal(4L, Scalar(fixture.ScorePath, "SELECT COUNT(*) FROM source_captures;"));
        Assert.Equal(3L, Scalar(fixture.ScorePath, "SELECT COUNT(*) FROM analysis_logs;"));
        Assert.Equal(4L, Scalar(
            fixture.ScorePath,
            "SELECT COUNT(DISTINCT capture_id) FROM source_captures;"));
        Assert.Equal(3L, Scalar(
            fixture.ScorePath,
            "SELECT COUNT(DISTINCT analysis_id) FROM analysis_logs;"));
        Assert.Equal(3L, Scalar(
            fixture.ScorePath,
            "SELECT COUNT(DISTINCT duplicate_key) FROM plays;"));
    }

    [Fact]
    public async Task Shared_write_gate_serializes_only_each_write_and_finishes_in_flight_work_after_cancel()
    {
        var gate = new AppProcessScoreWriteGate();
        using var firstEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var first = Task.Run(() => gate.RunAsync(
            () =>
            {
                firstEntered.Set();
                releaseFirst.Wait();
                return 1;
            },
            cancellation.Token));
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)));
        var second = Task.Run(() => gate.RunAsync(
            () =>
            {
                secondEntered.Set();
                return 2;
            },
            CancellationToken.None));

        Assert.False(secondEntered.Wait(TimeSpan.FromMilliseconds(150)));
        cancellation.Cancel();
        Assert.False(first.IsCompleted);
        releaseFirst.Set();

        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.True(secondEntered.IsSet);
    }

    private static AppOwnedScreenshotImportService Service(
        Func<CapturedFrame, CancellationToken, Task<LiveResultObservation>> analyze) =>
        new(
            analyze,
            (_, observation, _, _) => observation,
            (input, _, _) => Task.FromResult(WorkflowResult("saved", true, input)));

    private static PersonalScoreDbWorkflowResult WorkflowResult(
        string status,
        bool written,
        AppSaveAdapterInput input) =>
        new(
            status,
            "not_requested",
            "ready",
            written ? "written" : "not_checked",
            written,
            input.CaptureId,
            input.AnalysisId,
            input.FormalPlay?.PlayId,
            [],
            null,
            "fixture.sqlite");

    private static LiveResultObservation FormalObservation() =>
        new(
            true,
            "987650",
            "event",
            "formal-result",
            DigitRecognitionStatus: "recognized",
            FormalEvidence: new AppOwnedFormalEvidence(
                "master-v1",
                "song-1",
                "chart-1",
                987650,
                456,
                400,
                40,
                10,
                4,
                2,
                1750,
                "AAA",
                "CLEAR",
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["master_version"] = FormalEvidenceSourceNames.MasterMetadata,
                    ["song_id"] = FormalEvidenceSourceNames.ResultIdentityVisualEvidence,
                    ["chart_id"] = FormalEvidenceSourceNames.ResultIdentityVisualEvidence,
                    ["score"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["max_combo"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["marvelous"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["perfect"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["great"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["good"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["miss"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["ex_score"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["ok"] = FormalEvidenceSourceNames.ResultNumericVisualEvidence,
                    ["rank"] = FormalEvidenceSourceNames.ResultRankVisualEvidence,
                    ["clear_type"] = FormalEvidenceSourceNames.ResultClearTypeVisualEvidence,
                },
                new Dictionary<string, double?>(StringComparer.Ordinal)
                {
                    ["master_version"] = 0.99,
                    ["song_id"] = 0.99,
                    ["chart_id"] = 0.99,
                    ["score"] = 0.99,
                    ["max_combo"] = 0.99,
                    ["marvelous"] = 0.99,
                    ["perfect"] = 0.99,
                    ["great"] = 0.99,
                    ["good"] = 0.99,
                    ["miss"] = 0.99,
                    ["ex_score"] = 0.99,
                    ["ok"] = 0.99,
                    ["rank"] = 0.99,
                    ["clear_type"] = 0.99,
                },
                Ok: 0));

    private static string WritePng(
        string directory,
        string fileName,
        int width,
        int height,
        byte marker)
    {
        var pixels = new byte[width * height * 4];
        pixels[0] = marker;
        pixels[3] = 0xff;
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(directory, fileName);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static long Scalar(string databasePath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
