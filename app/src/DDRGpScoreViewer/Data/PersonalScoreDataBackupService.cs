using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private const int BackupFormatVersion = 1;
    private const string RestoredSourcePath = "personal-score-backup";
    private const string RestoredAppVersion = "personal-score-backup-v1";

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
                $"個人スコアデータのバックアップを作成しました。保存済みプレー: {plays.Count:N0}件。",
                plays.Count);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return new(
                false,
                $"バックアップを作成できませんでした。現在のデータは変更していません。{ToUserMessage(exception)}",
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
                InsertRestoredPlay(connection, transaction, play);
            }

            var restoredCount = ReadPlayCount(connection, transaction);
            if (restoredCount != document.Plays.Count)
            {
                throw new InvalidDataException("復元対象のプレー件数を確認できませんでした。");
            }

            transaction.Commit();
            return new(
                true,
                $"個人スコアデータを復元しました。保存済みプレー: {restoredCount:N0}件。",
                restoredCount);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return new(
                false,
                $"バックアップを復元できませんでした。現在のデータは変更していません。{ToUserMessage(exception)}",
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
                   rank, clear_type, flare_rank, duplicate_key
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
                reader.GetString(17)));
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
            document.FormatVersion != BackupFormatVersion ||
            document.Plays is null)
        {
            throw new InvalidDataException("対応していないバックアップ形式です。");
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
        RequireText(play.PlayedAt, "played_at");
        RequireText(play.SavedAt, "created_at");
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
        PersonalScoreBackupPlay play)
    {
        var sourceCaptureId = "backup-restore-" + Hash(play.PlayId);
        var captureHash = "backup-restore-hash-" + Hash(play.PlayId + "\0" + play.DuplicateKey);
        Execute(
            transaction,
            """
            INSERT INTO source_captures (
              capture_id, capture_hash, captured_at, source_kind, source_path
            ) VALUES ($capture_id, $capture_hash, $captured_at, 'manifest', $source_path);
            INSERT INTO plays (
              play_id, played_at, master_version, song_id, chart_id, score, max_combo,
              marvelous, perfect, great, good, miss, ex_score, rank, clear_type,
              flare_rank, capture_hash, source_capture_id, duplicate_key,
              analysis_confidence, app_version, created_at
            ) VALUES (
              $play_id, $played_at, $master_version, $song_id, $chart_id, $score, $max_combo,
              $marvelous, $perfect, $great, $good, $miss, $ex_score, $rank, $clear_type,
              $flare_rank, $capture_hash, $capture_id, $duplicate_key,
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
        JsonException or InvalidDataException =>
            "対応していない形式または壊れたバックアップです。",
        ViewerDatabaseException =>
            "保存済みデータの形式を確認できませんでした。",
        SqliteException =>
            "保存済みデータを確認できませんでした。",
        UnauthorizedAccessException =>
            "ファイルのアクセス権を確認してください。",
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
        [property: JsonPropertyName("score")] int Score,
        [property: JsonPropertyName("maxCombo")] int MaxCombo,
        [property: JsonPropertyName("marvelous")] int Marvelous,
        [property: JsonPropertyName("perfect")] int Perfect,
        [property: JsonPropertyName("great")] int Great,
        [property: JsonPropertyName("good")] int Good,
        [property: JsonPropertyName("miss")] int Miss,
        [property: JsonPropertyName("exScore")] int ExScore,
        [property: JsonPropertyName("rank")] string Rank,
        [property: JsonPropertyName("clearType")] string ClearType,
        [property: JsonPropertyName("flareRank")] string? FlareRank,
        [property: JsonPropertyName("duplicateKey")] string DuplicateKey);
}
