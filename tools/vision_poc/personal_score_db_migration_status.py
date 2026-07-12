from __future__ import annotations

import sqlite3
from pathlib import Path

from . import personal_score_db_migration_contract as migration_contract
from . import personal_score_db_schema as score_schema

MIGRATION_STATUS_PROJECTION_VERSION = 1


def project_personal_score_db_migration_status(
    db_path: Path,
    *,
    target_version: int,
    backup_path: Path,
    dry_run: bool,
) -> dict[str, object]:
    database_state, source_version, inspection_reason = _inspect_database_state(db_path)
    backup_inspection = _inspect_backup_path(db_path, backup_path)
    plan = migration_contract.plan_personal_score_db_migration(
        migration_contract.MigrationRequest(
            database_state=database_state,
            source_version=source_version,
            target_version=target_version,
            dry_run=dry_run,
            explicit_confirmation=False,
            backup_path_is_safe=bool(backup_inspection["is_safe"]),
            backup_path_is_new=bool(backup_inspection["is_new"]),
        )
    )
    return {
        "projection_version": MIGRATION_STATUS_PROJECTION_VERSION,
        "mode": "dry-run" if dry_run else "status",
        "db_path": str(db_path),
        "backup_path": str(backup_path),
        "database_state": database_state,
        "inspection_reason": inspection_reason,
        "source_version": source_version,
        "target_version": target_version,
        "backup_path_inspection": backup_inspection,
        "status": plan.status,
        "reason": plan.reason,
        "planned_steps": list(plan.steps),
        "may_create_backup": plan.may_create_backup,
        "may_modify_source": plan.may_modify_source,
        "exit_code": plan.exit_code,
    }


def format_personal_score_db_migration_status_markdown(
    projection: dict[str, object],
) -> str:
    backup = projection["backup_path_inspection"]
    if not isinstance(backup, dict):
        raise TypeError("backup_path_inspection must be a mapping")
    steps = projection["planned_steps"]
    if not isinstance(steps, list):
        raise TypeError("planned_steps must be a list")
    lines = [
        "# Personal Score DB Migration Status",
        "",
        f"- mode: `{projection['mode']}`",
        f"- db_path: `{projection['db_path']}`",
        f"- database_state: `{projection['database_state']}`",
        f"- inspection_reason: `{projection['inspection_reason']}`",
        f"- source_version: `{projection['source_version']}`",
        f"- target_version: `{projection['target_version']}`",
        f"- status: `{projection['status']}`",
        f"- reason: `{projection['reason']}`",
        f"- exit_code: `{projection['exit_code']}`",
        "",
        "## Backup Path Inspection",
        f"- path: `{projection['backup_path']}`",
        f"- is_safe: `{str(backup['is_safe']).lower()}`",
        f"- is_new: `{str(backup['is_new']).lower()}`",
        f"- parent_exists: `{str(backup['parent_exists']).lower()}`",
        f"- reason: `{backup['reason']}`",
        "",
        "## Planned Steps",
    ]
    lines.extend(f"- `{step}`" for step in steps)
    if not steps:
        lines.append("- `(none)`")
    lines.append("")
    return "\n".join(lines)


def _inspect_database_state(path: Path) -> tuple[str, int | None, str]:
    if path.is_dir():
        return "unknown", None, "path_is_directory"
    if not path.exists():
        return "unknown", None, "path_does_not_exist"
    try:
        uri = f"{path.resolve().as_uri()}?mode=ro"
        with sqlite3.connect(uri, uri=True) as connection:
            inspection = score_schema.inspect_personal_score_db_schema(connection)
            return _classify_inspection(connection, inspection)
    except sqlite3.DatabaseError:
        return "unknown", None, "invalid_sqlite_database"


def _classify_inspection(
    connection: sqlite3.Connection,
    inspection: score_schema.PersonalScoreDbSchemaInspection,
) -> tuple[str, int | None, str]:
    errors = inspection.compatibility_errors
    if "m8_preview_database_not_supported" in errors:
        return "preview", inspection.user_version, "preview_database_not_supported"
    if _identity_mismatch(errors):
        return "identity_mismatch", inspection.user_version, "formal_database_identity_mismatch"
    if not inspection.metadata:
        return "unknown", None, "unknown_database_not_supported"

    metadata_version = _parse_positive_version(inspection.metadata.get("schema_version"))
    history_versions = _read_history_versions(connection, inspection.table_names)
    source_version = inspection.user_version
    if (
        metadata_version is None
        or history_versions is None
        or not history_versions
        or history_versions != tuple(range(1, source_version + 1))
        or metadata_version != source_version
    ):
        return "partial_migration", source_version, "formal_version_history_mismatch"
    if source_version > migration_contract.CURRENT_SCHEMA_VERSION:
        return "newer_unsupported", source_version, "newer_schema_not_supported"
    if source_version == migration_contract.CURRENT_SCHEMA_VERSION and not errors:
        return "compatible_current", source_version, "schema_compatible"
    if source_version < migration_contract.CURRENT_SCHEMA_VERSION:
        return "older_supported", source_version, "older_formal_schema"
    return "partial_migration", source_version, "formal_schema_contract_mismatch"


def _identity_mismatch(errors: tuple[str, ...]) -> bool:
    return any(
        error.startswith(f"score_db_metadata.{key}_")
        for error in errors
        for key in score_schema.PERSONAL_SCORE_DB_IDENTITY_METADATA_KEYS
        if key != "schema_version"
    )


def _parse_positive_version(value: str | None) -> int | None:
    try:
        version = int(value or "")
    except ValueError:
        return None
    return version if version >= 1 else None


def _read_history_versions(
    connection: sqlite3.Connection,
    table_names: tuple[str, ...],
) -> tuple[int, ...] | None:
    if score_schema.PERSONAL_SCORE_DB_MIGRATIONS_TABLE not in table_names:
        return None
    try:
        return tuple(
            int(row[0])
            for row in connection.execute(
                "SELECT schema_version FROM schema_migrations ORDER BY schema_version"
            )
        )
    except (sqlite3.DatabaseError, TypeError, ValueError):
        return None


def _inspect_backup_path(db_path: Path, backup_path: Path) -> dict[str, object]:
    source = db_path.resolve()
    backup = backup_path.resolve()
    is_new = not backup_path.exists()
    parent_exists = backup_path.parent.exists() and backup_path.parent.is_dir()
    is_safe = source != backup and parent_exists and is_new
    if source == backup:
        reason = "backup_path_matches_source"
    elif not is_new:
        reason = "backup_path_already_exists"
    elif not parent_exists:
        reason = "backup_parent_does_not_exist"
    else:
        reason = "backup_path_available"
    return {
        "is_safe": is_safe,
        "is_new": is_new,
        "parent_exists": parent_exists,
        "reason": reason,
    }
