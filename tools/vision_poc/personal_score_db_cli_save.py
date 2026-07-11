from __future__ import annotations

import json
import math
import sqlite3
import sys
from pathlib import Path
from typing import Any

from .personal_score_db_file_save import (
    PersonalScoreDbFileSaveResult,
    save_personal_score_db_file,
)
from .personal_score_db_save_adapter import (
    PersonalScoreDbFormalPlayValues,
    PersonalScoreDbSaveAdapterInput,
    PersonalScoreDbSaveExclusion,
    adapt_personal_score_db_save_input,
)

PERSONAL_SCORE_DB_SAVE_INPUT_SCHEMA_VERSION = 1
PERSONAL_SCORE_DB_SAVE_RESULT_SCHEMA_VERSION = 1
PERSONAL_SCORE_DB_SAVE_INPUT_VALIDATION_RESULT_SCHEMA_VERSION = 1

_TOP_LEVEL_REQUIRED_KEYS = frozenset(
    {
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
    }
)
_TOP_LEVEL_OPTIONAL_KEYS = frozenset(
    {
        "formal_play",
        "exclusion",
        "manifest_image_path",
        "frame_index",
        "timestamp_ms",
        "candidate_duration_ms",
        "log_path",
    }
)
_FORMAL_PLAY_TEXT_KEYS = (
    "play_id",
    "played_at",
    "master_version",
    "song_id",
    "chart_id",
    "rank",
    "clear_type",
    "duplicate_key",
)
_FORMAL_PLAY_INTEGER_KEYS = (
    "score",
    "max_combo",
    "marvelous",
    "perfect",
    "great",
    "good",
    "miss",
    "ex_score",
)
_FORMAL_PLAY_KEYS = frozenset(_FORMAL_PLAY_TEXT_KEYS + _FORMAL_PLAY_INTEGER_KEYS)
_EXCLUSION_KEYS = frozenset({"kind", "reason"})


def load_personal_score_db_save_input(path: Path) -> PersonalScoreDbSaveAdapterInput:
    """Load one strict UTF-8 JSON adapter input without deriving formal values."""
    input_path = Path(path)
    try:
        text = input_path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        raise ValueError(f"personal score DB save input could not be read: {exc}") from exc
    try:
        value = json.loads(text, object_pairs_hook=_object_without_duplicate_keys)
    except (json.JSONDecodeError, ValueError) as exc:
        raise ValueError(f"personal score DB save input is invalid JSON: {exc}") from exc

    root = _require_object(value, "input")
    _validate_keys(
        root,
        required=_TOP_LEVEL_REQUIRED_KEYS,
        optional=_TOP_LEVEL_OPTIONAL_KEYS,
        field_name="input",
    )
    version = _require_int(root["input_schema_version"], "input.input_schema_version")
    if version != PERSONAL_SCORE_DB_SAVE_INPUT_SCHEMA_VERSION:
        raise ValueError(
            "input.input_schema_version must be "
            f"{PERSONAL_SCORE_DB_SAVE_INPUT_SCHEMA_VERSION}"
        )

    candidate_material = _require_string_mapping(
        root["candidate_material"],
        "input.candidate_material",
    )
    formal_play = _load_formal_play(root.get("formal_play"))
    exclusion = _load_exclusion(root.get("exclusion"))

    return PersonalScoreDbSaveAdapterInput(
        candidate_material=candidate_material,
        capture_id=_require_str(root["capture_id"], "input.capture_id"),
        capture_hash=_require_str(root["capture_hash"], "input.capture_hash"),
        captured_at=_require_str(root["captured_at"], "input.captured_at"),
        source_kind=_require_str(root["source_kind"], "input.source_kind"),
        source_path=_require_str(root["source_path"], "input.source_path"),
        analysis_id=_require_str(root["analysis_id"], "input.analysis_id"),
        event_type=_require_str(root["event_type"], "input.event_type"),
        confirmed_result=_require_bool(
            root["confirmed_result"],
            "input.confirmed_result",
        ),
        duplicate=_require_bool(root["duplicate"], "input.duplicate"),
        confirmation_mode=_require_str(
            root["confirmation_mode"],
            "input.confirmation_mode",
        ),
        identity_signal_status=_require_str(
            root["identity_signal_status"],
            "input.identity_signal_status",
        ),
        digit_review_status=_require_str(
            root["digit_review_status"],
            "input.digit_review_status",
        ),
        analysis_confidence=_optional_number(
            root["analysis_confidence"],
            "input.analysis_confidence",
        ),
        analysis_summary_json=_require_str(
            root["analysis_summary_json"],
            "input.analysis_summary_json",
        ),
        app_version=_require_str(root["app_version"], "input.app_version"),
        formal_play=formal_play,
        exclusion=exclusion,
        manifest_image_path=_optional_str(
            root.get("manifest_image_path"),
            "input.manifest_image_path",
            default="",
        ),
        frame_index=_optional_int(root.get("frame_index"), "input.frame_index"),
        timestamp_ms=_optional_int(root.get("timestamp_ms"), "input.timestamp_ms"),
        candidate_duration_ms=_optional_int(
            root.get("candidate_duration_ms"),
            "input.candidate_duration_ms",
        ),
        log_path=_optional_str(root.get("log_path"), "input.log_path", default=""),
    )


def personal_score_db_file_save_result_json(
    result: PersonalScoreDbFileSaveResult,
) -> dict[str, object]:
    return {
        "result_schema_version": PERSONAL_SCORE_DB_SAVE_RESULT_SCHEMA_VERSION,
        "db_path": str(result.db_path),
        "adapter_status": result.adapter_status,
        "written": result.written,
        "play_id": result.play_id,
        "source_capture_id": result.source_capture_id,
        "analysis_id": result.analysis_id,
        "reasons": list(result.reasons),
    }


def personal_score_db_save_input_validation_result_json(
    *,
    input_path: Path,
    adapter_status: str,
    save_input_constructed: bool,
    reasons: tuple[str, ...],
) -> dict[str, object]:
    return {
        "validation_result_schema_version": (
            PERSONAL_SCORE_DB_SAVE_INPUT_VALIDATION_RESULT_SCHEMA_VERSION
        ),
        "input_path": str(input_path),
        "adapter_status": adapter_status,
        "save_input_constructed": save_input_constructed,
        "reasons": list(reasons),
    }


def emit_personal_score_db_save_input_validation_invalid(
    *, input_path: Path, reason: str
) -> int:
    """Emit one invalid validation result without reading input or touching a DB."""
    result = personal_score_db_save_input_validation_result_json(
        input_path=input_path,
        adapter_status="invalid",
        save_input_constructed=False,
        reasons=(reason,),
    )
    print(json.dumps(result, ensure_ascii=False, sort_keys=True), file=sys.stderr)
    return 2


def run_personal_score_db_save_input_validation_cli(*, input_path: Path) -> int:
    """Strictly load and adapt one formal save input without opening a database."""
    try:
        adapter_input = load_personal_score_db_save_input(input_path)
        adapter_result = adapt_personal_score_db_save_input(adapter_input)
    except (OSError, ValueError) as exc:
        return emit_personal_score_db_save_input_validation_invalid(
            input_path=input_path,
            reason=str(exc),
        )

    result = personal_score_db_save_input_validation_result_json(
        input_path=input_path,
        adapter_status=adapter_result.status,
        save_input_constructed=adapter_result.save_input is not None,
        reasons=adapter_result.reasons,
    )
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 1 if adapter_result.status == "unresolved" else 0


def run_personal_score_db_save_cli(*, input_path: Path, db_path: Path) -> int:
    """Run one explicit formal DB save and emit one machine-readable JSON result."""
    try:
        adapter_input = load_personal_score_db_save_input(input_path)
        result = save_personal_score_db_file(db_path, adapter_input)
    except (OSError, sqlite3.Error, ValueError) as exc:
        error = {
            "result_schema_version": PERSONAL_SCORE_DB_SAVE_RESULT_SCHEMA_VERSION,
            "db_path": str(db_path),
            "adapter_status": "invalid",
            "written": False,
            "play_id": None,
            "source_capture_id": None,
            "analysis_id": None,
            "reasons": [str(exc)],
        }
        print(json.dumps(error, ensure_ascii=False, sort_keys=True), file=sys.stderr)
        return 2

    print(
        json.dumps(
            personal_score_db_file_save_result_json(result),
            ensure_ascii=False,
            sort_keys=True,
        )
    )
    return 0 if result.written else 1


def _load_formal_play(value: object) -> PersonalScoreDbFormalPlayValues | None:
    if value is None:
        return None
    formal = _require_object(value, "input.formal_play")
    _validate_keys(
        formal,
        required=_FORMAL_PLAY_KEYS,
        optional=frozenset(),
        field_name="input.formal_play",
    )
    values: dict[str, object] = {
        key: _require_str(formal[key], f"input.formal_play.{key}")
        for key in _FORMAL_PLAY_TEXT_KEYS
    }
    values.update(
        {
            key: _optional_int(formal[key], f"input.formal_play.{key}")
            for key in _FORMAL_PLAY_INTEGER_KEYS
        }
    )
    return PersonalScoreDbFormalPlayValues(**values)


def _load_exclusion(value: object) -> PersonalScoreDbSaveExclusion | None:
    if value is None:
        return None
    exclusion = _require_object(value, "input.exclusion")
    _validate_keys(
        exclusion,
        required=_EXCLUSION_KEYS,
        optional=frozenset(),
        field_name="input.exclusion",
    )
    return PersonalScoreDbSaveExclusion(
        kind=_require_str(exclusion["kind"], "input.exclusion.kind"),
        reason=_require_str(exclusion["reason"], "input.exclusion.reason"),
    )


def _object_without_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate key: {key}")
        result[key] = value
    return result


def _require_object(value: object, field_name: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"{field_name} must be an object")
    return value


def _validate_keys(
    value: dict[str, object],
    *,
    required: frozenset[str],
    optional: frozenset[str],
    field_name: str,
) -> None:
    missing = sorted(required - value.keys())
    unknown = sorted(value.keys() - required - optional)
    if missing:
        raise ValueError(f"{field_name} missing required key(s): {', '.join(missing)}")
    if unknown:
        raise ValueError(f"{field_name} has unknown key(s): {', '.join(unknown)}")


def _require_str(value: object, field_name: str) -> str:
    if not isinstance(value, str):
        raise ValueError(f"{field_name} must be a string")
    return value


def _optional_str(value: object, field_name: str, *, default: str) -> str:
    if value is None:
        return default
    return _require_str(value, field_name)


def _require_bool(value: object, field_name: str) -> bool:
    if not isinstance(value, bool):
        raise ValueError(f"{field_name} must be a boolean")
    return value


def _require_int(value: object, field_name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise ValueError(f"{field_name} must be an integer")
    return value


def _optional_int(value: object, field_name: str) -> int | None:
    if value is None:
        return None
    return _require_int(value, field_name)


def _optional_number(value: object, field_name: str) -> float | None:
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{field_name} must be a number or null")
    number = float(value)
    if not math.isfinite(number):
        raise ValueError(f"{field_name} must be finite")
    return number


def _require_string_mapping(value: object, field_name: str) -> dict[str, str]:
    mapping = _require_object(value, field_name)
    for key, item in mapping.items():
        if not isinstance(item, str):
            raise ValueError(f"{field_name}.{key} must be a string")
    return {str(key): item for key, item in mapping.items()}
