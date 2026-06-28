from __future__ import annotations

# ruff: noqa: I001

import csv
import json
from pathlib import Path

import pytest

pytest.importorskip("numpy")
pytest.importorskip("PIL")

from PIL import Image  # noqa: E402
from tools.vision_poc import runner  # noqa: E402


METADATA_PATH = Path("samples/screenshots/metadata.csv")
SCREENSHOTS_ROOT = Path("samples/screenshots")
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


def test_preprocess_ocr_roi_supports_judgment_rois() -> None:
    image = Image.new("RGB", (1280, 720), "black")

    preprocessed = runner.preprocess_ocr_roi(image, "max_combo")

    assert preprocessed.roi_name == "max_combo"
    assert preprocessed.original.size == (284, 32)
    assert preprocessed.enlarged.size == (1136, 128)
    assert preprocessed.binary.size == (1176, 168)


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
    ]

    summary = json.loads((output_dir / "score_ocr_summary.json").read_text(encoding="utf-8"))
    assert summary["total_ocr_attempts"] == 7
    assert summary["ok_count"] == 7
    assert summary["match_count"] == 2
    assert summary["mismatch_count"] == 2
    assert summary["empty_ocr_count"] == 1
    assert summary["no_expected_value_count"] == 2
    assert summary["by_status"] == {"ok": 7}
    assert summary["failure_reasons"] == {
        "engine_unavailable": 0,
        "ocr_failed": 0,
        "empty_ocr": 1,
        "mismatch": 2,
        "no_expected_value": 2,
    }
    assert summary["by_roi"]["score_digits"]["match_count"] == 1
    assert summary["by_roi"]["score_digits"]["no_expected_value_count"] == 0
    assert summary["by_roi"]["marvelous"]["mismatch_count"] == 1
    assert summary["by_roi"]["good"]["empty_ocr_count"] == 1
    assert summary["by_roi"]["perfect"]["no_expected_value_count"] == 1
    assert summary["by_roi"]["miss"]["no_expected_value_count"] == 1
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
    assert summary["best_by_roi"]["perfect"]["reference_profiles"] == [
        "default",
        "high-contrast",
        "no-sharpen",
        "tighter-white",
    ]

    coverage = (output_dir / "ocr_expected_coverage.md").read_text(encoding="utf-8")
    assert "| `perfect` | `no_expected_values` | 0 | 1 | 1 |" in coverage
    assert "| `perfect` | `no_expected_values` | `empty_ocr_reference_only` | 0 | 1 |" in coverage


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
            "missing_judgment_rois": "marvelous perfect great good miss",
        }
    ]


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
