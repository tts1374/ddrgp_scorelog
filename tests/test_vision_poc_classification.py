from __future__ import annotations

# ruff: noqa: I001

from pathlib import Path

import pytest

pytest.importorskip("numpy")
pytest.importorskip("PIL")

from PIL import Image  # noqa: E402
from tools.vision_poc import runner  # noqa: E402


METADATA_PATH = Path("samples/screenshots/metadata.csv")
SCREENSHOTS_ROOT = Path("samples/screenshots")


def load_local_classifications():
    if not METADATA_PATH.exists():
        pytest.skip("local screenshot metadata is not available")

    rows = runner.read_metadata(METADATA_PATH)
    missing_images = [
        row["organized_file"]
        for row in rows
        if not (SCREENSHOTS_ROOT / row["organized_file"]).exists()
    ]
    if missing_images:
        pytest.skip(f"local screenshot images are not available: {missing_images[:3]}")

    classifications = []
    for row in rows:
        with Image.open(SCREENSHOTS_ROOT / row["organized_file"]) as image:
            classifications.append(runner.classify(image.convert("RGB"), row))
    return classifications


def signal(value: bool, score: float = 1.0) -> runner.SignalResult:
    return runner.SignalResult(value=value, score=score, features={})


def test_result_candidate_matches_metadata_screen_type() -> None:
    classifications = load_local_classifications()
    expected_false_types = {"song_select", "gameplay", "menu_setup", "transition"}

    for item in classifications:
        if item.screen_type == "result":
            assert item.result_candidate, item.organized_file
        elif item.screen_type in expected_false_types:
            assert not item.result_candidate, item.organized_file


def test_transition_countup_keeps_shape_but_not_result_candidate() -> None:
    classifications = load_local_classifications()
    countups = [
        item
        for item in classifications
        if Path(item.organized_file).name.startswith("transition_countup_")
    ]
    if not countups:
        pytest.skip("local transition_countup_* screenshots are not available")

    for item in countups:
        assert item.result_shape_candidate, item.organized_file
        assert not item.result_candidate, item.organized_file


def test_result_candidate_allows_low_score_and_weak_rank_auxiliary_signals(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(runner, "score_header", lambda _features: signal(True))
    monkeypatch.setattr(runner, "score_detail_panel", lambda _features, _border: signal(True))
    monkeypatch.setattr(runner, "score_score_area", lambda _features: signal(False, 0.1))
    monkeypatch.setattr(runner, "score_rank", lambda _features: signal(False, 0.1))

    item = runner.classify(
        Image.new("RGB", (1280, 720), "black"),
        {
            "organized_file": "organized/result/result_105_score000000_rank_d.png",
            "screen_type": "result",
        },
    )

    assert item.result_shape_candidate
    assert item.result_candidate
    assert item.correct
    assert "finished_result_frame" in item.reason


def test_countup_with_finished_result_shape_stays_out_of_save_candidates(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(runner, "score_header", lambda _features: signal(True))
    monkeypatch.setattr(runner, "score_detail_panel", lambda _features, _border: signal(True))
    monkeypatch.setattr(runner, "score_score_area", lambda _features: signal(True))
    monkeypatch.setattr(runner, "score_rank", lambda _features: signal(True))

    item = runner.classify(
        Image.new("RGB", (1280, 720), "black"),
        {
            "organized_file": "organized/transition/transition_countup_score000000.png",
            "screen_type": "transition",
        },
    )

    assert item.result_shape_candidate
    assert not item.result_candidate
    assert item.transition_kind == "countup"
