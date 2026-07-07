from __future__ import annotations

# ruff: noqa: I001

import csv
import json
from pathlib import Path

import pytest

pytest.importorskip("numpy")
pytest.importorskip("PIL")

from PIL import Image, ImageDraw  # noqa: E402
from tools.vision_poc import runner  # noqa: E402


METADATA_PATH = Path("samples/screenshots/metadata.csv")
SCREENSHOTS_ROOT = Path("samples/screenshots")
SIGNAL = runner.SignalResult(value=False, score=0.0, features={})
DIGIT_PATTERNS = {
    "0": ("111", "101", "101", "101", "111"),
    "1": ("010", "110", "010", "010", "111"),
    "2": ("111", "001", "111", "100", "111"),
    "3": ("111", "001", "111", "001", "111"),
    "4": ("101", "101", "111", "001", "001"),
    "5": ("111", "100", "111", "001", "111"),
    "6": ("111", "100", "111", "101", "111"),
    "7": ("111", "001", "001", "001", "001"),
    "8": ("111", "101", "111", "101", "111"),
    "9": ("111", "101", "111", "001", "111"),
}


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


def digit_glyph(label: str, *, scale: int = 6) -> Image.Image:
    pattern = DIGIT_PATTERNS[label]
    padding = scale // 2
    image = Image.new(
        "RGB",
        (len(pattern[0]) * scale + padding * 2, len(pattern) * scale + padding * 2),
        "black",
    )
    draw = ImageDraw.Draw(image)
    for row_index, row in enumerate(pattern):
        for column_index, value in enumerate(row):
            if value == "1":
                draw.rectangle(
                    (
                        padding + column_index * scale,
                        padding + row_index * scale,
                        padding + (column_index + 1) * scale - 1,
                        padding + (row_index + 1) * scale - 1,
                    ),
                    fill="white",
                )
    return image


def write_digit_templates(root: Path, roi_name: str = "score_digits") -> None:
    template_dir = root / roi_name
    template_dir.mkdir(parents=True, exist_ok=True)
    for label in runner.M7A_DIGIT_REQUIRED_LABELS:
        digit_glyph(label).save(template_dir / f"{label}.png")


def write_score_digit_image(path: Path, digits: str) -> None:
    image = Image.new("RGB", (1280, 720), "black")
    left, top, _right, _bottom = runner.scaled_box(image, runner.ROI_DEFINITIONS["score_digits"])
    cursor_x = left + 20
    cursor_y = top + 8
    for digit in digits:
        glyph = digit_glyph(digit)
        image.paste(glyph, (cursor_x, cursor_y))
        cursor_x += glyph.width + 6
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)


def write_score_digit_image_with_comma(path: Path, digits: str) -> None:
    image = Image.new("RGB", (1280, 720), "black")
    left, top, right, _bottom = runner.scaled_box(image, runner.ROI_DEFINITIONS["score_digits"])
    slot_width = (right - left) / 7
    cursor_y = top + 8
    slot_indices = (0, 1, 2, 4, 5, 6)
    for digit, slot_index in zip(digits, slot_indices, strict=True):
        glyph = digit_glyph(digit)
        cursor_x = int(left + slot_width * slot_index + (slot_width - glyph.width) / 2)
        image.paste(glyph, (cursor_x, cursor_y))

    draw = ImageDraw.Draw(image)
    comma_x = int(left + slot_width * 3 + slot_width * 0.58)
    comma_y = top + 34
    draw.rectangle((comma_x, comma_y, comma_x + 4, comma_y + 6), fill="white")
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)


def write_score_digit_display_image(path: Path, display_text: str) -> None:
    image = Image.new("RGB", (1280, 720), "black")
    left, top, _right, _bottom = runner.scaled_box(image, runner.ROI_DEFINITIONS["score_digits"])
    cursor_x = left + 2
    cursor_y = top + 8
    draw = ImageDraw.Draw(image)
    for character in display_text:
        if character == ",":
            comma_x = cursor_x
            comma_y = top + 34
            draw.rectangle((comma_x, comma_y, comma_x + 4, comma_y + 6), fill="white")
            cursor_x += 6
            continue
        glyph = digit_glyph(character)
        image.paste(glyph, (cursor_x, cursor_y))
        cursor_x += glyph.width + 3
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)


def write_chart_feature_image(path: Path, field_colors: dict[str, str]) -> None:
    image = Image.new("RGB", (1280, 720), "black")
    for field_name, color in field_colors.items():
        image.paste(color, runner.scaled_box(image, runner.ROI_DEFINITIONS[field_name]))
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)


def read_csv_rows(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", newline="") as file:
        return list(csv.DictReader(file))


def result_event(
    organized_file: str,
    *,
    confirmed_result: bool,
    duplicate: bool = False,
    event_type: str = "confirmed",
    result_candidate: bool = True,
) -> runner.ResultEvent:
    return runner.ResultEvent(
        frame_index=0,
        organized_file=organized_file,
        screen_type="result" if result_candidate else "transition",
        result_candidate=result_candidate,
        result_shape_candidate=result_candidate,
        confirmed_result=confirmed_result,
        event_type=event_type,
        duplicate=duplicate,
        duplicate_key=f"file:{organized_file}",
        timestamp_ms=None,
        candidate_duration_ms=None,
        confirmation_mode="frames",
        reason="test",
    )


def stub_tesseract(
    _binary: Image.Image,
    _roi_name: str = "score_digits",
) -> tuple[str, str, str, str]:
    return "123456", "tesseract", "ok", ""


def stub_profile_preprocess(
    _image: Image.Image,
    roi_name: str,
    config: runner.OcrPreprocessConfig = runner.OCR_PREPROCESS_CONFIG,
) -> runner.OcrPreprocessedImages:
    original = Image.new("RGB", (4, 4), "black")
    enlarged = Image.new("RGB", (8, 8), "white")
    binary = Image.new("L", (8, 8), "white")
    profile = next(
        name
        for name, profile_config in runner.OCR_PREPROCESS_PROFILES.items()
        if profile_config == config
    )
    binary.info["profile"] = profile
    binary.info["roi_name"] = roi_name
    return runner.OcrPreprocessedImages(
        roi_name=roi_name,
        original=original,
        enlarged=enlarged,
        binary=binary,
    )


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


def test_ocr_digits_match_ignores_leading_zero_padding() -> None:
    assert runner.ocr_digits_match("0111", "111") is True
    assert runner.ocr_digits_match("00", "0") is True
    assert runner.ocr_digits_match("852", "552") is False
    assert runner.ocr_digits_match("", "0") is None


def test_preprocess_ocr_roi_supports_judgment_rois() -> None:
    image = Image.new("RGB", (1280, 720), "black")

    preprocessed = runner.preprocess_ocr_roi(image, "max_combo")

    assert preprocessed.roi_name == "max_combo"
    assert preprocessed.original.size == (284, 32)
    assert preprocessed.enlarged.size == (1136, 128)
    assert preprocessed.binary.size == (1176, 168)


def test_primary_rois_include_m3_song_and_chart_fields(tmp_path: Path) -> None:
    image = Image.new("RGB", (1280, 720), "black")

    runner.save_primary_rois(image, tmp_path, "sample")

    roi_dir = tmp_path / "rois" / "sample"
    for roi_name in (
        "play_style",
        "difficulty",
        "level",
        "rank",
        "song_title",
        "artist",
    ):
        assert (roi_dir / f"{roi_name}.png").exists()


def test_preprocess_ocr_roi_digit_focus_is_limited_to_miss() -> None:
    image = Image.new("RGB", (1280, 720), "black")

    assert set(runner.OCR_DIGIT_FOCUS_LEFT_FRACTIONS) == {"miss"}

    miss = runner.preprocess_ocr_roi(image, "miss")
    ex_score = runner.preprocess_ocr_roi(image, "ex_score")
    good = runner.preprocess_ocr_roi(image, "good")

    assert miss.original.size == (151, 28)
    assert miss.enlarged.size == (603, 112)
    assert miss.binary.size == (629, 152)
    assert ex_score.original.size == (250, 34)
    assert ex_score.enlarged.size == (1000, 136)
    assert ex_score.binary.size == (1040, 176)
    assert good.original.size == (232, 28)
    assert good.enlarged.size == (928, 112)
    assert good.binary.size == (968, 152)


def test_preprocess_ocr_roi_black_dilation_is_limited_to_ex_score(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    crop = Image.new("RGB", (16, 12), "black")
    for y in range(crop.height):
        crop.putpixel((10, y), (255, 255, 255))

    monkeypatch.setattr(runner, "crop_roi", lambda _image, _roi: crop.copy())

    image = Image.new("RGB", (1280, 720), "black")
    ex_score = runner.preprocess_ocr_roi(image, "ex_score")
    good = runner.preprocess_ocr_roi(image, "good")
    miss = runner.preprocess_ocr_roi(image, "miss")

    assert set(runner.OCR_BINARY_BLACK_DILATE_KERNELS) == {"ex_score"}
    assert ex_score.binary.size == good.binary.size
    assert count_dark_pixels(ex_score.binary) > count_dark_pixels(good.binary)
    assert miss.binary.size[0] < good.binary.size[0]
    assert count_dark_pixels(miss.binary) <= count_dark_pixels(good.binary)


def count_dark_pixels(image: Image.Image) -> int:
    return sum(1 for value in image.convert("L").tobytes() if value < 128)


def test_expected_ocr_value_uses_optional_judgment_columns() -> None:
    row = {
        "organized_file": "organized/result_score111111_sample.png",
        "expected_score": "123456",
        "max_combo": "321",
        "expected_marvelous": "98",
        "perfect": "",
    }

    assert runner.expected_ocr_value_from_row(row, "score_digits") == "123456"
    assert runner.expected_ocr_value_from_row(row, "max_combo") == "321"
    assert runner.expected_ocr_value_from_row(row, "marvelous") == "98"
    assert runner.expected_ocr_value_from_row(row, "perfect") == ""
    assert runner.expected_ocr_value_from_row(row, "miss") == ""


def test_m3_metadata_expected_values_are_separate_from_digit_ocr_expected_values() -> None:
    row = {
        "organized_file": "organized/result_score111111_sample.png",
        "song_title": "  CHAOS   ",
        "expected_artist": "DE-SIRE retunes",
        "play_style": "SINGLE",
        "difficulty": "BEGINNER",
        "level": "06",
        "expected_rank": "D",
        "expected_score": "111111",
    }

    assert runner.expected_m3_metadata_value_from_row(row, "song_title") == "CHAOS"
    assert runner.expected_m3_metadata_value_from_row(row, "artist") == "DE-SIRE retunes"
    assert runner.expected_m3_metadata_value_from_row(row, "play_style") == "SINGLE"
    assert runner.expected_m3_metadata_value_from_row(row, "difficulty") == "BEGINNER"
    assert runner.expected_m3_metadata_value_from_row(row, "level") == "06"
    assert runner.expected_m3_metadata_value_from_row(row, "rank") == "D"
    assert runner.expected_ocr_value_from_row(row, "rank") == ""


def test_m3_metadata_expected_report_uses_confirmed_events_boundary(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    frame_names = (
        "result_001_sp_beginner_lv06_score123456_a.png",
        "result_001_sp_beginner_lv06_score123456_b.png",
        "result_001_sp_beginner_lv06_score123456_c.png",
        "result_001_sp_beginner_lv06_score123456_d.png",
        "transition_countup_score999999.png",
    )
    for name in frame_names:
        write_test_image(tmp_path / "frames" / name)
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms,screen_type,song_title,artist,play_style,difficulty,level,"
        "expected_rank,expected_score\n"
        "result_001_sp_beginner_lv06_score123456_a.png,0,result,,,,,,,\n"
        "result_001_sp_beginner_lv06_score123456_b.png,500,result,,,,,,,\n"
        "result_001_sp_beginner_lv06_score123456_c.png,1000,result,CHAOS,DE-SIRE,SINGLE,BEGINNER,06,D,123456\n"
        "result_001_sp_beginner_lv06_score123456_d.png,1500,result,,,,,,,123456\n"
        "transition_countup_score999999.png,2000,transition,COUNTUP,NOPE,SINGLE,EXPERT,16,A,999999\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        if row["organized_file"].startswith("transition_countup_"):
            return classification(
                row["organized_file"],
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            )
        return classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    monkeypatch.setattr(runner, "classify", classify_synthetic)

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
                "--chart-field-template-root",
                str(tmp_path / "missing_templates"),
            ]
        )
        == 0
    )

    coverage = (output_dir / "m3_metadata_expected_coverage.md").read_text(encoding="utf-8")
    assert "| `song_title` | `evaluated` | 1 | 0 | 1 |" in coverage
    assert "| `rank` | `evaluated` | 1 | 0 | 1 |" in coverage
    assert "`expected_rank` は数字OCRの" in coverage

    template_rows = read_csv_rows(output_dir / "m3_metadata_expected_template.csv")
    assert template_rows == []

    chart_rows = read_csv_rows(output_dir / "m3_chart_fields.csv")
    assert [row["chart_field_target"] for row in chart_rows] == [
        "False",
        "False",
        "True",
        "False",
        "False",
    ]
    assert [row["exclusion_reason"] for row in chart_rows] == [
        "unconfirmed",
        "unconfirmed",
        "",
        "duplicate",
        "rejected_transition",
    ]
    assert chart_rows[2]["expected_play_style"] == "SINGLE"
    assert chart_rows[2]["expected_difficulty"] == "BEGINNER"
    assert chart_rows[2]["expected_level"] == "06"
    assert chart_rows[2]["play_style_roi_path"] == (
        "rois/result_001_sp_beginner_lv06_score123456_c/play_style.png"
    )

    chart_summary = json.loads(
        (output_dir / "m3_chart_fields_summary.json").read_text(encoding="utf-8")
    )
    assert chart_summary["target_boundary"] == "confirmed_result=true and duplicate=false"
    assert chart_summary["chart_field_target_count"] == 1
    assert chart_summary["excluded_counts"] == {
        "duplicate": 1,
        "rejected_transition": 1,
        "unconfirmed": 2,
        "non_result": 0,
    }
    assert chart_summary["fields"]["play_style"]["evaluation_status"] == "evaluated"
    assert chart_summary["fields"]["difficulty"]["expected_value_count"] == 1
    assert chart_summary["fields"]["level"]["no_expected_value_count"] == 0

    extraction_rows = read_csv_rows(output_dir / "m3_chart_field_extraction.csv")
    target_extraction_rows = [
        row for row in extraction_rows if row["chart_field_target"] == "True"
    ]
    extracted_values = [
        (row["field_name"], row["expected_value"], row["extracted_value"], row["match"])
        for row in target_extraction_rows
    ]
    assert extracted_values == [
        ("play_style", "SINGLE", "SINGLE", "True"),
        ("difficulty", "BEGINNER", "BEGINNER", "True"),
        ("level", "6", "6", "True"),
    ]
    assert {row["status"] for row in target_extraction_rows} == {"match"}
    assert extraction_rows[0]["status"] == "skipped"
    assert extraction_rows[0]["failure_reason"] == "unconfirmed"
    assert extraction_rows[9]["failure_reason"] == "duplicate"
    assert extraction_rows[12]["failure_reason"] == "rejected_transition"

    extraction_summary = json.loads(
        (output_dir / "m3_chart_field_extraction_summary.json").read_text(encoding="utf-8")
    )
    assert extraction_summary["target_boundary"] == "confirmed_result=true and duplicate=false"
    assert extraction_summary["extractor"] == "filename-baseline"
    assert extraction_summary["chart_field_target_count"] == 1
    assert extraction_summary["total_attempts"] == 3
    assert extraction_summary["status_counts"]["match"] == 3
    assert extraction_summary["status_counts"]["skipped"] == 12
    assert extraction_summary["fields"]["level"]["match_count"] == 1

    image_feature_rows = read_csv_rows(
        output_dir / "m3_chart_field_image_feature_extraction.csv"
    )
    assert [row["status"] for row in image_feature_rows[:6]] == [
        "skipped",
        "skipped",
        "skipped",
        "skipped",
        "skipped",
        "skipped",
    ]
    assert {row["status"] for row in image_feature_rows[6:9]} == {"empty_extraction"}
    assert image_feature_rows[9]["failure_reason"] == "duplicate"
    assert image_feature_rows[12]["failure_reason"] == "rejected_transition"

    image_feature_summary = json.loads(
        (output_dir / "m3_chart_field_image_feature_extraction_summary.json").read_text(
            encoding="utf-8"
        )
    )
    assert image_feature_summary["target_boundary"] == (
        "confirmed_result=true and duplicate=false"
    )
    assert image_feature_summary["extractor"] == "roi-feature-nearest-centroid"
    assert image_feature_summary["chart_field_target_count"] == 1
    assert image_feature_summary["total_attempts"] == 3
    assert image_feature_summary["status_counts"]["empty_extraction"] == 3
    assert image_feature_summary["status_counts"]["skipped"] == 12

    template_extraction_rows = read_csv_rows(
        output_dir / "m3_chart_field_template_extraction.csv"
    )
    assert [row["status"] for row in template_extraction_rows[:6]] == [
        "skipped",
        "skipped",
        "skipped",
        "skipped",
        "skipped",
        "skipped",
    ]
    assert {row["status"] for row in template_extraction_rows[6:9]} == {
        "empty_extraction"
    }
    assert {
        row["failure_reason"] for row in template_extraction_rows[6:9]
    } == {"no_template_references"}
    assert template_extraction_rows[9]["failure_reason"] == "duplicate"
    assert template_extraction_rows[12]["failure_reason"] == "rejected_transition"

    template_summary = json.loads(
        (output_dir / "m3_chart_field_template_extraction_summary.json").read_text(
            encoding="utf-8"
        )
    )
    assert template_summary["target_boundary"] == "confirmed_result=true and duplicate=false"
    assert template_summary["extractor"] == "roi-template-nearest"
    assert template_summary["template_image_count"] == 0
    assert template_summary["chart_field_target_count"] == 1
    assert template_summary["total_attempts"] == 3
    assert template_summary["status_counts"]["empty_extraction"] == 3
    assert template_summary["status_counts"]["skipped"] == 12

    holdout_rows = read_csv_rows(output_dir / "m3_chart_field_template_holdout_extraction.csv")
    assert [row["status"] for row in holdout_rows[:6]] == [
        "skipped",
        "skipped",
        "skipped",
        "skipped",
        "skipped",
        "skipped",
    ]
    assert {row["status"] for row in holdout_rows[6:9]} == {"empty_extraction"}
    assert {row["failure_reason"] for row in holdout_rows[6:9]} == {
        "no_template_references"
    }
    assert {row["extractor"] for row in holdout_rows} == {"roi-template-holdout"}

    holdout_summary = json.loads(
        (output_dir / "m3_chart_field_template_holdout_extraction_summary.json").read_text(
            encoding="utf-8"
        )
    )
    assert holdout_summary["extractor"] == "roi-template-holdout"
    assert holdout_summary["reference_source_image_counts"] == {
        "chart_field_templates": 0,
        "confirmed_events": 0,
    }
    assert holdout_summary["status_counts"]["empty_extraction"] == 3

    event_rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["event_type"] for row in event_rows] == [
        "none",
        "none",
        "confirmed",
        "duplicate",
        "rejected_transition",
    ]


def test_m3_song_artist_ocr_report_uses_confirmed_events_boundary(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    frame_names = (
        "result_score123456_a.png",
        "result_score123456_b.png",
        "result_score123456_c.png",
        "result_score123456_d.png",
        "transition_countup_score999999.png",
    )
    for name in frame_names:
        write_test_image(tmp_path / "frames" / name)
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms,screen_type,expected_score,song_title,artist\n"
        "result_score123456_a.png,0,result,123456,,\n"
        "result_score123456_b.png,500,result,123456,,\n"
        "result_score123456_c.png,1000,result,123456,CHAOS,DE-SIRE retunes\n"
        "result_score123456_d.png,1500,result,123456,,\n"
        "transition_countup_score999999.png,2000,transition,999999,COUNTUP,NOPE\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        if row["organized_file"].startswith("transition_countup_"):
            return classification(
                row["organized_file"],
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            )
        return classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    def run_tesseract_synthetic(
        _binary: Image.Image,
        roi_name: str = "score_digits",
        _config: runner.TesseractConfig = runner.TESSERACT_CONFIG,
    ) -> tuple[str, str, str, str]:
        raw_by_roi = {
            "score_digits": "123456",
            "song_title": " CHAOS\n",
            "artist": "DE-SIRE retunes",
        }
        return raw_by_roi[roi_name], "tesseract", "ok", ""

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "run_tesseract", run_tesseract_synthetic)

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
                "--ocr-target",
                "confirmed-events",
                "--m3-song-artist-ocr",
                "--no-rois",
                "--chart-field-template-root",
                str(tmp_path / "missing_templates"),
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "m3_song_artist_ocr.csv")
    assert [(row["organized_file"], row["field_name"]) for row in rows] == [
        ("result_score123456_c.png", "song_title"),
        ("result_score123456_c.png", "artist"),
    ]
    assert rows[0]["ocr_raw"] == " CHAOS\n"
    assert rows[0]["pre_normalized_text"] == "CHAOS"
    assert rows[0]["expected_value"] == "CHAOS"
    assert rows[0]["failure_reason"] == ""
    assert rows[0]["roi_path"] == "rois/result_score123456_c/song_title.png"
    assert rows[1]["pre_normalized_text"] == "DE-SIRE retunes"
    assert rows[1]["expected_value"] == "DE-SIRE retunes"

    summary = json.loads(
        (output_dir / "m3_song_artist_ocr_summary.json").read_text(encoding="utf-8")
    )
    assert summary["target_boundary"] == "confirmed_result=true and duplicate=false"
    assert summary["extractor"] == "tesseract-text-raw"
    assert summary["target_count"] == 1
    assert summary["total_attempts"] == 2
    assert summary["skipped_counts"] == {
        "duplicate": 1,
        "rejected_transition": 1,
        "unconfirmed": 2,
        "non_result": 0,
    }
    assert summary["fields"]["song_title"]["ok_count"] == 1
    assert summary["fields"]["artist"]["ok_count"] == 1

    report = (output_dir / "m3_song_artist_ocr.md").read_text(encoding="utf-8")
    assert "マスタ照合、ファジーマッチ" in report
    assert "`song_title`" in report
    entry_failure_summary = json.loads(
        (output_dir / "m3_song_artist_ocr_entry_failures_summary.json").read_text(
            encoding="utf-8"
        )
    )
    assert entry_failure_summary["scope"] == (
        "M3-9 song_title/artist OCR entry failure representatives"
    )
    assert entry_failure_summary["failure_count"] == 0
    assert entry_failure_summary["fields"]["song_title"]["field_role"] == (
        "primary_song_identifier"
    )
    entry_failure_report = (
        output_dir / "m3_song_artist_ocr_entry_failures.md"
    ).read_text(encoding="utf-8")
    assert "OCR入口失敗代表" in entry_failure_report

    aggregate_rows = read_csv_rows(output_dir / "m3_save_candidate_summary.csv")
    assert [row["organized_file"] for row in aggregate_rows] == ["result_score123456_c.png"]
    assert aggregate_rows[0]["song_title_status"] == "ready"
    assert aggregate_rows[0]["artist_status"] == "ready"
    assert aggregate_rows[0]["play_style_status"] == "no_expected_value"
    assert aggregate_rows[0]["difficulty_status"] == "no_expected_value"
    assert aggregate_rows[0]["level_status"] == "no_expected_value"
    aggregate_summary = json.loads(
        (output_dir / "m3_save_candidate_summary.json").read_text(encoding="utf-8")
    )
    assert aggregate_summary["target_boundary"] == (
        "confirmed_result=true and duplicate=false"
    )
    assert aggregate_summary["target_count"] == 1
    aggregate_report = (output_dir / "m3_save_candidate_summary.md").read_text(
        encoding="utf-8"
    )
    assert "DB保存、マスタ照合" in aggregate_report

    blocker_summary = json.loads(
        (output_dir / "m3_save_candidate_blockers_summary.json").read_text(
            encoding="utf-8"
        )
    )
    assert blocker_summary["target_boundary"] == (
        "confirmed_result=true and duplicate=false"
    )
    assert blocker_summary["target_count"] == 1
    assert blocker_summary["blocker_candidate_count"] == 1
    assert blocker_summary["fields"]["song_title"]["blocker_count"] == 0
    assert blocker_summary["fields"]["play_style"]["blocker_count"] == 1
    blocker_report = (
        output_dir / "m3_save_candidate_blockers_summary.md"
    ).read_text(encoding="utf-8")
    assert "保存前に止める理由" in blocker_report
    assert "`rois/result_score123456_c/play_style.png`" in blocker_report


def test_m3_song_artist_ocr_failure_reason_keeps_engine_unavailable() -> None:
    assert (
        runner.m3_song_artist_ocr_failure_reason(
            status="engine_unavailable",
            pre_normalized_text="",
            expected_value="CHAOS",
        )
        == "engine_unavailable"
    )
    assert (
        runner.m3_song_artist_ocr_failure_reason(
            status="ok",
            pre_normalized_text="",
            expected_value="CHAOS",
        )
        == "empty_ocr"
    )
    assert (
        runner.m3_song_artist_ocr_failure_reason(
            status="ok",
            pre_normalized_text="CHAOS",
            expected_value="",
        )
        == "no_expected_value"
    )


def test_m3_chart_field_image_feature_extraction_uses_confirmed_events_boundary(
    tmp_path: Path,
) -> None:
    samples = [
        ("single_a.png", "SINGLE", "red"),
        ("single_b.png", "SINGLE", "red"),
        ("double_a.png", "DOUBLE", "green"),
        ("double_b.png", "DOUBLE", "green"),
        ("duplicate.png", "DOUBLE", "green"),
        ("transition_countup.png", "SINGLE", "red"),
    ]
    frames: list[runner.FrameInput] = []
    for name, play_style, color in samples:
        image_path = tmp_path / name
        write_chart_feature_image(image_path, {"play_style": color})
        frames.append(
            runner.FrameInput(
                row={
                    "organized_file": name,
                    "screen_type": "result",
                    "play_style": play_style,
                },
                image_path=image_path,
            )
        )
    events = [
        result_event("single_a.png", confirmed_result=True),
        result_event("single_b.png", confirmed_result=True),
        result_event("double_a.png", confirmed_result=True),
        result_event("double_b.png", confirmed_result=True),
        result_event(
            "duplicate.png",
            confirmed_result=True,
            duplicate=True,
            event_type="duplicate",
        ),
        result_event(
            "transition_countup.png",
            confirmed_result=False,
            event_type="rejected_transition",
            result_candidate=False,
        ),
    ]

    rows = runner.m3_chart_field_image_feature_extraction_rows(frames, events)

    play_style_rows = [row for row in rows if row["field_name"] == "play_style"]
    assert [row["status"] for row in play_style_rows] == [
        "match",
        "match",
        "match",
        "match",
        "skipped",
        "skipped",
    ]
    assert [row["extracted_value"] for row in play_style_rows[:4]] == [
        "SINGLE",
        "SINGLE",
        "DOUBLE",
        "DOUBLE",
    ]
    assert play_style_rows[4]["failure_reason"] == "duplicate"
    assert play_style_rows[5]["failure_reason"] == "rejected_transition"
    assert all(row["feature_green_ratio"] for row in play_style_rows[:4])

    summary = runner.summarize_m3_chart_field_image_feature_extraction(frames, events)
    assert summary["extractor"] == "roi-feature-nearest-centroid"
    assert summary["chart_field_target_count"] == 4
    assert summary["status_counts"] == {
        "match": 4,
        "mismatch": 0,
        "empty_extraction": 0,
        "no_expected_value": 8,
        "skipped": 6,
    }
    assert summary["fields"]["play_style"]["match_count"] == 4
    assert summary["fields"]["difficulty"]["no_expected_value_count"] == 4


def test_m3_chart_field_template_extraction_uses_confirmed_events_boundary(
    tmp_path: Path,
) -> None:
    template_root = tmp_path / "templates"
    write_chart_feature_image(
        template_root / "chart_field_template_001_single_basic_lv01.png",
        {"play_style": "red", "difficulty": "blue", "level": "yellow"},
    )
    write_chart_feature_image(
        template_root / "chart_field_template_002_double_expert_lv02.png",
        {"play_style": "green", "difficulty": "purple", "level": "white"},
    )

    samples = [
        (
            "single_target.png",
            {"play_style": "red", "difficulty": "blue", "level": "yellow"},
            {"play_style": "SINGLE", "difficulty": "BASIC", "level": "1"},
        ),
        (
            "double_target.png",
            {"play_style": "green", "difficulty": "purple", "level": "white"},
            {"play_style": "DOUBLE", "difficulty": "EXPERT", "level": "2"},
        ),
        (
            "duplicate.png",
            {"play_style": "green", "difficulty": "purple", "level": "white"},
            {"play_style": "DOUBLE", "difficulty": "EXPERT", "level": "2"},
        ),
        (
            "transition_countup.png",
            {"play_style": "red", "difficulty": "blue", "level": "yellow"},
            {"play_style": "SINGLE", "difficulty": "BASIC", "level": "1"},
        ),
    ]
    frames: list[runner.FrameInput] = []
    for name, colors, expected in samples:
        image_path = tmp_path / name
        write_chart_feature_image(image_path, colors)
        frames.append(
            runner.FrameInput(
                row={
                    "organized_file": name,
                    "screen_type": "result",
                    **expected,
                },
                image_path=image_path,
            )
        )
    events = [
        result_event("single_target.png", confirmed_result=True),
        result_event("double_target.png", confirmed_result=True),
        result_event(
            "duplicate.png",
            confirmed_result=True,
            duplicate=True,
            event_type="duplicate",
        ),
        result_event(
            "transition_countup.png",
            confirmed_result=False,
            event_type="rejected_transition",
            result_candidate=False,
        ),
    ]

    rows = runner.m3_chart_field_template_extraction_rows(frames, events, template_root)

    assert [row["status"] for row in rows[:6]] == ["match"] * 6
    assert [row["status"] for row in rows[6:]] == ["skipped"] * 6
    assert rows[0]["extractor"] == "roi-template-nearest"
    assert rows[0]["extracted_value"] == "SINGLE"
    assert rows[1]["extracted_value"] == "BASIC"
    assert rows[2]["extracted_value"] == "1"
    assert rows[3]["extracted_value"] == "DOUBLE"
    assert rows[4]["extracted_value"] == "EXPERT"
    assert rows[5]["extracted_value"] == "2"
    assert rows[6]["failure_reason"] == "duplicate"
    assert rows[9]["failure_reason"] == "rejected_transition"
    assert rows[0]["nearest_source_type"] == "chart_field_templates"
    assert rows[0]["template_reference_count"] == "3"
    assert rows[0]["expected_template_reference_count"] == "1"
    assert rows[0]["nearest_template_path"].endswith(
        "chart_field_template_001_single_basic_lv01.png"
    )

    summary = runner.summarize_m3_chart_field_template_extraction(
        frames,
        events,
        template_root,
    )
    assert summary["extractor"] == "roi-template-nearest"
    assert summary["template_image_count"] == 2
    assert summary["field_vector_modes"]["difficulty"] == "foreground-color-pattern"
    assert summary["template_reference_counts"] == {
        "play_style": 3,
        "difficulty": 3,
        "level": 3,
    }
    assert summary["reference_source_image_counts"] == {
        "chart_field_templates": 2,
        "confirmed_events": 2,
    }
    assert summary["reference_value_counts_by_source"]["difficulty"] == {
        "chart_field_templates": {"BASIC": 1, "EXPERT": 1},
        "confirmed_events": {"BASIC": 1, "EXPERT": 1},
        "combined": {"BASIC": 2, "EXPERT": 2},
    }
    assert summary["template_value_counts"]["difficulty"] == {"BASIC": 2, "EXPERT": 2}
    assert summary["chart_field_target_count"] == 2
    assert summary["status_counts"] == {
        "match": 6,
        "mismatch": 0,
        "empty_extraction": 0,
        "no_expected_value": 0,
        "skipped": 6,
    }

    holdout_rows = runner.m3_chart_field_template_holdout_extraction_rows(
        frames,
        events,
        template_root,
    )
    assert [row["status"] for row in holdout_rows[:6]] == ["match"] * 6
    assert {row["extractor"] for row in holdout_rows} == {"roi-template-holdout"}
    assert holdout_rows[0]["nearest_source_type"] == "chart_field_templates"
    assert holdout_rows[0]["template_reference_count"] == "2"

    holdout_summary = runner.summarize_m3_chart_field_template_extraction_rows(
        holdout_rows,
        frames,
        events,
        template_root,
        include_result_references=False,
        extractor_method=runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        reference_mode="templates only",
    )
    assert holdout_summary["extractor"] == "roi-template-holdout"
    assert holdout_summary["reference_mode"] == "templates only"
    assert holdout_summary["reference_source_image_counts"] == {
        "chart_field_templates": 2,
        "confirmed_events": 0,
    }
    assert holdout_summary["template_reference_counts"] == {
        "play_style": 2,
        "difficulty": 2,
        "level": 2,
    }


def test_m3_chart_field_template_extraction_uses_result_references_leave_one_out(
    tmp_path: Path,
) -> None:
    samples = [
        ("single_a.png", "SINGLE", "red"),
        ("single_b.png", "SINGLE", "red"),
        ("double.png", "DOUBLE", "green"),
    ]
    frames: list[runner.FrameInput] = []
    for name, play_style, color in samples:
        image_path = tmp_path / name
        write_chart_feature_image(image_path, {"play_style": color})
        frames.append(
            runner.FrameInput(
                row={
                    "organized_file": name,
                    "screen_type": "result",
                    "play_style": play_style,
                },
                image_path=image_path,
            )
        )
    events = [result_event(name, confirmed_result=True) for name, _, _ in samples]

    rows = runner.m3_chart_field_template_extraction_rows(
        frames,
        events,
        tmp_path / "missing_templates",
    )

    play_style_rows = [row for row in rows if row["field_name"] == "play_style"]
    assert [row["status"] for row in play_style_rows] == [
        "match",
        "match",
        "mismatch",
    ]
    assert [row["nearest_source_type"] for row in play_style_rows] == [
        "confirmed_events",
        "confirmed_events",
        "confirmed_events",
    ]
    assert [row["expected_template_reference_count"] for row in play_style_rows] == [
        "1",
        "1",
        "0",
    ]
    assert play_style_rows[2]["failure_reason"] == "missing_expected_template_reference"

    summary = runner.summarize_m3_chart_field_template_extraction_rows(
        rows,
        frames,
        events,
        tmp_path / "missing_templates",
    )
    assert summary["reference_source_image_counts"] == {
        "chart_field_templates": 0,
        "confirmed_events": 3,
    }
    assert summary["template_value_counts"]["play_style"] == {"DOUBLE": 1, "SINGLE": 2}

    holdout_rows = runner.m3_chart_field_template_holdout_extraction_rows(
        frames,
        events,
        tmp_path / "missing_templates",
    )
    play_style_holdout_rows = [row for row in holdout_rows if row["field_name"] == "play_style"]
    assert [row["status"] for row in play_style_holdout_rows] == [
        "empty_extraction",
        "empty_extraction",
        "empty_extraction",
    ]
    assert {row["failure_reason"] for row in play_style_holdout_rows} == {
        "no_template_references"
    }

    holdout_summary = runner.summarize_m3_chart_field_template_extraction_rows(
        holdout_rows,
        frames,
        events,
        tmp_path / "missing_templates",
        include_result_references=False,
        extractor_method=runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        reference_mode="templates only",
    )
    assert holdout_summary["reference_source_image_counts"] == {
        "chart_field_templates": 0,
        "confirmed_events": 0,
    }
    assert holdout_summary["template_value_counts"]["play_style"] == {}


def test_m3_chart_field_adoption_candidates_use_holdout_failure_vocabulary(
    tmp_path: Path,
) -> None:
    rows = [
        {
            "field_name": "play_style",
            "chart_field_target": "True",
            "status": "match",
            "failure_reason": "",
            "expected_value": "SINGLE",
        },
        {
            "field_name": "play_style",
            "chart_field_target": "True",
            "status": "match",
            "failure_reason": "",
            "expected_value": "DOUBLE",
        },
        {
            "field_name": "difficulty",
            "chart_field_target": "True",
            "status": "mismatch",
            "failure_reason": "missing_expected_template_reference",
            "expected_value": "DIFFICULT",
        },
        {
            "field_name": "level",
            "chart_field_target": "True",
            "status": "mismatch",
            "failure_reason": "missing_expected_template_reference",
            "expected_value": "12",
        },
        {
            "field_name": "level",
            "chart_field_target": "True",
            "status": "mismatch",
            "failure_reason": "missing_expected_template_reference",
            "expected_value": "6",
        },
        {
            "field_name": "level",
            "chart_field_target": "False",
            "status": "skipped",
            "failure_reason": "duplicate",
            "expected_value": "6",
        },
    ]

    summary = runner.summarize_m3_chart_field_adoption_candidates(rows)

    assert summary["candidate_evidence_extractor"] == "roi-template-holdout"
    fields = summary["fields"]
    assert fields["play_style"]["adoption_readiness"] == "adoption_candidate"
    assert fields["play_style"]["recommended_extractor"] == "roi-template-holdout"
    assert fields["difficulty"]["adoption_readiness"] == "needs_template_references"
    assert fields["difficulty"]["save_failure_reason_counts"] == {"missing_reference": 1}
    assert fields["difficulty"]["missing_reference_values"] == ["DIFFICULT"]
    assert fields["level"]["adoption_readiness"] == "needs_template_references"
    assert fields["level"]["missing_reference_values"] == ["6", "12"]
    assert fields["level"]["save_failure_reason_counts"] == {"missing_reference": 2}

    output_path = tmp_path / "m3_chart_field_adoption_candidates.md"
    runner.write_m3_chart_field_adoption_candidates_report(output_path, summary)
    report = output_path.read_text(encoding="utf-8")
    assert "`play_style` | `adoption_candidate` | `roi-template-holdout`" in report
    assert "`difficulty` | `needs_template_references` | ``" in report
    assert "`missing_reference`" in report


def test_m3_save_candidate_summary_uses_save_status_vocabulary(tmp_path: Path) -> None:
    frames = [
        runner.FrameInput(
            row={
                "organized_file": "result_score123456_c.png",
                "screen_type": "result",
            },
            image_path=tmp_path / "result_score123456_c.png",
            timestamp_ms=1000,
        )
    ]
    events = [
        runner.ResultEvent(
            frame_index=2,
            organized_file="result_score123456_c.png",
            screen_type="result",
            result_candidate=True,
            result_shape_candidate=True,
            confirmed_result=True,
            event_type="confirmed",
            duplicate=False,
            duplicate_key="score:123456",
            timestamp_ms=1000,
            candidate_duration_ms=1000,
            confirmation_mode="time",
            reason="test",
        )
    ]
    song_artist_results = [
        runner.M3SongArtistOcrResult(
            organized_file="result_score123456_c.png",
            screen_type="result",
            event_type="confirmed",
            confirmed_result=True,
            duplicate=False,
            field_name="song_title",
            extractor=runner.M3_SONG_ARTIST_OCR_METHOD,
            ocr_raw=" CHAOS\n",
            pre_normalized_text="CHAOS",
            expected_value="CHAOS",
            engine="tesseract",
            status="ok",
            error="",
            failure_reason="",
            roi_path="rois/result_score123456_c/song_title.png",
            original_path="",
            enlarged_path="",
            binary_path="",
        ),
        runner.M3SongArtistOcrResult(
            organized_file="result_score123456_c.png",
            screen_type="result",
            event_type="confirmed",
            confirmed_result=True,
            duplicate=False,
            field_name="artist",
            extractor=runner.M3_SONG_ARTIST_OCR_METHOD,
            ocr_raw="",
            pre_normalized_text="",
            expected_value="DE-SIRE",
            engine="tesseract",
            status="ok",
            error="",
            failure_reason="empty_ocr",
            roi_path="rois/result_score123456_c/artist.png",
            original_path="",
            enlarged_path="",
            binary_path="",
        ),
    ]
    holdout_rows = [
        {
            "organized_file": "result_score123456_c.png",
            "field_name": "play_style",
            "chart_field_target": "True",
            "status": "match",
            "failure_reason": "",
            "expected_value": "SINGLE",
            "extracted_value": "SINGLE",
            "extractor": runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        },
        {
            "organized_file": "result_score123456_c.png",
            "field_name": "difficulty",
            "chart_field_target": "True",
            "status": "mismatch",
            "failure_reason": "missing_expected_template_reference",
            "expected_value": "DIFFICULT",
            "extracted_value": "BASIC",
            "extractor": runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        },
        {
            "organized_file": "result_score123456_c.png",
            "field_name": "level",
            "chart_field_target": "True",
            "status": "mismatch",
            "failure_reason": "mismatch",
            "expected_value": "12",
            "extracted_value": "11",
            "extractor": runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        },
    ]
    adoption_summary = runner.summarize_m3_chart_field_adoption_candidates(holdout_rows)

    rows = runner.m3_save_candidate_rows(
        frames,
        events,
        holdout_rows,
        adoption_summary,
        song_artist_results,
    )

    assert len(rows) == 1
    row = rows[0]
    assert row["song_title_status"] == "ready"
    assert row["artist_status"] == "empty_ocr"
    assert row["play_style_status"] == "ready"
    assert row["difficulty_status"] == "missing_reference"
    assert row["level_status"] == "not_adopted"
    assert row["blocking_fields"] == "artist difficulty level"

    summary = runner.summarize_m3_save_candidates(rows)
    assert summary["fields"]["song_title"]["status_counts"] == {"ready": 1}
    assert summary["fields"]["artist"]["failure_reason_counts"] == {"empty_ocr": 1}
    assert summary["fields"]["difficulty"]["status_counts"] == {"missing_reference": 1}
    assert summary["fields"]["level"]["status_counts"] == {"not_adopted": 1}
    assert summary["overall_status_counts"] == {"not_ready": 1}

    output_path = tmp_path / "m3_save_candidate_summary.md"
    runner.write_m3_save_candidate_summary_report(output_path, rows, summary)
    report = output_path.read_text(encoding="utf-8")
    assert "`play_style`" in report
    assert "DB保存可能やマスタ照合成功を意味しません" in report

    blocker_summary = runner.summarize_m3_save_candidate_blockers(rows)
    assert blocker_summary["scope"] == "M3-6 save candidate blocker representative review"
    assert blocker_summary["target_count"] == 1
    assert blocker_summary["blocker_candidate_count"] == 1
    assert blocker_summary["fields"]["song_title"]["blocker_count"] == 0
    artist_groups = blocker_summary["fields"]["artist"]["groups"]
    assert artist_groups[0]["status"] == "empty_ocr"
    assert artist_groups[0]["failure_reason"] == "empty_ocr"
    assert artist_groups[0]["representatives"][0]["roi_path"] == (
        "rois/result_score123456_c/artist.png"
    )
    difficulty_groups = blocker_summary["fields"]["difficulty"]["groups"]
    assert difficulty_groups[0]["status"] == "missing_reference"
    assert difficulty_groups[0]["representatives"][0]["expected_value"] == "DIFFICULT"

    blocker_path = tmp_path / "m3_save_candidate_blockers_summary.md"
    runner.write_m3_save_candidate_blockers_report(blocker_path, blocker_summary)
    blocker_report = blocker_path.read_text(encoding="utf-8")
    assert "# M3 Save Candidate Blockers" in blocker_report
    assert "`artist`" in blocker_report
    assert "`missing_reference`" in blocker_report

    resolution_summary = runner.summarize_m3_save_candidate_blocker_resolution(rows)
    assert resolution_summary["scope"] == "M3-7 save candidate blocker resolution order"
    assert resolution_summary["target_count"] == 1
    resolution_items = resolution_summary["resolution_order"]
    assert [item["field"] for item in resolution_items] == [
        "difficulty",
        "artist",
        "level",
    ]
    assert resolution_items[0]["action"] == "add_template_references"
    assert resolution_items[0]["required_reference_label_counts"] == {"DIFFICULT": 1}
    assert resolution_items[1]["action"] == "inspect_ocr_entry_failures"
    assert resolution_items[1]["representatives"][0]["roi_path"] == (
        "rois/result_score123456_c/artist.png"
    )

    resolution_path = tmp_path / "m3_save_candidate_blocker_resolution_plan.md"
    runner.write_m3_save_candidate_blocker_resolution_report(
        resolution_path,
        resolution_summary,
    )
    resolution_report = resolution_path.read_text(encoding="utf-8")
    assert "# M3 Save Candidate Blocker Resolution Order" in resolution_report
    assert "`add_template_references`" in resolution_report
    assert "`{\"DIFFICULT\": 1}`" in resolution_report
    assert "曲名正規化やマスタ照合の失敗判定ではありません" in resolution_report


def test_m3_chart_field_ready_fields_leave_only_song_artist_ocr_blockers(
    tmp_path: Path,
) -> None:
    frames = [
        runner.FrameInput(
            row={
                "organized_file": "result_score765432_c.png",
                "screen_type": "result",
            },
            image_path=tmp_path / "result_score765432_c.png",
            timestamp_ms=1000,
        )
    ]
    events = [
        runner.ResultEvent(
            frame_index=2,
            organized_file="result_score765432_c.png",
            screen_type="result",
            result_candidate=True,
            result_shape_candidate=True,
            confirmed_result=True,
            event_type="confirmed",
            duplicate=False,
            duplicate_key="score:765432",
            timestamp_ms=1000,
            candidate_duration_ms=1000,
            confirmation_mode="time",
            reason="test",
        )
    ]
    holdout_rows = [
        {
            "organized_file": "result_score765432_c.png",
            "field_name": "play_style",
            "chart_field_target": "True",
            "status": "match",
            "failure_reason": "",
            "expected_value": "SINGLE",
            "extracted_value": "SINGLE",
            "extractor": runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        },
        {
            "organized_file": "result_score765432_c.png",
            "field_name": "difficulty",
            "chart_field_target": "True",
            "status": "match",
            "failure_reason": "",
            "expected_value": "EXPERT",
            "extracted_value": "EXPERT",
            "extractor": runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        },
        {
            "organized_file": "result_score765432_c.png",
            "field_name": "level",
            "chart_field_target": "True",
            "status": "match",
            "failure_reason": "",
            "expected_value": "16",
            "extracted_value": "16",
            "extractor": runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        },
    ]
    song_artist_results = [
        runner.M3SongArtistOcrResult(
            organized_file="result_score765432_c.png",
            screen_type="result",
            event_type="confirmed",
            confirmed_result=True,
            duplicate=False,
            field_name="song_title",
            extractor=runner.M3_SONG_ARTIST_OCR_METHOD,
            ocr_raw="",
            pre_normalized_text="",
            expected_value="PARANOiA",
            engine="tesseract",
            status="ok",
            error="",
            failure_reason="empty_ocr",
            roi_path="rois/result_score765432_c/song_title.png",
            original_path="",
            enlarged_path="",
            binary_path="",
        ),
        runner.M3SongArtistOcrResult(
            organized_file="result_score765432_c.png",
            screen_type="result",
            event_type="confirmed",
            confirmed_result=True,
            duplicate=False,
            field_name="artist",
            extractor=runner.M3_SONG_ARTIST_OCR_METHOD,
            ocr_raw="",
            pre_normalized_text="",
            expected_value="180",
            engine="tesseract",
            status="ok",
            error="",
            failure_reason="empty_ocr",
            roi_path="rois/result_score765432_c/artist.png",
            original_path="",
            enlarged_path="",
            binary_path="",
        ),
    ]

    adoption_summary = runner.summarize_m3_chart_field_adoption_candidates(holdout_rows)
    for field_name in ("play_style", "difficulty", "level"):
        field = adoption_summary["fields"][field_name]
        assert field["adoption_readiness"] == "adoption_candidate"
        assert field["recommended_extractor"] == (
            runner.M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD
        )

    rows = runner.m3_save_candidate_rows(
        frames,
        events,
        holdout_rows,
        adoption_summary,
        song_artist_results,
    )

    assert len(rows) == 1
    row = rows[0]
    assert row["play_style_status"] == "ready"
    assert row["difficulty_status"] == "ready"
    assert row["level_status"] == "ready"
    assert row["song_title_status"] == "empty_ocr"
    assert row["artist_status"] == "empty_ocr"
    assert row["blocking_fields"] == "song_title artist"

    blocker_summary = runner.summarize_m3_save_candidate_blockers(rows)
    assert blocker_summary["fields"]["play_style"]["blocker_count"] == 0
    assert blocker_summary["fields"]["difficulty"]["blocker_count"] == 0
    assert blocker_summary["fields"]["level"]["blocker_count"] == 0
    assert blocker_summary["fields"]["song_title"]["blocker_count"] == 1
    assert blocker_summary["fields"]["artist"]["blocker_count"] == 1

    resolution_summary = runner.summarize_m3_save_candidate_blocker_resolution(rows)
    resolution_items = resolution_summary["resolution_order"]
    assert [item["field"] for item in resolution_items] == ["song_title", "artist"]
    assert {item["action"] for item in resolution_items} == {"inspect_ocr_entry_failures"}
    assert all(item["required_reference_label_counts"] == {} for item in resolution_items)

    entry_failure_summary = runner.summarize_m3_song_artist_ocr_entry_failures(
        song_artist_results
    )
    assert entry_failure_summary["scope"] == (
        "M3-9 song_title/artist OCR entry failure representatives"
    )
    assert entry_failure_summary["failure_count"] == 2
    assert entry_failure_summary["affected_candidate_count"] == 1
    assert entry_failure_summary["fields"]["song_title"]["field_role"] == (
        "primary_song_identifier"
    )
    assert entry_failure_summary["fields"]["artist"]["field_role"] == (
        "auxiliary_clipped_reference"
    )
    assert entry_failure_summary["fields"]["song_title"]["failure_reason_counts"] == {
        "empty_ocr": 1
    }
    assert entry_failure_summary["fields"]["artist"]["failure_reason_counts"] == {
        "empty_ocr": 1
    }

    entry_failure_path = tmp_path / "m3_song_artist_ocr_entry_failures.md"
    runner.write_m3_song_artist_ocr_entry_failures_report(
        entry_failure_path,
        entry_failure_summary,
    )
    entry_failure_report = entry_failure_path.read_text(encoding="utf-8")
    assert "# M3 Song / Artist OCR Entry Failures" in entry_failure_report
    assert "`primary_song_identifier`" in entry_failure_report
    assert "`auxiliary_clipped_reference`" in entry_failure_report
    assert "曲名正規化、ファジーマッチ" in entry_failure_report


def test_m3_chart_field_template_diagnostics_reports_review_candidates(
    tmp_path: Path,
) -> None:
    diagnostic_rows = [
        {
            "organized_file": "result_056.png",
            "field_name": "difficulty",
            "expected_value": "DIFFICULT",
            "extracted_value": "EXPERT",
            "status": "mismatch",
            "failure_reason": "mismatch",
            "nearest_distance": "0.000000",
            "nearest_source_type": "chart_field_templates",
            "nearest_template_path": "templates/chart_field_template_126_single_expert_lv15.png",
            "roi_path": "rois/result_056/difficulty.png",
        },
        {
            "organized_file": "result_073.png",
            "field_name": "difficulty",
            "expected_value": "EXPERT",
            "extracted_value": "DIFFICULT",
            "status": "mismatch",
            "failure_reason": "mismatch",
            "nearest_distance": "0.000000",
            "nearest_source_type": "confirmed_events",
            "nearest_template_path": "organized/result/result_044_sp_difficult_lv12.png",
            "roi_path": "rois/result_073/difficulty.png",
        },
        {
            "organized_file": "result_level.png",
            "field_name": "level",
            "expected_value": "12",
            "extracted_value": "12",
            "status": "match",
            "failure_reason": "",
            "nearest_distance": "0.010000",
            "nearest_source_type": "confirmed_events",
            "nearest_template_path": "organized/result/result_level_ref.png",
            "roi_path": "rois/result_level/level.png",
        },
    ]
    for field_name in runner.M3_CHART_FIELD_FIELDS:
        diagnostic_rows.append(
            {
                "organized_file": f"skipped_{field_name}.png",
                "field_name": field_name,
                "expected_value": "",
                "extracted_value": "",
                "status": "skipped",
                "failure_reason": "unconfirmed",
                "nearest_distance": "",
                "nearest_source_type": "",
                "nearest_template_path": "",
                "roi_path": f"rois/skipped_{field_name}/{field_name}.png",
            }
        )

    output_path = tmp_path / "template_diagnostics.md"
    runner.write_m3_chart_field_template_diagnostics_rows(output_path, diagnostic_rows)

    report = output_path.read_text(encoding="utf-8")
    assert "| `difficulty` | `DIFFICULT` | `EXPERT` | `mismatch` | 1 |" in report
    assert "| `difficulty` | `EXPERT` | `DIFFICULT` | `mismatch` | 1 |" in report
    assert "`result_056.png`" in report
    assert "`chart_field_templates`" in report
    assert "`confirmed_events`" in report
    assert "Difficulty Expected Review Candidates" in report
    assert "metadata / ファイル名由来期待値の食い違い候補" in report
    assert "採用済みテンプレート照合、マスタ照合の成功扱いにはしません" in report


def test_m3_chart_field_image_feature_diagnostics_reports_mismatches(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    diagnostic_rows = [
        {
            "organized_file": "sample_play_style.png",
            "field_name": "play_style",
            "expected_value": "DOUBLE",
            "extracted_value": "SINGLE",
            "status": "mismatch",
            "nearest_distance": "0.120000",
            "roi_path": "rois/sample_play_style/play_style.png",
        },
        {
            "organized_file": "sample_difficulty_a.png",
            "field_name": "difficulty",
            "expected_value": "DIFFICULT",
            "extracted_value": "CHALLENGE",
            "status": "mismatch",
            "nearest_distance": "0.230000",
            "roi_path": "rois/sample_difficulty_a/difficulty.png",
        },
        {
            "organized_file": "sample_difficulty_b.png",
            "field_name": "difficulty",
            "expected_value": "DIFFICULT",
            "extracted_value": "CHALLENGE",
            "status": "mismatch",
            "nearest_distance": "0.240000",
            "roi_path": "rois/sample_difficulty_b/difficulty.png",
        },
        {
            "organized_file": "sample_level_match.png",
            "field_name": "level",
            "expected_value": "12",
            "extracted_value": "12",
            "status": "match",
            "nearest_distance": "0.010000",
            "roi_path": "rois/sample_level_match/level.png",
        },
        {
            "organized_file": "sample_level_mismatch_a.png",
            "field_name": "level",
            "expected_value": "13",
            "extracted_value": "14",
            "status": "mismatch",
            "nearest_distance": "0.310000",
            "roi_path": "rois/sample_level_mismatch_a/level.png",
        },
        {
            "organized_file": "sample_level_mismatch_b.png",
            "field_name": "level",
            "expected_value": "13",
            "extracted_value": "16",
            "status": "mismatch",
            "nearest_distance": "0.320000",
            "roi_path": "rois/sample_level_mismatch_b/level.png",
        },
    ]
    for field_name in runner.M3_CHART_FIELD_FIELDS:
        diagnostic_rows.append(
            {
                "organized_file": f"skipped_{field_name}.png",
                "field_name": field_name,
                "expected_value": "",
                "extracted_value": "",
                "status": "skipped",
                "nearest_distance": "",
                "roi_path": f"rois/skipped_{field_name}/{field_name}.png",
            }
        )

    monkeypatch.setattr(
        runner,
        "m3_chart_field_image_feature_extraction_rows",
        lambda _frames, _events: diagnostic_rows,
    )

    output_path = tmp_path / "diagnostics.md"
    runner.write_m3_chart_field_image_feature_diagnostics(output_path, [], [])

    report = output_path.read_text(encoding="utf-8")
    assert "| `difficulty` | `DIFFICULT` | `CHALLENGE` | 2 |" in report
    assert "| `play_style` | `DOUBLE` | `SINGLE` | 1 |" in report
    assert "`sample_level_mismatch_a.png`" in report
    assert "`level` is" not in report
    assert "`level` は match が半数未満" in report
    assert "OCR、テンプレート照合、マスタ照合の成功扱いにはしません" in report


def test_m7a_digit_recognition_writes_confirmed_events_report(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    template_root = tmp_path / "digit_templates"
    write_digit_templates(template_root)
    frame_names = (
        "result_score123456_a.png",
        "result_score123456_b.png",
        "result_score123456_c.png",
    )
    for name in frame_names:
        write_score_digit_image(tmp_path / "frames" / name, "123456")
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms,screen_type,expected_score\n"
        "result_score123456_a.png,0,result,123456\n"
        "result_score123456_b.png,500,result,123456\n"
        "result_score123456_c.png,1000,result,123456\n",
        encoding="utf-8",
    )

    monkeypatch.setattr(
        runner,
        "classify",
        lambda _image, row: classification(
            row["organized_file"],
            result_candidate=True,
            screen_type="result",
        ),
    )
    monkeypatch.setattr(
        runner,
        "run_tesseract",
        lambda _binary, _roi_name="score_digits": ("123456", "tesseract", "ok", ""),
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
                "--ocr-target",
                "confirmed-events",
                "--m7a-digit-recognition",
                "--m7a-digit-template-root",
                str(template_root),
                "--no-rois",
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "m7a_digit_recognition.csv")
    assert len(rows) == 1
    assert rows[0]["organized_file"] == "result_score123456_c.png"
    assert rows[0]["roi_name"] == "score_digits"
    assert rows[0]["recognized_digits"] == "123456"
    assert rows[0]["expected_value"] == "123456"
    assert rows[0]["match"] == "True"
    assert rows[0]["status"] == "recognized"
    assert rows[0]["failure_reason"] == ""
    assert rows[0]["segment_count"] == "6"

    summary = json.loads(
        (output_dir / "m7a_digit_recognition_summary.json").read_text(encoding="utf-8")
    )
    assert summary["target_boundary"] == "confirmed_result=true and duplicate=false"
    assert summary["target_count"] == 1
    assert summary["total_attempts"] == 1
    assert summary["status_counts"] == {"recognized": 1}
    assert summary["match_count"] == 1
    assert summary["tesseract_comparison"] == {
        "available_attempts": 1,
        "same_normalized_count": 1,
        "different_normalized_count": 0,
        "unavailable_count": 0,
    }
    assert summary["by_roi"]["score_digits"]["status_counts"] == {"recognized": 1}

    report = (output_dir / "m7a_digit_recognition_report.md").read_text(encoding="utf-8")
    assert "status vocabulary" in report
    assert "DB保存OK/NG判定ではありません" in report


def test_m7a_digit_recognition_reports_missing_reference(tmp_path: Path) -> None:
    image = Image.new("RGB", (1280, 720), "black")
    frame = runner.FrameInput(
        row={
            "organized_file": "result_score123456.png",
            "screen_type": "result",
            "expected_score": "123456",
        },
        image_path=tmp_path / "result_score123456.png",
    )
    event = result_event("result_score123456.png", confirmed_result=True)

    result = runner.process_m7a_digit_roi(image, frame, event, "score_digits", [])

    assert result.status == "missing_reference"
    assert result.failure_reason == "missing_digit_templates=0123456789"
    assert result.match is None


def test_m7a_digit_recognition_reports_failed_segmentation(tmp_path: Path) -> None:
    template_root = tmp_path / "digit_templates"
    write_digit_templates(template_root)
    templates = runner.load_m7a_digit_templates(template_root, "score_digits")
    image = Image.new("RGB", (1280, 720), "black")
    frame = runner.FrameInput(
        row={
            "organized_file": "blank_result.png",
            "screen_type": "result",
            "expected_score": "123456",
        },
        image_path=tmp_path / "blank_result.png",
    )
    event = result_event("blank_result.png", confirmed_result=True)

    result = runner.process_m7a_digit_roi(image, frame, event, "score_digits", templates)

    assert result.status == "failed_segmentation"
    assert result.failure_reason == "no_digit_segments"
    assert result.segment_count == 0


def test_m7a_digit_recognition_ignores_score_digit_comma(tmp_path: Path) -> None:
    template_root = tmp_path / "digit_templates"
    write_digit_templates(template_root)
    templates = runner.load_m7a_digit_templates(template_root, "score_digits")
    image_path = tmp_path / "result_score935730.png"
    write_score_digit_image_with_comma(image_path, "935730")
    frame = runner.FrameInput(
        row={
            "organized_file": "result_score935730.png",
            "screen_type": "result",
            "expected_score": "935730",
        },
        image_path=image_path,
    )
    event = result_event("result_score935730.png", confirmed_result=True)

    with Image.open(image_path) as image:
        result = runner.process_m7a_digit_roi(
            image.convert("RGB"),
            frame,
            event,
            "score_digits",
            templates,
        )

    assert result.segment_count == 6
    assert result.recognized_digits == "935730"
    assert result.status == "recognized"
    assert result.match is True


def test_m7a_digit_recognition_supports_score_range_boundaries(tmp_path: Path) -> None:
    template_root = tmp_path / "digit_templates"
    write_digit_templates(template_root)
    templates = runner.load_m7a_digit_templates(template_root, "score_digits")
    cases = [
        ("result_score000000.png", "0", "0"),
        ("result_score009870.png", "9870", "9,870"),
        ("result_score054321.png", "54321", "54,321"),
        ("result_score935730.png", "935730", "935,730"),
        ("result_score1000000.png", "1000000", "1,000,000"),
    ]

    for file_name, expected_score, display_text in cases:
        image_path = tmp_path / file_name
        write_score_digit_display_image(image_path, display_text)
        frame = runner.FrameInput(
            row={
                "organized_file": file_name,
                "screen_type": "result",
                "expected_score": expected_score,
            },
            image_path=image_path,
        )
        event = result_event(file_name, confirmed_result=True)

        with Image.open(image_path) as image:
            result = runner.process_m7a_digit_roi(
                image.convert("RGB"),
                frame,
                event,
                "score_digits",
                templates,
            )

        assert result.recognized_digits == expected_score
        assert result.status == "recognized"
        assert result.match is True


def test_m7a_digit_recognition_keeps_recognized_candidate_without_expected_value(
    tmp_path: Path,
) -> None:
    template_root = tmp_path / "digit_templates"
    write_digit_templates(template_root)
    templates = runner.load_m7a_digit_templates(template_root, "score_digits")
    image_path = tmp_path / "result_no_expected.png"
    write_score_digit_image(image_path, "789")
    frame = runner.FrameInput(
        row={
            "organized_file": "result_no_expected.png",
            "screen_type": "result",
        },
        image_path=image_path,
    )
    event = result_event("result_no_expected.png", confirmed_result=True)
    with Image.open(image_path) as image:
        result = runner.process_m7a_digit_roi(
            image.convert("RGB"),
            frame,
            event,
            "score_digits",
            templates,
        )

    assert result.status == "not_evaluated"
    assert result.recognized_digits == "789"
    assert result.failure_reason == "no_expected_value"
    assert result.match is None


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


def test_result_candidate_ocr_target_keeps_legacy_result_candidate_selection(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in ("a.png", "b.png", "c.png", "d.png"):
        write_test_image(tmp_path / "screenshots" / name)
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type\n"
        "a.png,result\n"
        "b.png,result\n"
        "c.png,transition\n"
        "d.png,menu_setup\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        if row["organized_file"] == "c.png":
            return classification(
                "transition_countup_score999999_c.png",
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            )
        return classification(
            f"organized/result_score123456_{row['organized_file']}",
            result_candidate=row["organized_file"] in {"a.png", "b.png"},
            screen_type=row["screen_type"],
        )

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "run_tesseract", stub_tesseract)

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
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert [row["organized_file"] for row in rows] == [
        "a.png",
        "b.png",
    ]
    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["ocr_target_mode"] == "result-candidate"
    assert summary["total_ocr_attempts"] == 2
    assert summary["ok_count"] == 2
    assert summary["skipped_duplicate_count"] == 0
    assert summary["skipped_unconfirmed_count"] == 0


def test_ocr_rois_all_writes_roi_summary_and_failure_reasons(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    write_test_image(tmp_path / "screenshots" / "result_score111111_a.png")
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type,expected_score,max_combo,expected_marvelous,great,good\n"
        "result_score111111_a.png,result,123456,321,50,4,7\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        return classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    def run_tesseract_synthetic(
        _binary: Image.Image,
        roi_name: str = "score_digits",
    ) -> tuple[str, str, str, str]:
        raw_by_roi = {
            "score_digits": "123456",
            "max_combo": "321",
            "marvelous": "51",
            "perfect": "999",
            "great": "5",
            "good": "",
            "miss": "2",
            "ex_score": "777",
        }
        return raw_by_roi[roi_name], "tesseract", "ok", ""

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "run_tesseract", run_tesseract_synthetic)

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
                "--ocr-rois",
                "all",
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert [row["roi_name"] for row in rows] == list(runner.OCR_ROIS)
    assert [row["match"] for row in rows] == [
        "True",
        "True",
        "False",
        "",
        "False",
        "",
        "",
        "",
    ]

    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["total_ocr_attempts"] == 8
    assert summary["ok_count"] == 8
    assert summary["match_count"] == 2
    assert summary["mismatch_count"] == 2
    assert summary["empty_ocr_count"] == 1
    assert summary["no_expected_value_count"] == 3
    assert summary["by_status"] == {"ok": 8}
    assert summary["failure_reasons"] == {
        "engine_unavailable": 0,
        "ocr_failed": 0,
        "empty_ocr": 1,
        "mismatch": 2,
        "no_expected_value": 3,
    }
    assert summary["by_roi"]["score_digits"]["match_count"] == 1
    assert summary["by_roi"]["score_digits"]["no_expected_value_count"] == 0
    assert summary["by_roi"]["marvelous"]["mismatch_count"] == 1
    assert summary["by_roi"]["good"]["empty_ocr_count"] == 1
    assert summary["by_roi"]["perfect"]["no_expected_value_count"] == 1
    assert summary["by_roi"]["miss"]["no_expected_value_count"] == 1
    assert summary["by_roi"]["ex_score"]["no_expected_value_count"] == 1
    assert summary["expected_coverage_by_roi"]["score_digits"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["perfect"]["evaluation_status"] == (
        "no_expected_values"
    )

    coverage = (output_dir / "ocr_expected_coverage.md").read_text(encoding="utf-8")
    assert "| `score_digits` | `evaluated` | 1 | 0 | 1 |" in coverage
    assert "| `perfect` | `no_expected_values` | 0 | 1 | 1 |" in coverage
    assert "- `perfect`" in coverage


def test_profile_comparison_keeps_score_ocr_csv_compatible_and_summarizes_by_profile(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in ("result_score123456_a.png", "result_score123456_b.png"):
        write_test_image(tmp_path / "screenshots" / name)
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type,expected_score,max_combo,expected_marvelous\n"
        "result_score123456_a.png,result,123456,10,20\n"
        "result_score123456_b.png,result,123456,10,20\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        return classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    def run_tesseract_synthetic(
        binary: Image.Image,
        roi_name: str = "score_digits",
    ) -> tuple[str, str, str, str]:
        profile = binary.info.get("profile", "default")
        raw_by_profile = {
            "default": {
                "score_digits": "123456",
                "max_combo": "",
                "marvelous": "21",
                "perfect": "7",
            },
            "high-contrast": {
                "score_digits": "",
                "max_combo": "10",
                "marvelous": "20",
                "perfect": "7",
            },
            "low-threshold": {
                "score_digits": "999999",
                "max_combo": "",
                "marvelous": "20",
                "perfect": "",
            },
            "tighter-white": {
                "score_digits": "123456",
                "max_combo": "10",
                "marvelous": "",
                "perfect": "7",
            },
            "no-sharpen": {
                "score_digits": "123456",
                "max_combo": "9",
                "marvelous": "20",
                "perfect": "7",
            },
        }
        return raw_by_profile[profile][roi_name], "tesseract", "ok", ""

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "preprocess_ocr_roi", stub_profile_preprocess)
    monkeypatch.setattr(runner, "run_tesseract", run_tesseract_synthetic)

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
                "--ocr-target",
                "confirmed-events",
                "--ocr-rois",
                "score_digits",
                "max_combo",
                "marvelous",
                "perfect",
                "--ocr-profile",
                "all",
                "--no-rois",
            ]
        )
        == 0
    )

    legacy_rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert len(legacy_rows) == 4
    assert {row["organized_file"] for row in legacy_rows} == {"result_score123456_b.png"}
    assert list(legacy_rows[0]) == [
        "organized_file",
        "screen_type",
        "result_candidate",
        "roi_name",
        "score_ocr_raw",
        "score_ocr_normalized",
        "expected_score",
        "match",
        "engine",
        "status",
        "error",
        "original_path",
        "enlarged_path",
        "binary_path",
    ]

    report = (output_dir / "ocr_roi_report.md").read_text(encoding="utf-8")
    assert "| `max_combo` | `evaluated` | 1 | 0 | 0 | 1 | 0 | 0 |" in report
    assert "- evaluation_status: `evaluated`" in report
    assert "- representative empty_ocr: `result_score123456_b.png`" in report
    assert "`result_score123456_b.png`" in report

    profile_rows = read_csv_rows(output_dir / "score_ocr_profiles.csv")
    assert len(profile_rows) == 20
    assert {row["profile"] for row in profile_rows} == {
        "default",
        "high-contrast",
        "low-threshold",
        "tighter-white",
        "no-sharpen",
    }
    assert {row["organized_file"] for row in profile_rows} == {"result_score123456_b.png"}

    summary = json.loads(
        (output_dir / "score_ocr_profiles_summary.json").read_text(encoding="utf-8")
    )
    assert summary["ocr_target_mode"] == "confirmed-events"
    assert summary["profiles"]["default"]["max_combo"]["empty_ocr_count"] == 1
    assert summary["profiles"]["default"]["marvelous"]["mismatch_count"] == 1
    assert summary["profiles"]["default"]["perfect"]["no_expected_value_count"] == 1
    assert summary["profiles"]["high-contrast"]["max_combo"]["match_count"] == 1
    assert summary["profiles"]["low-threshold"]["score_digits"]["mismatch_count"] == 1
    assert summary["profiles"]["low-threshold"]["perfect"]["empty_ocr_count"] == 1
    assert summary["profiles"]["tighter-white"]["max_combo"]["match_count"] == 1
    assert summary["profiles"]["no-sharpen"]["max_combo"]["mismatch_count"] == 1
    assert summary["best_by_roi"]["max_combo"]["best_match_profiles"] == [
        "high-contrast",
        "tighter-white",
    ]
    assert summary["best_by_roi"]["max_combo"]["evaluation_status"] == "evaluated"
    assert summary["best_by_roi"]["max_combo"]["recommendation_basis"] == "match_count"
    assert summary["best_by_roi"]["max_combo"]["recommended_profiles"] == [
        "high-contrast",
        "tighter-white",
    ]
    assert summary["best_by_roi"]["max_combo"]["recommendation_readiness"] == (
        "adoption_candidate"
    )
    assert summary["best_by_roi"]["max_combo"]["default_profile_counts"][
        "empty_ocr_count"
    ] == 1
    assert summary["best_by_roi"]["max_combo"]["top_recommended_profile"] == (
        "high-contrast"
    )
    assert summary["best_by_roi"]["max_combo"]["recommended_vs_default_delta"] == {
        "match_count": 1,
        "mismatch_count": 0,
        "empty_ocr_count": -1,
    }
    assert summary["best_by_roi"]["score_digits"]["lowest_empty_profiles"] == [
        "default",
        "low-threshold",
        "no-sharpen",
        "tighter-white",
    ]
    assert summary["best_by_roi"]["perfect"]["evaluation_status"] == "no_expected_values"
    assert summary["best_by_roi"]["perfect"]["recommendation_basis"] == (
        "empty_ocr_reference_only"
    )
    assert summary["best_by_roi"]["perfect"]["match_recommendation_evaluated"] is False
    assert summary["best_by_roi"]["perfect"]["recommended_profiles"] == []
    assert summary["best_by_roi"]["perfect"]["recommendation_readiness"] == "reference_only"
    assert summary["best_by_roi"]["perfect"]["reference_profiles"] == [
        "default",
        "high-contrast",
        "no-sharpen",
        "tighter-white",
    ]

    coverage = (output_dir / "ocr_expected_coverage.md").read_text(encoding="utf-8")
    assert "| `perfect` | `no_expected_values` | 0 | 1 | 1 |" in coverage
    assert (
        "| `perfect` | `no_expected_values` | `reference_only` | "
        "`empty_ocr_reference_only` | 0 | 1 |"
    ) in coverage


def test_expected_coverage_marks_partially_evaluated_roi(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in ("result_score123456_a.png", "result_score123456_b.png"):
        write_test_image(tmp_path / "screenshots" / name)
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type,expected_score,max_combo\n"
        "result_score123456_a.png,result,123456,10\n"
        "result_score123456_b.png,result,123456,\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        return classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(
        runner,
        "run_tesseract",
        lambda _binary, roi_name="score_digits": (
            "10" if roi_name == "max_combo" else "123456",
            "tesseract",
            "ok",
            "",
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
                "--ocr-rois",
                "score_digits",
                "max_combo",
                "--no-rois",
            ]
        )
        == 0
    )

    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["expected_coverage_by_roi"]["max_combo"]["expected_value_count"] == 1
    assert summary["expected_coverage_by_roi"]["max_combo"]["no_expected_value_count"] == 1
    assert summary["expected_coverage_by_roi"]["max_combo"]["evaluation_status"] == (
        "partially_evaluated"
    )
    assert summary["by_roi"]["max_combo"]["match_count"] == 1
    assert summary["by_roi"]["max_combo"]["no_expected_value_count"] == 1

    coverage = (output_dir / "ocr_expected_coverage.md").read_text(encoding="utf-8")
    assert "| `max_combo` | `partially_evaluated` | 1 | 1 | 2 |" in coverage


def test_profile_summary_recommends_low_threshold_when_ex_score_default_is_weaker() -> None:
    def profile_result(
        profile: str,
        normalized: str,
        expected_score: str,
    ) -> runner.ProfileScoreOcrResult:
        return runner.ProfileScoreOcrResult(
            profile=profile,
            organized_file=f"{profile}.png",
            screen_type="result",
            result_candidate=True,
            roi_name="ex_score",
            score_ocr_raw=normalized,
            score_ocr_normalized=normalized,
            expected_score=expected_score,
            match=normalized == expected_score if expected_score else None,
            engine="tesseract",
            status="ok",
            error="",
            original_path="",
            enlarged_path="",
            binary_path="",
        )

    summary = runner.summarize_profile_score_ocr(
        [
            profile_result("default", "552", "552"),
            profile_result("default", "662", "552"),
            profile_result("low-threshold", "552", "552"),
            profile_result("low-threshold", "552", "552"),
        ],
        "confirmed-events",
    )

    bucket = summary["best_by_roi"]["ex_score"]
    assert bucket["evaluation_status"] == "evaluated"
    assert bucket["recommendation_is_tentative"] is False
    assert bucket["recommended_profiles"] == ["low-threshold"]
    assert bucket["recommendation_readiness"] == "adoption_candidate"
    assert bucket["default_profile_counts"]["match_count"] == 1
    assert bucket["top_recommended_profile_counts"]["match_count"] == 2
    assert bucket["recommended_vs_default_delta"] == {
        "match_count": 1,
        "mismatch_count": -1,
        "empty_ocr_count": 0,
    }


def test_ocr_expected_template_lists_result_rows_missing_judgment_values(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in ("result_score123456_a.png", "song_select.png"):
        write_test_image(tmp_path / "screenshots" / name)
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type,expected_score,max_combo\n"
        "result_score123456_a.png,result,123456,10\n"
        "song_select.png,song_select,,\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        return classification(
            row["organized_file"],
            result_candidate=row["screen_type"] == "result",
            screen_type=row["screen_type"],
        )

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "run_tesseract", stub_tesseract)

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
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "ocr_expected_template.csv")
    assert rows == [
        {
            "organized_file": "result_score123456_a.png",
            "screen_type": "result",
            "score_digits": "123456",
            "max_combo": "10",
            "marvelous": "",
            "perfect": "",
                "great": "",
                "good": "",
                "miss": "",
                "ex_score": "",
                "missing_judgment_rois": "marvelous perfect great good miss ex_score",
            }
        ]


def test_ocr_expected_template_clears_stale_rows_when_all_values_exist(tmp_path: Path) -> None:
    output_path = tmp_path / "ocr_expected_template.csv"
    output_path.write_text(
        "organized_file,screen_type,score_digits,max_combo,marvelous,perfect,great,good,miss,"
        "ex_score,missing_judgment_rois\n"
        "stale.png,result,123456,,,,,,,,max_combo marvelous perfect great good miss ex_score\n",
        encoding="utf-8",
    )
    frame = runner.FrameInput(
        row={
            "organized_file": "result_score123456_a.png",
            "screen_type": "result",
            "max_combo": "10",
            "marvelous": "20",
            "perfect": "30",
            "great": "40",
            "good": "0",
            "miss": "1",
            "ex_score": "234",
        },
        image_path=tmp_path / "screenshots" / "result_score123456_a.png",
    )

    assert runner.write_ocr_expected_template(output_path, [frame]) == 0

    assert read_csv_rows(output_path) == []
    assert output_path.read_text(encoding="utf-8").startswith(
        "organized_file,screen_type,score_digits,max_combo,marvelous,perfect,great,good,miss,"
        "ex_score,missing_judgment_rois\n"
    )


def test_profile_summary_marks_partially_evaluated_recommendations_as_tentative() -> None:
    def profile_result(
        profile: str,
        organized_file: str,
        expected_score: str,
        normalized: str,
    ) -> runner.ProfileScoreOcrResult:
        return runner.ProfileScoreOcrResult(
            profile=profile,
            organized_file=organized_file,
            screen_type="result",
            result_candidate=True,
            roi_name="max_combo",
            score_ocr_raw=normalized,
            score_ocr_normalized=normalized,
            expected_score=expected_score,
            match=normalized == expected_score if expected_score else None,
            engine="tesseract",
            status="ok",
            error="",
            original_path="",
            enlarged_path="",
            binary_path="",
        )

    summary = runner.summarize_profile_score_ocr(
        [
            profile_result("default", "a.png", "10", "10"),
            profile_result("default", "b.png", "", "10"),
            profile_result("tighter-white", "a.png", "10", "9"),
            profile_result("tighter-white", "b.png", "", "10"),
        ],
        "confirmed-events",
    )

    bucket = summary["best_by_roi"]["max_combo"]
    assert bucket["evaluation_status"] == "partially_evaluated"
    assert bucket["recommendation_basis"] == "match_count_partial"
    assert bucket["recommendation_is_tentative"] is True
    assert bucket["recommended_profiles"] == ["default"]
    assert bucket["match_recommendation_evaluated"] is True
    assert bucket["recommendation_readiness"] == "tentative"


def test_confirmed_events_ocr_target_filters_metadata_frame_events(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    frame_names = (
        "result_score123456_a.png",
        "result_score123456_b.png",
        "result_score123456_c.png",
        "transition_countup_score999999_d.png",
        "result_score222222_e.png",
        "result_score222222_f.png",
    )
    for name in frame_names:
        write_test_image(tmp_path / "screenshots" / name)
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type\n"
        "result_score123456_a.png,result\n"
        "result_score123456_b.png,result\n"
        "result_score123456_c.png,result\n"
        "transition_countup_score999999_d.png,transition\n"
        "result_score222222_e.png,result\n"
        "result_score222222_f.png,result\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        if row["organized_file"].startswith("transition_countup_"):
            return classification(
                "transition_countup_score999999_d.png",
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            )
        score = "123456" if "score123456" in row["organized_file"] else "222222"
        return classification(
            f"organized/result_score{score}_{row['organized_file']}",
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "run_tesseract", stub_tesseract)

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
                "--ocr-target",
                "confirmed-events",
                "--no-rois",
            ]
        )
        == 0
    )

    event_rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["confirmation_mode"] for row in event_rows] == ["frames"] * 6
    assert [row["event_type"] for row in event_rows] == [
        "none",
        "confirmed",
        "duplicate",
        "rejected_transition",
        "none",
        "confirmed",
    ]

    rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert [row["organized_file"] for row in rows] == [
        "result_score123456_b.png",
        "result_score222222_f.png",
    ]
    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["ocr_target_mode"] == "confirmed-events"
    assert summary["total_ocr_attempts"] == 2
    assert summary["ok_count"] == 2
    assert summary["match_count"] == 1
    assert summary["mismatch_count"] == 1
    assert summary["skipped_duplicate_count"] == 1
    assert summary["skipped_unconfirmed_count"] == 3


def test_confirmed_events_ocr_rois_all_summary_uses_only_target_events(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in ("result_score123456_a.png", "result_score123456_b.png"):
        write_test_image(tmp_path / "screenshots" / name)
    metadata_path = tmp_path / "metadata.csv"
    metadata_path.write_text(
        "organized_file,screen_type,expected_score,max_combo\n"
        "result_score123456_a.png,result,123456,10\n"
        "result_score123456_b.png,result,123456,10\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        return classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(
        runner,
        "run_tesseract",
        lambda _binary, roi_name="score_digits": (
            "10" if roi_name == "max_combo" else "123456",
            "tesseract",
            "ok",
            "",
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
                "--ocr-target",
                "confirmed-events",
                "--ocr-rois",
                "all",
                "--no-rois",
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert len(rows) == len(runner.OCR_ROIS)
    assert {row["organized_file"] for row in rows} == {"result_score123456_b.png"}

    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["ocr_target_mode"] == "confirmed-events"
    assert summary["total_ocr_attempts"] == len(runner.OCR_ROIS)
    assert summary["skipped_unconfirmed_count"] == 1
    assert set(summary["by_roi"]) == set(runner.OCR_ROIS)
    assert all(bucket["total_ocr_attempts"] == 1 for bucket in summary["by_roi"].values())
    assert summary["by_roi"]["score_digits"]["match_count"] == 1
    assert summary["by_roi"]["max_combo"]["match_count"] == 1
    assert summary["by_roi"]["perfect"]["no_expected_value_count"] == 1
    assert summary["expected_coverage_by_roi"]["score_digits"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["max_combo"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["perfect"]["evaluation_status"] == (
        "no_expected_values"
    )

    coverage = (output_dir / "ocr_expected_coverage.md").read_text(encoding="utf-8")
    assert "- OCR target mode: `confirmed-events`" in coverage
    assert "| `score_digits` | `evaluated` | 1 | 0 | 1 |" in coverage


def test_confirmed_events_ocr_target_uses_time_events_in_timestamped_mode(
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
        "d.png,result\n"
        "e.png,transition\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        if row["organized_file"] == "e.png":
            return classification(
                "transition_countup_score999999_e.png",
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
    monkeypatch.setattr(runner, "run_tesseract", stub_tesseract)

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
                "--ocr-target",
                "confirmed-events",
                "--no-rois",
            ]
        )
        == 0
    )

    event_rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["confirmation_mode"] for row in event_rows] == ["time"] * 5
    assert [row["event_type"] for row in event_rows] == [
        "none",
        "none",
        "confirmed",
        "duplicate",
        "rejected_transition",
    ]

    rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert [row["organized_file"] for row in rows] == ["c.png"]
    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["total_ocr_attempts"] == 1
    assert summary["skipped_duplicate_count"] == 1
    assert summary["skipped_unconfirmed_count"] == 3


def test_confirmed_events_ocr_target_uses_time_events_in_manifest_mode(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in ("a.png", "b.png", "c.png", "d.png", "e.png"):
        write_test_image(tmp_path / "frames" / name)
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms,screen_type\n"
        "a.png,1000,result\n"
        "b.png,1500,result\n"
        "c.png,2000,result\n"
        "d.png,2500,result\n"
        "e.png,3000,transition\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        if row["organized_file"] == "e.png":
            return classification(
                "transition_countup_score999999_e.png",
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
    monkeypatch.setattr(runner, "run_tesseract", stub_tesseract)

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
                "--ocr-target",
                "confirmed-events",
                "--no-rois",
            ]
        )
        == 0
    )

    event_rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["confirmation_mode"] for row in event_rows] == ["time"] * 5
    assert [row["event_type"] for row in event_rows] == [
        "none",
        "none",
        "confirmed",
        "duplicate",
        "rejected_transition",
    ]

    rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert [row["organized_file"] for row in rows] == ["c.png"]
    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["ocr_target_mode"] == "confirmed-events"
    assert summary["total_ocr_attempts"] == 1
    assert summary["skipped_duplicate_count"] == 1
    assert summary["skipped_unconfirmed_count"] == 3


def test_manifest_confirmed_events_ocr_uses_expected_columns_and_skips_duplicates(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    frame_names = (
        "result_score123456_a.png",
        "result_score123456_b.png",
        "result_score123456_c.png",
        "result_score123456_d.png",
        "transition_countup_score999999_e.png",
    )
    for name in frame_names:
        write_test_image(tmp_path / "frames" / name)
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms,screen_type,expected_score,max_combo,miss,ex_score\n"
        "result_score123456_a.png,1000,result,123456,10,1,234\n"
        "result_score123456_b.png,1500,result,123456,10,1,234\n"
        "result_score123456_c.png,2000,result,123456,10,1,234\n"
        "result_score123456_d.png,2500,result,123456,10,1,234\n"
        "transition_countup_score999999_e.png,3000,transition,999999,99,9,999\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        if row["organized_file"].startswith("transition_countup_"):
            return classification(
                row["organized_file"],
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            )
        return classification(
            row["organized_file"],
            result_candidate=True,
            screen_type=row["screen_type"],
        )

    def run_tesseract_synthetic(
        _binary: Image.Image,
        roi_name: str = "score_digits",
    ) -> tuple[str, str, str, str]:
        raw_by_roi = {
            "score_digits": "123456",
            "max_combo": "10",
            "marvelous": "20",
            "perfect": "30",
            "great": "40",
            "good": "50",
            "miss": "1",
            "ex_score": "234",
        }
        return raw_by_roi[roi_name], "tesseract", "ok", ""

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "preprocess_ocr_roi", stub_profile_preprocess)
    monkeypatch.setattr(runner, "run_tesseract", run_tesseract_synthetic)

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
                "--ocr-target",
                "confirmed-events",
                "--ocr-rois",
                "all",
                "--no-rois",
            ]
        )
        == 0
    )

    event_rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["event_type"] for row in event_rows] == [
        "none",
        "none",
        "confirmed",
        "duplicate",
        "rejected_transition",
    ]

    rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert len(rows) == len(runner.OCR_ROIS)
    assert {row["organized_file"] for row in rows} == {"result_score123456_c.png"}
    assert {row["roi_name"] for row in rows} == set(runner.OCR_ROIS)

    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["ocr_target_mode"] == "confirmed-events"
    assert summary["total_ocr_attempts"] == len(runner.OCR_ROIS)
    assert summary["skipped_duplicate_count"] == 1
    assert summary["skipped_unconfirmed_count"] == 3
    assert summary["expected_coverage_by_roi"]["score_digits"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["max_combo"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["miss"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["ex_score"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["perfect"]["evaluation_status"] == (
        "no_expected_values"
    )


def test_confirmed_events_expected_coverage_statuses_and_exclusions(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    frame_names = (
        "result_score111111_a.png",
        "result_score111111_b.png",
        "result_score111111_c.png",
        "gameplay_reset.png",
        "result_score222222_a.png",
        "result_score222222_b.png",
        "result_score222222_c.png",
        "result_score222222_d.png",
        "transition_countup_score999999.png",
    )
    for name in frame_names:
        write_test_image(tmp_path / "frames" / name)
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms,screen_type,expected_score,max_combo\n"
        "result_score111111_a.png,0,result,111111,10\n"
        "result_score111111_b.png,500,result,111111,10\n"
        "result_score111111_c.png,1000,result,111111,10\n"
        "gameplay_reset.png,1500,gameplay,,\n"
        "result_score222222_a.png,2000,result,222222,\n"
        "result_score222222_b.png,2500,result,222222,\n"
        "result_score222222_c.png,3000,result,222222,\n"
        "result_score222222_d.png,3500,result,222222,\n"
        "transition_countup_score999999.png,4000,transition,999999,99\n",
        encoding="utf-8",
    )

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        organized_file = row["organized_file"]
        if organized_file.startswith("transition_countup_"):
            return classification(
                organized_file,
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            )
        return classification(
            organized_file,
            result_candidate=row.get("screen_type") == "result",
            result_shape_candidate=row.get("screen_type") == "result",
            screen_type=row.get("screen_type", "unknown"),
        )

    def run_tesseract_synthetic(
        _binary: Image.Image,
        roi_name: str = "score_digits",
    ) -> tuple[str, str, str, str]:
        raw_by_roi = {
            "score_digits": "111111",
            "max_combo": "10",
            "perfect": "30",
        }
        return raw_by_roi[roi_name], "tesseract", "ok", ""

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "preprocess_ocr_roi", stub_profile_preprocess)
    monkeypatch.setattr(runner, "run_tesseract", run_tesseract_synthetic)

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
                "--ocr-target",
                "confirmed-events",
                "--ocr-rois",
                "score_digits",
                "max_combo",
                "perfect",
                "--no-rois",
            ]
        )
        == 0
    )

    event_rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["event_type"] for row in event_rows] == [
        "none",
        "none",
        "confirmed",
        "none",
        "none",
        "none",
        "confirmed",
        "duplicate",
        "rejected_transition",
    ]

    ocr_rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert {row["organized_file"] for row in ocr_rows} == {
        "result_score111111_c.png",
        "result_score222222_c.png",
    }
    assert "result_score222222_d.png" not in {row["organized_file"] for row in ocr_rows}
    assert "transition_countup_score999999.png" not in {
        row["organized_file"] for row in ocr_rows
    }

    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["ocr_target_mode"] == "confirmed-events"
    assert summary["skipped_duplicate_count"] == 1
    assert summary["skipped_rejected_transition_count"] == 1
    assert summary["expected_coverage_by_roi"]["score_digits"]["evaluation_status"] == (
        "evaluated"
    )
    assert summary["expected_coverage_by_roi"]["max_combo"]["evaluation_status"] == (
        "partially_evaluated"
    )
    assert summary["expected_coverage_by_roi"]["max_combo"]["expected_value_count"] == 1
    assert summary["expected_coverage_by_roi"]["max_combo"]["no_expected_value_count"] == 1
    assert summary["expected_coverage_by_roi"]["perfect"]["evaluation_status"] == (
        "no_expected_values"
    )
    assert summary["by_roi"]["max_combo"]["match_count"] == 1
    assert summary["by_roi"]["max_combo"]["no_expected_value_count"] == 1
    assert summary["by_roi"]["perfect"]["match_count"] == 0
    assert summary["by_roi"]["perfect"]["no_expected_value_count"] == 2

    coverage = (output_dir / "ocr_expected_coverage.md").read_text(encoding="utf-8")
    assert "| `score_digits` | `evaluated` | 2 | 0 | 2 |" in coverage
    assert "| `max_combo` | `partially_evaluated` | 1 | 1 | 2 |" in coverage
    assert "| `perfect` | `no_expected_values` | 0 | 2 | 2 |" in coverage


def test_dry_run_sequence_scenario_replays_manifest_save_boundary(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.chdir(tmp_path)
    frame_root = tmp_path / "scenario_frames"
    frame_names = (
        "menu_setup_a.png",
        "result_score111111_short_a.png",
        "result_score111111_short_b.png",
        "gameplay_reset.png",
        "result_score123456_a.png",
        "result_score123456_b.png",
        "result_score123456_c.png",
        "result_score123456_d.png",
        "transition_countup_score999999.png",
    )
    for name in frame_names:
        write_test_image(frame_root / name)
    scenario_manifest = tmp_path / "dry_run_sequence.csv"
    scenario_manifest.write_text(
        "image_path,timestamp_ms,screen_type,expected_score,max_combo,capture_note\n"
        "menu_setup_a.png,0,menu_setup,,,non-result\n"
        "result_score111111_short_a.png,100,result,111111,5,short-start\n"
        "result_score111111_short_b.png,500,result,111111,5,short-still-unconfirmed\n"
        "gameplay_reset.png,700,gameplay,,,reset\n"
        "result_score123456_a.png,1000,result,123456,10,sustained-start\n"
        "result_score123456_b.png,1500,result,123456,10,sustained-middle\n"
        "result_score123456_c.png,2100,result,123456,10,confirmed-save-boundary\n"
        "result_score123456_d.png,2600,result,123456,10,duplicate-window\n"
        "transition_countup_score999999.png,3000,transition,999999,,countup-shape\n",
        encoding="utf-8",
    )

    assert (
        runner.main(
            [
                "--capture-dry-run-scenario",
                str(scenario_manifest),
                "--frame-root",
                str(frame_root),
                "--capture-dry-run-output",
                "data/dry_run_sequence",
            ]
        )
        == 0
    )

    generated_manifest = Path("data/dry_run_sequence/frame_manifest.csv")
    frames = runner.read_frame_manifest(generated_manifest)
    assert [frame.timestamp_ms for frame in frames] == [
        0,
        100,
        500,
        700,
        1000,
        1500,
        2100,
        2600,
        3000,
    ]
    assert [frame.row["screen_type"] for frame in frames] == [
        "menu_setup",
        "result",
        "result",
        "gameplay",
        "result",
        "result",
        "result",
        "result",
        "transition",
    ]
    assert frames[6].row["expected_score"] == "123456"
    assert frames[6].row["max_combo"] == "10"
    assert frames[8].row["capture_note"] == "countup-shape"

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        organized_file = row["organized_file"]
        if "transition_countup_" in organized_file:
            return classification(
                organized_file,
                result_candidate=False,
                result_shape_candidate=True,
                screen_type="transition",
                transition_kind="countup",
            )
        return classification(
            organized_file,
            result_candidate=row["screen_type"] == "result",
            screen_type=row["screen_type"],
        )

    def run_tesseract_synthetic(
        _binary: Image.Image,
        roi_name: str = "score_digits",
    ) -> tuple[str, str, str, str]:
        raw_by_roi = {
            "score_digits": "123456",
            "max_combo": "10",
            "marvelous": "20",
            "perfect": "30",
            "great": "40",
            "good": "50",
            "miss": "1",
            "ex_score": "234",
        }
        return raw_by_roi[roi_name], "tesseract", "ok", ""

    monkeypatch.setattr(runner, "classify", classify_synthetic)
    monkeypatch.setattr(runner, "preprocess_ocr_roi", stub_profile_preprocess)
    monkeypatch.setattr(runner, "run_tesseract", run_tesseract_synthetic)

    output_dir = Path("data/dry_run_sequence_replay")
    assert (
        runner.main(
            [
                "--sequence-mode",
                "manifest",
                "--frame-manifest",
                str(generated_manifest),
                "--output",
                str(output_dir),
                "--ocr-target",
                "confirmed-events",
                "--ocr-rois",
                "all",
                "--no-rois",
            ]
        )
        == 0
    )

    event_rows = read_csv_rows(output_dir / "result_events.csv")
    assert [row["confirmation_mode"] for row in event_rows] == ["time"] * 9
    assert [row["timestamp_ms"] for row in event_rows] == [
        "0",
        "100",
        "500",
        "700",
        "1000",
        "1500",
        "2100",
        "2600",
        "3000",
    ]
    assert [row["event_type"] for row in event_rows] == [
        "none",
        "none",
        "none",
        "none",
        "none",
        "none",
        "confirmed",
        "duplicate",
        "rejected_transition",
    ]
    assert event_rows[2]["candidate_duration_ms"] == "400"
    assert event_rows[6]["confirmed_result"] == "True"
    assert event_rows[6]["duplicate"] == "False"
    assert event_rows[7]["confirmed_result"] == "True"
    assert event_rows[7]["duplicate"] == "True"
    assert event_rows[8]["result_shape_candidate"] == "True"
    assert event_rows[8]["confirmed_result"] == "False"

    ocr_rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert len(ocr_rows) == len(runner.OCR_ROIS)
    assert {row["organized_file"] for row in ocr_rows} == {
        "frames/result_score123456_c_frame_0007.png"
    }
    assert {row["roi_name"] for row in ocr_rows} == set(runner.OCR_ROIS)

    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["ocr_target_mode"] == "confirmed-events"
    assert summary["total_ocr_attempts"] == len(runner.OCR_ROIS)
    assert summary["skipped_duplicate_count"] == 1
    assert summary["skipped_unconfirmed_count"] == 7
    assert summary["expected_coverage_by_roi"]["score_digits"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["max_combo"]["evaluation_status"] == "evaluated"
    assert summary["expected_coverage_by_roi"]["perfect"]["evaluation_status"] == (
        "no_expected_values"
    )


def test_minimal_manifest_marks_judgment_rois_as_no_expected_values(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in (
        "result_score123456_a.png",
        "result_score123456_b.png",
        "result_score123456_c.png",
    ):
        write_test_image(tmp_path / name)
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms\n"
        "result_score123456_a.png,1000\n"
        "result_score123456_b.png,1500\n"
        "result_score123456_c.png,2000\n",
        encoding="utf-8",
    )

    monkeypatch.setattr(
        runner,
        "classify",
        lambda _image, row: classification(
            row["organized_file"],
            result_candidate=True,
            screen_type="result",
        ),
    )
    monkeypatch.setattr(runner, "preprocess_ocr_roi", stub_profile_preprocess)
    monkeypatch.setattr(
        runner,
        "run_tesseract",
        lambda _binary, roi_name="score_digits": (
            "123456" if roi_name == "score_digits" else "7",
            "tesseract",
            "ok",
            "",
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
                "--output",
                str(output_dir),
                "--ocr-target",
                "confirmed-events",
                "--ocr-rois",
                "all",
                "--no-rois",
            ]
        )
        == 0
    )

    rows = read_csv_rows(output_dir / "score_ocr.csv")
    assert len(rows) == len(runner.OCR_ROIS)

    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["match_count"] == 1
    assert summary["no_expected_value_count"] == len(runner.JUDGMENT_OCR_ROIS)
    assert summary["failure_reasons"]["no_expected_value"] == len(runner.JUDGMENT_OCR_ROIS)
    assert summary["expected_coverage_by_roi"]["score_digits"]["evaluation_status"] == "evaluated"
    for roi_name in runner.JUDGMENT_OCR_ROIS:
        assert summary["expected_coverage_by_roi"][roi_name]["evaluation_status"] == (
            "no_expected_values"
        )

    coverage = (output_dir / "ocr_expected_coverage.md").read_text(encoding="utf-8")
    assert "| `score_digits` | `evaluated` | 1 | 0 | 1 |" in coverage
    assert "| `max_combo` | `no_expected_values` | 0 | 1 | 1 |" in coverage


def test_m5_result_features_use_classified_result_candidates_without_screen_type(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in (
        "result_score123456_a.png",
        "result_score123456_b.png",
        "result_score123456_c.png",
    ):
        write_test_image(tmp_path / name)
    manifest_path = tmp_path / "manifest.csv"
    manifest_path.write_text(
        "image_path,timestamp_ms\n"
        "result_score123456_a.png,1000\n"
        "result_score123456_b.png,1500\n"
        "result_score123456_c.png,2000\n",
        encoding="utf-8",
    )
    captured_jacket_files: list[str] = []
    captured_title_files: list[str] = []

    def classify_synthetic(_image: Image.Image, row: dict[str, str]) -> runner.Classification:
        return runner.Classification(
            organized_file=row["organized_file"],
            screen_type=row.get("screen_type", ""),
            result_candidate=True,
            result_shape_candidate=True,
            transition_kind="",
            expected_result_candidate=True,
            correct=True,
            header_signal=SIGNAL,
            detail_panel_signal=SIGNAL,
            score_signal=SIGNAL,
            rank_signal=SIGNAL,
            reason="test",
        )

    def extract_jacket_synthetic(_image: Image.Image) -> object:
        captured_jacket_files.append(current_file)
        return object()

    def extract_title_synthetic(_image: Image.Image) -> object:
        captured_title_files.append(current_file)
        return object()

    current_file = ""

    def tracking_classify(image: Image.Image, row: dict[str, str]) -> runner.Classification:
        nonlocal current_file
        current_file = row["organized_file"]
        return classify_synthetic(image, row)

    monkeypatch.setattr(runner, "classify", tracking_classify)
    monkeypatch.setattr(runner.master_match, "extract_jacket_feature", extract_jacket_synthetic)
    monkeypatch.setattr(
        runner.master_match,
        "extract_title_image_feature",
        extract_title_synthetic,
    )

    output_dir = tmp_path / "output"
    assert (
        runner.main(
            [
                "--sequence-mode",
                "manifest",
                "--frame-manifest",
                str(manifest_path),
                "--output",
                str(output_dir),
                "--no-rois",
            ]
        )
        == 0
    )

    assert captured_jacket_files == [
        "result_score123456_a.png",
        "result_score123456_b.png",
        "result_score123456_c.png",
    ]
    assert captured_title_files == captured_jacket_files
