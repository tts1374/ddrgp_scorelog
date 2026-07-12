from __future__ import annotations

import copy
import json
import sqlite3
from pathlib import Path

import pytest

from tools.vision_poc import personal_score_db_workflow as workflow

ROOT = Path(__file__).parent
READY = ROOT / "fixtures/personal_score_db_cli/ready-v1.json"
DETAIL = ROOT / "fixtures/personal_score_db_analysis_artifacts/low-confidence-v1.json"


def _ready() -> dict[str, object]:
    return json.loads(READY.read_text(encoding="utf-8"))


def _detail() -> dict[str, object]:
    return json.loads(DETAIL.read_text(encoding="utf-8"))


def _workflow(
    save: dict[str, object], detail: dict[str, object] | None = None
) -> workflow.PersonalScoreDbWorkflowInput:
    return workflow.PersonalScoreDbWorkflowInput(detail, save)


def test_ready_without_artifact_saves_once(tmp_path: Path) -> None:
    result = workflow.run_personal_score_db_workflow(
        _workflow(_ready()),
        artifact_output=None,
        db_path=tmp_path / "score.sqlite",
        repository_root=tmp_path,
    )
    assert (result.workflow_status, result.artifact_status, result.play_id) == (
        "saved",
        "not_requested",
        "play-cli-ready",
    )


def test_ready_with_artifact_is_supported(tmp_path: Path) -> None:
    detail = _detail()
    save = _ready()
    output = "logs/analysis_details/ready.json"
    detail.update(
        analysis_id=save["analysis_id"],
        source_capture_id=save["capture_id"],
        analysis_status="saved",
        save_boundary_status="save_ready",
        skip_reason="",
    )
    save["log_path"] = output

    result = workflow.run_personal_score_db_workflow(
        _workflow(save, detail),
        artifact_output=output,
        db_path=tmp_path / "score.sqlite",
        repository_root=tmp_path,
    )

    assert (result.workflow_status, result.artifact_status) == ("saved", "created")


def test_required_artifact_is_created_before_excluded_save(tmp_path: Path) -> None:
    detail = _detail()
    save = _ready()
    output = "logs/analysis_details/low.json"
    save.update(
        analysis_id=detail["analysis_id"],
        capture_id=detail["source_capture_id"],
        formal_play=None,
        exclusion={"kind": "low_confidence", "reason": detail["skip_reason"]},
        log_path=output,
    )
    result = workflow.run_personal_score_db_workflow(
        _workflow(save, detail),
        artifact_output=output,
        db_path=tmp_path / "score.sqlite",
        repository_root=tmp_path,
    )
    assert (result.workflow_status, result.artifact_status, result.play_id) == (
        "excluded",
        "created",
        None,
    )
    assert (tmp_path / output).is_file()


def test_shared_value_mismatch_has_no_side_effect(tmp_path: Path) -> None:
    detail = _detail()
    save = _ready()
    save.update(
        formal_play=None,
        exclusion={"kind": "low_confidence", "reason": "x"},
        log_path="logs/analysis_details/a.json",
    )
    result = workflow.run_personal_score_db_workflow(
        _workflow(save, detail),
        artifact_output="logs/analysis_details/a.json",
        db_path=tmp_path / "db/score.sqlite",
        repository_root=tmp_path,
    )
    assert result.workflow_status == "invalid"
    assert not (tmp_path / "logs").exists()
    assert not (tmp_path / "db").exists()


def test_incompatible_db_rejects_before_artifact(tmp_path: Path) -> None:
    db = tmp_path / "bad.sqlite"
    db.write_text("bad", encoding="utf-8")
    before = db.read_bytes()
    result = workflow.run_personal_score_db_workflow(
        _workflow(_ready()), artifact_output=None, db_path=db, repository_root=tmp_path
    )
    assert result.workflow_status == "db_rejected"
    assert db.read_bytes() == before


def test_same_artifact_is_reused_and_conflict_is_preserved(tmp_path: Path) -> None:
    detail = _detail()
    save = _ready()
    output = "logs/analysis_details/low.json"
    save.update(
        analysis_id=detail["analysis_id"],
        capture_id=detail["source_capture_id"],
        formal_play=None,
        exclusion={"kind": "low_confidence", "reason": detail["skip_reason"]},
        log_path=output,
    )
    first = workflow.run_personal_score_db_workflow(
        _workflow(save, detail),
        artifact_output=output,
        db_path=tmp_path / "one.sqlite",
        repository_root=tmp_path,
    )
    reused_save = copy.deepcopy(save)
    reused_save["capture_hash"] = "sha256:other"
    second = workflow.run_personal_score_db_workflow(
        _workflow(reused_save, detail),
        artifact_output=output,
        db_path=tmp_path / "two.sqlite",
        repository_root=tmp_path,
    )
    assert first.artifact_status == "created"
    assert second.artifact_status == "reused"
    changed = copy.deepcopy(detail)
    changed["review"]["analysis_confidence"] = 0.41
    conflict = workflow.run_personal_score_db_workflow(
        _workflow(reused_save, changed),
        artifact_output=output,
        db_path=tmp_path / "three.sqlite",
        repository_root=tmp_path,
    )
    assert (conflict.workflow_status, conflict.artifact_status) == ("artifact_conflict", "conflict")
    assert json.loads((tmp_path / output).read_text(encoding="utf-8")) == detail


def test_early_duplicate_still_records_source_and_analysis(tmp_path: Path) -> None:
    db = tmp_path / "score.sqlite"
    first = workflow.run_personal_score_db_workflow(
        _workflow(_ready()), artifact_output=None, db_path=db, repository_root=tmp_path
    )
    second_save = _ready()
    second_save.update(
        capture_id="capture-002",
        capture_hash="sha256:capture-002",
        analysis_id="analysis-002",
    )
    second_save["formal_play"]["play_id"] = "play-002"
    second = workflow.run_personal_score_db_workflow(
        _workflow(second_save), artifact_output=None, db_path=db, repository_root=tmp_path
    )
    assert first.workflow_status == "saved"
    assert (second.workflow_status, second.play_id) == ("duplicate", None)
    with sqlite3.connect(db) as connection:
        assert connection.execute("SELECT COUNT(*) FROM source_captures").fetchone()[0] == 2
        assert connection.execute("SELECT COUNT(*) FROM analysis_logs").fetchone()[0] == 2
        assert connection.execute("SELECT COUNT(*) FROM plays").fetchone()[0] == 1


def test_artifact_publish_failure_does_not_create_database(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    detail = _detail()
    save = _ready()
    output = "logs/analysis_details/low.json"
    save.update(
        analysis_id=detail["analysis_id"],
        capture_id=detail["source_capture_id"],
        formal_play=None,
        exclusion={"kind": "low_confidence", "reason": detail["skip_reason"]},
        log_path=output,
    )
    monkeypatch.setattr(
        workflow,
        "write_analysis_detail_file",
        lambda *args, **kwargs: (_ for _ in ()).throw(OSError("publish failed")),
    )
    result = workflow.run_personal_score_db_workflow(
        _workflow(save, detail),
        artifact_output=output,
        db_path=tmp_path / "db/score.sqlite",
        repository_root=tmp_path,
    )
    assert result.workflow_status == "artifact_failed"
    assert not (tmp_path / "db").exists()


def test_db_failure_after_artifact_keeps_artifact(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    detail = _detail()
    save = _ready()
    output = "logs/analysis_details/low.json"
    save.update(
        analysis_id=detail["analysis_id"],
        capture_id=detail["source_capture_id"],
        formal_play=None,
        exclusion={"kind": "low_confidence", "reason": detail["skip_reason"]},
        log_path=output,
    )

    def fail_save(*args: object, **kwargs: object) -> None:
        raise sqlite3.OperationalError("transaction fixture failure")

    monkeypatch.setattr(workflow, "save_personal_score_db_file_adapted", fail_save)
    result = workflow.run_personal_score_db_workflow(
        _workflow(save, detail),
        artifact_output=output,
        db_path=tmp_path / "score.sqlite",
        repository_root=tmp_path,
    )

    assert result.workflow_status == "artifact_created_db_failed"
    assert (tmp_path / output).is_file()
    assert not (tmp_path / "score.sqlite").exists()


def test_workflow_cli_rejects_mixed_mode_before_side_effects(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    pytest.importorskip("numpy")
    pytest.importorskip("PIL")
    from tools.vision_poc import runner

    monkeypatch.chdir(tmp_path)
    exit_code = runner.main(
        [
            "--personal-score-db-workflow-input",
            "missing.json",
            "--personal-score-db-workflow-database",
            "score.sqlite",
            "--personal-score-db-save-input",
            "other.json",
        ]
    )

    assert exit_code == 2
    assert "cannot be combined" in json.loads(capsys.readouterr().err)["reasons"][0]
    assert not (tmp_path / "score.sqlite").exists()


def test_workflow_cli_saves_ready_input(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    pytest.importorskip("numpy")
    pytest.importorskip("PIL")
    from tools.vision_poc import runner

    monkeypatch.chdir(tmp_path)
    input_path = tmp_path / "workflow.json"
    input_path.write_text(
        json.dumps(
            {
                "workflow_schema_version": 1,
                "analysis_detail": None,
                "save_input": _ready(),
            }
        ),
        encoding="utf-8",
        newline="\n",
    )
    db_path = tmp_path / "score.sqlite"

    exit_code = runner.main(
        [
            "--personal-score-db-workflow-input",
            str(input_path),
            "--personal-score-db-workflow-database",
            str(db_path),
        ]
    )

    result = json.loads(capsys.readouterr().out)
    assert exit_code == 0
    assert result["workflow_status"] == "saved"
    assert result["play_id"] == "play-cli-ready"
    assert db_path.is_file()
