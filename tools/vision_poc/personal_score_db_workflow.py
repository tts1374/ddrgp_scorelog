from __future__ import annotations

import json
import sqlite3
import sys
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any

from .personal_score_db_analysis_artifacts import (
    load_analysis_detail,
    validate_analysis_detail,
    validate_analysis_detail_log_path,
    write_analysis_detail_file,
)
from .personal_score_db_cli_save import parse_personal_score_db_save_input
from .personal_score_db_file_save import save_personal_score_db_file_adapted
from .personal_score_db_save_adapter import adapt_personal_score_db_save_input
from .personal_score_db_schema import assert_personal_score_db_compatible

WORKFLOW_INPUT_SCHEMA_VERSION = 1
WORKFLOW_RESULT_SCHEMA_VERSION = 1
_WORKFLOW_KEYS = {"workflow_schema_version", "analysis_detail", "save_input"}


@dataclass(frozen=True)
class PersonalScoreDbWorkflowInput:
    analysis_detail: dict[str, object] | None
    save_input: object


@dataclass(frozen=True)
class PersonalScoreDbWorkflowResult:
    workflow_status: str
    artifact_status: str
    adapter_status: str
    db_status: str
    written: bool
    source_capture_id: str | None
    analysis_id: str | None
    play_id: str | None
    reasons: tuple[str, ...]
    artifact_path: str | None
    db_path: Path


def load_personal_score_db_workflow_input(path: Path) -> PersonalScoreDbWorkflowInput:
    try:
        value = json.loads(
            Path(path).read_text(encoding="utf-8"),
            object_pairs_hook=_object_without_duplicate_keys,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exc:
        raise ValueError(f"workflow input could not be loaded: {exc}") from exc
    if not isinstance(value, dict) or set(value) != _WORKFLOW_KEYS:
        raise ValueError("workflow input keys are invalid")
    version = value["workflow_schema_version"]
    if isinstance(version, bool) or version != WORKFLOW_INPUT_SCHEMA_VERSION:
        raise ValueError("workflow_schema_version must be 1")
    detail = value["analysis_detail"]
    if detail is not None:
        validate_analysis_detail(detail)
    # Keep this object separate; strict save parsing happens in orchestration once.
    if not isinstance(value["save_input"], dict):
        raise ValueError("save_input must be an object")
    return PersonalScoreDbWorkflowInput(detail, value["save_input"])


def run_personal_score_db_workflow(
    workflow: PersonalScoreDbWorkflowInput,
    *,
    artifact_output: str | None,
    db_path: Path,
    repository_root: Path | None = None,
) -> PersonalScoreDbWorkflowResult:
    path = Path(db_path)
    try:
        adapter_input = parse_personal_score_db_save_input(workflow.save_input)
        adapter = adapt_personal_score_db_save_input(adapter_input)
    except ValueError as exc:
        return _result(
            "invalid", "not_requested", "invalid", "not_checked", path, (str(exc),), artifact_output
        )
    if adapter.status == "unresolved":
        return _result(
            "unresolved",
            "not_requested",
            adapter.status,
            "not_checked",
            path,
            adapter.reasons,
            artifact_output,
        )
    save_input = adapter.save_input
    if save_input is None:
        raise AssertionError("resolved adapter requires save input")

    detail = workflow.analysis_detail
    required = save_input.analysis.analysis_status in {"low_confidence", "error"}
    if required and (detail is None or artifact_output is None):
        return _result(
            "invalid",
            "not_requested",
            adapter.status,
            "not_checked",
            path,
            ("analysis artifact is required",),
            artifact_output,
        )
    if detail is None and artifact_output is not None:
        return _result(
            "invalid",
            "not_requested",
            adapter.status,
            "not_checked",
            path,
            ("artifact output requires analysis_detail",),
            artifact_output,
        )
    if detail is not None and artifact_output is None:
        return _result(
            "invalid",
            "not_requested",
            adapter.status,
            "not_checked",
            path,
            ("analysis_detail requires artifact output",),
            artifact_output,
        )
    if detail is not None:
        mismatch = _shared_value_mismatch(detail, save_input, artifact_output)
        if mismatch:
            return _result(
                "invalid",
                "not_requested",
                adapter.status,
                "not_checked",
                path,
                mismatch,
                artifact_output,
            )
    elif save_input.analysis.log_path != "":
        return _result(
            "invalid",
            "not_requested",
            adapter.status,
            "not_checked",
            path,
            ("analysis.log_path must be empty without artifact",),
            artifact_output,
        )

    try:
        duplicate = _inspect_database(
            path, save_input.play.duplicate_key if save_input.play else None
        )
    except (OSError, sqlite3.Error, ValueError) as exc:
        return _result(
            "db_rejected",
            "not_requested",
            adapter.status,
            "rejected",
            path,
            (str(exc),),
            artifact_output,
        )

    artifact_status = "not_requested"
    if detail is not None and artifact_output is not None:
        root = Path.cwd() if repository_root is None else repository_root
        target = root.joinpath(*PurePosixPath(artifact_output).parts)
        try:
            validate_analysis_detail_log_path(artifact_output)
            resolved_root = root.resolve()
            if resolved_root not in target.resolve().parents:
                raise ValueError("analysis detail output resolves outside the repository root")
            if target.exists():
                if load_analysis_detail(target) != detail:
                    return _result(
                        "artifact_conflict",
                        "conflict",
                        adapter.status,
                        "compatible",
                        path,
                        ("existing artifact payload differs",),
                        artifact_output,
                    )
                artifact_status = "reused"
            else:
                write_analysis_detail_file(detail, artifact_output, repository_root=root)
                artifact_status = "created"
        except ValueError as exc:
            status = "artifact_conflict" if target.exists() else "artifact_failed"
            artifact = "conflict" if target.exists() else "failed"
            return _result(
                status, artifact, adapter.status, "compatible", path, (str(exc),), artifact_output
            )
        except OSError as exc:
            return _result(
                "artifact_failed",
                "failed",
                adapter.status,
                "compatible",
                path,
                (str(exc),),
                artifact_output,
            )

    try:
        saved = save_personal_score_db_file_adapted(path, adapter)
    except (OSError, sqlite3.Error, ValueError) as exc:
        status = (
            "artifact_created_db_failed"
            if artifact_status in {"created", "reused"}
            else "db_rejected"
        )
        return _result(
            status, artifact_status, adapter.status, "failed", path, (str(exc),), artifact_output
        )
    workflow_status = (
        "duplicate"
        if duplicate or (saved.written and saved.play_id is None and adapter.status == "ready")
        else ("saved" if saved.play_id is not None else "excluded")
    )
    return PersonalScoreDbWorkflowResult(
        workflow_status,
        artifact_status,
        saved.adapter_status,
        "written",
        saved.written,
        saved.source_capture_id,
        saved.analysis_id,
        saved.play_id,
        saved.reasons,
        artifact_output,
        path,
    )


def personal_score_db_workflow_result_json(
    result: PersonalScoreDbWorkflowResult,
) -> dict[str, object]:
    return {
        "result_schema_version": WORKFLOW_RESULT_SCHEMA_VERSION,
        "workflow_status": result.workflow_status,
        "artifact_status": result.artifact_status,
        "adapter_status": result.adapter_status,
        "db_status": result.db_status,
        "written": result.written,
        "source_capture_id": result.source_capture_id,
        "analysis_id": result.analysis_id,
        "play_id": result.play_id,
        "reasons": list(result.reasons),
        "artifact_path": result.artifact_path,
        "db_path": str(result.db_path),
    }


def run_personal_score_db_workflow_cli(
    *,
    input_path: Path,
    artifact_output: str | None,
    db_path: Path,
    use_input_log_path: bool = False,
) -> int:
    try:
        workflow = load_personal_score_db_workflow_input(input_path)
        if use_input_log_path:
            log_path = workflow.save_input.get("log_path")
            artifact_output = log_path if isinstance(log_path, str) and log_path else None
        result = run_personal_score_db_workflow(
            workflow, artifact_output=artifact_output, db_path=db_path
        )
    except ValueError as exc:
        result = _result(
            "invalid",
            "not_requested",
            "invalid",
            "not_checked",
            db_path,
            (str(exc),),
            artifact_output,
        )
    payload = personal_score_db_workflow_result_json(result)
    exit_code = (
        0
        if result.workflow_status in {"saved", "excluded", "duplicate"}
        else (1 if result.workflow_status == "unresolved" else 2)
    )
    print(
        json.dumps(payload, ensure_ascii=False, sort_keys=True),
        file=sys.stdout if exit_code == 0 else sys.stderr,
    )
    return exit_code


def _shared_value_mismatch(
    detail: dict[str, object], save_input: Any, artifact_output: str
) -> tuple[str, ...]:
    analysis = save_input.analysis
    expected_status = {
        "save_ready": "save_ready",
        "duplicate": "duplicate",
        "low_confidence": "excluded",
        "error": "excluded",
        "excluded": "excluded",
    }[analysis.save_boundary_status]
    mismatches = []
    if detail["analysis_id"] != analysis.analysis_id:
        mismatches.append("analysis_id mismatch")
    if detail["source_capture_id"] != analysis.source_capture_id:
        mismatches.append("source_capture_id mismatch")
    if detail["save_boundary_status"] != expected_status:
        mismatches.append("save_boundary_status mismatch")
    if analysis.log_path != artifact_output:
        mismatches.append("analysis.log_path and artifact output mismatch")
    return tuple(mismatches)


def _inspect_database(path: Path, duplicate_key: str | None) -> bool:
    if path.exists() and path.is_dir():
        raise ValueError(f"personal score DB path is a directory: {path}")
    if not path.exists() or path.stat().st_size == 0:
        return False
    uri = f"file:{path.resolve().as_posix()}?mode=ro"
    try:
        connection = sqlite3.connect(uri, uri=True)
        try:
            assert_personal_score_db_compatible(connection)
            return bool(
                duplicate_key
                and connection.execute(
                    "SELECT 1 FROM plays WHERE duplicate_key=? LIMIT 1", (duplicate_key,)
                ).fetchone()
            )
        finally:
            connection.close()
    except sqlite3.DatabaseError as exc:
        raise ValueError("personal score DB is not a compatible SQLite database") from exc


def _result(
    status: str,
    artifact: str,
    adapter: str,
    db: str,
    path: Path,
    reasons: tuple[str, ...],
    artifact_path: str | None,
) -> PersonalScoreDbWorkflowResult:
    return PersonalScoreDbWorkflowResult(
        status, artifact, adapter, db, False, None, None, None, reasons, artifact_path, path
    )


def _object_without_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate key: {key}")
        result[key] = value
    return result
