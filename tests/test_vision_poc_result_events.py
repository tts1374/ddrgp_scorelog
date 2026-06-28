from __future__ import annotations

# ruff: noqa: I001

from pathlib import Path

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
    assert events[1].confirmation_mode == "frames"


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
    assert "duplicate_within_frames=1" in events[2].reason


def test_timestamp_confirmed_result_requires_sustained_duration() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
            classification("organized/result_score123456_c.png", result_candidate=True),
        ],
        timestamps_ms=[1_000, 1_400, 2_100],
        min_confirmed_duration_ms=1_000,
    )

    assert [event.confirmed_result for event in events] == [False, False, True]
    assert events[2].event_type == "confirmed"
    assert events[2].timestamp_ms == 2_100
    assert events[2].candidate_duration_ms == 1_100
    assert events[2].confirmation_mode == "time"
    assert "confirmed_after_ms=1100" in events[2].reason


def test_timestamp_irregular_intervals_confirm_after_duration() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
            classification("organized/result_score123456_c.png", result_candidate=True),
        ],
        timestamps_ms=[10_000, 10_120, 11_050],
        min_confirmed_duration_ms=1_000,
    )

    assert [event.confirmed_result for event in events] == [False, False, True]
    assert events[2].candidate_duration_ms == 1_050
    assert events[2].confirmation_mode == "time"


def test_timestamp_few_frames_confirm_when_duration_is_enough() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
        ],
        timestamps_ms=[2_000, 3_250],
        min_confirmed_duration_ms=1_000,
    )

    assert [event.confirmed_result for event in events] == [False, True]
    assert events[1].event_type == "confirmed"
    assert events[1].candidate_duration_ms == 1_250


def test_timestamp_many_frames_do_not_confirm_when_duration_is_short() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
            classification("organized/result_score123456_c.png", result_candidate=True),
            classification("organized/result_score123456_d.png", result_candidate=True),
            classification("organized/result_score123456_e.png", result_candidate=True),
        ],
        timestamps_ms=[5_000, 5_090, 5_180, 5_270, 5_360],
        min_confirmed_duration_ms=1_000,
    )

    assert [event.confirmed_result for event in events] == [False, False, False, False, False]
    assert events[4].candidate_duration_ms == 360
    assert events[4].confirmation_mode == "time"


def test_timestamp_short_duration_is_not_confirmed() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
            classification("organized/result_score123456_c.png", result_candidate=True),
        ],
        timestamps_ms=[1_000, 1_250, 1_700],
        min_confirmed_duration_ms=1_000,
    )

    assert [event.confirmed_result for event in events] == [False, False, False]
    assert [event.event_type for event in events] == ["none", "none", "none"]
    assert events[2].candidate_duration_ms == 700


def test_timestamp_duplicate_uses_time_window() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
            classification("organized/result_score123456_c.png", result_candidate=True),
        ],
        timestamps_ms=[1_000, 2_100, 2_600],
        min_confirmed_duration_ms=1_000,
        duplicate_window_ms=1_000,
    )

    assert events[1].event_type == "confirmed"
    assert events[2].confirmed_result
    assert events[2].event_type == "duplicate"
    assert events[2].duplicate
    assert "duplicate_within_ms=500" in events[2].reason


def test_timestamped_frame_inputs_attach_artificial_capture_time() -> None:
    frames = runner.build_timestamped_frame_inputs(
        [
            {"organized_file": "organized/a.png", "screen_type": "menu_setup"},
            {"organized_file": "organized/b.png", "screen_type": "result"},
            {"organized_file": "organized/c.png", "screen_type": "result"},
        ],
        Path("samples/screenshots"),
        start_ms=250,
        frame_interval_ms=333,
    )

    assert [frame.timestamp_ms for frame in frames] == [250, 583, 916]
    assert [frame.image_path.as_posix() for frame in frames] == [
        "samples/screenshots/organized/a.png",
        "samples/screenshots/organized/b.png",
        "samples/screenshots/organized/c.png",
    ]
