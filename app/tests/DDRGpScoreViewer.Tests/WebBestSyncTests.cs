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
            MainWindow.ProductionWebApiOrigin,
            MainWindow.ResolveWebApiOrigin(null).AbsoluteUri);
        Assert.Equal(
            MainWindow.ProductionWebApiOrigin,
            MainWindow.ResolveWebApiOrigin("http://insecure.example.test").AbsoluteUri);
        Assert.Equal(
            "https://staging.example.test/",
            MainWindow.ResolveWebApiOrigin("https://staging.example.test").AbsoluteUri);
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
    public void StatePersistsPendingUpsertDeleteAndSyncedTransitions()
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
            Assert.Equal(1, pendingDelete.PendingCount);
            Assert.Null(pendingDelete.Entries[0].DesiredProjectionHash);
            Assert.NotNull(pendingDelete.Entries[0].SyncedProjectionHash);
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
    public async Task OffToOnUsesFullSnapshotAndPublicDeleteTurnsSyncOff()
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

        Assert.Equal(1, api.BeginSnapshotCalls);
        Assert.Single(api.UploadedItems);
        Assert.False(coordinator.State.FullSnapshotRequired);
        Assert.Equal(WebBestSyncStatus.Idle, coordinator.State.Status);

        await coordinator.SetEnabledAsync(false, CancellationToken.None);
        Assert.False(coordinator.State.Enabled);
        await coordinator.SetEnabledAsync(true, CancellationToken.None);
        Assert.Equal(2, api.BeginSnapshotCalls);

        await coordinator.DeletePublicBestsAsync(CancellationToken.None);
        Assert.Equal(1, api.DeleteCalls);
        Assert.False(coordinator.State.Enabled);
        Assert.Equal(WebBestSyncStatus.PublicBestsDeleted, coordinator.State.Status);
        Assert.Empty(coordinator.State.Entries);
    }

    [Fact]
    public async Task SettingsToggleDoesNotPublishUntilSettingsAreApplied()
    {
        using var fixture = new DatabaseFixture();
        var stateStore = new SqliteWebBestSyncStateStore(
            Path.Combine(fixture.DirectoryPath, "settings-sync.sqlite"));
        var api = new FakeWebBestSyncApiClient();
        var identityStore = new MemoryWebPlayerIdentityStore();
        identityStore.SaveRegistered("public-player", "credential-secret");
        using var identityHttpClient = new HttpClient(new DelegateHttpMessageHandler(
            _ => Task.FromResult(JsonResponse("Player"))))
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var viewModel = new MainViewModel(new ScoreViewerRepository());
        viewModel.ConfigureWebBestSync(
            new WebBestSyncCoordinator(
                stateStore,
                new WebBestProjectionRepository(),
                api,
                fixture.ScorePath,
                fixture.MasterPath,
                delay: (_, _) => Task.CompletedTask),
            new WebPlayerIdentityService(identityHttpClient, identityStore));

        viewModel.WebBestSyncEnabled = true;

        Assert.False(stateStore.Load().Enabled);
        Assert.Equal(0, api.BeginSnapshotCalls);

        await viewModel.ApplyWebSettingsAsync();

        Assert.True(stateStore.Load().Enabled);
        Assert.Equal(1, api.BeginSnapshotCalls);
    }

    [Fact]
    public async Task SettingsUsePublicPlayerNameForRegistrationAndMetadataUpdate()
    {
        using var fixture = new DatabaseFixture();
        string? registrationBody = null;
        var registrationStore = new MemoryWebPlayerIdentityStore();
        using var registrationHttpClient = new HttpClient(new DelegateHttpMessageHandler(
            async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                registrationBody = await request.Content!.ReadAsStringAsync();
                return JsonResponse("2ten", includeCredential: true);
            }))
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var registrationApi = new FakeWebBestSyncApiClient();
        var registrationViewModel = new MainViewModel(new ScoreViewerRepository());
        registrationViewModel.ConfigureWebBestSync(
            new WebBestSyncCoordinator(
                new SqliteWebBestSyncStateStore(
                    Path.Combine(fixture.DirectoryPath, "registration-sync.sqlite")),
                new WebBestProjectionRepository(),
                registrationApi,
                fixture.ScorePath,
                fixture.MasterPath,
                delay: (_, _) => Task.CompletedTask),
            new WebPlayerIdentityService(registrationHttpClient, registrationStore));
        registrationViewModel.WebPlayerDisplayName = "2ten";
        registrationViewModel.WebBestSyncEnabled = true;

        await registrationViewModel.ApplyWebSettingsAsync();

        Assert.Contains("\"display_name\":\"2ten\"", registrationBody, StringComparison.Ordinal);
        Assert.Equal(PlayerIdentityState.Registered, registrationStore.Load().State);
        Assert.Equal(1, registrationApi.BeginSnapshotCalls);

        string? updateBody = null;
        var updateStore = new MemoryWebPlayerIdentityStore();
        updateStore.SaveRegistered("public-player", "credential-secret");
        using var updateHttpClient = new HttpClient(new DelegateHttpMessageHandler(
            async request =>
            {
                if (request.Method == HttpMethod.Get)
                {
                    return JsonResponse("Existing player");
                }
                Assert.Equal(HttpMethod.Patch, request.Method);
                updateBody = await request.Content!.ReadAsStringAsync();
                return JsonResponse("Updated player");
            }))
        {
            BaseAddress = new Uri("https://best.example.test/"),
        };
        var updateApi = new FakeWebBestSyncApiClient();
        var updateViewModel = new MainViewModel(new ScoreViewerRepository());
        updateViewModel.ConfigureWebBestSync(
            new WebBestSyncCoordinator(
                new SqliteWebBestSyncStateStore(
                    Path.Combine(fixture.DirectoryPath, "update-sync.sqlite")),
                new WebBestProjectionRepository(),
                updateApi,
                fixture.ScorePath,
                fixture.MasterPath),
            new WebPlayerIdentityService(updateHttpClient, updateStore));
        await updateViewModel.RefreshWebPlayerProfileAsync();
        Assert.Equal("Existing player", updateViewModel.WebPlayerDisplayName);
        updateViewModel.WebPlayerDisplayName = "Updated player";

        await updateViewModel.ApplyWebSettingsAsync();

        Assert.Contains(
            "\"display_name\":\"Updated player\"",
            updateBody,
            StringComparison.Ordinal);
        Assert.Equal(0, updateApi.BeginSnapshotCalls);
        Assert.Equal("Updated player", updateViewModel.WebPlayerDisplayName);
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
            BeginResult = new(
                WebBestApiStatus.AuthenticationInvalid,
                null,
                null,
                "AUTH_INVALID"),
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
        api.BeginResults.Enqueue(new(
            WebBestApiStatus.RetryableError, null, null, "SERVER_RETRYABLE"));
        api.BeginResults.Enqueue(new(WebBestApiStatus.Success, "bs_fixture", 0));
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

        Assert.Equal(2, api.BeginSnapshotCalls);
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
        var pending = new WebBestSyncEntry("chart_1", "desired", null, null);
        var unknown = pending with { DeferredError = "UNKNOWN_CHART" };

        var cases = new[]
        {
            (WebBestSyncStatus.Disabled, false, Array.Empty<WebBestSyncEntry>(), "同期OFF", false),
            (WebBestSyncStatus.Idle, true, Array.Empty<WebBestSyncEntry>(), "同期済み", false),
            (WebBestSyncStatus.Dirty, true, new[] { pending }, "同期待ち", true),
            (WebBestSyncStatus.Syncing, true, new[] { pending }, "変更分を同期中", false),
            (WebBestSyncStatus.Reconciling, true, new[] { pending }, "自己ベストを同期中", false),
            (WebBestSyncStatus.ErrorRetryable, true, new[] { pending }, "同期できませんでした", true),
            (WebBestSyncStatus.AuthInvalid, true, new[] { pending }, "認証情報の確認が必要です", false),
            (WebBestSyncStatus.Dirty, true, new[] { unknown }, "一部の譜面をあとで同期します", false),
            (WebBestSyncStatus.PublicBestsDeleted, false, Array.Empty<WebBestSyncEntry>(), "公開Bestなし", false),
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
            Assert.Equal(title, viewModel.WebBestSyncStatusTitle);
            Assert.Equal(canSync, viewModel.CanSyncWebBestsNow);
        }
        viewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
            true, false, WebBestSyncStatus.AuthInvalid, null, 0, null,
            "AUTH_INVALID", [pending]));
        Assert.Equal(System.Windows.Visibility.Visible,
            viewModel.WebBestAuthActionVisibility);
        identityStore.Clear();
        viewModel.ApplyWebBestSyncState(new WebBestSyncSnapshot(
            false, false, WebBestSyncStatus.Disabled, null, 0, null, null, []));
        Assert.False(viewModel.CanDeletePublicBests);
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
    public async Task BackupRestoreRunsAnEmptyFullReconciliationForRestoredManifestPlays()
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
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
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
        Assert.Equal(1, api.BeginSnapshotCalls);
        Assert.Empty(api.UploadedItems);
        Assert.False(coordinator.State.FullSnapshotRequired);
        Assert.Empty(coordinator.State.Entries);
        Assert.Equal(WebBestSyncStatus.Idle, coordinator.State.Status);
    }

    private sealed class FakeWebBestSyncApiClient : IWebBestSyncApiClient
    {
        public int BeginSnapshotCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public List<PlayerChartBestProjectionV1> UploadedItems { get; } = [];
        public Queue<WebBestSnapshotBeginResult> BeginResults { get; } = new();
        public WebBestSnapshotBeginResult BeginResult { get; set; } =
            new(WebBestApiStatus.Success, "bs_fixture", 0);
        public Func<IReadOnlyList<WebBestDeltaOperation>, WebBestDeltaResult>? DeltaHandler { get; set; }

        public Task<WebBestDeltaResult> SendDeltaAsync(
            string masterVersion,
            IReadOnlyList<WebBestDeltaOperation> operations,
            CancellationToken cancellationToken) =>
            Task.FromResult(DeltaHandler?.Invoke(operations) ?? new WebBestDeltaResult(
                WebBestApiStatus.Success,
                operations.Select((_, index) => new WebBestDeltaItemResult(
                    index, true, true, null)).ToArray()));

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

        public Task<WebBestApiResult> CommitSnapshotAsync(
            string snapshotId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WebBestApiResult(WebBestApiStatus.Success));

        public Task<WebBestApiResult> AbortSnapshotAsync(
            string snapshotId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WebBestApiResult(WebBestApiStatus.Success));

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
        private bool authenticationInvalid;

        public WebPlayerIdentitySnapshot Load() => new(
            publicPlayerId is null || credential is null
                ? PlayerIdentityState.Unregistered
                : authenticationInvalid
                    ? PlayerIdentityState.AuthInvalid
                    : PlayerIdentityState.Registered,
            publicPlayerId,
            credential,
            null);

        public void SavePendingRegistration(string registrationRequestId)
        {
        }

        public void SaveRegistered(string savedPublicPlayerId, string appCredential)
        {
            publicPlayerId = savedPublicPlayerId;
            credential = appCredential;
            authenticationInvalid = false;
        }

        public void SetAuthenticationInvalid(bool invalid) => authenticationInvalid = invalid;

        public void Clear()
        {
            publicPlayerId = null;
            credential = null;
            authenticationInvalid = false;
        }
    }
}
