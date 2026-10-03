using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;

namespace DDRGpScoreViewer.WebIdentity;

internal sealed partial class WebPlayerIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };
    private readonly HttpClient httpClient;
    private readonly IWebPlayerIdentityStore identityStore;
    private readonly IAppAuthorizationStore? authorizationStore;
    private readonly Func<TimeSpan, CancellationToken, Task> authorizationDelay;
    private readonly Func<DateTimeOffset> authorizationNow;

    public WebPlayerIdentityService(
        HttpClient httpClient,
        IWebPlayerIdentityStore identityStore,
        bool allowLoopbackHttp = false,
        IAppAuthorizationStore? authorizationStore = null,
        Func<TimeSpan, CancellationToken, Task>? authorizationDelay = null,
        Func<DateTimeOffset>? authorizationNow = null)
    {
        this.httpClient = httpClient;
        this.identityStore = identityStore;
        this.authorizationStore = authorizationStore;
        this.authorizationDelay = authorizationDelay ?? Task.Delay;
        this.authorizationNow = authorizationNow ?? (() => DateTimeOffset.UtcNow);
        if (httpClient.BaseAddress is null ||
            !(httpClient.BaseAddress.Scheme == Uri.UriSchemeHttps ||
              allowLoopbackHttp && httpClient.BaseAddress.Scheme == Uri.UriSchemeHttp &&
              httpClient.BaseAddress.IsLoopback))
        {
            throw new ArgumentException(
                "The Player identity API requires HTTPS, or loopback HTTP in development.",
                nameof(httpClient));
        }
    }

    public static WebPlayerIdentityService CreateForPaths(
        HttpClient httpClient,
        ViewerDatabasePaths paths) =>
        new(
            httpClient,
            new FileWebPlayerIdentityStore(
                paths.WebPlayerIdentityPath,
                paths.WebPlayerCredentialPath),
            allowLoopbackHttp: paths.Environment == ViewerDatabaseEnvironment.Development,
            authorizationStore: new FileAppAuthorizationStore(paths.WebPlayerCredentialPath + ".authorization"));

    public WebPlayerIdentitySnapshot LoadIdentity() => identityStore.Load();

    public bool? GoogleLinked { get; private set; }

    public PlayerIdentityOperationResult ForgetInvalidIdentity()
    {
        WebPlayerIdentitySnapshot identity;
        try
        {
            identity = identityStore.Load();
        }
        catch (Exception exception) when (IsLocalStorageException(exception))
        {
            return StorageFailure();
        }
        if (identity.State != PlayerIdentityState.AuthInvalid)
        {
            return new(PlayerIdentityRequestStatus.InvalidState, identity);
        }

        try
        {
            authorizationStore?.Clear();
            identityStore.Clear();
            return new(
                PlayerIdentityRequestStatus.Succeeded,
                identityStore.Load());
        }
        catch (Exception exception) when (IsLocalStorageException(exception))
        {
            return StorageFailure();
        }
    }

    public Task<PlayerIdentityOperationResult> GetCurrentPlayerAsync(
        CancellationToken cancellationToken = default) =>
        SendAuthenticatedAsync(
            HttpMethod.Get,
            content: null,
            clearAfterSuccess: false,
            cancellationToken);

    private async Task<PlayerIdentityOperationResult> SendAuthenticatedAsync(
        HttpMethod method,
        HttpContent? content,
        bool clearAfterSuccess,
        CancellationToken cancellationToken)
    {
        WebPlayerIdentitySnapshot identity;
        try
        {
            identity = identityStore.Load();
            if (authorizationStore?.Load()?.State == AppAuthorizationState.CredentialActivationPending)
            {
                content?.Dispose();
                return new(PlayerIdentityRequestStatus.InvalidState, identity);
            }
        }
        catch (Exception exception) when (IsLocalStorageException(exception))
        {
            content?.Dispose();
            return StorageFailure();
        }
        if (identity.State != PlayerIdentityState.Registered || identity.AppCredential is null || identity.PublicPlayerId is null)
        {
            content?.Dispose();
            return new(PlayerIdentityRequestStatus.InvalidState, identity);
        }

        using var request = new HttpRequestMessage(method, "api/v1/me")
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            identity.AppCredential);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (IsNetworkException(exception, cancellationToken))
        {
            return new(PlayerIdentityRequestStatus.NetworkError, identity);
        }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                try
                {
                    identityStore.SetAuthenticationInvalid(true);
                    return new(
                        PlayerIdentityRequestStatus.AuthenticationInvalid,
                        identityStore.Load());
                }
                catch (Exception exception) when (IsLocalStorageException(exception))
                {
                    return StorageFailure();
                }
            }
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new(PlayerIdentityRequestStatus.ServerError, identity);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse, identity);
            }

            if (clearAfterSuccess)
            {
                try
                {
                    identityStore.Clear();
                    return new(
                        PlayerIdentityRequestStatus.Succeeded,
                        identityStore.Load());
                }
                catch (Exception exception) when (IsLocalStorageException(exception))
                {
                    return StorageFailure();
                }
            }

            PlayerResponse? player;
            try
            {
                player = await response.Content.ReadFromJsonAsync<PlayerResponse>(
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse, identity);
            }
            if (!IsValidPlayer(player))
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse, identity);
            }
            var validPlayer = player!;
            if (!string.Equals(
                    validPlayer.PublicPlayerId,
                    identity.PublicPlayerId,
                    StringComparison.Ordinal))
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse, identity);
            }
            try
            {
                identityStore.SetDisplayName(validPlayer.DisplayName);
                identityStore.SetAuthenticationInvalid(false);
                GoogleLinked = validPlayer.GoogleLinked;
                return new(
                    PlayerIdentityRequestStatus.Succeeded,
                    identityStore.Load(),
                    ToPlayer(validPlayer));
            }
            catch (Exception exception) when (IsLocalStorageException(exception))
            {
                return StorageFailure();
            }
        }
    }

    private PlayerIdentityOperationResult StorageFailure()
    {
        WebPlayerIdentitySnapshot fallback;
        try
        {
            fallback = identityStore.Load();
        }
        catch
        {
            fallback = new WebPlayerIdentitySnapshot(
                PlayerIdentityState.AuthInvalid,
                null,
                null,
                null);
        }
        return new(PlayerIdentityRequestStatus.LocalStorageError, fallback);
    }

    private static bool IsValidPlayer(PlayerResponse? response) =>
        response is not null &&
        !string.IsNullOrWhiteSpace(response.PublicPlayerId) &&
        !string.IsNullOrWhiteSpace(response.DisplayName);

    private static WebPlayer ToPlayer(PlayerResponse response) =>
        new(
            response.PublicPlayerId,
            response.DisplayName,
            response.CreatedAt,
            response.UpdatedAt);

    private static bool IsNetworkException(
        Exception exception,
        CancellationToken cancellationToken) =>
        exception is HttpRequestException or TimeoutException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static bool IsLocalStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
        CryptographicException or JsonException or InvalidDataException or
        InvalidOperationException;

    private record PlayerResponse(
        [property: JsonPropertyName("public_player_id")] string PublicPlayerId,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("google_linked")] bool? GoogleLinked = null);

}
