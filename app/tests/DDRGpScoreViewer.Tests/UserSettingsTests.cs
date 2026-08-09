using System.Security.Cryptography;
using System.Text;
using DDRGpScoreViewer;
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
                StartupPage: UserSettings.HistoryStartupPage,
                Language: UserSettings.KoreanLanguage);
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

    [Theory]
    [InlineData(UserSettings.JapaneseLanguage)]
    [InlineData(UserSettings.EnglishLanguage)]
    [InlineData(UserSettings.KoreanLanguage)]
    public void New_environment_language_follows_the_operating_system_locale(
        string language)
    {
        var locale = language switch
        {
            UserSettings.JapaneseLanguage => "ja-JP",
            UserSettings.KoreanLanguage => "ko-KR",
            _ => "en-US",
        };

        Assert.Equal(language, UserSettings.ForNewEnvironment(locale).Language);
    }

    [Fact]
    public void Missing_language_and_legacy_startup_page_are_read_as_japanese_and_codes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ddrgp-user-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "user-settings.json");
        try
        {
            File.WriteAllText(
                path,
                "{\n" +
                "  \"StartMonitoringOnLaunch\": true,\n" +
                "  \"NotifyUnresolvedResults\": true,\n" +
                "  \"DefaultPlayStyle\": \"SINGLE\",\n" +
                "  \"StartupPage\": \"自己ベスト\"\n" +
                "}\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var loaded = new LocalUserSettingsStore(path).Load();

            Assert.NotNull(loaded);
            Assert.Equal(UserSettings.JapaneseLanguage, loaded.Language);
            Assert.Equal(UserSettings.BestStartupPage, loaded.StartupPage);
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
    public void Unsupported_saved_language_falls_back_to_english()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ddrgp-user-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "user-settings.json");
        try
        {
            File.WriteAllText(
                path,
                "{\n" +
                "  \"StartMonitoringOnLaunch\": true,\n" +
                "  \"NotifyUnresolvedResults\": true,\n" +
                "  \"DefaultPlayStyle\": \"SINGLE\",\n" +
                "  \"StartupPage\": \"home\",\n" +
                "  \"Language\": \"fr\"\n" +
                "}\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var loaded = new LocalUserSettingsStore(path).Load();

            Assert.NotNull(loaded);
            Assert.Equal(UserSettings.EnglishLanguage, loaded.Language);
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
    public void Localization_translates_supported_languages_and_falls_back_to_the_base_text()
    {
        Assert.Equal("ホーム", Localization.GetForLanguage("ホーム", UserSettings.JapaneseLanguage));
        Assert.Equal("Home", Localization.GetForLanguage("ホーム", UserSettings.EnglishLanguage));
        Assert.Equal("홈", Localization.GetForLanguage("ホーム", UserSettings.KoreanLanguage));
        Assert.Equal(
            "未登録の表示文言",
            Localization.GetForLanguage("未登録の表示文言", UserSettings.EnglishLanguage));
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
        viewModel.Language = UserSettings.EnglishLanguage;

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
        Assert.Equal(UserSettings.EnglishLanguage, restartedViewModel.Language);
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
