from __future__ import annotations

import json
import os
import tempfile
from datetime import UTC, datetime, timedelta
from pathlib import Path, PurePosixPath
from typing import Any

ANALYSIS_DETAIL_SCHEMA_VERSION = 1
ANALYSIS_DETAIL_GENERATED_BY = "tools.vision_poc.personal_score_db_analysis_artifacts"
ANALYSIS_DETAIL_LOG_ROOT = PurePosixPath("logs/analysis_details")
ANALYSIS_FAILURE_IMAGE_ROOT = PurePosixPath("logs/analysis_failures")
ANALYSIS_DETAIL_RETENTION_DAYS = {
    "short": 7,
    "standard": 30,
    "indefinite": None,
}

_ROOT_KEYS = {
    "schema_version",
    "generated_by",
    "generated_at",
    "app_version",
    "analysis_id",
    "source_capture_id",
    "analysis_status",
    "save_boundary_status",
    "skip_reason",
    "event",
    "review",
    "investigation",
    "failure_image_path",
    "retention",
}
_EVENT_KEYS = {
    "confirmed_result",
    "duplicate",
    "event_type",
    "confirmation_mode",
    "timestamp_ms",
    "candidate_duration_ms",
}
_REVIEW_KEYS = {
    "identity_status",
    "digit_status",
    "analysis_confidence",
}
_INVESTIGATION_KEYS = {"candidate_material", "diagnostic_summary"}
_CANDIDATE_KEYS = {"kind", "status", "summary"}
_RETENTION_KEYS = {"retention_class", "basis_at", "expires_at"}
_FORBIDDEN_KEYS = {
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
    "validation_result_schema_version",
    "adapter_status",
    "save_input_constructed",
    "diagnostic",
    "diagnostic_output_path",
    "migration_plan_status",
}


def build_analysis_detail(
    *,
    generated_at: str,
    app_version: str,
    analysis_id: str,
    source_capture_id: str,
    analysis_status: str,
    save_boundary_status: str,
    skip_reason: str,
    event: dict[str, object],
    review: dict[str, object],
    candidate_material: list[dict[str, str]],
    diagnostic_summary: list[str],
    failure_image_path: str | None,
    retention_class: str,
) -> dict[str, object]:
    payload: dict[str, object] = {
        "schema_version": ANALYSIS_DETAIL_SCHEMA_VERSION,
        "generated_by": ANALYSIS_DETAIL_GENERATED_BY,
        "generated_at": generated_at,
        "app_version": app_version,
        "analysis_id": analysis_id,
        "source_capture_id": source_capture_id,
        "analysis_status": analysis_status,
        "save_boundary_status": save_boundary_status,
        "skip_reason": skip_reason,
        "event": event,
        "review": review,
        "investigation": {
            "candidate_material": candidate_material,
            "diagnostic_summary": diagnostic_summary,
        },
        "failure_image_path": failure_image_path,
        "retention": analysis_detail_retention_metadata(
            retention_class,
            generated_at,
        ),
    }
    validate_analysis_detail(payload)
    return payload


def analysis_detail_retention_metadata(
    retention_class: str,
    basis_at: str,
) -> dict[str, str | None]:
    if retention_class not in ANALYSIS_DETAIL_RETENTION_DAYS:
        raise ValueError("analysis detail retention class is invalid")
    basis = _parse_utc_timestamp(basis_at, "retention.basis_at")
    days = ANALYSIS_DETAIL_RETENTION_DAYS[retention_class]
    expires_at = None
    if days is not None:
        expires_at = _format_utc_timestamp(basis + timedelta(days=days))
    return {
        "retention_class": retention_class,
        "basis_at": _format_utc_timestamp(basis),
        "expires_at": expires_at,
    }


def validate_analysis_detail_log_path(log_path: str) -> None:
    if log_path == "":
        return
    _validate_relative_artifact_path(
        log_path,
        root=ANALYSIS_DETAIL_LOG_ROOT,
        extensions={".json"},
        field_name="analysis_logs.log_path",
    )


def load_analysis_detail(path: Path) -> dict[str, object]:
    """Load and validate one version 1 analysis detail JSON object."""
    try:
        text = Path(path).read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        raise ValueError(f"analysis detail input could not be read: {exc}") from exc
    try:
        payload = json.loads(text, object_pairs_hook=_object_without_duplicate_keys)
    except (json.JSONDecodeError, ValueError) as exc:
        raise ValueError(f"analysis detail input is invalid JSON: {exc}") from exc
    validate_analysis_detail(payload)
    return payload


def write_analysis_detail_file(
    payload: object,
    output_path: str,
    *,
    repository_root: Path | None = None,
) -> Path:
    """Validate and atomically publish one new analysis detail JSON file."""
    validate_analysis_detail(payload)
    validate_analysis_detail_log_path(output_path)
    relative_path = PurePosixPath(output_path)
    root = (Path.cwd() if repository_root is None else Path(repository_root)).resolve()
    target = root.joinpath(*relative_path.parts)
    resolved_target = target.resolve()
    if root not in resolved_target.parents:
        raise ValueError("analysis detail output resolves outside the repository root")
    if target.exists():
        raise ValueError(f"analysis detail output already exists: {output_path}")

    content = (
        json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    ).encode("utf-8")
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary_path: Path | None = None
    try:
        descriptor, temporary_name = tempfile.mkstemp(
            dir=target.parent,
            prefix=f".{target.name}.",
            suffix=".tmp",
        )
        temporary_path = Path(temporary_name)
        with os.fdopen(descriptor, "wb") as output_file:
            output_file.write(content)
            output_file.flush()
            os.fsync(output_file.fileno())
        os.link(temporary_path, target)
    except FileExistsError as exc:
        raise ValueError(f"analysis detail output already exists: {output_path}") from exc
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)
    return target


def validate_analysis_failure_image_path(image_path: str | None) -> None:
    if image_path is None:
        return
    _validate_relative_artifact_path(
        image_path,
        root=ANALYSIS_FAILURE_IMAGE_ROOT,
        extensions={".png", ".jpg", ".jpeg", ".webp"},
        field_name="failure_image_path",
    )


def validate_analysis_detail(payload: object) -> None:
    root = _require_object(payload, "analysis detail")
    _require_exact_keys(root, _ROOT_KEYS, "analysis detail")
    forbidden = _find_forbidden_keys(root)
    if forbidden:
        raise ValueError(
            "analysis detail contains forbidden projection keys: "
            + ", ".join(sorted(forbidden))
        )

    _require_exact_int(root["schema_version"], 1, "schema_version")
    _require_exact_text(root["generated_by"], ANALYSIS_DETAIL_GENERATED_BY, "generated_by")
    _parse_utc_timestamp(_require_text(root["generated_at"], "generated_at"), "generated_at")
    _require_text(root["app_version"], "app_version")
    _require_text(root["analysis_id"], "analysis_id")
    _require_text(root["source_capture_id"], "source_capture_id")
    analysis_status = _require_text(root["analysis_status"], "analysis_status")
    if analysis_status not in {"low_confidence", "error", "skipped"}:
        raise ValueError("analysis_status must be low_confidence, error, or skipped")
    save_status = _require_text(root["save_boundary_status"], "save_boundary_status")
    if save_status == "save_ready":
        raise ValueError("analysis detail must not represent a save-ready play")
    skip_reason = _require_text(root["skip_reason"], "skip_reason")

    event = _require_object(root["event"], "event")
    _require_exact_keys(event, _EVENT_KEYS, "event")
    confirmed = _require_bool(event["confirmed_result"], "event.confirmed_result")
    duplicate = _require_bool(event["duplicate"], "event.duplicate")
    _require_text(event["event_type"], "event.event_type")
    confirmation_mode = _require_text(event["confirmation_mode"], "event.confirmation_mode")
    if confirmation_mode not in {"frames", "time"}:
        raise ValueError("event.confirmation_mode is invalid")
    timestamp_ms = _require_optional_nonnegative_int(event["timestamp_ms"], "event.timestamp_ms")
    _require_optional_nonnegative_int(
        event["candidate_duration_ms"], "event.candidate_duration_ms"
    )
    if confirmation_mode == "time" and timestamp_ms is None:
        raise ValueError("time confirmation requires event.timestamp_ms")
    if confirmation_mode == "frames" and timestamp_ms is not None:
        raise ValueError("frames confirmation must not carry event.timestamp_ms")
    if duplicate:
        if analysis_status != "skipped" or save_status != "duplicate":
            raise ValueError("duplicate detail requires skipped/duplicate statuses")
    elif save_status == "duplicate":
        raise ValueError("duplicate save status requires event.duplicate=true")
    if analysis_status == "low_confidence" and duplicate:
        raise ValueError("low_confidence detail must not be duplicate")
    if confirmed and analysis_status == "error" and not skip_reason:
        raise ValueError("error detail requires skip_reason")

    review = _require_object(root["review"], "review")
    _require_exact_keys(review, _REVIEW_KEYS, "review")
    _require_text(review["identity_status"], "review.identity_status", allow_empty=True)
    _require_text(review["digit_status"], "review.digit_status", allow_empty=True)
    confidence = review["analysis_confidence"]
    if confidence is not None:
        if isinstance(confidence, bool) or not isinstance(confidence, (int, float)):
            raise ValueError("review.analysis_confidence must be a number or null")
        if not 0.0 <= confidence <= 1.0:
            raise ValueError("review.analysis_confidence is out of range")

    investigation = _require_object(root["investigation"], "investigation")
    _require_exact_keys(investigation, _INVESTIGATION_KEYS, "investigation")
    materials = investigation["candidate_material"]
    if not isinstance(materials, list):
        raise ValueError("investigation.candidate_material must be an array")
    for index, value in enumerate(materials):
        item = _require_object(value, f"investigation.candidate_material[{index}]")
        _require_exact_keys(item, _CANDIDATE_KEYS, f"investigation.candidate_material[{index}]")
        _require_text(item["kind"], f"candidate_material[{index}].kind")
        _require_text(item["status"], f"candidate_material[{index}].status")
        _require_text(item["summary"], f"candidate_material[{index}].summary")
    diagnostic_summary = investigation["diagnostic_summary"]
    if not isinstance(diagnostic_summary, list) or not all(
        isinstance(value, str) and value.strip() for value in diagnostic_summary
    ):
        raise ValueError("investigation.diagnostic_summary must be an array of text")

    failure_path = root["failure_image_path"]
    if failure_path is not None and not isinstance(failure_path, str):
        raise ValueError("failure_image_path must be text or null")
    validate_analysis_failure_image_path(failure_path)

    retention = _require_object(root["retention"], "retention")
    _require_exact_keys(retention, _RETENTION_KEYS, "retention")
    expected_retention = analysis_detail_retention_metadata(
        _require_text(retention["retention_class"], "retention.retention_class"),
        _require_text(retention["basis_at"], "retention.basis_at"),
    )
    if retention != expected_retention:
        raise ValueError("retention metadata does not match the deterministic policy")


def _validate_relative_artifact_path(
    value: str,
    *,
    root: PurePosixPath,
    extensions: set[str],
    field_name: str,
) -> None:
    if not value or "\\" in value:
        raise ValueError(f"{field_name} must be a non-empty POSIX relative path")
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts or "." in path.parts:
        raise ValueError(f"{field_name} contains an unsafe path")
    if path.parent == PurePosixPath(".") or tuple(path.parts[: len(root.parts)]) != root.parts:
        raise ValueError(f"{field_name} must be under {root.as_posix()}/")
    if path.suffix.lower() not in extensions:
        raise ValueError(f"{field_name} has an unsupported extension")


def _require_object(value: object, field_name: str) -> dict[str, Any]:
    if not isinstance(value, dict) or not all(isinstance(key, str) for key in value):
        raise ValueError(f"{field_name} must be an object")
    return value


def _object_without_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def _require_exact_keys(value: dict[str, Any], expected: set[str], field_name: str) -> None:
    actual = set(value)
    if actual != expected:
        missing = sorted(expected - actual)
        unknown = sorted(actual - expected)
        raise ValueError(f"{field_name} keys are invalid; missing={missing}, unknown={unknown}")


def _find_forbidden_keys(value: object) -> set[str]:
    if isinstance(value, dict):
        found = set(value) & _FORBIDDEN_KEYS
        for nested in value.values():
            found.update(_find_forbidden_keys(nested))
        return found
    if isinstance(value, list):
        found: set[str] = set()
        for nested in value:
            found.update(_find_forbidden_keys(nested))
        return found
    return set()


def _require_text(value: object, field_name: str, *, allow_empty: bool = False) -> str:
    if not isinstance(value, str) or (not allow_empty and not value.strip()):
        raise ValueError(f"{field_name} must be text")
    return value


def _require_exact_text(value: object, expected: str, field_name: str) -> None:
    if value != expected:
        raise ValueError(f"{field_name} is invalid")


def _require_exact_int(value: object, expected: int, field_name: str) -> None:
    if isinstance(value, bool) or not isinstance(value, int) or value != expected:
        raise ValueError(f"{field_name} is invalid")


def _require_bool(value: object, field_name: str) -> bool:
    if not isinstance(value, bool):
        raise ValueError(f"{field_name} must be boolean")
    return value


def _require_optional_nonnegative_int(value: object, field_name: str) -> int | None:
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise ValueError(f"{field_name} must be a non-negative integer or null")
    return value


def _parse_utc_timestamp(value: str, field_name: str) -> datetime:
    normalized = f"{value[:-1]}+00:00" if value.endswith("Z") else value
    try:
        parsed = datetime.fromisoformat(normalized)
    except ValueError as error:
        raise ValueError(f"{field_name} must be an ISO 8601 timestamp") from error
    if parsed.tzinfo is None or parsed.utcoffset() != timedelta(0):
        raise ValueError(f"{field_name} must be UTC")
    return parsed.astimezone(UTC)


def _format_utc_timestamp(value: datetime) -> str:
    return value.astimezone(UTC).isoformat().replace("+00:00", "Z")
