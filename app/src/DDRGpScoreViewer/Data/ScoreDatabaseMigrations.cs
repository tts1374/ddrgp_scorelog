using Microsoft.Data.Sqlite;

namespace DDRGpScoreViewer.Data;

internal sealed class ScoreDatabaseV1ToV2Migration : IScoreDatabaseMigration
{
    public int FromVersion => 1;

    public int ToVersion => 2;

    public void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        ScoreViewerRepository.ValidateScoreDatabaseForMigration(connection, FromVersion);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE INDEX IF NOT EXISTS idx_plays_played_at_order
              ON plays(julianday(played_at) DESC, played_at DESC, play_id DESC);
            CREATE INDEX IF NOT EXISTS idx_plays_song_chart_order
              ON plays(song_id, chart_id,
                       julianday(played_at) DESC, played_at DESC, play_id DESC);
            INSERT INTO schema_migrations (
              migration_id, schema_version, app_version, notes
            )
            VALUES (
              '002_play_order_indexes', 2, 'schema-contract',
              'Added timezone-aware chronological play ordering indexes.');
            UPDATE score_db_metadata
            SET value = '2'
            WHERE key = 'schema_version';
            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
    }
}

internal sealed class ScoreDatabaseV2ToV3Migration : IScoreDatabaseMigration
{
    public int FromVersion => 2;

    public int ToVersion => 3;

    public void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        ScoreViewerRepository.ValidateScoreDatabaseForMigration(connection, FromVersion);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            ALTER TABLE plays ADD COLUMN ok INTEGER CHECK (ok IS NULL OR ok >= 0);
            ALTER TABLE plays ADD COLUMN calories REAL CHECK (
              calories IS NULL OR calories >= 0.0
            );
            INSERT INTO schema_migrations (
              migration_id, schema_version, app_version, notes
            )
            VALUES (
              '003_optional_result_metrics', 3, 'schema-contract',
              'Added nullable O.K. and calories play values.');
            UPDATE score_db_metadata
            SET value = '3'
            WHERE key = 'schema_version';
            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();
    }
}
