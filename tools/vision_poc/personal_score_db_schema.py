from __future__ import annotations

import sqlite3
from contextlib import closing
from dataclasses import dataclass
from functools import cache
from pathlib import Path

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
PERSONAL_SCORE_DB_MIGRATION_HISTORY = (
    ("001_initial_personal_score_db_schema", 1),
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

PERSONAL_SCORE_DB_SOURCE_CAPTURE_COLUMNS = (
    "capture_id",
    "capture_hash",
    "captured_at",
    "source_kind",
    "source_path",
    "manifest_image_path",
    "frame_index",
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


@dataclass(frozen=True)
class PersonalScoreDbInitializationResult:
    before: PersonalScoreDbSchemaInspection
    after: PersonalScoreDbSchemaInspection
    initialized: bool


@dataclass(frozen=True)
class PersonalScoreDbFilePreparationResult:
    path: Path
    existed_before: bool
    size_before: int | None
    initialization: PersonalScoreDbInitializationResult
    inspection: PersonalScoreDbSchemaInspection

    @property
    def initialized(self) -> bool:
        return self.initialization.initialized


def personal_score_db_schema_inspection_diagnostic(
    inspection: PersonalScoreDbSchemaInspection,
    *,
    path: Path | None = None,
) -> dict[str, object]:
    present_required_tables = tuple(
        table_name
        for table_name in PERSONAL_SCORE_DB_REQUIRED_TABLES
        if table_name in inspection.table_names
    )
    metadata_identity = {
        key: {
            "expected": expected_value,
            "actual": inspection.metadata.get(key, ""),
            "status": _metadata_identity_diagnostic_status(
                actual_value=inspection.metadata.get(key),
                expected_value=expected_value,
            ),
        }
        for key, expected_value in PERSONAL_SCORE_DB_METADATA.items()
        if key in PERSONAL_SCORE_DB_IDENTITY_METADATA_KEYS
    }
    return {
        "path": str(path) if path is not None else "",
        "schema_name": PERSONAL_SCORE_DB_SCHEMA_NAME,
        "expected_schema_version": PERSONAL_SCORE_DB_SCHEMA_VERSION,
        "user_version": inspection.user_version,
        "is_compatible": inspection.is_compatible,
        "migration_plan_status": inspection.migration_plan_status,
        "migration_plan_reason": inspection.migration_plan_reason,
        "compatibility_errors": list(inspection.compatibility_errors),
        "required_tables": {
            "present": list(present_required_tables),
            "missing": list(inspection.missing_required_tables),
        },
        "metadata_identity": metadata_identity,
        "metadata": dict(sorted(inspection.metadata.items())),
        "table_names": list(inspection.table_names),
    }


def format_personal_score_db_schema_diagnostic_markdown(
    diagnostic: dict[str, object],
) -> str:
    required_tables = _diagnostic_mapping(diagnostic["required_tables"])
    metadata_identity = _diagnostic_mapping(diagnostic["metadata_identity"])
    compatibility_errors = _diagnostic_list(diagnostic["compatibility_errors"])
    missing_tables = _diagnostic_list(required_tables["missing"])
    present_tables = _diagnostic_list(required_tables["present"])

    lines = [
        "# Personal Score DB Diagnostic",
        "",
        f"- path: {_diagnostic_code_or_none(diagnostic['path'])}",
        f"- compatible: `{str(diagnostic['is_compatible']).lower()}`",
        f"- migration_plan_status: `{diagnostic['migration_plan_status']}`",
        f"- migration_plan_reason: `{diagnostic['migration_plan_reason']}`",
        (
            "- user_version: "
            f"`{diagnostic['user_version']}` "
            f"(expected `{diagnostic['expected_schema_version']}`)"
        ),
        "",
        "## Compatibility Errors",
    ]
    lines.extend(_markdown_bullets_or_none(compatibility_errors))
    lines.extend(
        [
            "",
            "## Required Tables",
            f"- missing: {_diagnostic_inline_list_or_none(missing_tables)}",
            f"- present: {_diagnostic_inline_list_or_none(present_tables)}",
            "",
            "## Metadata Identity",
            "| key | expected | actual | status |",
            "| --- | --- | --- | --- |",
        ]
    )
    for key in PERSONAL_SCORE_DB_IDENTITY_METADATA_KEYS:
        row = _diagnostic_mapping(metadata_identity[key])
        lines.append(
            "| "
            f"`{key}` | "
            f"`{row['expected']}` | "
            f"{_diagnostic_code_or_none(row['actual'])} | "
            f"`{row['status']}` |"
        )
    if "file_preparation" in diagnostic:
        file_preparation = _diagnostic_mapping(diagnostic["file_preparation"])
        lines.extend(
            [
                "",
                "## File Preparation",
                f"- existed_before: `{str(file_preparation['existed_before']).lower()}`",
                f"- size_before: {_diagnostic_code_or_none(file_preparation['size_before'])}",
                f"- initialized: `{str(file_preparation['initialized']).lower()}`",
                (
                    "- initial_migration_plan_status: "
                    f"`{file_preparation['initial_migration_plan_status']}`"
                ),
                (
                    "- final_migration_plan_status: "
                    f"`{file_preparation['final_migration_plan_status']}`"
                ),
            ]
        )
    lines.append("")
    return "\n".join(lines)


def personal_score_db_file_preparation_diagnostic(
    result: PersonalScoreDbFilePreparationResult,
) -> dict[str, object]:
    diagnostic = personal_score_db_schema_inspection_diagnostic(
        result.inspection,
        path=result.path,
    )
    diagnostic["file_preparation"] = {
        "existed_before": result.existed_before,
        "size_before": result.size_before,
        "initialized": result.initialized,
        "initial_migration_plan_status": (
            result.initialization.before.migration_plan_status
        ),
        "final_migration_plan_status": result.inspection.migration_plan_status,
    }
    return diagnostic


def _metadata_identity_diagnostic_status(
    *,
    actual_value: str | None,
    expected_value: str,
) -> str:
    if actual_value is None:
        return "missing"
    if actual_value != expected_value:
        return "mismatch"
    return "match"


def _diagnostic_mapping(value: object) -> dict[str, object]:
    if not isinstance(value, dict):
        msg = f"diagnostic value must be a mapping: {value!r}"
        raise TypeError(msg)
    return value


def _diagnostic_list(value: object) -> list[object]:
    if not isinstance(value, list):
        msg = f"diagnostic value must be a list: {value!r}"
        raise TypeError(msg)
    return value


def _diagnostic_code_or_none(value: object) -> str:
    if value in ("", None):
        return "`(none)`"
    return f"`{value}`"


def _diagnostic_inline_list_or_none(values: list[object]) -> str:
    if not values:
        return "`(none)`"
    return ", ".join(f"`{value}`" for value in values)


def _markdown_bullets_or_none(values: list[object]) -> list[str]:
    if not values:
        return ["- `(none)`"]
    return [f"- `{value}`" for value in values]


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
            PERSONAL_SCORE_DB_MIGRATION_HISTORY[0][0],
            PERSONAL_SCORE_DB_MIGRATION_HISTORY[0][1],
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
    try:
        return {
            str(key): str(value)
            for key, value in connection.execute(
                "SELECT key, value FROM score_db_metadata ORDER BY key"
            )
        }
    except sqlite3.DatabaseError:
        return {}


def _normalized_table_sql(sql: str) -> str:
    return " ".join(sql.lower().split())


@cache
def _formal_table_sql() -> dict[str, str]:
    with closing(sqlite3.connect(":memory:")) as connection:
        connection.executescript(PERSONAL_SCORE_DB_SCHEMA_SQL)
        return {
            str(name): _normalized_table_sql(str(sql))
            for name, sql in connection.execute(
                "SELECT name, sql FROM sqlite_master "
                "WHERE type = 'table' AND name IN ({})".format(
                    ", ".join("?" for _ in PERSONAL_SCORE_DB_REQUIRED_TABLES)
                ),
                PERSONAL_SCORE_DB_REQUIRED_TABLES,
            )
        }


def _table_sql(connection: sqlite3.Connection, table_name: str) -> str:
    row = connection.execute(
        "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = ?",
        (table_name,),
    ).fetchone()
    return "" if row is None or row[0] is None else _normalized_table_sql(str(row[0]))


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
        elif _table_sql(connection, table_name) != _formal_table_sql()[table_name]:
            errors.append(f"table_schema_mismatch:{table_name}")

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


def initialize_personal_score_db_if_empty(
    connection: sqlite3.Connection,
) -> PersonalScoreDbInitializationResult:
    before = inspect_personal_score_db_schema(connection)
    if before.migration_plan_status != "initialize_empty_database":
        return PersonalScoreDbInitializationResult(
            before=before,
            after=before,
            initialized=False,
        )

    create_personal_score_db_schema(connection)
    after = inspect_personal_score_db_schema(connection)
    return PersonalScoreDbInitializationResult(
        before=before,
        after=after,
        initialized=True,
    )


def prepare_personal_score_db_for_write(
    connection: sqlite3.Connection,
) -> PersonalScoreDbSchemaInspection:
    initialization = initialize_personal_score_db_if_empty(connection)
    inspection = initialization.after
    if inspection.compatibility_errors:
        joined_errors = ", ".join(inspection.compatibility_errors)
        msg = (
            "personal score DB cannot be opened for write: "
            f"{inspection.migration_plan_status}: {joined_errors}"
        )
        raise ValueError(msg)
    return inspection


def prepare_personal_score_db_file_for_write(
    path: Path,
) -> PersonalScoreDbFilePreparationResult:
    db_path = Path(path)
    existed_before = db_path.exists()
    size_before = db_path.stat().st_size if existed_before else None
    if existed_before and db_path.is_dir():
        raise ValueError(f"personal score DB path is a directory: {db_path}")

    db_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        with sqlite3.connect(db_path) as connection:
            initialization = initialize_personal_score_db_if_empty(connection)
            inspection = initialization.after
            if inspection.compatibility_errors:
                joined_errors = ", ".join(inspection.compatibility_errors)
                msg = (
                    "personal score DB file cannot be opened for write: "
                    f"{db_path}: {inspection.migration_plan_status}: {joined_errors}"
                )
                raise ValueError(msg)
    except sqlite3.DatabaseError as exc:
        msg = (
            "personal score DB file cannot be opened for write: "
            f"{db_path}: invalid_sqlite_database"
        )
        raise ValueError(msg) from exc

    return PersonalScoreDbFilePreparationResult(
        path=db_path,
        existed_before=existed_before,
        size_before=size_before,
        initialization=initialization,
        inspection=inspection,
    )
