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
    "song_title": (488, 274, 304, 52),
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
    "rank",
    "score_area",
    "score_digits",
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
    whitelist: str = "0123456789"


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
OCR_PREPROCESS_CONFIG = OcrPreprocessConfig()
OCR_PREPROCESS_PROFILES: dict[str, OcrPreprocessConfig] = {
    "default": OCR_PREPROCESS_CONFIG,
    "high-contrast": OcrPreprocessConfig(luma_threshold=150, channel_spread_max=105),
    "low-threshold": OcrPreprocessConfig(luma_threshold=115, channel_spread_max=165),
    "tighter-white": OcrPreprocessConfig(luma_threshold=165, channel_spread_max=80),
    "no-sharpen": OcrPreprocessConfig(sharpen=False, padding=16),
}
TESSERACT_CONFIG = TesseractConfig()
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
        value=score >= 0.68,
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

    result_shape_candidate = header.value and detail.value and (score_area.value or rank.value)
    result_candidate = header.value and detail.value and score_area.value
    is_countup = Path(row["organized_file"]).name.startswith("transition_countup_")
    transition_kind = "countup" if is_countup else ""
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
            "-c",
            f"tessedit_char_whitelist={config.whitelist}",
        ]
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
            "recommendation_basis_detail": recommendation_basis_detail,
            "recommendation_is_tentative": evaluation_status == "partially_evaluated",
        }

    return {
        "ocr_target_mode": ocr_target_mode,
        "profiles": by_profile,
        "best_by_roi": best_by_roi,
    }


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
        lines.extend([f"### `{roi_name}`", ""])
        lines.append(f"- evaluation_status: `{ocr_evaluation_status(summary.by_roi[roi_name])}`")
        lines.append(f"- recommended profile candidate: {recommended_profiles}")
        lines.append(f"- recommendation_basis: `{recommendation_basis}`")
        lines.append(f"- recommendation note: {recommendation_detail}")
        lines.append(f"- representative mismatch: {format_representative_list(mismatches)}")
        lines.append(f"- representative empty_ocr: {format_representative_list(empties)}")
        lines.append(
            "- next preprocessing image hint: "
            f"mismatch={representative_path_hint(roi_name, mismatches)}, "
            f"empty_ocr={representative_path_hint(roi_name, empties)}"
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
                    "| ROI | evaluation_status | recommendation basis | expected values | "
                    "no expected values |",
                    "| --- | --- | --- | ---: | ---: |",
                ]
            )
            for roi_name, raw_bucket in sorted(best_by_roi.items()):
                if not isinstance(raw_bucket, dict):
                    continue
                lines.append(
                    f"| `{roi_name}` | `{raw_bucket.get('evaluation_status', '')}` | "
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
    for frame in frames:
        with Image.open(frame.image_path) as image:
            image = image.convert("RGB")
            classification = classify(image, frame.row)
            classifications.append(classification)
            if not args.no_rois:
                save_primary_rois(image, output_dir, frame.image_path.stem)

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
