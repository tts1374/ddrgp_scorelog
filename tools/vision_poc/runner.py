from __future__ import annotations

import argparse
import csv
import json
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
)
OCR_PREPROCESS_CONFIG = OcrPreprocessConfig()
TESSERACT_CONFIG = TesseractConfig()


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
                        "organized_file": raw_image_path,
                        "screen_type": row.get("screen_type", ""),
                    },
                    image_path=image_path,
                    timestamp_ms=timestamp_ms,
                )
            )
    return frames


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
    fieldnames = ["image_path", "timestamp_ms", "screen_type"]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        for frame in frames:
            writer.writerow(
                {
                    "image_path": frame.row["organized_file"],
                    "timestamp_ms": frame.timestamp_ms,
                    "screen_type": frame.row.get("screen_type", ""),
                }
            )


def save_primary_rois(image: Image.Image, output_dir: Path, stem: str) -> None:
    target_dir = output_dir / "rois" / stem
    target_dir.mkdir(parents=True, exist_ok=True)
    for name in PRIMARY_ROIS:
        crop_roi(image, ROI_DEFINITIONS[name]).save(target_dir / f"{name}.png")


def normalize_digits(value: str) -> str:
    return "".join(re.findall(r"\d", value))


def expected_score_from_row(row: dict[str, str]) -> str:
    for key in ("score", "expected_score"):
        normalized = normalize_digits(row.get(key, ""))
        if normalized:
            return normalized

    match = re.search(r"score(\d+)", Path(row["organized_file"]).stem, flags=re.IGNORECASE)
    return match.group(1) if match else ""


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
) -> ScoreOcrResult:
    target_dir = output_dir / "ocr" / Path(row["organized_file"]).stem
    target_dir.mkdir(parents=True, exist_ok=True)

    preprocessed = preprocess_ocr_roi(image, roi_name)
    original_path = target_dir / f"{roi_name}_original.png"
    enlarged_path = target_dir / f"{roi_name}_enlarged.png"
    binary_path = target_dir / f"{roi_name}_binary.png"
    preprocessed.original.save(original_path)
    preprocessed.enlarged.save(enlarged_path)
    preprocessed.binary.save(binary_path)

    raw, engine, status, error = run_tesseract(preprocessed.binary, roi_name)
    normalized = normalize_digits(raw)
    expected = expected_score_from_row(row) if roi_name == "score_digits" else ""
    match = normalized == expected if normalized and expected else None

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
        "--ocr-rois",
        nargs="+",
        default=["score_digits"],
        help=(
            "OCR ROI names to preprocess and attempt. Use 'all' for score_digits and judgment "
            f"ROIs. Default: score_digits. Supported: {', '.join(OCR_ROIS)}"
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


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    output_dir: Path = args.output or (
        {
            "metadata": Path("data/vision_poc"),
            "timestamped": Path("data/vision_poc_timestamped"),
            "manifest": Path("data/vision_poc_manifest"),
        }[args.sequence_mode]
    )
    output_dir.mkdir(parents=True, exist_ok=True)
    ocr_rois = resolve_ocr_rois(args.ocr_rois)

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

    classifications: list[Classification] = []
    score_ocr_results: list[ScoreOcrResult] = []
    for frame in frames:
        with Image.open(frame.image_path) as image:
            image = image.convert("RGB")
            classification = classify(image, frame.row)
            classifications.append(classification)
            if not args.no_rois:
                save_primary_rois(image, output_dir, frame.image_path.stem)
            if not args.no_ocr and classification.result_candidate:
                for roi_name in ocr_rois:
                    score_ocr_results.append(
                        process_ocr_roi(image, frame.row, classification, output_dir, roi_name)
                    )

    write_results_csv(output_dir / "results.csv", classifications)
    timestamps_ms = (
        None if args.sequence_mode == "metadata" else [frame.timestamp_ms for frame in frames]
    )
    result_events = build_result_events(classifications, timestamps_ms=timestamps_ms)
    write_result_events_csv(output_dir / "result_events.csv", result_events)
    write_score_ocr_csv(output_dir / "score_ocr.csv", score_ocr_results)
    summary = summarize(classifications)
    (output_dir / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_misclassification_notes(output_dir / "misclassifications.md", classifications)
    print_summary(summary, output_dir)
    return 0 if not summary["false_positives"] and not summary["false_negatives"] else 1
