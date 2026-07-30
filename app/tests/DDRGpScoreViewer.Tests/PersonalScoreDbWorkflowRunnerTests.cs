using System.Text;
using System.Text.Json;
using DDRGpScoreViewer.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class PersonalScoreDbWorkflowRunnerTests
{
    [Fact]
    public void Default_constructor_does_not_require_a_checkout()
    {
        var exception = Record.Exception(() => new AppOwnedPersonalScoreDbWorkflowRunner());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Ready_workflow_is_saved_by_the_app_owned_formal_writer()
    {
        using var fixture = new DatabaseFixture();
        var inputPath = WriteWorkflowInput(fixture.DirectoryPath, SaveInput());
        var runner = new AppOwnedPersonalScoreDbWorkflowRunner(
            () => ViewerDatabasePaths.ForDevelopment(fixture.DirectoryPath));

        var result = await runner.RunAsync(inputPath, fixture.ScorePath);

        Assert.Equal("saved", result.WorkflowStatus);
        Assert.Equal("written", result.DatabaseStatus);
        Assert.True(result.Written);
        Assert.Equal("play-app-owned", result.PlayId);
        var data = new ScoreViewerRepository().Load(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath);
        Assert.Contains(data.Plays, play => play.PlayId == "play-app-owned");
    }

    [Fact]
    public async Task Valid_analysis_detail_is_published_and_reused_by_the_app_owned_workflow()
    {
        using var fixture = new DatabaseFixture();
        var save = SaveInput();
        save["confirmation_mode"] = "time";
        save["timestamp_ms"] = 1_000L;
        save["candidate_duration_ms"] = 1_000L;
        save["log_path"] = "logs/analysis_details/detail.json";
        var detail = AnalysisDetail();
        var inputPath = WriteWorkflowInput(fixture.DirectoryPath, save, "detail.json", detail);
        var runner = new AppOwnedPersonalScoreDbWorkflowRunner(
            () => ViewerDatabasePaths.ForDevelopment(fixture.DirectoryPath));

        var first = await runner.RunAsync(inputPath, fixture.ScorePath);

        Assert.Equal("saved", first.WorkflowStatus);
        Assert.Equal("created", first.ArtifactStatus);
        Assert.True(File.Exists(Path.Combine(
            fixture.DirectoryPath,
            "logs",
            "analysis_details",
            "detail.json")));

        var secondSave = SaveInput();
        secondSave["confirmation_mode"] = "time";
        secondSave["timestamp_ms"] = 1_000L;
        secondSave["candidate_duration_ms"] = 1_000L;
        secondSave["log_path"] = "logs/analysis_details/detail.json";
        var secondInput = WriteWorkflowInput(
            fixture.DirectoryPath,
            secondSave,
            "second-detail.json",
            AnalysisDetail());
        var second = await runner.RunAsync(
            secondInput,
            Path.Combine(fixture.DirectoryPath, "second-score.sqlite"));

        Assert.Equal("saved", second.WorkflowStatus);
        Assert.Equal("reused", second.ArtifactStatus);
    }

    [Fact]
    public async Task Analysis_detail_shared_value_mismatch_is_rejected_before_side_effects()
    {
        using var fixture = new DatabaseFixture();
        var save = SaveInput();
        save["log_path"] = "logs/analysis_details/mismatch.json";
        var inputPath = WriteWorkflowInput(
            fixture.DirectoryPath,
            save,
            "mismatch.json",
            AnalysisDetail(analysisId: "other-analysis"));
        var runner = new AppOwnedPersonalScoreDbWorkflowRunner(
            () => ViewerDatabasePaths.ForDevelopment(fixture.DirectoryPath));
        var databasePath = Path.Combine(fixture.DirectoryPath, "mismatch.sqlite");

        var result = await runner.RunAsync(inputPath, databasePath);

        Assert.Equal("invalid", result.WorkflowStatus);
        Assert.Contains("analysis_id mismatch", result.Reasons);
        Assert.False(File.Exists(databasePath));
        Assert.False(Directory.Exists(Path.Combine(fixture.DirectoryPath, "logs")));
    }

    [Fact]
    public async Task Unresolved_candidate_does_not_create_or_modify_the_score_database()
    {
        using var fixture = new DatabaseFixture();
        var databasePath = Path.Combine(fixture.DirectoryPath, "not-created.sqlite");
        var input = SaveInput();
        input.Remove("formal_play");
        var inputPath = WriteWorkflowInput(fixture.DirectoryPath, input);
        var runner = new AppOwnedPersonalScoreDbWorkflowRunner(
            () => ViewerDatabasePaths.ForDevelopment(fixture.DirectoryPath));

        var result = await runner.RunAsync(inputPath, databasePath);

        Assert.Equal("unresolved", result.WorkflowStatus);
        Assert.Contains("formal_play_required", result.Reasons);
        Assert.False(File.Exists(databasePath));
    }

    [Fact]
    public async Task Duplicate_key_collision_keeps_source_and_analysis_without_a_second_play()
    {
        using var fixture = new DatabaseFixture();
        var runner = new AppOwnedPersonalScoreDbWorkflowRunner(
            () => ViewerDatabasePaths.ForDevelopment(fixture.DirectoryPath));

        var first = SaveInput();
        var firstPath = WriteWorkflowInput(fixture.DirectoryPath, first);
        var firstResult = await runner.RunAsync(firstPath, fixture.ScorePath);

        var second = SaveInput();
        second["capture_id"] = "capture-app-owned-2";
        second["capture_hash"] = "sha256:app-owned-2";
        second["analysis_id"] = "analysis-app-owned-2";
        var secondPath = WriteWorkflowInput(fixture.DirectoryPath, second, "second-workflow.json");
        var secondResult = await runner.RunAsync(secondPath, fixture.ScorePath);

        Assert.Equal("saved", firstResult.WorkflowStatus);
        Assert.Equal("duplicate", secondResult.WorkflowStatus);
        Assert.Null(secondResult.PlayId);
        Assert.Contains("duplicate_key_already_saved", secondResult.Reasons);
        var data = new ScoreViewerRepository().Load(
            fixture.ScorePath,
            fixture.MasterPath,
            fixture.CatalogPath);
        Assert.Single(data.Plays, play => play.PlayId == "play-app-owned");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.ScorePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM source_captures;";
        Assert.Equal(2L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Duplicate_json_keys_are_rejected_before_database_preparation()
    {
        using var fixture = new DatabaseFixture();
        var inputPath = Path.Combine(fixture.DirectoryPath, "duplicate.json");
        File.WriteAllText(
            inputPath,
            "{\"workflow_schema_version\":1,\"workflow_schema_version\":1," +
            "\"analysis_detail\":null,\"save_input\":{}}",
            new UTF8Encoding(false));
        var databasePath = Path.Combine(fixture.DirectoryPath, "duplicate.sqlite");
        var runner = new AppOwnedPersonalScoreDbWorkflowRunner();

        var result = await runner.RunAsync(inputPath, databasePath);

        Assert.Equal("invalid", result.WorkflowStatus);
        Assert.False(File.Exists(databasePath));
    }

    private static Dictionary<string, object?> SaveInput() =>
        new(StringComparer.Ordinal)
        {
            ["input_schema_version"] = 1,
            ["candidate_material"] = new Dictionary<string, string>
            {
                ["recognized_digits"] = "candidate-only",
            },
            ["capture_id"] = "capture-app-owned-1",
            ["capture_hash"] = "sha256:app-owned-1",
            ["captured_at"] = "2026-07-29T12:00:00+09:00",
            ["source_kind"] = "manual",
            ["source_path"] = "fixture://app-owned",
            ["analysis_id"] = "analysis-app-owned-1",
            ["event_type"] = "confirmed",
            ["confirmed_result"] = true,
            ["duplicate"] = false,
            ["confirmation_mode"] = "manual",
            ["identity_signal_status"] = "reviewed",
            ["digit_review_status"] = "reviewed",
            ["analysis_confidence"] = 0.98,
            ["analysis_summary_json"] = "{\"contract\":\"app-owned-test\"}",
            ["app_version"] = "test",
            ["formal_play"] = new Dictionary<string, object?>
            {
                ["play_id"] = "play-app-owned",
                ["played_at"] = "2026-07-29T12:00:00+09:00",
                ["master_version"] = "master-v1",
                ["song_id"] = "song-1",
                ["chart_id"] = "chart-1",
                ["score"] = 987650,
                ["max_combo"] = 456,
                ["marvelous"] = 400,
                ["perfect"] = 40,
                ["great"] = 10,
                ["good"] = 4,
                ["miss"] = 2,
                ["ex_score"] = 1750,
                ["rank"] = "AAA",
                ["clear_type"] = "CLEAR",
                ["flare_rank"] = null,
                ["duplicate_key"] = "play:v1:app-owned",
            },
            ["exclusion"] = null,
            ["manifest_image_path"] = "",
            ["frame_index"] = null,
            ["timestamp_ms"] = null,
            ["candidate_duration_ms"] = null,
            ["log_path"] = "",
        };

    private static string WriteWorkflowInput(
        string root,
        Dictionary<string, object?> saveInput,
        string fileName = "workflow.json",
        Dictionary<string, object?>? analysisDetail = null)
    {
        var path = Path.Combine(root, fileName);
        var workflow = new Dictionary<string, object?>
        {
            ["workflow_schema_version"] = 1,
            ["analysis_detail"] = analysisDetail,
            ["save_input"] = saveInput,
        };
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(workflow, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
        return path;
    }

    private static Dictionary<string, object?> AnalysisDetail(
        string analysisId = "analysis-app-owned-1",
        string sourceCaptureId = "capture-app-owned-1") =>
        new(StringComparer.Ordinal)
        {
            ["schema_version"] = 1,
            ["generated_by"] = "tools.vision_poc.personal_score_db_analysis_artifacts",
            ["generated_at"] = "2026-07-29T03:00:00Z",
            ["app_version"] = "test",
            ["analysis_id"] = analysisId,
            ["source_capture_id"] = sourceCaptureId,
            ["analysis_status"] = "saved",
            ["save_boundary_status"] = "save_ready",
            ["skip_reason"] = "",
            ["event"] = new Dictionary<string, object?>
            {
                ["confirmed_result"] = true,
                ["duplicate"] = false,
                ["event_type"] = "confirmed",
                ["confirmation_mode"] = "time",
                ["timestamp_ms"] = 1_000L,
                ["candidate_duration_ms"] = 1_000L,
            },
            ["review"] = new Dictionary<string, object?>
            {
                ["identity_status"] = "reviewed",
                ["digit_status"] = "reviewed",
                ["analysis_confidence"] = 0.98,
            },
            ["investigation"] = new Dictionary<string, object?>
            {
                ["candidate_material"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["kind"] = "identity",
                        ["status"] = "reviewed",
                        ["summary"] = "formal identity supplied",
                    },
                },
                ["diagnostic_summary"] = new[] { "app-owned formal workflow" },
            },
            ["failure_image_path"] = null,
            ["retention"] = new Dictionary<string, object?>
            {
                ["retention_class"] = "standard",
                ["basis_at"] = "2026-07-29T03:00:00Z",
                ["expires_at"] = "2026-08-28T03:00:00Z",
            },
        };
}
