using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDRGpScoreViewer.Data;

namespace DDRGpScoreViewer.WebIdentity;

internal sealed class WebPlayerIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };
    private readonly HttpClient httpClient;
    private readonly IWebPlayerIdentityStore identityStore;

    public WebPlayerIdentityService(
        HttpClient httpClient,
        IWebPlayerIdentityStore identityStore)
    {
        this.httpClient = httpClient;
        this.identityStore = identityStore;
        if (httpClient.BaseAddress is null ||
            !string.Equals(httpClient.BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Player identity API base address must use HTTPS.",
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
                paths.WebPlayerCredentialPath));

    public WebPlayerIdentitySnapshot LoadIdentity() => identityStore.Load();

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

    public async Task<PlayerIdentityOperationResult> RegisterAsync(
        string displayName,
        CancellationToken cancellationToken = default)
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
        if (identity.State != PlayerIdentityState.Unregistered)
        {
            return new(PlayerIdentityRequestStatus.InvalidState, identity);
        }

        var requestId = identity.PendingRegistrationRequestId ??
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        try
        {
            identityStore.SavePendingRegistration(requestId);
            identity = identityStore.Load();
        }
        catch (Exception exception) when (IsLocalStorageException(exception))
        {
            return StorageFailure();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "api/v1/players/register");
        request.Headers.Add("Idempotency-Key", requestId);
        request.Content = JsonContent.Create(new RegistrationRequest(displayName), options: JsonOptions);

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
            if ((int)response.StatusCode >= 500)
            {
                return new(PlayerIdentityRequestStatus.ServerError, identity);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse, identity);
            }

            RegistrationResponse? registration;
            try
            {
                registration = await response.Content.ReadFromJsonAsync<RegistrationResponse>(
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse, identity);
            }
            if (!IsValidRegistration(registration))
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse, identity);
            }
            var validRegistration = registration!;
            try
            {
                identityStore.SaveRegistered(
                    validRegistration.PublicPlayerId,
                    validRegistration.Credential,
                    validRegistration.DisplayName);
                var saved = identityStore.Load();
                return new(
                    PlayerIdentityRequestStatus.Succeeded,
                    saved,
                    ToPlayer(validRegistration));
            }
            catch (Exception exception) when (IsLocalStorageException(exception))
            {
                return StorageFailure();
            }
        }
    }

    public Task<PlayerIdentityOperationResult> GetCurrentPlayerAsync(
        CancellationToken cancellationToken = default) =>
        SendAuthenticatedAsync(
            HttpMethod.Get,
            content: null,
            clearAfterSuccess: false,
            cancellationToken);

    public Task<PlayerIdentityOperationResult> UpdateDisplayNameAsync(
        string displayName,
        CancellationToken cancellationToken = default) =>
        SendAuthenticatedAsync(
            HttpMethod.Patch,
            JsonContent.Create(new RegistrationRequest(displayName), options: JsonOptions),
            clearAfterSuccess: false,
            cancellationToken);

    public Task<PlayerIdentityOperationResult> DeletePlayerAsync(
        CancellationToken cancellationToken = default) =>
        SendAuthenticatedAsync(
            HttpMethod.Delete,
            content: null,
            clearAfterSuccess: true,
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
        }
        catch (Exception exception) when (IsLocalStorageException(exception))
        {
            content?.Dispose();
            return StorageFailure();
        }
        if (identity.AppCredential is null || identity.PublicPlayerId is null)
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
            if ((int)response.StatusCode >= 500)
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
                PlayerIdentityState.Unregistered,
                null,
                null,
                null);
        }
        return new(PlayerIdentityRequestStatus.LocalStorageError, fallback);
    }

    private static bool IsValidRegistration(RegistrationResponse? response) =>
        response is not null &&
        !string.IsNullOrWhiteSpace(response.PublicPlayerId) &&
        !string.IsNullOrWhiteSpace(response.Credential) &&
        !string.IsNullOrWhiteSpace(response.DisplayName);

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

    private sealed record RegistrationRequest(
        [property: JsonPropertyName("display_name")] string DisplayName);

    private record PlayerResponse(
        [property: JsonPropertyName("public_player_id")] string PublicPlayerId,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

    private sealed record RegistrationResponse(
        string PublicPlayerId,
        string DisplayName,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("credential")] string Credential) :
        PlayerResponse(PublicPlayerId, DisplayName, CreatedAt, UpdatedAt);
}
