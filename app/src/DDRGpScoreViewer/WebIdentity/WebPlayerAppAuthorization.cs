using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DDRGpScoreViewer.WebIdentity;

internal sealed record AppAuthorizationStartResult(
    PlayerIdentityRequestStatus Status,
    Uri? BrowserUri = null,
    string? ComparisonCode = null);

internal sealed partial class WebPlayerIdentityService
{
    public PendingAppAuthorization? PendingAuthorization => authorizationStore?.Load();

    public Uri ProfileUri => new(httpClient.BaseAddress!, "/my/profile");

    public async Task<AppAuthorizationStartResult> BeginAppAuthorizationAsync(
        bool unlink = false,
        CancellationToken cancellationToken = default)
    {
        if (authorizationStore is null)
        {
            return new(PlayerIdentityRequestStatus.InvalidState);
        }
        WebPlayerIdentitySnapshot identity;
        PendingAppAuthorization pending;
        try
        {
            identity = identityStore.Load();
            if (unlink && identity.State != PlayerIdentityState.Registered)
            {
                return new(PlayerIdentityRequestStatus.InvalidState);
            }
            var previous = authorizationStore.Load();
            if (previous is { State: AppAuthorizationState.CredentialActivationPending })
            {
                return new(PlayerIdentityRequestStatus.InvalidState);
            }
            if (previous is not null && previous.ExpiresAt <= authorizationNow())
            {
                authorizationStore.Clear();
                previous = null;
            }
            if (previous is not null)
            {
                if (previous.Purpose != (unlink ? "unlink" : "connect") ||
                    previous.ExpectedPublicPlayerId != identity.PublicPlayerId)
                {
                    return new(PlayerIdentityRequestStatus.InvalidState);
                }
                if (previous.BrowserUrl is not null)
                {
                    return new(PlayerIdentityRequestStatus.Succeeded,
                        ValidateAuthorizationUri(previous.BrowserUrl, previous.Id), previous.ComparisonCode);
                }
                pending = previous;
            }
            else
            {
                var now = authorizationNow();
                pending = new PendingAppAuthorization(Opaque(16), Opaque(32), unlink ? "unlink" : "connect",
                    identity.PublicPlayerId, now, now.AddMinutes(10));
                authorizationStore.Save(pending);
                if (authorizationStore.Load() != pending)
                    throw new InvalidDataException("App proof was not saved.");
            }
        }
        catch (Exception exception) when (IsLocalStorageException(exception))
        {
            return new(PlayerIdentityRequestStatus.LocalStorageError);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/app-authorizations")
        {
            Content = JsonContent.Create(new
            {
                id = pending.Id,
                request_secret = pending.RequestSecret,
                purpose = pending.Purpose,
                expected_public_player_id = pending.ExpectedPublicPlayerId,
            }, options: new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
        };
        if (identity.State == PlayerIdentityState.Registered)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.AppCredential);
        }
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (IsNetworkException(exception, cancellationToken))
        {
            return new(PlayerIdentityRequestStatus.NetworkError);
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && request.Headers.Authorization is not null)
            {
                try
                {
                    identityStore.SetAuthenticationInvalid(true);
                    authorizationStore.Clear();
                }
                catch (Exception exception) when (IsLocalStorageException(exception))
                {
                    return new(PlayerIdentityRequestStatus.LocalStorageError);
                }
                return new(PlayerIdentityRequestStatus.AuthenticationInvalid);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests
                    ? PlayerIdentityRequestStatus.ServerError : PlayerIdentityRequestStatus.InvalidResponse);
            }
            try
            {
                var result = await response.Content.ReadFromJsonAsync<AuthorizationStarted>(JsonOptions, cancellationToken);
                if (result is null || result.Id != pending.Id || result.ExpiresAt <= authorizationNow() ||
                    result.ExpiresAt > pending.StartedAt.AddMinutes(10).AddSeconds(30) ||
                    !Regex.IsMatch(result.ComparisonCode ?? "", "^[A-F0-9]{8}$"))
                {
                    return new(PlayerIdentityRequestStatus.InvalidResponse);
                }
                var uri = ValidateAuthorizationUri(result.Url, pending.Id);
                authorizationStore.Save(pending with
                {
                    BrowserUrl = uri.AbsoluteUri,
                    ComparisonCode = result.ComparisonCode,
                    ExpiresAt = result.ExpiresAt < pending.ExpiresAt ? result.ExpiresAt : pending.ExpiresAt,
                });
                return new(PlayerIdentityRequestStatus.Succeeded, uri, result.ComparisonCode);
            }
            catch (JsonException)
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse);
            }
            catch (Exception exception) when (IsLocalStorageException(exception))
            {
                return new(PlayerIdentityRequestStatus.LocalStorageError);
            }
        }
    }

    public async Task<PlayerIdentityOperationResult> CompleteAppAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (authorizationStore?.Load() is not { } pending)
            {
                return new(PlayerIdentityRequestStatus.InvalidState, identityStore.Load());
            }
            if (pending.State == AppAuthorizationState.CredentialActivationPending)
            {
                return await ActivateSavedAuthorizationAsync(pending, cancellationToken);
            }
            while (authorizationNow() < pending.ExpiresAt)
            {
                using var request = ProofRequest(pending, "result");
                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(request, cancellationToken);
                }
                catch (Exception exception) when (IsNetworkException(exception, cancellationToken))
                {
                    return new(PlayerIdentityRequestStatus.NetworkError, identityStore.Load());
                }
                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var delay = response.Headers.RetryAfter?.Delta ??
                            (response.Headers.RetryAfter?.Date - authorizationNow()) ?? TimeSpan.FromSeconds(5);
                        if (authorizationNow() + delay >= pending.ExpiresAt)
                        {
                            return new(PlayerIdentityRequestStatus.ServerError, identityStore.Load());
                        }
                        await authorizationDelay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(5), cancellationToken);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        return new((int)response.StatusCode >= 500 ? PlayerIdentityRequestStatus.ServerError : PlayerIdentityRequestStatus.InvalidResponse,
                            identityStore.Load());
                    }
                    var result = await response.Content.ReadFromJsonAsync<AuthorizationResult>(JsonOptions, cancellationToken);
                    if (result is null)
                    {
                        return new(PlayerIdentityRequestStatus.InvalidResponse, identityStore.Load());
                    }
                    if (result.Status is "LINKED" or "UNLINKED")
                    {
                        if ((result.Status == "UNLINKED") != (pending.Purpose == "unlink") ||
                            result.PublicPlayerId != pending.ExpectedPublicPlayerId ||
                            identityStore.Load().State != PlayerIdentityState.Registered)
                            return new(PlayerIdentityRequestStatus.InvalidResponse, identityStore.Load());
                        authorizationStore.Clear();
                        GoogleLinked = result.Status == "LINKED";
                        return new(PlayerIdentityRequestStatus.Succeeded, identityStore.Load(),
                            result.ToPlayer());
                    }
                    if (result.Status is "APPROVED" or "ACTIVATED")
                    {
                        if (pending.Purpose != "connect" || result.Credential is null || !Regex.IsMatch(result.Credential, "^ac_[A-Za-z0-9_-]{20,64}\\.[A-Za-z0-9_-]{43}$") ||
                            result.PublicPlayerId is null || !Regex.IsMatch(result.PublicPlayerId, "^p_[A-Za-z0-9_-]{20,64}$") ||
                            string.IsNullOrWhiteSpace(result.DisplayName) || result.DisplayName.EnumerateRunes().Count() > 64 || result.ToPlayer() is not { } player ||
                            pending.ExpectedPublicPlayerId is not null && pending.ExpectedPublicPlayerId != player.PublicPlayerId)
                        {
                            return new(PlayerIdentityRequestStatus.InvalidResponse, identityStore.Load());
                        }
                        pending = pending with
                        {
                            State = AppAuthorizationState.CredentialActivationPending,
                            Credential = result.Credential,
                            Player = player
                        };
                        authorizationStore.Save(pending);
                        var saved = authorizationStore.Load();
                        if (saved != pending)
                        {
                            return new(PlayerIdentityRequestStatus.LocalStorageError, identityStore.Load());
                        }
                        return await ActivateSavedAuthorizationAsync(saved, cancellationToken);
                    }
                    if (result.Status != "PENDING")
                    {
                        authorizationStore.Clear();
                        return new(PlayerIdentityRequestStatus.InvalidState, identityStore.Load());
                    }
                }
                var remaining = pending.ExpiresAt - authorizationNow();
                await authorizationDelay(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5), cancellationToken);
            }
            authorizationStore.Clear();
            return new(PlayerIdentityRequestStatus.InvalidState, identityStore.Load());
        }
        catch (JsonException)
        {
            return new(PlayerIdentityRequestStatus.InvalidResponse, identityStore.Load());
        }
        catch (Exception exception) when (IsLocalStorageException(exception))
        {
            return StorageFailure();
        }
    }

    private async Task<PlayerIdentityOperationResult> ActivateSavedAuthorizationAsync(
        PendingAppAuthorization pending,
        CancellationToken cancellationToken)
    {
        if (pending.Credential is null || pending.Player is null)
        {
            return new(PlayerIdentityRequestStatus.InvalidResponse, identityStore.Load());
        }
        var current = identityStore.Load();
        if (current.PublicPlayerId is not null && current.PublicPlayerId != pending.Player.PublicPlayerId)
        {
            return new(PlayerIdentityRequestStatus.InvalidResponse, current);
        }
        using var request = ProofRequest(pending, "activate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pending.Credential);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (IsNetworkException(exception, cancellationToken))
        {
            return new(PlayerIdentityRequestStatus.NetworkError, identityStore.Load());
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
                {
                    using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                        cancellationToken: cancellationToken);
                    if (error.RootElement.TryGetProperty("error", out var details) && details.ValueKind == JsonValueKind.Object &&
                        details.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String &&
                        (response.StatusCode, code.GetString()) is
                            (HttpStatusCode.Unauthorized, "UNAUTHORIZED") or
                            (HttpStatusCode.NotFound, "AUTHORIZATION_NOT_FOUND") or
                            (HttpStatusCode.Conflict, "AUTHORIZATION_FINISHED" or "AUTHORIZATION_EXPIRED"))
                    {
                        identityStore.SaveRegistered(pending.Player.PublicPlayerId, pending.Credential, pending.Player.DisplayName);
                        identityStore.SetAuthenticationInvalid(true);
                        var invalid = identityStore.Load();
                        if (invalid.State != PlayerIdentityState.AuthInvalid || invalid.PublicPlayerId != pending.Player.PublicPlayerId ||
                            invalid.AppCredential != pending.Credential || invalid.DisplayName != pending.Player.DisplayName)
                        {
                            return new(PlayerIdentityRequestStatus.LocalStorageError, invalid);
                        }
                        authorizationStore!.Clear();
                        return new(PlayerIdentityRequestStatus.AuthenticationInvalid, invalid);
                    }
                }
                return new((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests
                    ? PlayerIdentityRequestStatus.ServerError : PlayerIdentityRequestStatus.InvalidResponse, identityStore.Load());
            }
            var result = await response.Content.ReadFromJsonAsync<AuthorizationResult>(JsonOptions, cancellationToken);
            if (result?.Status != "ACTIVATED")
            {
                return new(PlayerIdentityRequestStatus.InvalidResponse, identityStore.Load());
            }
            identityStore.SaveRegistered(pending.Player.PublicPlayerId, pending.Credential, pending.Player.DisplayName);
            var saved = identityStore.Load();
            if (saved.State != PlayerIdentityState.Registered || saved.AppCredential != pending.Credential ||
                saved.PublicPlayerId != pending.Player.PublicPlayerId)
            {
                return new(PlayerIdentityRequestStatus.LocalStorageError, saved);
            }
            authorizationStore!.Clear();
            GoogleLinked = true;
            return new(PlayerIdentityRequestStatus.Succeeded, saved, pending.Player);
        }
    }

    public async Task CancelAppAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        if (authorizationStore?.Load() is not { } pending)
        {
            return;
        }
        if (pending.State == AppAuthorizationState.CredentialActivationPending)
        {
            // This proof may already be active. Retain it for result recovery.
            return;
        }
        using var request = ProofRequest(pending, "cancel");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<AuthorizationResult>(JsonOptions, cancellationToken);
            if (result?.Status == "CANCELLED")
            {
                authorizationStore.Clear();
            }
        }
    }

    private static HttpRequestMessage ProofRequest(PendingAppAuthorization pending, string action) =>
        new(HttpMethod.Post, $"api/v1/auth/app-authorizations/{Uri.EscapeDataString(pending.Id)}/{action}")
        {
            Content = JsonContent.Create(new { request_secret = pending.RequestSecret }),
        };

    private Uri ValidateAuthorizationUri(string url, string id)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) !=
            httpClient.BaseAddress!.GetLeftPart(UriPartial.Authority) || uri.AbsolutePath != "/my/app-connect" ||
            uri.Query != "?request=" + Uri.EscapeDataString(id) || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
        {
            throw new InvalidDataException("The App authorization URL is invalid.");
        }
        return uri;
    }

    private static string Opaque(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record AuthorizationStarted(
        [property: JsonPropertyName("authorization_id")] string Id,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("comparison_code")] string ComparisonCode,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

    private sealed record AuthorizationResult(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("credential")] string? Credential,
        [property: JsonPropertyName("public_player_id")] string? PublicPlayerId,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt)
    {
        public WebPlayer? ToPlayer() => PublicPlayerId is null || DisplayName is null ? null :
            new(PublicPlayerId, DisplayName, CreatedAt, UpdatedAt);
    }
}
