from __future__ import annotations

# ruff: noqa: I001

import pytest

pytest.importorskip("numpy")
pytest.importorskip("PIL")

from tools.vision_poc import runner  # noqa: E402


SIGNAL = runner.SignalResult(value=False, score=0.0, features={})


def classification(
    organized_file: str,
    *,
    result_candidate: bool,
    result_shape_candidate: bool | None = None,
    screen_type: str = "result",
    transition_kind: str = "",
) -> runner.Classification:
    shape = result_candidate if result_shape_candidate is None else result_shape_candidate
    return runner.Classification(
        organized_file=organized_file,
        screen_type=screen_type,
        result_candidate=result_candidate,
        result_shape_candidate=shape,
        transition_kind=transition_kind,
        expected_result_candidate=screen_type == "result",
        correct=True,
        header_signal=SIGNAL,
        detail_panel_signal=SIGNAL,
        score_signal=SIGNAL,
        rank_signal=SIGNAL,
        reason="test",
    )


def test_confirmed_result_requires_sustained_candidates() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_a.png", result_candidate=True),
        ]
    )

    assert not events[0].confirmed_result
    assert events[0].event_type == "none"
    assert events[1].confirmed_result
    assert events[1].event_type == "confirmed"


def test_single_frame_result_candidate_is_not_confirmed() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification(
                "organized/song_select_a.png",
                result_candidate=False,
                result_shape_candidate=False,
                screen_type="song_select",
            ),
        ]
    )

    assert [event.confirmed_result for event in events] == [False, False]
    assert [event.event_type for event in events] == ["none", "none"]


def test_transition_countup_shape_candidate_is_rejected() -> None:
    events = runner.build_result_events(
        [
            classification(
                "organized/transition_countup_score123456_a.png",
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            ),
            classification(
                "organized/transition_countup_score123456_b.png",
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            ),
        ]
    )

    assert [event.confirmed_result for event in events] == [False, False]
    assert [event.event_type for event in events] == ["rejected_transition", "rejected_transition"]


def test_repeated_confirmed_duplicate_key_becomes_duplicate() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
            classification("organized/result_score123456_c.png", result_candidate=True),
        ]
    )

    assert events[1].event_type == "confirmed"
    assert not events[1].duplicate
    assert events[2].confirmed_result
    assert events[2].event_type == "duplicate"
    assert events[2].duplicate
    assert events[2].duplicate_key == "score:123456"
