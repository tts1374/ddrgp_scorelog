using System.Security.Cryptography;
using System.Text;
using DDRGpScoreViewer.Data;
using DDRGpScoreViewer.Models;
using DDRGpScoreViewer.ViewModels;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class UserSettingsTests
{
    [Fact]
    public void Local_store_round_trips_supported_values_without_a_bom()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ddrgp-user-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "user-settings.json");
        try
        {
            var expected = new UserSettings(
                StartMonitoringOnLaunch: false,
                NotifyUnresolvedResults: false,
                DefaultPlayStyle: UserSettings.DoublePlayStyle,
                StartupPage: UserSettings.HistoryStartupPage);
            var store = new LocalUserSettingsStore(path);

            store.Save(expected);

            Assert.Equal(expected, store.Load());
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);
            Assert.NotEqual(0xEF, bytes[0]);
            Assert.EndsWith("\n", File.ReadAllText(path, new UTF8Encoding(false)));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Missing_or_unreadable_settings_fall_back_to_all_defaults()
    {
        var missingStore = new MemoryUserSettingsStore(null);
        var missingViewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: missingStore);

        missingViewModel.RestoreUserSettings();

        AssertDefaults(missingViewModel);

        var unreadableViewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: new ThrowingUserSettingsStore());

        unreadableViewModel.RestoreUserSettings();

        AssertDefaults(unreadableViewModel);
    }

    [Fact]
    public void Save_and_restart_restore_settings_without_modifying_score_data()
    {
        using var fixture = new DatabaseFixture();
        var scoreHashBefore = SHA256.HashData(File.ReadAllBytes(fixture.ScorePath));
        var store = new MemoryUserSettingsStore(null);
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            defaultDatabasePaths: ConfiguredPaths(fixture),
            userSettingsStore: store);

        viewModel.StartMonitoringOnLaunch = false;
        viewModel.NotifyUnresolvedResults = false;
        viewModel.DefaultPlayStyle = UserSettings.DoublePlayStyle;
        viewModel.StartupPage = UserSettings.BestStartupPage;

        Assert.True(viewModel.SaveUserSettings());
        Assert.Equal(scoreHashBefore, SHA256.HashData(File.ReadAllBytes(fixture.ScorePath)));

        var restartedViewModel = new MainViewModel(
            new ScoreViewerRepository(),
            defaultDatabasePaths: ConfiguredPaths(fixture),
            userSettingsStore: store);
        restartedViewModel.RestoreUserSettings();

        Assert.False(restartedViewModel.StartMonitoringOnLaunch);
        Assert.False(restartedViewModel.NotifyUnresolvedResults);
        Assert.Equal(UserSettings.DoublePlayStyle, restartedViewModel.DefaultPlayStyle);
        Assert.Equal(UserSettings.BestStartupPage, restartedViewModel.StartupPage);
        Assert.False(restartedViewModel.IsAutomaticMonitoringEnabled);
    }

    [Fact]
    public void Editing_any_saved_setting_marks_the_draft_as_unsaved()
    {
        var store = new MemoryUserSettingsStore(UserSettings.Defaults);
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: store);
        viewModel.RestoreUserSettings();

        Assert.True(viewModel.SaveUserSettings());
        Assert.Equal("設定を保存しました。", viewModel.SettingsStatusMessage);

        viewModel.StartMonitoringOnLaunch = false;
        Assert.Equal("変更内容は保存時に反映されます", viewModel.SettingsStatusMessage);
        Assert.True(viewModel.SaveUserSettings());

        viewModel.NotifyUnresolvedResults = false;
        Assert.Equal("変更内容は保存時に反映されます", viewModel.SettingsStatusMessage);
        Assert.True(viewModel.SaveUserSettings());

        viewModel.DefaultPlayStyle = UserSettings.DoublePlayStyle;
        Assert.Equal("変更内容は保存時に反映されます", viewModel.SettingsStatusMessage);
        Assert.True(viewModel.SaveUserSettings());

        viewModel.StartupPage = UserSettings.HistoryStartupPage;
        Assert.Equal("変更内容は保存時に反映されます", viewModel.SettingsStatusMessage);
    }

    [Fact]
    public void Reset_returns_draft_values_to_defaults_without_saving_them()
    {
        var saved = new UserSettings(
            StartMonitoringOnLaunch: false,
            NotifyUnresolvedResults: false,
            DefaultPlayStyle: UserSettings.DoublePlayStyle,
            StartupPage: UserSettings.HistoryStartupPage);
        var store = new MemoryUserSettingsStore(saved);
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: store);
        viewModel.RestoreUserSettings();

        viewModel.ResetUserSettings();

        AssertDefaults(viewModel);
        Assert.Equal(saved, store.StoredSettings);
    }

    private static void AssertDefaults(MainViewModel viewModel)
    {
        Assert.True(viewModel.StartMonitoringOnLaunch);
        Assert.True(viewModel.NotifyUnresolvedResults);
        Assert.Equal(UserSettings.SinglePlayStyle, viewModel.DefaultPlayStyle);
        Assert.Equal(UserSettings.HomeStartupPage, viewModel.StartupPage);
    }

    private static ViewerDatabasePaths ConfiguredPaths(DatabaseFixture fixture) =>
        new(
            ViewerDatabaseEnvironment.Development,
            fixture.DirectoryPath,
            fixture.MasterPath,
            fixture.CatalogPath,
            fixture.ScorePath,
            null,
            fixture.DirectoryPath,
            Path.Combine(fixture.DirectoryPath, "logs"),
            Path.Combine(fixture.DirectoryPath, "viewer-paths.json"));
}

internal sealed class MemoryUserSettingsStore(UserSettings? settings) : IUserSettingsStore
{
    public UserSettings? StoredSettings { get; private set; } = settings;

    public UserSettings? Load() => StoredSettings;

    public void Save(UserSettings settings) => StoredSettings = settings;
}

internal sealed class ThrowingUserSettingsStore : IUserSettingsStore
{
    public UserSettings? Load() => throw new IOException("settings read failed");

    public void Save(UserSettings settings) => throw new IOException("settings write failed");
}
