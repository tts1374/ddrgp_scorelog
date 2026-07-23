using System.Text.Json.Nodes;

namespace JacketCatalogCollector.Tests;

public sealed class ProjectionJsonLoaderTests
{
    private readonly ProjectionJsonLoader loader = new();

    private static string FixtureJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "current.json"));

    [Fact]
    public void LoadsCurrentProjectionAndPreservesHistoryAndOpaqueText()
    {
        var projection = loader.Load(FixtureJson());

        Assert.Equal(5, projection.ProjectionSchemaVersion);
        Assert.Equal(1, projection.Catalog.SchemaVersion);
        Assert.Equal("manual_confirm", projection.ReviewReferences[0].History[0].Action);
        Assert.Contains("日本語", projection.ReviewReferences[0].ManualNote);
    }

    [Fact]
    public void RejectsLegacyProjectionCatalogAndCapabilityFields()
    {
        foreach (var version in new[] { 1, 2, 3, 4 })
        {
            var legacyProjection = JsonNode.Parse(FixtureJson())!.AsObject();
            legacyProjection["projection_schema_version"] = version;
            Assert.Throws<InvalidOperationException>(
                () => loader.Load(legacyProjection.ToJsonString()));
        }

        foreach (var version in new[] { 2, 3 })
        {
            var legacyCatalog = JsonNode.Parse(FixtureJson())!.AsObject();
            legacyCatalog["catalog"]!["schema_version"] = version;
            Assert.Throws<InvalidOperationException>(
                () => loader.Load(legacyCatalog.ToJsonString()));
        }

        foreach (var field in new[] { "migration_required", "mutation_capability" })
        {
            var legacyCapability = JsonNode.Parse(FixtureJson())!.AsObject();
            legacyCapability["catalog"]![field] = true;
            Assert.Throws<InvalidOperationException>(
                () => loader.Load(legacyCapability.ToJsonString()));
        }
    }

    [Fact]
    public void RejectsInvalidHistoryCurrentStateAndStrictJson()
    {
        var invalidPayloads = new List<string> { "", "{\"projection_schema_version\":5" };

        var missing = JsonNode.Parse(FixtureJson())!.AsObject();
        missing.Remove("master");
        invalidPayloads.Add(missing.ToJsonString());

        var missingAliases = JsonNode.Parse(FixtureJson())!.AsObject();
        missingAliases["songs"]![0]!.AsObject().Remove("aliases");
        invalidPayloads.Add(missingAliases.ToJsonString());

        var missingSourceImagePath = JsonNode.Parse(FixtureJson())!.AsObject();
        missingSourceImagePath["review_references"]![0]!.AsObject().Remove("source_image_path");
        invalidPayloads.Add(missingSourceImagePath.ToJsonString());

        var unknown = JsonNode.Parse(FixtureJson())!.AsObject();
        unknown["unexpected"] = true;
        invalidPayloads.Add(unknown.ToJsonString());

        var badHistory = JsonNode.Parse(FixtureJson())!.AsObject();
        badHistory["review_references"]![0]!["history"]![0]!["after_revision"] = 2;
        invalidPayloads.Add(badHistory.ToJsonString());

        var badStatus = JsonNode.Parse(FixtureJson())!.AsObject();
        badStatus["review_references"]![0]!["stored_status"] = "future";
        invalidPayloads.Add(badStatus.ToJsonString());

        var currentMismatch = JsonNode.Parse(FixtureJson())!.AsObject();
        currentMismatch["review_references"]![0]!["manual_action_id"] = "different";
        invalidPayloads.Add(currentMismatch.ToJsonString());

        var histogramMismatch = JsonNode.Parse(FixtureJson())!.AsObject();
        histogramMismatch["coverage"]!["status_counts"] =
            JsonNode.Parse("{\"uncollected\":1}");
        invalidPayloads.Add(histogramMismatch.ToJsonString());

        var candidateMismatch = JsonNode.Parse(FixtureJson())!.AsObject();
        candidateMismatch["review_references"]![0]!["candidate_evaluation"]![
            "classification"] = "exact_unique";
        invalidPayloads.Add(candidateMismatch.ToJsonString());

        foreach (var payload in invalidPayloads)
        {
            Assert.Throws<InvalidOperationException>(() => loader.Load(payload));
        }
    }
}
