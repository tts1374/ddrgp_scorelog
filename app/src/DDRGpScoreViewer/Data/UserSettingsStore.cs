using System.IO;
using System.Text;
using System.Text.Json;

namespace DDRGpScoreViewer.Data;

public sealed record UserSettings(
    bool StartMonitoringOnLaunch,
    bool NotifyUnresolvedResults,
    string DefaultPlayStyle,
    string StartupPage)
{
    public const string SinglePlayStyle = "SINGLE";
    public const string DoublePlayStyle = "DOUBLE";
    public const string HomeStartupPage = "ホーム";
    public const string BestStartupPage = "自己ベスト";
    public const string HistoryStartupPage = "直近プレー履歴";

    public static UserSettings Defaults { get; } = new(
        StartMonitoringOnLaunch: true,
        NotifyUnresolvedResults: true,
        DefaultPlayStyle: SinglePlayStyle,
        StartupPage: HomeStartupPage);

    public bool IsValid =>
        IsValidPlayStyle(DefaultPlayStyle) && IsValidStartupPage(StartupPage);

    public static bool IsValidPlayStyle(string? value) =>
        value is SinglePlayStyle or DoublePlayStyle;

    public static bool IsValidStartupPage(string? value) =>
        value is HomeStartupPage or BestStartupPage or HistoryStartupPage;
}

public interface IUserSettingsStore
{
    UserSettings? Load();

    void Save(UserSettings settings);
}

public sealed class LocalUserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly Encoding Utf8NoBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly string filePath;

    public LocalUserSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DDRGpScoreViewer",
            "user-settings.json"))
    {
    }

    public LocalUserSettingsStore(string filePath)
    {
        this.filePath = Path.GetFullPath(filePath);
    }

    public UserSettings? Load()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath, Utf8NoBom);
            var stored = JsonSerializer.Deserialize<StoredUserSettings>(json, JsonOptions);
            if (stored?.StartMonitoringOnLaunch is not bool startMonitoringOnLaunch ||
                stored.NotifyUnresolvedResults is not bool notifyUnresolvedResults ||
                string.IsNullOrWhiteSpace(stored.DefaultPlayStyle) ||
                string.IsNullOrWhiteSpace(stored.StartupPage))
            {
                return null;
            }

            var settings = new UserSettings(
                startMonitoringOnLaunch,
                notifyUnresolvedResults,
                stored.DefaultPlayStyle,
                stored.StartupPage);
            return settings.IsValid ? settings : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new ArgumentException("The user settings contain an unsupported value.", nameof(settings));
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("User settings directory could not be determined.");
        }

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(
            new StoredUserSettings(
                settings.StartMonitoringOnLaunch,
                settings.NotifyUnresolvedResults,
                settings.DefaultPlayStyle,
                settings.StartupPage),
            JsonOptions) + "\n";
        File.WriteAllText(filePath, json, Utf8NoBom);
    }

    private sealed record StoredUserSettings(
        bool? StartMonitoringOnLaunch,
        bool? NotifyUnresolvedResults,
        string? DefaultPlayStyle,
        string? StartupPage);
}
