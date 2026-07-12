from __future__ import annotations

import hashlib
import sqlite3
from pathlib import Path

import pytest

from tools.vision_poc import personal_score_db_backup as backup
from tools.vision_poc import personal_score_db_schema as schema
from tools.vision_poc import runner


def _formal_db(path: Path) -> None:
    with sqlite3.connect(path) as connection:
        schema.create_personal_score_db_schema(connection)


def _digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def test_verified_backup_matches_formal_source_snapshot(tmp_path: Path) -> None:
    source = tmp_path / "scores.sqlite"
    target = tmp_path / "scores.backup.sqlite"
    _formal_db(source)
    before = _digest(source)

    result = backup.create_verified_personal_score_db_backup(source, target)

    assert result.schema_version == 1
    assert result.metadata == schema.PERSONAL_SCORE_DB_METADATA
    assert result.migration_history == (("001_initial_personal_score_db_schema", 1),)
    assert target.exists()
    assert _digest(source) == before
    with sqlite3.connect(target) as connection:
        assert connection.execute("PRAGMA integrity_check").fetchone() == ("ok",)


@pytest.mark.parametrize(
    "kind",
    ["preview", "unknown", "identity", "partial", "history", "migration-id"],
)
def test_rejected_source_does_not_create_backup(tmp_path: Path, kind: str) -> None:
    source = tmp_path / "source.sqlite"
    with sqlite3.connect(source) as connection:
        if kind == "preview":
            connection.execute("CREATE TABLE preview_metadata (key TEXT, value TEXT)")
            connection.execute("PRAGMA user_version = 1")
        elif kind == "unknown":
            connection.execute("CREATE TABLE unknown_table (id INTEGER)")
        else:
            schema.create_personal_score_db_schema(connection)
            if kind == "identity":
                connection.execute(
                    "UPDATE score_db_metadata SET value='other' WHERE key='schema_name'"
                )
            elif kind == "partial":
                connection.execute("PRAGMA user_version = 2")
            elif kind == "history":
                connection.execute("DELETE FROM schema_migrations")
            else:
                connection.execute(
                    "UPDATE schema_migrations SET migration_id = '001_tampered'"
                )
    target = tmp_path / "backup.sqlite"

    with pytest.raises(backup.PersonalScoreDbBackupError) as error:
        backup.create_verified_personal_score_db_backup(source, target)

    assert error.value.reason == "source_not_compatible"
    assert not target.exists()


def test_existing_backup_is_not_changed(tmp_path: Path) -> None:
    source = tmp_path / "source.sqlite"
    target = tmp_path / "backup.sqlite"
    _formal_db(source)
    target.write_bytes(b"keep")

    with pytest.raises(backup.PersonalScoreDbBackupError) as error:
        backup.create_verified_personal_score_db_backup(source, target)

    assert error.value.reason == "backup_path_already_exists"
    assert target.read_bytes() == b"keep"


def test_backup_cli_requires_pair_and_reports_verified(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    source = tmp_path / "source.sqlite"
    target = tmp_path / "backup.sqlite"
    _formal_db(source)

    exit_code = runner.main(
        [
            "--personal-score-db-backup-source",
            str(source),
            "--personal-score-db-backup-output",
            str(target),
            "--personal-score-db-backup-format",
            "json",
        ]
    )

    assert exit_code == 0
    assert '"status": "verified"' in capsys.readouterr().out
    with pytest.raises(ValueError):
        runner.main(["--personal-score-db-backup-source", str(source)])


def test_explicit_default_backup_format_still_requires_paths(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)

    with pytest.raises(ValueError):
        runner.main(["--personal-score-db-backup-format", "markdown"])

    assert not (tmp_path / "data").exists()


def test_backup_cli_is_exclusive(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        runner.main(
            [
                "--personal-score-db-backup-source",
                str(tmp_path / "source.sqlite"),
                "--personal-score-db-backup-output",
                str(tmp_path / "backup.sqlite"),
                "--personal-score-db-diagnostic",
                str(tmp_path / "source.sqlite"),
            ]
        )


@pytest.mark.parametrize("failure", ["copy", "readback"])
def test_failure_removes_only_incomplete_new_backup(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, failure: str
) -> None:
    source = tmp_path / "source.sqlite"
    target = tmp_path / "backup.sqlite"
    _formal_db(source)
    before = _digest(source)
    function = "_copy_database" if failure == "copy" else "_read_verified_backup"
    monkeypatch.setattr(
        backup, function, lambda *_args: (_ for _ in ()).throw(sqlite3.OperationalError("boom"))
    )

    with pytest.raises(backup.PersonalScoreDbBackupError) as error:
        backup.create_verified_personal_score_db_backup(source, target)

    assert error.value.reason == "backup_copy_or_readback_failed"
    assert not target.exists()
    assert _digest(source) == before


def test_backup_uses_same_snapshot_when_source_changes_concurrently(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    source = tmp_path / "source.sqlite"
    target = tmp_path / "backup.sqlite"
    _formal_db(source)
    with sqlite3.connect(source) as connection:
        connection.execute("PRAGMA journal_mode = WAL")

    original_copy = backup._copy_database

    def copy_after_concurrent_write(
        source_connection: sqlite3.Connection, backup_path: Path
    ) -> None:
        with sqlite3.connect(source) as writer:
            writer.execute(
                "INSERT INTO source_captures "
                "(capture_id, capture_hash, captured_at, source_kind, source_path) "
                "VALUES ('capture-concurrent', 'hash-concurrent', "
                "'2026-07-12T12:00:00+00:00', 'manual', 'concurrent.png')"
            )
        original_copy(source_connection, backup_path)

    monkeypatch.setattr(backup, "_copy_database", copy_after_concurrent_write)

    result = backup.create_verified_personal_score_db_backup(source, target)

    assert result.table_row_counts["source_captures"] == 0
    with sqlite3.connect(target) as connection:
        assert connection.execute("SELECT COUNT(*) FROM source_captures").fetchone() == (0,)
    with sqlite3.connect(source) as connection:
        assert connection.execute("SELECT COUNT(*) FROM source_captures").fetchone() == (1,)
