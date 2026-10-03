using System.Net;
using System.Text;
using System.Text.Json;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using DDRGpScoreViewer.WebBestSync;
using DDRGpScoreViewer.WebIdentity;
using Xunit;

namespace DDRGpScoreViewer.Tests;

[Collection("Localized WPF views")]
public sealed class WebBestSyncTests
{
    [Fact]
    public void ProductionApiOriginIsTheDefaultAndHttpsOverrideIsSupported()
    {
        Assert.Equal(
            "https://ddrgp-scorelog.tts1374.workers.dev/",
            MainWindow.ProductionWebApiOrigin);
        Assert.Equal(
            MainWindow.ProductionWebApiOrigin,
            MainWindow.ResolveWebApiOrigin(ViewerDatabaseEnvironment.Production, null).AbsoluteUri);
        Assert.Equal(
            MainWindow.ProductionWebApiOrigin,
            MainWindow.ResolveWebApiOrigin(
                ViewerDatabaseEnvironment.Production, "http://insecure.example.test").AbsoluteUri);
        Assert.Equal(
            "https://staging.example.test/",
            MainWindow.ResolveWebApiOrigin(
                ViewerDatabaseEnvironment.Production, "https://staging.example.test").AbsoluteUri);
        Assert.Equal(
            "https://ddrgp-scorelog.tts1374.workers.dev/player/p_example",
            MainWindow.ResolvePublicPlayerPageUri(
                MainWindow.ResolveWebApiOrigin(
                    ViewerDatabaseEnvironment.Production, null), "p_example").AbsoluteUri);
        Assert.Equal(
            "https://staging.example.test/player/p_example",
            MainWindow.ResolvePublicPlayerPageUri(
                MainWindow.ResolveWebApiOrigin(
                    ViewerDatabaseEnvironment.Production, "https://staging.example.test"),
                "p_example").AbsoluteUri);
    }

    [Theory]
    [InlineData(null, "https://ddrgp-scorelog-dev.tts1374.workers.dev/")]
    [InlineData("http://127.0.0.1:5173", "http://127.0.0.1:5173/")]
    [InlineData("http://localhost:8787", "http://localhost:8787/")]
    [InlineData("http://remote.example.test", "https://ddrgp-scorelog-dev.tts1374.workers.dev/")]
    [InlineData("https://staging.example.test", "https://staging.example.test/")]
    [InlineData("https://ddrgp-scorelog.tts1374.workers.dev", "https://ddrgp-scorelog-dev.tts1374.workers.dev/")]
    [InlineData("https://ddrgp-scorelog-identity-api.tts1374.workers.dev", "https://ddrgp-scorelog-dev.tts1374.workers.dev/")]
    [InlineData("https://DDRGP-SCORELOG-IDENTITY-API.tts1374.workers.dev:443/", "https://ddrgp-scorelog-dev.tts1374.workers.dev/")]
    public void DevelopmentSyncAndPublicPageUseTheDevelopmentOrigin(
        string? overrideOrigin,
        string expectedOrigin)
    {
        var origin = MainWindow.ResolveWebApiOrigin(
            ViewerDatabaseEnvironment.Development, overrideOrigin);

        Assert.Equal(expectedOrigin, origin.AbsoluteUri);
        Assert.Equal(
            expectedOrigin + "player/p_example",
            MainWindow.ResolvePublicPlayerPageUri(origin, "p_example").AbsoluteUri);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5173/", false)]
    [InlineData("http://remote.example.test/", true)]
    public void IdentityAndBestClientsRejectHttpOutsideDevelopmentLoopback(
        string origin,
        bool allowLoopbackHttp)
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri(origin) };
        var store = new MemoryWebPlayerIdentityStore();

        Assert.Throws<ArgumentException>(() =>
            new WebPlayerIdentityService(httpClient, store, allowLoopbackHttp));
        Assert.Throws<ArgumentException>(() =>
            new WebBestSyncApiClient(httpClient, store, allowLoopbackHttp));
    }

    [Fact]
    public void DevelopmentIdentityAndBestClientsAcceptLocalHttp()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = MainWindow.ResolveWebApiOrigin(
                ViewerDatabaseEnvironment.Development, "http://127.0.0.1:5173/"),
        };
        var store = new MemoryWebPlayerIdentityStore();

        _ = new WebPlayerIdentityService(httpClient, store, allowLoopbackHttp: true);
        _ = new WebBestSyncApiClient(httpClient, store, allowLoopbackHttp: true);
    }

    [Fact]
    public void ProjectionPrecedenceCoversEveryClearAndFlareValue()
    {
        Assert.Equal(
            "MFC",
            WebBestProjectionContract.BestClear(
                ["FAILED", "CLEAR", "FULL COMBO", "FC", "GFC", "PFC", "MFC"]));
        Assert.Equal("FC", WebBestProjectionContract.BestClear(["FULL COMBO"]));
        Assert.Equal(
            "EX",
            WebBestProjectionContract.BestFlare(
                ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"]));
        Assert.Null(WebBestProjectionContract.BestFlare([null, "unknown"]));
    }

    [Fact]
    public void ProjectionUsesOnlyCaptureAndAggregatesFieldsIndependently()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("score-best", "2026-09-20T01:00:00+00:00", 990_000, 100);
        fixture.AddPlay("ex-best", "2026-09-20T02:00:00+00:00", 900_000, 2_000);
        fixture.AddPlay("clear-best", "2026-09-20T03:00:00+00:00", 800_000, 500);
        fixture.AddPlay("manual", "2026-09-20T04:00:00+00:00", 1_000_000, 9_999);
        fixture.ExecuteScoreSql(
            """
            UPDATE source_captures SET source_kind = 'capture'
            WHERE capture_id IN (
              'capture-score-best', 'capture-ex-best', 'capture-clear-best'
            );
            UPDATE plays SET clear_type = 'FAILED', flare_rank = 'EX'
            WHERE play_id = 'score-best';
            UPDATE plays SET clear_type = 'FULL COMBO', flare_rank = 'IX'
            WHERE play_id = 'ex-best';
            UPDATE plays SET clear_type = 'PFC', flare_rank = 'V'
            WHERE play_id = 'clear-best';
            UPDATE plays SET clear_type = 'MFC', flare_rank = 'EX'
            WHERE play_id = 'manual';
            """);

        var projection = Assert.Single(
            new WebBestProjectionRepository().ReadAll(fixture.ScorePath));

        Assert.Equal("chart-1", projection.ChartId);
        Assert.Equal(990_000, projection.BestScore);
        Assert.Equal(2_000, projection.BestExScore);
        Assert.Equal("PFC", projection.BestClearType);
        Assert.Equal("IX", projection.BestFlareRank);
    }

    [Fact]
    public void ProjectionExcludesEveryNonCaptureKindAndUsesNullWithoutValidFlare()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddMasterSongAndChart("song-2", "Song 2", "Artist", "chart-2");
        fixture.AddPlay("capture", "2026-09-20T01:00:00+00:00", 0, 0, "song-2", "chart-2");
        fixture.AddPlay("manifest", "2026-09-20T02:00:00+00:00", 999_990, 2_000, "song-2", "chart-2");
        fixture.AddPlay("timestamped", "2026-09-20T03:00:00+00:00", 999_980, 1_900, "song-2", "chart-2");
        fixture.ExecuteScoreSql(
            """
            UPDATE source_captures SET source_kind = 'capture'
            WHERE capture_id = 'capture-capture';
            UPDATE source_captures SET source_kind = 'manifest'
            WHERE capture_id = 'capture-manifest';
            UPDATE source_captures SET source_kind = 'timestamped'
            WHERE capture_id = 'capture-timestamped';
            UPDATE plays SET clear_type = 'FAILED', flare_rank = 'EX'
            WHERE play_id = 'capture';
            """);

        var projection = Assert.Single(
            new WebBestProjectionRepository().ReadAll(fixture.ScorePath));

        Assert.Equal(0, projection.BestScore);
        Assert.Equal(0, projection.BestExScore);
        Assert.Equal("FAILED", projection.BestClearType);
        Assert.Null(projection.BestFlareRank);
    }

    [Fact]
    public void ProjectionHashUsesCanonicalFieldOrderAndExplicitNull()
    {
        var projection = new PlayerChartBestProjectionV1(
            "chart_1",
            987_650,
            1_234,
            "FC",
            null);

        Assert.Equal(
            "{\"chart_id\":\"chart_1\",\"best_score\":987650,\"best_ex_score\":1234," +
            "\"best_clear_type\":\"FC\",\"best_flare_rank\":null}",
            WebBestProjectionContract.CanonicalJson(projection));
        Assert.Equal(
            WebBestProjectionContract.Hash(projection),
            WebBestProjectionContract.Hash(projection with { }));
        Assert.Equal(64, WebBestProjectionContract.Hash(projection).Length);
    }

    [Fact]
    public void StatePersistsReceivedHashesAndForgetsLocalRemovalWithoutSendingDelete()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"web-sync-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "state.sqlite");
            var projection = new PlayerChartBestProjectionV1(
                "chart_1", 900_000, 1_000, "CLEAR", null);
            var first = new SqliteWebBestSyncStateStore(path);
            var dirty = first.Reconcile([projection]);
            Assert.Equal(1, dirty.PendingCount);
            Assert.Null(dirty.Entries[0].SyncedProjectionHash);

            var unchanged = first.Reconcile([projection]);
            Assert.Equal(dirty.Entries[0].DesiredProjectionHash,
                unchanged.Entries[0].DesiredProjectionHash);
            Assert.Equal(1, unchanged.PendingCount);

            var restarted = new SqliteWebBestSyncStateStore(path);
            var persisted = restarted.Load();
            Assert.Equal(dirty.Entries[0].DesiredProjectionHash,
                persisted.Entries[0].DesiredProjectionHash);
            var synced = restarted.MarkSynced(
                "chart_1",
                persisted.Entries[0].DesiredProjectionHash);
            Assert.Equal(0, synced.PendingCount);

            var pendingDelete = restarted.Reconcile([]);
            Assert.Equal(0, pendingDelete.PendingCount);
            Assert.Empty(pendingDelete.Entries);
            var deleted = restarted.MarkSynced("chart_1", null);
            Assert.Empty(deleted.Entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnknownChartDeferralPersistsUntilTheProjectionChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"web-sync-deferred-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SqliteWebBestSyncStateStore(
                Path.Combine(directory, "state.sqlite"));
            var projection = new PlayerChartBestProjectionV1(
                "chart_1", 900_000, 1_000, "CLEAR", null);
            store.Reconcile([projection]);
            store.MarkDeferred("chart_1", "UNKNOWN_CHART");

            var unchanged = store.Reconcile([projection]);
            Assert.Equal("UNKNOWN_CHART", unchanged.Entries[0].DeferredError);

            var changed = store.Reconcile([projection with { BestScore = 910_000 }]);
            Assert.Null(changed.Entries[0].DeferredError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OffToOnReplaysCaptureMergeAndPublicDeleteTurnsSyncOff()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-09-20T01:00:00+00:00", 900_000, 1_000);
        fixture.ExecuteScoreSql(
            "UPDATE source_captures SET source_kind = 'capture' WHERE capture_id = 'capture-capture';");
        var statePath = Path.Combine(fixture.DirectoryPath, "sync.sqlite");
        var stateStore = new SqliteWebBestSyncStateStore(statePath);
        var api = new FakeWebBestSyncApiClient();
        var coordinator = new WebBestSyncCoordinator(
            stateStore,
            new WebBestProjectionRepository(),
            api,
            fixture.ScorePath,
            fixture.MasterPath,
            delay: (_, _) => Task.CompletedTask,
            jitter: () => 0.5);

        await coordinator.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(0, api.BeginSnapshotCalls);
        Assert.Single(api.MergedItems);
        Assert.False(coordinator.State.FullSnapshotRequired);
        Assert.Equal(WebBestSyncStatus.Idle, coordinator.State.Status);

        await coordinator.SetEnabledAsync(false, CancellationToken.None);
        Assert.False(coordinator.State.Enabled);
        await coordinator.SetEnabledAsync(true, CancellationToken.None);
        Assert.Equal(0, api.BeginSnapshotCalls);
        Assert.Equal(2, api.MergeCalls);

        await coordinator.DeletePublicBestsAsync(CancellationToken.None);
        Assert.Equal(1, api.DeleteCalls);
        Assert.False(coordinator.State.Enabled);
        Assert.Equal(WebBestSyncStatus.PublicBestsDeleted, coordinator.State.Status);
        Assert.Empty(coordinator.State.Entries);
    }

    [Fact]
    public async Task SettingsApplyDoesNotRegisterOrPatchNameAndToggleImmediatelyResumesMerge()
    {
        using var fixture = new DatabaseFixture();
        var stateStore = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "settings-sync.sqlite"));
        var api = new FakeWebBestSyncApiClient();
        var identityStore = new MemoryWebPlayerIdentityStore();
        var identityRequests = 0;
        using var client = new HttpClient(new DelegateHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/me", request.RequestUri?.AbsolutePath);
            identityRequests++;
            return Task.FromResult(JsonResponse("Web player"));
        }))
        { BaseAddress = new("https://best.example.test/") };
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(stateStore, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath), new WebPlayerIdentityService(client, identityStore));
        viewModel.WebPlayerDisplayName = "2ten";
        await viewModel.ApplyWebSettingsAsync();
        Assert.Equal(PlayerIdentityState.Unregistered, identityStore.Load().State);
        Assert.False(stateStore.Load().Enabled);
        Assert.False(viewModel.CanToggleWebSync);
        identityStore.SaveRegistered("public-player", "credential-secret", "Existing player");
        viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(stateStore, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath), new WebPlayerIdentityService(client, identityStore));
        viewModel.WebPlayerDisplayName = "Updated player";
        await viewModel.ApplyWebSettingsAsync();
        Assert.Equal("Existing player", identityStore.Load().DisplayName);
        Assert.Equal(0, identityRequests);
        await viewModel.ToggleWebBestSyncAsync();
        Assert.True(stateStore.Load().Enabled);
        Assert.Equal(1, identityRequests);
        Assert.Equal("Web player", viewModel.WebPlayerDisplayName);
        await viewModel.ToggleWebBestSyncAsync();
        Assert.False(stateStore.Load().Enabled);
        Assert.False(viewModel.CanSyncWebBestsNow);
        await viewModel.SyncWebBestsNowAsync();
        Assert.Equal(0, api.MergeCalls);
        Assert.Equal(0, api.BeginSnapshotCalls);
        Assert.Equal(1, identityRequests);
    }

    [Fact]
    public async Task InvalidIdentityRecoveryRequiresExplicitForgetAndTurnsSyncOff()
    {
        using var fixture = new DatabaseFixture();
        var stateStore = new SqliteWebBestSyncStateStore(
            Path.Combine(fixture.DirectoryPath, "invalid-identity-sync.sqlite"));
        stateStore.SetEnabled(true);
        var identityStore = new MemoryWebPlayerIdentityStore();
        identityStore.SaveRegistered("public-player", "credential-secret");
        identityStore.SetAuthenticationInvalid(true);
        using var identityHttpClient = new HttpClient(new DelegateHttpMessageHandler(
            _ => throw new InvalidOperationException("Recovery must not call the API.")))
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var api = new FakeWebBestSyncApiClient();
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(
            new WebBestSyncCoordinator(
                stateStore,
                new WebBestProjectionRepository(),
                api,
                fixture.ScorePath,
                fixture.MasterPath),
            new WebPlayerIdentityService(identityHttpClient, identityStore));

        await viewModel.ForgetInvalidWebIdentityAsync();

        Assert.Equal(PlayerIdentityState.Unregistered, identityStore.Load().State);
        Assert.False(stateStore.Load().Enabled);
        Assert.False(viewModel.WebBestSyncEnabled);
        Assert.False(viewModel.CanOpenPublicPlayerPage);
        Assert.Equal("Player", viewModel.WebPlayerDisplayName);
        Assert.Equal(0, api.BeginSnapshotCalls);
    }

    [Fact]
    public async Task DeltaPartialSuccessKeepsUnknownChartPending()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture-1", "2026-09-20T01:00:00+00:00", 900_000, 1_000);
        fixture.ExecuteScoreSql(
            "UPDATE source_captures SET source_kind = 'capture' WHERE capture_id = 'capture-capture-1';");
        var store = new SqliteWebBestSyncStateStore(
            Path.Combine(fixture.DirectoryPath, "sync.sqlite"));
        var initial = new WebBestProjectionRepository().ReadAll(fixture.ScorePath);
        store.SetEnabled(true);
        store.CompleteSnapshot(initial, DateTimeOffset.UtcNow);
        fixture.AddMasterSongAndChart("song-2", "Song 2", "Artist", "chart-2");
        fixture.AddPlay(
            "capture-2", "2026-09-20T02:00:00+00:00", 910_000, 1_100,
            "song-2", "chart-2");
        fixture.ExecuteScoreSql(
            "UPDATE source_captures SET source_kind = 'capture' WHERE capture_id = 'capture-capture-2';");
        var api = new FakeWebBestSyncApiClient
        {
            DeltaHandler = operations => new WebBestDeltaResult(
                WebBestApiStatus.Success,
                operations.Select((_, index) => new WebBestDeltaItemResult(
                    index,
                    Accepted: false,
                    Changed: false,
                    ErrorCode: "UNKNOWN_CHART")).ToArray()),
        };
        var coordinator = new WebBestSyncCoordinator(
            store,
            new WebBestProjectionRepository(),
            api,
            fixture.ScorePath,
            fixture.MasterPath,
            delay: (_, _) => Task.CompletedTask);

        await coordinator.SynchronizeAsync(CancellationToken.None);

        Assert.Equal(WebBestSyncStatus.Dirty, coordinator.State.Status);
        Assert.Equal(1, coordinator.State.UnknownChartCount);
        Assert.Equal(1, coordinator.State.PendingCount);
    }

    [Fact]
    public async Task AuthInvalidStopsWithoutRegistrationAndRetryPolicyIsBounded()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-09-20T01:00:00+00:00", 900_000, 1_000);
        fixture.ExecuteScoreSql(
            "UPDATE source_captures SET source_kind = 'capture' WHERE capture_id = 'capture-capture';");
        var store = new SqliteWebBestSyncStateStore(
            Path.Combine(fixture.DirectoryPath, "sync.sqlite"));
        store.SetEnabled(true);
        var api = new FakeWebBestSyncApiClient
        {
            DeltaHandler = _ => new(WebBestApiStatus.AuthenticationInvalid, [], "AUTH_INVALID"),
        };
        var coordinator = new WebBestSyncCoordinator(
            store,
            new WebBestProjectionRepository(),
            api,
            fixture.ScorePath,
            fixture.MasterPath,
            delay: (_, _) => Task.CompletedTask);

        await coordinator.SynchronizeAsync(CancellationToken.None);

        Assert.Equal(WebBestSyncStatus.AuthInvalid, coordinator.State.Status);
        Assert.Equal("AUTH_INVALID", coordinator.State.LastErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(4), WebBestSyncCoordinator.RetryDelay(0, 0));
        Assert.Equal(TimeSpan.FromMinutes(6), WebBestSyncCoordinator.RetryDelay(99, 1));
    }

    [Fact]
    public async Task ApiClientUsesCredentialOnlyAndPersistsAuthenticationInvalid()
    {
        var store = new MemoryWebPlayerIdentityStore();
        store.SaveRegistered("public-player-must-not-be-sent", "credential-secret");
        string? requestBody = null;
        var handler = new DelegateHttpMessageHandler(async request =>
        {
            requestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync();
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("credential-secret", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var client = new WebBestSyncApiClient(httpClient, store);

        var result = await client.SendDeltaAsync(
            "fixture-v1",
            [new WebBestDeltaOperation(
                "upsert",
                "chart_1",
                "hash",
                new PlayerChartBestProjectionV1(
                    "chart_1", 900_000, 1_000, "FC", null))],
            CancellationToken.None);

        Assert.Equal(WebBestApiStatus.AuthenticationInvalid, result.Status);
        Assert.DoesNotContain("public-player-must-not-be-sent", requestBody);
        var identity = store.Load();
        Assert.Equal(PlayerIdentityState.AuthInvalid, identity.State);
        Assert.Equal("credential-secret", identity.AppCredential);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    public async Task ApiClientMapsRateLimitAndServerFailureToRetryable(int statusCode)
    {
        var store = new MemoryWebPlayerIdentityStore();
        store.SaveRegistered("public-player", "credential-secret");
        using var httpClient = new HttpClient(new DelegateHttpMessageHandler(
            _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode))))
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var client = new WebBestSyncApiClient(httpClient, store);

        var result = await client.DeletePublicBestsAsync(CancellationToken.None);

        Assert.Equal(WebBestApiStatus.RetryableError, result.Status);
        Assert.Equal(PlayerIdentityState.Registered, store.Load().State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApiClientMapsNetworkAndTimeoutToRetryable(bool timeout)
    {
        var store = new MemoryWebPlayerIdentityStore();
        store.SaveRegistered("public-player", "credential-secret");
        using var httpClient = new HttpClient(new DelegateHttpMessageHandler(
            _ => timeout
                ? throw new TaskCanceledException("timeout")
                : throw new HttpRequestException("network")))
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var client = new WebBestSyncApiClient(httpClient, store);

        var result = await client.DeletePublicBestsAsync(CancellationToken.None);

        Assert.Equal(WebBestApiStatus.RetryableError, result.Status);
        Assert.Equal(PlayerIdentityState.Registered, store.Load().State);
    }

    [Fact]
    public async Task CoordinatorRetriesAndCompletesAfterATransientFailure()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-09-20T01:00:00+00:00", 900_000, 1_000);
        fixture.ExecuteScoreSql(
            "UPDATE source_captures SET source_kind = 'capture' WHERE capture_id = 'capture-capture';");
        var store = new SqliteWebBestSyncStateStore(
            Path.Combine(fixture.DirectoryPath, "sync.sqlite"));
        store.SetEnabled(true);
        var api = new FakeWebBestSyncApiClient();
        var attempt = 0;
        api.DeltaHandler = operations => ++attempt == 1
            ? new(WebBestApiStatus.RetryableError, [], "SERVER_RETRYABLE")
            : new(WebBestApiStatus.Success, operations.Select((_, index) => new WebBestDeltaItemResult(index, true, true, null)).ToArray());
        var delays = new List<TimeSpan>();
        var coordinator = new WebBestSyncCoordinator(
            store,
            new WebBestProjectionRepository(),
            api,
            fixture.ScorePath,
            fixture.MasterPath,
            delay: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            jitter: () => 0.5);

        await coordinator.SynchronizeAsync(CancellationToken.None);

        Assert.Equal(0, api.BeginSnapshotCalls);
        Assert.Equal(2, api.MergeCalls);
        Assert.Equal([TimeSpan.FromSeconds(5)], delays);
        Assert.Equal(WebBestSyncStatus.Idle, coordinator.State.Status);
        Assert.Equal(0, coordinator.State.RetryAttempt);
    }

    [Fact]
    public void SettingsStatePresentationDistinguishesTheRequiredSyncStates()
    {
        Localization.Configure(UserSettings.JapaneseLanguage);
        using var fixture = new DatabaseFixture();
        var identityStore = new MemoryWebPlayerIdentityStore();
        identityStore.SaveRegistered("public-player", "credential-secret");
        using var identityHttpClient = new HttpClient(new DelegateHttpMessageHandler(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(
            new WebBestSyncCoordinator(
                new SqliteWebBestSyncStateStore(
                    Path.Combine(fixture.DirectoryPath, "ui-sync.sqlite")),
                new WebBestProjectionRepository(),
                new FakeWebBestSyncApiClient(),
                fixture.ScorePath,
                fixture.MasterPath),
            new WebPlayerIdentityService(identityHttpClient, identityStore));
        Assert.True(viewModel.CanOpenPublicPlayerPage);
        var pending = new WebBestSyncEntry("chart_1", "desired", null, null);
        var unknown = pending with { DeferredError = "UNKNOWN_CHART" };

        var cases = new[]
        {
            (WebBestSyncStatus.Disabled, false, Array.Empty<WebBestSyncEntry>(), "連携停止中", false),
            (WebBestSyncStatus.Idle, true, Array.Empty<WebBestSyncEntry>(), "同期済み", true),
            (WebBestSyncStatus.Dirty, true, new[] { pending }, "同期待ち", true),
            (WebBestSyncStatus.Syncing, true, new[] { pending }, "変更分を同期中", false),
            (WebBestSyncStatus.Reconciling, true, new[] { pending }, "自己ベストを同期中", false),
            (WebBestSyncStatus.ErrorRetryable, true, new[] { pending }, "同期できませんでした", true),
            (WebBestSyncStatus.AuthInvalid, true, new[] { pending }, "認証情報の確認が必要です", false),
            (WebBestSyncStatus.Dirty, true, new[] { unknown }, "一部の譜面をあとで同期します", true),
            (WebBestSyncStatus.PublicBestsDeleted, false, Array.Empty<WebBestSyncEntry>(), "公開データなし", false),
        };

        foreach (var (status, enabled, entries, title, canSync) in cases)
        {
            viewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
                enabled,
                false,
                status,
                null,
                0,
                null,
                null,
                entries));
            Assert.Equal(Localization.Get(title), viewModel.WebBestSyncStatusTitle);
            Assert.Equal(canSync, viewModel.CanSyncWebBestsNow);
            if (status is WebBestSyncStatus.Idle or WebBestSyncStatus.Syncing or
                WebBestSyncStatus.Reconciling or WebBestSyncStatus.PublicBestsDeleted)
            {
                Assert.Empty(viewModel.WebBestSyncStatusMessage);
            }
        }
        identityStore.SetAuthenticationInvalid(true);
        viewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
            true, false, WebBestSyncStatus.AuthInvalid, null, 0, null,
            "AUTH_INVALID", [pending]));
        Assert.Equal(System.Windows.Visibility.Visible,
            viewModel.WebBestAuthActionVisibility);
        Assert.True(viewModel.CanOpenPublicPlayerPage);
        Assert.Equal("public-player", viewModel.GetPublicPlayerId());
        Assert.False(viewModel.CanSyncWebBestsNow);
        Assert.False(viewModel.CanDeletePublicBests);
        viewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
            false, false, WebBestSyncStatus.Disabled, null, 0, null, null, []));
        Assert.True(viewModel.CanOpenPublicPlayerPage);
        Assert.False(viewModel.CanDeletePublicBests);
        identityStore.Clear();
        viewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
            false, false, WebBestSyncStatus.Disabled, null, 0, null, null, []));
        Assert.False(viewModel.CanDeletePublicBests);
        Assert.False(viewModel.CanOpenPublicPlayerPage);
    }

    [Fact]
    public async Task Stop_waits_for_inflight_merge_before_Web_deletion_and_drains_queued_sync()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-10-02T01:00:00+00:00", 900000, 1000);
        fixture.ExecuteScoreSql("UPDATE source_captures SET source_kind = 'capture';");
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "stop-inflight.sqlite"));
        store.SetEnabled(true);
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publicRecordExists = false;
        var commits = 0;
        var api = new FakeWebBestSyncApiClient
        {
            AsyncDeltaHandler = async operations =>
            {
                sendStarted.SetResult();
                await finishSend.Task;
                publicRecordExists = true;
                commits++;
                return new(WebBestApiStatus.Success,
                    operations.Select((_, index) => new WebBestDeltaItemResult(index, true, true, null)).ToArray());
            },
        };
        var coordinator = new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath);
        var prematureStoppedNotification = false;
        coordinator.StateChanged += state => prematureStoppedNotification |= !state.Enabled && commits == 0;
        var sync = coordinator.SynchronizeAsync(CancellationToken.None);
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedSync = coordinator.SynchronizeAsync(CancellationToken.None);
        var stop = coordinator.SetEnabledAsync(false, CancellationToken.None);
        var repeatedStop = coordinator.SetEnabledAsync(false, CancellationToken.None);
        try
        {
            Assert.False(coordinator.State.Enabled);
            Assert.False(stop.IsCompleted);
            Assert.False(repeatedStop.IsCompleted);
            Assert.Equal(0, commits);
            Assert.False(prematureStoppedNotification);
        }
        finally
        {
            finishSend.SetResult();
            await Task.WhenAll(sync, queuedSync, stop, repeatedStop).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.True(publicRecordExists);
        Assert.Equal(1, commits);
        publicRecordExists = false; // The user deletes Web records after the stop has completed.
        await coordinator.SynchronizeAsync(CancellationToken.None);
        Assert.False(publicRecordExists);
        Assert.Equal(1, commits);
        Assert.Equal(1, api.MergeCalls);
        Assert.Equal(WebBestSyncStatus.Disabled, coordinator.State.Status);
    }

    [Fact]
    public async Task Stop_during_automatic_merge_retry_wait_prevents_further_requests_and_preserves_disabled_state()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-10-02T01:00:00+00:00", 900000, 1000);
        fixture.ExecuteScoreSql("UPDATE source_captures SET source_kind = 'capture';");
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "stop-retry.sqlite"));
        store.SetEnabled(true);
        var api = new FakeWebBestSyncApiClient
        {
            DeltaHandler = _ => new(WebBestApiStatus.RetryableError, [], "NETWORK_ERROR"),
        };
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRetryWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath, delay: async (_, _) =>
            {
                retryStarted.SetResult();
                await finishRetryWait.Task;
            });
        var sync = coordinator.SynchronizeAsync(CancellationToken.None);
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = coordinator.SetEnabledAsync(false, CancellationToken.None);
        Assert.False(coordinator.State.Enabled);
        try
        {
            Assert.False(stop.IsCompleted);
        }
        finally
        {
            finishRetryWait.SetResult();
            await Task.WhenAll(sync, stop).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(1, api.MergeCalls);
        Assert.False(coordinator.State.Enabled);
        Assert.Equal(WebBestSyncStatus.Disabled, coordinator.State.Status);
        Assert.True(coordinator.State.PendingCount > 0);
        await coordinator.SynchronizeAsync(CancellationToken.None);
        Assert.Equal(1, api.MergeCalls);
        await coordinator.DeletePublicBestsAsync(CancellationToken.None);
        Assert.Equal(1, api.DeleteCalls);
        Assert.False(coordinator.State.Enabled);
    }

    [Fact]
    public void PostedSyncStateIsAppliedWhenDispatcherUsesAnotherContextInstance()
    {
        Localization.Configure(UserSettings.JapaneseLanguage);
        var originalContext = SynchronizationContext.Current;
        var dispatcherContext = new QueuedSynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(dispatcherContext);
            var viewModel = new MainViewModel(new ScoreViewerRepository());
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            viewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
                true, false, WebBestSyncStatus.Idle, null, 0, null, null, []));
            Assert.Equal(1, dispatcherContext.PendingCount);
            Assert.Equal(Localization.Get("連携停止中"), viewModel.WebBestSyncStatusTitle);

            dispatcherContext.RunNext();
            Assert.Equal(0, dispatcherContext.PendingCount);
            Assert.Equal(Localization.Get("同期済み"), viewModel.WebBestSyncStatusTitle);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void BackupRestoreCanPersistReconciliationIntentBeforeReplacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"web-sync-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SqliteWebBestSyncStateStore(Path.Combine(directory, "sync.sqlite"));
            store.SetEnabled(true);
            var state = store.RequestFullSnapshot();

            Assert.True(state.Enabled);
            Assert.True(state.FullSnapshotRequired);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BackupRestoreManifestHasNoNormalSnapshotOrDelete()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-09-20T01:00:00+00:00", 900_000, 1_000);
        fixture.ExecuteScoreSql(
            "UPDATE source_captures SET source_kind = 'capture' WHERE capture_id = 'capture-capture';");
        var backupPath = Path.Combine(fixture.DirectoryPath, "backup.json");
        Assert.True(new PersonalScoreDataBackupService()
            .CreateBackup(fixture.ScorePath, backupPath).Succeeded);
        var stateStore = new SqliteWebBestSyncStateStore(
            Path.Combine(fixture.DirectoryPath, "restore-sync.sqlite"));
        var beforeRestore = new WebBestProjectionRepository().ReadAll(fixture.ScorePath);
        stateStore.SetEnabled(true);
        stateStore.CompleteSnapshot(beforeRestore, DateTimeOffset.UtcNow);
        var api = new FakeWebBestSyncApiClient();
        var coordinator = new WebBestSyncCoordinator(
            stateStore,
            new WebBestProjectionRepository(),
            api,
            fixture.ScorePath,
            fixture.MasterPath,
            delay: (_, _) => Task.CompletedTask);
        var identityStore = new MemoryWebPlayerIdentityStore();
        identityStore.SaveRegistered("public-player", "credential-secret");
        using var identityHttpClient = new HttpClient(new DelegateHttpMessageHandler(
            _ => Task.FromResult(JsonResponse("Player"))))
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var paths = new ViewerDatabasePaths(
            ViewerDatabaseEnvironment.Development,
            fixture.DirectoryPath,
            fixture.MasterPath,
            fixture.CatalogPath,
            fixture.ScorePath,
            Path.Combine(fixture.DirectoryPath, "evaluation.db"),
            Path.Combine(fixture.DirectoryPath, "data"),
            Path.Combine(fixture.DirectoryPath, "logs"),
            Path.Combine(fixture.DirectoryPath, "viewer-settings.json"));
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            defaultDatabasePaths: paths);
        viewModel.ConfigureWebBestSync(
            coordinator,
            new WebPlayerIdentityService(identityHttpClient, identityStore));
        viewModel.Load(fixture.ScorePath, fixture.MasterPath, fixture.CatalogPath, persist: false);

        var result = viewModel.RestorePersonalScoreBackup(backupPath);
        await viewModel.WaitForOperationsAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(0, api.BeginSnapshotCalls);
        Assert.Empty(api.UploadedItems);
        Assert.False(coordinator.State.FullSnapshotRequired);
        Assert.Empty(coordinator.State.Entries);
        Assert.Equal(WebBestSyncStatus.Idle, coordinator.State.Status);
    }

    [Fact]
    public async Task Explicit_empty_replacement_requires_review_then_authorize_and_can_be_cancelled()
    {
        using var fixture = new DatabaseFixture();
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "explicit.sqlite"));
        var api = new FakeWebBestSyncApiClient();
        var coordinator = new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath);
        Assert.False(await coordinator.ConfirmReplacementAsync(CancellationToken.None));
        Assert.Equal(0, api.CommitCalls);
        var review = await coordinator.PrepareReplacementAsync(CancellationToken.None);
        Assert.NotNull(review);
        Assert.Equal(0, review.EligibleCount);
        Assert.Equal(2, review.PublicCount);
        Assert.Contains("removed-chart", review.Removed);
        Assert.Equal(0, api.AuthorizeCalls);
        Assert.Equal(0, api.CommitCalls);
        await coordinator.CancelReplacementAsync(CancellationToken.None);
        Assert.Equal(1, api.AbortCalls);
        Assert.False(await coordinator.ConfirmReplacementAsync(CancellationToken.None));
        await coordinator.PrepareReplacementAsync(CancellationToken.None);
        Assert.True(await coordinator.ConfirmReplacementAsync(CancellationToken.None));
        Assert.Equal(1, api.AuthorizeCalls);
        Assert.Equal(1, api.CommitCalls);
        Assert.False(coordinator.State.Enabled);
    }

    [Fact]
    public async Task Replacement_response_loss_retries_same_authorized_snapshot_and_retains_fixed_content()
    {
        using var fixture = new DatabaseFixture();
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "replacement-retry.sqlite"));
        var api = new FakeWebBestSyncApiClient { CommitStatus = WebBestApiStatus.RetryableError };
        var coordinator = new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath, delay: (_, _) => Task.CompletedTask);
        var review = await coordinator.PrepareReplacementAsync(CancellationToken.None);
        Assert.False(await coordinator.ConfirmReplacementAsync(CancellationToken.None));
        Assert.Equal(1, api.AuthorizeCalls);
        Assert.Equal(6, api.CommitCalls);
        fixture.AddPlay("new-capture", "2026-10-02T01:00:00+00:00", 910000, 1200);
        fixture.ExecuteScoreSql("UPDATE source_captures SET source_kind = 'capture';");
        Assert.Same(review, await coordinator.PrepareReplacementAsync(CancellationToken.None));
        api.CommitStatus = WebBestApiStatus.Success;
        Assert.True(await coordinator.ConfirmReplacementAsync(CancellationToken.None));
        Assert.Equal(1, api.BeginSnapshotCalls);
        Assert.Equal(1, api.AuthorizeCalls);
        Assert.Equal(7, api.CommitCalls);
        Assert.Equal(1, coordinator.State.PendingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_generation_invalidates_prepared_replacement_even_when_OFF(bool enabled)
    {
        using var fixture = new DatabaseFixture();
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "generation.sqlite"));
        store.SetEnabled(enabled);
        var api = new FakeWebBestSyncApiClient();
        var coordinator = new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath);
        Assert.NotNull(await coordinator.PrepareReplacementAsync(CancellationToken.None));
        coordinator.RequireReconciliation();
        Assert.False(await coordinator.ConfirmReplacementAsync(CancellationToken.None));
        Assert.Equal(0, api.AuthorizeCalls);
        Assert.Equal(0, api.CommitCalls);
        Assert.True(coordinator.State.FullSnapshotRequired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Merge_ack_must_match_this_PC_received_hash_and_contract_409_keeps_identity(bool conflict)
    {
        var identity = new MemoryWebPlayerIdentityStore();
        identity.SaveRegistered("public-player", "credential");
        var projection = new PlayerChartBestProjectionV1("chart_1", 1000, 100, "CLEAR", null);
        using var client = new HttpClient(new DelegateHttpMessageHandler(async request =>
        {
            Assert.Equal("/api/v1/me/bests/merge", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("items", body);
            Assert.DoesNotContain("operations", body);
            return new HttpResponseMessage(conflict ? HttpStatusCode.Conflict : HttpStatusCode.OK)
            {
                Content = new StringContent(conflict ? "{\"error\":{\"code\":\"HISTORICAL_BEST_REQUIRED\"}}" :
                "{\"results\":[{\"index\":0,\"status\":\"accepted\",\"changed\":false,\"received_hash\":\"wrong\"}]}", Encoding.UTF8, "application/json")
            };
        }))
        { BaseAddress = new("https://best.example.test/") };
        var api = new WebBestSyncApiClient(client, identity);
        var result = await api.SendDeltaAsync("v1", [new("upsert", projection.ChartId,
            WebBestProjectionContract.Hash(projection), projection)], CancellationToken.None);
        Assert.Equal(WebBestApiStatus.PermanentError, result.Status);
        Assert.Equal(conflict ? "HISTORICAL_BEST_REQUIRED" : "INVALID_RESPONSE", result.ErrorCode);
        Assert.Equal(PlayerIdentityState.Registered, identity.Load().State);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Startup_saved_activation_waits_for_result_while_pending_link_keeps_existing_sync(
        bool activationPending, int expectedRequests)
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-10-02T01:00:00+00:00", 900000, 1000);
        fixture.ExecuteScoreSql("UPDATE source_captures SET source_kind = 'capture';");
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "startup-sync.sqlite"));
        store.SetEnabled(true);
        var api = new FakeWebBestSyncApiClient();
        var identity = new MemoryWebPlayerIdentityStore();
        identity.SaveRegistered("public-player", "credential-secret");
        var now = DateTimeOffset.UtcNow;
        var pending = new PendingAuthorizationStore(new("request-id", new string('A', 43), "connect", "public-player",
            now, now.AddMinutes(10), State: activationPending ? AppAuthorizationState.CredentialActivationPending : AppAuthorizationState.AppAuthorizationPending,
            Credential: activationPending ? "saved-pending" : null));
        var identityRequests = 0;
        using var http = new HttpClient(new DelegateHttpMessageHandler(request =>
        {
            identityRequests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/me", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse("Player"));
        }))
        { BaseAddress = new("https://best.example.test/") };
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath), new WebPlayerIdentityService(http, identity, authorizationStore: pending));
        await viewModel.ResumeWebBestSyncAsync();
        Assert.Equal(expectedRequests, identityRequests);
        Assert.Equal(expectedRequests, api.MergeCalls);
        Assert.Equal(0, api.BeginSnapshotCalls);
        if (expectedRequests == 0)
        {
            Assert.False(viewModel.CanToggleWebSync);
            Assert.False(viewModel.CanSyncWebBestsNow);
            await viewModel.SyncWebBestsNowAsync();
            Assert.Equal(0, api.MergeCalls);
        }
        else
        {
            await viewModel.ToggleWebBestSyncAsync();
            Assert.False(store.Load().Enabled);
            await viewModel.ResumeWebBestSyncAsync();
            await viewModel.SyncWebBestsNowAsync();
            await viewModel.ApplyWebSettingsAsync();
            Assert.Equal(1, identityRequests);
            Assert.Equal(1, api.MergeCalls);
        }
    }

    [Fact]
    public async Task Activation_cleanup_failure_blocks_uploads_then_same_app_retry_enables_sync()
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-10-02T01:00:00+00:00", 900000, 1000);
        fixture.ExecuteScoreSql("UPDATE source_captures SET source_kind = 'capture';");
        const string playerId = "p_abcdefghijklmnopqrstuv";
        const string credential = "ac_abcdefghijklmnopqrstuv.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "activation-retry.sqlite"));
        store.SetEnabled(true);
        var api = new FakeWebBestSyncApiClient();
        var identity = new MemoryWebPlayerIdentityStore();
        identity.SaveRegistered(playerId, "old-credential");
        identity.SetAuthenticationInvalid(true);
        var now = DateTimeOffset.UtcNow;
        var pending = new PendingAuthorizationStore(new("request-id", new string('A', 43), "connect", playerId,
            now, now.AddMinutes(10)))
        { FailClear = true };
        var activationRequests = 0;
        using var http = new HttpClient(new DelegateHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            var activating = request.RequestUri!.AbsolutePath.EndsWith("/activate", StringComparison.Ordinal);
            if (activating)
            {
                activationRequests++;
            }
            else
            {
                Assert.EndsWith("/result", request.RequestUri.AbsolutePath);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = activating ? "ACTIVATED" : "APPROVED",
                    public_player_id = playerId,
                    credential,
                    display_name = "Player",
                    created_at = now,
                    updated_at = now
                }), Encoding.UTF8, "application/json")
            });
        }))
        { BaseAddress = new("https://best.example.test/") };
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath), new WebPlayerIdentityService(http, identity, authorizationStore: pending));

        await viewModel.CompleteWebAuthorizationAsync();
        Assert.Equal(credential, identity.Load().AppCredential);
        Assert.Equal(AppAuthorizationState.CredentialActivationPending, pending.Load()!.State);
        Assert.True(store.Load().Enabled);
        Assert.False(viewModel.CanSyncWebBestsNow);
        Assert.Equal(0, api.MergeCalls);

        pending.FailClear = false;
        await viewModel.CompleteWebAuthorizationAsync();
        Assert.Null(pending.Load());
        Assert.Equal(PlayerIdentityState.Registered, identity.Load().State);
        Assert.Equal(playerId, identity.Load().PublicPlayerId);
        Assert.Equal(credential, identity.Load().AppCredential);
        Assert.True(store.Load().Enabled);
        Assert.True(viewModel.CanSyncWebBestsNow);
        Assert.Equal(2, activationRequests);
        Assert.Equal(1, api.MergeCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Successful_registration_or_recovery_activation_enables_sync_and_uploads_current_bests(bool recovery, bool alreadyEnabled)
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-10-02T01:00:00+00:00", 900000, 1000);
        fixture.ExecuteScoreSql("UPDATE source_captures SET source_kind = 'capture';");
        const string playerId = "p_abcdefghijklmnopqrstuv";
        const string credential = "ac_abcdefghijklmnopqrstuv.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var identity = new MemoryWebPlayerIdentityStore();
        if (recovery)
        {
            identity.SaveRegistered(playerId, "old-credential");
            identity.SetAuthenticationInvalid(true);
        }
        var now = DateTimeOffset.UtcNow;
        var pending = new PendingAuthorizationStore(new("request-id", new string('A', 43), "connect",
            recovery ? playerId : null, now, now.AddMinutes(10)));
        using var http = new HttpClient(new DelegateHttpMessageHandler(request => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = request.RequestUri!.AbsolutePath.EndsWith("/activate", StringComparison.Ordinal) ? "ACTIVATED" : "APPROVED",
                    public_player_id = playerId,
                    credential,
                    display_name = "Player",
                    created_at = now,
                    updated_at = now
                }), Encoding.UTF8, "application/json")
            })))
        { BaseAddress = new("https://best.example.test/") };
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "new-activation.sqlite"));
        if (alreadyEnabled)
        {
            store.SetEnabled(true);
            var projections = new WebBestProjectionRepository().ReadAll(fixture.ScorePath);
            store.Reconcile(projections);
            foreach (var projection in projections)
            {
                store.MarkSynced(projection.ChartId, WebBestProjectionContract.Hash(projection));
            }
            Assert.Equal(0, store.Load().PendingCount);
            fixture.AddMasterSongAndChart("song-2", "Song 2", "Artist", "chart-2");
            fixture.AddPlay("during-auth-invalid", "2026-10-02T02:00:00+00:00", 950000, 1200, "song-2", "chart-2");
            fixture.ExecuteScoreSql("UPDATE source_captures SET source_kind = 'capture';");
        }
        var api = new FakeWebBestSyncApiClient();
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath), new WebPlayerIdentityService(http, identity, authorizationStore: pending));
        await viewModel.CompleteWebAuthorizationAsync();
        Assert.Null(pending.Load());
        Assert.True(store.Load().Enabled);
        Assert.Equal(1, api.MergeCalls);
        Assert.Equal(alreadyEnabled ? 2 : 1, api.MergedItems.Count);
        Assert.Equal(WebBestSyncStatus.Idle, store.Load().Status);
        Assert.Equal(0, api.BeginSnapshotCalls);
        Assert.Equal(System.Windows.Visibility.Collapsed, viewModel.GoogleConnectVisibility);
        Assert.Equal(System.Windows.Visibility.Visible, viewModel.GoogleUnlinkVisibility);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Google_link_or_unlink_preserves_sync_setting_and_updates_buttons(bool enabled, bool unlink)
    {
        using var fixture = new DatabaseFixture();
        fixture.AddPlay("capture", "2026-10-02T01:00:00+00:00", 900000, 1000);
        fixture.ExecuteScoreSql("UPDATE source_captures SET source_kind = 'capture';");
        const string playerId = "p_abcdefghijklmnopqrstuv";
        var identity = new MemoryWebPlayerIdentityStore();
        identity.SaveRegistered(playerId, "existing-credential");
        var now = DateTimeOffset.UtcNow;
        var pending = new PendingAuthorizationStore(new("request-id", new string('A', 43), unlink ? "unlink" : "connect",
            playerId, now, now.AddMinutes(10)));
        using var http = new HttpClient(new DelegateHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    status = unlink ? "UNLINKED" : "LINKED",
                    public_player_id = playerId,
                    display_name = "Player",
                    created_at = now,
                    updated_at = now
                }), Encoding.UTF8, "application/json")
            })))
        { BaseAddress = new("https://best.example.test/") };
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "google-link.sqlite"));
        store.SetEnabled(enabled);
        var api = new FakeWebBestSyncApiClient();
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath), new WebPlayerIdentityService(http, identity, authorizationStore: pending));
        await viewModel.CompleteWebAuthorizationAsync();
        Assert.Equal(enabled, store.Load().Enabled);
        Assert.Equal("existing-credential", identity.Load().AppCredential);
        Assert.Equal(0, api.MergeCalls);
        Assert.Equal(unlink ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed, viewModel.GoogleConnectVisibility);
        Assert.Equal(unlink ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible, viewModel.GoogleUnlinkVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, viewModel.GoogleSettingsVisibility);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("dpapi")]
    [InlineData("json")]
    public async Task Activation_pending_read_failure_keeps_proof_and_does_not_interrupt_the_app(string failure)
    {
        using var fixture = new DatabaseFixture();
        var identity = new MemoryWebPlayerIdentityStore();
        var now = DateTimeOffset.UtcNow;
        var pending = new PendingAuthorizationStore(new("request-id", new string('A', 43), "connect", null,
            now, now.AddMinutes(10)));
        using var http = new HttpClient(new DelegateHttpMessageHandler(_ => throw new Exception("No network request")))
        { BaseAddress = new("https://best.example.test/") };
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(
            new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "unreadable-pending.sqlite")),
            new WebBestProjectionRepository(), new FakeWebBestSyncApiClient(), fixture.ScorePath, fixture.MasterPath),
            new WebPlayerIdentityService(http, identity, authorizationStore: pending));
        var saved = pending.Load();
        pending.LoadFailure = failure switch
        {
            "io" => new IOException("Unreadable pending file"),
            "access" => new UnauthorizedAccessException("Unreadable pending file"),
            "dpapi" => new System.Security.Cryptography.CryptographicException("Unreadable pending file"),
            _ => new JsonException("Unreadable pending file")
        };
        await viewModel.CompleteWebAuthorizationAsync();
        Assert.False(viewModel.IsWebAuthorizationBusy);
        Assert.Equal(System.Windows.Visibility.Visible, viewModel.WebAuthorizationPendingVisibility);
        Assert.Equal(Localization.Get("結果を確認できませんでした。保存済みの連携情報で結果を確認してください。"), viewModel.WebAuthorizationMessage);
        pending.LoadFailure = null;
        Assert.Equal(saved, pending.Load());
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
    }

    [Fact]
    public async Task Cancellation_network_failure_keeps_pending_proof_and_does_not_interrupt_the_app()
    {
        using var fixture = new DatabaseFixture();
        var identity = new MemoryWebPlayerIdentityStore();
        var now = DateTimeOffset.UtcNow;
        var pending = new PendingAuthorizationStore(new("request-id", new string('A', 43), "connect", null,
            now, now.AddMinutes(10)));
        using var http = new HttpClient(new DelegateHttpMessageHandler(_ => throw new HttpRequestException("Offline")))
        { BaseAddress = new("https://best.example.test/") };
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(
            new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "cancel-sync.sqlite")),
            new WebBestProjectionRepository(), new FakeWebBestSyncApiClient(), fixture.ScorePath, fixture.MasterPath),
            new WebPlayerIdentityService(http, identity, authorizationStore: pending));
        await viewModel.CancelWebAuthorizationAsync();
        Assert.NotNull(pending.Load());
        Assert.Equal(PlayerIdentityState.Unregistered, identity.Load().State);
        Assert.Equal(Localization.Get("キャンセル結果を確認できませんでした。保存済みの情報で再確認できます。"), viewModel.WebAuthorizationMessage);
    }

    [Fact]
    public async Task Replacement_review_holds_restore_exclusion_and_disables_toggle_until_cancel()
    {
        using var fixture = new DatabaseFixture();
        var store = new SqliteWebBestSyncStateStore(Path.Combine(fixture.DirectoryPath, "reserved-sync.sqlite"));
        var api = new FakeWebBestSyncApiClient();
        var identity = new MemoryWebPlayerIdentityStore();
        identity.SaveRegistered("public-player", "credential-secret");
        using var http = new HttpClient(new DelegateHttpMessageHandler(_ => throw new Exception("No identity request")))
        { BaseAddress = new("https://best.example.test/") };
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(new WebBestSyncCoordinator(store, new WebBestProjectionRepository(), api,
            fixture.ScorePath, fixture.MasterPath), new WebPlayerIdentityService(http, identity));
        Assert.NotNull(await viewModel.PrepareWebBestReplacementAsync(CancellationToken.None));
        Assert.True(viewModel.IsPersonalDataOperationBusy);
        Assert.False(viewModel.CanToggleWebSync);
        Assert.False(viewModel.CanDeletePublicBests);
        await viewModel.ToggleWebBestSyncAsync();
        Assert.False(store.Load().Enabled);
        Assert.Equal(0, api.MergeCalls);
        Assert.False(viewModel.RestorePersonalScoreBackup("unrelated-backup.json").Succeeded);
        await viewModel.CancelWebBestReplacementAsync(CancellationToken.None);
        Assert.False(viewModel.IsPersonalDataOperationBusy);
        Assert.True(viewModel.CanToggleWebSync);
    }

    private sealed class PendingAuthorizationStore(PendingAppAuthorization value) : IAppAuthorizationStore
    {
        private PendingAppAuthorization? pending = value;
        public bool FailClear { get; set; }
        public Exception? LoadFailure { get; set; }
        public PendingAppAuthorization? Load()
        {
            if (LoadFailure is not null)
            {
                throw LoadFailure;
            }
            return pending;
        }
        public void Save(PendingAppAuthorization authorization) => pending = authorization;
        public void Clear()
        {
            if (FailClear)
            {
                throw new IOException("Pending authorization cleanup failed.");
            }
            pending = null;
        }
    }

    private sealed class FakeWebBestSyncApiClient : IWebBestSyncApiClient
    {
        public int BeginSnapshotCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int MergeCalls { get; private set; }
        public int AuthorizeCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public WebBestApiStatus CommitStatus { get; set; } = WebBestApiStatus.Success;
        public int AbortCalls { get; private set; }
        public List<PlayerChartBestProjectionV1> MergedItems { get; } = [];
        public List<PlayerChartBestProjectionV1> UploadedItems { get; } = [];
        public Queue<WebBestSnapshotBeginResult> BeginResults { get; } = new();
        public WebBestSnapshotBeginResult BeginResult { get; set; } =
            new(WebBestApiStatus.Success, "bs_fixture", 0);
        public Func<IReadOnlyList<WebBestDeltaOperation>, WebBestDeltaResult>? DeltaHandler { get; set; }
        public Func<IReadOnlyList<WebBestDeltaOperation>, Task<WebBestDeltaResult>>? AsyncDeltaHandler { get; set; }

        public Task<WebBestDeltaResult> SendDeltaAsync(string masterVersion, IReadOnlyList<WebBestDeltaOperation> operations,
            CancellationToken cancellationToken)
        {
            MergeCalls++;
            MergedItems.AddRange(operations.Select(item => item.Projection!));
            if (AsyncDeltaHandler is not null)
            {
                return AsyncDeltaHandler(operations);
            }
            return Task.FromResult(DeltaHandler?.Invoke(operations) ?? new WebBestDeltaResult(WebBestApiStatus.Success,
                operations.Select((_, index) => new WebBestDeltaItemResult(index, true, true, null)).ToArray()));
        }

        public Task<WebBestSnapshotBeginResult> BeginSnapshotAsync(
            string masterVersion,
            int expectedItemCount,
            CancellationToken cancellationToken)
        {
            BeginSnapshotCalls += 1;
            return Task.FromResult(
                BeginResults.Count > 0 ? BeginResults.Dequeue() : BeginResult);
        }

        public Task<WebBestApiResult> UploadSnapshotChunkAsync(
            string snapshotId,
            string chunkId,
            IReadOnlyList<PlayerChartBestProjectionV1> items,
            CancellationToken cancellationToken)
        {
            UploadedItems.AddRange(items);
            return Task.FromResult(new WebBestApiResult(WebBestApiStatus.Success));
        }

        public Task<WebBestApiResult> CommitSnapshotAsync(string snapshotId, CancellationToken cancellationToken)
        {
            CommitCalls++;
            Assert.True(AuthorizeCalls > 0);
            return Task.FromResult(new WebBestApiResult(CommitStatus));
        }
        public Task<WebBestApiResult> AbortSnapshotAsync(string snapshotId, CancellationToken cancellationToken)
        {
            AbortCalls++;
            return Task.FromResult(new WebBestApiResult(WebBestApiStatus.Success));
        }
        public Task<WebBestReplacementReviewResult> ReviewReplacementAsync(string snapshotId, CancellationToken token)
        {
            var canonical = "[" + string.Join(",", UploadedItems.OrderBy(item => item.ChartId, StringComparer.Ordinal)
                .Select(WebBestProjectionContract.CanonicalJson)) + "]";
            var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            return Task.FromResult(new WebBestReplacementReviewResult(WebBestApiStatus.Success,
                new WebBestReplacementReview(2, UploadedItems.Count, ["removed-chart"],
                    [new("chart-1", ["best_score", "best_flare_rank"])], digest, 0)));
        }
        public Task<WebBestApiResult> AuthorizeReplacementAsync(string snapshotId, WebBestReplacementReview review, CancellationToken token)
        {
            AuthorizeCalls++;
            return Task.FromResult(new WebBestApiResult(WebBestApiStatus.Success));
        }

        public Task<WebBestApiResult> DeletePublicBestsAsync(
            CancellationToken cancellationToken)
        {
            DeleteCalls += 1;
            return Task.FromResult(new WebBestApiResult(WebBestApiStatus.Success));
        }
    }

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> pending = new();

        public int PendingCount => pending.Count;

        public override void Post(SendOrPostCallback callback, object? state) =>
            pending.Enqueue((callback, state));

        public void RunNext()
        {
            var (callback, state) = pending.Dequeue();
            callback(state);
        }
    }

    private static HttpResponseMessage JsonResponse(
        string displayName,
        bool includeCredential = false)
    {
        var response = new Dictionary<string, object>
        {
            ["public_player_id"] = "public-player",
            ["display_name"] = displayName,
            ["created_at"] = "2026-09-20T00:00:00Z",
            ["updated_at"] = "2026-09-20T00:00:00Z",
        };
        if (includeCredential)
        {
            response["credential"] = "credential-secret";
        }
        var payload = JsonSerializer.Serialize(response);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class MemoryWebPlayerIdentityStore : IWebPlayerIdentityStore
    {
        private string? publicPlayerId;
        private string? credential;
        private string? displayName;
        private bool authenticationInvalid;

        public WebPlayerIdentitySnapshot Load() => new(
            publicPlayerId is null || credential is null
                ? PlayerIdentityState.Unregistered
                : authenticationInvalid
                    ? PlayerIdentityState.AuthInvalid
                    : PlayerIdentityState.Registered,
            publicPlayerId,
            credential,
            null,
            displayName);

        public void SavePendingRegistration(string registrationRequestId)
        {
        }

        public void SaveRegistered(
            string savedPublicPlayerId,
            string appCredential,
            string? savedDisplayName = null)
        {
            publicPlayerId = savedPublicPlayerId;
            credential = appCredential;
            displayName = savedDisplayName;
            authenticationInvalid = false;
        }

        public void SetDisplayName(string savedDisplayName) => displayName = savedDisplayName;

        public void SetAuthenticationInvalid(bool invalid) => authenticationInvalid = invalid;

        public void Clear()
        {
            publicPlayerId = null;
            credential = null;
            displayName = null;
            authenticationInvalid = false;
        }
    }
}
