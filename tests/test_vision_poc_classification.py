from __future__ import annotations

# ruff: noqa: I001

from pathlib import Path

import pytest

pytest.importorskip("numpy")
pytest.importorskip("PIL")

from PIL import Image  # noqa: E402
from tools.vision_poc.runner import classify, read_metadata  # noqa: E402


METADATA_PATH = Path("samples/screenshots/metadata.csv")
SCREENSHOTS_ROOT = Path("samples/screenshots")


def load_local_classifications():
    if not METADATA_PATH.exists():
        pytest.skip("local screenshot metadata is not available")

    rows = read_metadata(METADATA_PATH)
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
            classifications.append(classify(image.convert("RGB"), row))
    return classifications


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
