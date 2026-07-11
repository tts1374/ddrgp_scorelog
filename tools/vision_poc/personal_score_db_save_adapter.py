from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass

from .personal_score_db_save import (
    PersonalScoreDbAnalysisInput,
    PersonalScoreDbPlayInput,
    PersonalScoreDbSaveInput,
    PersonalScoreDbSourceCaptureInput,
    personal_score_db_save_input_errors,
)

PERSONAL_SCORE_DB_ADAPTER_STATUSES = ("ready", "unresolved", "excluded")
PERSONAL_SCORE_DB_EXCLUSION_KINDS = (
    "duplicate",
    "low_confidence",
    "skipped",
    "error",
)


@dataclass(frozen=True)
class PersonalScoreDbFormalPlayValues:
    play_id: str = ""
    played_at: str = ""
    master_version: str = ""
    song_id: str = ""
    chart_id: str = ""
    score: int | None = None
    max_combo: int | None = None
    marvelous: int | None = None
    perfect: int | None = None
    great: int | None = None
    good: int | None = None
    miss: int | None = None
    ex_score: int | None = None
    rank: str = ""
    clear_type: str = ""
    duplicate_key: str = ""


@dataclass(frozen=True)
class PersonalScoreDbSaveExclusion:
    kind: str
    reason: str


@dataclass(frozen=True)
class PersonalScoreDbSaveAdapterInput:
    candidate_material: Mapping[str, str]
    capture_id: str
    capture_hash: str
    captured_at: str
    source_kind: str
    source_path: str
    analysis_id: str
    event_type: str
    confirmed_result: bool
    duplicate: bool
    confirmation_mode: str
    identity_signal_status: str
    digit_review_status: str
    analysis_confidence: float | None
    analysis_summary_json: str
    app_version: str
    formal_play: PersonalScoreDbFormalPlayValues | None = None
    exclusion: PersonalScoreDbSaveExclusion | None = None
    manifest_image_path: str = ""
    frame_index: int | None = None
    timestamp_ms: int | None = None
    candidate_duration_ms: int | None = None
    log_path: str = ""


@dataclass(frozen=True)
class PersonalScoreDbSaveAdapterResult:
    status: str
    reasons: tuple[str, ...]
    save_input: PersonalScoreDbSaveInput | None


def adapt_personal_score_db_save_input(
    adapter_input: PersonalScoreDbSaveAdapterInput,
) -> PersonalScoreDbSaveAdapterResult:
    """Build formal DB input without promoting preview candidate observations."""
    source = _source_capture_input(adapter_input)
    exclusion = adapter_input.exclusion
    if adapter_input.duplicate:
        exclusion = PersonalScoreDbSaveExclusion(
            kind="duplicate",
            reason=(
                exclusion.reason
                if exclusion is not None and exclusion.kind == "duplicate"
                else "duplicate_result"
            ),
        )

    if exclusion is not None:
        return _excluded_result(adapter_input, source, exclusion)

    if adapter_input.formal_play is None:
        return PersonalScoreDbSaveAdapterResult(
            status="unresolved",
            reasons=("formal_play_required",),
            save_input=None,
        )

    missing_reasons = _formal_play_missing_reasons(adapter_input.formal_play)
    if missing_reasons:
        return PersonalScoreDbSaveAdapterResult(
            status="unresolved",
            reasons=missing_reasons,
            save_input=None,
        )

    formal = adapter_input.formal_play
    play = PersonalScoreDbPlayInput(
        play_id=formal.play_id,
        played_at=formal.played_at,
        master_version=formal.master_version,
        song_id=formal.song_id,
        chart_id=formal.chart_id,
        score=_required_int(formal.score),
        max_combo=_required_int(formal.max_combo),
        marvelous=_required_int(formal.marvelous),
        perfect=_required_int(formal.perfect),
        great=_required_int(formal.great),
        good=_required_int(formal.good),
        miss=_required_int(formal.miss),
        ex_score=_required_int(formal.ex_score),
        rank=formal.rank,
        clear_type=formal.clear_type,
        capture_hash=adapter_input.capture_hash,
        source_capture_id=adapter_input.capture_id,
        duplicate_key=formal.duplicate_key,
        analysis_confidence=_required_confidence(adapter_input.analysis_confidence),
        app_version=adapter_input.app_version,
    )
    analysis = _analysis_input(
        adapter_input,
        play_id=play.play_id,
        analysis_status="saved",
        save_boundary_status="save_ready",
        skip_reason="",
        duplicate=False,
    )
    save_input = PersonalScoreDbSaveInput(
        source_capture=source,
        play=play,
        analysis=analysis,
    )
    errors = personal_score_db_save_input_errors(save_input)
    if errors:
        return PersonalScoreDbSaveAdapterResult(
            status="unresolved",
            reasons=errors,
            save_input=None,
        )
    return PersonalScoreDbSaveAdapterResult(
        status="ready",
        reasons=(),
        save_input=save_input,
    )


def _excluded_result(
    adapter_input: PersonalScoreDbSaveAdapterInput,
    source: PersonalScoreDbSourceCaptureInput,
    exclusion: PersonalScoreDbSaveExclusion,
) -> PersonalScoreDbSaveAdapterResult:
    if exclusion.kind not in PERSONAL_SCORE_DB_EXCLUSION_KINDS:
        return PersonalScoreDbSaveAdapterResult(
            status="unresolved",
            reasons=("exclusion.kind_invalid",),
            save_input=None,
        )
    if not exclusion.reason.strip():
        return PersonalScoreDbSaveAdapterResult(
            status="unresolved",
            reasons=("exclusion.reason_required",),
            save_input=None,
        )

    analysis_status, boundary_status, duplicate = {
        "duplicate": ("skipped", "duplicate", True),
        "low_confidence": ("low_confidence", "low_confidence", False),
        "skipped": ("skipped", "excluded", False),
        "error": ("error", "error", False),
    }[exclusion.kind]
    analysis = _analysis_input(
        adapter_input,
        play_id=None,
        analysis_status=analysis_status,
        save_boundary_status=boundary_status,
        skip_reason=exclusion.reason,
        duplicate=duplicate,
    )
    save_input = PersonalScoreDbSaveInput(
        source_capture=source,
        play=None,
        analysis=analysis,
    )
    errors = personal_score_db_save_input_errors(save_input)
    if errors:
        return PersonalScoreDbSaveAdapterResult(
            status="unresolved",
            reasons=errors,
            save_input=None,
        )
    return PersonalScoreDbSaveAdapterResult(
        status="excluded",
        reasons=(exclusion.reason,),
        save_input=save_input,
    )


def _source_capture_input(
    adapter_input: PersonalScoreDbSaveAdapterInput,
) -> PersonalScoreDbSourceCaptureInput:
    return PersonalScoreDbSourceCaptureInput(
        capture_id=adapter_input.capture_id,
        capture_hash=adapter_input.capture_hash,
        captured_at=adapter_input.captured_at,
        source_kind=adapter_input.source_kind,
        source_path=adapter_input.source_path,
        manifest_image_path=adapter_input.manifest_image_path,
        frame_index=adapter_input.frame_index,
    )


def _analysis_input(
    adapter_input: PersonalScoreDbSaveAdapterInput,
    *,
    play_id: str | None,
    analysis_status: str,
    save_boundary_status: str,
    skip_reason: str,
    duplicate: bool,
) -> PersonalScoreDbAnalysisInput:
    return PersonalScoreDbAnalysisInput(
        analysis_id=adapter_input.analysis_id,
        play_id=play_id,
        source_capture_id=adapter_input.capture_id,
        analysis_status=analysis_status,
        save_boundary_status=save_boundary_status,
        skip_reason=skip_reason,
        event_type=adapter_input.event_type,
        confirmed_result=adapter_input.confirmed_result,
        duplicate=duplicate,
        confirmation_mode=adapter_input.confirmation_mode,
        timestamp_ms=adapter_input.timestamp_ms,
        candidate_duration_ms=adapter_input.candidate_duration_ms,
        identity_signal_status=adapter_input.identity_signal_status,
        digit_review_status=adapter_input.digit_review_status,
        analysis_confidence=adapter_input.analysis_confidence,
        analysis_summary_json=adapter_input.analysis_summary_json,
        log_path=adapter_input.log_path,
        app_version=adapter_input.app_version,
    )


def _formal_play_missing_reasons(
    formal: PersonalScoreDbFormalPlayValues,
) -> tuple[str, ...]:
    reasons: list[str] = []
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
        if not getattr(formal, field_name).strip():
            reasons.append(f"formal_play.{field_name}_required")
    for field_name in (
        "score",
        "max_combo",
        "marvelous",
        "perfect",
        "great",
        "good",
        "miss",
        "ex_score",
    ):
        if getattr(formal, field_name) is None:
            reasons.append(f"formal_play.{field_name}_required")
    return tuple(reasons)


def _required_int(value: int | None) -> int:
    assert value is not None
    return value


def _required_confidence(value: float | None) -> float:
    return value if value is not None else -1.0
