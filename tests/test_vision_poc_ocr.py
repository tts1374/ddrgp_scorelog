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


def test_expected_score_prefers_metadata_score() -> None:
    row = {
        "organized_file": "organized/result_score111111_sample.png",
        "score": "987,650",
        "expected_score": "123456",
    }

    assert runner.expected_score_from_row(row) == "987650"


def test_expected_score_uses_expected_score_when_score_is_empty() -> None:
    row = {
        "organized_file": "organized/result_score111111_sample.png",
        "score": "",
        "expected_score": "123 456",
    }

    assert runner.expected_score_from_row(row) == "123456"


def test_expected_score_falls_back_to_organized_file_score_token() -> None:
    row = {
        "organized_file": "organized/20260628_SP_score987654_song.png",
    }

    assert runner.expected_score_from_row(row) == "987654"


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        ("987650", "987650"),
        ("987,650", "987650"),
        (" score: 12O34\n", "1234"),
        ("ＳＣＯＲＥ 000123", "000123"),
        ("", ""),
    ],
)
def test_normalize_digits_keeps_only_digits(raw: str, expected: str) -> None:
    assert runner.normalize_digits(raw) == expected


def test_preprocess_ocr_roi_supports_judgment_rois() -> None:
    image = Image.new("RGB", (1280, 720), "black")

    preprocessed = runner.preprocess_ocr_roi(image, "max_combo")

    assert preprocessed.roi_name == "max_combo"
    assert preprocessed.original.size == (284, 32)
    assert preprocessed.enlarged.size == (1136, 128)
    assert preprocessed.binary.size == (1176, 168)


def test_score_digits_preprocessing_writes_images_without_ocr_engine(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    if not METADATA_PATH.exists():
        pytest.skip("local screenshot metadata is not available")

    rows = runner.read_metadata(METADATA_PATH)
    result_rows = [row for row in rows if row["screen_type"] == "result"]
    if not result_rows:
        pytest.skip("local result screenshot metadata is not available")

    row = result_rows[0]
    image_path = SCREENSHOTS_ROOT / row["organized_file"]
    if not image_path.exists():
        pytest.skip(f"local screenshot image is not available: {image_path}")

    monkeypatch.setattr(
        runner,
        "run_tesseract",
        lambda _binary, _roi_name="score_digits": (
            "",
            "none",
            "engine_unavailable",
            "test stub",
        ),
    )

    with Image.open(image_path) as image:
        image = image.convert("RGB")
        classification = runner.classify(image, row)
        result = runner.process_score_ocr(image, row, classification, tmp_path)

    assert result.roi_name == "score_digits"
    assert result.engine == "none"
    assert result.status == "engine_unavailable"
    assert Path(result.original_path).exists()
    assert Path(result.enlarged_path).exists()
    assert Path(result.binary_path).exists()
    assert Path(result.original_path).name == "score_digits_original.png"
    assert Path(result.enlarged_path).name == "score_digits_enlarged.png"
    assert Path(result.binary_path).name == "score_digits_binary.png"
