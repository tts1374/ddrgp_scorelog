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

        store.SaveRegistered(PublicPlayerId, Credential);

        Assert.DoesNotContain(Credential, File.ReadAllText(metadataPath));
        Assert.DoesNotContain(
            Credential,
            Encoding.UTF8.GetString(File.ReadAllBytes(credentialPath)));
        var restarted = new FileWebPlayerIdentityStore(metadataPath, credentialPath).Load();
        Assert.Equal(PlayerIdentityState.Registered, restarted.State);
        Assert.Equal(PublicPlayerId, restarted.PublicPlayerId);
        Assert.Equal(Credential, restarted.AppCredential);
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
    public async Task Registration_retry_reuses_the_persisted_request_id()
    {
        var requestIds = new List<string>();
        var handler = new DelegatingHandlerStub(async (request, attempt, _) =>
        {
            requestIds.Add(request.Headers.GetValues("Idempotency-Key").Single());
            if (attempt == 1)
            {
                throw new HttpRequestException("response lost");
            }
            return JsonResponse(HttpStatusCode.OK, RegistrationJson());
        });
        var store = new MemoryWebPlayerIdentityStore();
        var service = CreateService(handler, store);

        var first = await service.RegisterAsync("Player");
        var retry = await service.RegisterAsync("Player");

        Assert.Equal(PlayerIdentityRequestStatus.NetworkError, first.Status);
        Assert.Equal(PlayerIdentityState.Unregistered, first.Identity.State);
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, retry.Status);
        Assert.Equal(PlayerIdentityState.Registered, retry.Identity.State);
        Assert.Equal(2, requestIds.Count);
        Assert.Equal(requestIds[0], requestIds[1]);
    }

    [Fact]
    public async Task Restarted_service_authenticates_as_the_same_player()
    {
        var store = new MemoryWebPlayerIdentityStore();
        var registrationHandler = new DelegatingHandlerStub((_, _, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.Created, RegistrationJson())));
        var registration = await CreateService(registrationHandler, store)
            .RegisterAsync("Player");
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, registration.Status);

        var authenticationHandler = new DelegatingHandlerStub((request, _, _) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Credential, request.Headers.Authorization?.Parameter);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, PlayerJson()));
        });
        var restartedService = CreateService(authenticationHandler, store);

        var current = await restartedService.GetCurrentPlayerAsync();

        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, current.Status);
        Assert.Equal(PlayerIdentityState.Registered, current.Identity.State);
        Assert.Equal(PublicPlayerId, current.Player?.PublicPlayerId);
    }

    [Fact]
    public async Task Unauthorized_marks_auth_invalid_without_registering_or_discarding_credential()
    {
        var store = MemoryWebPlayerIdentityStore.Registered();
        var handler = new DelegatingHandlerStub((request, _, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(JsonResponse(HttpStatusCode.Created, RegistrationJson()));
        });
        var service = CreateService(handler, store);

        var prematureForget = service.ForgetInvalidIdentity();

        Assert.Equal(PlayerIdentityRequestStatus.InvalidState, prematureForget.Status);
        Assert.Equal(PlayerIdentityState.Registered, prematureForget.Identity.State);
        Assert.Equal(Credential, prematureForget.Identity.AppCredential);
        Assert.Equal(0, handler.Attempts);

        var result = await service.GetCurrentPlayerAsync();
        var blockedRegistration = await service.RegisterAsync("Replacement");

        Assert.Equal(PlayerIdentityRequestStatus.AuthenticationInvalid, result.Status);
        Assert.Equal(PlayerIdentityState.AuthInvalid, result.Identity.State);
        Assert.Equal(Credential, store.Load().AppCredential);
        Assert.Equal(PlayerIdentityRequestStatus.InvalidState, blockedRegistration.Status);
        Assert.Equal(1, handler.Attempts);

        var forgotten = service.ForgetInvalidIdentity();
        var explicitRegistration = await service.RegisterAsync("Replacement");

        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, forgotten.Status);
        Assert.Equal(PlayerIdentityState.Unregistered, forgotten.Identity.State);
        Assert.Null(forgotten.Identity.AppCredential);
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, explicitRegistration.Status);
        Assert.Equal(PlayerIdentityState.Registered, explicitRegistration.Identity.State);
        Assert.Equal(2, handler.Attempts);
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
    public async Task Metadata_update_preserves_public_player_id()
    {
        var store = MemoryWebPlayerIdentityStore.Registered();
        var handler = new DelegatingHandlerStub(async (request, _, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("Updated player", body);
            return JsonResponse(
                HttpStatusCode.OK,
                PlayerJson(displayName: "Updated player"));
        });

        var result = await CreateService(handler, store)
            .UpdateDisplayNameAsync("Updated player");

        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, result.Status);
        Assert.Equal(PublicPlayerId, result.Player?.PublicPlayerId);
        Assert.Equal(PublicPlayerId, store.Load().PublicPlayerId);
    }

    [Fact]
    public async Task Delete_clears_local_identity_only_after_server_success()
    {
        var retryableStore = MemoryWebPlayerIdentityStore.Registered();
        var retryable = CreateService(
            new DelegatingHandlerStub((_, _, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError))),
            retryableStore);
        var failed = await retryable.DeletePlayerAsync();
        Assert.Equal(PlayerIdentityRequestStatus.ServerError, failed.Status);
        Assert.Equal(PlayerIdentityState.Registered, retryableStore.Load().State);

        var successStore = MemoryWebPlayerIdentityStore.Registered();
        var success = CreateService(
            new DelegatingHandlerStub((_, _, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NoContent))),
            successStore);
        var deleted = await success.DeletePlayerAsync();
        Assert.Equal(PlayerIdentityRequestStatus.Succeeded, deleted.Status);
        Assert.Equal(PlayerIdentityState.Unregistered, successStore.Load().State);
        Assert.Null(successStore.Load().AppCredential);
        Assert.Null(successStore.Load().PublicPlayerId);
    }

    [Fact]
    public void Clear_recovers_as_unregistered_if_credential_deletion_is_interrupted()
    {
        using var directory = new TemporaryDirectory();
        var metadataPath = Path.Combine(directory.Path, "web-player-identity.json");
        var credentialPath = Path.Combine(directory.Path, "web-player-credential.bin");
        var store = new FileWebPlayerIdentityStore(metadataPath, credentialPath);
        store.SaveRegistered(PublicPlayerId, Credential);

        using (File.Open(credentialPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var exception = Record.Exception(store.Clear);

            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.False(File.Exists(metadataPath));
            Assert.True(File.Exists(credentialPath));
            var recovered = new FileWebPlayerIdentityStore(metadataPath, credentialPath).Load();
            Assert.Equal(PlayerIdentityState.Unregistered, recovered.State);
            Assert.Null(recovered.PublicPlayerId);
            Assert.Null(recovered.AppCredential);
        }

        store.Clear();
    }

    private static WebPlayerIdentityService CreateService(
        HttpMessageHandler handler,
        IWebPlayerIdentityStore store) =>
        new(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://identity.example.test/"),
            },
            store);

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
        private bool authenticationInvalid;

        public static MemoryWebPlayerIdentityStore Registered()
        {
            var store = new MemoryWebPlayerIdentityStore();
            store.SaveRegistered(PublicPlayerId, Credential);
            return store;
        }

        public WebPlayerIdentitySnapshot Load()
        {
            var state = publicPlayerId is null || appCredential is null
                ? PlayerIdentityState.Unregistered
                : authenticationInvalid
                    ? PlayerIdentityState.AuthInvalid
                    : PlayerIdentityState.Registered;
            return new(
                state,
                publicPlayerId,
                appCredential,
                pendingRegistrationRequestId);
        }

        public void SavePendingRegistration(string registrationRequestId) =>
            pendingRegistrationRequestId = registrationRequestId;

        public void SaveRegistered(string savedPublicPlayerId, string savedAppCredential)
        {
            publicPlayerId = savedPublicPlayerId;
            appCredential = savedAppCredential;
            pendingRegistrationRequestId = null;
            authenticationInvalid = false;
        }

        public void SetAuthenticationInvalid(bool invalid) =>
            authenticationInvalid = invalid;

        public void Clear()
        {
            publicPlayerId = null;
            appCredential = null;
            pendingRegistrationRequestId = null;
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
