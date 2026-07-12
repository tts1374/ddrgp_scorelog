from __future__ import annotations

import hashlib
import json
import sqlite3
from pathlib import Path

import pytest

from tools.vision_poc import personal_score_db_migration_contract as contract
from tools.vision_poc import personal_score_db_schema as schema
from tools.vision_poc import runner
from tools.vision_poc.personal_score_db_migration_status import (
    project_personal_score_db_migration_status,
)


def _create_formal_db(path: Path) -> None:
    with sqlite3.connect(path) as connection:
        schema.create_personal_score_db_schema(connection)


def _digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def test_status_cli_reports_current_database_without_side_effects(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    db_path = tmp_path / "scores.sqlite"
    backup_path = tmp_path / "scores.backup.sqlite"
    _create_formal_db(db_path)
    before = _digest(db_path)

    exit_code = runner.main(
        [
            "--personal-score-db-migration-status",
            str(db_path),
            "--personal-score-db-migration-target-version",
            "1",
            "--personal-score-db-migration-backup",
            str(backup_path),
            "--personal-score-db-migration-format",
            "json",
        ]
    )

    result = json.loads(capsys.readouterr().out)
    assert exit_code == 0
    assert result["status"] == "current"
    assert result["reason"] == "already_at_target_version"
    assert result["source_version"] == result["target_version"] == 1
    assert result["backup_path_inspection"] == {
        "is_safe": True,
        "is_new": True,
        "parent_exists": True,
        "reason": "backup_path_available",
    }
    assert result["planned_steps"] == []
    assert _digest(db_path) == before
    assert not backup_path.exists()


@pytest.mark.parametrize(
    ("kind", "expected_state", "expected_reason"),
    [
        ("missing", "unknown", "unknown_database_not_supported"),
        ("non-sqlite", "unknown", "unknown_database_not_supported"),
        ("directory", "unknown", "unknown_database_not_supported"),
        ("preview", "preview", "preview_database_not_supported"),
        ("identity", "identity_mismatch", "formal_database_identity_mismatch"),
        ("partial", "partial_migration", "partial_migration_state_requires_manual_recovery"),
        ("newer", "newer_unsupported", "newer_schema_not_supported"),
    ],
)
def test_projection_fixes_rejected_database_states(
    tmp_path: Path, kind: str, expected_state: str, expected_reason: str
) -> None:
    db_path = tmp_path / "candidate.sqlite"
    if kind == "non-sqlite":
        db_path.write_text("not sqlite", encoding="utf-8", newline="\n")
    elif kind == "directory":
        db_path.mkdir()
    elif kind == "preview":
        with sqlite3.connect(db_path) as connection:
            connection.execute("CREATE TABLE preview_metadata (key TEXT, value TEXT)")
            connection.execute("PRAGMA user_version = 1")
    elif kind in {"identity", "partial", "newer"}:
        _create_formal_db(db_path)
        with sqlite3.connect(db_path) as connection:
            if kind == "identity":
                connection.execute(
                    "UPDATE score_db_metadata SET value = 'other' WHERE key = 'schema_name'"
                )
            elif kind == "partial":
                connection.execute("PRAGMA user_version = 2")
            else:
                connection.execute("PRAGMA user_version = 2")
                connection.execute(
                    "UPDATE score_db_metadata SET value = '2' WHERE key = 'schema_version'"
                )
                connection.execute(
                    "INSERT INTO schema_migrations "
                    "(migration_id, schema_version, app_version, notes) "
                    "VALUES ('002_fixture', 2, 'test', 'test')"
                )
            connection.commit()

    result = project_personal_score_db_migration_status(
        db_path,
        target_version=1,
        backup_path=tmp_path / "backup.sqlite",
        dry_run=False,
    )

    assert result["database_state"] == expected_state
    assert result["status"] == "rejected"
    assert result["reason"] == expected_reason
    assert result["exit_code"] == 2


def test_future_supported_dry_run_displays_steps_from_same_contract(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    db_path = tmp_path / "scores.sqlite"
    _create_formal_db(db_path)
    monkeypatch.setattr(contract, "CURRENT_SCHEMA_VERSION", 2)

    result = project_personal_score_db_migration_status(
        db_path,
        target_version=2,
        backup_path=tmp_path / "backup.sqlite",
        dry_run=True,
    )

    assert result["database_state"] == "older_supported"
    assert result["status"] == "dry_run_ready"
    assert result["planned_steps"] == list(contract.MIGRATION_EXECUTION_STEPS)
    assert result["may_create_backup"] is False
    assert result["may_modify_source"] is False


@pytest.mark.parametrize(
    "argv",
    [
        ["--personal-score-db-migration-status", "scores.sqlite"],
        ["--personal-score-db-migration-target-version", "1"],
        ["--personal-score-db-migration-backup", "backup.sqlite"],
        [
            "--personal-score-db-migration-status",
            "scores.sqlite",
            "--personal-score-db-migration-target-version",
            "1",
            "--personal-score-db-migration-backup",
            "backup.sqlite",
            "--personal-score-db-diagnostic",
            "scores.sqlite",
        ],
    ],
)
def test_migration_cli_options_are_exclusive(argv: list[str]) -> None:
    with pytest.raises(ValueError):
        runner.main(argv)
