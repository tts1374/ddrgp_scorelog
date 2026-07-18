from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import math
import os
import shutil
import sqlite3
import subprocess
import tempfile
from collections import Counter
from collections.abc import Callable, Iterable
from contextlib import closing
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

from PIL import Image, ImageFilter, ImageOps

from tools.vision_poc import jacket_reference_catalog, master_match

DATASET_SCHEMA_VERSION = "m5c-title-artist-evaluation-dataset-v1"
REPORT_SCHEMA_VERSION = "m5c-title-artist-evaluation-report-v1"
RECEIPT_SCHEMA_VERSION = "m5c-title-artist-evaluation-receipt-v1"
TITLE_ARTIST_ROI_VERSION = "m5c-song-select-title-artist-roi-v2"
BASE_SIZE = (1280, 720)
FIELD_ROIS = {
    "title": (306, 58, 470, 34),
    "artist": (309, 97, 467, 23),
}
METHOD_VERSIONS = (
    "tesseract-autocontrast-v1",
    "tesseract-white-threshold-v1",
)
MINIMUM_EVALUATED_ARTIFACTS = 30
MINIMUM_PAIR_EXACT_RATE = 0.95
MINIMUM_FIELD_CONFIDENCE = 0.90

DATASET_KEYS = {"dataset_schema_version", "entries"}
ENTRY_KEYS = {
    "observation_manifest",
    "expected_title",
    "expected_artist",
    "expected_song_id",
}
MANIFEST_KEYS = {
    "manifest_version",
    "session_id",
    "observation_id",
    "source_image",
    "jacket_crop",
    "source_image_hash",
    "jacket_crop_hash",
    "source_width",
    "source_height",
    "source_sequence",
    "captured_at_utc",
    "feature_version",
    "roi_version",
    "master_version",
    "master_source_hash",
    "catalog_identity",
    "catalog_schema_version",
    "catalog_created_at",
    "feature_extractor_version",
    "detector_version",
    "frame_clock_version",
    "window",
    "change_threshold",
    "stable_frame_count_required",
    "minimum_stable_duration_milliseconds",
    "roi_x",
    "roi_y",
    "roi_width",
    "roi_height",
    "feature_hash",
    "mean_absolute_difference",
    "sample_width",
    "sample_height",
    "observed_title",
    "observed_artist",
    "observation_status",
    "created_at_utc",
}
COMPOSITE_MANIFEST_KEYS = MANIFEST_KEYS | {
    "title_line_feature_version",
    "title_line_hash",
    "title_line_detector_version",
    "title_line_roi_version",
    "title_line_source_sequence",
    "title_line_captured_at_utc",
    "composite_identity_version",
    "composite_identity_hash",
}
WINDOW_KEYS = {
    "handle",
    "process_id",
    "process_start_ticks",
    "process_name",
    "title",
    "class_name",
    "client_width",
    "client_height",
    "is_visible",
    "is_minimized",
}
CSV_FIELDS = [
    "method_version",
    "observation_manifest",
    "session_id",
    "observation_id",
    "coverage_status",
    "expected_title",
    "expected_artist",
    "expected_song_id",
    "title_raw",
    "title_normalized",
    "title_confidence",
    "title_status",
    "title_failure_reason",
    "artist_raw",
    "artist_normalized",
    "artist_confidence",
    "artist_status",
    "artist_failure_reason",
    "candidate_status",
    "candidate_song_id",
    "candidate_song_title",
    "candidate_song_artist",
    "candidate_reason",
    "candidate_song_ids",
    "pair_exact",
    "known_false_auto_confirm",
    "auto_confirm_eligible",
]


@dataclass(frozen=True)
class CatalogIdentity:
    identity: str
    schema_version: int
    created_at: str


@dataclass(frozen=True)
class DatasetEntry:
    observation_manifest: str
    expected_title: str | None
    expected_artist: str | None
    expected_song_id: str | None


@dataclass(frozen=True)
class ArtifactInput:
    entry: DatasetEntry
    manifest: dict[str, Any]
    source_bytes: bytes


@dataclass(frozen=True)
class FieldExtraction:
    raw: str
    normalized: str
    confidence: float | None
    status: str
    failure_reason: str


Extractor = Callable[[Image.Image, str, str], FieldExtraction]


def _sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _is_sha256(value: Any) -> bool:
    return (
        isinstance(value, str)
        and len(value) == 64
        and all(character in "0123456789abcdef" for character in value)
    )


def _timestamp(value: Any, label: str) -> datetime:
    if not isinstance(value, str):
        raise ValueError(f"observation manifest {label} is invalid")
    try:
        parsed = datetime.fromisoformat(value)
    except ValueError as exc:
        raise ValueError(f"observation manifest {label} is invalid") from exc
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise ValueError(f"observation manifest {label} must include a timezone")
    return parsed


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _read_strict_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid JSON: {path}") from exc


def _require_exact_keys(value: dict[str, Any], expected: set[str], label: str) -> None:
    actual = set(value)
    if actual != expected:
        missing = sorted(expected - actual)
        unknown = sorted(actual - expected)
        raise ValueError(f"{label} fields mismatch; missing={missing}, unknown={unknown}")


def _inside(root: Path, relative: str, *, label: str) -> Path:
    if not relative or Path(relative).is_absolute():
        raise ValueError(f"{label} must be a non-empty relative path")
    root = root.resolve()
    candidate = (root / relative).resolve()
    try:
        candidate.relative_to(root)
    except ValueError as exc:
        raise ValueError(f"{label} escapes artifact root") from exc
    return candidate


def _require_under_data(path: Path, label: str) -> Path:
    candidate = path.resolve()
    data_root = (Path.cwd() / "data").resolve()
    try:
        candidate.relative_to(data_root)
    except ValueError as exc:
        raise ValueError(f"{label} must be under the repository data directory") from exc
    return candidate


def load_dataset(dataset_path: Path) -> list[DatasetEntry]:
    document = _read_strict_json(dataset_path)
    if not isinstance(document, dict):
        raise ValueError("evaluation dataset must be an object")
    _require_exact_keys(document, DATASET_KEYS, "evaluation dataset")
    if document["dataset_schema_version"] != DATASET_SCHEMA_VERSION:
        raise ValueError("evaluation dataset schema version is unsupported")
    entries = document["entries"]
    if not isinstance(entries, list) or not entries:
        raise ValueError("evaluation dataset entries must be a non-empty array")
    loaded: list[DatasetEntry] = []
    seen: set[str] = set()
    for index, value in enumerate(entries):
        if not isinstance(value, dict):
            raise ValueError(f"evaluation dataset entry {index} must be an object")
        _require_exact_keys(value, ENTRY_KEYS, f"evaluation dataset entry {index}")
        manifest = value["observation_manifest"]
        if not isinstance(manifest, str) or not manifest:
            raise ValueError(f"evaluation dataset entry {index} manifest is invalid")
        if manifest in seen:
            raise ValueError("evaluation dataset contains a duplicate observation manifest")
        seen.add(manifest)
        expected_values: list[str | None] = []
        for field in ("expected_title", "expected_artist", "expected_song_id"):
            item = value[field]
            if item is not None and (not isinstance(item, str) or not item.strip()):
                raise ValueError(f"evaluation dataset entry {index} {field} is invalid")
            expected_values.append(None if item is None else item.strip())
        if expected_values[2] is not None and (
            expected_values[0] is None or expected_values[1] is None
        ):
            raise ValueError(
                f"evaluation dataset entry {index} expected_song_id requires title and artist"
            )
        loaded.append(DatasetEntry(manifest, *expected_values))
    return loaded


def load_catalog_identity(path: Path) -> CatalogIdentity:
    if not path.is_file():
        raise ValueError(f"catalog DB is not a file: {path}")
    schema_version = jacket_reference_catalog.catalog_schema_version(path)
    if schema_version != jacket_reference_catalog.CATALOG_SCHEMA_VERSION:
        raise ValueError("title/artist evaluation requires the current catalog schema")
    try:
        uri = f"file:{path.resolve().as_posix()}?mode=ro"
        with closing(sqlite3.connect(uri, uri=True)) as connection:
            metadata = dict(connection.execute("SELECT key, value FROM catalog_metadata"))
    except sqlite3.DatabaseError as exc:
        raise ValueError(f"invalid jacket catalog database: {path}") from exc
    identity = metadata.get("catalog_identity", "")
    created_at = metadata.get("created_at", "")
    if identity != jacket_reference_catalog.CATALOG_IDENTITY or not created_at:
        raise ValueError("jacket catalog identity metadata is invalid")
    return CatalogIdentity(identity, schema_version, created_at)


def _validate_manifest(
    value: Any,
    *,
    entry: DatasetEntry,
    artifact_root: Path,
    master: jacket_reference_catalog.MasterIdentity,
    catalog: CatalogIdentity,
) -> ArtifactInput:
    if not isinstance(value, dict):
        raise ValueError("observation manifest must be an object")
    manifest_version = value.get("manifest_version")
    if manifest_version == "m5c-observation-manifest-v1":
        _require_exact_keys(value, MANIFEST_KEYS, "observation manifest")
    elif manifest_version == "m5c-observation-manifest-v2":
        _require_exact_keys(value, COMPOSITE_MANIFEST_KEYS, "observation manifest")
    else:
        raise ValueError("observation manifest version is unsupported")
    window = value["window"]
    if not isinstance(window, dict):
        raise ValueError("observation manifest window must be an object")
    _require_exact_keys(window, WINDOW_KEYS, "observation manifest window")
    manifest_path = _inside(artifact_root, entry.observation_manifest, label="observation manifest")
    if (
        manifest_path.name != "observation.json"
        or manifest_path.parent.parent.name != "observations"
    ):
        raise ValueError("observation manifest path shape is invalid")
    manifest_dir = manifest_path.parent
    if (
        not isinstance(value["source_image"], str)
        or not isinstance(value["jacket_crop"], str)
        or not isinstance(value["source_image_hash"], str)
        or not isinstance(value["jacket_crop_hash"], str)
        or not isinstance(value["source_width"], int)
        or isinstance(value["source_width"], bool)
        or not isinstance(value["source_height"], int)
        or isinstance(value["source_height"], bool)
    ):
        raise ValueError("observation manifest artifact fields are invalid")
    source_path = _inside(manifest_dir, value["source_image"], label="source image")
    crop_path = _inside(manifest_dir, value["jacket_crop"], label="jacket crop")
    observation_id = value["observation_id"]
    captured_at = _timestamp(value["captured_at_utc"], "captured_at_utc")
    created_at = _timestamp(value["created_at_utc"], "created_at_utc")
    roi_x, roi_y, roi_width, roi_height = jacket_reference_catalog.SONG_SELECT_JACKET_ROI
    expected_roi = (
        round(roi_x * value["source_width"] / BASE_SIZE[0]),
        round(roi_y * value["source_height"] / BASE_SIZE[1]),
        round(roi_width * value["source_width"] / BASE_SIZE[0]),
        round(roi_height * value["source_height"] / BASE_SIZE[1]),
    )
    if (
        not _is_sha256(observation_id)
        or manifest_dir.name != observation_id
        or value["source_image"] != "source.png"
        or value["jacket_crop"] != "jacket-crop.png"
        or value["observed_title"] != ""
        or value["observed_artist"] != ""
        or value["observation_status"] != "unresolved"
        or value["source_width"] <= 0
        or value["source_height"] <= 0
        or not isinstance(value["session_id"], str)
        or not value["session_id"].strip()
        or not isinstance(value["source_sequence"], int)
        or isinstance(value["source_sequence"], bool)
        or value["source_sequence"] < 0
        or value["feature_version"] != "m5c-jacket-rgb-grid-v1"
        or value["roi_version"]
        != jacket_reference_catalog.SONG_SELECT_JACKET_ROI_VERSION
        or value["detector_version"] != "m5c-3b-jacket-detector-v1"
        or value["frame_clock_version"] != "m5c-capture-utc-clock-v1"
        or not _is_sha256(value["source_image_hash"])
        or not _is_sha256(value["jacket_crop_hash"])
        or not _is_sha256(value["feature_hash"])
        or tuple(value[field] for field in ("roi_x", "roi_y", "roi_width", "roi_height"))
        != expected_roi
        or value["sample_width"] != 16
        or value["sample_height"] != 16
        or not isinstance(value["change_threshold"], (int, float))
        or isinstance(value["change_threshold"], bool)
        or not math.isfinite(value["change_threshold"])
        or not 0 <= value["change_threshold"] <= 1
        or not isinstance(value["stable_frame_count_required"], int)
        or isinstance(value["stable_frame_count_required"], bool)
        or value["stable_frame_count_required"] < 2
        or not isinstance(value["minimum_stable_duration_milliseconds"], int)
        or isinstance(value["minimum_stable_duration_milliseconds"], bool)
        or value["minimum_stable_duration_milliseconds"] < 0
        or not isinstance(value["mean_absolute_difference"], (int, float))
        or isinstance(value["mean_absolute_difference"], bool)
        or not math.isfinite(value["mean_absolute_difference"])
        or created_at < captured_at
    ):
        raise ValueError("observation manifest immutable identity is invalid")
    if manifest_version == "m5c-observation-manifest-v2":
        composite_string_fields = (
            "title_line_feature_version",
            "title_line_hash",
            "title_line_detector_version",
            "title_line_roi_version",
            "composite_identity_version",
            "composite_identity_hash",
        )
        if any(not isinstance(value[field], str) for field in composite_string_fields):
            raise ValueError("observation manifest composite identity is invalid")
        if not isinstance(value["title_line_source_sequence"], int) or isinstance(
            value["title_line_source_sequence"], bool
        ):
            raise ValueError("observation manifest composite identity is invalid")
        title_line_captured_at = _timestamp(
            value["title_line_captured_at_utc"], "title_line_captured_at_utc"
        )
        canonical = "\0".join(
            (
                jacket_reference_catalog.COMPOSITE_IDENTITY_VERSION,
                value["feature_version"],
                value["feature_hash"],
                value["title_line_feature_version"],
                value["title_line_hash"],
            )
        ).encode("utf-8")
        if (
            value["title_line_feature_version"]
            != "m5c-information-title-line-binary-sha256-v1"
            or value["title_line_detector_version"]
            != "m5c-information-title-line-detector-v1"
            or value["title_line_roi_version"]
            != "m5c-song-select-information-panel-roi-v1"
            or value["composite_identity_version"]
            != jacket_reference_catalog.COMPOSITE_IDENTITY_VERSION
            or not _is_sha256(value["title_line_hash"])
            or not _is_sha256(value["composite_identity_hash"])
            or value["title_line_source_sequence"] != value["source_sequence"]
            or title_line_captured_at != captured_at
            or hashlib.sha256(canonical).hexdigest() != value["composite_identity_hash"]
        ):
            raise ValueError("observation manifest composite identity is invalid")
    required_window_strings = ("handle", "process_name")
    integer_window_fields = (
        "process_id",
        "process_start_ticks",
        "client_width",
        "client_height",
    )
    if (
        any(
            not isinstance(window[field], str) or not window[field]
            for field in required_window_strings
        )
        or not isinstance(window["title"], str)
        or not isinstance(window["class_name"], str)
        or any(
            not isinstance(window[field], int)
            or isinstance(window[field], bool)
            or window[field] <= 0
            for field in integer_window_fields
        )
        or not isinstance(window["is_visible"], bool)
        or not isinstance(window["is_minimized"], bool)
        or window["client_width"] != value["source_width"]
        or window["client_height"] != value["source_height"]
    ):
        raise ValueError("observation manifest window identity is invalid")
    if (
        value["master_version"] != master.version
        or value["master_source_hash"] != master.source_hash
        or value["catalog_identity"] != catalog.identity
        or value["catalog_schema_version"] != catalog.schema_version
        or value["catalog_created_at"] != catalog.created_at
        or value["feature_extractor_version"] != jacket_reference_catalog.FEATURE_EXTRACTOR_VERSION
    ):
        raise ValueError("observation manifest reference identity drift detected")
    if not source_path.is_file() or not crop_path.is_file():
        raise ValueError("observation artifact image is missing")
    source_bytes = source_path.read_bytes()
    crop_bytes = crop_path.read_bytes()
    if (
        _sha256_bytes(source_bytes) != value["source_image_hash"]
        or _sha256_bytes(crop_bytes) != value["jacket_crop_hash"]
    ):
        raise ValueError("observation artifact image hash mismatch")
    try:
        with Image.open(io.BytesIO(source_bytes)) as image:
            image.load()
            if image.size != (value["source_width"], value["source_height"]):
                raise ValueError("observation source dimensions mismatch")
        with Image.open(io.BytesIO(crop_bytes)) as image:
            image.load()
    except OSError as exc:
        raise ValueError("observation artifact image is corrupt") from exc
    return ArtifactInput(entry, value, source_bytes)


def load_artifacts(
    entries: Iterable[DatasetEntry],
    *,
    artifact_root: Path,
    master: jacket_reference_catalog.MasterIdentity,
    catalog: CatalogIdentity,
) -> list[ArtifactInput]:
    artifacts = []
    seen_ids: set[str] = set()
    for entry in entries:
        manifest_path = _inside(
            artifact_root, entry.observation_manifest, label="observation manifest"
        )
        artifact = _validate_manifest(
            _read_strict_json(manifest_path),
            entry=entry,
            artifact_root=artifact_root,
            master=master,
            catalog=catalog,
        )
        observation_id = artifact.manifest["observation_id"]
        if observation_id in seen_ids:
            raise ValueError("evaluation dataset contains a duplicate observation id")
        seen_ids.add(observation_id)
        artifacts.append(artifact)
    return artifacts


def _scaled_roi(image: Image.Image, field: str) -> tuple[int, int, int, int]:
    x, y, width, height = FIELD_ROIS[field]
    sx = image.width / BASE_SIZE[0]
    sy = image.height / BASE_SIZE[1]
    box = (
        round(x * sx),
        round(y * sy),
        round((x + width) * sx),
        round((y + height) * sy),
    )
    if box[0] < 0 or box[1] < 0 or box[2] > image.width or box[3] > image.height:
        raise ValueError("scaled title/artist ROI is outside source image")
    return box


def _preprocess(image: Image.Image, field: str, method: str) -> Image.Image:
    roi = image.crop(_scaled_roi(image, field)).convert("RGB")
    scale = 4 if field == "title" else 5
    gray = ImageOps.autocontrast(roi.convert("L")).resize(
        (roi.width * scale, roi.height * scale), Image.Resampling.LANCZOS
    )
    gray = gray.filter(ImageFilter.SHARPEN)
    if method == "tesseract-autocontrast-v1":
        prepared = gray
    elif method == "tesseract-white-threshold-v1":
        prepared = gray.point(lambda value: 0 if value >= 150 else 255, mode="1").convert("L")
    else:
        raise ValueError(f"unsupported title/artist method: {method}")
    return ImageOps.expand(prepared, border=24, fill=255)


def _parse_tesseract_tsv(stdout: str) -> tuple[str, float | None]:
    try:
        rows = list(csv.DictReader(io.StringIO(stdout), delimiter="\t"))
    except csv.Error as exc:
        raise ValueError("tesseract returned invalid TSV") from exc
    if not rows or not {"text", "conf"}.issubset(rows[0]):
        raise ValueError("tesseract returned incomplete TSV")
    words: list[str] = []
    confidences: list[float] = []
    for row in rows:
        word = (row.get("text") or "").strip()
        if not word:
            continue
        try:
            confidence = float(row.get("conf") or "-1")
        except ValueError as exc:
            raise ValueError("tesseract returned invalid confidence") from exc
        words.append(word)
        if confidence >= 0:
            confidences.append(confidence / 100.0)
    return " ".join(words), None if not confidences else sum(confidences) / len(confidences)


def extract_field(image: Image.Image, field: str, method: str) -> FieldExtraction:
    tesseract = shutil.which("tesseract")
    if not tesseract:
        return FieldExtraction("", "", None, "engine_unavailable", "tesseract_not_found")
    prepared = _preprocess(image, field, method)
    payload = io.BytesIO()
    prepared.save(payload, format="PNG")
    try:
        completed = subprocess.run(
            [tesseract, "stdin", "stdout", "--psm", "7", "--dpi", "300", "tsv"],
            input=payload.getvalue(),
            capture_output=True,
            check=False,
            timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired):
        return FieldExtraction("", "", None, "ocr_failed", "tesseract_execution_failed")
    if completed.returncode != 0:
        return FieldExtraction("", "", None, "ocr_failed", "tesseract_nonzero_exit")
    try:
        raw, confidence = _parse_tesseract_tsv(completed.stdout.decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, ValueError):
        return FieldExtraction("", "", None, "ocr_failed", "tesseract_output_invalid")
    normalized = master_match.normalize_song_title(raw)
    if not normalized:
        return FieldExtraction(raw, "", confidence, "empty", "empty_ocr")
    if confidence is None:
        return FieldExtraction(raw, normalized, None, "low_confidence", "confidence_unavailable")
    if confidence < MINIMUM_FIELD_CONFIDENCE:
        return FieldExtraction(
            raw, normalized, confidence, "low_confidence", "below_confidence_gate"
        )
    return FieldExtraction(raw, normalized, confidence, "ok", "")


def coverage_status(entry: DatasetEntry) -> str:
    expected_count = sum(
        value is not None for value in (entry.expected_title, entry.expected_artist)
    )
    if expected_count == 2:
        return "evaluated"
    if expected_count == 1:
        return "partially_evaluated"
    return "no_expected_values"


def _expected_song_id(
    entry: DatasetEntry,
    master: jacket_reference_catalog.MasterIdentity,
) -> str | None:
    if entry.expected_title is None or entry.expected_artist is None:
        return None
    resolution = jacket_reference_catalog.resolve_observation(
        master, entry.expected_title, entry.expected_artist
    )
    resolved_song_id = None if resolution.song is None else resolution.song.song_id
    if entry.expected_song_id is not None:
        if entry.expected_song_id not in {song.song_id for song in master.songs}:
            raise ValueError("evaluation dataset expected_song_id is not in current master")
        if resolved_song_id != entry.expected_song_id:
            raise ValueError("evaluation dataset expected values disagree with current master")
        return entry.expected_song_id
    return resolved_song_id


def evaluate(
    artifacts: Iterable[ArtifactInput],
    *,
    master: jacket_reference_catalog.MasterIdentity,
    methods: Iterable[str] = METHOD_VERSIONS,
    extractor: Extractor = extract_field,
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for artifact in artifacts:
        with Image.open(io.BytesIO(artifact.source_bytes)) as opened:
            image = opened.convert("RGB")
        expected_song_id = _expected_song_id(artifact.entry, master)
        expected_title_key = master_match.normalize_song_title(artifact.entry.expected_title or "")
        expected_artist_key = master_match.normalize_song_title(
            artifact.entry.expected_artist or ""
        )
        for method in methods:
            if method not in METHOD_VERSIONS:
                raise ValueError(f"unsupported title/artist method: {method}")
            title = extractor(image, "title", method)
            artist = extractor(image, "artist", method)
            observation_ok = title.status == "ok" and artist.status == "ok"
            resolution = jacket_reference_catalog.resolve_observation(
                master,
                title.raw,
                artist.raw,
                observation_status="ok" if observation_ok else "extraction_failed",
            )
            candidate_song_id = None if resolution.song is None else resolution.song.song_id
            pair_exact = (
                None
                if coverage_status(artifact.entry) != "evaluated"
                else observation_ok
                and title.normalized == expected_title_key
                and artist.normalized == expected_artist_key
            )
            known_false = bool(
                resolution.status == "auto_confirmed"
                and expected_song_id is not None
                and candidate_song_id != expected_song_id
            )
            eligible = bool(
                resolution.status == "auto_confirmed"
                and title.status == "ok"
                and artist.status == "ok"
                and not known_false
            )
            rows.append(
                {
                    "method_version": method,
                    "observation_manifest": artifact.entry.observation_manifest,
                    "session_id": artifact.manifest["session_id"],
                    "observation_id": artifact.manifest["observation_id"],
                    "coverage_status": coverage_status(artifact.entry),
                    "expected_title": artifact.entry.expected_title,
                    "expected_artist": artifact.entry.expected_artist,
                    "expected_song_id": expected_song_id,
                    "title_raw": title.raw,
                    "title_normalized": title.normalized,
                    "title_confidence": title.confidence,
                    "title_status": title.status,
                    "title_failure_reason": title.failure_reason,
                    "artist_raw": artist.raw,
                    "artist_normalized": artist.normalized,
                    "artist_confidence": artist.confidence,
                    "artist_status": artist.status,
                    "artist_failure_reason": artist.failure_reason,
                    "candidate_status": resolution.status,
                    "candidate_song_id": candidate_song_id,
                    "candidate_song_title": None
                    if resolution.song is None
                    else resolution.song.title,
                    "candidate_song_artist": None
                    if resolution.song is None
                    else resolution.song.artist,
                    "candidate_reason": resolution.reason,
                    "candidate_song_ids": list(resolution.candidate_song_ids),
                    "pair_exact": pair_exact,
                    "known_false_auto_confirm": known_false,
                    "auto_confirm_eligible": eligible,
                }
            )
    return sorted(rows, key=lambda row: (row["method_version"], row["observation_id"]))


def summarize(rows: Iterable[dict[str, Any]]) -> dict[str, Any]:
    rows = list(rows)
    methods: list[dict[str, Any]] = []
    adopted_methods: list[str] = []
    for method in METHOD_VERSIONS:
        method_rows = [row for row in rows if row["method_version"] == method]
        evaluated = [row for row in method_rows if row["coverage_status"] == "evaluated"]
        pair_exact_count = sum(row["pair_exact"] is True for row in evaluated)
        auto_candidates = [row for row in evaluated if row["candidate_status"] == "auto_confirmed"]
        known_false = sum(row["known_false_auto_confirm"] for row in evaluated)
        correct_auto = sum(
            row["candidate_status"] == "auto_confirmed"
            and row["expected_song_id"] is not None
            and row["candidate_song_id"] == row["expected_song_id"]
            for row in evaluated
        )
        pair_rate = None if not evaluated else pair_exact_count / len(evaluated)
        precision = None if not auto_candidates else correct_auto / len(auto_candidates)
        adopted = bool(
            len(evaluated) >= MINIMUM_EVALUATED_ARTIFACTS
            and pair_rate is not None
            and pair_rate >= MINIMUM_PAIR_EXACT_RATE
            and known_false == 0
            and auto_candidates
            and precision == 1.0
        )
        if adopted:
            adopted_methods.append(method)
        methods.append(
            {
                "method_version": method,
                "artifact_count": len(method_rows),
                "coverage_status_counts": dict(
                    sorted(Counter(row["coverage_status"] for row in method_rows).items())
                ),
                "field_status_counts": {
                    field: dict(
                        sorted(Counter(row[f"{field}_status"] for row in method_rows).items())
                    )
                    for field in ("title", "artist")
                },
                "candidate_status_counts": dict(
                    sorted(Counter(row["candidate_status"] for row in method_rows).items())
                ),
                "evaluated_count": len(evaluated),
                "pair_exact_count": pair_exact_count,
                "pair_exact_rate": pair_rate,
                "auto_confirm_candidate_count": len(auto_candidates),
                "auto_confirm_precision": precision,
                "known_false_auto_confirm_count": known_false,
                "adoption_status": "adopted" if adopted else "not_adopted",
                "adoption_failure_reasons": [
                    reason
                    for failed, reason in (
                        (
                            len(evaluated) < MINIMUM_EVALUATED_ARTIFACTS,
                            "insufficient_evaluated_artifacts",
                        ),
                        (
                            pair_rate is None or pair_rate < MINIMUM_PAIR_EXACT_RATE,
                            "pair_exact_rate_below_gate",
                        ),
                        (known_false != 0, "known_false_auto_confirm_detected"),
                        (not auto_candidates, "no_auto_confirm_candidates"),
                        (precision != 1.0, "auto_confirm_precision_below_gate"),
                    )
                    if failed
                ],
            }
        )
    return {
        "report_schema_version": REPORT_SCHEMA_VERSION,
        "roi_version": TITLE_ARTIST_ROI_VERSION,
        "adoption_policy": {
            "minimum_evaluated_artifacts": MINIMUM_EVALUATED_ARTIFACTS,
            "minimum_pair_exact_rate": MINIMUM_PAIR_EXACT_RATE,
            "minimum_field_confidence": MINIMUM_FIELD_CONFIDENCE,
            "required_known_false_auto_confirm_count": 0,
            "required_auto_confirm_precision": 1.0,
            "fixture_gate": "required_by_repository_tests",
        },
        "adopted_methods": adopted_methods,
        "methods": methods,
    }


def _csv_value(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, float):
        return f"{value:.6f}"
    if isinstance(value, list):
        return "|".join(str(item) for item in value)
    return str(value)


def render_csv(rows: Iterable[dict[str, Any]]) -> str:
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=CSV_FIELDS, lineterminator="\n")
    writer.writeheader()
    for row in rows:
        writer.writerow({field: _csv_value(row[field]) for field in CSV_FIELDS})
    return output.getvalue()


def render_markdown(summary: dict[str, Any]) -> str:
    policy = summary["adoption_policy"]
    lines = [
        "# M5c title/artist evaluation",
        "",
        f"- ROI version: `{summary['roi_version']}`",
        f"- adopted methods: `{', '.join(summary['adopted_methods']) or 'none'}`",
        "- adoption gate: "
        f"evaluated >= {policy['minimum_evaluated_artifacts']}, "
        f"pair exact >= {policy['minimum_pair_exact_rate']:.0%}, "
        "known false auto-confirm = 0, auto-confirm precision = 100%, "
        f"field confidence >= {policy['minimum_field_confidence']:.2f}",
        "",
        "| method | coverage | pair exact | candidates | known false | precision | adoption |",
        "| --- | --- | ---: | ---: | ---: | ---: | --- |",
    ]
    for method in summary["methods"]:
        coverage = json.dumps(method["coverage_status_counts"], ensure_ascii=False, sort_keys=True)
        pair_rate = "—" if method["pair_exact_rate"] is None else f"{method['pair_exact_rate']:.2%}"
        precision = (
            "—"
            if method["auto_confirm_precision"] is None
            else f"{method['auto_confirm_precision']:.2%}"
        )
        lines.append(
            f"| `{method['method_version']}` | `{coverage}` | "
            f"{method['pair_exact_count']}/{method['evaluated_count']} ({pair_rate}) | "
            f"{method['auto_confirm_candidate_count']} | "
            f"{method['known_false_auto_confirm_count']} | {precision} | "
            f"`{method['adoption_status']}` |"
        )
        if method["adoption_failure_reasons"]:
            lines.append(
                f"|  | failure reasons | `{' / '.join(method['adoption_failure_reasons'])}` | "
                " |  |  |  |"
            )
    lines.extend(
        [
            "",
            "`partially_evaluated` and `no_expected_values` are excluded from accuracy gates. ",
            "Raw/normalized values, confidence, candidate status, and failure reasons are in "
            "the CSV/JSON rows.",
            "Evaluation is read-only for catalog/manual-review state and does not "
            "auto-confirm observations.",
            "",
        ]
    )
    return "\n".join(lines)


def write_reports(output_dir: Path, rows: list[dict[str, Any]], summary: dict[str, Any]) -> None:
    output_dir = output_dir.resolve()
    output_dir.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".title-artist-evaluation-", dir=output_dir.parent))
    try:
        (temporary / "title_artist_evaluation.csv").write_text(
            render_csv(rows), encoding="utf-8", newline="\n"
        )
        report = {**summary, "rows": rows}
        (temporary / "title_artist_evaluation.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        (temporary / "title_artist_evaluation.md").write_text(
            render_markdown(summary), encoding="utf-8", newline="\n"
        )
        output_dir.mkdir(parents=True, exist_ok=True)
        for name in (
            "title_artist_evaluation.csv",
            "title_artist_evaluation.json",
            "title_artist_evaluation.md",
        ):
            os.replace(temporary / name, output_dir / name)
    finally:
        shutil.rmtree(temporary, ignore_errors=True)


def run_evaluation(
    *,
    dataset_path: Path,
    artifact_root: Path,
    master_db: Path,
    catalog_db: Path,
    output_dir: Path,
    extractor: Extractor = extract_field,
) -> dict[str, Any]:
    dataset_path = _require_under_data(dataset_path, "evaluation dataset")
    artifact_root = _require_under_data(artifact_root, "artifact root")
    output_dir = _require_under_data(output_dir, "evaluation output")
    before = {"master": _sha256_file(master_db), "catalog": _sha256_file(catalog_db)}
    master = jacket_reference_catalog.load_master_identity(master_db)
    catalog = load_catalog_identity(catalog_db)
    entries = load_dataset(dataset_path)
    artifacts = load_artifacts(entries, artifact_root=artifact_root, master=master, catalog=catalog)
    rows = evaluate(artifacts, master=master, extractor=extractor)
    summary = summarize(rows)
    after = {"master": _sha256_file(master_db), "catalog": _sha256_file(catalog_db)}
    if before != after:
        raise ValueError("master/catalog drift detected during title/artist evaluation")
    write_reports(output_dir, rows, summary)
    return {
        "receipt_schema_version": RECEIPT_SCHEMA_VERSION,
        "report_directory": str(output_dir.resolve()),
        "adopted_methods": summary["adopted_methods"],
        "methods": [
            {
                "method_version": method["method_version"],
                "evaluated_count": method["evaluated_count"],
                "known_false_auto_confirm_count": method["known_false_auto_confirm_count"],
                "adoption_status": method["adoption_status"],
                "adoption_failure_reasons": method["adoption_failure_reasons"],
            }
            for method in summary["methods"]
        ],
    }


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Evaluate M5c title/artist extraction methods")
    parser.add_argument("--dataset", type=Path, required=True)
    parser.add_argument("--artifact-root", type=Path, required=True)
    parser.add_argument("--master-db", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        receipt = run_evaluation(
            dataset_path=args.dataset,
            artifact_root=args.artifact_root,
            master_db=args.master_db,
            catalog_db=args.catalog,
            output_dir=args.output_dir,
        )
    except (OSError, ValueError) as exc:
        raise SystemExit(str(exc)) from exc
    print(json.dumps(receipt, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
