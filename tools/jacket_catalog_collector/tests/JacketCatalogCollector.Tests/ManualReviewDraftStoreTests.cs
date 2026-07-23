namespace JacketCatalogCollector.Tests;

public sealed class ManualReviewDraftStoreTests
{
    [Fact]
    public async Task SavesAndLoadsDraftsWithStableJsonFields()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"ddrgp-scorelog-drafts-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "manual-review-drafts.v1.json");
        try
        {
            var expected = new[]
            {
                new ManualReviewDraft
                {
                    ObservationId = "observation-2",
                    Status = "hold",
                    TruthSongId = null,
                    Notes = "保留して再確認",
                },
                new ManualReviewDraft
                {
                    ObservationId = "observation-1",
                    Status = "confirmed",
                    TruthSongId = "song-1",
                    Notes = "手動下書き",
                },
            };
            var store = new JsonManualReviewDraftStore(path);

            await store.SaveAsync(expected);

            var json = File.ReadAllText(path);
            Assert.Contains("\"draft_schema_version\": 1", json, StringComparison.Ordinal);
            Assert.Contains("\"observation_id\"", json, StringComparison.Ordinal);
            Assert.Contains("\"truth_song_id\"", json, StringComparison.Ordinal);
            var loaded = await store.LoadAsync();
            Assert.Equal(2, loaded.Count);
            Assert.Equal("confirmed", loaded["observation-1"].Status);
            Assert.Equal("song-1", loaded["observation-1"].TruthSongId);
            Assert.Equal("保留して再確認", loaded["observation-2"].Notes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("invalid", null)]
    [InlineData("confirmed", null)]
    [InlineData("rejected", "song-1")]
    public async Task RejectsInvalidDraftStatusAndTruthSongCombinations(
        string status,
        string? truthSongId)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"ddrgp-scorelog-invalid-draft-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "manual-review-drafts.v1.json");
        try
        {
            var store = new JsonManualReviewDraftStore(path);
            var draft = new ManualReviewDraft
            {
                ObservationId = "observation-1",
                Status = status,
                TruthSongId = truthSongId,
                Notes = "note",
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.SaveAsync([draft]));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
