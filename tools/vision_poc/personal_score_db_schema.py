from __future__ import annotations

import sqlite3
from dataclasses import dataclass

PERSONAL_SCORE_DB_SCHEMA_NAME = "personal_score_db"
PERSONAL_SCORE_DB_SCHEMA_VERSION = 1
PERSONAL_SCORE_DB_CREATED_BY = "tools.vision_poc.personal_score_db_schema"
PERSONAL_SCORE_DB_CONTRACT_SCOPE = "production_personal_score_db"
PERSONAL_SCORE_DB_PRODUCTION_STATUS = "production_schema"
PERSONAL_SCORE_DB_PREVIEW_REJECTION_STATUS = "rejects_m8_score_db_preview"

PERSONAL_SCORE_DB_METADATA_TABLE = "score_db_metadata"
PERSONAL_SCORE_DB_MIGRATIONS_TABLE = "schema_migrations"
PERSONAL_SCORE_DB_REQUIRED_TABLES = (
    "score_db_metadata",
    "schema_migrations",
    "source_captures",
    "plays",
    "analysis_logs",
)
PERSONAL_SCORE_DB_MIGRATION_PLAN_STATUSES = (
    "compatible",
    "initialize_empty_database",
    "manual_migration_required",
    "reject_m8_preview_database",
    "reject_unknown_database",
)
PERSONAL_SCORE_DB_IDENTITY_METADATA_KEYS = (
    "created_by",
    "schema_name",
    "schema_contract_scope",
    "production_schema_status",
)

PERSONAL_SCORE_DB_METADATA = {
    "created_by": PERSONAL_SCORE_DB_CREATED_BY,
    "schema_name": PERSONAL_SCORE_DB_SCHEMA_NAME,
    "schema_version": str(PERSONAL_SCORE_DB_SCHEMA_VERSION),
    "schema_version_source": "PRAGMA user_version and score_db_metadata",
    "schema_contract_scope": PERSONAL_SCORE_DB_CONTRACT_SCOPE,
    "production_schema_status": PERSONAL_SCORE_DB_PRODUCTION_STATUS,
    "preview_schema_status": PERSONAL_SCORE_DB_PREVIEW_REJECTION_STATUS,
}

PERSONAL_SCORE_DB_PLAYS_COLUMNS = (
    "play_id",
    "played_at",
    "master_version",
    "song_id",
    "chart_id",
    "score",
    "max_combo",
    "marvelous",
    "perfect",
    "great",
    "good",
    "miss",
    "ex_score",
    "rank",
    "clear_type",
    "capture_hash",
    "source_capture_id",
    "duplicate_key",
    "analysis_confidence",
    "app_version",
    "created_at",
)

PERSONAL_SCORE_DB_ANALYSIS_LOG_COLUMNS = (
    "analysis_id",
    "play_id",
    "source_capture_id",
    "analysis_status",
    "save_boundary_status",
    "skip_reason",
    "event_type",
    "confirmed_result",
    "duplicate",
    "confirmation_mode",
    "timestamp_ms",
    "candidate_duration_ms",
    "identity_signal_status",
    "digit_review_status",
    "analysis_confidence",
    "analysis_summary_json",
    "log_path",
    "app_version",
    "created_at",
)

PERSONAL_SCORE_DB_SCHEMA_SQL = """
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS score_db_metadata (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS schema_migrations (
  migration_id TEXT PRIMARY KEY,
  schema_version INTEGER NOT NULL,
  applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  app_version TEXT NOT NULL,
  notes TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS source_captures (
  capture_id TEXT PRIMARY KEY,
  capture_hash TEXT NOT NULL UNIQUE,
  captured_at TEXT NOT NULL,
  source_kind TEXT NOT NULL CHECK (
    source_kind IN ('manifest', 'timestamped', 'capture', 'manual', 'unknown')
  ),
  source_path TEXT NOT NULL,
  manifest_image_path TEXT NOT NULL DEFAULT '',
  frame_index INTEGER,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS plays (
  play_id TEXT PRIMARY KEY,
  played_at TEXT NOT NULL,
  master_version TEXT NOT NULL,
  song_id TEXT NOT NULL,
  chart_id TEXT NOT NULL,
  score INTEGER NOT NULL CHECK (score BETWEEN 0 AND 1000000),
  max_combo INTEGER NOT NULL CHECK (max_combo >= 0),
  marvelous INTEGER NOT NULL CHECK (marvelous >= 0),
  perfect INTEGER NOT NULL CHECK (perfect >= 0),
  great INTEGER NOT NULL CHECK (great >= 0),
  good INTEGER NOT NULL CHECK (good >= 0),
  miss INTEGER NOT NULL CHECK (miss >= 0),
  ex_score INTEGER NOT NULL CHECK (ex_score >= 0),
  rank TEXT NOT NULL,
  clear_type TEXT NOT NULL,
  capture_hash TEXT NOT NULL REFERENCES source_captures(capture_hash),
  source_capture_id TEXT NOT NULL REFERENCES source_captures(capture_id),
  duplicate_key TEXT NOT NULL UNIQUE,
  analysis_confidence REAL NOT NULL CHECK (
    analysis_confidence >= 0.0 AND analysis_confidence <= 1.0
  ),
  app_version TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS analysis_logs (
  analysis_id TEXT PRIMARY KEY,
  play_id TEXT REFERENCES plays(play_id),
  source_capture_id TEXT REFERENCES source_captures(capture_id),
  analysis_status TEXT NOT NULL CHECK (
    analysis_status IN ('saved', 'skipped', 'low_confidence', 'error')
  ),
  save_boundary_status TEXT NOT NULL,
  skip_reason TEXT NOT NULL DEFAULT '',
  event_type TEXT NOT NULL,
  confirmed_result INTEGER NOT NULL CHECK (confirmed_result IN (0, 1)),
  duplicate INTEGER NOT NULL CHECK (duplicate IN (0, 1)),
  confirmation_mode TEXT NOT NULL,
  timestamp_ms INTEGER,
  candidate_duration_ms INTEGER,
  identity_signal_status TEXT NOT NULL DEFAULT '',
  digit_review_status TEXT NOT NULL DEFAULT '',
  analysis_confidence REAL CHECK (
    analysis_confidence IS NULL
    OR (analysis_confidence >= 0.0 AND analysis_confidence <= 1.0)
  ),
  analysis_summary_json TEXT NOT NULL DEFAULT '',
  log_path TEXT NOT NULL DEFAULT '',
  app_version TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_plays_played_at ON plays(played_at);
CREATE INDEX IF NOT EXISTS idx_plays_song_chart ON plays(song_id, chart_id);
CREATE INDEX IF NOT EXISTS idx_plays_capture_hash ON plays(capture_hash);
CREATE INDEX IF NOT EXISTS idx_analysis_logs_play_id ON analysis_logs(play_id);
CREATE INDEX IF NOT EXISTS idx_analysis_logs_source_capture_id
  ON analysis_logs(source_capture_id);
CREATE INDEX IF NOT EXISTS idx_source_captures_capture_hash
  ON source_captures(capture_hash);
"""


@dataclass(frozen=True)
class PersonalScoreDbSchemaInspection:
    user_version: int
    table_names: tuple[str, ...]
    metadata: dict[str, str]
    missing_required_tables: tuple[str, ...]
    compatibility_errors: tuple[str, ...]
    migration_plan_status: str
    migration_plan_reason: str

    @property
    def is_compatible(self) -> bool:
        return not self.compatibility_errors


def create_personal_score_db_schema(connection: sqlite3.Connection) -> None:
    connection.executescript(PERSONAL_SCORE_DB_SCHEMA_SQL)
    connection.execute(f"PRAGMA user_version = {PERSONAL_SCORE_DB_SCHEMA_VERSION}")
    connection.executemany(
        """
        INSERT OR REPLACE INTO score_db_metadata (key, value)
        VALUES (?, ?)
        """,
        sorted(PERSONAL_SCORE_DB_METADATA.items()),
    )
    connection.execute(
        """
        INSERT OR REPLACE INTO schema_migrations (
          migration_id, schema_version, app_version, notes
        )
        VALUES (?, ?, ?, ?)
        """,
        (
            "001_initial_personal_score_db_schema",
            PERSONAL_SCORE_DB_SCHEMA_VERSION,
            "schema-contract",
            "Initial formal personal score DB schema contract.",
        ),
    )
    connection.commit()


def sqlite_table_names(connection: sqlite3.Connection) -> tuple[str, ...]:
    return tuple(
        str(row[0])
        for row in connection.execute(
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
            ORDER BY name
            """
        )
    )


def sqlite_table_exists(connection: sqlite3.Connection, table_name: str) -> bool:
    row = connection.execute(
        """
        SELECT 1
        FROM sqlite_master
        WHERE type = 'table' AND name = ?
        """,
        (table_name,),
    ).fetchone()
    return row is not None


def read_score_db_metadata(connection: sqlite3.Connection) -> dict[str, str]:
    if not sqlite_table_exists(connection, PERSONAL_SCORE_DB_METADATA_TABLE):
        return {}
    return {
        str(key): str(value)
        for key, value in connection.execute(
            "SELECT key, value FROM score_db_metadata ORDER BY key"
        )
    }


def _personal_score_db_migration_plan_status(
    *,
    table_names: tuple[str, ...],
    compatibility_errors: tuple[str, ...],
) -> tuple[str, str]:
    if not compatibility_errors:
        return "compatible", "schema_compatible"
    if not table_names:
        return "initialize_empty_database", "no_user_tables"
    if "m8_preview_database_not_supported" in compatibility_errors:
        return "reject_m8_preview_database", "preview_schema_is_not_production"
    if (
        "unknown_database_not_supported" in compatibility_errors
        or "score_db_metadata_missing" in compatibility_errors
        or any(
            error.startswith(f"score_db_metadata.{key}_")
            for error in compatibility_errors
            for key in PERSONAL_SCORE_DB_IDENTITY_METADATA_KEYS
        )
    ):
        return "reject_unknown_database", "metadata_does_not_identify_formal_schema"
    return "manual_migration_required", "formal_schema_contract_mismatch"


def personal_score_db_compatibility_errors(
    connection: sqlite3.Connection,
) -> list[str]:
    errors: list[str] = []
    user_version = int(connection.execute("PRAGMA user_version").fetchone()[0])
    table_names = sqlite_table_names(connection)
    if user_version != PERSONAL_SCORE_DB_SCHEMA_VERSION:
        errors.append("schema_version_mismatch")

    if "preview_metadata" in table_names:
        errors.append("m8_preview_database_not_supported")

    metadata = read_score_db_metadata(connection)
    if (
        table_names
        and not metadata
        and "preview_metadata" not in table_names
    ):
        errors.append("unknown_database_not_supported")

    for table_name in PERSONAL_SCORE_DB_REQUIRED_TABLES:
        if table_name not in table_names:
            errors.append(f"missing_table:{table_name}")

    if not metadata:
        errors.append("score_db_metadata_missing")
        return errors

    for key, expected_value in PERSONAL_SCORE_DB_METADATA.items():
        actual_value = metadata.get(key)
        if actual_value is None:
            errors.append(f"score_db_metadata.{key}_missing")
        elif actual_value != expected_value:
            errors.append(f"score_db_metadata.{key}_mismatch")
    return errors


def inspect_personal_score_db_schema(
    connection: sqlite3.Connection,
) -> PersonalScoreDbSchemaInspection:
    user_version = int(connection.execute("PRAGMA user_version").fetchone()[0])
    table_names = sqlite_table_names(connection)
    metadata = read_score_db_metadata(connection)
    missing_required_tables = tuple(
        table_name
        for table_name in PERSONAL_SCORE_DB_REQUIRED_TABLES
        if table_name not in table_names
    )
    compatibility_errors = tuple(personal_score_db_compatibility_errors(connection))
    migration_plan_status, migration_plan_reason = _personal_score_db_migration_plan_status(
        table_names=table_names,
        compatibility_errors=compatibility_errors,
    )
    return PersonalScoreDbSchemaInspection(
        user_version=user_version,
        table_names=table_names,
        metadata=metadata,
        missing_required_tables=missing_required_tables,
        compatibility_errors=compatibility_errors,
        migration_plan_status=migration_plan_status,
        migration_plan_reason=migration_plan_reason,
    )


def assert_personal_score_db_compatible(
    connection: sqlite3.Connection,
) -> PersonalScoreDbSchemaInspection:
    inspection = inspect_personal_score_db_schema(connection)
    if inspection.compatibility_errors:
        joined_errors = ", ".join(inspection.compatibility_errors)
        msg = f"personal score DB is not compatible: {joined_errors}"
        raise ValueError(msg)
    return inspection
