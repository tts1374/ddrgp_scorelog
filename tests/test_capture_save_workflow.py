from __future__ import annotations

import sqlite3
from dataclasses import replace
from pathlib import Path

import pytest

from tools.vision_poc import capture_save_workflow_app
from tools.vision_poc.capture_save_workflow import (
    AutomaticFormalEvidence,
    CaptureAnalyzedEvent,
    CaptureEventWorkflowResult,
    CaptureSaveSessionResult,
    promote_automatic_formal_values,
    run_capture_save_events,
    summarize_capture_save_events,
)
from tools.vision_poc.personal_score_db_save_adapter import (
    PersonalScoreDbFormalPlayValues,
)
from tools.vision_poc.personal_score_db_workflow import PersonalScoreDbWorkflowResult


def _formal() -> PersonalScoreDbFormalPlayValues:
    return PersonalScoreDbFormalPlayValues(
        play_id="play-capture-1",
        played_at="2026-07-14T12:34:56+09:00",
        master_version="master-2026-07-14",
        song_id="song-formal",
        chart_id="chart-formal",
        score=987_650,
        max_combo=456,
        marvelous=400,
        perfect=40,
        great=10,
        good=4,
        miss=2,
        ex_score=1_750,
        rank="AAA",
        clear_type="CLEAR",
        duplicate_key="capture-event:v1:formal-1",
    )


def _evidence() -> AutomaticFormalEvidence:
    sources = {
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
    return AutomaticFormalEvidence(
        values=_formal(),
        sources=sources,
        confidences={key: 0.99 for key in sources},
    )


def _event(tmp_path: Path, **changes: object) -> CaptureAnalyzedEvent:
    image = tmp_path / "frame.png"
    image.write_bytes(b"fixture-frame")
    event = CaptureAnalyzedEvent(
        frame_index=2,
        manifest_image_path="frames/frame-000003.png",
        image_path=image,
        captured_at="2026-07-14T12:34:56+09:00",
        timestamp_ms=2_000,
        candidate_duration_ms=1_000,
        event_type="confirmed",
        confirmed_result=True,
        duplicate=False,
        confirmation_mode="time",
        identity_signal_status="composite_resolved_candidate",
        digit_review_status="all_digits_recognized",
        analysis_confidence=0.99,
        candidate_material={
            "identity_signal_song_id": "candidate-song",
            "score_recognized_digits": "111111",
            "score_expected_value": "987650",
            "payload_preview_status": "payload_ready",
        },
        formal_evidence=_evidence(),
    )
    return replace(event, **changes)


def test_complete_adopted_evidence_promotes_all_formal_values(tmp_path: Path) -> None:
    formal, reasons = promote_automatic_formal_values(_event(tmp_path))

    assert reasons == ()
    assert formal == _formal()


@pytest.mark.parametrize(
    ("mutate", "reason"),
    [
        (
            lambda value: replace(
                value,
                sources={**value.sources, "song_id": "m5_candidate"},
            ),
            "formal_evidence.song_id_source_not_adopted",
        ),
        (
            lambda value: replace(
                value,
                confidences={**value.confidences, "score": 0.97},
            ),
            "formal_evidence.score_confidence_insufficient",
        ),
        (
            lambda value: replace(
                value,
                confidences={**value.confidences, "score": float("nan")},
            ),
            "formal_evidence.score_confidence_insufficient",
        ),
        (
            lambda value: replace(
                value,
                values=replace(value.values, clear_type=""),
            ),
            "formal_evidence.clear_type_missing",
        ),
        (
            lambda value: replace(
                value,
                values=replace(value.values, ex_score=None),
            ),
            "formal_evidence.ex_score_missing",
        ),
    ],
)
def test_incomplete_or_low_confidence_evidence_stays_unresolved(
    tmp_path: Path,
    mutate: object,
    reason: str,
) -> None:
    evidence = mutate(_evidence())  # type: ignore[operator]
    formal, reasons = promote_automatic_formal_values(
        _event(tmp_path, formal_evidence=evidence)
    )

    assert formal is None
    assert reason in reasons


def test_candidate_raw_expected_and_preview_values_are_never_promoted(
    tmp_path: Path,
) -> None:
    event = _event(tmp_path, formal_evidence=None)

    formal, reasons = promote_automatic_formal_values(event)

    assert formal is None
    assert reasons == ("automatic_formal_evidence_missing",)


@pytest.mark.parametrize(
    "changes",
    [
        {"confirmed_result": False, "event_type": "none"},
        {"duplicate": True, "event_type": "duplicate"},
        {"event_type": "rejected_transition"},
    ],
)
def test_formal_promotion_requires_confirmed_non_duplicate_boundary(
    tmp_path: Path, changes: dict[str, object]
) -> None:
    formal, reasons = promote_automatic_formal_values(_event(tmp_path, **changes))

    assert formal is None
    assert reasons == ("formal_promotion_requires_confirmed_non_duplicate",)


def test_event_boundary_skips_unconfirmed_and_rejected_without_workflow(
    tmp_path: Path,
) -> None:
    calls = 0

    def fail_if_called(*args: object, **kwargs: object) -> PersonalScoreDbWorkflowResult:
        nonlocal calls
        calls += 1
        raise AssertionError("workflow must not be called")

    events = [
        _event(tmp_path, confirmed_result=False, event_type="none"),
        _event(tmp_path, confirmed_result=False, event_type="rejected_transition"),
    ]
    results = run_capture_save_events(
        events,
        manifest_path=tmp_path / "manifest.csv",
        db_path=tmp_path / "score.sqlite",
        workflow_runner=fail_if_called,
    )

    assert calls == 0
    assert [item.event_status for item in results] == [
        "policy_excluded",
        "policy_excluded",
    ]


def test_confirmed_event_calls_existing_workflow_once_and_saves(tmp_path: Path) -> None:
    manifest = tmp_path / "frame_manifest.csv"
    manifest.write_text("image_path,timestamp_ms\nframe.png,2000\n", encoding="utf-8")
    db_path = tmp_path / "score.sqlite"

    results = run_capture_save_events(
        [_event(tmp_path)],
        manifest_path=manifest,
        db_path=db_path,
    )

    result = results[0]
    assert (result.workflow_invoked, result.workflow_status) == (True, "saved")
    assert result.play_id == "play-capture-1"
    with sqlite3.connect(db_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM plays").fetchone()[0] == 1


def test_confirmed_event_duplicate_is_recorded_without_play(tmp_path: Path) -> None:
    manifest = tmp_path / "frame_manifest.csv"
    manifest.write_text("image_path,timestamp_ms\nframe.png,2000\n", encoding="utf-8")
    db_path = tmp_path / "score.sqlite"

    result = run_capture_save_events(
        [_event(tmp_path, event_type="duplicate", duplicate=True, formal_evidence=None)],
        manifest_path=manifest,
        db_path=db_path,
    )[0]

    assert (result.workflow_invoked, result.event_status, result.play_id) == (
        True,
        "excluded",
        None,
    )
    with sqlite3.connect(db_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM plays").fetchone()[0] == 0
        assert connection.execute("SELECT COUNT(*) FROM source_captures").fetchone()[0] == 1
        assert connection.execute("SELECT duplicate FROM analysis_logs").fetchone()[0] == 1


def test_database_duplicate_is_not_reported_as_saved(tmp_path: Path) -> None:
    manifest = tmp_path / "frame_manifest.csv"
    manifest.write_text("image_path,timestamp_ms\nframe.png,2000\n", encoding="utf-8")
    db_path = tmp_path / "score.sqlite"
    event = _event(tmp_path)
    first = run_capture_save_events([event], manifest_path=manifest, db_path=db_path)
    second_image = tmp_path / "frame-2.png"
    second_image.write_bytes(b"fixture-frame-2")
    second_event = replace(
        event,
        frame_index=3,
        image_path=second_image,
        formal_evidence=replace(
            _evidence(), values=replace(_formal(), play_id="play-capture-2")
        ),
    )
    second = run_capture_save_events(
        [second_event], manifest_path=manifest, db_path=db_path
    )

    assert first[0].event_status == "saved"
    assert (second[0].event_status, second[0].play_id) == ("duplicate", None)
    with sqlite3.connect(db_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM plays").fetchone()[0] == 1


@pytest.mark.parametrize(
    "workflow_status",
    ["unresolved", "invalid", "artifact_failed", "artifact_created_db_failed", "db_rejected"],
)
def test_workflow_failure_status_is_preserved(
    tmp_path: Path, workflow_status: str
) -> None:
    def stub(*args: object, **kwargs: object) -> PersonalScoreDbWorkflowResult:
        return PersonalScoreDbWorkflowResult(
            workflow_status,
            "failed" if "artifact" in workflow_status else "not_requested",
            "unresolved" if workflow_status == "unresolved" else "invalid",
            "failed",
            False,
            None,
            None,
            None,
            ("fixture_failure",),
            None,
            tmp_path / "score.sqlite",
        )

    result = run_capture_save_events(
        [_event(tmp_path)],
        manifest_path=tmp_path / "manifest.csv",
        db_path=tmp_path / "score.sqlite",
        workflow_runner=stub,
    )[0]

    assert result.event_status == workflow_status
    assert not result.play_id


def test_session_promotes_fatal_event_status_and_keeps_saved_play() -> None:
    result = summarize_capture_save_events(
        Path("data/run"),
        [
            CaptureEventWorkflowResult(2, "saved", True, "saved", "play-1", ()),
            CaptureEventWorkflowResult(
                3, "db_rejected", True, "db_rejected", None, ("incompatible DB",)
            ),
        ],
    )

    assert result.status == "workflow_failed"
    assert [item.play_id for item in result.event_results] == ["play-1", None]
    assert result.reasons == ("frame_3:db_rejected:incompatible DB",)


@pytest.mark.parametrize("event_status", ["unresolved", "excluded", "duplicate"])
def test_session_keeps_expected_non_saved_statuses_completed(event_status: str) -> None:
    result = summarize_capture_save_events(
        Path("data/run"),
        [CaptureEventWorkflowResult(2, event_status, True, event_status, None, ("reason",))],
    )

    assert result.status == "completed"


def test_app_returns_nonzero_for_workflow_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    monkeypatch.setattr(
        capture_save_workflow_app,
        "run_capture_save_session",
        lambda **kwargs: CaptureSaveSessionResult(
            "workflow_failed",
            tmp_path / "analysis",
            (
                CaptureEventWorkflowResult(
                    2,
                    "db_rejected",
                    True,
                    "db_rejected",
                    None,
                    ("incompatible DB",),
                ),
            ),
            ("frame_2:db_rejected:incompatible DB",),
        ),
    )

    exit_code = capture_save_workflow_app.main(
        [
            "--manifest",
            str(tmp_path / "manifest.csv"),
            "--master-database",
            str(tmp_path / "master.sqlite"),
            "--database",
            str(tmp_path / "score.sqlite"),
        ]
    )

    assert exit_code == 2
    assert '"status": "workflow_failed"' in capsys.readouterr().err
