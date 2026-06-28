from __future__ import annotations

# ruff: noqa: I001

import csv
import argparse
import json
from pathlib import Path

import pytest

pytest.importorskip("numpy")
pytest.importorskip("PIL")

from PIL import Image  # noqa: E402
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


def write_test_image(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.new("RGB", (1280, 720), "black").save(path)


def read_csv_rows(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", newline="") as file:
        return list(csv.DictReader(file))


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


def test_timestamp_candidate_duration_grows_from_first_candidate() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
            classification("organized/result_score123456_c.png", result_candidate=True),
        ],
        timestamps_ms=[1_000, 1_400, 2_000],
        min_confirmed_duration_ms=1_000,
    )

    assert [event.candidate_duration_ms for event in events] == [0, 400, 1_000]
    assert events[2].confirmed_result
    assert events[2].confirmation_mode == "time"


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
        duplicate_window_frames=0,
        duplicate_window_ms=1_000,
    )

    assert events[1].event_type == "confirmed"
    assert events[2].confirmed_result
    assert events[2].event_type == "duplicate"
    assert events[2].duplicate
    assert "duplicate_within_ms=500" in events[2].reason


def test_result_events_summary_counts_event_outcomes() -> None:
    events = runner.build_result_events(
        [
            classification("organized/result_score123456_a.png", result_candidate=True),
            classification("organized/result_score123456_b.png", result_candidate=True),
            classification("organized/result_score123456_c.png", result_candidate=True),
            classification(
                "organized/transition_countup_score999999_a.png",
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            ),
        ],
        timestamps_ms=[1_000, 2_100, 2_600, 3_000],
        min_confirmed_duration_ms=1_000,
        duplicate_window_ms=1_000,
    )

    summary = runner.summarize_result_events(events)

    assert summary == {
        "total": 4,
        "confirmed_count": 1,
        "confirmed_result_count": 2,
        "duplicate_count": 1,
        "rejected_transition_count": 1,
        "first_confirmed_frame_index": 1,
        "first_confirmed_timestamp_ms": 2_100,
        "confirmation_mode_counts": {"time": 4},
    }


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


def test_read_frame_manifest_resolves_paths_from_frame_root(tmp_path: Path) -> None:
    image_path = tmp_path / "frames" / "a.png"
    write_test_image(image_path)
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms,screen_type\n"
        "a.png,100,result\n",
        encoding="utf-8",
    )

    frames = runner.read_frame_manifest(manifest_path, tmp_path / "frames")

    assert len(frames) == 1
    assert frames[0].image_path == image_path
    assert frames[0].timestamp_ms == 100
    assert frames[0].row == {"organized_file": "a.png", "screen_type": "result"}


def test_make_frame_manifest_writes_sorted_strictly_increasing_timestamps(tmp_path: Path) -> None:
    frame_root = tmp_path / "frames"
    write_test_image(frame_root / "frame_002.jpg")
    write_test_image(frame_root / "frame_001.png")
    write_test_image(frame_root / "frame_003.webp")
    manifest_path = tmp_path / "capture_manifest.csv"

    count = runner.write_capture_frame_manifest(
        manifest_path,
        frame_root,
        30,
        screen_type="unknown",
    )

    assert count == 3
    assert read_csv_rows(manifest_path) == [
        {"image_path": "frame_001.png", "timestamp_ms": "0", "screen_type": "unknown"},
        {"image_path": "frame_002.jpg", "timestamp_ms": "33", "screen_type": "unknown"},
        {"image_path": "frame_003.webp", "timestamp_ms": "67", "screen_type": "unknown"},
    ]


def test_make_frame_manifest_avoids_duplicate_timestamps_at_high_fps(tmp_path: Path) -> None:
    frame_root = tmp_path / "frames"
    write_test_image(frame_root / "frame_001.png")
    write_test_image(frame_root / "frame_002.png")
    write_test_image(frame_root / "frame_003.png")
    manifest_path = tmp_path / "capture_manifest.csv"

    runner.write_capture_frame_manifest(manifest_path, frame_root, 3000)

    rows = read_csv_rows(manifest_path)
    assert [row["timestamp_ms"] for row in rows] == ["0", "1", "2"]
    assert list(rows[0].keys()) == ["image_path", "timestamp_ms"]


def test_make_frame_manifest_rejects_invalid_fps() -> None:
    message = "fps must be a finite number greater than 0"
    with pytest.raises(argparse.ArgumentTypeError, match=message):
        runner.parse_positive_fps("0")

    with pytest.raises(argparse.ArgumentTypeError, match=message):
        runner.parse_positive_fps("not-a-number")


def test_make_frame_manifest_rejects_empty_frame_root(tmp_path: Path) -> None:
    frame_root = tmp_path / "frames"
    frame_root.mkdir()

    with pytest.raises(ValueError, match="contains no frame images"):
        runner.write_capture_frame_manifest(tmp_path / "capture_manifest.csv", frame_root, 30)


def test_make_frame_manifest_rejects_missing_output_parent(tmp_path: Path) -> None:
    frame_root = tmp_path / "frames"
    write_test_image(frame_root / "frame_001.png")

    with pytest.raises(ValueError, match="parent directory does not exist"):
        runner.write_capture_frame_manifest(
            tmp_path / "missing" / "capture_manifest.csv",
            frame_root,
            30,
        )


def test_generated_frame_manifest_is_readable(tmp_path: Path) -> None:
    frame_root = tmp_path / "frames"
    write_test_image(frame_root / "frame_001.png")
    write_test_image(frame_root / "frame_002.png")
    manifest_path = tmp_path / "capture_manifest.csv"
    runner.write_capture_frame_manifest(
        manifest_path,
        frame_root,
        60,
        screen_type="unknown",
    )

    frames = runner.read_frame_manifest(manifest_path, frame_root)

    assert [frame.row for frame in frames] == [
        {"organized_file": "frame_001.png", "screen_type": "unknown"},
        {"organized_file": "frame_002.png", "screen_type": "unknown"},
    ]
    assert [frame.timestamp_ms for frame in frames] == [0, 17]


@pytest.mark.parametrize(
    ("timestamp_ms", "expected_message"),
    [
        ("", "timestamp_ms is empty"),
        ("abc", "timestamp_ms must be an integer"),
        ("-1", "timestamp_ms must be non-negative"),
    ],
)
def test_read_frame_manifest_rejects_invalid_timestamps(
    tmp_path: Path,
    timestamp_ms: str,
    expected_message: str,
) -> None:
    write_test_image(tmp_path / "a.png")
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms\n"
        f"a.png,{timestamp_ms}\n",
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match=expected_message):
        runner.read_frame_manifest(manifest_path)


def test_read_frame_manifest_rejects_non_increasing_timestamps(tmp_path: Path) -> None:
    write_test_image(tmp_path / "a.png")
    write_test_image(tmp_path / "b.png")
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms\n"
        "a.png,100\n"
        "b.png,100\n",
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match="timestamp_ms must be strictly increasing"):
        runner.read_frame_manifest(manifest_path)


def test_read_frame_manifest_reports_missing_image_path(tmp_path: Path) -> None:
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms\n"
        "missing.png,100\n",
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match=r"line 2: image_path does not exist: .*missing\.png"):
        runner.read_frame_manifest(manifest_path)


def test_manifest_mode_writes_time_based_result_events(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    write_test_image(tmp_path / "frames" / "a.png")
    write_test_image(tmp_path / "frames" / "b.png")
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms,screen_type\n"
        "a.png,1000,result\n"
        "b.png,2200,result\n",
        encoding="utf-8",
    )

    monkeypatch.setattr(
        runner,
        "classify",
        lambda _image, row: classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        ),
    )

    output_dir = tmp_path / "output"
    assert (
        runner.main(
            [
                "--sequence-mode",
                "manifest",
                "--frame-manifest",
                str(manifest_path),
                "--frame-root",
                str(tmp_path / "frames"),
                "--output",
                str(output_dir),
                "--no-rois",
                "--no-ocr",
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["confirmation_mode"] for row in rows] == ["time", "time"]
    assert [row["timestamp_ms"] for row in rows] == ["1000", "2200"]
    assert rows[1]["candidate_duration_ms"] == "1200"
    assert rows[1]["event_type"] == "confirmed"
    summary = json.loads((output_dir / "result_events_summary.json").read_text(encoding="utf-8"))
    assert summary["confirmed_count"] == 1
    assert summary["first_confirmed_frame_index"] == 1
    assert summary["first_confirmed_timestamp_ms"] == 2200
    assert summary["confirmation_mode_counts"] == {"time": 2}


def test_metadata_mode_keeps_frame_based_result_events(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    write_test_image(tmp_path / "screenshots" / "a.png")
    write_test_image(tmp_path / "screenshots" / "b.png")
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type\n"
        "a.png,result\n"
        "b.png,result\n",
        encoding="utf-8",
    )

    monkeypatch.setattr(
        runner,
        "classify",
        lambda _image, row: classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        ),
    )

    output_dir = tmp_path / "output"
    assert (
        runner.main(
            [
                "--metadata",
                str(metadata_path),
                "--screenshots-root",
                str(tmp_path / "screenshots"),
                "--output",
                str(output_dir),
                "--no-rois",
                "--no-ocr",
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["confirmation_mode"] for row in rows] == ["frames", "frames"]
    assert [row["timestamp_ms"] for row in rows] == ["", ""]
    assert [row["candidate_duration_ms"] for row in rows] == ["", ""]
    assert rows[1]["event_type"] == "confirmed"
    summary = json.loads((output_dir / "result_events_summary.json").read_text(encoding="utf-8"))
    assert summary["confirmed_count"] == 1
    assert summary["first_confirmed_frame_index"] == 1
    assert summary["first_confirmed_timestamp_ms"] is None
    assert summary["confirmation_mode_counts"] == {"frames": 2}


def test_timestamped_synthetic_sequence_writes_time_event_summary(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in ("a.png", "b.png", "c.png", "d.png", "e.png"):
        write_test_image(tmp_path / "screenshots" / name)
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type\n"
        "a.png,result\n"
        "b.png,result\n"
        "c.png,result\n"
        "d.png,transition\n"
        "e.png,result\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        if row["organized_file"] == "d.png":
            return classification(
                "transition_countup_score999999_d.png",
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            )
        return classification(
            f"organized/result_score123456_{row['organized_file']}",
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    monkeypatch.setattr(runner, "classify", classify_synthetic)

    output_dir = tmp_path / "output"
    assert (
        runner.main(
            [
                "--sequence-mode",
                "timestamped",
                "--metadata",
                str(metadata_path),
                "--screenshots-root",
                str(tmp_path / "screenshots"),
                "--output",
                str(output_dir),
                "--timestamp-start-ms",
                "1000",
                "--timestamp-interval-ms",
                "500",
                "--no-rois",
                "--no-ocr",
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["candidate_duration_ms"] for row in rows] == ["0", "500", "1000", "", "0"]
    assert [row["event_type"] for row in rows] == [
        "none",
        "none",
        "confirmed",
        "rejected_transition",
        "none",
    ]
    assert rows[3]["confirmed_result"] == "False"
    assert rows[3]["result_shape_candidate"] == "True"

    summary = json.loads((output_dir / "result_events_summary.json").read_text(encoding="utf-8"))
    assert summary["confirmed_count"] == 1
    assert summary["duplicate_count"] == 0
    assert summary["rejected_transition_count"] == 1
    assert summary["first_confirmed_timestamp_ms"] == 2000
    assert summary["confirmation_mode_counts"] == {"time": 5}


def test_timestamped_mode_writes_reusable_frame_manifest(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    write_test_image(tmp_path / "screenshots" / "a.png")
    write_test_image(tmp_path / "screenshots" / "b.png")
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type\n"
        "a.png,menu_setup\n"
        "b.png,result\n",
        encoding="utf-8",
    )

    monkeypatch.setattr(
        runner,
        "classify",
        lambda _image, row: classification(
            row["organized_file"],
            result_candidate=row["screen_type"] == "result",
            screen_type=row["screen_type"],
        ),
    )

    output_dir = tmp_path / "output"
    assert (
        runner.main(
            [
                "--sequence-mode",
                "timestamped",
                "--metadata",
                str(metadata_path),
                "--screenshots-root",
                str(tmp_path / "screenshots"),
                "--output",
                str(output_dir),
                "--timestamp-start-ms",
                "500",
                "--timestamp-interval-ms",
                "333",
                "--no-rois",
                "--no-ocr",
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "frame_manifest.csv")
    assert rows == [
        {"image_path": "a.png", "timestamp_ms": "500", "screen_type": "menu_setup"},
        {"image_path": "b.png", "timestamp_ms": "833", "screen_type": "result"},
    ]
