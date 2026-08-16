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
                Language: UserSettings.KoreanLanguage,
                BestBrowseMode: UserSettings.VersionBrowseMode,
                BestLevel: "level_17",
                BestVersion: "DDR WORLD",
                BestGoal: "AA+");
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
    public void Invalid_saved_best_browse_values_fall_back_to_all_defaults()
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
                "  \"BestBrowseMode\": \"unsupported\",\n" +
                "  \"BestLevel\": \"level_20\",\n" +
                "  \"BestVersion\": \"unsupported\",\n" +
                "  \"BestGoal\": \"invalid\"\n" +
                "}\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var viewModel = new MainViewModel(
                new ScoreViewerRepository(),
                userSettingsStore: new LocalUserSettingsStore(path));
            viewModel.RestoreUserSettings();

            AssertDefaults(viewModel);
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
    public void Invalid_saved_goal_falls_back_to_AAA_without_resetting_other_browse_state()
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
                "  \"BestBrowseMode\": \"goal\",\n" +
                "  \"BestLevel\": \"level_17\",\n" +
                "  \"BestVersion\": \"DDR WORLD\",\n" +
                "  \"BestGoal\": \"invalid\"\n" +
                "}\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var viewModel = new MainViewModel(
                new ScoreViewerRepository(),
                userSettingsStore: new LocalUserSettingsStore(path));
            viewModel.RestoreUserSettings();

            Assert.Equal(UserSettings.GoalBrowseMode, viewModel.BestBrowseMode);
            Assert.Equal(UserSettings.DefaultBestGoal, viewModel.BestGoalFilter);
            Assert.Equal("level_17", viewModel.BestLevelFilter);
            Assert.Equal("DDR WORLD", viewModel.BestVersionFilter);
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
            "Record status is not aggregated for song-title searches.",
            Localization.GetForLanguage(
                "曲名検索では記録状況を集計しません",
                UserSettings.EnglishLanguage));
        Assert.Equal(
            "곡명 검색에서는 기록 현황을 집계하지 않습니다.",
            Localization.GetForLanguage(
                "曲名検索では記録状況を集計しません",
                UserSettings.KoreanLanguage));
        Assert.Equal(
            "未登録の表示文言",
            Localization.GetForLanguage("未登録の表示文言", UserSettings.EnglishLanguage));
    }

    [Theory]
    [InlineData("プレー記録（07:00切り替え）", "play records (07:00 boundary)", "플레이 기록 (07:00 전환)")]
    [InlineData("保存済みプレーの件数", "Saved play count", "저장된 플레이 수")]
    [InlineData("総ノーツ数", "Total notes", "총 노트 수")]
    [InlineData("各判定数の合計", "Sum of all judgment counts", "각 판정 수 합계")]
    [InlineData("消費カロリー", "Calories burned", "소비 칼로리")]
    [InlineData("プレーごとの消費カロリー合計", "Total calories burned across plays", "플레이별 소비 칼로리 합계")]
    [InlineData("振り返りをコピー", "Copy recap", "요약 복사")]
    [InlineData("コピーしました", "Copied", "복사했습니다")]
    [InlineData("コピーできませんでした", "Could not copy", "복사하지 못했습니다")]
    public void Home_recap_strings_are_translated_for_supported_languages(
        string japaneseText,
        string englishText,
        string koreanText)
    {
        Assert.Equal(japaneseText, Localization.GetForLanguage(japaneseText, UserSettings.JapaneseLanguage));
        Assert.Equal(englishText, Localization.GetForLanguage(japaneseText, UserSettings.EnglishLanguage));
        Assert.Equal(koreanText, Localization.GetForLanguage(japaneseText, UserSettings.KoreanLanguage));
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
        viewModel.BestBrowseMode = UserSettings.VersionBrowseMode;
        viewModel.BestLevelFilter = "level_17";
        viewModel.BestVersionFilter = "DDR WORLD";
        viewModel.BestGoalFilter = "AA+";

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
        Assert.Equal(UserSettings.VersionBrowseMode, restartedViewModel.BestBrowseMode);
        Assert.Equal("level_17", restartedViewModel.BestLevelFilter);
        Assert.Equal("DDR WORLD", restartedViewModel.BestVersionFilter);
        Assert.Equal("AA+", restartedViewModel.BestGoalFilter);
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
    public void Browse_state_save_failure_is_reported_without_blocking_selection()
    {
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: new ThrowingUserSettingsStore());

        viewModel.BestBrowseMode = UserSettings.VersionBrowseMode;

        Assert.Equal(UserSettings.VersionBrowseMode, viewModel.BestBrowseMode);
        Assert.Contains("settings write failed", viewModel.SettingsStatusMessage);
    }

    [Fact]
    public void Reset_returns_draft_values_to_defaults_without_saving_them()
    {
        var saved = new UserSettings(
            StartMonitoringOnLaunch: false,
            NotifyUnresolvedResults: false,
            DefaultPlayStyle: UserSettings.DoublePlayStyle,
            StartupPage: UserSettings.HistoryStartupPage,
            BestBrowseMode: UserSettings.VersionBrowseMode,
            BestLevel: "level_17",
            BestVersion: "DDR WORLD");
        var store = new MemoryUserSettingsStore(saved);
        var viewModel = new MainViewModel(
            new ScoreViewerRepository(),
            userSettingsStore: store);
        viewModel.RestoreUserSettings();

        viewModel.ResetUserSettings();

        AssertVisibleDefaults(viewModel);
        Assert.Equal(saved.BestBrowseMode, viewModel.BestBrowseMode);
        Assert.Equal(saved.BestLevel, viewModel.BestLevelFilter);
        Assert.Equal(saved.BestVersion, viewModel.BestVersionFilter);
        Assert.Equal(saved.BestGoal, viewModel.BestGoalFilter);
        Assert.Equal(saved, store.StoredSettings);

        Assert.True(viewModel.SaveUserSettings());
        Assert.Equal(saved.BestBrowseMode, store.StoredSettings?.BestBrowseMode);
        Assert.Equal(saved.BestLevel, store.StoredSettings?.BestLevel);
        Assert.Equal(saved.BestVersion, store.StoredSettings?.BestVersion);
        Assert.Equal(saved.BestGoal, store.StoredSettings?.BestGoal);
    }

    private static void AssertDefaults(MainViewModel viewModel)
    {
        AssertVisibleDefaults(viewModel);
        Assert.Equal(UserSettings.LevelBrowseMode, viewModel.BestBrowseMode);
        Assert.Equal(UserSettings.DefaultBestLevel, viewModel.BestLevelFilter);
        Assert.Equal(UserSettings.DefaultBestVersion, viewModel.BestVersionFilter);
        Assert.Equal(UserSettings.DefaultBestGoal, viewModel.BestGoalFilter);
    }

    private static void AssertVisibleDefaults(MainViewModel viewModel)
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
