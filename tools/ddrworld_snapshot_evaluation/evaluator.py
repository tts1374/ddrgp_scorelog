from __future__ import annotations

import csv
import hashlib
import json
import sqlite3
import unicodedata
import xml.etree.ElementTree as ET
import zipfile
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, UnidentifiedImageError

from tools.vision_poc import master_match

EVALUATOR_VERSION = "ddrworld-snapshot-jacket-evaluation-v1"
SUMMARY_SCHEMA = "ddrworld-snapshot-jacket-evaluation-summary-v1"
MANIFEST_SCHEMA = "ddrworld-music-snapshot-manifest-v1"
SNAPSHOT_SUMMARY_SCHEMA = "ddrworld-music-snapshot-summary-v1"
CATALOG_IDENTITY = "ddrgp-local-jacket-reference-catalog"
CATALOG_SCHEMA_VERSION = "1"
FEATURE_EXTRACTOR_VERSION = "m5-jacket-v2"
JACKET_FEATURE_VERSION = "m5c-jacket-rgb-grid-v1"
IMAGE_KIND = "jacket_crop"
TOP_K_VALUES = (1, 3, 5, 10)
MAX_ODS_DATA_ROWS = 10_000
MAX_ODS_DATA_COLUMNS = 256

NS = {
    "office": "urn:oasis:names:tc:opendocument:xmlns:office:1.0",
    "table": "urn:oasis:names:tc:opendocument:xmlns:table:1.0",
    "text": "urn:oasis:names:tc:opendocument:xmlns:text:1.0",
}


class EvaluationError(RuntimeError):
    """Raised when evaluation inputs are unsafe or inconsistent."""


@dataclass(frozen=True)
class EvaluationConfig:
    snapshot: Path
    truth_ods: Path
    catalog: Path
    master: Path
    output: Path


@dataclass(frozen=True)
class MasterSong:
    song_id: str
    title: str
    artist: str
    grand_prix_play_available: bool


@dataclass(frozen=True)
class SnapshotSong:
    source_page: int
    page_position: int
    title: str
    artist: str
    jacket_sha256: str
    jacket_path: Path
    feature: master_match.JacketFeature


@dataclass(frozen=True)
class TruthObservation:
    audit_no: int
    observation_id: str
    review_status: str
    truth_song_id: str
    truth_title: str
    truth_artist: str


@dataclass(frozen=True)
class MappingResult:
    song: MasterSong | None
    status: str
    reason: str


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
    except OSError as exc:
        raise EvaluationError(f"cannot read input file {path}: {exc}") from exc
    return digest.hexdigest()


def read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise EvaluationError(f"invalid JSON input {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise EvaluationError(f"JSON input must be an object: {path}")
    return value


def normalize(value: str) -> str:
    return master_match.normalize_song_title(unicodedata.normalize("NFKC", value))


def _ods_attr(namespace: str, name: str) -> str:
    return f"{{{NS[namespace]}}}{name}"


def _ods_cell_value(cell: ET.Element) -> Any:
    value_type = cell.get(_ods_attr("office", "value-type"))
    if value_type == "float":
        raw = cell.get(_ods_attr("office", "value"))
        if raw is None:
            return None
        value = float(raw)
        return int(value) if value.is_integer() else value
    if value_type == "boolean":
        return cell.get(_ods_attr("office", "boolean-value")) == "true"
    paragraphs = ["".join(item.itertext()) for item in cell.findall("text:p", NS)]
    if paragraphs:
        return "\n".join(paragraphs)
    return cell.get(_ods_attr("office", "string-value"))


def load_ods_sheets(path: Path) -> dict[str, list[list[Any]]]:
    try:
        with zipfile.ZipFile(path) as archive:
            root = ET.fromstring(archive.read("content.xml"))
    except (OSError, KeyError, zipfile.BadZipFile, ET.ParseError) as exc:
        raise EvaluationError(f"invalid ODS input {path}: {exc}") from exc

    sheets: dict[str, list[list[Any]]] = {}
    for table in root.findall(".//table:table", NS):
        name = table.get(_ods_attr("table", "name")) or ""
        rows: list[list[Any]] = []
        for row in table.findall("table:table-row", NS):
            try:
                row_repeat = int(
                    row.get(_ods_attr("table", "number-rows-repeated"), "1")
                )
            except ValueError as exc:
                raise EvaluationError(f"ODS sheet has an invalid row repeat: {name}") from exc
            if row_repeat < 1:
                raise EvaluationError(f"ODS sheet has a non-positive row repeat: {name}")
            values: list[Any] = []
            pending_blanks = 0
            for cell in list(row):
                if cell.tag not in {
                    f"{{{NS['table']}}}table-cell",
                    f"{{{NS['table']}}}covered-table-cell",
                }:
                    continue
                try:
                    repeat = int(
                        cell.get(_ods_attr("table", "number-columns-repeated"), "1")
                    )
                except ValueError as exc:
                    raise EvaluationError(
                        f"ODS sheet has an invalid column repeat: {name}"
                    ) from exc
                if repeat < 1:
                    raise EvaluationError(
                        f"ODS sheet has a non-positive column repeat: {name}"
                    )
                value = _ods_cell_value(cell)
                if value is None or value == "":
                    pending_blanks += repeat
                    continue
                expanded_columns = len(values) + pending_blanks + repeat
                if expanded_columns > MAX_ODS_DATA_COLUMNS:
                    raise EvaluationError(
                        f"ODS sheet exceeds {MAX_ODS_DATA_COLUMNS} data columns: {name}"
                    )
                values.extend([None] * pending_blanks)
                values.extend([value] * repeat)
                pending_blanks = 0
            if not values:
                continue
            if len(rows) + row_repeat > MAX_ODS_DATA_ROWS:
                raise EvaluationError(
                    f"ODS sheet exceeds {MAX_ODS_DATA_ROWS} non-empty rows: {name}"
                )
            rows.extend([list(values) for _ in range(row_repeat)])
        sheets[name] = rows
    return sheets


def rows_as_dicts(rows: list[list[Any]], *, sheet_name: str) -> list[dict[str, Any]]:
    if not rows:
        raise EvaluationError(f"ODS sheet is empty: {sheet_name}")
    headers = [str(value) for value in rows[0]]
    if not all(headers):
        raise EvaluationError(f"ODS sheet has a blank header: {sheet_name}")
    return [
        dict(zip(headers, row + [None] * (len(headers) - len(row)), strict=True))
        for row in rows[1:]
    ]


def load_truth(path: Path, master_by_id: dict[str, MasterSong]) -> list[TruthObservation]:
    sheets = load_ods_sheets(path)
    if "Review" not in sheets:
        raise EvaluationError("truth ODS is missing the Review sheet")
    rows = rows_as_dicts(sheets["Review"], sheet_name="Review")
    required = {
        "audit_no",
        "review_status",
        "truth_song_id",
        "truth_title",
        "truth_artist",
        "observation_id",
    }
    missing = sorted(required - set(rows[0] if rows else []))
    if missing:
        raise EvaluationError(f"truth ODS Review sheet is missing columns: {', '.join(missing)}")

    observations: list[TruthObservation] = []
    seen_audit: set[int] = set()
    seen_observations: set[str] = set()
    for row in rows:
        try:
            audit_no = int(row["audit_no"])
        except (TypeError, ValueError) as exc:
            raise EvaluationError("truth ODS contains an invalid audit_no") from exc
        status = str(row.get("review_status") or "")
        observation_id = str(row.get("observation_id") or "")
        if status not in {"confirmed", "rejected"}:
            raise EvaluationError(f"audit {audit_no} has unsupported review_status: {status}")
        if not observation_id:
            raise EvaluationError(f"audit {audit_no} is missing observation_id")
        if audit_no in seen_audit or observation_id in seen_observations:
            raise EvaluationError(f"truth ODS contains a duplicate audit/observation at {audit_no}")
        seen_audit.add(audit_no)
        seen_observations.add(observation_id)

        truth_song_id = str(row.get("truth_song_id") or "")
        truth_title = str(row.get("truth_title") or "")
        truth_artist = str(row.get("truth_artist") or "")
        if status == "confirmed":
            if not all((truth_song_id, truth_title, truth_artist)):
                raise EvaluationError(f"confirmed audit {audit_no} is missing truth fields")
            master = master_by_id.get(truth_song_id)
            if master is None:
                raise EvaluationError(f"confirmed audit {audit_no} has unknown truth song ID")
            if (truth_title, truth_artist) != (master.title, master.artist):
                raise EvaluationError(
                    f"confirmed audit {audit_no} does not exactly match master truth"
                )
        elif any((truth_song_id, truth_title, truth_artist)):
            raise EvaluationError(f"rejected audit {audit_no} must not contain truth fields")
        observations.append(
            TruthObservation(
                audit_no=audit_no,
                observation_id=observation_id,
                review_status=status,
                truth_song_id=truth_song_id,
                truth_title=truth_title,
                truth_artist=truth_artist,
            )
        )
    return observations


def _read_only_connection(path: Path) -> sqlite3.Connection:
    try:
        connection = sqlite3.connect(
            f"file:{path.resolve().as_posix()}?mode=ro&immutable=1",
            uri=True,
        )
    except sqlite3.Error as exc:
        raise EvaluationError(f"cannot open SQLite input read-only: {path}: {exc}") from exc
    connection.row_factory = sqlite3.Row
    return connection


def load_master(path: Path) -> tuple[dict[str, MasterSong], list[dict[str, str]]]:
    try:
        with _read_only_connection(path) as connection:
            song_rows = connection.execute(
                "SELECT song_id, title, artist, grand_prix_play_available FROM songs"
            ).fetchall()
            alias_rows = connection.execute(
                "SELECT song_id, alias_title, alias_artist FROM song_aliases"
            ).fetchall()
    except sqlite3.Error as exc:
        raise EvaluationError(f"invalid master database {path}: {exc}") from exc
    songs = {
        str(row["song_id"]): MasterSong(
            song_id=str(row["song_id"]),
            title=str(row["title"]),
            artist=str(row["artist"]),
            grand_prix_play_available=bool(row["grand_prix_play_available"]),
        )
        for row in song_rows
    }
    aliases = [
        {
            "song_id": str(row["song_id"]),
            "title": str(row["alias_title"]),
            "artist": str(row["alias_artist"]),
        }
        for row in alias_rows
    ]
    if not songs:
        raise EvaluationError("master database contains no songs")
    return songs, aliases


def build_master_indexes(
    songs: dict[str, MasterSong], aliases: list[dict[str, str]]
) -> dict[str, dict[Any, set[str]]]:
    indexes: dict[str, dict[Any, set[str]]] = {
        "canonical_exact_pair": defaultdict(set),
        "canonical_normalized_pair": defaultdict(set),
        "canonical_exact_title": defaultdict(set),
        "canonical_normalized_title": defaultdict(set),
        "alias_exact_pair": defaultdict(set),
        "alias_normalized_pair": defaultdict(set),
        "alias_exact_title": defaultdict(set),
        "alias_normalized_title": defaultdict(set),
    }
    for song in songs.values():
        indexes["canonical_exact_pair"][(song.title, song.artist)].add(song.song_id)
        indexes["canonical_normalized_pair"][
            (normalize(song.title), normalize(song.artist))
        ].add(song.song_id)
        indexes["canonical_exact_title"][song.title].add(song.song_id)
        indexes["canonical_normalized_title"][normalize(song.title)].add(song.song_id)
    for alias in aliases:
        if alias["song_id"] not in songs:
            raise EvaluationError(f"master alias references unknown song: {alias['song_id']}")
        indexes["alias_exact_pair"][(alias["title"], alias["artist"])].add(alias["song_id"])
        indexes["alias_normalized_pair"][
            (normalize(alias["title"]), normalize(alias["artist"]))
        ].add(alias["song_id"])
        indexes["alias_exact_title"][alias["title"]].add(alias["song_id"])
        indexes["alias_normalized_title"][normalize(alias["title"])].add(alias["song_id"])
    return indexes


def resolve_snapshot_song(
    title: str,
    artist: str,
    songs: dict[str, MasterSong],
    indexes: dict[str, dict[Any, set[str]]],
) -> MappingResult:
    normalized_title = normalize(title)
    normalized_artist = normalize(artist)
    if not normalized_title or not normalized_artist:
        return MappingResult(None, "unresolved", "missing_title_or_artist")
    checks = (
        ("canonical_exact_pair", (title, artist), "canonical_exact"),
        ("alias_exact_pair", (title, artist), "alias_exact"),
        (
            "canonical_normalized_pair",
            (normalized_title, normalized_artist),
            "canonical_notation_difference",
        ),
        (
            "alias_normalized_pair",
            (normalized_title, normalized_artist),
            "alias_notation_difference",
        ),
        (
            "canonical_exact_title",
            title,
            "canonical_unique_title_artist_difference",
        ),
        ("alias_exact_title", title, "alias_unique_title_artist_difference"),
        (
            "canonical_normalized_title",
            normalized_title,
            "canonical_unique_title_notation_or_artist_difference",
        ),
        (
            "alias_normalized_title",
            normalized_title,
            "alias_unique_title_notation_or_artist_difference",
        ),
    )
    for index_name, key, status in checks:
        song_ids = indexes[index_name].get(key, set())
        if len(song_ids) == 1:
            song = songs[next(iter(song_ids))]
            return MappingResult(song, status, "")
        if len(song_ids) > 1:
            return MappingResult(None, "unresolved", f"ambiguous_{index_name}")
    return MappingResult(None, "unresolved", "title_not_found")


def load_snapshot(path: Path) -> tuple[list[dict[str, Any]], dict[str, str]]:
    manifest_path = path / "manifest.json"
    summary_path = path / "summary.json"
    songs_path = path / "songs.jsonl"
    manifest = read_json(manifest_path)
    summary = read_json(summary_path)
    if manifest.get("schema_version") != MANIFEST_SCHEMA or manifest.get("status") != "complete":
        raise EvaluationError("snapshot manifest is not a complete v1 snapshot")
    if (
        summary.get("schema_version") != SNAPSHOT_SUMMARY_SCHEMA
        or summary.get("status") != "complete"
    ):
        raise EvaluationError("snapshot summary is not a complete v1 snapshot")
    if manifest.get("failures"):
        raise EvaluationError("complete snapshot manifest contains failures")
    try:
        lines = songs_path.read_text(encoding="utf-8").splitlines()
        rows = [json.loads(line) for line in lines if line]
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise EvaluationError(f"invalid snapshot songs input: {exc}") from exc
    if len(rows) != summary.get("song_count"):
        raise EvaluationError("snapshot song count does not match summary")
    if not rows or not all(isinstance(row, dict) for row in rows):
        raise EvaluationError("snapshot songs must contain JSON objects")
    raw_image_records = manifest.get("images")
    if not isinstance(raw_image_records, list) or not all(
        isinstance(row, dict) for row in raw_image_records
    ):
        raise EvaluationError("snapshot image manifest must be an object list")
    image_records = {str(row.get("source_url")): row for row in raw_image_records}
    if len(image_records) != len(raw_image_records):
        raise EvaluationError("snapshot image manifest contains duplicate source URLs")
    if len(image_records) != summary.get("image_request_count"):
        raise EvaluationError("snapshot image manifest count does not match summary")
    if summary.get("stored_jacket_count") != len(image_records):
        raise EvaluationError("snapshot stored jacket count does not match manifest")
    song_urls: set[str] = set()
    for index, row in enumerate(rows):
        source_url = str(row.get("jacket_source_url") or "")
        manifest_image = image_records.get(source_url)
        if manifest_image is None:
            raise EvaluationError(f"snapshot song row {index} has no image manifest record")
        if manifest_image.get("error") is not None:
            raise EvaluationError(f"snapshot song row {index} references a failed image")
        if (
            row.get("jacket_sha256") != manifest_image.get("sha256")
            or row.get("jacket_local_path") != manifest_image.get("local_path")
        ):
            raise EvaluationError(f"snapshot song row {index} image hash/path mismatch")
        song_urls.add(source_url)
    if song_urls != set(image_records):
        raise EvaluationError("snapshot contains unreferenced image manifest records")
    fingerprints = {
        "manifest_sha256": sha256_file(manifest_path),
        "summary_sha256": sha256_file(summary_path),
        "songs_sha256": sha256_file(songs_path),
    }
    return rows, fingerprints


def load_snapshot_features(path: Path, rows: list[dict[str, Any]]) -> list[SnapshotSong]:
    loaded_by_path: dict[Path, tuple[str, master_match.JacketFeature]] = {}
    songs: list[SnapshotSong] = []
    for index, row in enumerate(rows):
        required = ("title", "artist", "jacket_local_path", "jacket_sha256")
        if any(not row.get(field) for field in required) or row.get("jacket_error") is not None:
            raise EvaluationError(f"snapshot song row {index} has incomplete jacket metadata")
        relative = Path(str(row["jacket_local_path"]))
        if relative.is_absolute() or ".." in relative.parts:
            raise EvaluationError(f"snapshot song row {index} has unsafe jacket path")
        jacket_path = (path / relative).resolve()
        try:
            jacket_path.relative_to(path.resolve())
        except ValueError as exc:
            raise EvaluationError(f"snapshot song row {index} escapes snapshot root") from exc
        expected_hash = str(row["jacket_sha256"])
        loaded = loaded_by_path.get(jacket_path)
        if loaded is None:
            actual_hash = sha256_file(jacket_path)
            if actual_hash != expected_hash:
                raise EvaluationError(f"snapshot jacket hash mismatch: {relative}")
            try:
                with Image.open(jacket_path) as image:
                    image.load()
                    feature = master_match.extract_jacket_feature(image)
            except (OSError, UnidentifiedImageError) as exc:
                raise EvaluationError(f"invalid snapshot jacket image {relative}: {exc}") from exc
            loaded = (actual_hash, feature)
            loaded_by_path[jacket_path] = loaded
        elif loaded[0] != expected_hash:
            raise EvaluationError(f"shared snapshot jacket path has conflicting hashes: {relative}")
        songs.append(
            SnapshotSong(
                source_page=int(row.get("source_page", -1)),
                page_position=int(row.get("page_position", -1)),
                title=str(row["title"]),
                artist=str(row["artist"]),
                jacket_sha256=expected_hash,
                jacket_path=relative,
                feature=loaded[1],
            )
        )
    return songs


def _feature_from_catalog_row(row: sqlite3.Row) -> master_match.JacketFeature:
    try:
        thumbnail = np.asarray(json.loads(row["thumbnail_rgb_json"]), dtype=np.float32)
        histogram = np.asarray(json.loads(row["histogram_json"]), dtype=np.float32)
        dhash = np.asarray(json.loads(row["dhash_bits_json"]), dtype=np.float32)
    except (TypeError, ValueError, json.JSONDecodeError) as exc:
        raise EvaluationError(
            f"invalid catalog feature JSON for {row['source_capture_id']}"
        ) from exc
    expected_lengths = {
        "thumbnail": master_match.JACKET_THUMBNAIL_SIZE[0]
        * master_match.JACKET_THUMBNAIL_SIZE[1]
        * 3,
        "histogram": 24,
        "dhash": master_match.JACKET_DHASH_SIZE**2,
    }
    if (
        len(thumbnail) != expected_lengths["thumbnail"]
        or len(histogram) != expected_lengths["histogram"]
        or len(dhash) != expected_lengths["dhash"]
    ):
        raise EvaluationError(f"catalog feature shape mismatch for {row['source_capture_id']}")
    if not all(np.isfinite(values).all() for values in (thumbnail, histogram, dhash)):
        raise EvaluationError(
            f"catalog feature contains non-finite values: {row['source_capture_id']}"
        )
    if (
        np.any((thumbnail < 0) | (thumbnail > 1))
        or np.any(histogram < 0)
        or not np.isclose(float(histogram.sum()), 1.0, atol=1e-5)
        or np.any((dhash != 0) & (dhash != 1))
        or master_match.bits_to_hex(dhash) != row["dhash_hex"]
    ):
        raise EvaluationError(
            f"catalog feature values are inconsistent: {row['source_capture_id']}"
        )
    return master_match.JacketFeature(
        thumbnail=thumbnail,
        histogram=histogram,
        dhash_bits=dhash,
        dhash_hex=str(row["dhash_hex"]),
    )


def load_catalog_features(
    path: Path, observations: list[TruthObservation]
) -> dict[str, master_match.JacketFeature]:
    try:
        with _read_only_connection(path) as connection:
            metadata = {
                str(row["key"]): str(row["value"])
                for row in connection.execute("SELECT key, value FROM catalog_metadata")
            }
            rows = connection.execute(
                """
                SELECT source_capture_id, feature_extractor_version, jacket_feature_version,
                       image_kind, thumbnail_rgb_json, histogram_json, dhash_bits_json, dhash_hex
                FROM jacket_references
                """
            ).fetchall()
    except sqlite3.Error as exc:
        raise EvaluationError(f"invalid catalog database {path}: {exc}") from exc
    if metadata.get("catalog_identity") != CATALOG_IDENTITY:
        raise EvaluationError("catalog identity mismatch")
    if metadata.get("schema_version") != CATALOG_SCHEMA_VERSION:
        raise EvaluationError("catalog schema version mismatch")
    features: dict[str, master_match.JacketFeature] = {}
    for row in rows:
        observation_id = str(row["source_capture_id"])
        if observation_id in features:
            raise EvaluationError(f"catalog contains duplicate source capture: {observation_id}")
        if (
            row["feature_extractor_version"] != FEATURE_EXTRACTOR_VERSION
            or row["jacket_feature_version"] != JACKET_FEATURE_VERSION
            or row["image_kind"] != IMAGE_KIND
        ):
            raise EvaluationError(f"catalog contains an incompatible feature: {observation_id}")
        features[observation_id] = _feature_from_catalog_row(row)
    expected_ids = {item.observation_id for item in observations}
    if set(features) != expected_ids:
        raise EvaluationError("truth ODS and catalog observation sets do not match exactly")
    return features


def _distance_rows_by_song(
    grid_feature: master_match.JacketFeature,
    official_by_song: dict[str, list[SnapshotSong]],
) -> list[tuple[float, str, SnapshotSong]]:
    rows: list[tuple[float, str, SnapshotSong]] = []
    for song_id, official_rows in official_by_song.items():
        candidates = [
            (master_match.jacket_feature_distance(grid_feature, row.feature), row)
            for row in official_rows
        ]
        distance, best_row = min(candidates, key=lambda item: (item[0], item[1].jacket_sha256))
        rows.append((distance, song_id, best_row))
    return sorted(rows, key=lambda item: (item[0], item[1]))


def verify_snapshot_images_unchanged(
    snapshot: Path, snapshot_songs: list[SnapshotSong]
) -> None:
    expected_by_path: dict[Path, str] = {}
    for song in snapshot_songs:
        previous = expected_by_path.setdefault(song.jacket_path, song.jacket_sha256)
        if previous != song.jacket_sha256:
            raise EvaluationError(
                f"snapshot jacket path has conflicting hashes: {song.jacket_path}"
            )
    for relative, expected_hash in expected_by_path.items():
        if sha256_file(snapshot / relative) != expected_hash:
            raise EvaluationError(
                f"snapshot jacket changed during evaluation: {relative}"
            )


def evaluate_rows(
    observations: list[TruthObservation],
    grid_features: dict[str, master_match.JacketFeature],
    official_by_song: dict[str, list[SnapshotSong]],
    master_by_id: dict[str, MasterSong],
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    eligible = [item for item in observations if item.review_status == "confirmed"]
    if not eligible:
        raise EvaluationError("truth ODS contains no confirmed observations")
    if not official_by_song:
        raise EvaluationError("snapshot contains no songs resolved to the master")
    top_k_correct = Counter()
    decision_counts = Counter()
    for observation in eligible:
        ranked = _distance_rows_by_song(
            grid_features[observation.observation_id], official_by_song
        )
        truth_available = observation.truth_song_id in official_by_song
        top_distance, top_song_id, top_snapshot = ranked[0]
        second_distance = ranked[1][0] if len(ranked) > 1 else None
        margin = second_distance - top_distance if second_distance is not None else None
        rank_by_id = {song_id: index + 1 for index, (_, song_id, _) in enumerate(ranked)}
        expected_rank = rank_by_id.get(observation.truth_song_id)
        expected_distance = (
            next(
                distance
                for distance, song_id, _ in ranked
                if song_id == observation.truth_song_id
            )
            if truth_available
            else None
        )
        if not truth_available:
            decision_status = "hold_truth_not_in_snapshot"
        elif top_distance > master_match.DEFAULT_JACKET_DISTANCE_THRESHOLD:
            decision_status = "hold_distance"
        elif margin is not None and margin < master_match.DEFAULT_JACKET_AMBIGUITY_DELTA:
            decision_status = "hold_ambiguous"
        elif top_song_id == observation.truth_song_id:
            decision_status = "matched_correct"
        else:
            decision_status = "matched_false"
        decision_counts[decision_status] += 1
        if truth_available:
            for top_k in TOP_K_VALUES:
                if expected_rank is not None and expected_rank <= top_k:
                    top_k_correct[top_k] += 1
        top_master = master_by_id[top_song_id]
        rows.append(
            {
                "audit_no": observation.audit_no,
                "observation_id": observation.observation_id,
                "truth_song_id": observation.truth_song_id,
                "truth_title": observation.truth_title,
                "truth_artist": observation.truth_artist,
                "truth_official_snapshot_available": truth_available,
                "candidate_song_count": len(ranked),
                "top_song_id": top_song_id,
                "top_title": top_master.title,
                "top_artist": top_master.artist,
                "top_distance": top_distance,
                "second_distance": second_distance,
                "top_margin": margin,
                "expected_distance": expected_distance,
                "expected_rank": expected_rank,
                "top1_correct": top_song_id == observation.truth_song_id,
                "top3_correct": expected_rank is not None and expected_rank <= 3,
                "top5_correct": expected_rank is not None and expected_rank <= 5,
                "top10_correct": expected_rank is not None and expected_rank <= 10,
                "decision_status": decision_status,
                "top_candidates": json.dumps(
                    [
                        {"song_id": song_id, "distance": round(distance, 8)}
                        for distance, song_id, _ in ranked[:10]
                    ],
                    ensure_ascii=False,
                    separators=(",", ":"),
                ),
                "top_snapshot_jacket_sha256": top_snapshot.jacket_sha256,
            }
        )
    eligible_count = sum(item.review_status == "confirmed" for item in observations)
    truth_available_count = sum(row["truth_official_snapshot_available"] for row in rows)
    emitted = decision_counts["matched_correct"] + decision_counts["matched_false"]
    metrics = {
        "confirmed_truth_count": eligible_count,
        "rejected_count": sum(item.review_status == "rejected" for item in observations),
        "truth_official_snapshot_available_count": truth_available_count,
        "truth_official_snapshot_coverage": (
            truth_available_count / eligible_count if eligible_count else None
        ),
        "top_k": {
            str(top_k): {
                "correct": top_k_correct[top_k],
                "population": truth_available_count,
                "accuracy": (
                    top_k_correct[top_k] / truth_available_count
                    if truth_available_count
                    else None
                ),
            }
            for top_k in TOP_K_VALUES
        },
        "decision_status_counts": dict(sorted(decision_counts.items())),
        "decision_emitted_count": emitted,
        "decision_correct_count": decision_counts["matched_correct"],
        "decision_false_count": decision_counts["matched_false"],
        "decision_precision": decision_counts["matched_correct"] / emitted if emitted else None,
        "decision_coverage_of_confirmed": emitted / eligible_count if eligible_count else None,
        "decision_coverage_of_snapshot_available": (
            emitted / truth_available_count if truth_available_count else None
        ),
    }
    return rows, metrics


def write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str]) -> None:
    with path.open("x", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(
            stream,
            fieldnames=fieldnames,
            extrasaction="ignore",
            lineterminator="\n",
        )
        writer.writeheader()
        writer.writerows(rows)


def _percent(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.4%}"


def build_report(summary: dict[str, Any]) -> str:
    metrics = summary["jacket_metrics"]
    mapping = summary["snapshot_mapping_status_counts"]
    lines = [
        "# DDR WORLD snapshot jacket evaluation",
        "",
        "## Boundary",
        "",
        "- Completed snapshot, truth ODS, current ROI v2 catalog, and M4 master "
        "were read-only inputs.",
        "- No network access, DB mutation, spreadsheet mutation, manual-review mutation, "
        "or formal save occurred.",
        "- Existing M5 jacket distance/ambiguity thresholds are diagnostic only and were "
        "not tuned here.",
        "",
        "## Official snapshot to M4 mapping",
        "",
        f"- Snapshot songs: {summary['snapshot_song_count']}",
        f"- Resolved snapshot songs: {summary['resolved_snapshot_song_count']}",
        f"- Mapping statuses: `{json.dumps(mapping, ensure_ascii=False, sort_keys=True)}`",
        "- M4 GRAND PRIX-only candidates without a resolved DDR WORLD mapping: "
        f"{summary['grand_prix_only_candidate_song_count']}",
        "- Other M4 songs without a resolved DDR WORLD mapping: "
        f"{summary['not_in_ddrworld_candidate_song_count']}",
        "",
        "## Confirmed grid-jacket retrieval",
        "",
        f"- Confirmed truth: {metrics['confirmed_truth_count']}",
        f"- Rejected capture mismatches excluded: {metrics['rejected_count']}",
        "- Truth present in resolved snapshot: "
        f"{metrics['truth_official_snapshot_available_count']} "
        f"({_percent(metrics['truth_official_snapshot_coverage'])})",
        "",
        "| k | correct | population | accuracy |",
        "|---:|---:|---:|---:|",
    ]
    for top_k, item in metrics["top_k"].items():
        lines.append(
            f"| {top_k} | {item['correct']} | {item['population']} | {_percent(item['accuracy'])} |"
        )
    lines.extend(
        [
            "",
            "- Decision statuses: `"
            f"{json.dumps(metrics['decision_status_counts'], ensure_ascii=False, sort_keys=True)}`",
            f"- Decision precision: {_percent(metrics['decision_precision'])}",
            "- Decision coverage of confirmed truth: "
            f"{_percent(metrics['decision_coverage_of_confirmed'])}",
            "- Decision coverage where truth exists in snapshot: "
            f"{_percent(metrics['decision_coverage_of_snapshot_available'])}",
            "",
            "`matched_correct` / `matched_false` are evaluation outcomes, not production "
            "identity or save decisions.",
            "Rows held by distance, ambiguity, or missing official truth remain undecided.",
            "",
        ]
    )
    return "\n".join(lines)


def evaluate_snapshot(config: EvaluationConfig) -> Path:
    snapshot = config.snapshot.resolve()
    truth_ods = config.truth_ods.resolve()
    catalog = config.catalog.resolve()
    master = config.master.resolve()
    output = config.output.resolve()
    incomplete = output.with_name(f"{output.name}.incomplete")
    if output.exists() or incomplete.exists():
        raise EvaluationError(f"evaluation output already exists; refusing to overwrite: {output}")
    for label, path in (
        ("snapshot", snapshot),
        ("truth ODS", truth_ods),
        ("catalog", catalog),
        ("master", master),
    ):
        if not path.exists():
            raise EvaluationError(f"{label} input does not exist: {path}")

    input_hashes_before = {
        "truth_ods_sha256": sha256_file(truth_ods),
        "catalog_sha256": sha256_file(catalog),
        "master_sha256": sha256_file(master),
    }
    master_by_id, aliases = load_master(master)
    observations = load_truth(truth_ods, master_by_id)
    catalog_features = load_catalog_features(catalog, observations)
    snapshot_rows, snapshot_fingerprints = load_snapshot(snapshot)
    snapshot_songs = load_snapshot_features(snapshot, snapshot_rows)
    indexes = build_master_indexes(master_by_id, aliases)

    mapping_rows: list[dict[str, Any]] = []
    official_by_song: dict[str, list[SnapshotSong]] = defaultdict(list)
    mapping_statuses = Counter()
    for snapshot_song in snapshot_songs:
        mapping = resolve_snapshot_song(
            snapshot_song.title, snapshot_song.artist, master_by_id, indexes
        )
        mapping_statuses[mapping.status] += 1
        if mapping.song is not None:
            official_by_song[mapping.song.song_id].append(snapshot_song)
        mapping_rows.append(
            {
                "source_page": snapshot_song.source_page,
                "page_position": snapshot_song.page_position,
                "official_title": snapshot_song.title,
                "official_artist": snapshot_song.artist,
                "jacket_sha256": snapshot_song.jacket_sha256,
                "mapping_status": mapping.status,
                "mapping_reason": mapping.reason,
                "song_id": mapping.song.song_id if mapping.song else "",
                "master_title": mapping.song.title if mapping.song else "",
                "master_artist": mapping.song.artist if mapping.song else "",
            }
        )

    evaluation_rows, metrics = evaluate_rows(
        observations, catalog_features, official_by_song, master_by_id
    )
    present_song_ids = set(official_by_song)
    master_coverage_rows = []
    for song in sorted(
        master_by_id.values(), key=lambda item: (item.title, item.artist, item.song_id)
    ):
        if song.song_id in present_song_ids:
            status = "ddrworld_present"
        elif song.grand_prix_play_available:
            status = "grand_prix_only_candidate"
        else:
            status = "not_in_ddrworld_candidate"
        master_coverage_rows.append(
            {
                "song_id": song.song_id,
                "title": song.title,
                "artist": song.artist,
                "grand_prix_play_available": song.grand_prix_play_available,
                "ddrworld_status": status,
            }
        )
    coverage_counts = Counter(row["ddrworld_status"] for row in master_coverage_rows)

    input_hashes_after = {
        "truth_ods_sha256": sha256_file(truth_ods),
        "catalog_sha256": sha256_file(catalog),
        "master_sha256": sha256_file(master),
    }
    if input_hashes_after != input_hashes_before:
        raise EvaluationError(
            "read-only inputs changed during evaluation; output was not published"
        )
    snapshot_fingerprints_after = {
        "manifest_sha256": sha256_file(snapshot / "manifest.json"),
        "summary_sha256": sha256_file(snapshot / "summary.json"),
        "songs_sha256": sha256_file(snapshot / "songs.jsonl"),
    }
    if snapshot_fingerprints_after != snapshot_fingerprints:
        raise EvaluationError(
            "snapshot metadata changed during evaluation; output was not published"
        )
    verify_snapshot_images_unchanged(snapshot, snapshot_songs)

    summary = {
        "schema_version": SUMMARY_SCHEMA,
        "evaluator_version": EVALUATOR_VERSION,
        "inputs": {
            "snapshot": str(snapshot),
            "truth_ods": str(truth_ods),
            "catalog": str(catalog),
            "master": str(master),
            **input_hashes_before,
            **snapshot_fingerprints,
        },
        "feature_contract": {
            "feature_extractor_version": FEATURE_EXTRACTOR_VERSION,
            "jacket_feature_version": JACKET_FEATURE_VERSION,
            "distance_threshold": master_match.DEFAULT_JACKET_DISTANCE_THRESHOLD,
            "ambiguity_delta": master_match.DEFAULT_JACKET_AMBIGUITY_DELTA,
            "threshold_status": "diagnostic_existing_m5_not_tuned",
        },
        "snapshot_song_count": len(snapshot_songs),
        "resolved_snapshot_song_count": sum(len(rows) for rows in official_by_song.values()),
        "resolved_master_song_count": len(official_by_song),
        "snapshot_mapping_status_counts": dict(sorted(mapping_statuses.items())),
        "grand_prix_only_candidate_song_count": coverage_counts[
            "grand_prix_only_candidate"
        ],
        "not_in_ddrworld_candidate_song_count": coverage_counts[
            "not_in_ddrworld_candidate"
        ],
        "jacket_metrics": metrics,
        "output_files": [
            "snapshot_master_mapping.csv",
            "master_ddrworld_coverage.csv",
            "jacket_evaluation.csv",
            "summary.json",
            "report.md",
        ],
    }

    try:
        incomplete.mkdir(parents=True)
        write_csv(
            incomplete / "snapshot_master_mapping.csv",
            mapping_rows,
            [
                "source_page",
                "page_position",
                "official_title",
                "official_artist",
                "jacket_sha256",
                "mapping_status",
                "mapping_reason",
                "song_id",
                "master_title",
                "master_artist",
            ],
        )
        write_csv(
            incomplete / "master_ddrworld_coverage.csv",
            master_coverage_rows,
            [
                "song_id",
                "title",
                "artist",
                "grand_prix_play_available",
                "ddrworld_status",
            ],
        )
        write_csv(incomplete / "jacket_evaluation.csv", evaluation_rows, list(evaluation_rows[0]))
        (incomplete / "summary.json").write_text(
            json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        (incomplete / "report.md").write_text(
            build_report(summary), encoding="utf-8", newline="\n"
        )
        incomplete.rename(output)
    except (OSError, csv.Error) as exc:
        raise EvaluationError(f"failed to publish evaluation output: {exc}") from exc
    return output
