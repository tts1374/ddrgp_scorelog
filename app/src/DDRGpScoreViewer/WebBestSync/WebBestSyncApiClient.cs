using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDRGpScoreViewer.WebIdentity;

namespace DDRGpScoreViewer.WebBestSync;

internal interface IWebBestSyncApiClient
{
    Task<WebBestDeltaResult> SendDeltaAsync(
        string masterVersion,
        IReadOnlyList<WebBestDeltaOperation> operations,
        CancellationToken cancellationToken);

    Task<WebBestSnapshotBeginResult> BeginSnapshotAsync(
        string masterVersion,
        int expectedItemCount,
        CancellationToken cancellationToken);

    Task<WebBestApiResult> UploadSnapshotChunkAsync(
        string snapshotId,
        string chunkId,
        IReadOnlyList<PlayerChartBestProjectionV1> items,
        CancellationToken cancellationToken);

    Task<WebBestApiResult> CommitSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken);

    Task<WebBestApiResult> AbortSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken);

    Task<WebBestApiResult> DeletePublicBestsAsync(CancellationToken cancellationToken);

    Task<WebBestReplacementReviewResult> ReviewReplacementAsync(string snapshotId, CancellationToken cancellationToken);

    Task<WebBestApiResult> AuthorizeReplacementAsync(string snapshotId, WebBestReplacementReview review, CancellationToken cancellationToken);
}

internal sealed class WebBestSyncApiClient : IWebBestSyncApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient httpClient;
    private readonly IWebPlayerIdentityStore identityStore;

    public WebBestSyncApiClient(
        HttpClient httpClient,
        IWebPlayerIdentityStore identityStore,
        bool allowLoopbackHttp = false)
    {
        this.httpClient = httpClient;
        this.identityStore = identityStore;
        if (httpClient.BaseAddress is null ||
            !(httpClient.BaseAddress.Scheme == Uri.UriSchemeHttps ||
              allowLoopbackHttp && httpClient.BaseAddress.Scheme == Uri.UriSchemeHttp &&
              httpClient.BaseAddress.IsLoopback))
        {
            throw new ArgumentException(
                "The Web Best API requires HTTPS, or loopback HTTP in development.");
        }
    }

    public async Task<WebBestDeltaResult> SendDeltaAsync(
        string masterVersion,
        IReadOnlyList<WebBestDeltaOperation> operations,
        CancellationToken cancellationToken)
    {
        if (operations.Any(operation => operation.Type != "upsert" || operation.Projection is null))
        {
            return new(WebBestApiStatus.PermanentError, [], "INVALID_OPERATION");
        }
        var payload = new MergeRequest(
            1,
            masterVersion,
            operations.Select(operation => operation.Projection!).ToArray());
        using var request = CreateRequest(
            HttpMethod.Post,
            "api/v1/me/bests/merge",
            JsonContent.Create(payload, options: JsonOptions));
        var response = await SendAsync(request, cancellationToken);
        if (response.Response is null)
        {
            return new(response.Status, [], response.ErrorCode);
        }
        using (response.Response)
        {
            if (response.Status != WebBestApiStatus.Success)
            {
                return new(response.Status, [], response.ErrorCode);
            }
            try
            {
                var body = await response.Response.Content.ReadFromJsonAsync<DeltaResponse>(
                    JsonOptions,
                    cancellationToken);
                if (body?.Results is null || body.Results.Count != operations.Count ||
                    body.Results.Select(item => item.Index).Distinct().Count() != operations.Count ||
                    body.Results.Any(item => item.Index < 0 || item.Index >= operations.Count ||
                        item.Status == "accepted" && item.ReceivedHash != operations[item.Index].SentProjectionHash))
                {
                    return new(WebBestApiStatus.PermanentError, [], "INVALID_RESPONSE");
                }
                return new(
                    WebBestApiStatus.Success,
                    body.Results.Select(item => new WebBestDeltaItemResult(
                        item.Index,
                        string.Equals(item.Status, "accepted", StringComparison.Ordinal),
                        item.Changed,
                        item.Code)).ToArray());
            }
            catch (JsonException)
            {
                return new(WebBestApiStatus.PermanentError, [], "INVALID_RESPONSE");
            }
        }
    }

    public async Task<WebBestSnapshotBeginResult> BeginSnapshotAsync(
        string masterVersion,
        int expectedItemCount,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "api/v1/me/bests/snapshots",
            JsonContent.Create(
                new SnapshotBeginRequest(1, masterVersion, expectedItemCount),
                options: JsonOptions));
        var response = await SendAsync(request, cancellationToken);
        if (response.Response is null)
        {
            return new(response.Status, null, null, response.ErrorCode);
        }
        using (response.Response)
        {
            if (response.Status != WebBestApiStatus.Success)
            {
                return new(response.Status, null, null, response.ErrorCode);
            }
            try
            {
                var body = await response.Response.Content.ReadFromJsonAsync<SnapshotBeginResponse>(
                    JsonOptions,
                    cancellationToken);
                if (body is null || string.IsNullOrWhiteSpace(body.SnapshotId))
                {
                    return new(WebBestApiStatus.PermanentError, null, null, "INVALID_RESPONSE");
                }
                return new(WebBestApiStatus.Success, body.SnapshotId, body.BaseSyncRevision);
            }
            catch (JsonException)
            {
                return new(WebBestApiStatus.PermanentError, null, null, "INVALID_RESPONSE");
            }
        }
    }

    public Task<WebBestApiResult> UploadSnapshotChunkAsync(
        string snapshotId,
        string chunkId,
        IReadOnlyList<PlayerChartBestProjectionV1> items,
        CancellationToken cancellationToken) =>
        SendForResultAsync(
            HttpMethod.Put,
            $"api/v1/me/bests/snapshots/{Uri.EscapeDataString(snapshotId)}/items",
            JsonContent.Create(new SnapshotChunkRequest(chunkId, items), options: JsonOptions),
            cancellationToken);

    public Task<WebBestApiResult> CommitSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken) =>
        SendForResultAsync(
            HttpMethod.Post,
            $"api/v1/me/bests/snapshots/{Uri.EscapeDataString(snapshotId)}/commit",
            null,
            cancellationToken);

    public Task<WebBestApiResult> AbortSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken) =>
        SendForResultAsync(
            HttpMethod.Delete,
            $"api/v1/me/bests/snapshots/{Uri.EscapeDataString(snapshotId)}",
            null,
            cancellationToken);

    public Task<WebBestApiResult> DeletePublicBestsAsync(CancellationToken cancellationToken) =>
        SendForResultAsync(
            HttpMethod.Delete,
            "api/v1/me/bests",
            null,
            cancellationToken);

    public async Task<WebBestReplacementReviewResult> ReviewReplacementAsync(string snapshotId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post,
            $"api/v1/me/bests/snapshots/{Uri.EscapeDataString(snapshotId)}/replacement-review", null);
        var response = await SendAsync(request, cancellationToken);
        if (response.Response is null) return new(response.Status, ErrorCode: response.ErrorCode);
        using (response.Response)
        {
            try
            {
                var review = await response.Response.Content.ReadFromJsonAsync<WebBestReplacementReview>(JsonOptions, cancellationToken);
                if (review is null || review.PublicCount < 0 || review.EligibleCount < 0 ||
                    review.Removed is null || review.Lowered is null || review.ContentDigest is null ||
                    review.ContentDigest.Length != 64 || review.BaseSyncRevision < 0 ||
                    review.Lowered.Any(item => item.Fields is null))
                    return new(WebBestApiStatus.PermanentError, ErrorCode: "INVALID_RESPONSE");
                return new(WebBestApiStatus.Success, review);
            }
            catch (JsonException) { return new(WebBestApiStatus.PermanentError, ErrorCode: "INVALID_RESPONSE"); }
        }
    }

    public Task<WebBestApiResult> AuthorizeReplacementAsync(string snapshotId, WebBestReplacementReview review, CancellationToken cancellationToken) =>
        SendForResultAsync(HttpMethod.Post,
            $"api/v1/me/bests/snapshots/{Uri.EscapeDataString(snapshotId)}/replacement-authorize",
            JsonContent.Create(new
            {
                confirmed = true,
                content_digest = review.ContentDigest,
                base_sync_revision = review.BaseSyncRevision
            }), cancellationToken);

    private async Task<WebBestApiResult> SendForResultAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, content);
        var response = await SendAsync(request, cancellationToken);
        response.Response?.Dispose();
        return new(response.Status, response.ErrorCode);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        HttpContent? content)
    {
        var identity = identityStore.Load();
        if (identity.State != PlayerIdentityState.Registered ||
            string.IsNullOrWhiteSpace(identity.AppCredential))
        {
            content?.Dispose();
            throw new InvalidOperationException(
                "A registered Web Player identity is required for Web Best sync.");
        }
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            identity.AppCredential);
        return request;
    }

    private async Task<RawApiResult> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TimeoutException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new(WebBestApiStatus.RetryableError, null, "NETWORK_ERROR");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            identityStore.SetAuthenticationInvalid(true);
            return new(WebBestApiStatus.AuthenticationInvalid, null, "AUTH_INVALID");
        }
        if (response.StatusCode == HttpStatusCode.TooManyRequests ||
            (int)response.StatusCode >= 500)
        {
            response.Dispose();
            return new(WebBestApiStatus.RetryableError, null, "SERVER_RETRYABLE");
        }
        if (!response.IsSuccessStatusCode)
        {
            var errorCode = await ReadErrorCodeAsync(response, cancellationToken);
            var status = response.StatusCode == HttpStatusCode.Conflict &&
                string.Equals(errorCode, "SYNC_CONFLICT", StringComparison.Ordinal)
                ? WebBestApiStatus.SyncConflict
                : WebBestApiStatus.PermanentError;
            response.Dispose();
            return new(status, null, errorCode ?? "REQUEST_REJECTED");
        }
        return new(WebBestApiStatus.Success, response, null);
    }

    private static async Task<string?> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(
                JsonOptions,
                cancellationToken);
            return error?.Error?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RawApiResult(
        WebBestApiStatus Status,
        HttpResponseMessage? Response,
        string? ErrorCode);

    private sealed record MergeRequest(
        [property: JsonPropertyName("projection_version")] int ProjectionVersion,
        [property: JsonPropertyName("master_version")] string MasterVersion,
        [property: JsonPropertyName("items")] IReadOnlyList<PlayerChartBestProjectionV1> Items);

    private sealed record DeltaResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<DeltaItemResponse> Results);

    private sealed record DeltaItemResponse(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("changed")] bool Changed,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("received_hash")] string? ReceivedHash);

    private sealed record SnapshotBeginRequest(
        [property: JsonPropertyName("projection_version")] int ProjectionVersion,
        [property: JsonPropertyName("master_version")] string MasterVersion,
        [property: JsonPropertyName("expected_item_count")] int ExpectedItemCount);

    private sealed record SnapshotBeginResponse(
        [property: JsonPropertyName("snapshot_id")] string SnapshotId,
        [property: JsonPropertyName("base_sync_revision")] long BaseSyncRevision);

    private sealed record SnapshotChunkRequest(
        [property: JsonPropertyName("chunk_id")] string ChunkId,
        [property: JsonPropertyName("items")] IReadOnlyList<PlayerChartBestProjectionV1> Items);

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] ErrorBody? Error);

    private sealed record ErrorBody(
        [property: JsonPropertyName("code")] string Code);
}
