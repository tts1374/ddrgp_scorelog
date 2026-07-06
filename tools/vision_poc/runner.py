from __future__ import annotations

import argparse
import csv
import json
import math
import re
import shutil
import subprocess
import tempfile
from collections.abc import Iterable
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter, ImageOps

from . import master_match

BASE_WIDTH = 1280
BASE_HEIGHT = 720
CONFIRMED_RESULT_MIN_FRAMES = 2
CONFIRMED_RESULT_MIN_DURATION_MS = 1000
DUPLICATE_WINDOW_FRAMES = 90
DUPLICATE_WINDOW_MS = 90_000
FRAME_IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp"}
OCR_TARGET_MODES = ("result-candidate", "confirmed-events")


ROI_DEFINITIONS: dict[str, tuple[int, int, int, int]] = {
    "results_header": (480, 0, 320, 58),
    "play_style": (360, 56, 100, 24),
    "difficulty": (378, 80, 84, 24),
    "level": (380, 104, 52, 38),
    "rank": (170, 122, 160, 126),
    "score_area": (170, 250, 320, 90),
    "score_digits": (250, 278, 210, 48),
    "jacket": (532, 54, 216, 216),
    "song_select_grid_preview_jacket": (812, 28, 150, 150),
    "song_title": (488, 274, 304, 52),
    "artist": (548, 306, 184, 26),
    "detail_result_panel": (662, 330, 462, 288),
    "detail_result_header": (662, 330, 194, 36),
    "max_combo": (714, 368, 284, 32),
    "marvelous": (766, 404, 232, 28),
    "perfect": (766, 434, 232, 28),
    "great": (766, 464, 232, 28),
    "good": (766, 494, 232, 28),
    "ok": (766, 524, 232, 28),
    "miss": (766, 554, 232, 28),
    "fast": (1018, 472, 96, 44),
    "slow": (1018, 532, 96, 44),
    "ex_score": (748, 580, 250, 34),
    "skip_prompt": (468, 654, 346, 50),
}

PRIMARY_ROIS = (
    "results_header",
    "play_style",
    "difficulty",
    "level",
    "rank",
    "score_area",
    "score_digits",
    "song_title",
    "song_select_grid_preview_jacket",
    "artist",
    "detail_result_panel",
    "detail_result_header",
    "max_combo",
    "marvelous",
    "perfect",
    "great",
    "good",
    "miss",
    "ex_score",
    "skip_prompt",
)


@dataclass(frozen=True)
class RegionFeatures:
    bright_ratio: float
    white_ratio: float
    yellow_ratio: float
    cyan_ratio: float
    green_ratio: float
    edge_ratio: float
    mean_luma: float
    std_luma: float


@dataclass(frozen=True)
class SignalResult:
    value: bool
    score: float
    features: dict[str, float | bool]


@dataclass(frozen=True)
class Classification:
    organized_file: str
    screen_type: str
    result_candidate: bool
    result_shape_candidate: bool
    transition_kind: str
    expected_result_candidate: bool
    correct: bool
    header_signal: SignalResult
    detail_panel_signal: SignalResult
    score_signal: SignalResult
    rank_signal: SignalResult
    reason: str


@dataclass(frozen=True)
class ResultEvent:
    frame_index: int
    organized_file: str
    screen_type: str
    result_candidate: bool
    result_shape_candidate: bool
    confirmed_result: bool
    event_type: str
    duplicate: bool
    duplicate_key: str
    timestamp_ms: int | None
    candidate_duration_ms: int | None
    confirmation_mode: str
    reason: str


@dataclass(frozen=True)
class FrameInput:
    row: dict[str, str]
    image_path: Path
    timestamp_ms: int | None = None


@dataclass(frozen=True)
class M3ChartFieldTemplateReference:
    field_name: str
    expected_value: str
    image_path: Path
    vector: np.ndarray
    source_type: str
    frame_index: int | None = None


@dataclass(frozen=True)
class DryRunCaptureFrame:
    source_path: Path
    timestamp_ms: int


@dataclass(frozen=True)
class ScoreOcrResult:
    organized_file: str
    screen_type: str
    result_candidate: bool
    roi_name: str
    score_ocr_raw: str
    score_ocr_normalized: str
    expected_score: str
    match: bool | None
    engine: str
    status: str
    error: str
    original_path: str
    enlarged_path: str
    binary_path: str


@dataclass(frozen=True)
class ScoreOcrSummary:
    total_ocr_attempts: int
    ok_count: int
    engine_unavailable_count: int
    match_count: int
    mismatch_count: int
    empty_ocr_count: int
    no_expected_value_count: int
    skipped_duplicate_count: int
    skipped_unconfirmed_count: int
    skipped_rejected_transition_count: int
    ocr_target_mode: str
    by_roi: dict[str, dict[str, int]]
    by_status: dict[str, int]
    failure_reasons: dict[str, int]
    expected_coverage_by_roi: dict[str, dict[str, int | str]]


@dataclass(frozen=True)
class ProfileScoreOcrResult:
    profile: str
    organized_file: str
    screen_type: str
    result_candidate: bool
    roi_name: str
    score_ocr_raw: str
    score_ocr_normalized: str
    expected_score: str
    match: bool | None
    engine: str
    status: str
    error: str
    original_path: str
    enlarged_path: str
    binary_path: str


@dataclass(frozen=True)
class M3SongArtistOcrResult:
    organized_file: str
    screen_type: str
    event_type: str
    confirmed_result: bool
    duplicate: bool
    field_name: str
    extractor: str
    ocr_raw: str
    pre_normalized_text: str
    expected_value: str
    engine: str
    status: str
    error: str
    failure_reason: str
    roi_path: str
    original_path: str
    enlarged_path: str
    binary_path: str


@dataclass(frozen=True)
class OcrPreprocessedImages:
    roi_name: str
    original: Image.Image
    enlarged: Image.Image
    binary: Image.Image


@dataclass(frozen=True)
class OcrPreprocessConfig:
    scale: int = 4
    luma_threshold: int = 135
    channel_spread_max: int = 140
    invert_to_black_text: bool = True
    padding: int = 20
    sharpen: bool = True


@dataclass(frozen=True)
class TesseractConfig:
    psm: int = 8
    dpi: int = 300
    whitelist: str | None = "0123456789"
    lang: str | None = None


OCR_ROIS = (
    "score_digits",
    "max_combo",
    "marvelous",
    "perfect",
    "great",
    "good",
    "miss",
    "ex_score",
)
M3_METADATA_EXPECTED_FIELDS = (
    "song_title",
    "artist",
    "play_style",
    "difficulty",
    "level",
    "rank",
)
M3_CHART_FIELD_FIELDS = (
    "play_style",
    "difficulty",
    "level",
)
M3_CHART_FIELD_EXTRACTION_METHOD = "filename-baseline"
M3_CHART_FIELD_IMAGE_FEATURE_EXTRACTION_METHOD = "roi-feature-nearest-centroid"
M3_CHART_FIELD_TEMPLATE_EXTRACTION_METHOD = "roi-template-nearest"
M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD = "roi-template-holdout"
M3_SONG_ARTIST_OCR_FIELDS = ("song_title", "artist")
M3_SONG_ARTIST_OCR_METHOD = "tesseract-text-raw"
M3_SAVE_CANDIDATE_FIELDS = (*M3_SONG_ARTIST_OCR_FIELDS, *M3_CHART_FIELD_FIELDS)
M3_SAVE_CANDIDATE_BLOCKER_REPRESENTATIVE_LIMIT = 3
M3_SONG_ARTIST_ENTRY_FAILURE_REASONS = (
    "engine_unavailable",
    "ocr_failed",
    "empty_ocr",
)
M3_SONG_ARTIST_FIELD_ROLES = {
    "song_title": "primary_song_identifier",
    "artist": "auxiliary_clipped_reference",
}
M3_CHART_FIELD_TEMPLATE_ROOT = Path("samples/screenshots/organized/chart_field_templates")
M3_CHART_FIELD_DIFFICULTIES = (
    "BEGINNER",
    "BASIC",
    "DIFFICULT",
    "EXPERT",
    "CHALLENGE",
)
M3_CHART_FILENAME_PATTERN = re.compile(
    r"(?:^|[_-])(?P<style>sp|dp|single|double)[_-]"
    r"(?P<difficulty>beginner|basic|difficult|expert|challenge)[_-]"
    r"lv(?P<level>\d{1,2})(?:[_-]|$)",
    re.IGNORECASE,
)
M3_METADATA_EXPECTED_COLUMN_KEYS: dict[str, tuple[str, ...]] = {
    "song_title": ("song_title", "expected_song_title"),
    "artist": ("artist", "expected_artist"),
    "play_style": ("play_style", "expected_play_style"),
    "difficulty": ("difficulty", "expected_difficulty"),
    "level": ("level", "expected_level"),
    "rank": ("rank", "expected_rank"),
}
OCR_PREPROCESS_CONFIG = OcrPreprocessConfig()
OCR_PREPROCESS_PROFILES: dict[str, OcrPreprocessConfig] = {
    "default": OCR_PREPROCESS_CONFIG,
    "high-contrast": OcrPreprocessConfig(luma_threshold=150, channel_spread_max=105),
    "low-threshold": OcrPreprocessConfig(luma_threshold=115, channel_spread_max=165),
    "tighter-white": OcrPreprocessConfig(luma_threshold=165, channel_spread_max=80),
    "no-sharpen": OcrPreprocessConfig(sharpen=False, padding=16),
}
TESSERACT_CONFIG = TesseractConfig()
M3_TEXT_TESSERACT_CONFIGS: dict[str, TesseractConfig] = {
    "song_title": TesseractConfig(psm=6, dpi=300, whitelist=None),
    "artist": TesseractConfig(psm=7, dpi=300, whitelist=None),
}
JUDGMENT_OCR_ROIS = tuple(roi for roi in OCR_ROIS if roi != "score_digits")
OCR_DIGIT_FOCUS_LEFT_FRACTIONS: dict[str, float] = {
    "miss": 0.35,
}
OCR_BINARY_BLACK_DILATE_KERNELS: dict[str, int] = {
    "ex_score": 3,
}


def clamp01(value: float) -> float:
    return max(0.0, min(1.0, value))


def ratio_score(value: float, low: float, high: float) -> float:
    if high <= low:
        return 1.0 if value >= high else 0.0
    return clamp01((value - low) / (high - low))


def scaled_box(image: Image.Image, roi: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    width, height = image.size
    sx = width / BASE_WIDTH
    sy = height / BASE_HEIGHT
    x, y, w, h = roi
    return (
        round(x * sx),
        round(y * sy),
        round((x + w) * sx),
        round((y + h) * sy),
    )


def crop_roi(image: Image.Image, roi: tuple[int, int, int, int]) -> Image.Image:
    return image.crop(scaled_box(image, roi))


def crop_right_fraction(image: Image.Image, left_fraction: float) -> Image.Image:
    left = round(image.width * left_fraction)
    if left <= 0:
        return image
    if left >= image.width:
        msg = f"left_fraction leaves no pixels: {left_fraction}"
        raise ValueError(msg)
    return image.crop((left, 0, image.width, image.height))


def extract_features(region: Image.Image) -> RegionFeatures:
    rgb = np.asarray(region.convert("RGB")).astype(np.float32)
    red = rgb[:, :, 0]
    green = rgb[:, :, 1]
    blue = rgb[:, :, 2]
    luma = np.asarray(region.convert("L")).astype(np.float32)
    edges = np.asarray(region.convert("L").filter(ImageFilter.FIND_EDGES)).astype(np.float32)

    bright = ((red + green + blue) / 3 > 205).mean()
    white = (
        (red > 190)
        & (green > 190)
        & (blue > 190)
        & (np.abs(red - green) < 45)
        & (np.abs(green - blue) < 45)
    ).mean()
    yellow = ((red > 150) & (green > 120) & (blue < 140) & (red > green * 0.75)).mean()
    cyan = ((green > 120) & (blue > 120) & (red < 130) & (np.abs(green - blue) < 100)).mean()
    greenish = ((green > 135) & (red < 135) & (blue < 160)).mean()
    edge = (edges > 35).mean()

    return RegionFeatures(
        bright_ratio=float(bright),
        white_ratio=float(white),
        yellow_ratio=float(yellow),
        cyan_ratio=float(cyan),
        green_ratio=float(greenish),
        edge_ratio=float(edge),
        mean_luma=float(luma.mean()),
        std_luma=float(luma.std()),
    )


def border_cyan_ratio(region: Image.Image) -> float:
    rgb = np.asarray(region.convert("RGB")).astype(np.float32)
    height, width = rgb.shape[:2]
    thickness = max(3, min(height, width) // 25)
    border = np.zeros((height, width), dtype=bool)
    border[:thickness, :] = True
    border[-thickness:, :] = True
    border[:, :thickness] = True
    border[:, -thickness:] = True

    red = rgb[:, :, 0]
    green = rgb[:, :, 1]
    blue = rgb[:, :, 2]
    cyan = (green > 110) & (blue > 110) & (red < 140) & (np.abs(green - blue) < 110)
    return float(cyan[border].mean())


def score_header(features: RegionFeatures) -> SignalResult:
    score = (
        0.45 * ratio_score(features.white_ratio, 0.16, 0.21)
        + 0.35 * ratio_score(features.edge_ratio, 0.20, 0.24)
        + 0.20 * ratio_score(features.std_luma, 75, 95)
    )
    return SignalResult(
        value=score >= 0.72,
        score=score,
        features=asdict(features),
    )


def score_detail_panel(features: RegionFeatures, border_cyan: float) -> SignalResult:
    mean_window = 1.0 - min(abs(features.mean_luma - 130.0) / 55.0, 1.0)
    score = (
        0.45 * ratio_score(border_cyan, 0.22, 0.31)
        + 0.25 * ratio_score(features.cyan_ratio, 0.06, 0.085)
        + 0.20 * ratio_score(features.edge_ratio, 0.09, 0.12)
        + 0.10 * mean_window
    )
    signal_features = asdict(features)
    signal_features["border_cyan_ratio"] = border_cyan
    return SignalResult(
        value=score >= 0.74,
        score=score,
        features=signal_features,
    )


def score_score_area(features: RegionFeatures) -> SignalResult:
    score = (
        0.55 * ratio_score(features.white_ratio, 0.105, 0.13)
        + 0.35 * ratio_score(features.edge_ratio, 0.12, 0.18)
        + 0.10 * ratio_score(features.std_luma, 65, 80)
    )
    return SignalResult(
        value=score >= 0.35,
        score=score,
        features=asdict(features),
    )


def score_rank(features: RegionFeatures) -> SignalResult:
    score = (
        0.50 * ratio_score(features.std_luma, 75, 88)
        + 0.35 * ratio_score(features.yellow_ratio, 0.18, 0.30)
        + 0.15 * ratio_score(features.edge_ratio, 0.075, 0.10)
    )
    return SignalResult(
        value=score >= 0.62,
        score=score,
        features=asdict(features),
    )


def classify(image: Image.Image, row: dict[str, str]) -> Classification:
    header = score_header(extract_features(crop_roi(image, ROI_DEFINITIONS["results_header"])))
    detail_region = crop_roi(image, ROI_DEFINITIONS["detail_result_panel"])
    detail = score_detail_panel(extract_features(detail_region), border_cyan_ratio(detail_region))
    score_area = score_score_area(extract_features(crop_roi(image, ROI_DEFINITIONS["score_area"])))
    rank = score_rank(extract_features(crop_roi(image, ROI_DEFINITIONS["rank"])))

    is_countup = Path(row["organized_file"]).name.startswith("transition_countup_")
    transition_kind = "countup" if is_countup else ""
    finished_result_frame = header.value and detail.value and not is_countup
    result_shape_candidate = header.value and detail.value and (
        score_area.value or rank.value or finished_result_frame
    )
    result_candidate = finished_result_frame
    expected = row["screen_type"] == "result"

    reasons: list[str] = []
    if header.value:
        reasons.append("header")
    if detail.value:
        reasons.append("detail_panel")
    if score_area.value:
        reasons.append("score_area")
    if rank.value:
        reasons.append("rank")
    if transition_kind:
        reasons.append(f"transition_{transition_kind}")
    if finished_result_frame:
        reasons.append("finished_result_frame")

    return Classification(
        organized_file=row["organized_file"],
        screen_type=row["screen_type"],
        result_candidate=result_candidate,
        result_shape_candidate=result_shape_candidate,
        transition_kind=transition_kind,
        expected_result_candidate=expected,
        correct=result_candidate == expected,
        header_signal=header,
        detail_panel_signal=detail,
        score_signal=score_area,
        rank_signal=rank,
        reason=",".join(reasons) if reasons else "no_signals",
    )


def read_metadata(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", newline="") as file:
        rows = list(csv.DictReader(file))
    required = {"organized_file", "screen_type"}
    missing = required - set(rows[0].keys() if rows else ())
    if missing:
        joined = ", ".join(sorted(missing))
        raise ValueError(f"metadata.csv is missing required columns: {joined}")
    return rows


def parse_manifest_timestamp(value: str, *, line_number: int) -> int:
    if value == "":
        raise ValueError(f"frame manifest line {line_number}: timestamp_ms is empty")
    try:
        timestamp_ms = int(value)
    except ValueError as exc:
        raise ValueError(
            f"frame manifest line {line_number}: timestamp_ms must be an integer: {value!r}"
        ) from exc
    if timestamp_ms < 0:
        raise ValueError(
            f"frame manifest line {line_number}: timestamp_ms must be non-negative: {value!r}"
        )
    return timestamp_ms


def resolve_manifest_image_path(
    raw_image_path: str,
    manifest_path: Path,
    frame_root: Path | None,
) -> Path:
    image_path = Path(raw_image_path)
    if image_path.is_absolute():
        return image_path
    root = frame_root if frame_root is not None else manifest_path.parent
    return root / image_path


def read_frame_manifest(path: Path, frame_root: Path | None = None) -> list[FrameInput]:
    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        required = {"image_path", "timestamp_ms"}
        missing = required - set(reader.fieldnames or ())
        if missing:
            joined = ", ".join(sorted(missing))
            raise ValueError(f"frame manifest is missing required columns: {joined}")

        frames: list[FrameInput] = []
        previous_timestamp_ms: int | None = None
        for row in reader:
            line_number = reader.line_num
            raw_image_path = row.get("image_path", "")
            if raw_image_path == "":
                raise ValueError(f"frame manifest line {line_number}: image_path is empty")

            timestamp_ms = parse_manifest_timestamp(
                row.get("timestamp_ms", ""),
                line_number=line_number,
            )
            if previous_timestamp_ms is not None and timestamp_ms <= previous_timestamp_ms:
                raise ValueError(
                    f"frame manifest line {line_number}: timestamp_ms must be strictly "
                    f"increasing; previous={previous_timestamp_ms}, current={timestamp_ms}"
                )
            previous_timestamp_ms = timestamp_ms

            image_path = resolve_manifest_image_path(raw_image_path, path, frame_root)
            if not image_path.exists():
                raise ValueError(
                    f"frame manifest line {line_number}: image_path does not exist: {image_path}"
                )

            frames.append(
                FrameInput(
                    row={
                        **{
                            key: value
                            for key, value in row.items()
                            if key not in {"image_path", "timestamp_ms"}
                        },
                        "organized_file": row.get("organized_file") or raw_image_path,
                        "screen_type": row.get("screen_type", ""),
                    },
                    image_path=image_path,
                    timestamp_ms=timestamp_ms,
                )
            )
    return frames


def parse_positive_fps(value: str) -> float:
    try:
        fps = float(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError(
            f"fps must be a finite number greater than 0: {value!r}"
        ) from exc
    if not math.isfinite(fps) or fps <= 0:
        raise argparse.ArgumentTypeError(f"fps must be a finite number greater than 0: {value!r}")
    return fps


def frame_timestamp_ms(index: int, fps: float, previous_timestamp_ms: int | None) -> int:
    timestamp_ms = round(index * 1000 / fps)
    if previous_timestamp_ms is not None and timestamp_ms <= previous_timestamp_ms:
        return previous_timestamp_ms + 1
    return timestamp_ms


def find_frame_images(frame_root: Path) -> list[Path]:
    if not frame_root.exists():
        raise ValueError(f"--frame-root does not exist: {frame_root}")
    if not frame_root.is_dir():
        raise ValueError(f"--frame-root must be a directory: {frame_root}")

    images = [
        path
        for path in frame_root.iterdir()
        if path.is_file() and path.suffix.lower() in FRAME_IMAGE_EXTENSIONS
    ]
    if not images:
        extensions = ", ".join(sorted(FRAME_IMAGE_EXTENSIONS))
        raise ValueError(f"--frame-root contains no frame images ({extensions}): {frame_root}")
    return sorted(images, key=lambda path: path.name.lower())


def ensure_data_output_path(path: Path, *, argument_name: str) -> None:
    data_root = (Path.cwd() / "data").resolve()
    resolved_path = path.resolve()
    if resolved_path == data_root or data_root in resolved_path.parents:
        return
    raise ValueError(f"{argument_name} must be under data/: {path}")


def iter_dry_run_capture_frames(frame_root: Path, fps: float) -> Iterable[DryRunCaptureFrame]:
    previous_timestamp_ms: int | None = None
    for index, image_path in enumerate(find_frame_images(frame_root)):
        timestamp_ms = frame_timestamp_ms(index, fps, previous_timestamp_ms)
        previous_timestamp_ms = timestamp_ms
        yield DryRunCaptureFrame(source_path=image_path, timestamp_ms=timestamp_ms)


def build_frame_manifest_rows(
    frame_root: Path,
    fps: float,
    *,
    screen_type: str | None = None,
) -> list[dict[str, str | int]]:
    rows: list[dict[str, str | int]] = []
    previous_timestamp_ms: int | None = None
    for index, image_path in enumerate(find_frame_images(frame_root)):
        timestamp_ms = frame_timestamp_ms(index, fps, previous_timestamp_ms)
        previous_timestamp_ms = timestamp_ms
        row: dict[str, str | int] = {
            "image_path": image_path.relative_to(frame_root).as_posix(),
            "timestamp_ms": timestamp_ms,
        }
        if screen_type is not None:
            row["screen_type"] = screen_type
        rows.append(row)
    return rows


def write_capture_frame_manifest(
    output_path: Path,
    frame_root: Path,
    fps: float,
    *,
    screen_type: str | None = None,
) -> int:
    parent = output_path.parent
    if parent != Path("") and not parent.exists():
        raise ValueError(f"--make-frame-manifest parent directory does not exist: {parent}")
    rows = build_frame_manifest_rows(frame_root, fps, screen_type=screen_type)
    fieldnames = ["image_path", "timestamp_ms"]
    if screen_type is not None:
        fieldnames.append("screen_type")
    with output_path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)
    return len(rows)


def write_capture_dry_run(
    output_dir: Path,
    frame_root: Path,
    fps: float,
    *,
    screen_type: str | None = None,
) -> tuple[Path, int]:
    ensure_data_output_path(output_dir, argument_name="--capture-dry-run-output")
    frames_dir = output_dir / "frames"
    frames_dir.mkdir(parents=True, exist_ok=True)

    frames: list[FrameInput] = []
    for capture_frame in iter_dry_run_capture_frames(frame_root, fps):
        destination = frames_dir / capture_frame.source_path.name
        shutil.copy2(capture_frame.source_path, destination)
        relative_image_path = destination.relative_to(output_dir).as_posix()
        row = {
            "organized_file": relative_image_path,
            "screen_type": "" if screen_type is None else screen_type,
        }
        frames.append(
            FrameInput(
                row=row,
                image_path=destination,
                timestamp_ms=capture_frame.timestamp_ms,
            )
        )

    manifest_path = output_dir / "frame_manifest.csv"
    write_frame_manifest(manifest_path, frames)
    return manifest_path, len(frames)


def write_capture_dry_run_scenario(
    output_dir: Path,
    scenario_manifest: Path,
    frame_root: Path | None = None,
) -> tuple[Path, int]:
    ensure_data_output_path(output_dir, argument_name="--capture-dry-run-output")
    source_frames = read_frame_manifest(scenario_manifest, frame_root)
    frames_dir = output_dir / "frames"
    frames_dir.mkdir(parents=True, exist_ok=True)

    frames: list[FrameInput] = []
    for index, source_frame in enumerate(source_frames, start=1):
        destination = frames_dir / (
            f"{source_frame.image_path.stem}_frame_{index:04d}{source_frame.image_path.suffix}"
        )
        shutil.copy2(source_frame.image_path, destination)
        relative_image_path = destination.relative_to(output_dir).as_posix()
        row = {
            **source_frame.row,
            "organized_file": relative_image_path,
        }
        frames.append(
            FrameInput(
                row=row,
                image_path=destination,
                timestamp_ms=source_frame.timestamp_ms,
            )
        )

    manifest_path = output_dir / "frame_manifest.csv"
    write_frame_manifest(manifest_path, frames)
    return manifest_path, len(frames)


def build_metadata_frame_inputs(
    rows: Iterable[dict[str, str]],
    screenshots_root: Path,
) -> list[FrameInput]:
    return [
        FrameInput(
            row=row,
            image_path=screenshots_root / row["organized_file"],
        )
        for row in rows
    ]


def build_timestamped_frame_inputs(
    rows: Iterable[dict[str, str]],
    screenshots_root: Path,
    *,
    start_ms: int = 0,
    frame_interval_ms: int = 1000,
) -> list[FrameInput]:
    timestamp_ms = start_ms
    frames: list[FrameInput] = []
    for row in rows:
        frames.append(
            FrameInput(
                row=row,
                image_path=screenshots_root / row["organized_file"],
                timestamp_ms=timestamp_ms,
            )
        )
        timestamp_ms += frame_interval_ms
    return frames


def select_non_result_reset_row(rows: Iterable[dict[str, str]]) -> dict[str, str]:
    row_list = list(rows)
    preferred_screen_types = {"menu_setup", "song_select", "gameplay"}
    for row in row_list:
        if row.get("screen_type", "") in preferred_screen_types:
            return row
    for row in row_list:
        if row.get("screen_type", "") != "result":
            return row
    raise ValueError("metadata contains no non-result row to use as an expanded manifest reset")


def build_m2_expanded_confirmed_events_frames(
    rows: Iterable[dict[str, str]],
    screenshots_root: Path,
    *,
    start_ms: int = 0,
    sequence_stride_ms: int = DUPLICATE_WINDOW_MS + 5_000,
    reset_to_result_ms: int = 1_000,
    result_frame_interval_ms: int = CONFIRMED_RESULT_MIN_DURATION_MS,
) -> list[FrameInput]:
    row_list = list(rows)
    reset_row = select_non_result_reset_row(row_list)
    result_rows = [row for row in row_list if row.get("screen_type", "") == "result"]
    if not result_rows:
        raise ValueError("metadata contains no result rows for expanded confirmed-events manifest")
    if sequence_stride_ms <= DUPLICATE_WINDOW_MS:
        raise ValueError("sequence_stride_ms must be greater than DUPLICATE_WINDOW_MS")
    if reset_to_result_ms <= 0:
        raise ValueError("reset_to_result_ms must be greater than 0")
    if result_frame_interval_ms < CONFIRMED_RESULT_MIN_DURATION_MS:
        raise ValueError(
            "result_frame_interval_ms must be at least CONFIRMED_RESULT_MIN_DURATION_MS"
        )

    frames: list[FrameInput] = []
    for index, result_row in enumerate(result_rows):
        base_timestamp_ms = start_ms + index * sequence_stride_ms
        frames.extend(
            [
                FrameInput(
                    row=reset_row,
                    image_path=screenshots_root / reset_row["organized_file"],
                    timestamp_ms=base_timestamp_ms,
                ),
                FrameInput(
                    row=result_row,
                    image_path=screenshots_root / result_row["organized_file"],
                    timestamp_ms=base_timestamp_ms + reset_to_result_ms,
                ),
                FrameInput(
                    row=result_row,
                    image_path=screenshots_root / result_row["organized_file"],
                    timestamp_ms=(
                        base_timestamp_ms + reset_to_result_ms + result_frame_interval_ms
                    ),
                ),
            ]
        )
    return frames


def write_m2_expanded_confirmed_events_manifest(
    output_dir: Path,
    rows: Iterable[dict[str, str]],
    screenshots_root: Path,
) -> tuple[Path, int, int]:
    ensure_data_output_path(output_dir, argument_name="--make-m2-expanded-manifest")
    output_dir.mkdir(parents=True, exist_ok=True)
    frames = build_m2_expanded_confirmed_events_frames(rows, screenshots_root)
    for frame in frames:
        if not frame.image_path.exists():
            raise ValueError(f"expanded manifest image_path does not exist: {frame.image_path}")
    manifest_path = output_dir / "frame_manifest.csv"
    write_frame_manifest(manifest_path, frames)
    result_count = sum(1 for frame in frames if frame.row.get("screen_type", "") == "result") // 2
    return manifest_path, len(frames), result_count


def write_frame_manifest(path: Path, frames: Iterable[FrameInput]) -> None:
    frame_list = list(frames)
    extra_fieldnames: list[str] = []
    for frame in frame_list:
        for key in frame.row:
            if (
                key not in {"organized_file", "image_path", "timestamp_ms"}
                and key not in extra_fieldnames
            ):
                extra_fieldnames.append(key)

    fieldnames = ["image_path", "timestamp_ms"]
    if "screen_type" in extra_fieldnames:
        fieldnames.append("screen_type")
        extra_fieldnames.remove("screen_type")
    fieldnames.extend(extra_fieldnames)

    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        for frame in frame_list:
            row = {
                key: value
                for key, value in frame.row.items()
                if key not in {"organized_file", "image_path", "timestamp_ms"}
            }
            row["image_path"] = frame.row["organized_file"]
            row["timestamp_ms"] = frame.timestamp_ms
            writer.writerow(row)


def write_ocr_expected_template(path: Path, frames: Iterable[FrameInput]) -> int:
    rows: list[dict[str, str]] = []
    fieldnames = ["organized_file", "screen_type", "score_digits", *JUDGMENT_OCR_ROIS]
    fieldnames.append("missing_judgment_rois")
    for frame in frames:
        row = frame.row
        if row.get("screen_type") != "result":
            continue
        template_row = {
            "organized_file": row["organized_file"],
            "screen_type": row.get("screen_type", ""),
            "score_digits": expected_ocr_value_from_row(row, "score_digits"),
        }
        missing_rois: list[str] = []
        for roi_name in JUDGMENT_OCR_ROIS:
            value = expected_ocr_value_from_row(row, roi_name)
            template_row[roi_name] = value
            if value == "":
                missing_rois.append(roi_name)
        if missing_rois:
            template_row["missing_judgment_rois"] = " ".join(missing_rois)
            rows.append(template_row)

    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        if rows:
            writer.writerows(rows)
    return len(rows)


def normalize_expected_text(value: str) -> str:
    return " ".join(value.strip().split())


def expected_m3_metadata_value_from_row(row: dict[str, str], field_name: str) -> str:
    for key in M3_METADATA_EXPECTED_COLUMN_KEYS[field_name]:
        value = normalize_expected_text(row.get(key, ""))
        if value:
            return value
    return ""


def is_song_select_grid_frame(frame: FrameInput) -> bool:
    row = frame.row
    organized_file = row.get("organized_file", "").lower()
    song_select_view = normalize_expected_text(row.get("song_select_view", "")).casefold()
    return row.get("screen_type") == "song_select" and (
        song_select_view == "grid" or "grid" in organized_file
    )


def m5_jacket_feature_label_template_rows(frames: Iterable[FrameInput]) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for frame in frames:
        if not is_song_select_grid_frame(frame):
            continue
        song_title = normalize_expected_text(frame.row.get("song_title", ""))
        expected_song_title = normalize_expected_text(frame.row.get("expected_song_title", ""))
        if song_title or expected_song_title:
            continue
        rows.append(
            {
                "organized_file": frame.row.get("organized_file", ""),
                "screen_type": frame.row.get("screen_type", ""),
                "song_select_view": frame.row.get("song_select_view", ""),
                "preview_visible": frame.row.get("preview_visible", ""),
                "song_title": "",
                "expected_song_title": "",
                "note": frame.row.get("note", ""),
            }
        )
    return rows


def m3_expected_coverage_status(expected_value_count: int, total: int) -> str:
    if total == 0 or expected_value_count == 0:
        return "no_expected_values"
    if expected_value_count == total:
        return "evaluated"
    return "partially_evaluated"


def summarize_m3_metadata_expected_coverage(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> dict[str, dict[str, int | str]]:
    buckets = {
        field_name: {"expected_value_count": 0, "no_expected_value_count": 0, "total": 0}
        for field_name in M3_METADATA_EXPECTED_FIELDS
    }
    for frame, event in zip(frames, events, strict=True):
        if not is_save_candidate_event(event):
            continue
        for field_name, bucket in buckets.items():
            bucket["total"] += 1
            if expected_m3_metadata_value_from_row(frame.row, field_name):
                bucket["expected_value_count"] += 1
            else:
                bucket["no_expected_value_count"] += 1

    return {
        field_name: {
            "evaluation_status": m3_expected_coverage_status(
                int(bucket["expected_value_count"]),
                int(bucket["total"]),
            ),
            "expected_value_count": bucket["expected_value_count"],
            "no_expected_value_count": bucket["no_expected_value_count"],
            "total_events": bucket["total"],
        }
        for field_name, bucket in buckets.items()
    }


def write_m3_metadata_expected_template(
    path: Path,
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> int:
    fieldnames = ["organized_file", "screen_type", *M3_METADATA_EXPECTED_FIELDS, "missing_fields"]
    rows: list[dict[str, str]] = []
    for frame, event in zip(frames, events, strict=True):
        if not is_save_candidate_event(event):
            continue
        missing_fields: list[str] = []
        template_row = {
            "organized_file": frame.row["organized_file"],
            "screen_type": frame.row.get("screen_type", ""),
        }
        for field_name in M3_METADATA_EXPECTED_FIELDS:
            value = expected_m3_metadata_value_from_row(frame.row, field_name)
            template_row[field_name] = value
            if value == "":
                missing_fields.append(field_name)
        if missing_fields:
            template_row["missing_fields"] = " ".join(missing_fields)
            rows.append(template_row)

    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        if rows:
            writer.writerows(rows)
    return len(rows)


def write_m3_metadata_expected_report(
    path: Path,
    coverage: dict[str, dict[str, int | str]],
) -> None:
    lines = [
        "# M3 Metadata Expected Coverage",
        "",
        "曲・譜面情報ROIの期待値列を、数字OCR expected coverage とは別に確認するための"
        "レポートです。",
        "対象は保存直前イベント境界の `confirmed_result=true` かつ `duplicate=false` のみです。",
        "",
        "| field | status | expected | missing | total confirmed-events |",
        "|---|---|---:|---:|---:|",
    ]
    for field_name in M3_METADATA_EXPECTED_FIELDS:
        item = coverage[field_name]
        lines.append(
            f"| `{field_name}` | `{item['evaluation_status']}` | "
            f"{item['expected_value_count']} | {item['no_expected_value_count']} | "
            f"{item['total_events']} |"
        )
    lines.extend(
        [
            "",
            "## 読み方",
            "",
            "- `evaluated`: confirmed-events 対象行すべてに期待値があります。",
            "- `partially_evaluated`: 一部の confirmed-events 対象行だけに期待値があります。",
            "- `no_expected_values`: confirmed-events 対象行に期待値がありません。",
            "",
            "`rank` は `rank` または `expected_rank` から読みます。`expected_rank` は数字OCRの "
            "`ocr_expected_coverage.md` には含めません。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def save_primary_rois(image: Image.Image, output_dir: Path, stem: str) -> None:
    target_dir = output_dir / "rois" / stem
    target_dir.mkdir(parents=True, exist_ok=True)
    for name in PRIMARY_ROIS:
        crop_roi(image, ROI_DEFINITIONS[name]).save(target_dir / f"{name}.png")


def normalize_digits(value: str) -> str:
    return "".join(re.findall(r"\d", value))


def canonical_ocr_digits(value: str) -> str:
    normalized = normalize_digits(value)
    if not normalized:
        return ""
    return normalized.lstrip("0") or "0"


def ocr_digits_match(normalized: str, expected: str) -> bool | None:
    if not normalized or not expected:
        return None
    return canonical_ocr_digits(normalized) == canonical_ocr_digits(expected)


def expected_score_from_row(row: dict[str, str]) -> str:
    for key in ("score", "expected_score"):
        normalized = normalize_digits(row.get(key, ""))
        if normalized:
            return normalized

    match = re.search(r"score(\d+)", Path(row["organized_file"]).stem, flags=re.IGNORECASE)
    return match.group(1) if match else ""


def expected_ocr_value_from_row(row: dict[str, str], roi_name: str) -> str:
    if roi_name == "score_digits":
        return expected_score_from_row(row)

    for key in (roi_name, f"expected_{roi_name}"):
        normalized = normalize_digits(row.get(key, ""))
        if normalized:
            return normalized
    return ""


def duplicate_key_for_classification(classification: Classification) -> str:
    match = re.search(
        r"score(\d+)",
        Path(classification.organized_file).stem,
        flags=re.IGNORECASE,
    )
    if match:
        return f"score:{match.group(1)}"
    return f"file:{Path(classification.organized_file).name}"


def build_result_events(
    classifications: Iterable[Classification],
    *,
    timestamps_ms: Iterable[int | None] | None = None,
    min_confirmed_frames: int = CONFIRMED_RESULT_MIN_FRAMES,
    min_confirmed_duration_ms: int = CONFIRMED_RESULT_MIN_DURATION_MS,
    duplicate_window_frames: int = DUPLICATE_WINDOW_FRAMES,
    duplicate_window_ms: int = DUPLICATE_WINDOW_MS,
) -> list[ResultEvent]:
    events: list[ResultEvent] = []
    candidate_streak = 0
    candidate_start_timestamp_ms: int | None = None
    last_confirmed_by_key: dict[str, tuple[int, int | None]] = {}
    timestamp_iter = iter(timestamps_ms) if timestamps_ms is not None else None

    for frame_index, classification in enumerate(classifications):
        if timestamp_iter is None:
            timestamp_ms = None
        else:
            try:
                timestamp_ms = next(timestamp_iter)
            except StopIteration as exc:
                msg = "timestamps_ms must contain one item per classification"
                raise ValueError(msg) from exc

        duplicate_key = duplicate_key_for_classification(classification)
        confirmed_result = False
        duplicate = False
        candidate_duration_ms: int | None = None
        confirmation_mode = "time" if timestamp_ms is not None else "frames"
        event_type = "none"
        reasons = [classification.reason]

        if classification.transition_kind == "countup" and classification.result_shape_candidate:
            candidate_streak = 0
            candidate_start_timestamp_ms = None
            event_type = "rejected_transition"
            reasons.append("transition_countup_shape_candidate")
        elif classification.result_candidate:
            candidate_streak += 1
            if timestamp_ms is not None:
                if candidate_start_timestamp_ms is None:
                    candidate_start_timestamp_ms = timestamp_ms
                candidate_duration_ms = max(0, timestamp_ms - candidate_start_timestamp_ms)
            reasons.append(f"candidate_streak={candidate_streak}")

            if timestamp_ms is not None:
                confirmed_result = candidate_duration_ms >= min_confirmed_duration_ms
            else:
                confirmed_result = candidate_streak >= min_confirmed_frames

            if confirmed_result:
                confirmed_result = True
                previous = last_confirmed_by_key.get(duplicate_key)
                if previous is None:
                    duplicate_distance: int | None = None
                    duplicate_reason = ""
                else:
                    previous_frame, previous_timestamp_ms = previous
                    if timestamp_ms is not None and previous_timestamp_ms is not None:
                        duplicate_distance = timestamp_ms - previous_timestamp_ms
                        duplicate_reason = f"duplicate_within_ms={duplicate_distance}"
                        duplicate_window = duplicate_window_ms
                    else:
                        duplicate_distance = frame_index - previous_frame
                        duplicate_reason = f"duplicate_within_frames={duplicate_distance}"
                        duplicate_window = duplicate_window_frames

                if duplicate_distance is not None and duplicate_distance <= duplicate_window:
                    duplicate = True
                    event_type = "duplicate"
                    reasons.append(duplicate_reason)
                else:
                    event_type = "confirmed"
                    if timestamp_ms is not None:
                        reasons.append(f"confirmed_after_ms={candidate_duration_ms}")
                    else:
                        reasons.append(f"confirmed_after_frames={candidate_streak}")
                last_confirmed_by_key[duplicate_key] = (frame_index, timestamp_ms)
        else:
            if candidate_streak:
                reasons.append(f"candidate_streak_reset={candidate_streak}")
            candidate_streak = 0
            candidate_start_timestamp_ms = None

        events.append(
            ResultEvent(
                frame_index=frame_index,
                organized_file=classification.organized_file,
                screen_type=classification.screen_type,
                result_candidate=classification.result_candidate,
                result_shape_candidate=classification.result_shape_candidate,
                confirmed_result=confirmed_result,
                event_type=event_type,
                duplicate=duplicate,
                duplicate_key=duplicate_key,
                timestamp_ms=timestamp_ms,
                candidate_duration_ms=candidate_duration_ms,
                confirmation_mode=confirmation_mode,
                reason=",".join(part for part in reasons if part),
            )
        )

    if timestamp_iter is not None:
        try:
            next(timestamp_iter)
        except StopIteration:
            pass
        else:
            msg = "timestamps_ms must contain one item per classification"
            raise ValueError(msg)

    return events


def preprocess_ocr_roi(
    image: Image.Image,
    roi_name: str,
    config: OcrPreprocessConfig = OCR_PREPROCESS_CONFIG,
) -> OcrPreprocessedImages:
    if roi_name not in OCR_ROIS:
        joined = ", ".join(OCR_ROIS)
        raise ValueError(f"unsupported OCR ROI: {roi_name}; expected one of: {joined}")

    original = crop_roi(image, ROI_DEFINITIONS[roi_name]).convert("RGB")
    enlarged = original.resize(
        (original.width * config.scale, original.height * config.scale),
        resample=Image.Resampling.LANCZOS,
    )
    if config.sharpen:
        enlarged = enlarged.filter(ImageFilter.SHARPEN)

    rgb = np.asarray(enlarged).astype(np.float32)
    red = rgb[:, :, 0]
    green = rgb[:, :, 1]
    blue = rgb[:, :, 2]
    luma = np.asarray(enlarged.convert("L")).astype(np.float32)
    channel_spread = np.maximum.reduce([red, green, blue]) - np.minimum.reduce([red, green, blue])

    white_text = (luma > config.luma_threshold) & (channel_spread < config.channel_spread_max)
    foreground = 0 if config.invert_to_black_text else 255
    background = 255 if config.invert_to_black_text else 0
    binary_array = np.where(white_text, foreground, background).astype(np.uint8)
    binary = Image.fromarray(binary_array).convert("L")
    if config.padding:
        binary = ImageOps.expand(binary, border=config.padding, fill=background)
    black_dilate_kernel = OCR_BINARY_BLACK_DILATE_KERNELS.get(roi_name)
    if black_dilate_kernel is not None:
        binary = binary.filter(ImageFilter.MinFilter(black_dilate_kernel))
    digit_focus_left_fraction = OCR_DIGIT_FOCUS_LEFT_FRACTIONS.get(roi_name)
    if digit_focus_left_fraction is not None:
        original = crop_right_fraction(original, digit_focus_left_fraction)
        enlarged = crop_right_fraction(enlarged, digit_focus_left_fraction)
        binary = crop_right_fraction(binary, digit_focus_left_fraction)
    return OcrPreprocessedImages(
        roi_name=roi_name,
        original=original,
        enlarged=enlarged,
        binary=binary,
    )


def preprocess_score_roi(image: Image.Image) -> tuple[Image.Image, Image.Image, Image.Image]:
    preprocessed = preprocess_ocr_roi(image, "score_digits")
    return preprocessed.original, preprocessed.enlarged, preprocessed.binary


def run_tesseract(
    binary: Image.Image,
    roi_name: str = "score_digits",
    config: TesseractConfig = TESSERACT_CONFIG,
) -> tuple[str, str, str, str]:
    tesseract = shutil.which("tesseract")
    if not tesseract:
        return "", "none", "engine_unavailable", "tesseract executable was not found"

    with tempfile.TemporaryDirectory() as temp_dir:
        input_path = Path(temp_dir) / f"{roi_name}.png"
        binary.save(input_path)
        command = [
            tesseract,
            str(input_path),
            "stdout",
            "--psm",
            str(config.psm),
            "--dpi",
            str(config.dpi),
        ]
        if config.lang:
            command.extend(["-l", config.lang])
        if config.whitelist is not None:
            command.extend(["-c", f"tessedit_char_whitelist={config.whitelist}"])
        completed = subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    raw = completed.stdout.strip()
    if completed.returncode != 0:
        error = completed.stderr.strip()
        return raw, "tesseract", "ocr_failed", error
    return raw, "tesseract", "ok", completed.stderr.strip()


def process_ocr_roi(
    image: Image.Image,
    row: dict[str, str],
    classification: Classification,
    output_dir: Path,
    roi_name: str,
    *,
    preprocess_config: OcrPreprocessConfig = OCR_PREPROCESS_CONFIG,
    output_root_name: str = "ocr",
) -> ScoreOcrResult:
    target_dir = output_dir / output_root_name / Path(row["organized_file"]).stem
    target_dir.mkdir(parents=True, exist_ok=True)

    preprocessed = preprocess_ocr_roi(image, roi_name, preprocess_config)
    original_path = target_dir / f"{roi_name}_original.png"
    enlarged_path = target_dir / f"{roi_name}_enlarged.png"
    binary_path = target_dir / f"{roi_name}_binary.png"
    preprocessed.original.save(original_path)
    preprocessed.enlarged.save(enlarged_path)
    preprocessed.binary.save(binary_path)

    raw, engine, status, error = run_tesseract(preprocessed.binary, roi_name)
    normalized = normalize_digits(raw)
    expected = expected_ocr_value_from_row(row, roi_name)
    match = ocr_digits_match(normalized, expected)

    return ScoreOcrResult(
        organized_file=row["organized_file"],
        screen_type=row["screen_type"],
        result_candidate=classification.result_candidate,
        roi_name=roi_name,
        score_ocr_raw=raw,
        score_ocr_normalized=normalized,
        expected_score=expected,
        match=match,
        engine=engine,
        status=status,
        error=error,
        original_path=str(original_path),
        enlarged_path=str(enlarged_path),
        binary_path=str(binary_path),
    )


def process_profile_ocr_roi(
    image: Image.Image,
    row: dict[str, str],
    classification: Classification,
    output_dir: Path,
    roi_name: str,
    profile: str,
    preprocess_config: OcrPreprocessConfig,
) -> ProfileScoreOcrResult:
    result = process_ocr_roi(
        image,
        row,
        classification,
        output_dir,
        roi_name,
        preprocess_config=preprocess_config,
        output_root_name=f"ocr_profiles/{profile}",
    )
    return ProfileScoreOcrResult(profile=profile, **asdict(result))


def process_score_ocr(
    image: Image.Image,
    row: dict[str, str],
    classification: Classification,
    output_dir: Path,
) -> ScoreOcrResult:
    return process_ocr_roi(image, row, classification, output_dir, "score_digits")


def normalize_m3_text_ocr_for_review(value: str) -> str:
    return normalize_expected_text(value.replace("\r", " ").replace("\n", " ").replace("\t", " "))


def preprocess_m3_text_ocr_roi(image: Image.Image, field_name: str) -> OcrPreprocessedImages:
    if field_name not in M3_SONG_ARTIST_OCR_FIELDS:
        joined = ", ".join(M3_SONG_ARTIST_OCR_FIELDS)
        raise ValueError(f"unsupported M3 text OCR field: {field_name}; expected one of: {joined}")

    original = crop_roi(image, ROI_DEFINITIONS[field_name]).convert("RGB")
    scale = 4 if field_name == "song_title" else 5
    enlarged = original.resize(
        (original.width * scale, original.height * scale),
        resample=Image.Resampling.LANCZOS,
    )
    enlarged = ImageOps.autocontrast(enlarged.convert("L")).filter(ImageFilter.SHARPEN)
    luma = np.asarray(enlarged).astype(np.float32)
    text_mask = luma > 125
    binary_array = np.where(text_mask, 0, 255).astype(np.uint8)
    binary = Image.fromarray(binary_array).convert("L")
    binary = ImageOps.expand(binary, border=24, fill=255)
    return OcrPreprocessedImages(
        roi_name=field_name,
        original=original,
        enlarged=enlarged.convert("RGB"),
        binary=binary,
    )


def m3_song_artist_ocr_failure_reason(
    *,
    status: str,
    pre_normalized_text: str,
    expected_value: str,
) -> str:
    if status == "engine_unavailable":
        return "engine_unavailable"
    if status == "ocr_failed":
        return "ocr_failed"
    if not pre_normalized_text:
        return "empty_ocr"
    if not expected_value:
        return "no_expected_value"
    return ""


def process_m3_song_artist_ocr_field(
    image: Image.Image,
    frame: FrameInput,
    event: ResultEvent,
    output_dir: Path,
    field_name: str,
) -> M3SongArtistOcrResult:
    target_dir = output_dir / "m3_song_artist_ocr_images" / frame.image_path.stem
    target_dir.mkdir(parents=True, exist_ok=True)

    preprocessed = preprocess_m3_text_ocr_roi(image, field_name)
    original_path = target_dir / f"{field_name}_original.png"
    enlarged_path = target_dir / f"{field_name}_enlarged.png"
    binary_path = target_dir / f"{field_name}_binary.png"
    preprocessed.original.save(original_path)
    preprocessed.enlarged.save(enlarged_path)
    preprocessed.binary.save(binary_path)

    raw, engine, status, error = run_tesseract(
        preprocessed.binary,
        field_name,
        M3_TEXT_TESSERACT_CONFIGS[field_name],
    )
    pre_normalized_text = normalize_m3_text_ocr_for_review(raw)
    expected_value = expected_m3_metadata_value_from_row(frame.row, field_name)
    failure_reason = m3_song_artist_ocr_failure_reason(
        status=status,
        pre_normalized_text=pre_normalized_text,
        expected_value=expected_value,
    )

    return M3SongArtistOcrResult(
        organized_file=frame.row["organized_file"],
        screen_type=frame.row.get("screen_type", ""),
        event_type=event.event_type,
        confirmed_result=event.confirmed_result,
        duplicate=event.duplicate,
        field_name=field_name,
        extractor=M3_SONG_ARTIST_OCR_METHOD,
        ocr_raw=raw,
        pre_normalized_text=pre_normalized_text,
        expected_value=expected_value,
        engine=engine,
        status=status,
        error=error,
        failure_reason=failure_reason,
        roi_path=m3_chart_field_roi_path(frame.image_path.stem, field_name),
        original_path=str(original_path),
        enlarged_path=str(enlarged_path),
        binary_path=str(binary_path),
    )


def flatten_classification(classification: Classification) -> dict[str, str | int | float | bool]:
    row: dict[str, str | int | float | bool] = {
        "organized_file": classification.organized_file,
        "screen_type": classification.screen_type,
        "result_candidate": classification.result_candidate,
        "result_shape_candidate": classification.result_shape_candidate,
        "transition_kind": classification.transition_kind,
        "expected_result_candidate": classification.expected_result_candidate,
        "correct": classification.correct,
        "reason": classification.reason,
    }
    signals = {
        "header": classification.header_signal,
        "detail_panel": classification.detail_panel_signal,
        "score": classification.score_signal,
        "rank": classification.rank_signal,
    }
    for name, signal in signals.items():
        row[f"{name}_value"] = signal.value
        row[f"{name}_score"] = round(signal.score, 6)
        for feature_name, value in signal.features.items():
            row[f"{name}_{feature_name}"] = round(value, 6) if isinstance(value, float) else value
    return row


def write_results_csv(path: Path, classifications: Iterable[Classification]) -> None:
    rows = [flatten_classification(item) for item in classifications]
    if not rows:
        return
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def flatten_score_ocr(result: ScoreOcrResult) -> dict[str, str | bool | None]:
    return asdict(result)


def write_score_ocr_csv(path: Path, results: Iterable[ScoreOcrResult]) -> None:
    rows = [flatten_score_ocr(item) for item in results]
    if not rows:
        return
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def flatten_profile_score_ocr(result: ProfileScoreOcrResult) -> dict[str, str | bool | None]:
    return asdict(result)


def write_profile_score_ocr_csv(path: Path, results: Iterable[ProfileScoreOcrResult]) -> None:
    rows = [flatten_profile_score_ocr(item) for item in results]
    if not rows:
        return
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def flatten_m3_song_artist_ocr(
    result: M3SongArtistOcrResult,
) -> dict[str, str | bool]:
    return asdict(result)


def write_m3_song_artist_ocr_csv(
    path: Path,
    results: Iterable[M3SongArtistOcrResult],
) -> None:
    rows = [flatten_m3_song_artist_ocr(item) for item in results]
    fieldnames = [
        "organized_file",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "field_name",
        "extractor",
        "ocr_raw",
        "pre_normalized_text",
        "expected_value",
        "engine",
        "status",
        "error",
        "failure_reason",
        "roi_path",
        "original_path",
        "enlarged_path",
        "binary_path",
    ]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        if rows:
            writer.writerows(rows)


def is_ocr_target(
    classification: Classification,
    event: ResultEvent,
    ocr_target_mode: str,
) -> bool:
    if ocr_target_mode == "result-candidate":
        return classification.result_candidate
    if ocr_target_mode == "confirmed-events":
        return is_save_candidate_event(event)
    joined = ", ".join(OCR_TARGET_MODES)
    raise ValueError(f"unsupported OCR target mode: {ocr_target_mode}; expected: {joined}")


def is_save_candidate_event(event: ResultEvent) -> bool:
    return event.confirmed_result and not event.duplicate


OcrResultLike = ScoreOcrResult | ProfileScoreOcrResult


def empty_ocr_count(results: Iterable[OcrResultLike]) -> int:
    return sum(result.status == "ok" and result.score_ocr_normalized == "" for result in results)


def no_expected_value_count(results: Iterable[OcrResultLike]) -> int:
    return sum(result.expected_score == "" for result in results)


def ocr_summary_bucket(results: Iterable[OcrResultLike]) -> dict[str, int]:
    result_rows = list(results)
    return {
        "total_ocr_attempts": len(result_rows),
        "ok_count": sum(result.status == "ok" for result in result_rows),
        "engine_unavailable_count": sum(
            result.status == "engine_unavailable" for result in result_rows
        ),
        "match_count": sum(result.match is True for result in result_rows),
        "mismatch_count": sum(result.match is False for result in result_rows),
        "empty_ocr_count": empty_ocr_count(result_rows),
        "no_expected_value_count": no_expected_value_count(result_rows),
    }


def ocr_evaluation_status(bucket: dict[str, int]) -> str:
    total = bucket["total_ocr_attempts"]
    expected_count = total - bucket["no_expected_value_count"]
    if total == 0 or expected_count == 0:
        return "no_expected_values"
    if expected_count == total:
        return "evaluated"
    return "partially_evaluated"


def expected_coverage_bucket(bucket: dict[str, int]) -> dict[str, int | str]:
    expected_count = bucket["total_ocr_attempts"] - bucket["no_expected_value_count"]
    return {
        "total_ocr_attempts": bucket["total_ocr_attempts"],
        "expected_value_count": expected_count,
        "no_expected_value_count": bucket["no_expected_value_count"],
        "evaluation_status": ocr_evaluation_status(bucket),
    }


def summarize_expected_coverage_by_roi(
    by_roi: dict[str, dict[str, int]],
) -> dict[str, dict[str, int | str]]:
    return {
        roi_name: expected_coverage_bucket(bucket)
        for roi_name, bucket in sorted(by_roi.items())
    }


def summarize_ocr_by_roi(results: Iterable[OcrResultLike]) -> dict[str, dict[str, int]]:
    buckets: dict[str, list[OcrResultLike]] = {}
    for result in results:
        buckets.setdefault(result.roi_name, []).append(result)
    return {roi_name: ocr_summary_bucket(rows) for roi_name, rows in sorted(buckets.items())}


def summarize_ocr_by_status(results: Iterable[OcrResultLike]) -> dict[str, int]:
    by_status: dict[str, int] = {}
    for result in results:
        by_status[result.status] = by_status.get(result.status, 0) + 1
    return dict(sorted(by_status.items()))


def summarize_ocr_failure_reasons(results: Iterable[OcrResultLike]) -> dict[str, int]:
    result_rows = list(results)
    return {
        "engine_unavailable": sum(
            result.status == "engine_unavailable" for result in result_rows
        ),
        "ocr_failed": sum(result.status == "ocr_failed" for result in result_rows),
        "empty_ocr": empty_ocr_count(result_rows),
        "mismatch": sum(result.match is False for result in result_rows),
        "no_expected_value": no_expected_value_count(result_rows),
    }


def summarize_score_ocr(
    results: Iterable[ScoreOcrResult],
    events: Iterable[ResultEvent],
    ocr_target_mode: str,
) -> ScoreOcrSummary:
    result_rows = list(results)
    event_rows = list(events)
    skipped_duplicate_count = 0
    skipped_unconfirmed_count = 0
    skipped_rejected_transition_count = 0
    if ocr_target_mode == "confirmed-events":
        skipped_duplicate_count = sum(event.duplicate for event in event_rows)
        skipped_rejected_transition_count = sum(
            event.event_type == "rejected_transition" for event in event_rows
        )
        skipped_unconfirmed_count = sum(
            not event.confirmed_result and not event.duplicate for event in event_rows
        )

    bucket = ocr_summary_bucket(result_rows)
    by_roi = summarize_ocr_by_roi(result_rows)
    return ScoreOcrSummary(
        total_ocr_attempts=bucket["total_ocr_attempts"],
        ok_count=bucket["ok_count"],
        engine_unavailable_count=bucket["engine_unavailable_count"],
        match_count=bucket["match_count"],
        mismatch_count=bucket["mismatch_count"],
        empty_ocr_count=bucket["empty_ocr_count"],
        no_expected_value_count=bucket["no_expected_value_count"],
        skipped_duplicate_count=skipped_duplicate_count,
        skipped_unconfirmed_count=skipped_unconfirmed_count,
        skipped_rejected_transition_count=skipped_rejected_transition_count,
        ocr_target_mode=ocr_target_mode,
        by_roi=by_roi,
        by_status=summarize_ocr_by_status(result_rows),
        failure_reasons=summarize_ocr_failure_reasons(result_rows),
        expected_coverage_by_roi=summarize_expected_coverage_by_roi(by_roi),
    )


def summarize_profile_score_ocr(
    results: Iterable[ProfileScoreOcrResult],
    ocr_target_mode: str,
) -> dict[str, object]:
    result_rows = list(results)
    by_profile: dict[str, dict[str, dict[str, int]]] = {}
    roi_names: set[str] = set()
    for result in result_rows:
        roi_names.add(result.roi_name)

    profile_buckets: dict[str, dict[str, list[ProfileScoreOcrResult]]] = {}
    for result in result_rows:
        roi_buckets = profile_buckets.setdefault(result.profile, {})
        roi_buckets.setdefault(result.roi_name, []).append(result)

    for profile, roi_buckets in sorted(profile_buckets.items()):
        by_profile[profile] = {
            roi_name: ocr_summary_bucket(rows) for roi_name, rows in sorted(roi_buckets.items())
        }

    best_by_roi: dict[str, dict[str, object]] = {}
    for roi_name in sorted(roi_names):
        candidates = [
            (profile, by_profile[profile][roi_name])
            for profile in sorted(by_profile)
            if roi_name in by_profile[profile]
        ]
        if not candidates:
            continue
        best_match_count = max(bucket["match_count"] for _, bucket in candidates)
        lowest_empty_count = min(bucket["empty_ocr_count"] for _, bucket in candidates)
        total_attempts = max(bucket["total_ocr_attempts"] for _, bucket in candidates)
        no_expected_count = max(bucket["no_expected_value_count"] for _, bucket in candidates)
        expected_value_count = max(
            bucket["total_ocr_attempts"] - bucket["no_expected_value_count"]
            for _, bucket in candidates
        )
        evaluation_status = (
            "no_expected_values"
            if expected_value_count == 0
            else "evaluated"
            if all(
                bucket["no_expected_value_count"] == 0
                and bucket["total_ocr_attempts"] == total_attempts
                for _, bucket in candidates
            )
            else "partially_evaluated"
        )
        recommendation_basis = (
            "match_count"
            if evaluation_status == "evaluated"
            else "match_count_partial"
            if evaluation_status == "partially_evaluated"
            else "empty_ocr_reference_only"
        )
        ranked_candidates = sorted(
            candidates,
            key=lambda item: (
                -item[1]["match_count"],
                item[1]["mismatch_count"],
                item[1]["empty_ocr_count"],
                item[0],
            ),
        )
        recommended_profiles = (
            [
                profile
                for profile, bucket in ranked_candidates
                if bucket["match_count"] == ranked_candidates[0][1]["match_count"]
                and bucket["mismatch_count"] == ranked_candidates[0][1]["mismatch_count"]
                and bucket["empty_ocr_count"] == ranked_candidates[0][1]["empty_ocr_count"]
            ]
            if evaluation_status != "no_expected_values"
            else []
        )
        reference_profiles = [
            profile
            for profile, bucket in candidates
            if bucket["empty_ocr_count"] == lowest_empty_count
        ]
        default_counts = next(
            (bucket for profile, bucket in candidates if profile == "default"),
            None,
        )
        top_recommended_profile = recommended_profiles[0] if recommended_profiles else ""
        top_recommended_counts = next(
            (
                bucket
                for profile, bucket in candidates
                if profile == top_recommended_profile
            ),
            None,
        )
        recommended_vs_default_delta = (
            {
                "match_count": (
                    top_recommended_counts["match_count"] - default_counts["match_count"]
                ),
                "mismatch_count": (
                    top_recommended_counts["mismatch_count"]
                    - default_counts["mismatch_count"]
                ),
                "empty_ocr_count": (
                    top_recommended_counts["empty_ocr_count"]
                    - default_counts["empty_ocr_count"]
                ),
            }
            if default_counts is not None and top_recommended_counts is not None
            else {}
        )
        recommendation_readiness = (
            "adoption_candidate"
            if evaluation_status == "evaluated" and recommended_profiles
            else "tentative"
            if evaluation_status == "partially_evaluated" and recommended_profiles
            else "reference_only"
        )
        if evaluation_status == "evaluated":
            recommendation_basis_detail = (
                "evaluated: recommended profiles maximize match_count, then minimize "
                "mismatch_count and empty_ocr_count."
            )
        elif evaluation_status == "partially_evaluated":
            recommendation_basis_detail = (
                "tentative: only rows with expected values affect match/mismatch; "
                "fill remaining metadata before treating this as final."
            )
        else:
            recommendation_basis_detail = (
                "reference only: no expected values are present, so empty_ocr_count can "
                "guide image inspection but is not an accuracy success."
            )
        best_by_roi[roi_name] = {
            "best_match_profiles": [
                profile
                for profile, bucket in candidates
                if bucket["match_count"] == best_match_count
            ],
            "lowest_empty_profiles": [
                profile
                for profile, bucket in candidates
                if bucket["empty_ocr_count"] == lowest_empty_count
            ],
            "best_match_count": best_match_count,
            "lowest_empty_ocr_count": lowest_empty_count,
            "evaluation_status": evaluation_status,
            "expected_value_count": expected_value_count,
            "no_expected_value_count": no_expected_count,
            "recommendation_basis": recommendation_basis,
            "match_recommendation_evaluated": evaluation_status != "no_expected_values",
            "recommended_profiles": recommended_profiles,
            "reference_profiles": reference_profiles,
            "default_profile": "default" if default_counts is not None else "",
            "default_profile_counts": default_counts or {},
            "top_recommended_profile": top_recommended_profile,
            "top_recommended_profile_counts": top_recommended_counts or {},
            "recommended_vs_default_delta": recommended_vs_default_delta,
            "default_is_recommended": "default" in recommended_profiles,
            "recommendation_readiness": recommendation_readiness,
            "recommendation_basis_detail": recommendation_basis_detail,
            "recommendation_is_tentative": evaluation_status == "partially_evaluated",
        }

    return {
        "ocr_target_mode": ocr_target_mode,
        "profiles": by_profile,
        "best_by_roi": best_by_roi,
    }


def summarize_m3_song_artist_ocr(
    results: Iterable[M3SongArtistOcrResult],
    events: Iterable[ResultEvent],
) -> dict[str, object]:
    result_rows = list(results)
    event_rows = list(events)
    field_buckets = {
        field_name: {
            "total_attempts": 0,
            "ok_count": 0,
            "engine_unavailable_count": 0,
            "ocr_failed_count": 0,
            "empty_ocr_count": 0,
            "no_expected_value_count": 0,
        }
        for field_name in M3_SONG_ARTIST_OCR_FIELDS
    }
    by_status: dict[str, int] = {}
    failure_reason_counts: dict[str, int] = {}
    for result in result_rows:
        bucket = field_buckets[result.field_name]
        bucket["total_attempts"] += 1
        by_status[result.status] = by_status.get(result.status, 0) + 1
        if result.status == "ok":
            bucket["ok_count"] += 1
        elif result.status == "engine_unavailable":
            bucket["engine_unavailable_count"] += 1
        elif result.status == "ocr_failed":
            bucket["ocr_failed_count"] += 1
        if result.failure_reason == "empty_ocr":
            bucket["empty_ocr_count"] += 1
        elif result.failure_reason == "no_expected_value":
            bucket["no_expected_value_count"] += 1
        if result.failure_reason:
            failure_reason_counts[result.failure_reason] = (
                failure_reason_counts.get(result.failure_reason, 0) + 1
            )

    skipped_counts = {
        "duplicate": sum(event.duplicate for event in event_rows),
        "rejected_transition": sum(
            event.event_type == "rejected_transition" for event in event_rows
        ),
        "unconfirmed": sum(
            event.result_candidate
            and not event.confirmed_result
            and not event.duplicate
            and event.event_type != "rejected_transition"
            for event in event_rows
        ),
        "non_result": sum(
            not event.result_candidate
            and event.event_type != "rejected_transition"
            for event in event_rows
        ),
    }
    target_count = sum(is_save_candidate_event(event) for event in event_rows)
    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "scope": "M3-4 song_title/artist OCR entry report",
        "extractor": M3_SONG_ARTIST_OCR_METHOD,
        "fields": field_buckets,
        "total_events": len(event_rows),
        "target_count": target_count,
        "total_attempts": len(result_rows),
        "expected_attempts_if_enabled": target_count * len(M3_SONG_ARTIST_OCR_FIELDS),
        "skipped_counts": skipped_counts,
        "by_status": dict(sorted(by_status.items())),
        "failure_reason_counts": dict(sorted(failure_reason_counts.items())),
        "reading_notes": [
            "OCR raw text and pre_normalized_text are inspection inputs only.",
            "No master matching, fuzzy matching, or song title normalization is performed.",
            "artist is auxiliary because its ROI can be clipped on long names.",
        ],
    }


def representative_m3_song_artist_rows(
    rows: Iterable[M3SongArtistOcrResult],
    *,
    field_name: str,
    limit: int = 8,
) -> list[M3SongArtistOcrResult]:
    representatives: list[M3SongArtistOcrResult] = []
    seen: set[str] = set()
    for row in rows:
        if row.field_name != field_name or row.organized_file in seen:
            continue
        representatives.append(row)
        seen.add(row.organized_file)
        if len(representatives) >= limit:
            break
    return representatives


def write_m3_song_artist_ocr_report(
    path: Path,
    rows: Iterable[M3SongArtistOcrResult],
    summary: dict[str, object],
) -> None:
    row_list = list(rows)
    fields = summary["fields"]
    assert isinstance(fields, dict)
    lines = [
        "# M3 Song / Artist OCR Entry",
        "",
        "`song_title` / `artist` ROI のOCR入口レポートです。",
        "confirmed-events 境界だけを対象にし、マスタ照合、ファジーマッチ、"
        "曲名正規化の本格実装には進みません。",
        "",
        f"- target boundary: `{summary['target_boundary']}`",
        f"- extractor: `{summary['extractor']}`",
        f"- target confirmed-events: {summary['target_count']}",
        f"- total OCR attempts: {summary['total_attempts']}",
        "- failure_reason vocabulary: `engine_unavailable` / `ocr_failed` / "
        "`empty_ocr` / `no_expected_value`",
        "",
        "## Field Summary",
        "",
        "| field | attempts | ok | engine unavailable | ocr failed | empty | no expected |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for field_name in M3_SONG_ARTIST_OCR_FIELDS:
        bucket = fields[field_name]
        assert isinstance(bucket, dict)
        lines.append(
            f"| `{field_name}` | {bucket['total_attempts']} | {bucket['ok_count']} | "
            f"{bucket['engine_unavailable_count']} | {bucket['ocr_failed_count']} | "
            f"{bucket['empty_ocr_count']} | {bucket['no_expected_value_count']} |"
        )

    lines.extend(
        [
            "",
            "## Representative Rows",
            "",
        ]
    )
    for field_name in M3_SONG_ARTIST_OCR_FIELDS:
        lines.extend(
            [
                f"### `{field_name}`",
                "",
                "| organized_file | expected | pre-normalized OCR | status | failure | roi |",
                "|---|---|---|---|---|---|",
            ]
        )
        representatives = representative_m3_song_artist_rows(row_list, field_name=field_name)
        if not representatives:
            lines.append("| - | - | - | - | - | - |")
        else:
            for row in representatives:
                lines.append(
                    f"| `{row.organized_file}` | `{row.expected_value}` | "
                    f"`{row.pre_normalized_text}` | `{row.status}` | "
                    f"`{row.failure_reason}` | `{row.roi_path}` |"
                )
        lines.append("")

    lines.extend(
        [
            "## Reading Notes",
            "",
            "- `ocr_raw` はTesseract出力そのもの、`pre_normalized_text` は改行と連続空白だけを"
            "レビュー用に畳んだ文字列です。",
            "- `song_title` は主要項目、`artist` は左右切れがある補助項目として読みます。",
            "- OCRエンジンがない環境では `engine_unavailable` として記録し、"
            "PoC全体は落としません。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def summarize_m3_song_artist_ocr_entry_failures(
    rows: Iterable[M3SongArtistOcrResult],
    representative_limit: int = M3_SAVE_CANDIDATE_BLOCKER_REPRESENTATIVE_LIMIT,
) -> dict[str, object]:
    row_list = [
        row
        for row in rows
        if row.failure_reason in M3_SONG_ARTIST_ENTRY_FAILURE_REASONS
    ]
    fields: dict[str, dict[str, object]] = {}
    total_failures = 0
    affected_files: set[str] = set()

    for field_name in M3_SONG_ARTIST_OCR_FIELDS:
        field_rows = [row for row in row_list if row.field_name == field_name]
        total_failures += len(field_rows)
        affected_files.update(row.organized_file for row in field_rows)
        failure_reason_counts: dict[str, int] = {}
        expected_value_counts: dict[str, int] = {}
        representatives: list[dict[str, str]] = []
        seen_files: set[str] = set()
        for row in field_rows:
            failure_reason_counts[row.failure_reason] = (
                failure_reason_counts.get(row.failure_reason, 0) + 1
            )
            expected_value = row.expected_value or "(empty)"
            expected_value_counts[expected_value] = expected_value_counts.get(expected_value, 0) + 1
            if row.organized_file in seen_files:
                continue
            if len(representatives) < representative_limit:
                representatives.append(
                    {
                        "organized_file": row.organized_file,
                        "expected_value": row.expected_value,
                        "pre_normalized_text": row.pre_normalized_text,
                        "status": row.status,
                        "failure_reason": row.failure_reason,
                        "extractor": row.extractor,
                        "roi_path": row.roi_path,
                        "binary_path": row.binary_path,
                    }
                )
            seen_files.add(row.organized_file)

        fields[field_name] = {
            "field_role": M3_SONG_ARTIST_FIELD_ROLES[field_name],
            "failure_count": len(field_rows),
            "failure_reason_counts": dict(sorted(failure_reason_counts.items())),
            "expected_value_counts": dict(
                sorted(expected_value_counts.items(), key=lambda item: (-item[1], item[0]))
            ),
            "representatives": representatives,
            "reading_note": (
                "primary item: inspect as song_title OCR entry failures"
                if field_name == "song_title"
                else "auxiliary item: inspect separately because artist ROI can be clipped"
            ),
        }

    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "scope": "M3-9 song_title/artist OCR entry failure representatives",
        "failure_reason_scope": list(M3_SONG_ARTIST_ENTRY_FAILURE_REASONS),
        "representative_limit_per_field": representative_limit,
        "failure_count": total_failures,
        "affected_candidate_count": len(affected_files),
        "fields": fields,
        "reading_notes": [
            "This report is derived from the M3-4 OCR entry rows for confirmed-events only.",
            "song_title is the primary item and artist is an auxiliary clipped-reference item.",
            "Do not merge song_title and artist counts into one improvement target.",
            "This is not a DB save decision, master matching result, fuzzy match, "
            "or normalization result.",
        ],
    }


def write_m3_song_artist_ocr_entry_failures_report(
    path: Path,
    summary: dict[str, object],
) -> None:
    fields = summary["fields"]
    assert isinstance(fields, dict)
    lines = [
        "# M3 Song / Artist OCR Entry Failures",
        "",
        "M3-9として、`song_title` と `artist` のOCR入口失敗代表を分けて読むための"
        "レビュー補助です。",
        "曲名正規化、ファジーマッチ、マスタ照合、DB保存可否判定には進みません。",
        "",
        f"- target boundary: `{summary['target_boundary']}`",
        f"- failure reasons: `{', '.join(summary['failure_reason_scope'])}`",
        f"- entry failures: {summary['failure_count']}",
        f"- affected confirmed-events: {summary['affected_candidate_count']}",
        "",
        "## Field Failure Summary",
        "",
        "| field | role | failures | failure reasons |",
        "|---|---|---:|---|",
    ]
    for field_name in M3_SONG_ARTIST_OCR_FIELDS:
        bucket = fields[field_name]
        assert isinstance(bucket, dict)
        lines.append(
            f"| `{field_name}` | `{bucket['field_role']}` | {bucket['failure_count']} | "
            f"`{json.dumps(bucket['failure_reason_counts'], ensure_ascii=False, sort_keys=True)}` |"
        )

    lines.extend(["", "## Representatives", ""])
    for field_name in M3_SONG_ARTIST_OCR_FIELDS:
        bucket = fields[field_name]
        assert isinstance(bucket, dict)
        representatives = bucket["representatives"]
        assert isinstance(representatives, list)
        lines.extend(
            [
                f"### `{field_name}`",
                "",
                f"- role: `{bucket['field_role']}`",
                f"- note: {bucket['reading_note']}",
                "",
                "| organized_file | expected | pre-normalized OCR | status | failure | "
                "roi | binary |",
                "|---|---|---|---|---|---|---|",
            ]
        )
        if not representatives:
            lines.append("| - | - | - | - | - | - | - |")
        else:
            for representative in representatives:
                assert isinstance(representative, dict)
                lines.append(
                    f"| `{representative['organized_file']}` | "
                    f"`{representative['expected_value']}` | "
                    f"`{representative['pre_normalized_text']}` | "
                    f"`{representative['status']}` | "
                    f"`{representative['failure_reason']}` | "
                    f"`{representative['roi_path']}` | "
                    f"`{representative['binary_path']}` |"
                )
        lines.append("")

    lines.extend(
        [
            "## Reading Notes",
            "",
            "- `song_title` は主要項目のOCR入口失敗代表として読みます。",
            "- `artist` は左右切れがある補助項目のOCR入口失敗代表として、"
            "`song_title` と混ぜずに読みます。",
            "- `empty_ocr` はOCR入口の空読みであり、曲名正規化、ファジーマッチ、"
            "マスタ照合の失敗判定ではありません。",
            "- duplicate、`rejected_transition`、未確定候補、non-result は"
            "confirmed-events 境界外です。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def representative_files(
    results: Iterable[OcrResultLike],
    *,
    reason: str,
    limit: int = 5,
) -> list[str]:
    files: list[str] = []
    seen: set[str] = set()
    for result in results:
        matched = (
            (reason == "mismatch" and result.match is False)
            or (
                reason == "empty_ocr"
                and result.status == "ok"
                and result.score_ocr_normalized == ""
            )
        )
        if matched and result.organized_file not in seen:
            files.append(result.organized_file)
            seen.add(result.organized_file)
            if len(files) >= limit:
                break
    return files


def format_representative_list(files: list[str]) -> str:
    return ", ".join(f"`{name}`" for name in files) if files else "none"


def profile_recommendation_for_roi(
    profile_summary: dict[str, object] | None,
    roi_name: str,
) -> dict[str, object] | None:
    if profile_summary is None:
        return None
    best_by_roi = profile_summary.get("best_by_roi")
    if not isinstance(best_by_roi, dict):
        return None
    bucket = best_by_roi.get(roi_name)
    return bucket if isinstance(bucket, dict) else None


def format_profile_list(value: object) -> str:
    if not isinstance(value, list) or not value:
        return "none"
    return ", ".join(f"`{item}`" for item in value if isinstance(item, str))


def format_profile_counts(value: object) -> str:
    if not isinstance(value, dict) or not value:
        return "none"
    return (
        f"match={value.get('match_count', 0)}, "
        f"mismatch={value.get('mismatch_count', 0)}, "
        f"empty={value.get('empty_ocr_count', 0)}, "
        f"no_expected={value.get('no_expected_value_count', 0)}"
    )


def format_profile_delta(value: object) -> str:
    if not isinstance(value, dict) or not value:
        return "none"
    return (
        f"match={value.get('match_count', 0):+}, "
        f"mismatch={value.get('mismatch_count', 0):+}, "
        f"empty={value.get('empty_ocr_count', 0):+}"
    )


def representative_path_hint(
    roi_name: str,
    files: list[str],
    *,
    output_root_name: str = "ocr",
) -> str:
    if not files:
        return "none"
    stem = Path(files[0]).stem
    return f"`{output_root_name}/{stem}/{roi_name}_binary.png`"


def write_ocr_roi_report(
    path: Path,
    results: Iterable[ScoreOcrResult],
    summary: ScoreOcrSummary,
    profile_summary: dict[str, object] | None = None,
) -> None:
    result_rows = list(results)
    rows_by_roi: dict[str, list[ScoreOcrResult]] = {}
    for result in result_rows:
        rows_by_roi.setdefault(result.roi_name, []).append(result)

    lines = [
        "# OCR ROI Weakness Report",
        "",
        f"- OCR target mode: `{summary.ocr_target_mode}`",
        f"- Total OCR attempts: {summary.total_ocr_attempts}",
        "",
        "| ROI | evaluation_status | total | match | mismatch | empty | no expected | "
        "engine unavailable |",
        "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |",
    ]
    for roi_name, bucket in sorted(summary.by_roi.items()):
        lines.append(
            f"| `{roi_name}` | `{ocr_evaluation_status(bucket)}` | "
            f"{bucket['total_ocr_attempts']} | {bucket['match_count']} | "
            f"{bucket['mismatch_count']} | {bucket['empty_ocr_count']} | "
            f"{bucket['no_expected_value_count']} | {bucket['engine_unavailable_count']} |"
        )

    lines.extend(["", "## Representative Failures", ""])
    for roi_name in sorted(rows_by_roi):
        roi_rows = rows_by_roi[roi_name]
        mismatches = representative_files(roi_rows, reason="mismatch")
        empties = representative_files(roi_rows, reason="empty_ocr")
        recommendation = profile_recommendation_for_roi(profile_summary, roi_name)
        if recommendation is None:
            recommended_profiles = "not run"
            recommendation_basis = "default_only"
            recommendation_detail = "Run with `--ocr-profile all` to compare profiles."
        else:
            recommended_profiles = format_profile_list(recommendation.get("recommended_profiles"))
            if recommended_profiles == "none":
                recommended_profiles = (
                    "reference only: "
                    + format_profile_list(recommendation.get("reference_profiles"))
                )
            recommendation_basis = str(recommendation.get("recommendation_basis", ""))
            recommendation_detail = str(recommendation.get("recommendation_basis_detail", ""))
            recommendation_readiness = str(
                recommendation.get("recommendation_readiness", "")
            )
            default_profile_counts = format_profile_counts(
                recommendation.get("default_profile_counts")
            )
            top_recommended_profile = str(
                recommendation.get("top_recommended_profile", "")
            )
            top_recommended_counts = format_profile_counts(
                recommendation.get("top_recommended_profile_counts")
            )
            recommended_delta = format_profile_delta(
                recommendation.get("recommended_vs_default_delta")
            )
        lines.extend([f"### `{roi_name}`", ""])
        lines.append(f"- evaluation_status: `{ocr_evaluation_status(summary.by_roi[roi_name])}`")
        lines.append(f"- recommended profile candidate: {recommended_profiles}")
        lines.append(f"- recommendation_basis: `{recommendation_basis}`")
        lines.append(f"- recommendation note: {recommendation_detail}")
        if recommendation is not None:
            lines.append(f"- recommendation_readiness: `{recommendation_readiness}`")
            lines.append(f"- default profile counts: {default_profile_counts}")
            lines.append(
                "- top recommended profile counts: "
                f"`{top_recommended_profile}` {top_recommended_counts}"
            )
            lines.append(f"- recommended vs default delta: {recommended_delta}")
        lines.append(f"- representative mismatch: {format_representative_list(mismatches)}")
        lines.append(f"- representative empty_ocr: {format_representative_list(empties)}")
        lines.append(
            "- next preprocessing image hint: "
            f"mismatch={representative_path_hint(roi_name, mismatches)}, "
            f"empty_ocr={representative_path_hint(roi_name, empties)}"
        )
        lines.append("")

    path.write_text("\n".join(lines), encoding="utf-8")


def m3_chart_field_exclusion_reason(event: ResultEvent) -> str:
    if is_save_candidate_event(event):
        return ""
    if event.duplicate:
        return "duplicate"
    if event.event_type == "rejected_transition":
        return "rejected_transition"
    if event.result_candidate:
        return "unconfirmed"
    return "non_result"


def m3_chart_field_roi_path(image_stem: str, field_name: str) -> str:
    return f"rois/{image_stem}/{field_name}.png"


def m3_chart_field_rows(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for frame, event in zip(frames, events, strict=True):
        target = is_save_candidate_event(event)
        row = {
            "organized_file": frame.row["organized_file"],
            "screen_type": frame.row.get("screen_type", ""),
            "event_type": event.event_type,
            "confirmed_result": str(event.confirmed_result),
            "duplicate": str(event.duplicate),
            "chart_field_target": str(target),
            "exclusion_reason": m3_chart_field_exclusion_reason(event),
        }
        for field_name in M3_CHART_FIELD_FIELDS:
            row[f"expected_{field_name}"] = expected_m3_metadata_value_from_row(
                frame.row,
                field_name,
            )
            row[f"{field_name}_roi_path"] = m3_chart_field_roi_path(
                frame.image_path.stem,
                field_name,
            )
        rows.append(row)
    return rows


def write_m3_chart_fields_csv(
    path: Path,
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> None:
    fieldnames = [
        "organized_file",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "chart_field_target",
        "exclusion_reason",
    ]
    for field_name in M3_CHART_FIELD_FIELDS:
        fieldnames.append(f"expected_{field_name}")
        fieldnames.append(f"{field_name}_roi_path")

    rows = m3_chart_field_rows(frames, events)
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def summarize_m3_chart_fields(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> dict[str, object]:
    total_events = 0
    target_count = 0
    excluded_counts = {
        "duplicate": 0,
        "rejected_transition": 0,
        "unconfirmed": 0,
        "non_result": 0,
    }
    coverage = {
        field_name: {"expected_value_count": 0, "no_expected_value_count": 0, "total": 0}
        for field_name in M3_CHART_FIELD_FIELDS
    }
    for frame, event in zip(frames, events, strict=True):
        total_events += 1
        if not is_save_candidate_event(event):
            reason = m3_chart_field_exclusion_reason(event)
            excluded_counts[reason] = excluded_counts.get(reason, 0) + 1
            continue

        target_count += 1
        for field_name, bucket in coverage.items():
            bucket["total"] += 1
            if expected_m3_metadata_value_from_row(frame.row, field_name):
                bucket["expected_value_count"] += 1
            else:
                bucket["no_expected_value_count"] += 1

    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "total_events": total_events,
        "chart_field_target_count": target_count,
        "excluded_counts": excluded_counts,
        "fields": {
            field_name: {
                "evaluation_status": m3_expected_coverage_status(
                    int(bucket["expected_value_count"]),
                    int(bucket["total"]),
                ),
                "expected_value_count": bucket["expected_value_count"],
                "no_expected_value_count": bucket["no_expected_value_count"],
                "total_events": bucket["total"],
            }
            for field_name, bucket in coverage.items()
        },
    }


def normalize_m3_chart_field_value(field_name: str, value: str) -> str:
    normalized = normalize_expected_text(value).upper()
    if field_name == "play_style":
        if normalized in {"SP", "SINGLE"}:
            return "SINGLE"
        if normalized in {"DP", "DOUBLE"}:
            return "DOUBLE"
        return normalized
    if field_name == "difficulty":
        return normalized
    if field_name == "level":
        digits = normalize_digits(normalized)
        if not digits:
            return ""
        return str(int(digits))
    return normalized


def extract_m3_chart_fields_from_filename(organized_file: str) -> dict[str, str]:
    name = Path(organized_file).stem
    match = M3_CHART_FILENAME_PATTERN.search(name)
    if match is None:
        return {field_name: "" for field_name in M3_CHART_FIELD_FIELDS}

    return {
        "play_style": normalize_m3_chart_field_value("play_style", match.group("style")),
        "difficulty": normalize_m3_chart_field_value("difficulty", match.group("difficulty")),
        "level": normalize_m3_chart_field_value("level", match.group("level")),
    }


def m3_chart_field_extraction_rows(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for frame, event in zip(frames, events, strict=True):
        target = is_save_candidate_event(event)
        exclusion_reason = m3_chart_field_exclusion_reason(event)
        extracted_values = extract_m3_chart_fields_from_filename(frame.row["organized_file"])
        for field_name in M3_CHART_FIELD_FIELDS:
            expected_value = normalize_m3_chart_field_value(
                field_name,
                expected_m3_metadata_value_from_row(frame.row, field_name),
            )
            extracted_value = extracted_values[field_name]
            match_value: bool | None = None
            failure_reason = ""
            if not target:
                status = "skipped"
                failure_reason = exclusion_reason
            elif expected_value == "":
                status = "no_expected_value"
                failure_reason = "no_expected_value"
            elif extracted_value == "":
                status = "empty_extraction"
                failure_reason = "empty_extraction"
            else:
                match_value = extracted_value == expected_value
                status = "match" if match_value else "mismatch"
                failure_reason = "" if match_value else "mismatch"

            rows.append(
                {
                    "organized_file": frame.row["organized_file"],
                    "screen_type": frame.row.get("screen_type", ""),
                    "event_type": event.event_type,
                    "confirmed_result": str(event.confirmed_result),
                    "duplicate": str(event.duplicate),
                    "chart_field_target": str(target),
                    "exclusion_reason": exclusion_reason,
                    "field_name": field_name,
                    "extractor": M3_CHART_FIELD_EXTRACTION_METHOD,
                    "expected_value": expected_value,
                    "extracted_value": extracted_value,
                    "match": "" if match_value is None else str(match_value),
                    "status": status,
                    "failure_reason": failure_reason,
                    "roi_path": m3_chart_field_roi_path(frame.image_path.stem, field_name),
                }
            )
    return rows


def write_m3_chart_field_extraction_csv(
    path: Path,
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> None:
    fieldnames = [
        "organized_file",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "chart_field_target",
        "exclusion_reason",
        "field_name",
        "extractor",
        "expected_value",
        "extracted_value",
        "match",
        "status",
        "failure_reason",
        "roi_path",
    ]
    rows = m3_chart_field_extraction_rows(frames, events)
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def summarize_m3_chart_field_extraction(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> dict[str, object]:
    rows = m3_chart_field_extraction_rows(frames, events)
    field_buckets = {
        field_name: {
            "target_count": 0,
            "match_count": 0,
            "mismatch_count": 0,
            "empty_extraction_count": 0,
            "no_expected_value_count": 0,
            "skipped_count": 0,
        }
        for field_name in M3_CHART_FIELD_FIELDS
    }
    status_counts = {
        "match": 0,
        "mismatch": 0,
        "empty_extraction": 0,
        "no_expected_value": 0,
        "skipped": 0,
    }
    for row in rows:
        status = row["status"]
        status_counts[status] = status_counts.get(status, 0) + 1
        bucket = field_buckets[row["field_name"]]
        if row["chart_field_target"] == "True":
            bucket["target_count"] += 1
        if status == "match":
            bucket["match_count"] += 1
        elif status == "mismatch":
            bucket["mismatch_count"] += 1
        elif status == "empty_extraction":
            bucket["empty_extraction_count"] += 1
        elif status == "no_expected_value":
            bucket["no_expected_value_count"] += 1
        elif status == "skipped":
            bucket["skipped_count"] += 1

    target_count = sum(
        1
        for row in rows
        if row["chart_field_target"] == "True" and row["field_name"] == "level"
    )
    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "extractor": M3_CHART_FIELD_EXTRACTION_METHOD,
        "total_rows": len(rows),
        "chart_field_target_count": target_count,
        "total_attempts": target_count * len(M3_CHART_FIELD_FIELDS),
        "status_counts": status_counts,
        "fields": {
            field_name: {
                **bucket,
                "evaluation_status": m3_expected_coverage_status(
                    int(bucket["target_count"]) - int(bucket["no_expected_value_count"]),
                    int(bucket["target_count"]),
                ),
            }
            for field_name, bucket in field_buckets.items()
        },
    }


def m3_chart_field_feature_values(image: Image.Image, field_name: str) -> dict[str, float]:
    features = extract_features(crop_roi(image, ROI_DEFINITIONS[field_name]))
    return {
        "bright_ratio": features.bright_ratio,
        "white_ratio": features.white_ratio,
        "yellow_ratio": features.yellow_ratio,
        "cyan_ratio": features.cyan_ratio,
        "green_ratio": features.green_ratio,
        "edge_ratio": features.edge_ratio,
        "mean_luma": features.mean_luma,
        "std_luma": features.std_luma,
    }


def m3_chart_field_feature_vector(feature_values: dict[str, float]) -> tuple[float, ...]:
    return (
        feature_values["bright_ratio"],
        feature_values["white_ratio"],
        feature_values["yellow_ratio"],
        feature_values["cyan_ratio"],
        feature_values["green_ratio"],
        feature_values["edge_ratio"],
        feature_values["mean_luma"] / 255.0,
        feature_values["std_luma"] / 128.0,
    )


def centroid_feature_vector(feature_vectors: Iterable[tuple[float, ...]]) -> tuple[float, ...]:
    vectors = list(feature_vectors)
    if not vectors:
        return ()
    return tuple(
        sum(vector[index] for vector in vectors) / len(vectors)
        for index in range(len(vectors[0]))
    )


def m3_chart_field_feature_distance(
    feature_vector: tuple[float, ...],
    centroid: tuple[float, ...],
) -> float:
    return math.sqrt(
        sum((left - right) ** 2 for left, right in zip(feature_vector, centroid, strict=True))
    )


def nearest_m3_chart_field_feature_value(
    feature_vector: tuple[float, ...],
    references: dict[str, list[tuple[float, ...]]],
) -> tuple[str, float | None]:
    best_value = ""
    best_distance: float | None = None
    for expected_value in sorted(references):
        centroid = centroid_feature_vector(references[expected_value])
        if not centroid:
            continue
        distance = m3_chart_field_feature_distance(feature_vector, centroid)
        if best_distance is None or distance < best_distance:
            best_value = expected_value
            best_distance = distance
    return best_value, best_distance


def format_float(value: float | None) -> str:
    return "" if value is None else f"{value:.6f}"


def m3_chart_field_image_feature_extraction_rows(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    feature_samples: dict[tuple[int, str], dict[str, float]] | None = None,
) -> list[dict[str, str]]:
    frame_list = list(frames)
    event_list = list(events)
    if feature_samples is None:
        feature_samples = {}
        for frame_index, (frame, event) in enumerate(zip(frame_list, event_list, strict=True)):
            if not is_save_candidate_event(event):
                continue
            with Image.open(frame.image_path) as image:
                image = image.convert("RGB")
                for field_name in M3_CHART_FIELD_FIELDS:
                    feature_samples[(frame_index, field_name)] = m3_chart_field_feature_values(
                        image,
                        field_name,
                    )

    rows: list[dict[str, str]] = []
    for frame_index, (frame, event) in enumerate(zip(frame_list, event_list, strict=True)):
        target = is_save_candidate_event(event)
        exclusion_reason = m3_chart_field_exclusion_reason(event)
        for field_name in M3_CHART_FIELD_FIELDS:
            expected_value = normalize_m3_chart_field_value(
                field_name,
                expected_m3_metadata_value_from_row(frame.row, field_name),
            )
            feature_values = feature_samples.get((frame_index, field_name), {})
            feature_vector = (
                m3_chart_field_feature_vector(feature_values) if feature_values else ()
            )
            references: dict[str, list[tuple[float, ...]]] = {}
            for reference_index, reference_frame in enumerate(frame_list):
                if reference_index == frame_index:
                    continue
                reference_event = event_list[reference_index]
                if not is_save_candidate_event(reference_event):
                    continue
                reference_expected = normalize_m3_chart_field_value(
                    field_name,
                    expected_m3_metadata_value_from_row(reference_frame.row, field_name),
                )
                reference_features = feature_samples.get((reference_index, field_name))
                if not reference_expected or reference_features is None:
                    continue
                references.setdefault(reference_expected, []).append(
                    m3_chart_field_feature_vector(reference_features)
                )

            extracted_value = ""
            nearest_distance: float | None = None
            match_value: bool | None = None
            failure_reason = ""
            if not target:
                status = "skipped"
                failure_reason = exclusion_reason
            elif expected_value == "":
                status = "no_expected_value"
                failure_reason = "no_expected_value"
            elif not feature_vector:
                status = "empty_extraction"
                failure_reason = "empty_extraction"
            else:
                extracted_value, nearest_distance = nearest_m3_chart_field_feature_value(
                    feature_vector,
                    references,
                )
                if extracted_value == "":
                    status = "empty_extraction"
                    failure_reason = "empty_extraction"
                else:
                    match_value = extracted_value == expected_value
                    status = "match" if match_value else "mismatch"
                    failure_reason = "" if match_value else "mismatch"

            rows.append(
                {
                    "organized_file": frame.row["organized_file"],
                    "screen_type": frame.row.get("screen_type", ""),
                    "event_type": event.event_type,
                    "confirmed_result": str(event.confirmed_result),
                    "duplicate": str(event.duplicate),
                    "chart_field_target": str(target),
                    "exclusion_reason": exclusion_reason,
                    "field_name": field_name,
                    "extractor": M3_CHART_FIELD_IMAGE_FEATURE_EXTRACTION_METHOD,
                    "expected_value": expected_value,
                    "extracted_value": extracted_value,
                    "match": "" if match_value is None else str(match_value),
                    "status": status,
                    "failure_reason": failure_reason,
                    "nearest_distance": format_float(nearest_distance),
                    "feature_bright_ratio": format_float(feature_values.get("bright_ratio")),
                    "feature_white_ratio": format_float(feature_values.get("white_ratio")),
                    "feature_yellow_ratio": format_float(feature_values.get("yellow_ratio")),
                    "feature_cyan_ratio": format_float(feature_values.get("cyan_ratio")),
                    "feature_green_ratio": format_float(feature_values.get("green_ratio")),
                    "feature_edge_ratio": format_float(feature_values.get("edge_ratio")),
                    "feature_mean_luma": format_float(feature_values.get("mean_luma")),
                    "feature_std_luma": format_float(feature_values.get("std_luma")),
                    "roi_path": m3_chart_field_roi_path(frame.image_path.stem, field_name),
                }
            )
    return rows


def write_m3_chart_field_image_feature_extraction_csv(
    path: Path,
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> None:
    fieldnames = [
        "organized_file",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "chart_field_target",
        "exclusion_reason",
        "field_name",
        "extractor",
        "expected_value",
        "extracted_value",
        "match",
        "status",
        "failure_reason",
        "nearest_distance",
        "feature_bright_ratio",
        "feature_white_ratio",
        "feature_yellow_ratio",
        "feature_cyan_ratio",
        "feature_green_ratio",
        "feature_edge_ratio",
        "feature_mean_luma",
        "feature_std_luma",
        "roi_path",
    ]
    rows = m3_chart_field_image_feature_extraction_rows(frames, events)
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def write_m3_chart_field_image_feature_extraction_rows_csv(
    path: Path,
    rows: Iterable[dict[str, str]],
) -> None:
    fieldnames = [
        "organized_file",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "chart_field_target",
        "exclusion_reason",
        "field_name",
        "extractor",
        "expected_value",
        "extracted_value",
        "match",
        "status",
        "failure_reason",
        "nearest_distance",
        "feature_bright_ratio",
        "feature_white_ratio",
        "feature_yellow_ratio",
        "feature_cyan_ratio",
        "feature_green_ratio",
        "feature_edge_ratio",
        "feature_mean_luma",
        "feature_std_luma",
        "roi_path",
    ]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def summarize_m3_chart_field_image_feature_extraction_rows(
    rows: Iterable[dict[str, str]],
) -> dict[str, object]:
    row_list = list(rows)
    field_buckets = {
        field_name: {
            "target_count": 0,
            "match_count": 0,
            "mismatch_count": 0,
            "empty_extraction_count": 0,
            "no_expected_value_count": 0,
            "skipped_count": 0,
        }
        for field_name in M3_CHART_FIELD_FIELDS
    }
    status_counts = {
        "match": 0,
        "mismatch": 0,
        "empty_extraction": 0,
        "no_expected_value": 0,
        "skipped": 0,
    }
    for row in row_list:
        status = row["status"]
        status_counts[status] = status_counts.get(status, 0) + 1
        bucket = field_buckets[row["field_name"]]
        if row["chart_field_target"] == "True":
            bucket["target_count"] += 1
        if status == "match":
            bucket["match_count"] += 1
        elif status == "mismatch":
            bucket["mismatch_count"] += 1
        elif status == "empty_extraction":
            bucket["empty_extraction_count"] += 1
        elif status == "no_expected_value":
            bucket["no_expected_value_count"] += 1
        elif status == "skipped":
            bucket["skipped_count"] += 1

    target_count = sum(
        1
        for row in row_list
        if row["chart_field_target"] == "True" and row["field_name"] == "level"
    )
    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "extractor": M3_CHART_FIELD_IMAGE_FEATURE_EXTRACTION_METHOD,
        "reference_mode": "leave-one-out nearest centroid from confirmed-events expected labels",
        "total_rows": len(row_list),
        "chart_field_target_count": target_count,
        "total_attempts": target_count * len(M3_CHART_FIELD_FIELDS),
        "status_counts": status_counts,
        "fields": {
            field_name: {
                **bucket,
                "evaluation_status": m3_expected_coverage_status(
                    int(bucket["target_count"]) - int(bucket["no_expected_value_count"]),
                    int(bucket["target_count"]),
                ),
            }
            for field_name, bucket in field_buckets.items()
        },
    }


def summarize_m3_chart_field_image_feature_extraction(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> dict[str, object]:
    rows = m3_chart_field_image_feature_extraction_rows(frames, events)
    return summarize_m3_chart_field_image_feature_extraction_rows(rows)


def display_path(path: Path) -> str:
    try:
        return path.relative_to(Path.cwd()).as_posix()
    except ValueError:
        return path.as_posix()


def build_m5_jacket_feature_master_rows(
    frames: Iterable[FrameInput],
    db_path: Path,
    preview_features_by_file: dict[str, master_match.JacketFeature],
) -> tuple[list[dict[str, str]], list[master_match.JacketFeatureMasterEntry]]:
    rows: list[dict[str, str]] = []
    entries: list[master_match.JacketFeatureMasterEntry] = []
    for frame in frames:
        if not is_song_select_grid_frame(frame):
            continue
        organized_file = frame.row.get("organized_file", "")
        source_song_title = expected_m3_metadata_value_from_row(frame.row, "song_title")
        normalized_song_title = master_match.normalize_song_title(source_song_title)
        feature = preview_features_by_file.get(organized_file)
        base_row = {
            "organized_file": organized_file,
            "source_song_title": source_song_title,
            "normalized_song_title": normalized_song_title,
            "song_id": "",
            "title": "",
            "artist": "",
            "feature_status": "skipped",
            "failure_reason": "",
            "dhash_hex": "",
            "histogram": "",
            "thumbnail_rgb": "",
        }
        if feature is None:
            rows.append(
                {
                    **base_row,
                    "feature_status": "missing_feature",
                    "failure_reason": "preview_jacket_feature_unavailable",
                }
            )
            continue

        song, failure_reason = master_match.resolve_song_by_title(db_path, source_song_title)
        if song is None:
            rows.append({**base_row, "failure_reason": failure_reason})
            continue

        rows.append(
            {
                **base_row,
                "song_id": song.song_id,
                "title": song.title,
                "artist": song.artist,
                "feature_status": "accepted",
                "dhash_hex": feature.dhash_hex,
                "histogram": master_match.serialize_float_vector(feature.histogram),
                "thumbnail_rgb": master_match.serialize_float_vector(
                    feature.thumbnail,
                    limit=192,
                ),
            }
        )
        entries.append(
            master_match.JacketFeatureMasterEntry(
                organized_file=organized_file,
                source_song_title=source_song_title,
                song_id=song.song_id,
                title=song.title,
                artist=song.artist,
                feature=feature,
            )
        )
    return rows, entries


def build_m5_title_feature_master_entries(
    frames: Iterable[FrameInput],
    db_path: Path,
    title_features_by_file: dict[str, master_match.TitleImageFeature],
) -> list[master_match.TitleFeatureMasterEntry]:
    entries: list[master_match.TitleFeatureMasterEntry] = []
    for frame in frames:
        if frame.row.get("screen_type") != "result":
            continue
        organized_file = frame.row.get("organized_file", "")
        source_song_title = expected_m3_metadata_value_from_row(frame.row, "song_title")
        feature = title_features_by_file.get(organized_file)
        if source_song_title == "" or feature is None:
            continue
        song, failure_reason = master_match.resolve_song_by_title(db_path, source_song_title)
        if song is None or failure_reason:
            continue
        entries.append(
            master_match.TitleFeatureMasterEntry(
                organized_file=organized_file,
                source_song_title=source_song_title,
                song_id=song.song_id,
                title=song.title,
                artist=song.artist,
                feature=feature,
            )
        )
    return entries


def m3_chart_field_template_vector(image: Image.Image, field_name: str) -> np.ndarray:
    x, y, width, height = ROI_DEFINITIONS[field_name]
    del x, y
    crop = crop_roi(image, ROI_DEFINITIONS[field_name]).convert("RGB")
    if field_name == "difficulty":
        return m3_difficulty_color_pattern_vector(crop)
    normalized = crop.resize((width, height), Image.Resampling.BILINEAR)
    return np.asarray(normalized, dtype=np.float32).reshape(-1) / 255.0


def m3_difficulty_color_pattern_vector(region: Image.Image) -> np.ndarray:
    rgb = np.asarray(region.convert("RGB"), dtype=np.float32)
    red = rgb[:, :, 0]
    green = rgb[:, :, 1]
    blue = rgb[:, :, 2]
    max_channel = rgb.max(axis=2)
    min_channel = rgb.min(axis=2)
    luma = rgb.mean(axis=2)
    foreground = (luma > 50) & ((max_channel - min_channel) > 35) & (max_channel > 100)
    foreground_count = int(foreground.sum())
    if foreground_count == 0:
        return np.zeros(6, dtype=np.float32)

    def ratio(mask: np.ndarray) -> float:
        return float((mask & foreground).sum() / foreground_count)

    return np.asarray(
        [
            ratio((blue > 120) & (red < 120)),  # BEGINNER cyan/blue
            ratio((red > 140) & (green > 110) & (blue < 120)),  # BASIC yellow/orange
            ratio((red > 150) & (green < 100) & (blue < 120)),  # DIFFICULT red
            ratio((green > 120) & (red < 120) & (blue < 120)),  # EXPERT green
            ratio((red > 130) & (blue > 130) & (green < 90)),  # CHALLENGE magenta
            foreground_count / float(foreground.size),
        ],
        dtype=np.float32,
    )


def load_m3_chart_field_template_references(
    template_root: Path,
) -> dict[str, list[M3ChartFieldTemplateReference]]:
    references = {field_name: [] for field_name in M3_CHART_FIELD_FIELDS}
    if not template_root.exists() or not template_root.is_dir():
        return references

    template_paths = sorted(
        (
            path
            for path in template_root.iterdir()
            if path.is_file() and path.suffix.lower() in FRAME_IMAGE_EXTENSIONS
        ),
        key=lambda path: path.name.lower(),
    )
    for template_path in template_paths:
        expected_values = extract_m3_chart_fields_from_filename(template_path.name)
        if not any(expected_values.values()):
            continue
        with Image.open(template_path) as image:
            image = image.convert("RGB")
            for field_name in M3_CHART_FIELD_FIELDS:
                expected_value = expected_values[field_name]
                if not expected_value:
                    continue
                references[field_name].append(
                    M3ChartFieldTemplateReference(
                        field_name=field_name,
                        expected_value=expected_value,
                        image_path=template_path,
                        vector=m3_chart_field_template_vector(image, field_name),
                        source_type="chart_field_templates",
                    )
                )
    return references


def add_m3_chart_field_result_references(
    references: dict[str, list[M3ChartFieldTemplateReference]],
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    target_vectors: dict[tuple[int, str], np.ndarray],
) -> None:
    for frame_index, (frame, event) in enumerate(zip(frames, events, strict=True)):
        if not is_save_candidate_event(event):
            continue
        for field_name in M3_CHART_FIELD_FIELDS:
            expected_value = normalize_m3_chart_field_value(
                field_name,
                expected_m3_metadata_value_from_row(frame.row, field_name),
            )
            vector = target_vectors.get((frame_index, field_name))
            if not expected_value or vector is None:
                continue
            references[field_name].append(
                M3ChartFieldTemplateReference(
                    field_name=field_name,
                    expected_value=expected_value,
                    image_path=frame.image_path,
                    vector=vector,
                    source_type="confirmed_events",
                    frame_index=frame_index,
                )
            )


def m3_chart_field_template_distance(
    left: np.ndarray,
    right: np.ndarray,
) -> float:
    if left.shape != right.shape:
        return math.inf
    return float(np.sqrt(np.mean((left - right) ** 2)))


def nearest_m3_chart_field_template_value(
    target_vector: np.ndarray,
    references: Iterable[M3ChartFieldTemplateReference],
) -> tuple[str, float | None, M3ChartFieldTemplateReference | None]:
    best_value = ""
    best_distance: float | None = None
    best_reference: M3ChartFieldTemplateReference | None = None
    for reference in references:
        distance = m3_chart_field_template_distance(target_vector, reference.vector)
        if best_distance is None or distance < best_distance:
            best_value = reference.expected_value
            best_distance = distance
            best_reference = reference
    return best_value, best_distance, best_reference


def m3_chart_field_template_extraction_rows(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    template_root: Path = M3_CHART_FIELD_TEMPLATE_ROOT,
    *,
    include_result_references: bool = True,
    target_vectors: dict[tuple[int, str], np.ndarray] | None = None,
) -> list[dict[str, str]]:
    frame_list = list(frames)
    event_list = list(events)
    if target_vectors is None:
        target_vectors = {}
        for frame_index, (frame, event) in enumerate(zip(frame_list, event_list, strict=True)):
            if not is_save_candidate_event(event):
                continue
            with Image.open(frame.image_path) as image:
                image = image.convert("RGB")
                for field_name in M3_CHART_FIELD_FIELDS:
                    target_vectors[(frame_index, field_name)] = m3_chart_field_template_vector(
                        image,
                        field_name,
                    )

    references_by_field = load_m3_chart_field_template_references(template_root)
    if include_result_references:
        add_m3_chart_field_result_references(
            references_by_field,
            frame_list,
            event_list,
            target_vectors,
        )

    rows: list[dict[str, str]] = []
    for frame_index, (frame, event) in enumerate(zip(frame_list, event_list, strict=True)):
        target = is_save_candidate_event(event)
        exclusion_reason = m3_chart_field_exclusion_reason(event)
        for field_name in M3_CHART_FIELD_FIELDS:
            expected_value = normalize_m3_chart_field_value(
                field_name,
                expected_m3_metadata_value_from_row(frame.row, field_name),
            )
            target_vector = target_vectors.get((frame_index, field_name))
            references = [
                reference
                for reference in references_by_field[field_name]
                if reference.frame_index != frame_index
            ]
            expected_reference_count = sum(
                1 for reference in references if reference.expected_value == expected_value
            )
            extracted_value = ""
            nearest_distance: float | None = None
            nearest_reference: M3ChartFieldTemplateReference | None = None
            match_value: bool | None = None
            failure_reason = ""
            if not target:
                status = "skipped"
                failure_reason = exclusion_reason
            elif expected_value == "":
                status = "no_expected_value"
                failure_reason = "no_expected_value"
            elif target_vector is None:
                status = "empty_extraction"
                failure_reason = "empty_extraction"
            elif not references:
                status = "empty_extraction"
                failure_reason = "no_template_references"
            else:
                extracted_value, nearest_distance, nearest_reference = (
                    nearest_m3_chart_field_template_value(target_vector, references)
                )
                if extracted_value == "":
                    status = "empty_extraction"
                    failure_reason = "empty_extraction"
                else:
                    match_value = extracted_value == expected_value
                    status = "match" if match_value else "mismatch"
                    failure_reason = (
                        ""
                        if match_value
                        else (
                            "missing_expected_template_reference"
                            if expected_reference_count == 0
                            else "mismatch"
                        )
                    )

            rows.append(
                {
                    "organized_file": frame.row["organized_file"],
                    "screen_type": frame.row.get("screen_type", ""),
                    "event_type": event.event_type,
                    "confirmed_result": str(event.confirmed_result),
                    "duplicate": str(event.duplicate),
                    "chart_field_target": str(target),
                    "exclusion_reason": exclusion_reason,
                    "field_name": field_name,
                    "extractor": M3_CHART_FIELD_TEMPLATE_EXTRACTION_METHOD,
                    "expected_value": expected_value,
                    "extracted_value": extracted_value,
                    "match": "" if match_value is None else str(match_value),
                    "status": status,
                    "failure_reason": failure_reason,
                    "nearest_distance": format_float(nearest_distance),
                    "nearest_template_path": (
                        ""
                        if nearest_reference is None
                        else display_path(nearest_reference.image_path)
                    ),
                    "nearest_source_type": (
                        "" if nearest_reference is None else nearest_reference.source_type
                    ),
                    "template_reference_count": str(len(references)),
                    "expected_template_reference_count": str(expected_reference_count),
                    "roi_path": m3_chart_field_roi_path(frame.image_path.stem, field_name),
                }
            )
    return rows


def m3_chart_field_template_holdout_extraction_rows(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    template_root: Path = M3_CHART_FIELD_TEMPLATE_ROOT,
    *,
    target_vectors: dict[tuple[int, str], np.ndarray] | None = None,
) -> list[dict[str, str]]:
    rows = m3_chart_field_template_extraction_rows(
        frames,
        events,
        template_root,
        include_result_references=False,
        target_vectors=target_vectors,
    )
    for row in rows:
        row["extractor"] = M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD
    return rows


def write_m3_chart_field_template_extraction_csv(
    path: Path,
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    template_root: Path = M3_CHART_FIELD_TEMPLATE_ROOT,
) -> None:
    fieldnames = [
        "organized_file",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "chart_field_target",
        "exclusion_reason",
        "field_name",
        "extractor",
        "expected_value",
        "extracted_value",
        "match",
        "status",
        "failure_reason",
        "nearest_distance",
        "nearest_template_path",
        "nearest_source_type",
        "template_reference_count",
        "expected_template_reference_count",
        "roi_path",
    ]
    rows = m3_chart_field_template_extraction_rows(frames, events, template_root)
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def write_m3_chart_field_template_extraction_rows_csv(
    path: Path,
    rows: Iterable[dict[str, str]],
) -> None:
    fieldnames = [
        "organized_file",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "chart_field_target",
        "exclusion_reason",
        "field_name",
        "extractor",
        "expected_value",
        "extracted_value",
        "match",
        "status",
        "failure_reason",
        "nearest_distance",
        "nearest_template_path",
        "nearest_source_type",
        "template_reference_count",
        "expected_template_reference_count",
        "roi_path",
    ]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def m3_chart_field_template_reference_label_counts(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    template_root: Path = M3_CHART_FIELD_TEMPLATE_ROOT,
    *,
    include_result_references: bool = True,
) -> dict[str, object]:
    source_counts = {
        "chart_field_templates": 0,
        "confirmed_events": 0,
    }
    value_counts = {
        field_name: {"chart_field_templates": {}, "confirmed_events": {}, "combined": {}}
        for field_name in M3_CHART_FIELD_FIELDS
    }

    template_paths = []
    if template_root.exists() and template_root.is_dir():
        template_paths = [
            path
            for path in template_root.iterdir()
            if path.is_file() and path.suffix.lower() in FRAME_IMAGE_EXTENSIONS
        ]
    for template_path in template_paths:
        expected_values = extract_m3_chart_fields_from_filename(template_path.name)
        if not any(expected_values.values()):
            continue
        source_counts["chart_field_templates"] += 1
        for field_name in M3_CHART_FIELD_FIELDS:
            expected_value = expected_values[field_name]
            if not expected_value:
                continue
            bucket = value_counts[field_name]["chart_field_templates"]
            bucket[expected_value] = bucket.get(expected_value, 0) + 1

    if include_result_references:
        for frame, event in zip(frames, events, strict=True):
            if not is_save_candidate_event(event):
                continue
            row_has_reference = False
            for field_name in M3_CHART_FIELD_FIELDS:
                expected_value = normalize_m3_chart_field_value(
                    field_name,
                    expected_m3_metadata_value_from_row(frame.row, field_name),
                )
                if not expected_value:
                    continue
                row_has_reference = True
                bucket = value_counts[field_name]["confirmed_events"]
                bucket[expected_value] = bucket.get(expected_value, 0) + 1
            if row_has_reference:
                source_counts["confirmed_events"] += 1

    for field_name in M3_CHART_FIELD_FIELDS:
        combined: dict[str, int] = {}
        for source_name in ("chart_field_templates", "confirmed_events"):
            for expected_value, count in value_counts[field_name][source_name].items():
                combined[expected_value] = combined.get(expected_value, 0) + count
        value_counts[field_name]["combined"] = dict(sorted(combined.items()))
        value_counts[field_name]["chart_field_templates"] = dict(
            sorted(value_counts[field_name]["chart_field_templates"].items())
        )
        value_counts[field_name]["confirmed_events"] = dict(
            sorted(value_counts[field_name]["confirmed_events"].items())
        )

    return {
        "source_image_counts": source_counts,
        "value_counts": value_counts,
    }


def summarize_m3_chart_field_template_extraction_rows(
    rows: Iterable[dict[str, str]],
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    template_root: Path = M3_CHART_FIELD_TEMPLATE_ROOT,
    *,
    include_result_references: bool = True,
    extractor_method: str = M3_CHART_FIELD_TEMPLATE_EXTRACTION_METHOD,
    reference_mode: str | None = None,
) -> dict[str, object]:
    row_list = list(rows)
    label_counts = m3_chart_field_template_reference_label_counts(
        frames,
        events,
        template_root,
        include_result_references=include_result_references,
    )
    field_buckets = {
        field_name: {
            "target_count": 0,
            "match_count": 0,
            "mismatch_count": 0,
            "empty_extraction_count": 0,
            "no_expected_value_count": 0,
            "skipped_count": 0,
        }
        for field_name in M3_CHART_FIELD_FIELDS
    }
    status_counts = {
        "match": 0,
        "mismatch": 0,
        "empty_extraction": 0,
        "no_expected_value": 0,
        "skipped": 0,
    }
    for row in row_list:
        status = row["status"]
        status_counts[status] = status_counts.get(status, 0) + 1
        bucket = field_buckets[row["field_name"]]
        if row["chart_field_target"] == "True":
            bucket["target_count"] += 1
        if status == "match":
            bucket["match_count"] += 1
        elif status == "mismatch":
            bucket["mismatch_count"] += 1
        elif status == "empty_extraction":
            bucket["empty_extraction_count"] += 1
        elif status == "no_expected_value":
            bucket["no_expected_value_count"] += 1
        elif status == "skipped":
            bucket["skipped_count"] += 1

    target_count = sum(
        1
        for row in row_list
        if row["chart_field_target"] == "True" and row["field_name"] == "level"
    )
    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "extractor": extractor_method,
        "reference_mode": reference_mode
        or (
            "nearest ROI image template from chart_field_templates plus confirmed-events "
            "result references with leave-one-out self exclusion"
        ),
        "template_root": display_path(template_root),
        "field_vector_modes": {
            "play_style": "raw-roi-rgb",
            "difficulty": "foreground-color-pattern",
            "level": "raw-roi-rgb",
        },
        "reference_source_image_counts": label_counts["source_image_counts"],
        "template_image_count": label_counts["source_image_counts"]["chart_field_templates"],
        "result_reference_image_count": label_counts["source_image_counts"]["confirmed_events"],
        "template_reference_counts": {
            field_name: max(
                (
                    int(row["template_reference_count"])
                    for row in row_list
                    if row["field_name"] == field_name and row["chart_field_target"] == "True"
                ),
                default=0,
            )
            for field_name in M3_CHART_FIELD_FIELDS
        },
        "template_value_counts": {
            field_name: label_counts["value_counts"][field_name]["combined"]
            for field_name in M3_CHART_FIELD_FIELDS
        },
        "reference_value_counts_by_source": label_counts["value_counts"],
        "total_rows": len(row_list),
        "chart_field_target_count": target_count,
        "total_attempts": target_count * len(M3_CHART_FIELD_FIELDS),
        "status_counts": status_counts,
        "fields": {
            field_name: {
                **bucket,
                "evaluation_status": m3_expected_coverage_status(
                    int(bucket["target_count"]) - int(bucket["no_expected_value_count"]),
                    int(bucket["target_count"]),
                ),
            }
            for field_name, bucket in field_buckets.items()
        },
    }


def summarize_m3_chart_field_template_extraction(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    template_root: Path = M3_CHART_FIELD_TEMPLATE_ROOT,
    *,
    include_result_references: bool = True,
    extractor_method: str = M3_CHART_FIELD_TEMPLATE_EXTRACTION_METHOD,
    reference_mode: str | None = None,
) -> dict[str, object]:
    frame_list = list(frames)
    event_list = list(events)
    rows = m3_chart_field_template_extraction_rows(
        frame_list,
        event_list,
        template_root,
        include_result_references=include_result_references,
    )
    return summarize_m3_chart_field_template_extraction_rows(
        rows,
        frame_list,
        event_list,
        template_root,
        include_result_references=include_result_references,
        extractor_method=extractor_method,
        reference_mode=reference_mode,
    )


def summarize_m3_chart_field_template_holdout_extraction(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    template_root: Path = M3_CHART_FIELD_TEMPLATE_ROOT,
) -> dict[str, object]:
    frame_list = list(frames)
    event_list = list(events)
    rows = m3_chart_field_template_holdout_extraction_rows(
        frame_list,
        event_list,
        template_root,
    )
    return summarize_m3_chart_field_template_extraction_rows(
        rows,
        frame_list,
        event_list,
        template_root,
        include_result_references=False,
        extractor_method=M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        reference_mode=(
            "nearest ROI image template from chart_field_templates only; "
            "confirmed-events result ROI are evaluation targets only"
        ),
    )


def m3_chart_field_save_failure_reason(failure_reason: str) -> str:
    if failure_reason in {"no_template_references", "missing_expected_template_reference"}:
        return "missing_reference"
    if failure_reason == "no_expected_value":
        return "no_expected_value"
    if failure_reason == "empty_extraction":
        return "empty_extraction"
    if failure_reason == "mismatch":
        return "low_confidence"
    return failure_reason


def m3_chart_field_value_sort_key(field_name: str, value: str) -> tuple[int, int | str]:
    if field_name == "level" and value.isdecimal():
        return (0, int(value))
    return (1, value)


def m3_chart_field_adoption_readiness(
    bucket: dict[str, int],
    missing_reference_values: list[str],
) -> str:
    target_count = bucket["target_count"]
    if target_count == 0:
        return "no_targets"
    if bucket["no_expected_value_count"] > 0:
        return "needs_expected_values"
    if bucket["empty_extraction_count"] > 0:
        return "needs_references_or_extraction"
    if bucket["mismatch_count"] == 0 and bucket["match_count"] == target_count:
        return "adoption_candidate"
    if missing_reference_values:
        return "needs_template_references"
    return "low_confidence"


def summarize_m3_chart_field_adoption_candidates(
    holdout_rows: Iterable[dict[str, str]],
) -> dict[str, object]:
    row_list = list(holdout_rows)
    field_buckets = {
        field_name: {
            "target_count": 0,
            "match_count": 0,
            "mismatch_count": 0,
            "empty_extraction_count": 0,
            "no_expected_value_count": 0,
            "skipped_count": 0,
        }
        for field_name in M3_CHART_FIELD_FIELDS
    }
    failure_reason_counts = {field_name: {} for field_name in M3_CHART_FIELD_FIELDS}
    save_failure_reason_counts = {field_name: {} for field_name in M3_CHART_FIELD_FIELDS}
    missing_reference_values = {field_name: set() for field_name in M3_CHART_FIELD_FIELDS}

    for row in row_list:
        field_name = row["field_name"]
        status = row["status"]
        bucket = field_buckets[field_name]
        if row["chart_field_target"] == "True":
            bucket["target_count"] += 1
        if status == "match":
            bucket["match_count"] += 1
        elif status == "mismatch":
            bucket["mismatch_count"] += 1
        elif status == "empty_extraction":
            bucket["empty_extraction_count"] += 1
        elif status == "no_expected_value":
            bucket["no_expected_value_count"] += 1
        elif status == "skipped":
            bucket["skipped_count"] += 1

        if row["chart_field_target"] != "True" or status == "match":
            continue

        failure_reason = row.get("failure_reason", "")
        if failure_reason:
            counts = failure_reason_counts[field_name]
            counts[failure_reason] = counts.get(failure_reason, 0) + 1
            save_failure_reason = m3_chart_field_save_failure_reason(failure_reason)
            save_counts = save_failure_reason_counts[field_name]
            save_counts[save_failure_reason] = save_counts.get(save_failure_reason, 0) + 1
        if failure_reason in {"no_template_references", "missing_expected_template_reference"}:
            expected_value = row.get("expected_value", "")
            if expected_value:
                missing_reference_values[field_name].add(expected_value)

    fields: dict[str, dict[str, object]] = {}
    for field_name, bucket in field_buckets.items():
        missing_values = sorted(
            missing_reference_values[field_name],
            key=lambda value: m3_chart_field_value_sort_key(field_name, value),
        )
        readiness = m3_chart_field_adoption_readiness(bucket, missing_values)
        fields[field_name] = {
            **bucket,
            "adoption_readiness": readiness,
            "recommended_extractor": (
                M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD
                if readiness == "adoption_candidate"
                else ""
            ),
            "candidate_extractor_under_review": M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
            "failure_reason_counts": dict(sorted(failure_reason_counts[field_name].items())),
            "save_failure_reason_counts": dict(
                sorted(save_failure_reason_counts[field_name].items())
            ),
            "missing_reference_values": missing_values,
        }

    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "scope": "M3-3 chart-field adoption candidate review",
        "candidate_evidence_extractor": M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        "extractor_roles": {
            "filename-baseline": "filename drift diagnostic; not ROI extraction success",
            "roi-feature-nearest-centroid": "ROI feature diagnostic; not adoption evidence",
            "roi-template-nearest": (
                "same-distribution leave-one-out diagnostic; not adoption evidence"
            ),
            "roi-template-holdout": (
                "template-only split diagnostic used for M3-3 adoption candidate review"
            ),
        },
        "failure_reason_vocabulary": {
            "missing_reference": (
                "`no_template_references` or `missing_expected_template_reference` before save"
            ),
            "no_expected_value": "expected value is absent for the evaluation target",
            "empty_extraction": "extractor returned no value",
            "low_confidence": "reference exists but nearest value mismatched",
            "skipped": "duplicate, rejected_transition, unconfirmed, or non_result",
        },
        "fields": fields,
    }


def write_m3_chart_field_adoption_candidates_report(
    path: Path,
    summary: dict[str, object],
) -> None:
    fields = summary["fields"]
    assert isinstance(fields, dict)
    lines = [
        "# M3 Chart Field Adoption Candidates",
        "",
        "`play_style` / `difficulty` / `level` のM3-3採用候補を読むためのレポートです。",
        "`roi-template-holdout` を外部検証に近い分割診断として使いますが、",
        "このレポートも採用済みテンプレート照合、OCR、マスタ照合の成功扱いにはしません。",
        "",
        f"- target boundary: `{summary['target_boundary']}`",
        f"- candidate evidence extractor: `{summary['candidate_evidence_extractor']}`",
        "- save failure vocabulary: `missing_reference` / `no_expected_value` / "
        "`empty_extraction` / `low_confidence`",
        "",
        "## Candidate Matrix",
        "",
        "| field | readiness | recommended extractor | target | match | mismatch | "
        "empty | missing references | save failure reasons |",
        "|---|---|---|---:|---:|---:|---:|---|---|",
    ]
    for field_name in M3_CHART_FIELD_FIELDS:
        bucket = fields[field_name]
        assert isinstance(bucket, dict)
        missing_values = bucket["missing_reference_values"]
        assert isinstance(missing_values, list)
        save_reasons = bucket["save_failure_reason_counts"]
        assert isinstance(save_reasons, dict)
        lines.append(
            f"| `{field_name}` | `{bucket['adoption_readiness']}` | "
            f"`{bucket['recommended_extractor']}` | {bucket['target_count']} | "
            f"{bucket['match_count']} | {bucket['mismatch_count']} | "
            f"{bucket['empty_extraction_count']} | "
            f"`{', '.join(str(value) for value in missing_values)}` | "
            f"`{json.dumps(save_reasons, ensure_ascii=False, sort_keys=True)}` |"
        )

    lines.extend(
        [
            "",
            "## Extractor Roles",
            "",
        ]
    )
    extractor_roles = summary["extractor_roles"]
    assert isinstance(extractor_roles, dict)
    for extractor, role in extractor_roles.items():
        lines.append(f"- `{extractor}`: {role}")

    lines.extend(
        [
            "",
            "## Reading Notes",
            "",
            "- `adoption_candidate` はM3-3内で採用候補として読める状態であり、"
            "本番保存やマスタ照合へ直結しない。",
            "- `needs_template_references` は追加テンプレート素材が必要な状態として読み、"
            "画像そのものはGit管理しない。",
            "- `filename-baseline` の mismatch はファイル名ラベルのドリフト検出として読む。",
            "- `roi-template-nearest` は同分布 leave-one-out 診断で、"
            "採用済みテンプレート照合の成功扱いにしない。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def m3_save_candidate_song_artist_status(
    result: M3SongArtistOcrResult | None,
) -> tuple[str, str, str, str, str]:
    if result is None:
        return "ocr_unavailable", "ocr_not_run", "", "", ""
    if result.failure_reason == "engine_unavailable":
        return "ocr_unavailable", "engine_unavailable", result.expected_value, "", result.extractor
    if result.failure_reason == "ocr_failed":
        return "ocr_failed", "ocr_failed", result.expected_value, "", result.extractor
    if result.failure_reason == "empty_ocr":
        return "empty_ocr", "empty_ocr", result.expected_value, "", result.extractor
    if result.failure_reason == "no_expected_value":
        return (
            "no_expected_value",
            "no_expected_value",
            result.expected_value,
            result.pre_normalized_text,
            result.extractor,
        )
    return "ready", "", result.expected_value, result.pre_normalized_text, result.extractor


def m3_save_candidate_chart_status(
    row: dict[str, str] | None,
    adoption_bucket: dict[str, object] | None,
) -> tuple[str, str, str, str, str]:
    if row is None:
        return "not_adopted", "missing_chart_field_row", "", "", ""

    failure_reason = row.get("failure_reason", "")
    save_failure_reason = m3_chart_field_save_failure_reason(failure_reason)
    expected_value = row.get("expected_value", "")
    extracted_value = row.get("extracted_value", "")
    extractor = row.get("extractor", "")

    if save_failure_reason == "missing_reference":
        return "missing_reference", save_failure_reason, expected_value, extracted_value, extractor
    if save_failure_reason == "no_expected_value" or row["status"] == "no_expected_value":
        return "no_expected_value", "no_expected_value", expected_value, extracted_value, extractor
    if row["status"] == "empty_extraction":
        return "empty_ocr", "empty_extraction", expected_value, extracted_value, extractor

    adoption_readiness = ""
    if adoption_bucket is not None:
        value = adoption_bucket.get("adoption_readiness", "")
        adoption_readiness = value if isinstance(value, str) else ""

    if adoption_readiness == "adoption_candidate" and row["status"] == "match":
        return "ready", "", expected_value, extracted_value, extractor
    if adoption_readiness == "needs_template_references":
        return (
            "missing_reference",
            "field_needs_template_references",
            expected_value,
            extracted_value,
            extractor,
        )
    if adoption_readiness == "needs_expected_values":
        return (
            "no_expected_value",
            "field_needs_expected_values",
            expected_value,
            extracted_value,
            extractor,
        )

    reason = save_failure_reason or adoption_readiness or row["status"]
    return "not_adopted", reason, expected_value, extracted_value, extractor


def m3_save_candidate_rows(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
    holdout_rows: Iterable[dict[str, str]],
    adoption_summary: dict[str, object],
    song_artist_results: Iterable[M3SongArtistOcrResult],
) -> list[dict[str, str]]:
    frame_list = list(frames)
    event_list = list(events)
    song_artist_by_key = {
        (result.organized_file, result.field_name): result for result in song_artist_results
    }
    holdout_by_key = {
        (row["organized_file"], row["field_name"]): row
        for row in holdout_rows
        if row.get("chart_field_target") == "True"
    }
    adoption_fields = adoption_summary.get("fields", {})
    if not isinstance(adoption_fields, dict):
        adoption_fields = {}

    rows: list[dict[str, str]] = []
    for frame, event in zip(frame_list, event_list, strict=True):
        if not is_save_candidate_event(event):
            continue
        row: dict[str, str] = {
            "frame_index": str(event.frame_index),
            "organized_file": frame.row["organized_file"],
            "screen_type": frame.row.get("screen_type", ""),
            "event_type": event.event_type,
            "confirmed_result": str(event.confirmed_result),
            "duplicate": str(event.duplicate),
            "timestamp_ms": "" if event.timestamp_ms is None else str(event.timestamp_ms),
            "confirmation_mode": event.confirmation_mode,
        }
        blocking_fields: list[str] = []
        for field_name in M3_SAVE_CANDIDATE_FIELDS:
            if field_name in M3_SONG_ARTIST_OCR_FIELDS:
                status, failure, expected, extracted, extractor = (
                    m3_save_candidate_song_artist_status(
                        song_artist_by_key.get((frame.row["organized_file"], field_name))
                    )
                )
            else:
                adoption_bucket = adoption_fields.get(field_name)
                status, failure, expected, extracted, extractor = m3_save_candidate_chart_status(
                    holdout_by_key.get((frame.row["organized_file"], field_name)),
                    adoption_bucket if isinstance(adoption_bucket, dict) else None,
                )
            row[f"{field_name}_status"] = status
            row[f"{field_name}_failure_reason"] = failure
            row[f"{field_name}_expected_value"] = expected
            row[f"{field_name}_extracted_value"] = extracted
            row[f"{field_name}_extractor"] = extractor
            if status != "ready":
                blocking_fields.append(field_name)
        row["overall_status"] = "ready" if not blocking_fields else "not_ready"
        row["blocking_fields"] = " ".join(blocking_fields)
        rows.append(row)
    return rows


def is_m5_jacket_diagnostic_event(frame: FrameInput, event: ResultEvent) -> bool:
    return (
        frame.row.get("screen_type") == "result"
        or event.result_candidate
        or event.confirmed_result
    )


def m5_jacket_diagnostic_boundary_reason(event: ResultEvent) -> str:
    if is_save_candidate_event(event):
        return "save_candidate"
    if event.duplicate:
        return "duplicate"
    if event.event_type == "rejected_transition":
        return "rejected_transition"
    if event.result_candidate:
        return "unconfirmed"
    return "metadata_result_not_candidate"


def m5_jacket_diagnostic_metadata_field_status(
    row: dict[str, str],
    field_name: str,
) -> tuple[str, str]:
    value = expected_m3_metadata_value_from_row(row, field_name)
    if value == "":
        return "no_expected_value", ""
    return "ready", value


def m5_jacket_diagnostic_candidate_rows(
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for frame, event in zip(frames, events, strict=True):
        if not is_m5_jacket_diagnostic_event(frame, event):
            continue
        row: dict[str, str] = {
            "diagnostic_scope": "m5_jacket_result_boundary_diagnostic",
            "m5_target_boundary_reason": m5_jacket_diagnostic_boundary_reason(event),
            "frame_index": str(event.frame_index),
            "organized_file": frame.row["organized_file"],
            "screen_type": frame.row.get("screen_type", ""),
            "event_type": event.event_type,
            "confirmed_result": str(event.confirmed_result),
            "duplicate": str(event.duplicate),
            "duplicate_key": event.duplicate_key,
            "timestamp_ms": "" if event.timestamp_ms is None else str(event.timestamp_ms),
            "confirmation_mode": event.confirmation_mode,
        }
        for field_name in M3_SAVE_CANDIDATE_FIELDS:
            status, value = m5_jacket_diagnostic_metadata_field_status(
                frame.row,
                field_name,
            )
            row[f"{field_name}_status"] = status
            row[f"{field_name}_failure_reason"] = (
                "" if status == "ready" else "no_expected_value"
            )
            row[f"{field_name}_expected_value"] = value
            row[f"{field_name}_extracted_value"] = value
            row[f"{field_name}_extractor"] = "metadata-expected-diagnostic"
        row["overall_status"] = (
            "ready"
            if all(row[f"{field_name}_status"] == "ready" for field_name in M3_CHART_FIELD_FIELDS)
            else "not_ready"
        )
        row["blocking_fields"] = " ".join(
            field_name
            for field_name in M3_CHART_FIELD_FIELDS
            if row[f"{field_name}_status"] != "ready"
        )
        rows.append(row)
    return rows


def attach_m5_jacket_diagnostic_context(
    diagnostic_input_rows: Iterable[dict[str, str]],
    jacket_match_rows: Iterable[dict[str, str]],
) -> list[dict[str, str]]:
    context_fields = [
        "diagnostic_scope",
        "m5_target_boundary_reason",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "duplicate_key",
        "timestamp_ms",
        "confirmation_mode",
    ]
    return [
        {**{field: input_row.get(field, "") for field in context_fields}, **match_row}
        for input_row, match_row in zip(
            diagnostic_input_rows,
            jacket_match_rows,
            strict=True,
        )
    ]


def write_m3_save_candidate_summary_csv(path: Path, rows: Iterable[dict[str, str]]) -> None:
    fieldnames = [
        "frame_index",
        "organized_file",
        "screen_type",
        "event_type",
        "confirmed_result",
        "duplicate",
        "timestamp_ms",
        "confirmation_mode",
    ]
    for field_name in M3_SAVE_CANDIDATE_FIELDS:
        fieldnames.extend(
            [
                f"{field_name}_status",
                f"{field_name}_failure_reason",
                f"{field_name}_expected_value",
                f"{field_name}_extracted_value",
                f"{field_name}_extractor",
            ]
        )
    fieldnames.extend(["overall_status", "blocking_fields"])

    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def summarize_m3_save_candidates(rows: Iterable[dict[str, str]]) -> dict[str, object]:
    row_list = list(rows)
    fields: dict[str, dict[str, object]] = {}
    for field_name in M3_SAVE_CANDIDATE_FIELDS:
        status_counts: dict[str, int] = {}
        failure_reason_counts: dict[str, int] = {}
        for row in row_list:
            status = row[f"{field_name}_status"]
            status_counts[status] = status_counts.get(status, 0) + 1
            failure_reason = row[f"{field_name}_failure_reason"]
            if failure_reason:
                failure_reason_counts[failure_reason] = (
                    failure_reason_counts.get(failure_reason, 0) + 1
                )
        fields[field_name] = {
            "status_counts": dict(sorted(status_counts.items())),
            "failure_reason_counts": dict(sorted(failure_reason_counts.items())),
        }

    overall_status_counts: dict[str, int] = {}
    for row in row_list:
        status = row["overall_status"]
        overall_status_counts[status] = overall_status_counts.get(status, 0) + 1

    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "scope": "M3-5 save candidate aggregate report",
        "target_count": len(row_list),
        "fields": fields,
        "overall_status_counts": dict(sorted(overall_status_counts.items())),
        "status_vocabulary": [
            "ready",
            "missing_reference",
            "ocr_unavailable",
            "ocr_failed",
            "empty_ocr",
            "no_expected_value",
            "not_adopted",
        ],
        "reading_notes": [
            "One row represents one confirmed-events save candidate.",
            (
                "play_style, difficulty, and level ready mean M3-3 adoption candidates, "
                "not production template adoption."
            ),
            "song_title and artist statuses are OCR entry observations, not master matching.",
            "difficulty and level can remain missing_reference until local templates are added.",
        ],
    }


def write_m3_save_candidate_summary_report(
    path: Path,
    rows: Iterable[dict[str, str]],
    summary: dict[str, object],
) -> None:
    row_list = list(rows)
    fields = summary["fields"]
    assert isinstance(fields, dict)
    lines = [
        "# M3 Save Candidate Summary",
        "",
        "confirmed-events ごとに、曲名、artist、プレースタイル、難易度、"
        "レベルの抽出状態を1行に集約するM3-5レポートです。",
        "DB保存、マスタ照合、ファジーマッチ、曲名正規化には進みません。",
        "",
        f"- target boundary: `{summary['target_boundary']}`",
        f"- target confirmed-events: {summary['target_count']}",
        "- status vocabulary: `ready` / `missing_reference` / `ocr_unavailable` / "
        "`ocr_failed` / `empty_ocr` / `no_expected_value` / `not_adopted`",
        "",
        "## Field Status",
        "",
        "| field | status counts | failure reasons |",
        "|---|---|---|",
    ]
    for field_name in M3_SAVE_CANDIDATE_FIELDS:
        bucket = fields[field_name]
        assert isinstance(bucket, dict)
        lines.append(
            f"| `{field_name}` | "
            f"`{json.dumps(bucket['status_counts'], ensure_ascii=False, sort_keys=True)}` | "
            f"`{json.dumps(bucket['failure_reason_counts'], ensure_ascii=False, sort_keys=True)}` |"
        )

    lines.extend(
        [
            "",
            "## Candidate Rows",
            "",
            "| organized_file | overall | blocking fields | song_title | artist | "
            "play_style | difficulty | level |",
            "|---|---|---|---|---|---|---|---|",
        ]
    )
    for row in row_list[:20]:
        lines.append(
            f"| `{row['organized_file']}` | `{row['overall_status']}` | "
            f"`{row['blocking_fields']}` | `{row['song_title_status']}` | "
            f"`{row['artist_status']}` | `{row['play_style_status']}` | "
            f"`{row['difficulty_status']}` | `{row['level_status']}` |"
        )
    if len(row_list) > 20:
        lines.append("| ... | ... | ... | ... | ... | ... | ... | ... |")

    lines.extend(
        [
            "",
            "## Reading Notes",
            "",
            "- `ready` はこのM3 PoC内で次の確認へ渡せる状態です。"
            "DB保存可能やマスタ照合成功を意味しません。",
            "- `play_style` / `difficulty` / `level` は M3-3 の "
            "`adoption_candidate` を反映しますが、"
            "採用済みテンプレート照合として扱いません。",
            "- `song_title` / `artist` の `pre_normalized_text` はレビュー用文字列であり、"
            "曲名正規化やファジーマッチの成功扱いにしません。",
            "- duplicate、`rejected_transition`、未確定候補、non-result はこの集約対象外です。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def m3_save_candidate_blocker_key(row: dict[str, str], field_name: str) -> tuple[str, str]:
    status = row[f"{field_name}_status"]
    failure_reason = row[f"{field_name}_failure_reason"]
    return status, failure_reason


def m3_save_candidate_blocker_representative(
    row: dict[str, str],
    field_name: str,
) -> dict[str, str]:
    image_stem = Path(row["organized_file"]).stem
    return {
        "organized_file": row["organized_file"],
        "overall_status": row["overall_status"],
        "blocking_fields": row["blocking_fields"],
        "expected_value": row[f"{field_name}_expected_value"],
        "extracted_value": row[f"{field_name}_extracted_value"],
        "extractor": row[f"{field_name}_extractor"],
        "roi_path": m3_chart_field_roi_path(image_stem, field_name),
    }


def summarize_m3_save_candidate_blockers(
    rows: Iterable[dict[str, str]],
    representative_limit: int = M3_SAVE_CANDIDATE_BLOCKER_REPRESENTATIVE_LIMIT,
) -> dict[str, object]:
    row_list = list(rows)
    fields: dict[str, dict[str, object]] = {}
    blocker_row_indexes: set[int] = set()

    for field_name in M3_SAVE_CANDIDATE_FIELDS:
        groups: dict[tuple[str, str], dict[str, object]] = {}
        total_blockers = 0
        for row_index, row in enumerate(row_list):
            if row[f"{field_name}_status"] == "ready":
                continue
            blocker_row_indexes.add(row_index)
            total_blockers += 1
            key = m3_save_candidate_blocker_key(row, field_name)
            bucket = groups.setdefault(
                key,
                {
                    "status": key[0],
                    "failure_reason": key[1],
                    "count": 0,
                    "representatives": [],
                },
            )
            bucket["count"] = int(bucket["count"]) + 1
            representatives = bucket["representatives"]
            assert isinstance(representatives, list)
            if len(representatives) < representative_limit:
                representatives.append(
                    m3_save_candidate_blocker_representative(row, field_name)
                )

        fields[field_name] = {
            "blocker_count": total_blockers,
            "groups": sorted(
                groups.values(),
                key=lambda item: (
                    str(item["status"]),
                    str(item["failure_reason"]),
                ),
            ),
        }

    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "scope": "M3-6 save candidate blocker representative review",
        "target_count": len(row_list),
        "blocker_candidate_count": len(blocker_row_indexes),
        "representative_limit_per_group": representative_limit,
        "fields": fields,
        "reading_notes": [
            "Representatives are selected from M3-5 save candidate aggregate rows.",
            "Only confirmed-events save candidates are included.",
            "duplicate, rejected_transition, unconfirmed, and non-result rows are excluded.",
            "Blockers are review aids, not DB save decisions or master matching results.",
        ],
    }


def write_m3_save_candidate_blockers_report(
    path: Path,
    blocker_summary: dict[str, object],
) -> None:
    fields = blocker_summary["fields"]
    assert isinstance(fields, dict)
    lines = [
        "# M3 Save Candidate Blockers",
        "",
        "M3-5集約から、保存前に止める理由をfield別に代表化するM3-6レビュー補助です。",
        "DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化には進みません。",
        "",
        f"- target boundary: `{blocker_summary['target_boundary']}`",
        f"- target confirmed-events: {blocker_summary['target_count']}",
        f"- candidates with blockers: {blocker_summary['blocker_candidate_count']}",
        f"- representative limit per group: "
        f"{blocker_summary['representative_limit_per_group']}",
        "",
        "## Field Blockers",
        "",
        "| field | blocker count | grouped reasons |",
        "|---|---:|---|",
    ]
    for field_name in M3_SAVE_CANDIDATE_FIELDS:
        bucket = fields[field_name]
        assert isinstance(bucket, dict)
        group_labels = []
        groups = bucket["groups"]
        assert isinstance(groups, list)
        for group in groups:
            assert isinstance(group, dict)
            status = group["status"]
            reason = group["failure_reason"] or "(none)"
            count = group["count"]
            group_labels.append(f"{status}:{reason}={count}")
        grouped = ", ".join(group_labels) if group_labels else "none"
        lines.append(
            f"| `{field_name}` | {bucket['blocker_count']} | `{grouped}` |"
        )

    lines.extend(["", "## Representatives", ""])
    for field_name in M3_SAVE_CANDIDATE_FIELDS:
        bucket = fields[field_name]
        assert isinstance(bucket, dict)
        groups = bucket["groups"]
        assert isinstance(groups, list)
        if not groups:
            continue
        lines.extend([f"### `{field_name}`", ""])
        for group in groups:
            assert isinstance(group, dict)
            status = group["status"]
            reason = group["failure_reason"] or "(none)"
            lines.extend(
                [
                    f"- status `{status}`, failure `{reason}`, count {group['count']}",
                    "",
                    "| organized_file | expected | extracted | extractor | roi_path |",
                    "|---|---|---|---|---|",
                ]
            )
            representatives = group["representatives"]
            assert isinstance(representatives, list)
            for representative in representatives:
                assert isinstance(representative, dict)
                lines.append(
                    f"| `{representative['organized_file']}` | "
                    f"`{representative['expected_value']}` | "
                    f"`{representative['extracted_value']}` | "
                    f"`{representative['extractor']}` | "
                    f"`{representative['roi_path']}` |"
                )
            lines.append("")

    lines.extend(
        [
            "## Reading Notes",
            "",
            "- このレポートは confirmed-events 境界だけを対象にします。",
            "- duplicate、`rejected_transition`、未確定候補、non-result は対象外です。",
            "- `song_title` / `artist` の代表はOCR入口の観察であり、"
            "曲名正規化やマスタ照合の成功/失敗判定ではありません。",
            "- `difficulty` / `level` の `missing_reference` は追加テンプレート素材不足の"
            "レビュー補助であり、DB保存実装へは進みません。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def m3_save_candidate_blocker_resolution_action(
    field_name: str,
    status: str,
    failure_reason: str,
) -> tuple[str, str, int]:
    if field_name in {"difficulty", "level"} and status == "missing_reference":
        if failure_reason == "missing_reference":
            return (
                "add_template_references",
                "不足しているローカルテンプレート参照ラベルを追加し、holdoutを再確認する。",
                10 if field_name == "difficulty" else 20,
            )
        if failure_reason == "field_needs_template_references":
            return (
                "rerun_after_reference_update",
                "不足ラベル追加後にfield全体が採用候補へ戻るか再確認する。",
                30 if field_name == "difficulty" else 40,
            )
    if field_name in M3_SONG_ARTIST_OCR_FIELDS:
        if failure_reason == "ocr_not_run":
            return (
                "run_m3_song_artist_ocr",
                "`--m3-song-artist-ocr` でOCR入口を実行し、実失敗理由へ分解する。",
                50 if field_name == "song_title" else 51,
            )
        if failure_reason == "engine_unavailable":
            return (
                "prepare_ocr_engine",
                "OCRエンジンの利用可否を確認し、"
                "PoCが engine_unavailable で壊れないことも維持する。",
                55,
            )
        if failure_reason == "empty_ocr":
            return (
                "inspect_ocr_entry_failures",
                "代表ROIとOCR前処理画像を見て、OCR入口の空読み失敗として整理する。",
                60 if field_name == "song_title" else 70,
            )
    return (
        "review_blocker_group",
        "代表ROIと集約行を確認し、次のPoC単位へ分ける。",
        90,
    )


def m3_save_candidate_value_counts(
    rows: Iterable[dict[str, str]],
    field_name: str,
) -> dict[str, int]:
    counts: dict[str, int] = {}
    for row in rows:
        value = row[f"{field_name}_expected_value"]
        if not value:
            value = "(empty)"
        counts[value] = counts.get(value, 0) + 1
    return dict(sorted(counts.items(), key=lambda item: (-item[1], item[0])))


def summarize_m3_save_candidate_blocker_resolution(
    rows: Iterable[dict[str, str]],
    representative_limit: int = M3_SAVE_CANDIDATE_BLOCKER_REPRESENTATIVE_LIMIT,
) -> dict[str, object]:
    row_list = list(rows)
    grouped_rows: dict[tuple[str, str, str], list[dict[str, str]]] = {}
    blocker_row_indexes: set[int] = set()

    for row_index, row in enumerate(row_list):
        for field_name in M3_SAVE_CANDIDATE_FIELDS:
            status = row[f"{field_name}_status"]
            if status == "ready":
                continue
            failure_reason = row[f"{field_name}_failure_reason"]
            grouped_rows.setdefault((field_name, status, failure_reason), []).append(row)
            blocker_row_indexes.add(row_index)

    items: list[dict[str, object]] = []
    for (field_name, status, failure_reason), group_rows in grouped_rows.items():
        action, action_note, sort_priority = m3_save_candidate_blocker_resolution_action(
            field_name,
            status,
            failure_reason,
        )
        required_reference_label_counts: dict[str, int] = {}
        if (
            field_name in {"difficulty", "level"}
            and status == "missing_reference"
            and failure_reason == "missing_reference"
        ):
            required_reference_label_counts = m3_save_candidate_value_counts(
                group_rows,
                field_name,
            )
        items.append(
            {
                "sort_priority": sort_priority,
                "field": field_name,
                "status": status,
                "failure_reason": failure_reason,
                "blocker_count": len(group_rows),
                "action": action,
                "action_note": action_note,
                "expected_value_counts": m3_save_candidate_value_counts(
                    group_rows,
                    field_name,
                ),
                "required_reference_label_counts": required_reference_label_counts,
                "representatives": [
                    m3_save_candidate_blocker_representative(row, field_name)
                    for row in group_rows[:representative_limit]
                ],
            }
        )

    items.sort(
        key=lambda item: (
            int(item["sort_priority"]),
            -int(item["blocker_count"]),
            str(item["field"]),
            str(item["failure_reason"]),
        )
    )
    for priority, item in enumerate(items, start=1):
        item["priority"] = priority
        del item["sort_priority"]

    return {
        "target_boundary": "confirmed_result=true and duplicate=false",
        "scope": "M3-7 save candidate blocker resolution order",
        "target_count": len(row_list),
        "blocker_candidate_count": len(blocker_row_indexes),
        "representative_limit_per_group": representative_limit,
        "resolution_order": items,
        "reading_notes": [
            "Resolution order is derived from M3-5 save candidate aggregate rows.",
            "This is a review plan for local templates and OCR entry failures.",
            "Template images, PoC outputs, and OCR images remain outside Git.",
            "The plan is not a DB save decision, master matching result, "
            "fuzzy match, or normalization result.",
        ],
    }


def write_m3_save_candidate_blocker_resolution_report(
    path: Path,
    resolution_summary: dict[str, object],
) -> None:
    items = resolution_summary["resolution_order"]
    assert isinstance(items, list)
    lines = [
        "# M3 Save Candidate Blocker Resolution Order",
        "",
        "M3-6代表整理を入力に、保存前ブロッカーの解消順を読むM3-7レビュー補助です。",
        "ローカルテンプレート参照ラベルと曲名/artist OCR入口失敗を分けます。",
        "DB保存可否判定、マスタ照合、ファジーマッチ、曲名正規化には進みません。",
        "",
        f"- target boundary: `{resolution_summary['target_boundary']}`",
        f"- target confirmed-events: {resolution_summary['target_count']}",
        f"- candidates with blockers: {resolution_summary['blocker_candidate_count']}",
        "",
        "## Resolution Order",
        "",
        "| priority | field | blockers | status | failure | action | labels to add |",
        "|---:|---|---:|---|---|---|---|",
    ]
    for item in items:
        assert isinstance(item, dict)
        labels = item["required_reference_label_counts"]
        assert isinstance(labels, dict)
        label_text = json.dumps(labels, ensure_ascii=False, sort_keys=True)
        if not labels:
            label_text = ""
        lines.append(
            f"| {item['priority']} | `{item['field']}` | {item['blocker_count']} | "
            f"`{item['status']}` | `{item['failure_reason']}` | "
            f"`{item['action']}` | `{label_text}` |"
        )

    lines.extend(["", "## Representatives", ""])
    for item in items:
        assert isinstance(item, dict)
        representatives = item["representatives"]
        assert isinstance(representatives, list)
        if not representatives:
            continue
        lines.extend(
            [
                f"### {item['priority']}. `{item['field']}` / `{item['action']}`",
                "",
                f"- note: {item['action_note']}",
                "",
                "| organized_file | expected | extracted | extractor | roi_path |",
                "|---|---|---|---|---|",
            ]
        )
        for representative in representatives:
            assert isinstance(representative, dict)
            lines.append(
                f"| `{representative['organized_file']}` | "
                f"`{representative['expected_value']}` | "
                f"`{representative['extracted_value']}` | "
                f"`{representative['extractor']}` | "
                f"`{representative['roi_path']}` |"
            )
        lines.append("")

    lines.extend(
        [
            "## Reading Notes",
            "",
            "- `add_template_references` は必要ラベルと判断だけをdocsへ残し、"
            "画像はローカル素材としてGit管理しません。",
            "- `run_m3_song_artist_ocr` は `--no-ocr` 実行時の未実行状態を、"
            "OCR入口の実失敗理由へ分解するための次手です。",
            "- `inspect_ocr_entry_failures` はOCR入口の観察であり、"
            "曲名正規化やマスタ照合の失敗判定ではありません。",
            "- `rerun_after_reference_update` は不足ラベル追加後の再確認で、"
            "採用済みテンプレート照合の本番実装ではありません。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def m3_chart_field_template_diagnostic_tables(
    rows: Iterable[dict[str, str]],
) -> tuple[
    dict[str, dict[str, int]],
    dict[tuple[str, str, str, str], int],
    dict[str, list[dict[str, str]]],
]:
    status_by_field = {
        field_name: {
            "match": 0,
            "mismatch": 0,
            "empty_extraction": 0,
            "no_expected_value": 0,
            "skipped": 0,
        }
        for field_name in M3_CHART_FIELD_FIELDS
    }
    mismatch_confusions: dict[tuple[str, str, str, str], int] = {}
    representative_mismatches = {field_name: [] for field_name in M3_CHART_FIELD_FIELDS}

    for row in rows:
        field_name = row["field_name"]
        status = row["status"]
        status_by_field[field_name][status] = status_by_field[field_name].get(status, 0) + 1
        if status != "mismatch":
            continue

        key = (
            field_name,
            row["expected_value"],
            row["extracted_value"],
            row.get("failure_reason", ""),
        )
        mismatch_confusions[key] = mismatch_confusions.get(key, 0) + 1
        representatives = representative_mismatches[field_name]
        if len(representatives) < 8:
            representatives.append(row)

    return status_by_field, mismatch_confusions, representative_mismatches


def write_m3_chart_field_template_diagnostics_rows(
    path: Path,
    rows: Iterable[dict[str, str]],
    *,
    title: str = "M3 Chart Field Template Diagnostics",
    extractor_method: str = M3_CHART_FIELD_TEMPLATE_EXTRACTION_METHOD,
    reference_mode: str = (
        "`chart_field_templates` + confirmed-events result references with leave-one-out "
        "self exclusion"
    ),
    comparison_note: str = (
        "これはローカルテンプレート素材と confirmed-events result ROI の比較PoCであり、"
    ),
) -> None:
    row_list = list(rows)
    status_by_field, mismatch_confusions, representative_mismatches = (
        m3_chart_field_template_diagnostic_tables(row_list)
    )
    difficulty_review_candidates = [
        row
        for row in row_list
        if row["field_name"] == "difficulty" and row["status"] == "mismatch"
    ]
    lines = [
        f"# {title}",
        "",
        f"`{extractor_method}` の mismatch と期待値レビュー候補を読むための診断レポートです。",
        comparison_note,
        "OCR、採用済みテンプレート照合、マスタ照合の成功扱いにはしません。",
        "",
        "- target boundary: `confirmed_result=true and duplicate=false`",
        f"- extractor: `{extractor_method}`",
        f"- reference mode: {reference_mode}",
        "- status vocabulary: `match` / `mismatch` / `empty_extraction` / "
        "`no_expected_value` / `skipped`",
        "",
        "## Field Summary",
        "",
        "| field | match | mismatch | empty | no expected | skipped |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for field_name in M3_CHART_FIELD_FIELDS:
        bucket = status_by_field[field_name]
        lines.append(
            f"| `{field_name}` | {bucket['match']} | {bucket['mismatch']} | "
            f"{bucket['empty_extraction']} | {bucket['no_expected_value']} | "
            f"{bucket['skipped']} |"
        )

    lines.extend(
        [
            "",
            "## Mismatch Confusions",
            "",
            "| field | expected | extracted | reason | count |",
            "|---|---|---|---|---:|",
        ]
    )
    confusion_items = sorted(
        mismatch_confusions.items(),
        key=lambda item: (-item[1], item[0]),
    )
    if confusion_items:
        for (field_name, expected_value, extracted_value, failure_reason), count in (
            confusion_items
        ):
            lines.append(
                f"| `{field_name}` | `{expected_value}` | `{extracted_value}` | "
                f"`{failure_reason}` | {count} |"
            )
    else:
        lines.append("| - | - | - | - | 0 |")

    lines.extend(["", "## Representative Mismatches", ""])
    for field_name in M3_CHART_FIELD_FIELDS:
        lines.extend(
            [
                f"### `{field_name}`",
                "",
                "| organized_file | expected | extracted | reason | distance | source | "
                "nearest | roi |",
                "|---|---|---|---|---:|---|---|---|",
            ]
        )
        representatives = representative_mismatches[field_name]
        if not representatives:
            lines.append("| - | - | - | - | - | - | - | - |")
        else:
            for row in representatives:
                lines.append(
                    f"| `{row['organized_file']}` | `{row['expected_value']}` | "
                    f"`{row['extracted_value']}` | `{row.get('failure_reason', '')}` | "
                    f"{row.get('nearest_distance', '')} | "
                    f"`{row.get('nearest_source_type', '')}` | "
                    f"`{row.get('nearest_template_path', '')}` | "
                    f"`{row.get('roi_path', '')}` |"
                )
        lines.append("")

    lines.extend(
        [
            "## Difficulty Expected Review Candidates",
            "",
            "`difficulty` の mismatch は、抽出ロジックの失敗だけでなく、"
            "ROIの見た目と metadata / ファイル名由来期待値の食い違い候補として読む。",
            "",
            "| organized_file | expected | extracted | source | nearest | roi |",
            "|---|---|---|---|---|---|",
        ]
    )
    if difficulty_review_candidates:
        for row in difficulty_review_candidates:
            lines.append(
                f"| `{row['organized_file']}` | `{row['expected_value']}` | "
                f"`{row['extracted_value']}` | `{row.get('nearest_source_type', '')}` | "
                f"`{row.get('nearest_template_path', '')}` | "
                f"`{row.get('roi_path', '')}` |"
            )
    else:
        lines.append("| - | - | - | - | - | - |")

    lines.extend(
        [
            "",
            "## Reading Notes",
            "",
            "- `play_style` と `level` が全件matchでも、同分布内の leave-one-out 診断として読む。",
            "- `difficulty` の候補は実画像、ROI PNG、metadata、ファイル名を突き合わせて、"
            "どれを正とするかを別途決める。",
            "- 参照が confirmed-events 由来の場合、評価中の同一フレームは参照から除外されている。",
            "",
        ]
    )
    path.write_text("\n".join(lines), encoding="utf-8")


def m3_chart_field_image_feature_diagnostic_tables(
    rows: Iterable[dict[str, str]],
) -> tuple[
    dict[str, dict[str, int]],
    dict[tuple[str, str, str], int],
    dict[str, list[dict[str, str]]],
]:
    status_by_field = {
        field_name: {
            "match": 0,
            "mismatch": 0,
            "empty_extraction": 0,
            "no_expected_value": 0,
            "skipped": 0,
        }
        for field_name in M3_CHART_FIELD_FIELDS
    }
    mismatch_confusions: dict[tuple[str, str, str], int] = {}
    representative_mismatches = {field_name: [] for field_name in M3_CHART_FIELD_FIELDS}

    for row in rows:
        field_name = row["field_name"]
        status = row["status"]
        status_by_field[field_name][status] = status_by_field[field_name].get(status, 0) + 1
        if status != "mismatch":
            continue

        key = (field_name, row["expected_value"], row["extracted_value"])
        mismatch_confusions[key] = mismatch_confusions.get(key, 0) + 1
        representatives = representative_mismatches[field_name]
        if len(representatives) < 8:
            representatives.append(row)

    return status_by_field, mismatch_confusions, representative_mismatches


def write_m3_chart_field_image_feature_diagnostics(
    path: Path,
    frames: Iterable[FrameInput],
    events: Iterable[ResultEvent],
) -> None:
    rows = m3_chart_field_image_feature_extraction_rows(frames, events)
    write_m3_chart_field_image_feature_diagnostics_rows(path, rows)


def write_m3_chart_field_image_feature_diagnostics_rows(
    path: Path,
    rows: Iterable[dict[str, str]],
) -> None:
    status_by_field, mismatch_confusions, representative_mismatches = (
        m3_chart_field_image_feature_diagnostic_tables(rows)
    )
    lines = [
        "# M3 Chart Field Image Feature Diagnostics",
        "",
        "`roi-feature-nearest-centroid` の mismatch を読むための診断レポートです。",
        "これはROI画像特徴の軽い比較baselineであり、OCR、テンプレート照合、"
        "マスタ照合の成功扱いにはしません。",
        "",
        "- target boundary: `confirmed_result=true and duplicate=false`",
        "- status vocabulary: `match` / `mismatch` / `empty_extraction` / "
        "`no_expected_value` / `skipped`",
        "",
        "## Field Summary",
        "",
        "| field | match | mismatch | empty | no expected | skipped |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for field_name in M3_CHART_FIELD_FIELDS:
        bucket = status_by_field[field_name]
        lines.append(
            f"| `{field_name}` | {bucket['match']} | {bucket['mismatch']} | "
            f"{bucket['empty_extraction']} | {bucket['no_expected_value']} | "
            f"{bucket['skipped']} |"
        )

    lines.extend(
        [
            "",
            "## Mismatch Confusions",
            "",
            "| field | expected | extracted | count |",
            "|---|---|---|---:|",
        ]
    )
    confusion_items = sorted(
        mismatch_confusions.items(),
        key=lambda item: (-item[1], item[0]),
    )
    if confusion_items:
        for (field_name, expected_value, extracted_value), count in confusion_items:
            lines.append(
                f"| `{field_name}` | `{expected_value}` | `{extracted_value}` | {count} |"
            )
    else:
        lines.append("| - | - | - | 0 |")

    lines.extend(["", "## Representative Mismatches", ""])
    for field_name in M3_CHART_FIELD_FIELDS:
        lines.extend(
            [
                f"### `{field_name}`",
                "",
                "| organized_file | expected | extracted | distance | roi |",
                "|---|---|---|---:|---|",
            ]
        )
        representatives = representative_mismatches[field_name]
        if not representatives:
            lines.append("| - | - | - | - | - |")
        else:
            for row in representatives:
                lines.append(
                    f"| `{row['organized_file']}` | `{row['expected_value']}` | "
                    f"`{row['extracted_value']}` | {row['nearest_distance']} | "
                    f"`{row['roi_path']}` |"
                )
        lines.append("")

    level_bucket = status_by_field["level"]
    level_target_count = level_bucket["match"] + level_bucket["mismatch"]
    level_is_weak = level_target_count > 0 and level_bucket["match"] * 2 < level_target_count
    lines.extend(
        [
            "## Reading Notes",
            "",
            "- `play_style` は mismatch が少ない場合も、ROI画像特徴baselineの診断結果として読む。",
            "- `difficulty` は mismatch confusions を見て、色特徴だけで分けられる組み合わせと"
            "分けにくい組み合わせを次作業で確認する。",
        ]
    )
    if level_is_weak:
        lines.append(
            "- `level` は match が半数未満のため、この単純ROI特徴baselineを採用候補にしない。"
            "次はレベルROIだけを対象にした数字テンプレート比較、またはOCR前処理とは分けた"
            "軽い形状特徴を検討する。"
        )
    else:
        lines.append(
            "- `level` はこのレポートだけで採用判断せず、代表mismatchとROI画像を確認する。"
        )
    lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def write_ocr_expected_coverage_report(
    path: Path,
    score_summary: ScoreOcrSummary,
    profile_summary: dict[str, object] | None = None,
) -> None:
    lines = [
        "# OCR Expected Value Coverage",
        "",
        f"- OCR target mode: `{score_summary.ocr_target_mode}`",
        "- `evaluated`: every OCR attempt for the ROI had an expected value.",
        "- `partially_evaluated`: some attempts had expected values and some did not.",
        "- `no_expected_values`: OCR was attempted, but no rows had expected values; "
        "OCR accuracy is not evaluated.",
        "",
        "## Default OCR Output",
        "",
        "| ROI | evaluation_status | expected values | no expected values | total attempts |",
        "| --- | --- | ---: | ---: | ---: |",
    ]

    for roi_name, coverage in sorted(score_summary.expected_coverage_by_roi.items()):
        lines.append(
            f"| `{roi_name}` | `{coverage['evaluation_status']}` | "
            f"{coverage['expected_value_count']} | {coverage['no_expected_value_count']} | "
            f"{coverage['total_ocr_attempts']} |"
        )

    judgment_rois = [roi for roi in OCR_ROIS if roi != "score_digits"]
    unevaluated = [
        roi
        for roi in judgment_rois
        if score_summary.expected_coverage_by_roi.get(roi, {}).get("evaluation_status")
        == "no_expected_values"
    ]
    if unevaluated:
        lines.extend(
            [
                "",
                "## Unevaluated Judgment ROIs",
                "",
                "These ROIs need metadata columns before OCR accuracy can be judged:",
                "",
            ]
        )
        lines.extend(f"- `{roi}`" for roi in unevaluated)

    if profile_summary is not None:
        best_by_roi = profile_summary.get("best_by_roi", {})
        if isinstance(best_by_roi, dict):
            lines.extend(
                [
                    "",
                    "## Profile Comparison Coverage",
                    "",
                    "| ROI | evaluation_status | recommendation readiness | "
                    "recommendation basis | expected values | no expected values |",
                    "| --- | --- | --- | --- | ---: | ---: |",
                ]
            )
            for roi_name, raw_bucket in sorted(best_by_roi.items()):
                if not isinstance(raw_bucket, dict):
                    continue
                lines.append(
                    f"| `{roi_name}` | `{raw_bucket.get('evaluation_status', '')}` | "
                    f"`{raw_bucket.get('recommendation_readiness', '')}` | "
                    f"`{raw_bucket.get('recommendation_basis', '')}` | "
                    f"{raw_bucket.get('expected_value_count', 0)} | "
                    f"{raw_bucket.get('no_expected_value_count', 0)} |"
                )

    path.write_text("\n".join(lines), encoding="utf-8")


def flatten_result_event(event: ResultEvent) -> dict[str, str | int | bool]:
    return asdict(event)


def write_result_events_csv(path: Path, events: Iterable[ResultEvent]) -> None:
    fieldnames = [
        "frame_index",
        "organized_file",
        "screen_type",
        "result_candidate",
        "result_shape_candidate",
        "confirmed_result",
        "event_type",
        "duplicate",
        "duplicate_key",
        "reason",
        "timestamp_ms",
        "candidate_duration_ms",
        "confirmation_mode",
    ]
    rows = [flatten_result_event(item) for item in events]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def summarize_result_events(events: list[ResultEvent]) -> dict[str, object]:
    confirmation_mode_counts: dict[str, int] = {}
    for event in events:
        confirmation_mode_counts[event.confirmation_mode] = (
            confirmation_mode_counts.get(event.confirmation_mode, 0) + 1
        )

    first_confirmed = next((event for event in events if event.confirmed_result), None)
    return {
        "total": len(events),
        "confirmed_count": sum(event.event_type == "confirmed" for event in events),
        "confirmed_result_count": sum(event.confirmed_result for event in events),
        "duplicate_count": sum(event.event_type == "duplicate" for event in events),
        "rejected_transition_count": sum(
            event.event_type == "rejected_transition" for event in events
        ),
        "first_confirmed_frame_index": (
            first_confirmed.frame_index if first_confirmed is not None else None
        ),
        "first_confirmed_timestamp_ms": (
            first_confirmed.timestamp_ms if first_confirmed is not None else None
        ),
        "confirmation_mode_counts": confirmation_mode_counts,
    }


def summarize(classifications: list[Classification]) -> dict[str, object]:
    by_type: dict[str, dict[str, int]] = {}
    for item in classifications:
        bucket = by_type.setdefault(
            item.screen_type,
            {"total": 0, "result_candidate": 0, "shape_candidate": 0, "correct": 0},
        )
        bucket["total"] += 1
        bucket["result_candidate"] += int(item.result_candidate)
        bucket["shape_candidate"] += int(item.result_shape_candidate)
        bucket["correct"] += int(item.correct)

    false_positives = [
        item.organized_file
        for item in classifications
        if item.result_candidate and not item.expected_result_candidate
    ]
    false_negatives = [
        item.organized_file
        for item in classifications
        if not item.result_candidate and item.expected_result_candidate
    ]
    countup_shape = [
        item.organized_file
        for item in classifications
        if item.transition_kind == "countup" and item.result_shape_candidate
    ]
    return {
        "total": len(classifications),
        "correct": sum(int(item.correct) for item in classifications),
        "accuracy": (
            sum(int(item.correct) for item in classifications) / len(classifications)
            if classifications
            else 0.0
        ),
        "by_type": by_type,
        "false_positives": false_positives,
        "false_negatives": false_negatives,
        "transition_countup_shape_candidates": countup_shape,
    }


def write_misclassification_notes(path: Path, classifications: list[Classification]) -> None:
    lines = [
        "# Vision PoC Misclassification Notes",
        "",
        "OCRなしの固定ROI特徴量による初期PoCのメモです。",
        "",
    ]
    misses = [item for item in classifications if not item.correct]
    if not misses:
        lines.extend(
            [
                "## 誤検出/見逃し",
                "",
                "現サンプルでは `result_candidate` の誤検出/見逃しはありません。",
                "",
            ]
        )
    else:
        lines.extend(["## 誤検出/見逃し", ""])
        for item in misses:
            kind = "誤検出" if item.result_candidate else "見逃し"
            hypothesis = (
                "RESULTSヘッダー、詳細枠、スコア周辺の三条件が非リザルト画面でも同時に成立。"
                if item.result_candidate
                else "スコア周辺の白数字または詳細枠シグナルがしきい値未満。"
            )
            lines.append(f"- `{item.organized_file}`: {kind}。仮説: {hypothesis}")
        lines.append("")

    countups = [
        item
        for item in classifications
        if item.transition_kind == "countup" and item.result_shape_candidate
    ]
    lines.extend(["## transition_countup_*", ""])
    if countups:
        for item in countups:
            lines.append(
                f"- `{item.organized_file}`: リザルト形状は検出。"
                " スコア周辺シグナルが弱いため `result_candidate=false`。"
            )
    else:
        lines.append("- リザルト形状として検出されたカウントアップサンプルはありません。")
    lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def print_summary(summary: dict[str, object], output_dir: Path) -> None:
    print(f"Vision PoC output: {output_dir}")
    print(
        f"Total: {summary['total']}  "
        f"Correct: {summary['correct']}  "
        f"Accuracy: {summary['accuracy']:.3f}"
    )
    print("")
    print("By screen_type:")
    for screen_type, bucket in sorted(summary["by_type"].items()):  # type: ignore[union-attr]
        print(
            f"  {screen_type}: total={bucket['total']} "
            f"result_candidate={bucket['result_candidate']} "
            f"shape_candidate={bucket['shape_candidate']} correct={bucket['correct']}"
        )
    print("")
    print(f"False positives: {len(summary['false_positives'])}")
    for name in summary["false_positives"]:  # type: ignore[union-attr]
        print(f"  FP {name}")
    print(f"False negatives: {len(summary['false_negatives'])}")
    for name in summary["false_negatives"]:  # type: ignore[union-attr]
        print(f"  FN {name}")
    print(
        "transition_countup shape candidates: "
        f"{len(summary['transition_countup_shape_candidates'])}"
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Evaluate OCR-free DDR GP result screen signals.")
    parser.add_argument(
        "--sequence-mode",
        choices=("metadata", "timestamped", "manifest"),
        default="metadata",
        help=(
            "Input sequence mode. metadata keeps legacy frame-based events; timestamped attaches "
            "artificial capture timestamps to the same local image sequence; manifest reads "
            "timestamped frame rows from CSV."
        ),
    )
    parser.add_argument(
        "--metadata",
        type=Path,
        default=Path("samples/screenshots/metadata.csv"),
        help="Path to metadata.csv with organized_file and screen_type columns.",
    )
    parser.add_argument(
        "--screenshots-root",
        type=Path,
        default=Path("samples/screenshots"),
        help="Root directory used to resolve metadata organized_file paths.",
    )
    parser.add_argument(
        "--frame-manifest",
        type=Path,
        default=None,
        help="CSV frame manifest for --sequence-mode manifest; requires image_path,timestamp_ms.",
    )
    parser.add_argument(
        "--frame-root",
        type=Path,
        default=None,
        help=(
            "Optional root directory used to resolve relative image_path values in "
            "--frame-manifest. Defaults to the manifest file directory."
        ),
    )
    parser.add_argument(
        "--make-frame-manifest",
        type=Path,
        default=None,
        help=(
            "Write a frame manifest CSV from images directly under --frame-root and exit. "
            "The CSV is readable by --sequence-mode manifest."
        ),
    )
    parser.add_argument(
        "--make-m2-expanded-manifest",
        type=Path,
        default=None,
        help=(
            "Write an M2 local evaluation manifest under data/ that replays every metadata "
            "result row after a non-result reset and two sustained result frames."
        ),
    )
    parser.add_argument(
        "--capture-dry-run",
        action="store_true",
        help=(
            "Copy images directly under --frame-root into a data/ dry-run frame directory, "
            "write a manifest-compatible frame_manifest.csv, and exit."
        ),
    )
    parser.add_argument(
        "--capture-dry-run-scenario",
        type=Path,
        default=None,
        help=(
            "Read a manifest-compatible dry-run sequence CSV with image_path,timestamp_ms, "
            "copy its frames into data/, preserve optional columns, write frame_manifest.csv, "
            "and exit."
        ),
    )
    parser.add_argument(
        "--capture-dry-run-output",
        type=Path,
        default=Path("data/vision_poc_capture_dry_run"),
        help="Output directory under data/ for --capture-dry-run frames and frame_manifest.csv.",
    )
    parser.add_argument(
        "--fps",
        type=parse_positive_fps,
        default=None,
        help=(
            "Capture FPS used to generate timestamp_ms for --make-frame-manifest and "
            "--capture-dry-run."
        ),
    )
    parser.add_argument(
        "--screen-type",
        default=None,
        help="Optional fixed screen_type value to include in --make-frame-manifest output.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Directory for CSV/JSON logs and cropped ROI images.",
    )
    parser.add_argument(
        "--timestamp-start-ms",
        type=int,
        default=0,
        help="Starting timestamp for --sequence-mode timestamped.",
    )
    parser.add_argument(
        "--timestamp-interval-ms",
        type=int,
        default=1000,
        help="Artificial interval between local frames for --sequence-mode timestamped.",
    )
    parser.add_argument(
        "--no-rois",
        action="store_true",
        help="Skip writing cropped ROI PNG files.",
    )
    parser.add_argument(
        "--no-ocr",
        action="store_true",
        help="Skip score_digits OCR preprocessing and OCR attempts.",
    )
    parser.add_argument(
        "--ocr-target",
        choices=OCR_TARGET_MODES,
        default="result-candidate",
        help=(
            "Frames to OCR. result-candidate preserves the legacy behavior; confirmed-events "
            "OCRs only result_events rows with confirmed_result=true and duplicate=false."
        ),
    )
    parser.add_argument(
        "--ocr-rois",
        nargs="+",
        default=["score_digits"],
        help=(
            "OCR ROI names to preprocess and attempt. Use 'all' for score_digits and judgment "
            f"ROIs. Default: score_digits. Supported: {', '.join(OCR_ROIS)}"
        ),
    )
    parser.add_argument(
        "--ocr-profile",
        nargs="+",
        default=["default"],
        help=(
            "OCR preprocessing profile(s) for comparison outputs. Use 'all' to run every "
            f"profile. Legacy score_ocr.csv always uses default. Supported: "
            f"{', '.join(OCR_PREPROCESS_PROFILES)}"
        ),
    )
    parser.add_argument(
        "--m3-song-artist-ocr",
        action="store_true",
        help=(
            "Run the M3-4 song_title/artist OCR entry report for confirmed-events only. "
            "This does not perform master matching, fuzzy matching, or title normalization."
        ),
    )
    parser.add_argument(
        "--m5-master-match",
        action="store_true",
        help=(
            "Run the M5 master match PoC from the in-memory M3 save candidate rows and a "
            "generated M4 SQLite master DB. This reports observations only and does not save."
        ),
    )
    parser.add_argument(
        "--m5-jacket-match",
        action="store_true",
        help=(
            "Run the M5 jacket feature match PoC using song_select grid preview features and "
            "confirmed-events result jacket ROI features. This reports observations only."
        ),
    )
    parser.add_argument(
        "--master-db",
        type=Path,
        default=Path("data/master/ddrgp-master.sqlite"),
        help="Generated M4 SQLite master DB used by M5 match PoCs.",
    )
    parser.add_argument(
        "--m5-score-threshold",
        type=float,
        default=master_match.DEFAULT_SCORE_THRESHOLD,
        help="Minimum normalized title similarity for M5 PoC matched status.",
    )
    parser.add_argument(
        "--chart-field-template-root",
        type=Path,
        default=M3_CHART_FIELD_TEMPLATE_ROOT,
        help=(
            "Optional local template image directory for M3 roi-template-nearest extraction. "
            "Missing directories are reported as empty_extraction, not fatal."
        ),
    )
    return parser


def resolve_ocr_rois(values: list[str]) -> tuple[str, ...]:
    if values == ["all"]:
        return OCR_ROIS
    unknown = [value for value in values if value not in OCR_ROIS]
    if unknown:
        joined_unknown = ", ".join(unknown)
        joined_supported = ", ".join(OCR_ROIS)
        raise ValueError(f"unsupported OCR ROI(s): {joined_unknown}; expected: {joined_supported}")
    return tuple(dict.fromkeys(values))


def resolve_ocr_profiles(values: list[str]) -> tuple[str, ...]:
    if values == ["all"]:
        return tuple(OCR_PREPROCESS_PROFILES)
    unknown = [value for value in values if value not in OCR_PREPROCESS_PROFILES]
    if unknown:
        joined_unknown = ", ".join(unknown)
        joined_supported = ", ".join(OCR_PREPROCESS_PROFILES)
        raise ValueError(
            f"unsupported OCR profile(s): {joined_unknown}; expected: {joined_supported}"
        )
    return tuple(dict.fromkeys(values))


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.capture_dry_run and args.capture_dry_run_scenario is not None:
        raise ValueError("--capture-dry-run and --capture-dry-run-scenario are mutually exclusive")

    if args.capture_dry_run_scenario is not None:
        manifest_path, frame_count = write_capture_dry_run_scenario(
            args.capture_dry_run_output,
            args.capture_dry_run_scenario,
            args.frame_root,
        )
        print(f"Wrote dry-run sequence manifest: {manifest_path} ({frame_count} frames)")
        return 0

    if args.capture_dry_run:
        if args.frame_root is None:
            raise ValueError("--frame-root is required with --capture-dry-run")
        if args.fps is None:
            raise ValueError("--fps is required with --capture-dry-run")
        manifest_path, frame_count = write_capture_dry_run(
            args.capture_dry_run_output,
            args.frame_root,
            args.fps,
            screen_type=args.screen_type,
        )
        print(f"Wrote dry-run capture manifest: {manifest_path} ({frame_count} frames)")
        return 0

    if args.make_frame_manifest is not None:
        if args.frame_root is None:
            raise ValueError("--frame-root is required with --make-frame-manifest")
        if args.fps is None:
            raise ValueError("--fps is required with --make-frame-manifest")
        frame_count = write_capture_frame_manifest(
            args.make_frame_manifest,
            args.frame_root,
            args.fps,
            screen_type=args.screen_type,
        )
        print(f"Wrote frame manifest: {args.make_frame_manifest} ({frame_count} frames)")
        return 0

    if args.make_m2_expanded_manifest is not None:
        rows = read_metadata(args.metadata)
        manifest_path, frame_count, result_count = write_m2_expanded_confirmed_events_manifest(
            args.make_m2_expanded_manifest,
            rows,
            args.screenshots_root,
        )
        print(
            "Wrote M2 expanded confirmed-events manifest: "
            f"{manifest_path} ({frame_count} frames, {result_count} result events)"
        )
        return 0

    output_dir: Path = args.output or (
        {
            "metadata": Path("data/vision_poc"),
            "timestamped": Path("data/vision_poc_timestamped"),
            "manifest": Path("data/vision_poc_manifest"),
        }[args.sequence_mode]
    )
    output_dir.mkdir(parents=True, exist_ok=True)
    ocr_rois = resolve_ocr_rois(args.ocr_rois)
    ocr_profiles = resolve_ocr_profiles(args.ocr_profile)

    if args.sequence_mode == "manifest":
        if args.frame_manifest is None:
            raise ValueError("--frame-manifest is required when --sequence-mode manifest")
        frames = read_frame_manifest(args.frame_manifest, args.frame_root)
    else:
        rows = read_metadata(args.metadata)
        if args.sequence_mode == "timestamped":
            frames = build_timestamped_frame_inputs(
                rows,
                args.screenshots_root,
                start_ms=args.timestamp_start_ms,
                frame_interval_ms=args.timestamp_interval_ms,
            )
            write_frame_manifest(output_dir / "frame_manifest.csv", frames)
        else:
            frames = build_metadata_frame_inputs(rows, args.screenshots_root)

    if not frames:
        raise ValueError("input sequence contains no frames")

    write_ocr_expected_template(output_dir / "ocr_expected_template.csv", frames)

    classifications: list[Classification] = []
    m3_chart_field_feature_samples: dict[tuple[int, str], dict[str, float]] = {}
    m3_chart_field_template_vectors: dict[tuple[int, str], np.ndarray] = {}
    m5_jacket_result_features: dict[str, master_match.JacketFeature] = {}
    m5_jacket_song_select_preview_features: dict[str, master_match.JacketFeature] = {}
    m5_title_result_features: dict[str, master_match.TitleImageFeature] = {}
    for frame_index, frame in enumerate(frames):
        with Image.open(frame.image_path) as image:
            image = image.convert("RGB")
            classification = classify(image, frame.row)
            classifications.append(classification)
            if not args.no_rois:
                save_primary_rois(image, output_dir, frame.image_path.stem)
            for field_name in M3_CHART_FIELD_FIELDS:
                m3_chart_field_feature_samples[(frame_index, field_name)] = (
                    m3_chart_field_feature_values(image, field_name)
                )
                m3_chart_field_template_vectors[(frame_index, field_name)] = (
                    m3_chart_field_template_vector(image, field_name)
                )
            organized_file = frame.row.get("organized_file", "")
            if frame.row.get("screen_type") == "result":
                m5_jacket_result_features[organized_file] = master_match.extract_jacket_feature(
                    crop_roi(image, ROI_DEFINITIONS["jacket"])
                )
                m5_title_result_features[organized_file] = (
                    master_match.extract_title_image_feature(
                        crop_roi(image, ROI_DEFINITIONS["song_title"])
                    )
                )
            if is_song_select_grid_frame(frame):
                m5_jacket_song_select_preview_features[organized_file] = (
                    master_match.extract_jacket_feature(
                        crop_roi(image, ROI_DEFINITIONS["song_select_grid_preview_jacket"])
                    )
                )

    write_results_csv(output_dir / "results.csv", classifications)
    timestamps_ms = (
        None if args.sequence_mode == "metadata" else [frame.timestamp_ms for frame in frames]
    )
    result_events = build_result_events(classifications, timestamps_ms=timestamps_ms)
    write_result_events_csv(output_dir / "result_events.csv", result_events)
    result_events_summary = summarize_result_events(result_events)
    (output_dir / "result_events_summary.json").write_text(
        json.dumps(result_events_summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    m3_expected_coverage = summarize_m3_metadata_expected_coverage(frames, result_events)
    write_m3_metadata_expected_template(
        output_dir / "m3_metadata_expected_template.csv",
        frames,
        result_events,
    )
    write_m3_metadata_expected_report(
        output_dir / "m3_metadata_expected_coverage.md",
        m3_expected_coverage,
    )
    write_m3_chart_fields_csv(
        output_dir / "m3_chart_fields.csv",
        frames,
        result_events,
    )
    (output_dir / "m3_chart_fields_summary.json").write_text(
        json.dumps(
            summarize_m3_chart_fields(frames, result_events),
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    write_m3_chart_field_extraction_csv(
        output_dir / "m3_chart_field_extraction.csv",
        frames,
        result_events,
    )
    (output_dir / "m3_chart_field_extraction_summary.json").write_text(
        json.dumps(
            summarize_m3_chart_field_extraction(frames, result_events),
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    m3_chart_field_image_feature_rows = m3_chart_field_image_feature_extraction_rows(
        frames,
        result_events,
        m3_chart_field_feature_samples,
    )
    write_m3_chart_field_image_feature_extraction_rows_csv(
        output_dir / "m3_chart_field_image_feature_extraction.csv",
        m3_chart_field_image_feature_rows,
    )
    (output_dir / "m3_chart_field_image_feature_extraction_summary.json").write_text(
        json.dumps(
            summarize_m3_chart_field_image_feature_extraction_rows(
                m3_chart_field_image_feature_rows
            ),
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    write_m3_chart_field_image_feature_diagnostics_rows(
        output_dir / "m3_chart_field_image_feature_diagnostics.md",
        m3_chart_field_image_feature_rows,
    )
    m3_chart_field_template_rows = m3_chart_field_template_extraction_rows(
        frames,
        result_events,
        args.chart_field_template_root,
        target_vectors=m3_chart_field_template_vectors,
    )
    write_m3_chart_field_template_extraction_rows_csv(
        output_dir / "m3_chart_field_template_extraction.csv",
        m3_chart_field_template_rows,
    )
    write_m3_chart_field_template_diagnostics_rows(
        output_dir / "m3_chart_field_template_diagnostics.md",
        m3_chart_field_template_rows,
    )
    (output_dir / "m3_chart_field_template_extraction_summary.json").write_text(
        json.dumps(
            summarize_m3_chart_field_template_extraction_rows(
                m3_chart_field_template_rows,
                frames,
                result_events,
                args.chart_field_template_root,
            ),
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    m3_chart_field_template_holdout_rows = m3_chart_field_template_holdout_extraction_rows(
        frames,
        result_events,
        args.chart_field_template_root,
        target_vectors=m3_chart_field_template_vectors,
    )
    write_m3_chart_field_template_extraction_rows_csv(
        output_dir / "m3_chart_field_template_holdout_extraction.csv",
        m3_chart_field_template_holdout_rows,
    )
    write_m3_chart_field_template_diagnostics_rows(
        output_dir / "m3_chart_field_template_holdout_diagnostics.md",
        m3_chart_field_template_holdout_rows,
        title="M3 Chart Field Template Holdout Diagnostics",
        extractor_method=M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
        reference_mode=(
            "`chart_field_templates` only; confirmed-events result ROI are evaluation targets only"
        ),
        comparison_note=(
            "これはローカルテンプレート素材だけを参照し、confirmed-events result ROI を"
            "評価専用に分ける比較PoCであり、"
        ),
    )
    (output_dir / "m3_chart_field_template_holdout_extraction_summary.json").write_text(
        json.dumps(
            summarize_m3_chart_field_template_extraction_rows(
                m3_chart_field_template_holdout_rows,
                frames,
                result_events,
                args.chart_field_template_root,
                include_result_references=False,
                extractor_method=M3_CHART_FIELD_TEMPLATE_HOLDOUT_EXTRACTION_METHOD,
                reference_mode=(
                    "nearest ROI image template from chart_field_templates only; "
                    "confirmed-events result ROI are evaluation targets only"
                ),
            ),
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    m3_chart_field_adoption_summary = summarize_m3_chart_field_adoption_candidates(
        m3_chart_field_template_holdout_rows
    )
    (output_dir / "m3_chart_field_adoption_candidates_summary.json").write_text(
        json.dumps(m3_chart_field_adoption_summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_m3_chart_field_adoption_candidates_report(
        output_dir / "m3_chart_field_adoption_candidates.md",
        m3_chart_field_adoption_summary,
    )
    m3_song_artist_ocr_results: list[M3SongArtistOcrResult] = []
    if args.m3_song_artist_ocr and not args.no_ocr:
        for frame, event in zip(frames, result_events, strict=True):
            if not is_save_candidate_event(event):
                continue
            with Image.open(frame.image_path) as image:
                image = image.convert("RGB")
                for field_name in M3_SONG_ARTIST_OCR_FIELDS:
                    m3_song_artist_ocr_results.append(
                        process_m3_song_artist_ocr_field(
                            image,
                            frame,
                            event,
                            output_dir,
                            field_name,
                        )
                    )
    if args.m3_song_artist_ocr:
        write_m3_song_artist_ocr_csv(
            output_dir / "m3_song_artist_ocr.csv",
            m3_song_artist_ocr_results,
        )
        m3_song_artist_ocr_summary = summarize_m3_song_artist_ocr(
            m3_song_artist_ocr_results,
            result_events,
        )
        (output_dir / "m3_song_artist_ocr_summary.json").write_text(
            json.dumps(m3_song_artist_ocr_summary, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        write_m3_song_artist_ocr_report(
            output_dir / "m3_song_artist_ocr.md",
            m3_song_artist_ocr_results,
            m3_song_artist_ocr_summary,
        )
        m3_song_artist_entry_failure_summary = summarize_m3_song_artist_ocr_entry_failures(
            m3_song_artist_ocr_results
        )
        (output_dir / "m3_song_artist_ocr_entry_failures_summary.json").write_text(
            json.dumps(
                m3_song_artist_entry_failure_summary,
                ensure_ascii=False,
                indent=2,
            )
            + "\n",
            encoding="utf-8",
        )
        write_m3_song_artist_ocr_entry_failures_report(
            output_dir / "m3_song_artist_ocr_entry_failures.md",
            m3_song_artist_entry_failure_summary,
        )
    m3_save_candidate_summary_rows = m3_save_candidate_rows(
        frames,
        result_events,
        m3_chart_field_template_holdout_rows,
        m3_chart_field_adoption_summary,
        m3_song_artist_ocr_results,
    )
    write_m3_save_candidate_summary_csv(
        output_dir / "m3_save_candidate_summary.csv",
        m3_save_candidate_summary_rows,
    )
    m3_save_candidate_summary = summarize_m3_save_candidates(
        m3_save_candidate_summary_rows
    )
    (output_dir / "m3_save_candidate_summary.json").write_text(
        json.dumps(m3_save_candidate_summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_m3_save_candidate_summary_report(
        output_dir / "m3_save_candidate_summary.md",
        m3_save_candidate_summary_rows,
        m3_save_candidate_summary,
    )
    m3_save_candidate_blockers_summary = summarize_m3_save_candidate_blockers(
        m3_save_candidate_summary_rows
    )
    (output_dir / "m3_save_candidate_blockers_summary.json").write_text(
        json.dumps(m3_save_candidate_blockers_summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_m3_save_candidate_blockers_report(
        output_dir / "m3_save_candidate_blockers_summary.md",
        m3_save_candidate_blockers_summary,
    )
    m3_save_candidate_blocker_resolution_summary = (
        summarize_m3_save_candidate_blocker_resolution(
            m3_save_candidate_summary_rows
        )
    )
    (output_dir / "m3_save_candidate_blocker_resolution_plan.json").write_text(
        json.dumps(
            m3_save_candidate_blocker_resolution_summary,
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    write_m3_save_candidate_blocker_resolution_report(
        output_dir / "m3_save_candidate_blocker_resolution_plan.md",
        m3_save_candidate_blocker_resolution_summary,
    )
    if args.m5_master_match:
        if not args.master_db.exists():
            raise FileNotFoundError(f"--master-db does not exist: {args.master_db}")
        master_match_rows = master_match.match_save_candidate_rows(
            m3_save_candidate_summary_rows,
            args.master_db,
            score_threshold=args.m5_score_threshold,
        )
        master_match_summary = master_match.write_master_match_outputs(
            output_dir,
            master_match_rows,
        )
        print(
            "Wrote M5 master match PoC: "
            f"{output_dir} ({master_match_summary['target_count']} candidates)"
        )
    if args.m5_jacket_match:
        if not args.master_db.exists():
            raise FileNotFoundError(f"--master-db does not exist: {args.master_db}")
        jacket_feature_rows, jacket_feature_entries = build_m5_jacket_feature_master_rows(
            frames,
            args.master_db,
            m5_jacket_song_select_preview_features,
        )
        jacket_feature_summary = master_match.write_jacket_feature_master_outputs(
            output_dir,
            jacket_feature_rows,
        )
        master_match.write_jacket_feature_label_template(
            output_dir / "jacket_feature_label_template.csv",
            m5_jacket_feature_label_template_rows(frames),
        )
        title_feature_entries = build_m5_title_feature_master_entries(
            frames,
            args.master_db,
            m5_title_result_features,
        )
        title_ocr_observations = {
            result.organized_file: master_match.TitleOcrObservation(
                raw=result.ocr_raw,
                text=result.pre_normalized_text,
                status=result.status,
                failure_reason=result.failure_reason,
            )
            for result in m3_song_artist_ocr_results
            if result.field_name == "song_title"
        }
        jacket_match_rows = master_match.match_jacket_save_candidate_rows(
            m3_save_candidate_summary_rows,
            args.master_db,
            m5_jacket_result_features,
            jacket_feature_entries,
            m5_title_result_features,
            title_feature_entries,
            title_ocr_observations,
        )
        jacket_match_summary = master_match.write_jacket_match_outputs(
            output_dir,
            jacket_match_rows,
        )
        jacket_diagnostic_input_rows = m5_jacket_diagnostic_candidate_rows(
            frames,
            result_events,
        )
        jacket_diagnostic_match_rows = master_match.match_jacket_save_candidate_rows(
            jacket_diagnostic_input_rows,
            args.master_db,
            m5_jacket_result_features,
            jacket_feature_entries,
            m5_title_result_features,
            title_feature_entries,
            title_ocr_observations,
        )
        jacket_diagnostic_rows = attach_m5_jacket_diagnostic_context(
            jacket_diagnostic_input_rows,
            jacket_diagnostic_match_rows,
        )
        jacket_diagnostic_summary = master_match.write_jacket_match_diagnostic_outputs(
            output_dir,
            jacket_diagnostic_rows,
        )
        print(
            "Wrote M5 jacket match PoC: "
            f"{output_dir} "
            f"({jacket_feature_summary['status_counts'].get('accepted', 0)} features, "
            f"{jacket_match_summary['target_count']} candidates, "
            f"{jacket_diagnostic_summary['target_count']} diagnostics)"
        )
    score_ocr_results: list[ScoreOcrResult] = []
    profile_ocr_results: list[ProfileScoreOcrResult] = []
    run_profile_comparison = ocr_profiles != ("default",)
    if not args.no_ocr:
        frame_event_rows = zip(frames, classifications, result_events, strict=True)
        for frame, classification, event in frame_event_rows:
            if not is_ocr_target(classification, event, args.ocr_target):
                continue
            with Image.open(frame.image_path) as image:
                image = image.convert("RGB")
                for roi_name in ocr_rois:
                    score_ocr_results.append(
                        process_ocr_roi(image, frame.row, classification, output_dir, roi_name)
                    )
                    if run_profile_comparison:
                        for profile in ocr_profiles:
                            profile_ocr_results.append(
                                process_profile_ocr_roi(
                                    image,
                                    frame.row,
                                    classification,
                                    output_dir,
                                    roi_name,
                                    profile,
                                    OCR_PREPROCESS_PROFILES[profile],
                                )
                            )

    write_score_ocr_csv(output_dir / "score_ocr.csv", score_ocr_results)
    score_ocr_summary = summarize_score_ocr(
        score_ocr_results,
        result_events,
        args.ocr_target,
    )
    (output_dir / "score_ocr_summary.json").write_text(
        json.dumps(asdict(score_ocr_summary), ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    score_ocr_profiles_summary: dict[str, object] | None = None
    if run_profile_comparison:
        write_profile_score_ocr_csv(output_dir / "score_ocr_profiles.csv", profile_ocr_results)
        score_ocr_profiles_summary = summarize_profile_score_ocr(
            profile_ocr_results,
            args.ocr_target,
        )
        (output_dir / "score_ocr_profiles_summary.json").write_text(
            json.dumps(score_ocr_profiles_summary, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
    write_ocr_roi_report(
        output_dir / "ocr_roi_report.md",
        score_ocr_results,
        score_ocr_summary,
        score_ocr_profiles_summary,
    )
    write_ocr_expected_coverage_report(
        output_dir / "ocr_expected_coverage.md",
        score_ocr_summary,
        score_ocr_profiles_summary,
    )
    summary = summarize(classifications)
    (output_dir / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_misclassification_notes(output_dir / "misclassifications.md", classifications)
    print_summary(summary, output_dir)
    return 0 if not summary["false_positives"] and not summary["false_negatives"] else 1
