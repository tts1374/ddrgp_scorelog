using System.Text.Json.Nodes;

namespace JacketCatalogCollector.Tests;

public sealed class ProjectionJsonLoaderTests
{
    private readonly ProjectionJsonLoader loader = new();

    private static string FixtureJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "v1.json"));

    private static string FixtureV2Json() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "v2.json"));

    [Fact]
    public void LoadsVersionOneProducerFixtureAndPreservesOpaqueReasons()
    {
        var projection = loader.Load(FixtureJson());

        Assert.Equal("master-v1", projection.Master.MasterVersion);
        Assert.Equal("ddrgp-local-jacket-reference-catalog", projection.Catalog.CatalogIdentity);
        Assert.Equal("future opaque reason / 日本語", projection.ReviewReferences[0].Reason);
        Assert.Equal("candidate opaque reason", projection.ReviewReferences[0].Candidates[0].Reason);
    }

    [Fact]
    public void LoadsVersionTwoMutationProjectionAndHistory()
    {
        var projection = loader.Load(FixtureV2Json());

        Assert.Equal(2, projection.ProjectionSchemaVersion);
        Assert.Equal("manual_review_v2", projection.Catalog.MutationCapability);
        Assert.False(projection.Catalog.MigrationRequired);
        Assert.Equal(1, projection.ReviewReferences[0].Revision);
        Assert.Equal("manual_confirm", projection.ReviewReferences[0].History![0].Action);
        Assert.Contains("日本語", projection.ReviewReferences[0].ManualNote);
    }

    [Fact]
    public void RejectsInconsistentVersionTwoCapabilityAndHistory()
    {
        var badCapability = JsonNode.Parse(FixtureV2Json())!.AsObject();
        badCapability["catalog"]!["mutation_capability"] = "read_only";
        Assert.Throws<InvalidOperationException>(() => loader.Load(badCapability.ToJsonString()));

        var badHistory = JsonNode.Parse(FixtureV2Json())!.AsObject();
        badHistory["review_references"]![0]!["history"]![0]!["after_revision"] = 2;
        Assert.Throws<InvalidOperationException>(() => loader.Load(badHistory.ToJsonString()));

        var unknownStatus = JsonNode.Parse(FixtureV2Json())!.AsObject();
        unknownStatus["review_references"]![0]!["stored_status"] = "future";
        Assert.Throws<InvalidOperationException>(() => loader.Load(unknownStatus.ToJsonString()));

        var currentMismatch = JsonNode.Parse(FixtureV2Json())!.AsObject();
        currentMismatch["review_references"]![0]!["manual_action_id"] = "different";
        Assert.Throws<InvalidOperationException>(() => loader.Load(currentMismatch.ToJsonString()));

        var semanticMismatch = JsonNode.Parse(FixtureV2Json())!.AsObject();
        semanticMismatch["review_references"]![0]!["history"]![0]!["after_song_id"] = null;
        Assert.Throws<InvalidOperationException>(() => loader.Load(semanticMismatch.ToJsonString()));

        var sourceMismatch = JsonNode.Parse(FixtureV2Json())!.AsObject();
        sourceMismatch["review_references"]![0]!["history"]![0]!["before_status"] = "rejected";
        Assert.Throws<InvalidOperationException>(() => loader.Load(sourceMismatch.ToJsonString()));

        var v2FieldInV1 = JsonNode.Parse(FixtureJson())!.AsObject();
        v2FieldInV1["catalog"]!["mutation_capability"] = "manual_review_v2";
        Assert.Throws<InvalidOperationException>(() => loader.Load(v2FieldInV1.ToJsonString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void RejectsUnsupportedProjectionVersions(int version)
    {
        var root = JsonNode.Parse(FixtureJson())!.AsObject();
        root["projection_schema_version"] = version;
        Assert.Throws<InvalidOperationException>(() => loader.Load(root.ToJsonString()));
    }

    [Fact]
    public void RejectsMissingUnknownNullTypeStatusCandidateAndTruncatedPayloads()
    {
        var invalidPayloads = new List<string> { "", "{\"projection_schema_version\":1" };

        var missing = JsonNode.Parse(FixtureJson())!.AsObject();
        missing.Remove("master");
        invalidPayloads.Add(missing.ToJsonString());

        var unknown = JsonNode.Parse(FixtureJson())!.AsObject();
        unknown["unexpected"] = true;
        invalidPayloads.Add(unknown.ToJsonString());

        var nullSongs = JsonNode.Parse(FixtureJson())!.AsObject();
        nullSongs["songs"] = null;
        invalidPayloads.Add(nullSongs.ToJsonString());

        var wrongType = JsonNode.Parse(FixtureJson())!.AsObject();
        wrongType["coverage"]!["grand_prix_song_count"] = "one";
        invalidPayloads.Add(wrongType.ToJsonString());

        var unknownStatus = JsonNode.Parse(FixtureJson())!.AsObject();
        unknownStatus["songs"]![0]!["coverage_status"] = "future_status";
        invalidPayloads.Add(unknownStatus.ToJsonString());

        var badCandidate = JsonNode.Parse(FixtureJson())!.AsObject();
        badCandidate["review_references"]![0]!["candidates"]![0]!["song_id"] = "";
        invalidPayloads.Add(badCandidate.ToJsonString());

        foreach (var field in new[] { "observed_title", "observation_status", "reason" })
        {
            var nestedNull = JsonNode.Parse(FixtureJson())!.AsObject();
            nestedNull["review_references"]![0]![field] = null;
            invalidPayloads.Add(nestedNull.ToJsonString());
        }

        var songNull = JsonNode.Parse(FixtureJson())!.AsObject();
        songNull["songs"]![0]!["master_version"] = null;
        invalidPayloads.Add(songNull.ToJsonString());

        var catalogNull = JsonNode.Parse(FixtureJson())!.AsObject();
        catalogNull["catalog"]!["created_at"] = null;
        invalidPayloads.Add(catalogNull.ToJsonString());

        var assignedSong = JsonNode.Parse(FixtureJson())!.AsObject();
        assignedSong["review_references"]![0]!["assigned_song"] = JsonNode.Parse(
            "{\"song_id\":\"\",\"title\":null,\"artist\":null,\"master_song_missing\":true}");
        invalidPayloads.Add(assignedSong.ToJsonString());

        var histogramMismatch = JsonNode.Parse(FixtureJson())!.AsObject();
        histogramMismatch["coverage"]!["status_counts"] = JsonNode.Parse("{\"uncollected\":1}");
        invalidPayloads.Add(histogramMismatch.ToJsonString());

        foreach (var payload in invalidPayloads)
        {
            Assert.Throws<InvalidOperationException>(() => loader.Load(payload));
        }
    }
}
