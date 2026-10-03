using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DDRGpScoreViewer.WebIdentity;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class WebPlayerIdentityTests
{
    private const string PublicPlayerId = "p_abcdefghijklmnopqrstuv";
    private const string Credential =
        "ac_abcdefghijklmnopqrstuv.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void Dpapi_store_round_trip_survives_restart_without_plaintext_secret()
    {
        using var directory = new TemporaryDirectory();
        var metadataPath = Path.Combine(directory.Path, "web-player-identity.json");
        var credentialPath = Path.Combine(directory.Path, "web-player-credential.bin");
        var store = new FileWebPlayerIdentityStore(metadataPath, credentialPath);

        store.SaveRegistered(PublicPlayerId, Credential, "Local player");

        Assert.DoesNotContain(Credential, File.ReadAllText(metadataPath));
        Assert.DoesNotContain(
            Credential,
            Encoding.UTF8.GetString(File.ReadAllBytes(credentialPath)));
        var restarted = new FileWebPlayerIdentityStore(metadataPath, credentialPath).Load();
        Assert.Equal(PlayerIdentityState.Registered, restarted.State);
        Assert.Equal(PublicPlayerId, restarted.PublicPlayerId);
        Assert.Equal(Credential, restarted.AppCredential);
        Assert.Equal("Local player", restarted.DisplayName);
        Assert.DoesNotContain(Credential, restarted.ToString());
    }

    [Fact]
    public void Pending_registration_request_survives_restart_without_plaintext_secret()
    {
        using var directory = new TemporaryDirectory();
        var metadataPath = Path.Combine(directory.Path, "web-player-identity.json");
        var credentialPath = Path.Combine(directory.Path, "web-player-credential.bin");
        const string requestId = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var store = new FileWebPlayerIdentityStore(metadataPath, credentialPath);

        store.SavePendingRegistration(requestId);

        Assert.DoesNotContain(requestId, File.ReadAllText(metadataPath));
        Assert.False(File.Exists(credentialPath));
        var restarted = new FileWebPlayerIdentityStore(metadataPath, credentialPath).Load();
        Assert.Equal(PlayerIdentityState.Unregistered, restarted.State);
        Assert.Equal(requestId, restarted.PendingRegistrationRequestId);
    }

    [Fact]
    public async Task Auth_invalid_relogin_sends_only_expected_id_and_cannot_switch_player()
    {
        var store = MemoryWebPlayerIdentityStore.Registered();
        store.SetAuthenticationInvalid(true);
        var pending = new MemoryAuthorizationStore();
        var handler = new DelegatingHandlerStub(async (request, _, token) =>
        {
            Assert.Null(request.Headers.Authorization);
            if (request.RequestUri!.AbsolutePath.EndsWith("/result"))
                return JsonResponse(HttpStatusCode.OK, AuthorizationJson("APPROVED", "p_XXXXXXXXXXXXXXXXXXXXXX"));
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(PublicPlayerId, body.RootElement.GetProperty("expected_public_player_id").GetString());
            return StartedResponse(body.RootElement.GetProperty("id").GetString()!);
        });
        var service = CreateService(handler, store, pending);
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, (await service.BeginAppAuthorizationAsync()).Status);
        var result = await service.CompleteAppAuthorizationAsync();
        Assert.Equal(PlayerIdentityRequestStatus.InvalidResponse, result.Status);
        Assert.Equal(PublicPlayerId, store.Load().PublicPlayerId);
        Assert.Equal(Credential, store.Load().AppCredential);
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task Registered_401_ends_transaction_before_explicit_relogin()
    {
        var store = MemoryWebPlayerIdentityStore.Registered();
        var pending = new MemoryAuthorizationStore();
        var handler = new DelegatingHandlerStub((request, _, _) =>
        {
            Assert.Equal(Credential, request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        var result = await CreateService(handler, store, pending).BeginAppAuthorizationAsync();
        Assert.Equal(PlayerIdentityRequestStatus.AuthenticationInvalid, result.Status);
        Assert.Equal(PlayerIdentityState.AuthInvalid, store.Load().State);
        Assert.Null(pending.Load());
        Assert.Equal(1, handler.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Proof_save_or_readback_failure_prevents_start_and_activation(bool readback)
    {
        var identity = new MemoryWebPlayerIdentityStore();
        var pending = new MemoryAuthorizationStore { FailSave = !readback, CorruptReadback = readback };
        var handler = new DelegatingHandlerStub((_, _, _) => throw new Exception("No request allowed"));
        var result = await CreateService(handler, identity, pending).BeginAppAuthorizationAsync();
        Assert.Equal(PlayerIdentityRequestStatus.LocalStorageError, result.Status);
        Assert.Equal(0, handler.Attempts);
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
    }

    [Fact]
    public async Task Received_credential_must_be_saved_and_read_back_before_activation()
    {
        var identity = new MemoryWebPlayerIdentityStore();
        var pending = PendingStore();
        pending.FailCredentialReadback = true;
        var handler = new DelegatingHandlerStub((request, _, _) =>
        {
            Assert.EndsWith("/result", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, AuthorizationJson("APPROVED")));
        });
        var result = await CreateService(handler, identity, pending).CompleteAppAuthorizationAsync();
        Assert.Equal(PlayerIdentityRequestStatus.LocalStorageError, result.Status);
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Activation_response_loss_recovers_same_saved_credential_after_restart_and_TTL()
    {
        var identity = new MemoryWebPlayerIdentityStore();
        var pending = PendingStore();
        var handler = new DelegatingHandlerStub((request, attempt, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/result"))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, AuthorizationJson("APPROVED")));
            Assert.Equal(AppAuthorizationState.CredentialActivationPending, pending.Load()!.State);
            Assert.Equal(Credential, pending.Load()!.Credential);
            Assert.Equal(Credential, request.Headers.Authorization?.Parameter);
            if (attempt == 2) throw new HttpRequestException("response lost");
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"status\":\"ACTIVATED\"}"));
        });
        var first = await CreateService(handler, identity, pending).CompleteAppAuthorizationAsync();
        Assert.Equal(PlayerIdentityRequestStatus.NetworkError, first.Status);
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
        var restarted = new WebPlayerIdentityService(new HttpClient(handler) { BaseAddress = new("https://identity.example.test/") },
            identity, authorizationStore: pending, authorizationNow: () => DateTimeOffset.UtcNow.AddHours(1));
        var recovered = await restarted.CompleteAppAuthorizationAsync();
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, recovered.Status);
        Assert.Equal(PublicPlayerId, identity.Load().PublicPlayerId);
        Assert.Null(pending.Load());
        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task Never_reached_activation_recovers_expired_DPAPI_proof_as_same_player_relogin()
    {
        using var directory = new TemporaryDirectory();
        var metadataPath = Path.Combine(directory.Path, "identity.json");
        var credentialPath = Path.Combine(directory.Path, "credential.bin");
        var proofPath = Path.Combine(directory.Path, "authorization.bin");
        var identity = new FileWebPlayerIdentityStore(metadataPath, credentialPath);
        var pending = new FileAppAuthorizationStore(proofPath);
        pending.Save(PendingStore().Value!);
        var handler = new DelegatingHandlerStub(async (request, attempt, token) =>
        {
            if (attempt == 1)
                return JsonResponse(HttpStatusCode.OK, AuthorizationJson("APPROVED"));
            if (attempt == 2)
                throw new HttpRequestException("activation never reached server");
            if (attempt == 3)
            {
                Assert.EndsWith("/activate", request.RequestUri!.AbsolutePath);
                Assert.Equal(Credential, request.Headers.Authorization?.Parameter);
                return JsonResponse(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"UNAUTHORIZED\"}}");
            }
            Assert.Null(request.Headers.Authorization);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(PublicPlayerId, body.RootElement.GetProperty("expected_public_player_id").GetString());
            return StartedResponse(body.RootElement.GetProperty("id").GetString()!);
        });
        Assert.Equal(PlayerIdentityRequestStatus.NetworkError,
            (await CreateService(handler, identity, pending).CompleteAppAuthorizationAsync()).Status);
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
        Assert.Equal(AppAuthorizationState.CredentialActivationPending, pending.Load()!.State);
        var restartedIdentity = new FileWebPlayerIdentityStore(metadataPath, credentialPath);
        var restartedPending = new FileAppAuthorizationStore(proofPath);
        var restarted = new WebPlayerIdentityService(new HttpClient(handler) { BaseAddress = new("https://identity.example.test/") },
            restartedIdentity, authorizationStore: restartedPending, authorizationNow: () => DateTimeOffset.UtcNow.AddHours(1));

        var recovered = await restarted.CompleteAppAuthorizationAsync();

        Assert.Equal(PlayerIdentityRequestStatus.AuthenticationInvalid, recovered.Status);
        Assert.Equal(PlayerIdentityState.AuthInvalid, restartedIdentity.Load().State);
        Assert.Equal(PublicPlayerId, restartedIdentity.Load().PublicPlayerId);
        Assert.Equal("Player", restartedIdentity.Load().DisplayName);
        Assert.Null(restartedPending.Load());
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded,
            (await CreateService(handler, restartedIdentity, restartedPending).BeginAppAuthorizationAsync()).Status);
        Assert.Equal(4, handler.Attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHORIZED", false)]
    [InlineData(HttpStatusCode.NotFound, "AUTHORIZATION_NOT_FOUND", false)]
    [InlineData(HttpStatusCode.Conflict, "AUTHORIZATION_FINISHED", false)]
    [InlineData(HttpStatusCode.Conflict, "AUTHORIZATION_EXPIRED", false)]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHORIZED", true)]
    [InlineData(HttpStatusCode.NotFound, "AUTHORIZATION_NOT_FOUND", true)]
    [InlineData(HttpStatusCode.Conflict, "AUTHORIZATION_FINISHED", true)]
    [InlineData(HttpStatusCode.Conflict, "AUTHORIZATION_EXPIRED", true)]
    public async Task Terminal_activation_preserves_player_and_allows_only_same_player_relogin(
        HttpStatusCode status, string code, bool alreadyRegistered)
    {
        var identity = alreadyRegistered ? MemoryWebPlayerIdentityStore.Registered() : new MemoryWebPlayerIdentityStore();
        var pending = ActivationPendingStore();
        var originalId = pending.Value!.Id;
        var handler = new DelegatingHandlerStub(async (request, attempt, token) =>
        {
            if (attempt == 1)
            {
                Assert.EndsWith("/activate", request.RequestUri!.AbsolutePath);
                Assert.Equal(Credential, request.Headers.Authorization?.Parameter);
                return JsonResponse(status, JsonSerializer.Serialize(new { error = new { code } }));
            }
            Assert.Null(request.Headers.Authorization);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(PublicPlayerId, body.RootElement.GetProperty("expected_public_player_id").GetString());
            Assert.NotEqual(originalId, body.RootElement.GetProperty("id").GetString());
            return StartedResponse(body.RootElement.GetProperty("id").GetString()!);
        });
        var service = CreateService(handler, identity, pending);

        var result = await service.CompleteAppAuthorizationAsync();

        Assert.Equal(PlayerIdentityRequestStatus.AuthenticationInvalid, result.Status);
        Assert.Equal(PlayerIdentityState.AuthInvalid, result.Identity.State);
        Assert.Equal(PublicPlayerId, result.Identity.PublicPlayerId);
        Assert.Equal("Recovered player", result.Identity.DisplayName);
        Assert.Null(pending.Load());
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, (await service.BeginAppAuthorizationAsync()).Status);
        Assert.Equal(PublicPlayerId, pending.Load()!.ExpectedPublicPlayerId);
        Assert.Equal(2, handler.Attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UNAUTHORIZED", true)]
    [InlineData(HttpStatusCode.TooManyRequests, "UNAUTHORIZED", true)]
    [InlineData(HttpStatusCode.Conflict, "AUTHORIZATION_CONFLICT", false)]
    [InlineData(HttpStatusCode.Conflict, "UNAUTHORIZED", false)]
    [InlineData(HttpStatusCode.Unauthorized, "AUTHORIZATION_EXPIRED", false)]
    [InlineData(HttpStatusCode.NotFound, "OTHER_NOT_FOUND", false)]
    public async Task Nonterminal_activation_errors_retain_saved_proof_and_block_new_start(
        HttpStatusCode status, string code, bool serverError)
    {
        var identity = MemoryWebPlayerIdentityStore.Registered();
        var original = identity.Load();
        var pending = ActivationPendingStore();
        var proof = pending.Value;
        var handler = new DelegatingHandlerStub((_, _, _) =>
            Task.FromResult(JsonResponse(status, JsonSerializer.Serialize(new { error = new { code } }))));
        var service = CreateService(handler, identity, pending);

        Assert.Equal(serverError ? PlayerIdentityRequestStatus.ServerError : PlayerIdentityRequestStatus.InvalidResponse,
            (await service.CompleteAppAuthorizationAsync()).Status);
        Assert.Equal(proof, pending.Load());
        Assert.Equal(original.State, identity.Load().State);
        Assert.Equal(original.PublicPlayerId, identity.Load().PublicPlayerId);
        Assert.Equal(original.AppCredential, identity.Load().AppCredential);
        Assert.Equal(original.DisplayName, identity.Load().DisplayName);
        Assert.Equal(PlayerIdentityRequestStatus.InvalidState, (await service.BeginAppAuthorizationAsync()).Status);
        await service.CancelAppAuthorizationAsync();
        Assert.Equal(proof, pending.Load());
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Activation_network_failure_keeps_saved_proof_after_expiry()
    {
        var identity = new MemoryWebPlayerIdentityStore();
        var pending = ActivationPendingStore();
        var proof = pending.Value;
        var handler = new DelegatingHandlerStub((_, _, _) => throw new HttpRequestException("offline"));
        var service = CreateService(handler, identity, pending);
        Assert.Equal(PlayerIdentityRequestStatus.NetworkError, (await service.CompleteAppAuthorizationAsync()).Status);
        Assert.Equal(proof, pending.Load());
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
        Assert.Equal(PlayerIdentityRequestStatus.InvalidState, (await service.BeginAppAuthorizationAsync()).Status);
        Assert.Equal(1, handler.Attempts);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("invalid-state")]
    [InlineData("readback")]
    [InlineData("clear")]
    public async Task Terminal_activation_local_failure_retains_proof_and_old_player_until_verified(string failure)
    {
        var identity = MemoryWebPlayerIdentityStore.Registered();
        identity.FailSave = failure == "save";
        identity.FailAuthenticationInvalid = failure == "invalid-state";
        identity.CorruptAuthenticationReadback = failure == "readback";
        var pending = ActivationPendingStore();
        pending.FailClear = failure == "clear";
        var proof = pending.Value;
        var handler = new DelegatingHandlerStub((_, _, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"UNAUTHORIZED\"}}")));
        var service = CreateService(handler, identity, pending);

        var failed = await service.CompleteAppAuthorizationAsync();

        Assert.Equal(PlayerIdentityRequestStatus.LocalStorageError, failed.Status);
        Assert.Equal(PublicPlayerId, failed.Identity.PublicPlayerId);
        Assert.Equal(proof, pending.Load());
        Assert.Equal(PlayerIdentityRequestStatus.InvalidState, (await service.BeginAppAuthorizationAsync()).Status);
        identity.FailSave = false;
        identity.FailAuthenticationInvalid = false;
        identity.CorruptAuthenticationReadback = false;
        pending.FailClear = false;
        Assert.Equal(PlayerIdentityRequestStatus.AuthenticationInvalid, (await service.CompleteAppAuthorizationAsync()).Status);
        Assert.Equal(PlayerIdentityState.AuthInvalid, identity.Load().State);
        Assert.Equal(PublicPlayerId, identity.Load().PublicPlayerId);
        Assert.Null(pending.Load());
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task Saved_activation_never_overwrites_a_different_readable_existing_player()
    {
        var identity = MemoryWebPlayerIdentityStore.Registered();
        identity.SaveRegistered("p_XXXXXXXXXXXXXXXXXXXXXX", Credential, "Existing player");
        var original = identity.Load();
        var pending = ActivationPendingStore();
        var proof = pending.Value;
        var handler = new DelegatingHandlerStub((_, _, _) => throw new Exception("No request allowed"));
        var result = await CreateService(handler, identity, pending).CompleteAppAuthorizationAsync();
        Assert.Equal(PlayerIdentityRequestStatus.InvalidResponse, result.Status);
        Assert.Equal(original.State, identity.Load().State);
        Assert.Equal(original.PublicPlayerId, identity.Load().PublicPlayerId);
        Assert.Equal(original.AppCredential, identity.Load().AppCredential);
        Assert.Equal(original.DisplayName, identity.Load().DisplayName);
        Assert.Equal(proof, pending.Load());
        Assert.Equal(0, handler.Attempts);
    }

    [Theory]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(65)]
    public async Task Received_astral_name_uses_unicode_scalar_limit_before_activation(int scalarCount)
    {
        var name = string.Concat(Enumerable.Repeat("\U0001F600", scalarCount));
        var identity = new MemoryWebPlayerIdentityStore();
        var pending = PendingStore();
        var handler = new DelegatingHandlerStub((request, _, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            request.RequestUri!.AbsolutePath.EndsWith("/activate") ? "{\"status\":\"ACTIVATED\"}" :
                AuthorizationJson("APPROVED", displayName: name))));
        var result = await CreateService(handler, identity, pending).CompleteAppAuthorizationAsync();
        if (scalarCount <= 64)
        {
            Assert.Equal(PlayerIdentityRequestStatus.Succeeded, result.Status);
            Assert.Equal(name, identity.Load().DisplayName);
            Assert.Null(pending.Load());
            Assert.Equal(2, handler.Attempts);
        }
        else
        {
            Assert.Equal(PlayerIdentityRequestStatus.InvalidResponse, result.Status);
            Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
            Assert.Equal(AppAuthorizationState.AppAuthorizationPending, pending.Load()!.State);
            Assert.Null(pending.Load()!.Credential);
            Assert.Equal(1, handler.Attempts);
        }
    }

    [Fact]
    public async Task Approved_registration_save_failure_recovers_same_transaction_without_recreating_player()
    {
        var identity = new MemoryWebPlayerIdentityStore();
        var pending = PendingStore();
        pending.FailSave = true;
        var id = pending.Value!.Id;
        var handler = new DelegatingHandlerStub((request, _, _) =>
        {
            Assert.Contains(id, request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, request.RequestUri.AbsolutePath.EndsWith("/activate")
                ? "{\"status\":\"ACTIVATED\"}" : AuthorizationJson("APPROVED")));
        });
        var service = CreateService(handler, identity, pending);
        Assert.Equal(PlayerIdentityRequestStatus.LocalStorageError, (await service.CompleteAppAuthorizationAsync()).Status);
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
        pending.FailSave = false;
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, (await service.CompleteAppAuthorizationAsync()).Status);
        Assert.Equal(PublicPlayerId, identity.Load().PublicPlayerId);
        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task Cancellation_interrupts_poll_without_adopting_or_losing_pending_proof()
    {
        var identity = new MemoryWebPlayerIdentityStore();
        var pending = PendingStore();
        var handler = new DelegatingHandlerStub((_, _, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"status\":\"PENDING\"}")));
        var service = new WebPlayerIdentityService(new HttpClient(handler) { BaseAddress = new("https://identity.example.test/") },
            identity, authorizationStore: pending, authorizationDelay: (_, token) => Task.FromCanceled(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => service.CompleteAppAuthorizationAsync(cancellation.Token));
        Assert.NotNull(pending.Load());
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
    }

    [Fact]
    public async Task Poll_honors_rate_limit_and_five_second_interval_until_timeout()
    {
        var now = DateTimeOffset.UtcNow;
        var pending = PendingStore(now);
        pending.Value = pending.Value! with { ExpiresAt = now.AddSeconds(22) };
        var delays = new List<TimeSpan>();
        var handler = new DelegatingHandlerStub((_, attempt, _) =>
        {
            if (attempt == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(12));
                return Task.FromResult(response);
            }
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"status\":\"PENDING\"}"));
        });
        var identity = new MemoryWebPlayerIdentityStore();
        var service = new WebPlayerIdentityService(new HttpClient(handler) { BaseAddress = new("https://identity.example.test/") },
            identity, authorizationStore: pending, authorizationNow: () => now,
            authorizationDelay: (duration, _) => { delays.Add(duration); now += duration; return Task.CompletedTask; });
        var result = await service.CompleteAppAuthorizationAsync();
        Assert.Equal(PlayerIdentityRequestStatus.InvalidState, result.Status);
        Assert.Equal(new[] { TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5) }, delays);
        Assert.Null(pending.Load());
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
    }

    [Fact]
    public async Task Cancel_and_expiry_require_a_new_transaction_and_do_not_create_player()
    {
        var identity = new MemoryWebPlayerIdentityStore();
        var pending = PendingStore();
        var id = pending.Value!.Id;
        var handler = new DelegatingHandlerStub(async (request, _, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/cancel"))
                return JsonResponse(HttpStatusCode.OK, "{\"status\":\"CANCELLED\"}");
            var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.NotEqual(id, json.RootElement.GetProperty("id").GetString());
            return StartedResponse(json.RootElement.GetProperty("id").GetString()!);
        });
        var service = CreateService(handler, identity, pending);
        await service.CancelAppAuthorizationAsync();
        Assert.Null(pending.Load());
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, (await service.BeginAppAuthorizationAsync()).Status);
        pending.Value = pending.Value! with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
        id = pending.Value.Id;
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, (await service.BeginAppAuthorizationAsync()).Status);
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
    }

    [Fact]
    public void Invalid_local_forgetting_failure_preserves_old_identity_constraint()
    {
        var identity = MemoryWebPlayerIdentityStore.Registered();
        identity.SetAuthenticationInvalid(true);
        var pending = new MemoryAuthorizationStore { FailClear = true };
        var service = CreateService(new DelegatingHandlerStub((_, _, _) => throw new Exception()), identity, pending);
        var result = service.ForgetInvalidIdentity();
        Assert.Equal(PlayerIdentityRequestStatus.LocalStorageError, result.Status);
        Assert.Equal(PlayerIdentityState.AuthInvalid, result.Identity.State);
        Assert.Equal(PublicPlayerId, result.Identity.PublicPlayerId);
    }

    [Fact]
    public async Task Network_and_server_errors_preserve_registered_identity()
    {
        var store = MemoryWebPlayerIdentityStore.Registered();
        var handler = new DelegatingHandlerStub((_, attempt, _) =>
        {
            if (attempt == 1)
            {
                throw new HttpRequestException("offline");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        var service = CreateService(handler, store);

        var network = await service.GetCurrentPlayerAsync();
        var server = await service.GetCurrentPlayerAsync();

        Assert.Equal(PlayerIdentityRequestStatus.NetworkError, network.Status);
        Assert.Equal(PlayerIdentityRequestStatus.ServerError, server.Status);
        Assert.Equal(PlayerIdentityState.Registered, store.Load().State);
        Assert.Equal(Credential, store.Load().AppCredential);
    }

    [Fact]
    public async Task Explicit_profile_refresh_caches_name_and_rejects_another_player()
    {
        var store = MemoryWebPlayerIdentityStore.Registered();
        var handler = new DelegatingHandlerStub((request, _, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, PlayerJson("Updated player")));
        });
        var result = await CreateService(handler, store).GetCurrentPlayerAsync();
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, result.Status);
        Assert.Equal("Updated player", store.Load().DisplayName);
        Assert.Equal(PublicPlayerId, store.Load().PublicPlayerId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Profile_google_state_is_unknown_until_response_and_not_assumed_on_restart(bool linked)
    {
        var store = MemoryWebPlayerIdentityStore.Registered();
        var handler = new DelegatingHandlerStub((_, _, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            PlayerJson("Player").Replace("\"display_name\":", $"\"google_linked\":{linked.ToString().ToLowerInvariant()},\"display_name\":"))));
        var service = CreateService(handler, store);
        Assert.Null(service.GoogleLinked);
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, (await service.GetCurrentPlayerAsync()).Status);
        Assert.Equal(linked, service.GoogleLinked);
        Assert.Null(CreateService(handler, store).GoogleLinked);
    }

    [Fact]
    public void Dpapi_authorization_uses_distinct_purpose_and_no_plaintext_secrets()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "authorization.bin");
        var pending = PendingStore().Value!;
        var store = new FileAppAuthorizationStore(path);
        store.Save(pending);
        Assert.Equal(pending, new FileAppAuthorizationStore(path).Load());
        var bytes = File.ReadAllBytes(path);
        Assert.DoesNotContain(pending.RequestSecret, Encoding.UTF8.GetString(bytes));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
            new DpapiCurrentUserSecretProtector().Unprotect(bytes, UserSecretPurpose.AppCredential));
        Assert.DoesNotContain(pending.RequestSecret, pending.ToString());
    }

    [Fact]
    public void Unreadable_credential_preserves_readable_public_ID_for_relogin()
    {
        using var directory = new TemporaryDirectory();
        var metadata = Path.Combine(directory.Path, "identity.json");
        var credential = Path.Combine(directory.Path, "credential.bin");
        new FileWebPlayerIdentityStore(metadata, credential).SaveRegistered(PublicPlayerId, Credential);
        File.WriteAllBytes(credential, [1, 2, 3]);
        var snapshot = new FileWebPlayerIdentityStore(metadata, credential).Load();
        Assert.Equal(PlayerIdentityState.AuthInvalid, snapshot.State);
        Assert.Equal(PublicPlayerId, snapshot.PublicPlayerId);
        Assert.Null(snapshot.AppCredential);
    }

    [Fact]
    public void Credential_deletion_failure_does_not_forget_readable_public_id()
    {
        using var directory = new TemporaryDirectory();
        var metadataPath = Path.Combine(directory.Path, "identity.json");
        var credentialPath = Path.Combine(directory.Path, "credential.bin");
        var store = new FileWebPlayerIdentityStore(metadataPath, credentialPath);
        store.SaveRegistered(PublicPlayerId, Credential);
        store.SetAuthenticationInvalid(true);
        using (File.Open(credentialPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = CreateService(new DelegatingHandlerStub((_, _, _) => throw new Exception()), store).ForgetInvalidIdentity();
            Assert.Equal(PlayerIdentityRequestStatus.LocalStorageError, result.Status);
            Assert.Equal(PlayerIdentityState.AuthInvalid, result.Identity.State);
            Assert.Equal(PublicPlayerId, result.Identity.PublicPlayerId);
        }
    }

    private static MemoryAuthorizationStore PendingStore(DateTimeOffset? started = null)
    {
        var now = started ?? DateTimeOffset.UtcNow;
        return new MemoryAuthorizationStore
        {
            Value = new("abcdefghijklmnopqrstuv", new string('A', 43), "connect", null,
            now, now.AddMinutes(10))
        };
    }

    private static MemoryAuthorizationStore ActivationPendingStore()
    {
        var pending = PendingStore(DateTimeOffset.UtcNow.AddHours(-1));
        pending.Value = pending.Value! with
        {
            State = AppAuthorizationState.CredentialActivationPending,
            Credential = Credential,
            Player = new(PublicPlayerId, "Recovered player", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };
        return pending;
    }

    private static HttpResponseMessage StartedResponse(string id) => JsonResponse(HttpStatusCode.OK,
        JsonSerializer.Serialize(new
        {
            authorization_id = id,
            url = "https://identity.example.test/my/app-connect?request=" + id,
            comparison_code = "1234ABCD",
            expires_at = DateTimeOffset.UtcNow.AddMinutes(9)
        }));

    private static string AuthorizationJson(string status, string playerId = PublicPlayerId, string displayName = "Player") =>
        JsonSerializer.Serialize(new
        {
            status,
            credential = Credential,
            public_player_id = playerId,
            display_name = displayName,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });

    private sealed class MemoryAuthorizationStore : IAppAuthorizationStore
    {
        public PendingAppAuthorization? Value { get; set; }
        public bool FailSave { get; set; }
        public bool CorruptReadback { get; set; }
        public bool FailCredentialReadback { get; set; }
        public bool FailClear { get; set; }
        public PendingAppAuthorization? Load() => CorruptReadback || FailCredentialReadback && Value?.Credential is not null
            ? Value is null ? null : Value with { RequestSecret = "bad" } : Value;
        public void Save(PendingAppAuthorization authorization)
        {
            if (FailSave) throw new IOException("save failed");
            Value = authorization;
        }
        public void Clear()
        {
            if (FailClear) throw new IOException("clear failed");
            Value = null;
        }
    }

    private static WebPlayerIdentityService CreateService(
        HttpMessageHandler handler,
        IWebPlayerIdentityStore store,
        IAppAuthorizationStore? authorization = null) =>
        new(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://identity.example.test/"),
            },
            store, authorizationStore: authorization);

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static string RegistrationJson() => JsonSerializer.Serialize(new
    {
        public_player_id = PublicPlayerId,
        display_name = "Player",
        created_at = "2026-09-20T00:00:00Z",
        updated_at = "2026-09-20T00:00:00Z",
        credential = Credential,
    });

    private static string PlayerJson(string displayName = "Player") =>
        JsonSerializer.Serialize(new
        {
            public_player_id = PublicPlayerId,
            display_name = displayName,
            created_at = "2026-09-20T00:00:00Z",
            updated_at = "2026-09-20T00:00:00Z",
        });

    private sealed class DelegatingHandlerStub(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> send) :
        HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            return send(request, Attempts, cancellationToken);
        }
    }

    private sealed class MemoryWebPlayerIdentityStore : IWebPlayerIdentityStore
    {
        private string? publicPlayerId;
        private string? appCredential;
        private string? pendingRegistrationRequestId;
        private string? displayName;
        private bool authenticationInvalid;
        public bool FailSave { get; set; }
        public bool FailAuthenticationInvalid { get; set; }
        public bool CorruptAuthenticationReadback { get; set; }

        public static MemoryWebPlayerIdentityStore Registered()
        {
            var store = new MemoryWebPlayerIdentityStore();
            store.SaveRegistered(PublicPlayerId, Credential, "Player");
            return store;
        }

        public WebPlayerIdentitySnapshot Load()
        {
            var state = publicPlayerId is null || appCredential is null
                ? PlayerIdentityState.Unregistered
                : authenticationInvalid && !CorruptAuthenticationReadback
                    ? PlayerIdentityState.AuthInvalid
                    : PlayerIdentityState.Registered;
            return new(
                state,
                publicPlayerId,
                appCredential,
                pendingRegistrationRequestId,
                displayName);
        }

        public void SavePendingRegistration(string registrationRequestId) =>
            pendingRegistrationRequestId = registrationRequestId;

        public void SaveRegistered(
            string savedPublicPlayerId,
            string savedAppCredential,
            string? savedDisplayName = null)
        {
            if (FailSave) throw new IOException("save failed");
            publicPlayerId = savedPublicPlayerId;
            appCredential = savedAppCredential;
            displayName = savedDisplayName;
            pendingRegistrationRequestId = null;
            authenticationInvalid = false;
        }

        public void SetDisplayName(string savedDisplayName) => displayName = savedDisplayName;

        public void SetAuthenticationInvalid(bool invalid)
        {
            if (FailAuthenticationInvalid) throw new IOException("authentication state save failed");
            authenticationInvalid = invalid;
        }

        public void Clear()
        {
            publicPlayerId = null;
            appCredential = null;
            pendingRegistrationRequestId = null;
            displayName = null;
            authenticationInvalid = false;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ddrgp-web-identity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
