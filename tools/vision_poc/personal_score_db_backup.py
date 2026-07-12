from __future__ import annotations

import hashlib
import json
import os
import sqlite3
from contextlib import closing
from dataclasses import dataclass
from pathlib import Path

from . import personal_score_db_schema as score_schema


@dataclass(frozen=True)
class VerifiedPersonalScoreDbBackup:
    source_path: Path
    backup_path: Path
    schema_version: int
    metadata: dict[str, str]
    migration_history: tuple[tuple[str, int], ...]
    table_row_counts: dict[str, int]
    table_content_hashes: dict[str, str]


class PersonalScoreDbBackupError(RuntimeError):
    def __init__(self, reason: str, detail: str = "") -> None:
        super().__init__(f"{reason}: {detail}" if detail else reason)
        self.reason = reason


def create_verified_personal_score_db_backup(
    source_path: Path, backup_path: Path
) -> VerifiedPersonalScoreDbBackup:
    source_path = Path(source_path)
    backup_path = Path(backup_path)
    if source_path.resolve() == backup_path.resolve():
        raise PersonalScoreDbBackupError("backup_path_matches_source")
    if not backup_path.parent.is_dir():
        raise PersonalScoreDbBackupError("backup_parent_does_not_exist")

    created = False
    try:
        source_uri = f"{source_path.resolve().as_uri()}?mode=ro"
        with closing(sqlite3.connect(source_uri, uri=True)) as source:
            inspection = score_schema.inspect_personal_score_db_schema(source)
            if not inspection.is_compatible:
                reason = inspection.migration_plan_reason
                raise PersonalScoreDbBackupError("source_not_compatible", reason)
            source_snapshot = _read_snapshot(source)
            _assert_version_history(source_snapshot, "source_not_compatible")
            _exclusive_create(backup_path)
            created = True
            _copy_database(source, backup_path)

        _flush_file(backup_path)
        backup_snapshot = _read_verified_backup(backup_path)
        if backup_snapshot != source_snapshot:
            raise PersonalScoreDbBackupError("backup_snapshot_mismatch")
        return VerifiedPersonalScoreDbBackup(
            source_path=source_path,
            backup_path=backup_path,
            schema_version=int(source_snapshot["schema_version"]),
            metadata=dict(source_snapshot["metadata"]),
            migration_history=tuple(source_snapshot["migration_history"]),
            table_row_counts=dict(source_snapshot["table_row_counts"]),
            table_content_hashes=dict(source_snapshot["table_content_hashes"]),
        )
    except FileExistsError as exc:
        raise PersonalScoreDbBackupError("backup_path_already_exists") from exc
    except PersonalScoreDbBackupError:
        if created:
            backup_path.unlink(missing_ok=True)
        raise
    except (OSError, sqlite3.Error) as exc:
        if created:
            backup_path.unlink(missing_ok=True)
        raise PersonalScoreDbBackupError("backup_copy_or_readback_failed", str(exc)) from exc


def _exclusive_create(path: Path) -> None:
    descriptor = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    os.close(descriptor)


def _copy_database(source: sqlite3.Connection, backup_path: Path) -> None:
    with closing(sqlite3.connect(backup_path)) as destination:
        source.backup(destination)


def _flush_file(path: Path) -> None:
    descriptor = os.open(path, os.O_RDWR)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def _read_verified_backup(path: Path) -> dict[str, object]:
    uri = f"{path.resolve().as_uri()}?mode=ro"
    with closing(sqlite3.connect(uri, uri=True)) as connection:
        integrity = connection.execute("PRAGMA integrity_check").fetchall()
        if integrity != [("ok",)]:
            raise PersonalScoreDbBackupError("backup_integrity_check_failed")
        inspection = score_schema.inspect_personal_score_db_schema(connection)
        if not inspection.is_compatible:
            raise PersonalScoreDbBackupError(
                "backup_formal_contract_mismatch", inspection.migration_plan_reason
            )
        snapshot = _read_snapshot(connection)
        _assert_version_history(snapshot, "backup_formal_contract_mismatch")
        return snapshot


def _assert_version_history(snapshot: dict[str, object], reason: str) -> None:
    schema_version = snapshot["schema_version"]
    metadata = snapshot["metadata"]
    history = snapshot["migration_history"]
    if not isinstance(schema_version, int) or not isinstance(metadata, dict):
        raise PersonalScoreDbBackupError(reason, "formal_version_history_mismatch")
    try:
        metadata_version = int(metadata.get("schema_version", ""))
        history_versions = tuple(int(row[1]) for row in history)
    except (TypeError, ValueError):
        raise PersonalScoreDbBackupError(
            reason, "formal_version_history_mismatch"
        ) from None
    if (
        metadata_version != schema_version
        or history_versions != tuple(range(1, schema_version + 1))
    ):
        raise PersonalScoreDbBackupError(reason, "formal_version_history_mismatch")


def _read_snapshot(connection: sqlite3.Connection) -> dict[str, object]:
    inspection = score_schema.inspect_personal_score_db_schema(connection)
    history = tuple(
        (str(row[0]), int(row[1]))
        for row in connection.execute(
            "SELECT migration_id, schema_version FROM schema_migrations "
            "ORDER BY schema_version, migration_id"
        )
    )
    counts = {
        table: int(connection.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0])
        for table in score_schema.PERSONAL_SCORE_DB_REQUIRED_TABLES
    }
    content_hashes = {
        table: hashlib.sha256(
            json.dumps(
                connection.execute(f'SELECT * FROM "{table}" ORDER BY rowid').fetchall(),
                ensure_ascii=False,
                separators=(",", ":"),
            ).encode("utf-8")
        ).hexdigest()
        for table in score_schema.PERSONAL_SCORE_DB_REQUIRED_TABLES
    }
    return {
        "schema_version": inspection.user_version,
        "metadata": dict(sorted(inspection.metadata.items())),
        "migration_history": history,
        "table_row_counts": counts,
        "table_content_hashes": content_hashes,
    }
