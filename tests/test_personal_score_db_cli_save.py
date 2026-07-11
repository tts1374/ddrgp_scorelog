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


def assert_validation_has_no_output_side_effects(tmp_path: Path) -> None:
    assert not (tmp_path / "data").exists()
    assert not (tmp_path / "logs").exists()
    assert not (tmp_path / "formal.sqlite").exists()


def test_template_cli_creates_loader_compatible_unresolved_review_input(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.chdir(tmp_path)
    output_path = Path("data/review/save-input-v1.json")

    exit_code = runner.main(
        ["--personal-score-db-save-input-template", str(output_path)]
    )

    assert exit_code == 0
    result = json.loads(capsys.readouterr().out)
    assert result == {
        "template_result_schema_version": 1,
        "output_path": str(output_path),
        "template_schema_version": 1,
        "status": "created",
        "reasons": [],
    }
    assert output_path.read_bytes().startswith(b"{\n")
    assert output_path.read_bytes().endswith(b"\n")
    assert not output_path.read_bytes().startswith(b"\xef\xbb\xbf")
    assert b"\r\n" not in output_path.read_bytes()

    template = json.loads(output_path.read_text(encoding="utf-8"))
    assert list(template) == [
        "input_schema_version",
        "candidate_material",
        "capture_id",
        "capture_hash",
        "captured_at",
        "source_kind",
        "source_path",
        "analysis_id",
        "event_type",
        "confirmed_result",
        "duplicate",
        "confirmation_mode",
        "identity_signal_status",
        "digit_review_status",
        "analysis_confidence",
        "analysis_summary_json",
        "app_version",
        "formal_play",
        "exclusion",
        "manifest_image_path",
        "frame_index",
        "timestamp_ms",
        "candidate_duration_ms",
        "log_path",
    ]
    assert list(template["formal_play"]) == [
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
        "duplicate_key",
    ]
    assert template["candidate_material"] == {}
    assert template["exclusion"] is None
    assert all(
        value in ("", None)
        for value in template["formal_play"].values()
    )
    loaded = cli_save.load_personal_score_db_save_input(output_path)
    adapted = cli_save.adapt_personal_score_db_save_input(loaded)
    assert adapted.status == "unresolved"
    assert adapted.save_input is None
    assert "formal_play.play_id_required" in adapted.reasons
    assert not (tmp_path / "logs").exists()
    assert not (tmp_path / "formal.sqlite").exists()


def test_unedited_template_is_unresolved_through_validation_cli(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.chdir(tmp_path)
    output_path = Path("data/save-input-v1.json")
    assert (
        runner.main(["--personal-score-db-save-input-template", str(output_path)])
        == 0
    )
    capsys.readouterr()

    exit_code = runner.main(
        ["--personal-score-db-save-input-validate", str(output_path)]
    )

    assert exit_code == 1
    result = json.loads(capsys.readouterr().out)
    assert result["adapter_status"] == "unresolved"
    assert result["save_input_constructed"] is False


@pytest.mark.parametrize(
    ("output_name", "expected_error"),
    [
        ("outside.json", "must be under data/"),
        ("data/review.txt", "must end in .json"),
    ],
)
def test_template_cli_rejects_invalid_output_before_side_effects(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
    output_name: str,
    expected_error: str,
) -> None:
    monkeypatch.chdir(tmp_path)
    output_path = Path(output_name)

    exit_code = runner.main(
        ["--personal-score-db-save-input-template", str(output_path)]
    )

    assert exit_code == 2
    result = json.loads(capsys.readouterr().err)
    assert result["status"] == "invalid"
    assert expected_error in result["reasons"][0]
    assert not (tmp_path / "data").exists()
    assert not (tmp_path / "logs").exists()


def test_template_cli_rejects_existing_file_without_overwrite(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.chdir(tmp_path)
    output_path = Path("data/save-input-v1.json")
    output_path.parent.mkdir()
    output_path.write_text("keep\n", encoding="utf-8", newline="\n")

    exit_code = runner.main(
        ["--personal-score-db-save-input-template", str(output_path)]
    )

    assert exit_code == 2
    result = json.loads(capsys.readouterr().err)
    assert result["status"] == "invalid"
    assert "already exists" in result["reasons"][0]
    assert output_path.read_text(encoding="utf-8") == "keep\n"


@pytest.mark.parametrize(
    "mixed_args",
    [
        ["--personal-score-db-save-input-validate", "save-input.json"],
        [
            "--personal-score-db-save-input",
            "save-input.json",
            "--personal-score-db-save-database",
            "formal.sqlite",
        ],
        ["--personal-score-db-diagnostic", "formal.sqlite"],
        ["--output", "data/poc"],
        ["--m8-score-db-output", "data/preview.sqlite"],
    ],
)
def test_template_cli_rejects_option_mixing_before_side_effects(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
    mixed_args: list[str],
) -> None:
    monkeypatch.chdir(tmp_path)
    output_path = Path("data/save-input-v1.json")

    exit_code = runner.main(
        ["--personal-score-db-save-input-template", str(output_path), *mixed_args]
    )

    assert exit_code == 2
    result = json.loads(capsys.readouterr().err)
    assert result["status"] == "invalid"
    assert "cannot be combined" in result["reasons"][0]
    assert not (tmp_path / "data").exists()
    assert not (tmp_path / "logs").exists()
    assert not (tmp_path / "formal.sqlite").exists()


def test_validation_cli_reports_ready_without_database_or_output_side_effects(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    input_path = FIXTURE_PATH.resolve()
    monkeypatch.chdir(tmp_path)

    exit_code = runner.main(
        ["--personal-score-db-save-input-validate", str(input_path)]
    )

    assert exit_code == 0
    result = json.loads(capsys.readouterr().out)
    assert result == {
        "validation_result_schema_version": 1,
        "input_path": str(input_path),
        "adapter_status": "ready",
        "save_input_constructed": True,
        "reasons": [],
    }
    assert_validation_has_no_output_side_effects(tmp_path)


def test_validation_cli_loads_and_adapts_exactly_once(
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    calls = {"load": 0, "adapt": 0}
    original_load = cli_save.load_personal_score_db_save_input
    original_adapt = cli_save.adapt_personal_score_db_save_input

    def counted_load(path: Path) -> object:
        calls["load"] += 1
        return original_load(path)

    def counted_adapt(adapter_input: object) -> object:
        calls["adapt"] += 1
        return original_adapt(adapter_input)

    monkeypatch.setattr(cli_save, "load_personal_score_db_save_input", counted_load)
    monkeypatch.setattr(cli_save, "adapt_personal_score_db_save_input", counted_adapt)

    assert (
        cli_save.run_personal_score_db_save_input_validation_cli(
            input_path=FIXTURE_PATH
        )
        == 0
    )
    capsys.readouterr()
    assert calls == {"load": 1, "adapt": 1}


def test_validation_cli_reports_excluded_without_replaying_formal_values(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    value = fixture_input()
    value["formal_play"] = None
    value["exclusion"] = {"kind": "low_confidence", "reason": "manual_review"}
    input_path = write_input(tmp_path, value)

    exit_code = runner.main(
        ["--personal-score-db-save-input-validate", str(input_path)]
    )

    assert exit_code == 0
    result = json.loads(capsys.readouterr().out)
    assert result["adapter_status"] == "excluded"
    assert result["save_input_constructed"] is True
    assert result["reasons"] == ["manual_review"]
    assert "formal_play" not in result
    assert "candidate_material" not in result


def test_validation_cli_reports_unresolved_with_exit_code_one(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    value = fixture_input()
    value["formal_play"] = None
    input_path = write_input(tmp_path, value)

    exit_code = runner.main(
        ["--personal-score-db-save-input-validate", str(input_path)]
    )

    assert exit_code == 1
    result = json.loads(capsys.readouterr().out)
    assert result["adapter_status"] == "unresolved"
    assert result["save_input_constructed"] is False
    assert result["reasons"] == ["formal_play_required"]
    assert_validation_has_no_output_side_effects(tmp_path)


def test_validation_cli_reuses_strict_loader_for_invalid_json(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    input_path = tmp_path / "invalid.json"
    input_path.write_text('{"input_schema_version": 1,', encoding="utf-8", newline="\n")

    exit_code = runner.main(
        ["--personal-score-db-save-input-validate", str(input_path)]
    )

    assert exit_code == 2
    result = json.loads(capsys.readouterr().err)
    assert result["adapter_status"] == "invalid"
    assert result["save_input_constructed"] is False
    assert "invalid JSON" in result["reasons"][0]
    assert_validation_has_no_output_side_effects(tmp_path)


@pytest.mark.parametrize(
    "mixed_args",
    [
        [
            "--personal-score-db-save-input",
            "save-input.json",
            "--personal-score-db-save-database",
            "formal.sqlite",
        ],
        ["--personal-score-db-save-database", "formal.sqlite"],
        ["--personal-score-db-diagnostic", "formal.sqlite"],
        ["--output", "data/poc"],
        ["--m8-score-db-output", "data/preview.sqlite"],
    ],
)
def test_validation_cli_rejects_option_mixing_before_side_effects(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
    mixed_args: list[str],
) -> None:
    input_path = FIXTURE_PATH.resolve()
    monkeypatch.chdir(tmp_path)

    exit_code = runner.main(
        ["--personal-score-db-save-input-validate", str(input_path), *mixed_args]
    )

    assert exit_code == 2
    result = json.loads(capsys.readouterr().err)
    assert result["adapter_status"] == "invalid"
    assert "cannot be combined" in result["reasons"][0]
    assert_validation_has_no_output_side_effects(tmp_path)


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


def test_cli_records_duplicate_key_collision_as_excluded(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    db_path = tmp_path / "formal.sqlite"
    first = fixture_input()
    collision = fixture_input()
    collision["capture_id"] = "capture-cli-collision"
    collision["capture_hash"] = "sha256:cli-collision"
    collision["source_path"] = "fixtures/cli-collision.png"
    collision["analysis_id"] = "analysis-cli-collision"
    collision["formal_play"]["play_id"] = "play-cli-collision"

    assert (
        runner.main(
            [
                "--personal-score-db-save-input",
                str(write_input(tmp_path, first)),
                "--personal-score-db-save-database",
                str(db_path),
            ]
        )
        == 0
    )
    capsys.readouterr()
    exit_code = runner.main(
        [
            "--personal-score-db-save-input",
            str(write_input(tmp_path, collision)),
            "--personal-score-db-save-database",
            str(db_path),
        ]
    )

    assert exit_code == 0
    result = json.loads(capsys.readouterr().out)
    assert result == {
        "result_schema_version": 1,
        "db_path": str(db_path),
        "adapter_status": "excluded",
        "written": True,
        "play_id": None,
        "source_capture_id": "capture-cli-collision",
        "analysis_id": "analysis-cli-collision",
        "reasons": ["duplicate_key_already_saved"],
    }
    assert row_counts(db_path) == (2, 1, 2)


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
