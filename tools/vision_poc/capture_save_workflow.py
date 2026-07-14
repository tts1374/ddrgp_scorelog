from __future__ import annotations

import csv
import hashlib
import json
import math
import uuid
from collections import Counter
from collections.abc import Callable, Iterable, Mapping
from dataclasses import dataclass
from pathlib import Path

from .personal_score_db_save_adapter import PersonalScoreDbFormalPlayValues
from .personal_score_db_workflow import (
    PersonalScoreDbWorkflowInput,
    PersonalScoreDbWorkflowResult,
    run_personal_score_db_workflow,
)
from .runner import main as run_vision_poc

CAPTURE_SAVE_RESULT_SCHEMA_VERSION = 1
CAPTURE_SAVE_APP_VERSION = "0.1.0"
CAPTURE_SAVE_DIGIT_FIELDS = (
    "score",
    "max_combo",
    "marvelous",
    "perfect",
    "great",
    "good",
    "miss",
    "ex_score",
)
CAPTURE_SAVE_WORKFLOW_FAILURE_STATUSES = frozenset(
    {
        "invalid",
        "artifact_failed",
        "artifact_conflict",
        "artifact_created_db_failed",
        "db_rejected",
        "process_failed",
    }
)
_ACCEPTED_EVIDENCE_SOURCES = {
    "play_id": "capture_event_v1",
    "played_at": "capture_utc",
    "master_version": "master_metadata",
    "song_id": "m5_adopted_identity",
    "chart_id": "m5_adopted_identity",
    "score": "m7a_adopted_profile",
    "max_combo": "m7a_adopted_profile",
    "marvelous": "m7a_adopted_profile",
    "perfect": "m7a_adopted_profile",
    "great": "m7a_adopted_profile",
    "good": "m7a_adopted_profile",
    "miss": "m7a_adopted_profile",
    "ex_score": "m7a_adopted_profile",
    "rank": "adopted_rank_recognizer",
    "clear_type": "adopted_clear_type_recognizer",
    "duplicate_key": "capture_event_v1",
}


@dataclass(frozen=True)
class AutomaticFormalEvidence:
    values: PersonalScoreDbFormalPlayValues
    sources: Mapping[str, str]
    confidences: Mapping[str, float]


@dataclass(frozen=True)
class CaptureAnalyzedEvent:
    frame_index: int
    manifest_image_path: str
    image_path: Path
    captured_at: str
    timestamp_ms: int
    candidate_duration_ms: int | None
    event_type: str
    confirmed_result: bool
    duplicate: bool
    confirmation_mode: str
    identity_signal_status: str
    digit_review_status: str
    analysis_confidence: float | None
    candidate_material: Mapping[str, str]
    formal_evidence: AutomaticFormalEvidence | None = None


@dataclass(frozen=True)
class CaptureEventWorkflowResult:
    frame_index: int
    event_status: str
    workflow_invoked: bool
    workflow_status: str
    play_id: str | None
    reasons: tuple[str, ...]


@dataclass(frozen=True)
class CaptureSaveSessionResult:
    status: str
    analysis_output: Path | None
    event_results: tuple[CaptureEventWorkflowResult, ...]
    reasons: tuple[str, ...] = ()


def promote_automatic_formal_values(
    event: CaptureAnalyzedEvent,
    *,
    minimum_confidence: float = 0.98,
) -> tuple[PersonalScoreDbFormalPlayValues | None, tuple[str, ...]]:
    """Promote only complete values carrying adopted, field-specific evidence."""
    if (
        not event.confirmed_result
        or event.duplicate
        or event.event_type != "confirmed"
    ):
        return None, ("formal_promotion_requires_confirmed_non_duplicate",)
    evidence = event.formal_evidence
    if evidence is None:
        return None, ("automatic_formal_evidence_missing",)

    reasons: list[str] = []
    for field_name, required_source in _ACCEPTED_EVIDENCE_SOURCES.items():
        if evidence.sources.get(field_name) != required_source:
            reasons.append(f"formal_evidence.{field_name}_source_not_adopted")
        confidence = evidence.confidences.get(field_name)
        if (
            confidence is None
            or not math.isfinite(confidence)
            or confidence < minimum_confidence
            or confidence > 1.0
        ):
            reasons.append(f"formal_evidence.{field_name}_confidence_insufficient")
    values = evidence.values
    for field_name in (
        "play_id",
        "played_at",
        "master_version",
        "song_id",
        "chart_id",
        "rank",
        "clear_type",
        "duplicate_key",
    ):
        if not getattr(values, field_name).strip():
            reasons.append(f"formal_evidence.{field_name}_missing")
    for field_name in CAPTURE_SAVE_DIGIT_FIELDS:
        if getattr(values, field_name) is None:
            reasons.append(f"formal_evidence.{field_name}_missing")
    return (None, tuple(reasons)) if reasons else (values, ())


def run_capture_save_events(
    events: Iterable[CaptureAnalyzedEvent],
    *,
    manifest_path: Path,
    db_path: Path,
    workflow_runner: Callable[..., PersonalScoreDbWorkflowResult] = (
        run_personal_score_db_workflow
    ),
) -> tuple[CaptureEventWorkflowResult, ...]:
    """Process events serially and invoke the existing workflow at most once per event."""
    results: list[CaptureEventWorkflowResult] = []
    for event in events:
        if not event.confirmed_result or event.event_type == "rejected_transition":
            results.append(
                CaptureEventWorkflowResult(
                    event.frame_index,
                    "policy_excluded",
                    False,
                    "not_invoked",
                    None,
                    ("confirmed_non_duplicate_boundary_not_met",),
                )
            )
            continue

        formal_play, promotion_reasons = promote_automatic_formal_values(event)
        exclusion = (
            {"kind": "duplicate", "reason": "duplicate_result"}
            if event.duplicate
            else None
        )
        capture_id = _stable_id("capture", manifest_path, event.frame_index)
        analysis_id = _stable_id("analysis", manifest_path, event.frame_index)
        save_input = {
            "input_schema_version": 1,
            "candidate_material": dict(event.candidate_material),
            "capture_id": capture_id,
            "capture_hash": _sha256(event.image_path),
            "captured_at": event.captured_at,
            "source_kind": "capture",
            "source_path": str(manifest_path.parent),
            "analysis_id": analysis_id,
            "event_type": event.event_type,
            "confirmed_result": event.confirmed_result,
            "duplicate": event.duplicate,
            "confirmation_mode": event.confirmation_mode,
            "identity_signal_status": event.identity_signal_status,
            "digit_review_status": event.digit_review_status,
            "analysis_confidence": event.analysis_confidence,
            "analysis_summary_json": json.dumps(
                {
                    "contract": "capture-save-workflow-v1",
                    "promotion_status": "ready" if formal_play else "unresolved",
                    "promotion_reasons": list(promotion_reasons),
                },
                ensure_ascii=False,
                sort_keys=True,
            ),
            "app_version": CAPTURE_SAVE_APP_VERSION,
            "formal_play": _formal_play_json(formal_play),
            "exclusion": exclusion,
            "manifest_image_path": event.manifest_image_path,
            "frame_index": event.frame_index,
            "timestamp_ms": event.timestamp_ms,
            "candidate_duration_ms": event.candidate_duration_ms,
            "log_path": "",
        }
        workflow = PersonalScoreDbWorkflowInput(None, save_input)
        workflow_result = workflow_runner(
            workflow,
            artifact_output=None,
            db_path=db_path,
        )
        results.append(
            CaptureEventWorkflowResult(
                event.frame_index,
                workflow_result.workflow_status,
                True,
                workflow_result.workflow_status,
                workflow_result.play_id,
                workflow_result.reasons or promotion_reasons,
            )
        )
    return tuple(results)


def run_capture_save_session(
    *, manifest_path: Path, master_db_path: Path, db_path: Path, repository_root: Path
) -> CaptureSaveSessionResult:
    output = repository_root / "data" / "capture_save_workflow" / (
        f"{manifest_path.parent.name}-{uuid.uuid4().hex[:12]}"
    )
    args = [
        "--sequence-mode",
        "manifest",
        "--frame-manifest",
        str(manifest_path),
        "--output",
        str(output),
        "--ocr-target",
        "confirmed-events",
        "--no-ocr",
        "--m7a-digit-recognition",
        "--m7a-digit-rois",
        "all",
        "--m5-jacket-match",
        "--master-db",
        str(master_db_path),
    ]
    try:
        exit_code = run_vision_poc(args)
        if exit_code != 0:
            return CaptureSaveSessionResult(
                "analysis_failed", output, (), (f"vision_poc_exit_{exit_code}",)
            )
        events = load_capture_analyzed_events(manifest_path, output)
        results = run_capture_save_events(events, manifest_path=manifest_path, db_path=db_path)
        return summarize_capture_save_events(output, results)
    except Exception as exc:
        return CaptureSaveSessionResult("analysis_failed", output, (), (str(exc),))


def summarize_capture_save_events(
    analysis_output: Path,
    event_results: Iterable[CaptureEventWorkflowResult],
) -> CaptureSaveSessionResult:
    results = tuple(event_results)
    failed = tuple(
        item
        for item in results
        if item.event_status in CAPTURE_SAVE_WORKFLOW_FAILURE_STATUSES
    )
    if not failed:
        return CaptureSaveSessionResult("completed", analysis_output, results)
    reasons = tuple(
        f"frame_{item.frame_index}:{item.event_status}:{reason}"
        for item in failed
        for reason in (item.reasons or ("workflow_failed",))
    )
    return CaptureSaveSessionResult("workflow_failed", analysis_output, results, reasons)


def load_capture_analyzed_events(
    manifest_path: Path, analysis_output: Path
) -> tuple[CaptureAnalyzedEvent, ...]:
    manifest_rows = _read_csv(manifest_path)
    event_rows = _read_csv(analysis_output / "result_events.csv")
    decision_rows = {
        int(row["frame_index"]): row
        for row in _read_csv(analysis_output / "m7_save_decision_preview.csv")
    }
    events: list[CaptureAnalyzedEvent] = []
    for row in event_rows:
        frame_index = int(row["frame_index"])
        is_observable_event = row["event_type"] != "none" or _bool(row["result_candidate"])
        if not is_observable_event:
            continue
        manifest_row = manifest_rows[frame_index]
        relative_image = manifest_row["image_path"]
        image_path = (manifest_path.parent / relative_image).resolve()
        decision = decision_rows.get(frame_index, {})
        candidate_material = {
            key: value
            for key, value in decision.items()
            if value
            and (
                key.startswith("m5_")
                or key.endswith("_recognized_digits")
                or key.endswith("_status")
                or key.endswith("_confidence")
            )
            and "expected" not in key
            and "match" not in key
        }
        confidences = [
            float(value)
            for key, value in decision.items()
            if key.endswith("_confidence") and value
        ]
        events.append(
            CaptureAnalyzedEvent(
                frame_index=frame_index,
                manifest_image_path=relative_image.replace("\\", "/"),
                image_path=image_path,
                captured_at=manifest_row.get("captured_at_utc", ""),
                timestamp_ms=int(row["timestamp_ms"]),
                candidate_duration_ms=(
                    int(row["candidate_duration_ms"])
                    if row["candidate_duration_ms"]
                    else None
                ),
                event_type=row["event_type"],
                confirmed_result=_bool(row["confirmed_result"]),
                duplicate=_bool(row["duplicate"]),
                confirmation_mode=row["confirmation_mode"],
                identity_signal_status=decision.get("m5_identity_signal_status", ""),
                digit_review_status=decision.get("m7a_digit_aggregate_status", ""),
                analysis_confidence=min(confidences) if confidences else None,
                candidate_material=candidate_material,
            )
        )
    return tuple(events)


def capture_save_session_result_json(result: CaptureSaveSessionResult) -> dict[str, object]:
    counts = Counter(item.event_status for item in result.event_results)
    return {
        "result_schema_version": CAPTURE_SAVE_RESULT_SCHEMA_VERSION,
        "status": result.status,
        "analysis_output": str(result.analysis_output) if result.analysis_output else None,
        "event_count": len(result.event_results),
        "status_counts": dict(sorted(counts.items())),
        "saved_play_ids": [item.play_id for item in result.event_results if item.play_id],
        "reasons": list(result.reasons),
        "events": [
            {
                "frame_index": item.frame_index,
                "event_status": item.event_status,
                "workflow_invoked": item.workflow_invoked,
                "workflow_status": item.workflow_status,
                "play_id": item.play_id,
                "reasons": list(item.reasons),
            }
            for item in result.event_results
        ],
    }


def _formal_play_json(values: PersonalScoreDbFormalPlayValues | None) -> object:
    if values is None:
        return None
    return {
        name: getattr(values, name)
        for name in (
            "play_id",
            "played_at",
            "master_version",
            "song_id",
            "chart_id",
            *CAPTURE_SAVE_DIGIT_FIELDS,
            "rank",
            "clear_type",
            "duplicate_key",
        )
    }


def _stable_id(prefix: str, manifest_path: Path, frame_index: int) -> str:
    value = f"{manifest_path.resolve()}:{frame_index}".encode()
    return f"{prefix}-{hashlib.sha256(value).hexdigest()[:24]}"


def _sha256(path: Path) -> str:
    return "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()


def _read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8", newline="") as file:
        return list(csv.DictReader(file))


def _bool(value: str) -> bool:
    return value.lower() == "true"
