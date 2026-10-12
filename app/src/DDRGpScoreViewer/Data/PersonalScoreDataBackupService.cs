using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DDRGpScoreViewer;
using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Data;

public interface IPersonalScoreDataBackupService
{
    PersonalScoreDataBackupResult CreateBackup(string scoreDatabasePath, string backupPath);

    PersonalScoreDataBackupResult RestoreBackup(string scoreDatabasePath, string backupPath);
}

public sealed record PersonalScoreDataBackupResult(
    bool Succeeded,
    string Message,
    int PlayCount);

public sealed class PersonalScoreDataBackupService : IPersonalScoreDataBackupService
{
    private const string BackupFormat = "ddrgp.personal-score-data";
    private const int BackupFormatVersion = 2;
    private const string RestoredSourcePath = "personal-score-backup";
    private const string RestoredAppVersion = "personal-score-backup-v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public PersonalScoreDataBackupResult CreateBackup(
        string scoreDatabasePath,
        string backupPath)
    {
        try
        {
            var paths = ValidatePaths(scoreDatabasePath, backupPath, requireBackupFile: false);
            IReadOnlyList<PersonalScoreBackupPlay> plays;
            using (var connection = Open(paths.ScoreDatabasePath, SqliteOpenMode.ReadOnly))
            {
                EnableForeignKeys(connection);
                ScoreViewerRepository.ValidateScoreDatabaseForWrite(connection);
                plays = ReadPlays(connection);
            }

            var document = new PersonalScoreBackupDocument(
                BackupFormat,
                BackupFormatVersion,
                DateTimeOffset.UtcNow.ToString("O"),
                plays);
            var json = JsonSerializer.Serialize(document, JsonOptions) + "\n";
            var pendingPath = paths.BackupPath + $".{Guid.NewGuid():N}.pending";
            try
            {
                File.WriteAllText(pendingPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(pendingPath, paths.BackupPath, overwrite: true);
            }
            finally
            {
                TryDelete(pendingPath);
            }

            return new(
                true,
                Localization.Format(
                    "個人スコアデータのバックアップを作成しました。保存済みプレー: {0:N0}件。",
                    plays.Count),
                plays.Count);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return new(
                false,
                Localization.Format(
                    "バックアップを作成できませんでした。現在のデータは変更していません。{0}",
                    ToUserMessage(exception)),
                0);
        }
    }

    public PersonalScoreDataBackupResult RestoreBackup(
        string scoreDatabasePath,
        string backupPath)
    {
        try
        {
            var paths = ValidatePaths(scoreDatabasePath, backupPath, requireBackupFile: true);
            var document = ReadAndValidateBackup(paths.BackupPath);
            var restoreSessionId = Guid.NewGuid().ToString("N");

            using var connection = Open(paths.ScoreDatabasePath, SqliteOpenMode.ReadWrite);
            EnableForeignKeys(connection);
            ScoreViewerRepository.ValidateScoreDatabaseForWrite(connection);
            using var transaction = connection.BeginTransaction();
            Execute(
                transaction,
                "UPDATE analysis_logs SET play_id = NULL WHERE play_id IN (SELECT play_id FROM plays); " +
                "DELETE FROM plays;");
            foreach (var play in document.Plays)
            {
                InsertRestoredPlay(connection, transaction, play, restoreSessionId);
            }

            var restoredCount = ReadPlayCount(connection, transaction);
            if (restoredCount != document.Plays.Count)
            {
                throw new InvalidDataException("復元対象のプレー件数を確認できませんでした。");
            }

            transaction.Commit();
            return new(
                true,
                Localization.Format(
                    "個人スコアデータを復元しました。保存済みプレー: {0:N0}件。",
                    restoredCount),
                restoredCount);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return new(
                false,
                Localization.Format(
                    "バックアップを復元できませんでした。現在のデータは変更していません。{0}",
                    ToUserMessage(exception)),
                0);
        }
    }

    private static PersonalScoreBackupPaths ValidatePaths(
        string scoreDatabasePath,
        string backupPath,
        bool requireBackupFile)
    {
        if (string.IsNullOrWhiteSpace(scoreDatabasePath))
        {
            throw new ArgumentException("保存済みデータの場所を確認できません。");
        }
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            throw new ArgumentException("バックアップファイルの場所を選択してください。");
        }

        var fullScorePath = Path.GetFullPath(scoreDatabasePath);
        var fullBackupPath = Path.GetFullPath(backupPath);
        if (!File.Exists(fullScorePath) || new FileInfo(fullScorePath).Length == 0)
        {
            throw new FileNotFoundException("保存済みデータが見つかりません。");
        }
        if (string.Equals(fullScorePath, fullBackupPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("保存済みデータと同じファイルは選択できません。");
        }
        if (requireBackupFile &&
            (!File.Exists(fullBackupPath) || new FileInfo(fullBackupPath).Length == 0))
        {
            throw new FileNotFoundException("バックアップファイルが見つかりません。");
        }

        var backupDirectory = Path.GetDirectoryName(fullBackupPath);
        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
        {
            throw new DirectoryNotFoundException("バックアップ先のフォルダーが見つかりません。");
        }

        return new(fullScorePath, fullBackupPath);
    }

    private static IReadOnlyList<PersonalScoreBackupPlay> ReadPlays(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT play_id, played_at, created_at, master_version, song_id, chart_id,
                   score, max_combo, marvelous, perfect, great, good, miss, ex_score,
                   rank, clear_type, flare_rank, duplicate_key, ok, calories
            FROM plays
            ORDER BY play_id;
            """;
        using var reader = command.ExecuteReader();
        var plays = new List<PersonalScoreBackupPlay>();
        while (reader.Read())
        {
            plays.Add(new PersonalScoreBackupPlay(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.GetInt32(13),
                reader.GetString(14),
                reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetInt32(18),
                reader.IsDBNull(19) ? null : reader.GetDouble(19)));
        }

        return plays;
    }

    private static PersonalScoreBackupDocument ReadAndValidateBackup(string backupPath)
    {
        var document = JsonSerializer.Deserialize<PersonalScoreBackupDocument>(
            File.ReadAllText(backupPath),
            JsonOptions)
            ?? throw new InvalidDataException("バックアップの内容が空です。");
        if (!string.Equals(document.Format, BackupFormat, StringComparison.Ordinal) ||
            document.FormatVersion is not (1 or BackupFormatVersion) ||
            document.Plays is null)
        {
            throw new InvalidDataException("対応していないバックアップ形式です。");
        }
        if (document.FormatVersion == 1)
        {
            document = document with
            {
                Plays = document.Plays
                    .Select(play => play with { Ok = null, Calories = null })
                    .ToArray(),
            };
        }
        if (!DateTimeOffset.TryParse(
                document.CreatedAt,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out _))
        {
            throw new InvalidDataException("バックアップの作成日時を確認できません。");
        }

        var playIds = new HashSet<string>(StringComparer.Ordinal);
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var play in document.Plays)
        {
            ValidatePlay(play, playIds, duplicateKeys);
        }

        return document;
    }

    private static void ValidatePlay(
        PersonalScoreBackupPlay play,
        ISet<string> playIds,
        ISet<string> duplicateKeys)
    {
        RequireText(play.PlayId, "play_id", playIds);
        RequireTimestamp(play.PlayedAt, "プレー日時");
        RequireTimestamp(play.SavedAt, "保存日時", allowSqliteUtc: true);
        RequireText(play.MasterVersion, "master_version");
        RequireText(play.SongId, "song_id");
        RequireText(play.ChartId, "chart_id");
        RequireText(play.Rank, "rank");
        RequireText(play.ClearType, "clear_type");
        RequireText(play.DuplicateKey, "duplicate_key", duplicateKeys);
        if (play.Score is < 0 or > 1_000_000 || play.Score % 10 != 0)
        {
            throw new InvalidDataException("バックアップのスコアを確認できません。");
        }
        if (play.MaxCombo < 0 ||
            play.Marvelous < 0 ||
            play.Perfect < 0 ||
            play.Great < 0 ||
            play.Good < 0 ||
            play.Miss < 0 ||
            play.ExScore < 0)
        {
            throw new InvalidDataException("バックアップの判定数を確認できません。");
        }
        if (play.FlareRank is not null &&
            !new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX" }
                .Contains(play.FlareRank, StringComparer.Ordinal))
        {
            throw new InvalidDataException("バックアップのフレアランクを確認できません。");
        }
        if (play.Ok < 0)
        {
            throw new InvalidDataException("バックアップのO.K.数を確認できません。");
        }
        if (play.Calories is not null &&
            (!double.IsFinite(play.Calories.Value) || play.Calories.Value < 0.0))
        {
            throw new InvalidDataException("バックアップの消費カロリーを確認できません。");
        }
    }

    private static void RequireTimestamp(string? value, string field, bool allowSqliteUtc = false)
    {
        var validTimestamp = IsFormalTimestamp(value) || IsAppWriterTimestamp(value);
        if (!validTimestamp &&
            !(allowSqliteUtc && DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out _)))
        {
            throw new InvalidDataException($"バックアップの{field}を確認できません。");
        }
    }

    private static bool IsAppWriterTimestamp(string? value)
    {
        if (value is null) return false;
        // The app-owned writer also stores invariant-culture calendar timestamps.
        // Require a complete date and offset so parsing never supplies a date.
        // The legacy writer accepts a missing clock as midnight; keep its raw value.
        const string date = @"(?<date>[\s\S]+?)";
        const string clock = @"[0-9]{1,2}(?::[0-9]{1,2}(?::[0-9]{1,2}(?:[.,][0-9]+)?)?)?(?:\s*[AP]\.?M\.?)?";
        const string offset = @"(?:Z|GMT|[+-][0-9]{1,2}(?::[0-9]{1,2}|[0-9]{2})?)";
        const string separator = @"(?:T|\s+)";
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp))
        {
            return false;
        }

        // Ignore permitted whitespace around punctuation only for the shape check.
        // The original text has already been parsed and is still the restored value.
        var candidate = Regex.Replace(value, @"\s*([:/.,+\-])\s*", "$1");
        foreach (var pattern in new[]
        {
            date + separator + clock + @"\s*" + offset,
            clock + separator + date + @"\s*" + offset,
            clock + @"\s*" + offset + separator + date,
            date + @"\s*" + offset + separator + clock,
            offset + @"\s*" + date + separator + clock,
            offset + @"\s*" + clock + separator + date,
            date + @"\s*" + offset,
            offset + @"\s*" + date,
        })
        {
            var match = Regex.Match(candidate, @"\A\s*" + pattern + @"\s*\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var calendar = match.Groups["date"].Value;
            if (!DateTime.TryParse(calendar, CultureInfo.InvariantCulture,
                    DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.RoundtripKind, out var parsedDate) ||
                parsedDate.Kind != DateTimeKind.Unspecified || parsedDate.TimeOfDay != TimeSpan.Zero ||
                parsedDate.Date != timestamp.Date)
            {
                continue;
            }
            var dateNumbers = Regex.Matches(calendar, "[0-9]+").Count;
            var format = CultureInfo.InvariantCulture.DateTimeFormat;
            if (dateNumbers == 3 ||
                (dateNumbers == 2 && format.MonthNames.Concat(format.AbbreviatedMonthNames)
                    .Any(month => month.Length > 0 && calendar.Contains(month, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsFormalTimestamp(string? value)
    {
        if (value is null) return false;
        // Match the ISO forms accepted by Python's formal writer without imposing
        // DateTimeOffset's fractional precision or +/-14-hour offset limits.
        var match = Regex.Match(
            value,
            @"\A(?<date>(?<year>[0-9]{4})(?:(?<dateSeparator>-?)(?<month>[0-9]{2})\k<dateSeparator>(?<day>[0-9]{2})|-W(?<week>[0-9]{2})(?:-(?<weekDay>[1-7])(?![0-9]))?|W(?<week>[0-9]{2})(?<weekDay>[1-7])?))" +
            @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[\s\S])" +
            @"(?<hour>[0-9]{2})(?:(?<timeSeparator>:?)(?<minute>[0-9]{2})(?:\k<timeSeparator>(?<second>[0-9]{2}))?)?(?:[.,][0-9]*)?" +
            @"(?:Z|[+-](?<offsetHour>[0-9]{2})(?:(?<offsetSeparator>:?)(?<offsetMinute>[0-9]{2})(?:\k<offsetSeparator>(?<offsetSecond>[0-9]{2}))?)?(?:[.,][0-9]+)?)\z");
        if (!match.Success) return false;

        int Part(string name) => match.Groups[name].Success
            ? int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture)
            : 0;

        if (match.Groups["week"].Success)
        {
            var year = Part("year");
            var week = Part("week");
            if (year is < 1 or > 9999 || week < 1 || week > ISOWeek.GetWeeksInYear(year))
            {
                return false;
            }
            var day = match.Groups["weekDay"].Success ? Part("weekDay") : 1;
            try
            {
                _ = ISOWeek.ToDateTime(year, week, (DayOfWeek)(day % 7));
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
        else if (!DateOnly.TryParseExact(
            match.Groups["date"].Value,
            new[] { "yyyy-MM-dd", "yyyyMMdd" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _))
        {
            return false;
        }

        // The Python writer accepts an empty clock fraction before the timezone.
        // Python permits normalized offset components; the total must be below 24 hours.
        var offsetSeconds = Part("offsetHour") * 3600 + Part("offsetMinute") * 60 + Part("offsetSecond");
        return Part("hour") < 24 && Part("minute") < 60 && Part("second") < 60 && offsetSeconds < 86400;
    }

    private static void RequireText(
        string? value,
        string field,
        ISet<string>? distinctValues = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"バックアップの{field}を確認できません。");
        }
        if (distinctValues is not null && !distinctValues.Add(value))
        {
            throw new InvalidDataException($"バックアップ内の{field}が重複しています。");
        }
    }

    private static void InsertRestoredPlay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonalScoreBackupPlay play,
        string restoreSessionId)
    {
        var sourceCaptureId = $"backup-restore-{restoreSessionId}-{Hash(play.PlayId)}";
        var captureHash = "backup-restore-hash-" +
            Hash(restoreSessionId + "\0" + play.PlayId + "\0" + play.DuplicateKey);
        Execute(
            transaction,
            """
            INSERT INTO source_captures (
              capture_id, capture_hash, captured_at, source_kind, source_path
            ) VALUES ($capture_id, $capture_hash, $captured_at, 'manifest', $source_path);
            INSERT INTO plays (
              play_id, played_at, master_version, song_id, chart_id, score, max_combo,
              marvelous, perfect, great, good, miss, ex_score, rank, clear_type,
              flare_rank, ok, calories, capture_hash, source_capture_id, duplicate_key,
              analysis_confidence, app_version, created_at
            ) VALUES (
              $play_id, $played_at, $master_version, $song_id, $chart_id, $score, $max_combo,
              $marvelous, $perfect, $great, $good, $miss, $ex_score, $rank, $clear_type,
              $flare_rank, $ok, $calories, $capture_hash, $capture_id, $duplicate_key,
              1.0, $app_version, $created_at
            );
            """,
            ("$capture_id", sourceCaptureId),
            ("$capture_hash", captureHash),
            ("$captured_at", play.PlayedAt),
            ("$source_path", RestoredSourcePath),
            ("$play_id", play.PlayId),
            ("$played_at", play.PlayedAt),
            ("$master_version", play.MasterVersion),
            ("$song_id", play.SongId),
            ("$chart_id", play.ChartId),
            ("$score", play.Score),
            ("$max_combo", play.MaxCombo),
            ("$marvelous", play.Marvelous),
            ("$perfect", play.Perfect),
            ("$great", play.Great),
            ("$good", play.Good),
            ("$miss", play.Miss),
            ("$ex_score", play.ExScore),
            ("$rank", play.Rank),
            ("$clear_type", play.ClearType),
            ("$flare_rank", (object?)play.FlareRank ?? DBNull.Value),
            ("$ok", (object?)play.Ok ?? DBNull.Value),
            ("$calories", (object?)play.Calories ?? DBNull.Value),
            ("$duplicate_key", play.DuplicateKey),
            ("$app_version", RestoredAppVersion),
            ("$created_at", play.SavedAt));
    }

    private static int ReadPlayCount(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM plays;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Execute(
        SqliteTransaction transaction,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void EnableForeignKeys(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string ToUserMessage(Exception exception) => exception switch
    {
        InvalidDataException when exception.Message is
            "バックアップのプレー日時を確認できません。" or
            "バックアップの保存日時を確認できません。" =>
            Localization.Get(exception.Message),
        JsonException or InvalidDataException =>
            Localization.Get("対応していない形式または壊れたバックアップです。"),
        ViewerDatabaseException =>
            Localization.Get("保存済みデータの形式を確認できませんでした。"),
        SqliteException =>
            Localization.Get("保存済みデータを確認できませんでした。"),
        UnauthorizedAccessException =>
            Localization.Get("ファイルのアクセス権を確認してください。"),
        _ => exception.Message,
    };

    private static bool IsExpectedFailure(Exception exception) => exception is
        ArgumentException or
        DirectoryNotFoundException or
        FileNotFoundException or
        IOException or
        InvalidDataException or
        InvalidOperationException or
        JsonException or
        UnauthorizedAccessException or
        SqliteException or
        ViewerDatabaseException;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A failed temporary-file cleanup must not replace the original operation result.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed temporary-file cleanup must not replace the original operation result.
        }
    }

    private sealed record PersonalScoreBackupPaths(string ScoreDatabasePath, string BackupPath);

    private sealed record PersonalScoreBackupDocument(
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("formatVersion")] int FormatVersion,
        [property: JsonPropertyName("createdAt")] string CreatedAt,
        [property: JsonPropertyName("plays")] IReadOnlyList<PersonalScoreBackupPlay> Plays);

    private sealed record PersonalScoreBackupPlay(
        [property: JsonPropertyName("playId")] string PlayId,
        [property: JsonPropertyName("playedAt")] string PlayedAt,
        [property: JsonPropertyName("savedAt")] string SavedAt,
        [property: JsonPropertyName("masterVersion")] string MasterVersion,
        [property: JsonPropertyName("songId")] string SongId,
        [property: JsonPropertyName("chartId")] string ChartId,
        [property: JsonPropertyName("score"), JsonRequired] int Score,
        [property: JsonPropertyName("maxCombo"), JsonRequired] int MaxCombo,
        [property: JsonPropertyName("marvelous"), JsonRequired] int Marvelous,
        [property: JsonPropertyName("perfect"), JsonRequired] int Perfect,
        [property: JsonPropertyName("great"), JsonRequired] int Great,
        [property: JsonPropertyName("good"), JsonRequired] int Good,
        [property: JsonPropertyName("miss"), JsonRequired] int Miss,
        [property: JsonPropertyName("exScore"), JsonRequired] int ExScore,
        [property: JsonPropertyName("rank")] string Rank,
        [property: JsonPropertyName("clearType")] string ClearType,
        [property: JsonPropertyName("flareRank")] string? FlareRank,
        [property: JsonPropertyName("duplicateKey")] string DuplicateKey,
        [property: JsonPropertyName("ok")] int? Ok = null,
        [property: JsonPropertyName("calories")] double? Calories = null);
}
