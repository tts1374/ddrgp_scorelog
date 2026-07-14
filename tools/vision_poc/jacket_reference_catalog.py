from __future__ import annotations

import argparse
import csv
import hashlib
import json
import shutil
import sqlite3
import uuid
from collections import Counter
from contextlib import closing
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from master import builder as master_builder
from tools.vision_poc import master_match

CATALOG_IDENTITY = "ddrgp-local-jacket-reference-catalog"
CATALOG_SCHEMA_VERSION = 1
FEATURE_EXTRACTOR_VERSION = "m5-jacket-v1"
BASE_SIZE = (1280, 720)
SONG_SELECT_JACKET_ROI = (812, 28, 150, 150)
REVIEW_STATUSES = ("auto_confirmed", "needs_review", "unresolved")
OBSERVATION_COLUMNS = {
    "source_image_path",
    "source_capture_id",
    "observed_title",
    "observed_artist",
    "observation_status",
    "image_kind",
    "expected_song_id",
}
COVERAGE_FIELDNAMES = (
    "song_id",
    "title",
    "artist",
    "master_version",
    "coverage_status",
    "reference_count",
    "reason",
)
CATALOG_TABLE_COLUMNS = {
    "catalog_metadata": {"key", "value"},
    "jacket_references": {
        "reference_id",
        "source_capture_id",
        "source_image_hash",
        "master_version",
        "song_id",
        "canonical_title_snapshot",
        "canonical_artist_snapshot",
        "review_status",
        "resolution_reason",
        "resolution_basis",
        "feature_extractor_version",
        "image_kind",
        "thumbnail_rgb_json",
        "histogram_json",
        "dhash_bits_json",
        "dhash_hex",
        "observed_title",
        "observed_artist",
        "observation_status",
        "expected_song_id",
        "created_at",
        "updated_at",
    },
    "reference_candidates": {"reference_id", "song_id", "candidate_reason"},
}
MASTER_TABLE_COLUMNS = {
    "songs": {
        "song_id",
        "title",
        "artist",
        "version",
        "source_version",
        "bpm",
        "category",
        "movie_stage",
        "availability",
        "free_play_available",
        "grand_prix_play_available",
        "official_availability_match",
        "notes",
        "created_at",
        "updated_at",
    },
    "charts": {
        "chart_id",
        "song_id",
        "play_style",
        "difficulty",
        "level",
        "raw_level",
        "shock_arrow",
        "is_removed",
        "is_limited",
        "notes",
    },
    "song_aliases": {
        "alias_id",
        "song_id",
        "alias_title",
        "alias_artist",
        "alias_type",
        "source",
    },
    "master_metadata": {"key", "value"},
    "source_snapshots": {
        "snapshot_id",
        "source_url",
        "fetched_at",
        "content_hash",
        "parser_version",
        "html_content",
    },
}
MASTER_REQUIRED_METADATA = {
    "master_version",
    "source_url",
    "generated_at",
    "generator_version",
    "source_hash",
    "song_count",
    "chart_count",
}
CATALOG_SCHEMA_SQL = f"""
PRAGMA user_version = {CATALOG_SCHEMA_VERSION};
CREATE TABLE catalog_metadata (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);
CREATE TABLE jacket_references (
  reference_id TEXT PRIMARY KEY,
  source_capture_id TEXT,
  source_image_hash TEXT NOT NULL,
  master_version TEXT NOT NULL,
  song_id TEXT,
  canonical_title_snapshot TEXT NOT NULL,
  canonical_artist_snapshot TEXT NOT NULL,
  review_status TEXT NOT NULL CHECK (
    review_status IN ('auto_confirmed', 'needs_review', 'unresolved')
  ),
  resolution_reason TEXT NOT NULL,
  resolution_basis TEXT NOT NULL,
  feature_extractor_version TEXT NOT NULL,
  image_kind TEXT NOT NULL CHECK (image_kind IN ('full_frame', 'jacket_crop')),
  thumbnail_rgb_json TEXT,
  histogram_json TEXT,
  dhash_bits_json TEXT,
  dhash_hex TEXT NOT NULL,
  observed_title TEXT NOT NULL,
  observed_artist TEXT NOT NULL,
  observation_status TEXT NOT NULL,
  expected_song_id TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE (source_image_hash, feature_extractor_version)
);
CREATE UNIQUE INDEX idx_jacket_references_capture
ON jacket_references(source_capture_id, feature_extractor_version)
WHERE source_capture_id IS NOT NULL;
CREATE INDEX idx_jacket_references_song ON jacket_references(song_id);
CREATE TABLE reference_candidates (
  reference_id TEXT NOT NULL REFERENCES jacket_references(reference_id)
    ON DELETE CASCADE,
  song_id TEXT NOT NULL,
  candidate_reason TEXT NOT NULL,
  PRIMARY KEY (reference_id, song_id)
);
"""


@dataclass(frozen=True)
class MasterIdentity:
    version: str
    songs: tuple[master_match.MasterSong, ...]
    aliases: tuple[tuple[str, str, str], ...]


@dataclass(frozen=True)
class Resolution:
    status: str
    song: master_match.MasterSong | None
    reason: str
    basis: str
    candidate_song_ids: tuple[str, ...]


@dataclass(frozen=True)
class IngestResult:
    reference_id: str
    disposition: str
    review_status: str
    reason: str


def utc_now() -> str:
    return datetime.now(UTC).isoformat(timespec="seconds")


def _resolved(path: Path) -> Path:
    return path.resolve(strict=False)


def ensure_data_path(path: Path, *, argument_name: str, directory: bool = False) -> None:
    root = _resolved(Path.cwd() / "data")
    candidate = _resolved(path)
    try:
        relative = candidate.relative_to(root)
    except ValueError as exc:
        raise ValueError(f"{argument_name} must be under data/: {path}") from exc
    if not relative.parts:
        raise ValueError(f"{argument_name} must not be the data/ directory itself")
    if not directory and candidate.suffix.lower() not in {".sqlite", ".sqlite3", ".db"}:
        raise ValueError(f"{argument_name} must be a SQLite file under data/: {path}")


def _connect_read_only(path: Path) -> sqlite3.Connection:
    if not path.is_file():
        raise ValueError(f"catalog is not a file: {path}")
    return sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=ro", uri=True)


def _table_columns(connection: sqlite3.Connection, table: str) -> set[str]:
    return {str(row[1]) for row in connection.execute(f"PRAGMA table_info({table})")}


def _user_tables(connection: sqlite3.Connection) -> set[str]:
    return {
        str(row[0])
        for row in connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'"
        )
    }


def _unique_index_columns(connection: sqlite3.Connection, table: str) -> set[tuple[str, ...]]:
    result: set[tuple[str, ...]] = set()
    for row in connection.execute(f"PRAGMA index_list({table})"):
        if not bool(row[2]):
            continue
        index_name = str(row[1]).replace("'", "''")
        columns = tuple(
            str(info[2]) for info in connection.execute(f"PRAGMA index_info('{index_name}')")
        )
        result.add(columns)
    return result


def _schema_signature(connection: sqlite3.Connection) -> tuple[tuple[str, str, str, str], ...]:
    return tuple(
        (
            str(row[0]),
            str(row[1]),
            str(row[2]),
            " ".join(str(row[3]).split()),
        )
        for row in connection.execute(
            """
            SELECT type, name, tbl_name, sql
            FROM sqlite_master
            WHERE type IN ('table', 'index')
              AND name NOT LIKE 'sqlite_%'
              AND sql IS NOT NULL
            ORDER BY type, name
            """
        )
    )


def _validate_catalog(connection: sqlite3.Connection) -> None:
    with closing(sqlite3.connect(":memory:")) as expected:
        expected.executescript(CATALOG_SCHEMA_SQL)
        if _schema_signature(connection) != _schema_signature(expected):
            raise ValueError("not a jacket reference catalog: exact schema mismatch")
    tables = _user_tables(connection)
    if tables != set(CATALOG_TABLE_COLUMNS):
        raise ValueError("not a jacket reference catalog: table identity mismatch")
    for table, expected_columns in CATALOG_TABLE_COLUMNS.items():
        if _table_columns(connection, table) != expected_columns:
            raise ValueError(f"jacket reference catalog {table} columns mismatch")
    metadata = dict(connection.execute("SELECT key, value FROM catalog_metadata"))
    if set(metadata) != {"catalog_identity", "schema_version", "created_at"}:
        raise ValueError("jacket reference catalog metadata keys mismatch")
    if metadata.get("catalog_identity") != CATALOG_IDENTITY:
        raise ValueError("not a jacket reference catalog: identity mismatch")
    if metadata.get("schema_version") != str(CATALOG_SCHEMA_VERSION):
        raise ValueError("unsupported jacket reference catalog schema version")
    if connection.execute("PRAGMA user_version").fetchone()[0] != CATALOG_SCHEMA_VERSION:
        raise ValueError("jacket reference catalog user_version mismatch")
    reference_unique = _unique_index_columns(connection, "jacket_references")
    expected_reference_unique = {
        ("reference_id",),
        ("source_image_hash", "feature_extractor_version"),
        ("source_capture_id", "feature_extractor_version"),
    }
    if not expected_reference_unique <= reference_unique:
        raise ValueError("jacket reference catalog reference uniqueness mismatch")
    if ("reference_id", "song_id") not in _unique_index_columns(connection, "reference_candidates"):
        raise ValueError("jacket reference catalog candidate uniqueness mismatch")
    foreign_keys = list(connection.execute("PRAGMA foreign_key_list(reference_candidates)"))
    if len(foreign_keys) != 1 or str(foreign_keys[0][2]) != "jacket_references":
        raise ValueError("jacket reference catalog candidate foreign key mismatch")


def create_catalog(path: Path) -> None:
    ensure_data_path(path, argument_name="--catalog")
    if path.exists():
        raise ValueError(f"catalog already exists: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        with closing(sqlite3.connect(path)) as connection, connection:
            connection.executescript(CATALOG_SCHEMA_SQL)
            now = utc_now()
            connection.executemany(
                "INSERT INTO catalog_metadata (key, value) VALUES (?, ?)",
                (
                    ("catalog_identity", CATALOG_IDENTITY),
                    ("schema_version", str(CATALOG_SCHEMA_VERSION)),
                    ("created_at", now),
                ),
            )
    except Exception:
        path.unlink(missing_ok=True)
        raise


def validate_catalog(path: Path) -> None:
    try:
        with closing(_connect_read_only(path)) as connection:
            _validate_catalog(connection)
    except sqlite3.DatabaseError as exc:
        raise ValueError(f"invalid jacket reference catalog: {path}") from exc


def _validate_master_tables(connection: sqlite3.Connection) -> None:
    with closing(sqlite3.connect(":memory:")) as expected:
        master_builder.create_schema(expected)
        if _schema_signature(connection) != _schema_signature(expected):
            raise ValueError("not a compatible M4 master database: exact schema mismatch")
    if _user_tables(connection) != set(MASTER_TABLE_COLUMNS):
        raise ValueError("not a compatible M4 master database: table identity mismatch")
    for table, expected_columns in MASTER_TABLE_COLUMNS.items():
        if _table_columns(connection, table) != expected_columns:
            raise ValueError(f"not a compatible M4 master database: {table} columns mismatch")


def _validate_master_content(connection: sqlite3.Connection) -> dict[str, str]:
    metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
    missing = sorted(MASTER_REQUIRED_METADATA - metadata.keys())
    empty = sorted(key for key in MASTER_REQUIRED_METADATA if not metadata.get(key))
    if missing or empty:
        raise ValueError("M4 master metadata is missing required non-empty values")
    song_count = int(connection.execute("SELECT COUNT(*) FROM songs").fetchone()[0])
    chart_count = int(connection.execute("SELECT COUNT(*) FROM charts").fetchone()[0])
    if song_count <= 0 or chart_count <= 0:
        raise ValueError("M4 master database must contain songs and charts")
    if metadata["song_count"] != str(song_count) or metadata["chart_count"] != str(chart_count):
        raise ValueError("M4 master metadata count mismatch")
    snapshots = list(connection.execute("SELECT source_url, content_hash FROM source_snapshots"))
    if len(snapshots) not in {1, 2}:
        raise ValueError("M4 master database must contain one or two source snapshots")
    snapshots_by_url = {str(row[0]): str(row[1]) for row in snapshots}
    if snapshots_by_url.get(metadata["source_url"]) != metadata["source_hash"]:
        raise ValueError("M4 master source metadata mismatch")
    official_url = metadata.get("official_source_url", "")
    official_hash = metadata.get("official_source_hash", "")
    if bool(official_url) != bool(official_hash):
        raise ValueError("M4 master official source metadata must be a complete pair")
    if official_url and snapshots_by_url.get(official_url) != official_hash:
        raise ValueError("M4 master official source metadata mismatch")
    return metadata


def load_master_identity(path: Path) -> MasterIdentity:
    if not path.is_file():
        raise ValueError(f"master DB is not a file: {path}")
    try:
        read_only = sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=ro", uri=True)
        with closing(read_only) as connection:
            _validate_master_tables(connection)
            metadata = _validate_master_content(connection)
            version = metadata["master_version"]
            songs = tuple(
                master_match.MasterSong(
                    song_id=str(row[0]),
                    title=str(row[1]),
                    artist=str(row[2]),
                    grand_prix_play_available=bool(row[3]),
                    official_availability_match=str(row[4] or ""),
                )
                for row in connection.execute(
                    """
                    SELECT song_id, title, artist, grand_prix_play_available,
                           official_availability_match
                    FROM songs ORDER BY song_id
                    """
                )
            )
            aliases = tuple(
                (str(row[0]), str(row[1]), str(row[2]))
                for row in connection.execute(
                    "SELECT song_id, alias_title, alias_artist FROM song_aliases ORDER BY alias_id"
                )
            )
    except sqlite3.DatabaseError as exc:
        raise ValueError(f"invalid M4 master database: {path}") from exc
    return MasterIdentity(version=version, songs=songs, aliases=aliases)


def _normalized_pair(title: str, artist: str) -> tuple[str, str]:
    return (
        master_match.normalize_song_title(title),
        master_match.normalize_song_title(artist),
    )


def resolve_observation(
    master: MasterIdentity,
    observed_title: str,
    observed_artist: str,
    *,
    observation_status: str = "ok",
    feature_available: bool = True,
) -> Resolution:
    title_key, artist_key = _normalized_pair(observed_title, observed_artist)
    if observation_status != "ok":
        return Resolution("unresolved", None, f"observation_{observation_status}", "", ())
    if not title_key or not artist_key:
        return Resolution("unresolved", None, "missing_title_or_artist", "", ())

    canonical = [
        song
        for song in master.songs
        if _normalized_pair(song.title, song.artist) == (title_key, artist_key)
    ]
    canonical_ids = {song.song_id for song in canonical}
    if len(canonical_ids) == 1:
        song = canonical[0]
        if not feature_available:
            return Resolution(
                "unresolved", song, "feature_extraction_failed", "canonical_exact", (song.song_id,)
            )
        if not song.grand_prix_play_available:
            return Resolution(
                "needs_review",
                song,
                "song_not_grand_prix_available",
                "canonical_exact",
                (song.song_id,),
            )
        return Resolution(
            "auto_confirmed",
            song,
            "canonical_title_artist_exact",
            "canonical_exact",
            (song.song_id,),
        )
    if len(canonical_ids) > 1:
        return Resolution(
            "needs_review",
            None,
            "ambiguous_canonical_title_artist",
            "",
            tuple(sorted(canonical_ids)),
        )

    alias_song_ids = {
        song_id
        for song_id, alias_title, alias_artist in master.aliases
        if _normalized_pair(alias_title, alias_artist) == (title_key, artist_key)
    }
    songs_by_id = {song.song_id: song for song in master.songs}
    if len(alias_song_ids) == 1:
        song = songs_by_id[next(iter(alias_song_ids))]
        if not feature_available:
            return Resolution(
                "unresolved", song, "feature_extraction_failed", "unique_alias", (song.song_id,)
            )
        if not song.grand_prix_play_available:
            return Resolution(
                "needs_review",
                song,
                "song_not_grand_prix_available",
                "unique_alias",
                (song.song_id,),
            )
        return Resolution(
            "auto_confirmed",
            song,
            "unique_alias_title_artist_exact",
            "unique_alias",
            (song.song_id,),
        )
    if len(alias_song_ids) > 1:
        return Resolution(
            "needs_review", None, "ambiguous_alias_title_artist", "", tuple(sorted(alias_song_ids))
        )

    title_candidates = {
        song.song_id
        for song in master.songs
        if master_match.normalize_song_title(song.title) == title_key
    }
    title_candidates.update(
        song_id
        for song_id, alias_title, _ in master.aliases
        if master_match.normalize_song_title(alias_title) == title_key
    )
    if title_candidates:
        return Resolution(
            "needs_review", None, "title_match_artist_mismatch", "", tuple(sorted(title_candidates))
        )
    return Resolution("unresolved", None, "identity_not_found", "", ())


def _scaled_box(image: Image.Image) -> tuple[int, int, int, int]:
    x, y, width, height = SONG_SELECT_JACKET_ROI
    sx = image.width / BASE_SIZE[0]
    sy = image.height / BASE_SIZE[1]
    return (round(x * sx), round(y * sy), round((x + width) * sx), round((y + height) * sy))


def _extract_feature(path: Path, image_kind: str) -> master_match.JacketFeature:
    with Image.open(path) as image:
        rgb = image.convert("RGB")
        if image_kind == "full_frame":
            rgb = rgb.crop(_scaled_box(rgb))
        elif image_kind != "jacket_crop":
            raise ValueError(f"unsupported image_kind: {image_kind}")
        return master_match.extract_jacket_feature(rgb)


def _feature_json(values: np.ndarray) -> str:
    return json.dumps([float(value) for value in values], separators=(",", ":"))


def _reference_id(source_hash: str) -> str:
    return hashlib.sha256(f"{FEATURE_EXTRACTOR_VERSION}:{source_hash}".encode()).hexdigest()


def ingest_observation(
    catalog_path: Path,
    master_db: Path,
    *,
    source_image_path: Path,
    source_capture_id: str,
    observed_title: str,
    observed_artist: str,
    observation_status: str = "ok",
    image_kind: str = "full_frame",
    expected_song_id: str = "",
    now: str | None = None,
) -> IngestResult:
    validate_catalog(catalog_path)
    master = load_master_identity(master_db)
    if not source_image_path.is_file():
        raise ValueError(f"source image does not exist: {source_image_path}")
    image_bytes = source_image_path.read_bytes()
    source_hash = hashlib.sha256(image_bytes).hexdigest()
    if image_kind not in {"full_frame", "jacket_crop"}:
        raise ValueError(f"unsupported image_kind: {image_kind}")
    feature: master_match.JacketFeature | None = None
    try:
        feature = _extract_feature(source_image_path, image_kind)
    except OSError:
        pass
    resolution = resolve_observation(
        master,
        observed_title,
        observed_artist,
        observation_status=observation_status,
        feature_available=feature is not None,
    )
    reference_id = _reference_id(source_hash)
    timestamp = now or utc_now()
    capture_id = source_capture_id.strip() or None
    with closing(sqlite3.connect(catalog_path)) as connection, connection:
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        _validate_catalog(connection)
        existing = connection.execute(
            """
            SELECT *
            FROM jacket_references
            WHERE (source_image_hash = ? AND feature_extractor_version = ?)
               OR (source_capture_id IS ? AND source_capture_id IS NOT NULL
                   AND feature_extractor_version = ?)
            """,
            (source_hash, FEATURE_EXTRACTOR_VERSION, capture_id, FEATURE_EXTRACTOR_VERSION),
        ).fetchall()
        if existing:
            if any(str(row["source_image_hash"]) != source_hash for row in existing):
                raise ValueError("source_capture_id was already used with different image bytes")
            row = existing[0]
            existing_reference_id = str(row["reference_id"])
            desired_capture_id = row["source_capture_id"] or capture_id
            desired_expected_song_id = expected_song_id or str(row["expected_song_id"])
            identity_changed = (
                observed_title != str(row["observed_title"])
                or observed_artist != str(row["observed_artist"])
                or observation_status != str(row["observation_status"])
            )
            feature_changed = image_kind != str(row["image_kind"])
            audit_or_capture_changed = desired_capture_id != row[
                "source_capture_id"
            ] or desired_expected_song_id != str(row["expected_song_id"])
            if not identity_changed and not feature_changed and not audit_or_capture_changed:
                return IngestResult(
                    existing_reference_id,
                    "existing",
                    str(row["review_status"]),
                    str(row["resolution_reason"]),
                )
            if identity_changed or feature_changed:
                song = resolution.song
                connection.execute(
                    """
                    UPDATE jacket_references
                    SET source_capture_id = ?, master_version = ?, song_id = ?,
                        canonical_title_snapshot = ?, canonical_artist_snapshot = ?,
                        review_status = ?, resolution_reason = ?, resolution_basis = ?,
                        image_kind = ?, thumbnail_rgb_json = ?, histogram_json = ?,
                        dhash_bits_json = ?, dhash_hex = ?,
                        observed_title = ?, observed_artist = ?, observation_status = ?,
                        expected_song_id = ?, updated_at = ?
                    WHERE reference_id = ?
                    """,
                    (
                        desired_capture_id,
                        master.version,
                        None if song is None else song.song_id,
                        "" if song is None else song.title,
                        "" if song is None else song.artist,
                        resolution.status,
                        resolution.reason,
                        resolution.basis,
                        image_kind if feature_changed else str(row["image_kind"]),
                        (
                            None if feature is None else _feature_json(feature.thumbnail)
                        )
                        if feature_changed
                        else row["thumbnail_rgb_json"],
                        (None if feature is None else _feature_json(feature.histogram))
                        if feature_changed
                        else row["histogram_json"],
                        (None if feature is None else _feature_json(feature.dhash_bits))
                        if feature_changed
                        else row["dhash_bits_json"],
                        ("" if feature is None else feature.dhash_hex)
                        if feature_changed
                        else str(row["dhash_hex"]),
                        observed_title,
                        observed_artist,
                        observation_status,
                        desired_expected_song_id,
                        timestamp,
                        existing_reference_id,
                    ),
                )
                connection.execute(
                    "DELETE FROM reference_candidates WHERE reference_id = ?",
                    (existing_reference_id,),
                )
                connection.executemany(
                    "INSERT INTO reference_candidates "
                    "(reference_id, song_id, candidate_reason) VALUES (?, ?, ?)",
                    (
                        (existing_reference_id, song_id, resolution.reason)
                        for song_id in resolution.candidate_song_ids
                    ),
                )
                return IngestResult(
                    existing_reference_id,
                    "updated",
                    resolution.status,
                    resolution.reason,
                )
            connection.execute(
                """
                UPDATE jacket_references
                SET source_capture_id = ?, expected_song_id = ?, updated_at = ?
                WHERE reference_id = ?
                """,
                (
                    desired_capture_id,
                    desired_expected_song_id,
                    timestamp,
                    existing_reference_id,
                ),
            )
            return IngestResult(
                existing_reference_id,
                "updated",
                str(row["review_status"]),
                str(row["resolution_reason"]),
            )
        song = resolution.song
        reason = resolution.reason
        connection.execute(
            """
            INSERT INTO jacket_references (
              reference_id, source_capture_id, source_image_hash, master_version, song_id,
              canonical_title_snapshot, canonical_artist_snapshot, review_status,
              resolution_reason, resolution_basis, feature_extractor_version,
              image_kind, thumbnail_rgb_json, histogram_json, dhash_bits_json, dhash_hex,
              observed_title, observed_artist, observation_status, expected_song_id,
              created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                reference_id,
                capture_id,
                source_hash,
                master.version,
                None if song is None else song.song_id,
                "" if song is None else song.title,
                "" if song is None else song.artist,
                resolution.status,
                reason,
                resolution.basis,
                FEATURE_EXTRACTOR_VERSION,
                image_kind,
                None if feature is None else _feature_json(feature.thumbnail),
                None if feature is None else _feature_json(feature.histogram),
                None if feature is None else _feature_json(feature.dhash_bits),
                "" if feature is None else feature.dhash_hex,
                observed_title,
                observed_artist,
                observation_status,
                expected_song_id,
                timestamp,
                timestamp,
            ),
        )
        connection.executemany(
            "INSERT INTO reference_candidates "
            "(reference_id, song_id, candidate_reason) VALUES (?, ?, ?)",
            (
                (reference_id, song_id, resolution.reason)
                for song_id in resolution.candidate_song_ids
            ),
        )
    return IngestResult(reference_id, "created", resolution.status, reason)


def _decode_vector(raw: str | None, *, length: int, field_name: str) -> np.ndarray:
    if raw is None:
        raise ValueError(f"catalog reference is missing {field_name}")
    try:
        values = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise ValueError(f"catalog reference has invalid {field_name}") from exc
    if not isinstance(values, list) or len(values) != length:
        raise ValueError(f"catalog reference has invalid {field_name} length")
    if any(isinstance(value, bool) or not isinstance(value, (int, float)) for value in values):
        raise ValueError(f"catalog reference has invalid {field_name} values")
    vector = np.asarray(values, dtype=np.float32)
    if not np.isfinite(vector).all() or ((vector < 0) | (vector > 1)).any():
        raise ValueError(f"catalog reference has out-of-range {field_name} values")
    return vector


def _reference_state(row: sqlite3.Row, master: MasterIdentity) -> tuple[str, str]:
    song_id = str(row["song_id"] or "")
    if not song_id:
        return str(row["review_status"]), str(row["resolution_reason"])
    songs_by_id = {song.song_id: song for song in master.songs}
    song = songs_by_id.get(song_id)
    if song is None:
        return "orphaned", "master_song_missing"
    if not song.grand_prix_play_available:
        return "orphaned", "song_not_grand_prix_available"
    review_status = str(row["review_status"])
    if review_status != "auto_confirmed":
        return review_status, str(row["resolution_reason"])
    if (song.title, song.artist) != (
        str(row["canonical_title_snapshot"]),
        str(row["canonical_artist_snapshot"]),
    ):
        return "needs_review", "master_identity_changed"
    if str(row["master_version"]) != master.version:
        return "needs_review", "master_version_changed"
    return review_status, str(row["resolution_reason"])


def load_m5_feature_entries(
    catalog_path: Path,
    master_db: Path,
) -> list[master_match.JacketFeatureMasterEntry]:
    validate_catalog(catalog_path)
    master = load_master_identity(master_db)
    songs_by_id = {song.song_id: song for song in master.songs}
    entries: list[master_match.JacketFeatureMasterEntry] = []
    with closing(_connect_read_only(catalog_path)) as connection:
        connection.row_factory = sqlite3.Row
        _validate_catalog(connection)
        for row in connection.execute("SELECT * FROM jacket_references ORDER BY reference_id"):
            state, _ = _reference_state(row, master)
            if state != "auto_confirmed":
                continue
            song = songs_by_id[str(row["song_id"])]
            feature = master_match.JacketFeature(
                thumbnail=_decode_vector(
                    row["thumbnail_rgb_json"], length=768, field_name="thumbnail_rgb"
                ),
                histogram=_decode_vector(row["histogram_json"], length=24, field_name="histogram"),
                dhash_bits=_decode_vector(
                    row["dhash_bits_json"], length=64, field_name="dhash_bits"
                ),
                dhash_hex=str(row["dhash_hex"]),
            )
            entries.append(
                master_match.JacketFeatureMasterEntry(
                    organized_file=f"catalog:{row['reference_id']}",
                    source_song_title=str(row["observed_title"]),
                    song_id=song.song_id,
                    title=song.title,
                    artist=song.artist,
                    feature=feature,
                )
            )
    return entries


def build_coverage(
    catalog_path: Path, master_db: Path
) -> tuple[list[dict[str, str]], dict[str, Any]]:
    validate_catalog(catalog_path)
    master = load_master_identity(master_db)
    gp_songs = [song for song in master.songs if song.grand_prix_play_available]
    references_by_song: dict[str, list[tuple[str, str]]] = {}
    orphan_reasons: Counter[str] = Counter()
    failure_reasons: Counter[str] = Counter()
    current_state_reasons: Counter[str] = Counter()
    auto_confirmed = 0
    current_auto_confirmed = 0
    audited_auto_confirmed = 0
    known_false = 0
    unresolved_observations = 0
    total_observations = 0
    with closing(_connect_read_only(catalog_path)) as connection:
        connection.row_factory = sqlite3.Row
        rows = list(connection.execute("SELECT * FROM jacket_references ORDER BY reference_id"))
        total_observations = len(rows)
        for row in rows:
            state, reason = _reference_state(row, master)
            original_status = str(row["review_status"])
            song_id = str(row["song_id"] or "")
            if original_status == "auto_confirmed":
                auto_confirmed += 1
                expected = str(row["expected_song_id"])
                if expected:
                    audited_auto_confirmed += 1
                    if expected != song_id:
                        known_false += 1
            else:
                failure_reasons[str(row["resolution_reason"])] += 1
            if state == "orphaned":
                orphan_reasons[reason] += 1
                continue
            if song_id:
                references_by_song.setdefault(song_id, []).append((state, reason))
            else:
                unresolved_observations += 1
            if state == "auto_confirmed":
                current_auto_confirmed += 1
            else:
                current_state_reasons[reason] += 1

    coverage_rows: list[dict[str, str]] = []
    status_counts: Counter[str] = Counter()
    for song in gp_songs:
        states = references_by_song.get(song.song_id, [])
        if any(state == "auto_confirmed" for state, _ in states):
            status, reason = "referenced", ""
        elif any(state == "needs_review" for state, _ in states):
            status = "needs_review"
            reason = next(reason for state, reason in states if state == "needs_review")
        elif any(state == "unresolved" for state, _ in states):
            status = "unresolved"
            reason = next(reason for state, reason in states if state == "unresolved")
        else:
            status, reason = "uncollected", ""
        status_counts[status] += 1
        coverage_rows.append(
            {
                "song_id": song.song_id,
                "title": song.title,
                "artist": song.artist,
                "master_version": master.version,
                "coverage_status": status,
                "reference_count": str(len(states)),
                "reason": reason,
            }
        )
    ingest_auto_rate = 0.0 if total_observations == 0 else auto_confirmed / total_observations
    current_auto_rate = (
        0.0 if total_observations == 0 else current_auto_confirmed / total_observations
    )
    improvement_by_reason = {
        "feature_extraction_failed": "recapture or verify the jacket crop/image format",
        "missing_title_or_artist": "add the missing title/artist observation",
        "identity_not_found": "review the observation and current master identity",
        "title_match_artist_mismatch": "review artist text without relaxing exact identity",
        "ambiguous_canonical_title_artist": "manually review the duplicate canonical identity",
        "ambiguous_alias_title_artist": "manually review the ambiguous alias evidence",
        "master_version_changed": "review the reference against the current master version",
        "master_identity_changed": "review the changed canonical identity",
        "song_not_grand_prix_available": "keep as orphan unless GP availability is restored",
        "master_song_missing": "keep as orphan and inspect the master update",
    }
    improvement_reasons = sorted(
        set(failure_reasons) | set(current_state_reasons) | set(orphan_reasons)
    )
    if audited_auto_confirmed == 0:
        audit_status = "no_expected_values"
    elif audited_auto_confirmed < auto_confirmed:
        audit_status = "partially_evaluated"
    else:
        audit_status = "evaluated"
    summary: dict[str, Any] = {
        "catalog_identity": CATALOG_IDENTITY,
        "schema_version": CATALOG_SCHEMA_VERSION,
        "master_version": master.version,
        "grand_prix_song_count": len(gp_songs),
        "coverage_status_counts": dict(sorted(status_counts.items())),
        "orphaned_reference_count": sum(orphan_reasons.values()),
        "orphan_reason_counts": dict(sorted(orphan_reasons.items())),
        "captured_observation_count": total_observations,
        "auto_confirmed_observation_count": current_auto_confirmed,
        "auto_confirm_rate": current_auto_rate,
        "auto_confirm_target_met": current_auto_rate >= 0.90,
        "auto_confirm_stretch_target_met": current_auto_rate >= 0.95,
        "ingest_auto_confirmed_observation_count": auto_confirmed,
        "ingest_auto_confirm_rate": ingest_auto_rate,
        "non_auto_confirm_reason_counts": dict(sorted(failure_reasons.items())),
        "current_reference_state_reason_counts": dict(sorted(current_state_reasons.items())),
        "improvement_candidates": [
            {
                "reason": reason,
                "suggestion": improvement_by_reason.get(
                    reason, "review the observation and resolution evidence"
                ),
            }
            for reason in improvement_reasons
        ],
        "unassigned_unresolved_observation_count": unresolved_observations,
        "audited_auto_confirm_count": audited_auto_confirmed,
        "unaudited_auto_confirm_count": auto_confirmed - audited_auto_confirmed,
        "known_false_auto_confirm_count": known_false,
        "known_false_auto_confirm_audit_status": audit_status,
        "known_false_auto_confirm_audit_passed": audit_status == "evaluated" and known_false == 0,
        "notes": [
            "All GP-available songs are counted once in the four coverage statuses.",
            "Unassigned unresolved observations cannot be attributed to a song "
            "and are reported separately.",
            "Expected song IDs are audit-only and never participate in identity resolution.",
        ],
    }
    return coverage_rows, summary


def write_coverage_outputs(
    output_dir: Path, rows: list[dict[str, str]], summary: dict[str, Any]
) -> None:
    ensure_data_path(output_dir, argument_name="--coverage-output", directory=True)
    output_dir.mkdir(parents=True, exist_ok=True)
    with (output_dir / "jacket_catalog_song_coverage.csv").open(
        "w", encoding="utf-8", newline=""
    ) as file:
        writer = csv.DictWriter(file, fieldnames=COVERAGE_FIELDNAMES)
        writer.writeheader()
        writer.writerows(rows)
    (output_dir / "jacket_catalog_coverage_summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    lines = [
        "# Jacket catalog coverage",
        "",
        f"- master version: `{summary['master_version']}`",
        f"- GP songs: `{summary['grand_prix_song_count']}`",
        f"- coverage: `{json.dumps(summary['coverage_status_counts'], sort_keys=True)}`",
        f"- orphaned references: `{summary['orphaned_reference_count']}`",
        f"- captured observations: `{summary['captured_observation_count']}`",
        f"- current auto-confirm rate: `{summary['auto_confirm_rate']:.2%}`",
        f"- ingest-time auto-confirm rate: `{summary['ingest_auto_confirm_rate']:.2%}`",
        f"- known false auto-confirms: `{summary['known_false_auto_confirm_count']}`",
        f"- known false audit: `{summary['known_false_auto_confirm_audit_status']}`",
        "",
        "## Non-auto-confirm reasons",
        "",
    ]
    for reason, count in summary["non_auto_confirm_reason_counts"].items():
        lines.append(f"- `{reason}`: `{count}`")
    if not summary["non_auto_confirm_reason_counts"]:
        lines.append("- none")
    lines.extend(["", "## Improvement candidates", ""])
    for candidate in summary["improvement_candidates"]:
        lines.append(f"- `{candidate['reason']}`: {candidate['suggestion']}")
    if not summary["improvement_candidates"]:
        lines.append("- none")
    lines.extend(
        [
            "",
            "Unassigned unresolved observations are reported separately because they "
            "cannot be safely attributed to a GP song.",
            "Expected song IDs are used only by the known-false audit.",
            "",
        ]
    )
    (output_dir / "jacket_catalog_coverage.md").write_text(
        "\n".join(lines), encoding="utf-8", newline="\n"
    )


def read_observations(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as file:
        reader = csv.DictReader(file)
        fieldnames = set(reader.fieldnames or ())
        required = {"source_image_path", "observed_title", "observed_artist"}
        if not required <= fieldnames:
            raise ValueError(f"observation CSV missing columns: {sorted(required - fieldnames)}")
        unknown = fieldnames - OBSERVATION_COLUMNS
        if unknown:
            raise ValueError(f"observation CSV has unknown columns: {sorted(unknown)}")
        return [{key: value or "" for key, value in row.items()} for row in reader]


def build_from_observation_csv(
    catalog_path: Path,
    master_db: Path,
    observation_csv: Path,
) -> list[IngestResult]:
    ensure_data_path(catalog_path, argument_name="--catalog")
    rows = read_observations(observation_csv)
    if catalog_path.exists():
        validate_catalog(catalog_path)
    staging_path = catalog_path.with_name(f".{catalog_path.stem}-{uuid.uuid4().hex}.sqlite")
    ensure_data_path(staging_path, argument_name="catalog staging path")
    results: list[IngestResult] = []
    try:
        if catalog_path.exists():
            shutil.copy2(catalog_path, staging_path)
        else:
            create_catalog(staging_path)
        for row in rows:
            image_path = Path(row["source_image_path"])
            if not image_path.is_absolute():
                image_path = observation_csv.parent / image_path
            results.append(
                ingest_observation(
                    staging_path,
                    master_db,
                    source_image_path=image_path,
                    source_capture_id=row.get("source_capture_id", ""),
                    observed_title=row["observed_title"],
                    observed_artist=row["observed_artist"],
                    observation_status=row.get("observation_status", "") or "ok",
                    image_kind=row.get("image_kind", "") or "full_frame",
                    expected_song_id=row.get("expected_song_id", ""),
                )
            )
        staging_path.replace(catalog_path)
    finally:
        staging_path.unlink(missing_ok=True)
    return results


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Build and inspect the local M5b jacket catalog.")
    subparsers = parser.add_subparsers(dest="command", required=True)
    build = subparsers.add_parser("build", help="Create/update a catalog from local observations.")
    build.add_argument("--catalog", type=Path, required=True)
    build.add_argument("--master-db", type=Path, required=True)
    build.add_argument("--observations", type=Path, required=True)
    build.add_argument("--coverage-output", type=Path, required=True)
    coverage = subparsers.add_parser("coverage", help="Generate read-only master coverage.")
    coverage.add_argument("--catalog", type=Path, required=True)
    coverage.add_argument("--master-db", type=Path, required=True)
    coverage.add_argument("--output", type=Path, required=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "build":
        ensure_data_path(args.catalog, argument_name="--catalog")
        ensure_data_path(args.coverage_output, argument_name="--coverage-output", directory=True)
        results = build_from_observation_csv(args.catalog, args.master_db, args.observations)
        rows, summary = build_coverage(args.catalog, args.master_db)
        write_coverage_outputs(args.coverage_output, rows, summary)
        created = sum(result.disposition == "created" for result in results)
        updated = sum(result.disposition == "updated" for result in results)
        existing = len(results) - created - updated
        print(
            f"Jacket catalog: {args.catalog} "
            f"({created} created, {updated} updated, {existing} existing)"
        )
        return 0
    ensure_data_path(args.catalog, argument_name="--catalog")
    ensure_data_path(args.output, argument_name="--output", directory=True)
    rows, summary = build_coverage(args.catalog, args.master_db)
    write_coverage_outputs(args.output, rows, summary)
    print(f"Jacket catalog coverage: {args.output} ({len(rows)} GP songs)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
