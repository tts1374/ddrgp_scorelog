from __future__ import annotations

import json
import sqlite3
from dataclasses import dataclass, replace
from datetime import datetime

from .personal_score_db_schema import prepare_personal_score_db_for_write

PERSONAL_SCORE_DB_WRITABLE_SOURCE_KINDS = (
    "manifest",
    "timestamped",
    "capture",
    "manual",
)
PERSONAL_SCORE_DB_ANALYSIS_STATUSES = (
    "saved",
    "skipped",
    "low_confidence",
    "error",
)
PERSONAL_SCORE_DB_SAVE_READY_STATUS = "save_ready"
PERSONAL_SCORE_DB_DUPLICATE_SKIP_REASON = "duplicate_key_already_saved"


@dataclass(frozen=True)
class PersonalScoreDbSourceCaptureInput:
    capture_id: str
    capture_hash: str
    captured_at: str
    source_kind: str
    source_path: str
    manifest_image_path: str = ""
    frame_index: int | None = None


@dataclass(frozen=True)
class PersonalScoreDbPlayInput:
    play_id: str
    played_at: str
    master_version: str
    song_id: str
    chart_id: str
    score: int
    max_combo: int
    marvelous: int
    perfect: int
    great: int
    good: int
    miss: int
    ex_score: int
    rank: str
    clear_type: str
    capture_hash: str
    source_capture_id: str
    duplicate_key: str
    analysis_confidence: float
    app_version: str


@dataclass(frozen=True)
class PersonalScoreDbAnalysisInput:
    analysis_id: str
    play_id: str | None
    source_capture_id: str
    analysis_status: str
    save_boundary_status: str
    skip_reason: str
    event_type: str
    confirmed_result: bool
    duplicate: bool
    confirmation_mode: str
    timestamp_ms: int | None
    candidate_duration_ms: int | None
    identity_signal_status: str
    digit_review_status: str
    analysis_confidence: float | None
    analysis_summary_json: str
    log_path: str
    app_version: str


@dataclass(frozen=True)
class PersonalScoreDbSaveInput:
    source_capture: PersonalScoreDbSourceCaptureInput
    play: PersonalScoreDbPlayInput | None
    analysis: PersonalScoreDbAnalysisInput


@dataclass(frozen=True)
class PersonalScoreDbWriteResult:
    source_capture_id: str
    analysis_id: str
    play_id: str | None
    analysis_status: str
    save_boundary_status: str
    skip_reason: str
    duplicate: bool

    @property
    def saved(self) -> bool:
        return self.play_id is not None


def personal_score_db_save_input_errors(
    save_input: PersonalScoreDbSaveInput,
) -> tuple[str, ...]:
    errors: list[str] = []
    source = save_input.source_capture
    analysis = save_input.analysis
    play = save_input.play

    _require_text(errors, "source_capture.capture_id", source.capture_id)
    _require_text(errors, "source_capture.capture_hash", source.capture_hash)
    _require_aware_timestamp(errors, "source_capture.captured_at", source.captured_at)
    _require_text(errors, "source_capture.source_path", source.source_path)
    if source.source_kind not in PERSONAL_SCORE_DB_WRITABLE_SOURCE_KINDS:
        errors.append("source_capture.source_kind_not_writable")
    if source.frame_index is not None and source.frame_index < 0:
        errors.append("source_capture.frame_index_negative")

    _require_text(errors, "analysis.analysis_id", analysis.analysis_id)
    _require_text(errors, "analysis.source_capture_id", analysis.source_capture_id)
    _require_text(errors, "analysis.save_boundary_status", analysis.save_boundary_status)
    _require_text(errors, "analysis.event_type", analysis.event_type)
    _require_text(errors, "analysis.confirmation_mode", analysis.confirmation_mode)
    _require_text(errors, "analysis.app_version", analysis.app_version)
    if analysis.analysis_status not in PERSONAL_SCORE_DB_ANALYSIS_STATUSES:
        errors.append("analysis.analysis_status_invalid")
    if analysis.timestamp_ms is not None and analysis.timestamp_ms < 0:
        errors.append("analysis.timestamp_ms_negative")
    if analysis.candidate_duration_ms is not None and analysis.candidate_duration_ms < 0:
        errors.append("analysis.candidate_duration_ms_negative")
    _validate_confidence(
        errors,
        "analysis.analysis_confidence",
        analysis.analysis_confidence,
        allow_none=True,
    )
    _validate_summary_json(errors, analysis.analysis_summary_json)

    if analysis.source_capture_id != source.capture_id:
        errors.append("analysis.source_capture_id_mismatch")

    if play is None:
        if analysis.play_id is not None:
            errors.append("analysis.play_id_requires_play")
        if analysis.analysis_status == "saved":
            errors.append("saved_analysis_requires_play")
        if analysis.save_boundary_status == PERSONAL_SCORE_DB_SAVE_READY_STATUS:
            errors.append("save_ready_status_requires_play")
        if not analysis.skip_reason:
            errors.append("non_saved_analysis_requires_skip_reason")
    else:
        _validate_play(errors, play)
        if analysis.analysis_status != "saved":
            errors.append("play_requires_saved_analysis")
        if analysis.save_boundary_status != PERSONAL_SCORE_DB_SAVE_READY_STATUS:
            errors.append("play_requires_save_ready_status")
        if analysis.skip_reason:
            errors.append("saved_analysis_skip_reason_must_be_empty")
        if analysis.play_id != play.play_id:
            errors.append("analysis.play_id_mismatch")
        if not analysis.confirmed_result:
            errors.append("play_requires_confirmed_result")
        if analysis.duplicate:
            errors.append("play_must_not_be_duplicate")
        if analysis.event_type != "confirmed":
            errors.append("play_requires_confirmed_event_type")
        if play.source_capture_id != source.capture_id:
            errors.append("play.source_capture_id_mismatch")
        if play.capture_hash != source.capture_hash:
            errors.append("play.capture_hash_mismatch")
        if analysis.app_version != play.app_version:
            errors.append("analysis.app_version_mismatch")
        if analysis.analysis_confidence != play.analysis_confidence:
            errors.append("analysis.analysis_confidence_mismatch")

    if analysis.duplicate:
        if analysis.analysis_status != "skipped":
            errors.append("duplicate_requires_skipped_analysis")
        if analysis.save_boundary_status != "duplicate":
            errors.append("duplicate_requires_duplicate_boundary_status")
    if analysis.analysis_status == "low_confidence" and analysis.duplicate:
        errors.append("low_confidence_must_not_be_duplicate")

    return tuple(errors)


def validate_personal_score_db_save_input(
    save_input: PersonalScoreDbSaveInput,
) -> None:
    errors = personal_score_db_save_input_errors(save_input)
    if errors:
        msg = "personal score DB save input is invalid: " + ", ".join(errors)
        raise ValueError(msg)


def write_personal_score_db_save(
    connection: sqlite3.Connection,
    save_input: PersonalScoreDbSaveInput,
) -> PersonalScoreDbWriteResult:
    validate_personal_score_db_save_input(save_input)
    if connection.in_transaction:
        raise ValueError("personal score DB writer requires no active transaction")

    prepare_personal_score_db_for_write(connection)
    source = save_input.source_capture
    analysis = save_input.analysis
    play = save_input.play

    with connection:
        if play is not None and _duplicate_key_already_saved(
            connection,
            play.duplicate_key,
        ):
            play = None
            analysis = replace(
                analysis,
                play_id=None,
                analysis_status="skipped",
                save_boundary_status="duplicate",
                skip_reason=PERSONAL_SCORE_DB_DUPLICATE_SKIP_REASON,
                duplicate=True,
            )
        connection.execute(
            """
            INSERT INTO source_captures (
              capture_id, capture_hash, captured_at, source_kind, source_path,
              manifest_image_path, frame_index
            )
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            (
                source.capture_id,
                source.capture_hash,
                source.captured_at,
                source.source_kind,
                source.source_path,
                source.manifest_image_path,
                source.frame_index,
            ),
        )
        if play is not None:
            _insert_play(connection, play)
        _insert_analysis(connection, analysis)

    return PersonalScoreDbWriteResult(
        source_capture_id=source.capture_id,
        analysis_id=analysis.analysis_id,
        play_id=play.play_id if play is not None else None,
        analysis_status=analysis.analysis_status,
        save_boundary_status=analysis.save_boundary_status,
        skip_reason=analysis.skip_reason,
        duplicate=analysis.duplicate,
    )


def _duplicate_key_already_saved(
    connection: sqlite3.Connection,
    duplicate_key: str,
) -> bool:
    row = connection.execute(
        "SELECT 1 FROM plays WHERE duplicate_key = ? LIMIT 1",
        (duplicate_key,),
    ).fetchone()
    return row is not None


def _validate_play(errors: list[str], play: PersonalScoreDbPlayInput) -> None:
    for field_name in (
        "play_id",
        "master_version",
        "song_id",
        "chart_id",
        "rank",
        "clear_type",
        "capture_hash",
        "source_capture_id",
        "duplicate_key",
        "app_version",
    ):
        _require_text(errors, f"play.{field_name}", getattr(play, field_name))
    _require_aware_timestamp(errors, "play.played_at", play.played_at)
    if play.duplicate_key.startswith(("score:", "file:")):
        errors.append("play.duplicate_key_uses_preview_format")
    if not 0 <= play.score <= 1_000_000:
        errors.append("play.score_out_of_range")
    for field_name in (
        "max_combo",
        "marvelous",
        "perfect",
        "great",
        "good",
        "miss",
        "ex_score",
    ):
        if getattr(play, field_name) < 0:
            errors.append(f"play.{field_name}_negative")
    _validate_confidence(
        errors,
        "play.analysis_confidence",
        play.analysis_confidence,
        allow_none=False,
    )


def _require_text(errors: list[str], field_name: str, value: str) -> None:
    if not value.strip():
        errors.append(f"{field_name}_required")


def _require_aware_timestamp(
    errors: list[str],
    field_name: str,
    value: str,
) -> None:
    if not value.strip():
        errors.append(f"{field_name}_required")
        return
    normalized = f"{value[:-1]}+00:00" if value.endswith("Z") else value
    try:
        parsed = datetime.fromisoformat(normalized)
    except ValueError:
        errors.append(f"{field_name}_invalid")
        return
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        errors.append(f"{field_name}_timezone_required")


def _validate_confidence(
    errors: list[str],
    field_name: str,
    value: float | None,
    *,
    allow_none: bool,
) -> None:
    if value is None:
        if not allow_none:
            errors.append(f"{field_name}_required")
        return
    if not 0.0 <= value <= 1.0:
        errors.append(f"{field_name}_out_of_range")


def _validate_summary_json(errors: list[str], value: str) -> None:
    try:
        parsed = json.loads(value)
    except json.JSONDecodeError:
        errors.append("analysis.analysis_summary_json_invalid")
        return
    if not isinstance(parsed, dict):
        errors.append("analysis.analysis_summary_json_must_be_object")


def _insert_play(
    connection: sqlite3.Connection,
    play: PersonalScoreDbPlayInput,
) -> None:
    connection.execute(
        """
        INSERT INTO plays (
          play_id, played_at, master_version, song_id, chart_id, score,
          max_combo, marvelous, perfect, great, good, miss, ex_score, rank,
          clear_type, capture_hash, source_capture_id, duplicate_key,
          analysis_confidence, app_version
        )
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            play.play_id,
            play.played_at,
            play.master_version,
            play.song_id,
            play.chart_id,
            play.score,
            play.max_combo,
            play.marvelous,
            play.perfect,
            play.great,
            play.good,
            play.miss,
            play.ex_score,
            play.rank,
            play.clear_type,
            play.capture_hash,
            play.source_capture_id,
            play.duplicate_key,
            play.analysis_confidence,
            play.app_version,
        ),
    )


def _insert_analysis(
    connection: sqlite3.Connection,
    analysis: PersonalScoreDbAnalysisInput,
) -> None:
    connection.execute(
        """
        INSERT INTO analysis_logs (
          analysis_id, play_id, source_capture_id, analysis_status,
          save_boundary_status, skip_reason, event_type, confirmed_result,
          duplicate, confirmation_mode, timestamp_ms, candidate_duration_ms,
          identity_signal_status, digit_review_status, analysis_confidence,
          analysis_summary_json, log_path, app_version
        )
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            analysis.analysis_id,
            analysis.play_id,
            analysis.source_capture_id,
            analysis.analysis_status,
            analysis.save_boundary_status,
            analysis.skip_reason,
            analysis.event_type,
            int(analysis.confirmed_result),
            int(analysis.duplicate),
            analysis.confirmation_mode,
            analysis.timestamp_ms,
            analysis.candidate_duration_ms,
            analysis.identity_signal_status,
            analysis.digit_review_status,
            analysis.analysis_confidence,
            analysis.analysis_summary_json,
            analysis.log_path,
            analysis.app_version,
        ),
    )
