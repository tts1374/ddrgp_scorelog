from __future__ import annotations

import json
import sqlite3
from pathlib import Path

import pytest

from tools.vision_poc import personal_score_db_cli_save as cli_save
from tools.vision_poc import personal_score_db_schema as score_schema
from tools.vision_poc import runner

FIXTURE_PATH = Path("tests/fixtures/personal_score_db_cli/ready-v1.json")


def fixture_input() -> dict[str, object]:
    return json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))


def write_input(tmp_path: Path, value: object) -> Path:
    path = tmp_path / "save-input.json"
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2),
        encoding="utf-8",
        newline="\n",
    )
    return path


def row_counts(db_path: Path) -> tuple[int, int, int]:
    with sqlite3.connect(db_path) as connection:
        return tuple(
            int(connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0])
            for table in ("source_captures", "plays", "analysis_logs")
        )


@pytest.mark.parametrize("existing_empty_file", [False, True])
def test_cli_saves_ready_input_to_new_or_zero_byte_database(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    existing_empty_file: bool,
) -> None:
    db_path = tmp_path / "formal.sqlite"
    if existing_empty_file:
        db_path.touch()

    exit_code = runner.main(
        [
            "--personal-score-db-save-input",
            str(FIXTURE_PATH),
            "--personal-score-db-save-database",
            str(db_path),
        ]
    )

    assert exit_code == 0
    result = json.loads(capsys.readouterr().out)
    assert result == {
        "result_schema_version": 1,
        "db_path": str(db_path),
        "adapter_status": "ready",
        "written": True,
        "play_id": "play-cli-ready",
        "source_capture_id": "capture-cli-ready",
        "analysis_id": "analysis-cli-ready",
        "reasons": [],
    }
    assert row_counts(db_path) == (1, 1, 1)


def test_cli_appends_to_compatible_database(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    db_path = tmp_path / "formal.sqlite"
    with sqlite3.connect(db_path) as connection:
        score_schema.create_personal_score_db_schema(connection)

    assert (
        runner.main(
            [
                "--personal-score-db-save-input",
                str(FIXTURE_PATH),
                "--personal-score-db-save-database",
                str(db_path),
            ]
        )
        == 0
    )
    capsys.readouterr()
    assert row_counts(db_path) == (1, 1, 1)


@pytest.mark.parametrize("kind", ["duplicate", "low_confidence", "skipped", "error"])
def test_cli_saves_excluded_analysis_without_play(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    kind: str,
) -> None:
    value = fixture_input()
    value["formal_play"] = None
    value["exclusion"] = {"kind": kind, "reason": f"fixture_{kind}"}
    value["duplicate"] = kind == "duplicate"
    db_path = tmp_path / "formal.sqlite"

    exit_code = runner.main(
        [
            "--personal-score-db-save-input",
            str(write_input(tmp_path, value)),
            "--personal-score-db-save-database",
            str(db_path),
        ]
    )

    assert exit_code == 0
    result = json.loads(capsys.readouterr().out)
    assert result["adapter_status"] == "excluded"
    assert result["written"] is True
    assert result["play_id"] is None
    assert row_counts(db_path) == (1, 0, 1)


def test_cli_returns_unresolved_before_creating_database_or_parent(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    value = fixture_input()
    value["formal_play"] = None
    db_path = tmp_path / "missing" / "formal.sqlite"

    exit_code = runner.main(
        [
            "--personal-score-db-save-input",
            str(write_input(tmp_path, value)),
            "--personal-score-db-save-database",
            str(db_path),
        ]
    )

    assert exit_code == 1
    result = json.loads(capsys.readouterr().out)
    assert result["adapter_status"] == "unresolved"
    assert result["written"] is False
    assert result["reasons"] == ["formal_play_required"]
    assert not db_path.parent.exists()


@pytest.mark.parametrize(
    ("mutate", "expected_error"),
    [
        (lambda value: value.pop("capture_id"), "missing required key(s): capture_id"),
        (lambda value: value.update({"unknown": "value"}), "unknown key(s): unknown"),
        (lambda value: value.update({"confirmed_result": 1}), "must be a boolean"),
        (lambda value: value.update({"timestamp_ms": True}), "must be an integer"),
        (lambda value: value.update({"formal_play": []}), "formal_play must be an object"),
        (
            lambda value: value["formal_play"].update({"score": True}),
            "formal_play.score must be an integer",
        ),
        (
            lambda value: value["formal_play"].update({"recognized_digits": "987650"}),
            "formal_play has unknown key(s): recognized_digits",
        ),
    ],
)
def test_cli_rejects_invalid_json_schema_before_database_creation(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    mutate: object,
    expected_error: str,
) -> None:
    value = fixture_input()
    mutate(value)
    db_path = tmp_path / "missing" / "formal.sqlite"

    exit_code = runner.main(
        [
            "--personal-score-db-save-input",
            str(write_input(tmp_path, value)),
            "--personal-score-db-save-database",
            str(db_path),
        ]
    )

    assert exit_code == 2
    error = json.loads(capsys.readouterr().err)
    assert expected_error in error["reasons"][0]
    assert error["written"] is False
    assert not db_path.parent.exists()


def test_loader_does_not_promote_candidate_values_into_formal_play(tmp_path: Path) -> None:
    value = fixture_input()
    value["formal_play"]["played_at"] = ""
    value["formal_play"]["song_id"] = ""
    value["formal_play"]["score"] = None
    loaded = cli_save.load_personal_score_db_save_input(write_input(tmp_path, value))

    result = cli_save.save_personal_score_db_file(tmp_path / "formal.sqlite", loaded)

    assert result.adapter_status == "unresolved"
    assert "formal_play.played_at_required" in result.reasons
    assert "formal_play.song_id_required" in result.reasons
    assert "formal_play.score_required" in result.reasons
    assert not result.db_path.exists()


@pytest.mark.parametrize(
    "rejection_kind",
    [
        "preview",
        "unknown",
        "identity_mismatch",
        "manual_migration",
        "non_sqlite",
        "directory",
    ],
)
def test_cli_rejects_incompatible_database(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    rejection_kind: str,
) -> None:
    db_path = tmp_path / "rejected.sqlite"
    if rejection_kind == "preview":
        with sqlite3.connect(db_path) as connection:
            connection.execute("CREATE TABLE preview_metadata (key TEXT, value TEXT)")
    elif rejection_kind == "unknown":
        with sqlite3.connect(db_path) as connection:
            connection.execute("CREATE TABLE unknown_table (value TEXT)")
    elif rejection_kind == "identity_mismatch":
        with sqlite3.connect(db_path) as connection:
            score_schema.create_personal_score_db_schema(connection)
            connection.execute(
                "UPDATE score_db_metadata SET value = ? WHERE key = ?",
                ("other_schema", "schema_name"),
            )
    elif rejection_kind == "manual_migration":
        with sqlite3.connect(db_path) as connection:
            score_schema.create_personal_score_db_schema(connection)
            connection.execute("DROP TABLE analysis_logs")
    elif rejection_kind == "non_sqlite":
        db_path.write_text("not sqlite", encoding="utf-8", newline="\n")
    elif rejection_kind == "directory":
        db_path.mkdir()
    before = db_path.read_bytes() if db_path.is_file() else None

    exit_code = runner.main(
        [
            "--personal-score-db-save-input",
            str(FIXTURE_PATH),
            "--personal-score-db-save-database",
            str(db_path),
        ]
    )

    assert exit_code == 2
    error = json.loads(capsys.readouterr().err)
    assert error["adapter_status"] == "invalid"
    if before is not None:
        assert db_path.read_bytes() == before


@pytest.mark.parametrize("missing_option", ["input", "database"])
def test_cli_requires_both_explicit_paths_before_database_creation(
    tmp_path: Path,
    missing_option: str,
) -> None:
    db_path = tmp_path / "missing" / "formal.sqlite"
    args = (
        ["--personal-score-db-save-database", str(db_path)]
        if missing_option == "input"
        else ["--personal-score-db-save-input", str(FIXTURE_PATH)]
    )

    with pytest.raises(ValueError, match="must be specified together"):
        runner.main(args)

    assert not db_path.parent.exists()
