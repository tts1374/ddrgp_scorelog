from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import shutil
import sqlite3
import uuid
from collections import Counter
from contextlib import closing
from dataclasses import dataclass
from datetime import UTC, datetime
from io import BytesIO
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from master import builder as master_builder
from tools.vision_poc import master_match

CATALOG_IDENTITY = "ddrgp-local-jacket-reference-catalog"
CATALOG_SCHEMA_VERSION = 1
CATALOG_SCHEMA_VERSION_V2 = 2
CATALOG_SCHEMA_VERSION_V3 = 3
FEATURE_EXTRACTOR_VERSION = "m5-jacket-v1"
JACKET_FRAME_FEATURE_VERSION = "m5c-jacket-rgb-grid-v1"
TITLE_LINE_FEATURE_VERSION = "m5c-information-title-line-binary-sha256-v1"
COMPOSITE_IDENTITY_VERSION = "m5c-jacket-title-composite-identity-v1"
BASE_SIZE = (1280, 720)
SONG_SELECT_JACKET_ROI = (812, 28, 150, 150)
REVIEW_STATUSES = ("auto_confirmed", "needs_review", "unresolved")
REVIEW_STATUSES_V2 = (
    "auto_confirmed",
    "manual_confirmed",
    "needs_review",
    "unresolved",
    "rejected",
)
REVIEW_ACTIONS = ("manual_confirm", "reassign", "reject", "reopen")
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
CATALOG_V2_TABLE_COLUMNS = {
    "catalog_metadata": {"key", "value"},
    "jacket_references": CATALOG_TABLE_COLUMNS["jacket_references"]
    | {"review_revision", "manual_action_id", "manual_note"},
    "reference_candidates": CATALOG_TABLE_COLUMNS["reference_candidates"],
    "reference_review_history": {
        "history_id",
        "action_id",
        "reference_id",
        "action",
        "before_status",
        "after_status",
        "before_song_id",
        "after_song_id",
        "reason",
        "note",
        "action_at",
        "before_revision",
        "after_revision",
        "request_payload_json",
        "receipt_json",
    },
}
COMPOSITE_IDENTITY_COLUMNS = {
    "jacket_feature_version",
    "jacket_feature_hash",
    "title_line_feature_version",
    "title_line_hash",
    "composite_identity_version",
    "composite_identity_hash",
}
CATALOG_V3_TABLE_COLUMNS = {
    **CATALOG_V2_TABLE_COLUMNS,
    "jacket_references": CATALOG_V2_TABLE_COLUMNS["jacket_references"]
    | COMPOSITE_IDENTITY_COLUMNS,
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
  updated_at TEXT NOT NULL
);
CREATE INDEX idx_jacket_references_hash
ON jacket_references(source_image_hash, feature_extractor_version);
CREATE UNIQUE INDEX idx_jacket_references_hash_song
ON jacket_references(source_image_hash, feature_extractor_version, song_id)
WHERE song_id IS NOT NULL;
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

CATALOG_SCHEMA_V2_SQL = f"""
PRAGMA user_version = {CATALOG_SCHEMA_VERSION_V2};
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
    review_status IN (
      'auto_confirmed', 'manual_confirmed', 'needs_review', 'unresolved', 'rejected'
    )
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
  review_revision INTEGER NOT NULL CHECK (review_revision >= 0),
  manual_action_id TEXT,
  manual_note TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE INDEX idx_jacket_references_hash
ON jacket_references(source_image_hash, feature_extractor_version);
CREATE UNIQUE INDEX idx_jacket_references_hash_song
ON jacket_references(source_image_hash, feature_extractor_version, song_id)
WHERE song_id IS NOT NULL;
CREATE UNIQUE INDEX idx_jacket_references_capture
ON jacket_references(source_capture_id, feature_extractor_version)
WHERE source_capture_id IS NOT NULL;
CREATE INDEX idx_jacket_references_song ON jacket_references(song_id);
CREATE TABLE reference_candidates (
  reference_id TEXT NOT NULL REFERENCES jacket_references(reference_id),
  song_id TEXT NOT NULL,
  candidate_reason TEXT NOT NULL,
  PRIMARY KEY (reference_id, song_id)
);
CREATE TABLE reference_review_history (
  history_id INTEGER PRIMARY KEY AUTOINCREMENT,
  action_id TEXT NOT NULL UNIQUE,
  reference_id TEXT NOT NULL REFERENCES jacket_references(reference_id),
  action TEXT NOT NULL CHECK (action IN ('manual_confirm', 'reassign', 'reject', 'reopen')),
  before_status TEXT NOT NULL,
  after_status TEXT NOT NULL,
  before_song_id TEXT,
  after_song_id TEXT,
  reason TEXT NOT NULL,
  note TEXT NOT NULL,
  action_at TEXT NOT NULL,
  before_revision INTEGER NOT NULL,
  after_revision INTEGER NOT NULL,
  request_payload_json TEXT NOT NULL,
  receipt_json TEXT NOT NULL
);
CREATE INDEX idx_reference_review_history_reference
ON reference_review_history(reference_id, history_id);
"""

CATALOG_SCHEMA_V3_SQL = CATALOG_SCHEMA_V2_SQL.replace(
    f"PRAGMA user_version = {CATALOG_SCHEMA_VERSION_V2};",
    f"PRAGMA user_version = {CATALOG_SCHEMA_VERSION_V3};",
).replace(
    "  manual_note TEXT NOT NULL,\n  created_at TEXT NOT NULL,",
    """  manual_note TEXT NOT NULL,
  jacket_feature_version TEXT,
  jacket_feature_hash TEXT,
  title_line_feature_version TEXT,
  title_line_hash TEXT,
  composite_identity_version TEXT,
  composite_identity_hash TEXT,
  created_at TEXT NOT NULL,""",
).replace(
    "  created_at TEXT NOT NULL,\n  updated_at TEXT NOT NULL\n);",
    """  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  CHECK (
    (jacket_feature_version IS NULL AND jacket_feature_hash IS NULL
      AND title_line_feature_version IS NULL AND title_line_hash IS NULL
      AND composite_identity_version IS NULL AND composite_identity_hash IS NULL)
    OR
    (jacket_feature_version IS NOT NULL AND jacket_feature_hash IS NOT NULL
      AND title_line_feature_version IS NOT NULL AND title_line_hash IS NOT NULL
      AND composite_identity_version IS NOT NULL AND composite_identity_hash IS NOT NULL)
  )
);""",
).replace(
    "CREATE INDEX idx_jacket_references_song ON jacket_references(song_id);",
    """CREATE INDEX idx_jacket_references_song ON jacket_references(song_id);
CREATE UNIQUE INDEX idx_jacket_references_composite_identity
ON jacket_references(composite_identity_version, composite_identity_hash)
WHERE composite_identity_version IS NOT NULL AND composite_identity_hash IS NOT NULL;""",
)


@dataclass(frozen=True)
class MasterIdentity:
    version: str
    songs: tuple[master_match.MasterSong, ...]
    aliases: tuple[tuple[str, str, str], ...]
    source_hash: str = ""


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


@dataclass(frozen=True)
class ReviewMutationRequest:
    action_id: str
    reference_id: str
    action: str
    expected_revision: int
    expected_status: str
    expected_song_id: str | None
    song_id: str | None = None
    reason: str = ""
    note: str = ""
    action_at: str | None = None


@dataclass(frozen=True)
class ReviewMutationReceipt:
    action_id: str
    reference_id: str
    action: str
    status: str
    song_id: str | None
    revision: int
    idempotent: bool


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


def ensure_data_file_path(path: Path, *, argument_name: str) -> None:
    root = _resolved(Path.cwd() / "data")
    candidate = _resolved(path)
    try:
        relative = candidate.relative_to(root)
    except ValueError as exc:
        raise ValueError(f"{argument_name} must be under data/: {path}") from exc
    if not relative.parts:
        raise ValueError(f"{argument_name} must not be the data/ directory itself")


def _connect_read_only(path: Path) -> sqlite3.Connection:
    if not path.is_file():
        raise ValueError(f"catalog is not a file: {path}")
    return sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=ro", uri=True)


def _connect_read_write(path: Path) -> sqlite3.Connection:
    if not path.is_file():
        raise ValueError(f"catalog is not a file: {path}")
    return sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=rw", uri=True)


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


def _json_object(raw: str, *, field_name: str, expected_keys: set[str]) -> dict[str, Any]:
    try:
        value = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise ValueError(f"jacket reference catalog has invalid {field_name}") from exc
    if not isinstance(value, dict) or set(value) != expected_keys:
        raise ValueError(f"jacket reference catalog has invalid {field_name} fields")
    return value


def _is_sha256(value: object) -> bool:
    return isinstance(value, str) and len(value) == 64 and all(
        character in "0123456789abcdef" for character in value
    )


def composite_identity_hash(
    jacket_feature_version: str,
    jacket_feature_hash: str,
    title_line_feature_version: str,
    title_line_hash: str,
) -> str:
    if (
        jacket_feature_version != JACKET_FRAME_FEATURE_VERSION
        or title_line_feature_version != TITLE_LINE_FEATURE_VERSION
        or not _is_sha256(jacket_feature_hash)
        or not _is_sha256(title_line_hash)
    ):
        raise ValueError("composite identity feature fields are invalid")
    canonical = "\0".join(
        (
            COMPOSITE_IDENTITY_VERSION,
            jacket_feature_version,
            jacket_feature_hash,
            title_line_feature_version,
            title_line_hash,
        )
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def _validate_catalog_v3_identities(connection: sqlite3.Connection) -> None:
    seen: set[tuple[str, str]] = set()
    for row in connection.execute(
        "SELECT reference_id, jacket_feature_version, jacket_feature_hash, "
        "title_line_feature_version, title_line_hash, composite_identity_version, "
        "composite_identity_hash FROM jacket_references"
    ):
        values = tuple(row[index] for index in range(1, 7))
        if all(value is None for value in values):
            continue
        if any(value is None for value in values):
            raise ValueError("jacket reference catalog has partial composite identity")
        (
            jacket_version,
            jacket_hash,
            title_version,
            title_hash,
            identity_version,
            identity_hash,
        ) = (str(value) for value in values)
        if (
            identity_version != COMPOSITE_IDENTITY_VERSION
            or not _is_sha256(identity_hash)
            or composite_identity_hash(
                jacket_version, jacket_hash, title_version, title_hash
            )
            != identity_hash
        ):
            raise ValueError("jacket reference catalog has invalid composite identity")
        key = (identity_version, identity_hash)
        if key in seen:
            raise ValueError("jacket reference catalog has duplicate composite identity")
        seen.add(key)


def _validate_catalog_v2_content(connection: sqlite3.Connection) -> None:
    references = {
        str(row["reference_id"]): row
        for row in connection.execute(
            "SELECT reference_id, song_id, review_status, review_revision, manual_action_id, "
            "manual_note "
            "FROM jacket_references"
        )
    }
    histories: dict[str, list[sqlite3.Row]] = {}
    for row in connection.execute(
        "SELECT * FROM reference_review_history ORDER BY reference_id, history_id"
    ):
        histories.setdefault(str(row["reference_id"]), []).append(row)
    orphan_history_ids = sorted(set(histories) - set(references))
    if orphan_history_ids:
        raise ValueError("jacket reference catalog review history has orphan references")
    request_keys = {
        "action_id",
        "reference_id",
        "action",
        "expected_revision",
        "expected_status",
        "expected_song_id",
        "song_id",
        "reason",
        "note",
    }
    receipt_keys = {
        "action_id",
        "reference_id",
        "action",
        "status",
        "song_id",
        "revision",
    }
    for reference_id, reference in references.items():
        history_rows = histories.get(reference_id, [])
        expected_revision = 0
        previous_status: object | None = None
        previous_song_id: object | None = None
        for history in history_rows:
            if (
                str(history["action"]) not in REVIEW_ACTIONS
                or str(history["before_status"]) not in REVIEW_STATUSES_V2
                or str(history["after_status"]) not in REVIEW_STATUSES_V2
                or int(history["before_revision"]) != expected_revision
                or int(history["after_revision"]) != expected_revision + 1
            ):
                raise ValueError("jacket reference catalog review history chain mismatch")
            if expected_revision > 0 and (
                history["before_status"] != previous_status
                or history["before_song_id"] != previous_song_id
            ):
                raise ValueError("jacket reference catalog review history state discontinuity")
            request = _json_object(
                str(history["request_payload_json"]),
                field_name="review request payload",
                expected_keys=request_keys,
            )
            receipt = _json_object(
                str(history["receipt_json"]),
                field_name="review receipt",
                expected_keys=receipt_keys,
            )
            if (
                request["action_id"] != history["action_id"]
                or request["reference_id"] != reference_id
                or request["action"] != history["action"]
                or request["expected_revision"] != history["before_revision"]
                or request["expected_status"] != history["before_status"]
                or request["expected_song_id"] != history["before_song_id"]
                or request["reason"] != history["reason"]
                or request["note"] != history["note"]
                or receipt["action_id"] != history["action_id"]
                or receipt["reference_id"] != reference_id
                or receipt["action"] != history["action"]
                or receipt["status"] != history["after_status"]
                or receipt["song_id"] != history["after_song_id"]
                or receipt["revision"] != history["after_revision"]
            ):
                raise ValueError("jacket reference catalog review audit payload mismatch")
            action = str(history["action"])
            if (
                action in {"manual_confirm", "reassign"}
                and (
                    request["song_id"] is None
                    or request["song_id"] != history["after_song_id"]
                    or history["after_status"] != "manual_confirmed"
                )
            ) or (
                action == "reject"
                and (
                    request["song_id"] is not None
                    or history["after_song_id"] != history["before_song_id"]
                    or history["after_status"] != "rejected"
                )
            ) or (
                action == "reopen"
                and (
                    request["song_id"] is not None
                    or history["after_song_id"] is not None
                    or history["after_status"] != "needs_review"
                )
            ):
                raise ValueError("jacket reference catalog review action semantics mismatch")
            if (
                action == "manual_confirm"
                and history["before_status"] not in {"needs_review", "unresolved"}
            ) or (
                action == "reassign"
                and history["before_status"]
                not in {"auto_confirmed", "manual_confirmed"}
            ) or (
                action == "reject" and history["before_status"] == "rejected"
            ) or (
                action == "reopen" and history["before_status"] != "rejected"
            ):
                raise ValueError("jacket reference catalog review action source mismatch")
            previous_status = history["after_status"]
            previous_song_id = history["after_song_id"]
            expected_revision += 1
        if int(reference["review_revision"]) != expected_revision:
            raise ValueError("jacket reference catalog current revision mismatch")
        if history_rows:
            last = history_rows[-1]
            if (
                reference["manual_action_id"] != last["action_id"]
                or reference["review_status"] != last["after_status"]
                or reference["song_id"] != last["after_song_id"]
                or reference["manual_note"] != last["note"]
            ):
                raise ValueError("jacket reference catalog current review state mismatch")
        elif (
            reference["manual_action_id"] is not None
            or reference["review_status"] not in REVIEW_STATUSES
            or reference["manual_note"] != ""
        ):
            raise ValueError("jacket reference catalog has manual state without history")


def _catalog_schema_version(connection: sqlite3.Connection) -> int:
    if "catalog_metadata" not in _user_tables(connection):
        raise ValueError("not a jacket reference catalog: metadata table missing")
    metadata = dict(connection.execute("SELECT key, value FROM catalog_metadata"))
    try:
        return int(metadata.get("schema_version", ""))
    except ValueError as exc:
        raise ValueError("unsupported jacket reference catalog schema version") from exc


def _validate_catalog(connection: sqlite3.Connection) -> int:
    version = _catalog_schema_version(connection)
    if version == CATALOG_SCHEMA_VERSION:
        schema_sql = CATALOG_SCHEMA_SQL
        expected_columns_by_table = CATALOG_TABLE_COLUMNS
    elif version == CATALOG_SCHEMA_VERSION_V2:
        schema_sql = CATALOG_SCHEMA_V2_SQL
        expected_columns_by_table = CATALOG_V2_TABLE_COLUMNS
    elif version == CATALOG_SCHEMA_VERSION_V3:
        schema_sql = CATALOG_SCHEMA_V3_SQL
        expected_columns_by_table = CATALOG_V3_TABLE_COLUMNS
    else:
        raise ValueError("unsupported jacket reference catalog schema version")
    with closing(sqlite3.connect(":memory:")) as expected:
        expected.executescript(schema_sql)
        if _schema_signature(connection) != _schema_signature(expected):
            raise ValueError("not a jacket reference catalog: exact schema mismatch")
    tables = _user_tables(connection)
    if tables != set(expected_columns_by_table):
        raise ValueError("not a jacket reference catalog: table identity mismatch")
    for table, expected_columns in expected_columns_by_table.items():
        if _table_columns(connection, table) != expected_columns:
            raise ValueError(f"jacket reference catalog {table} columns mismatch")
    metadata = dict(connection.execute("SELECT key, value FROM catalog_metadata"))
    if set(metadata) != {"catalog_identity", "schema_version", "created_at"}:
        raise ValueError("jacket reference catalog metadata keys mismatch")
    if metadata.get("catalog_identity") != CATALOG_IDENTITY:
        raise ValueError("not a jacket reference catalog: identity mismatch")
    if connection.execute("PRAGMA user_version").fetchone()[0] != version:
        raise ValueError("jacket reference catalog user_version mismatch")
    reference_unique = _unique_index_columns(connection, "jacket_references")
    expected_reference_unique = {
        ("reference_id",),
        ("source_image_hash", "feature_extractor_version", "song_id"),
        ("source_capture_id", "feature_extractor_version"),
    }
    if not expected_reference_unique <= reference_unique:
        raise ValueError("jacket reference catalog reference uniqueness mismatch")
    if ("reference_id", "song_id") not in _unique_index_columns(connection, "reference_candidates"):
        raise ValueError("jacket reference catalog candidate uniqueness mismatch")
    foreign_keys = list(connection.execute("PRAGMA foreign_key_list(reference_candidates)"))
    if len(foreign_keys) != 1 or str(foreign_keys[0][2]) != "jacket_references":
        raise ValueError("jacket reference catalog candidate foreign key mismatch")
    if version in {CATALOG_SCHEMA_VERSION_V2, CATALOG_SCHEMA_VERSION_V3}:
        history_unique = _unique_index_columns(connection, "reference_review_history")
        if ("action_id",) not in history_unique:
            raise ValueError("jacket reference catalog review history uniqueness mismatch")
        history_foreign_keys = list(
            connection.execute("PRAGMA foreign_key_list(reference_review_history)")
        )
        if len(history_foreign_keys) != 1 or str(history_foreign_keys[0][2]) != "jacket_references":
            raise ValueError("jacket reference catalog review history foreign key mismatch")
        connection.row_factory = sqlite3.Row
        _validate_catalog_v2_content(connection)
        if version == CATALOG_SCHEMA_VERSION_V3:
            _validate_catalog_v3_identities(connection)
    return version


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


def catalog_schema_version(path: Path) -> int:
    try:
        with closing(_connect_read_only(path)) as connection:
            return _validate_catalog(connection)
    except sqlite3.DatabaseError as exc:
        raise ValueError(f"invalid jacket reference catalog: {path}") from exc


def load_composite_identities(path: Path) -> frozenset[tuple[str, str]]:
    """Return the complete v3 identity set, including rejected references."""
    try:
        with closing(_connect_read_only(path)) as connection:
            if _validate_catalog(connection) != CATALOG_SCHEMA_VERSION_V3:
                raise ValueError("composite identity lookup requires catalog schema version 3")
            return frozenset(
                (str(version), str(identity_hash))
                for version, identity_hash in connection.execute(
                    "SELECT composite_identity_version, composite_identity_hash "
                    "FROM jacket_references WHERE composite_identity_version IS NOT NULL "
                    "ORDER BY composite_identity_version, composite_identity_hash"
                )
            )
    except sqlite3.DatabaseError as exc:
        raise ValueError(f"invalid jacket reference catalog: {path}") from exc


def _create_catalog_v2(path: Path, *, created_at: str) -> None:
    with closing(sqlite3.connect(path)) as connection, connection:
        connection.execute("PRAGMA foreign_keys = ON")
        connection.executescript(CATALOG_SCHEMA_V2_SQL)
        connection.executemany(
            "INSERT INTO catalog_metadata (key, value) VALUES (?, ?)",
            (
                ("catalog_identity", CATALOG_IDENTITY),
                ("schema_version", str(CATALOG_SCHEMA_VERSION_V2)),
                ("created_at", created_at),
            ),
        )


def _create_catalog_v3(path: Path, *, created_at: str) -> None:
    with closing(sqlite3.connect(path)) as connection, connection:
        connection.execute("PRAGMA foreign_keys = ON")
        connection.executescript(CATALOG_SCHEMA_V3_SQL)
        connection.executemany(
            "INSERT INTO catalog_metadata (key, value) VALUES (?, ?)",
            (
                ("catalog_identity", CATALOG_IDENTITY),
                ("schema_version", str(CATALOG_SCHEMA_VERSION_V3)),
                ("created_at", created_at),
            ),
        )


def migrate_catalog_v1_to_v2(source_path: Path, target_path: Path) -> None:
    ensure_data_path(source_path, argument_name="--source-catalog")
    ensure_data_path(target_path, argument_name="--target-catalog")
    if _resolved(source_path) == _resolved(target_path):
        raise ValueError("v1 source and v2 target must be different paths")
    if target_path.exists():
        raise ValueError(f"v2 target already exists: {target_path}")
    if not target_path.parent.is_dir():
        raise ValueError(f"v2 target parent does not exist: {target_path.parent}")
    if catalog_schema_version(source_path) != CATALOG_SCHEMA_VERSION:
        raise ValueError("migration source must be a catalog schema version 1 database")
    staging_path = target_path.with_name(f".{target_path.name}.{uuid.uuid4().hex}.staging")
    try:
        with closing(_connect_read_only(source_path)) as source:
            source.row_factory = sqlite3.Row
            source.execute("BEGIN")
            _validate_catalog(source)
            created_at = dict(
                source.execute("SELECT key, value FROM catalog_metadata")
            )["created_at"]
            _create_catalog_v2(staging_path, created_at=created_at)
            with closing(sqlite3.connect(staging_path)) as target, target:
                target.execute("PRAGMA foreign_keys = ON")
                reference_columns = sorted(CATALOG_TABLE_COLUMNS["jacket_references"])
                placeholders = ", ".join("?" for _ in reference_columns)
                target.executemany(
                    f"INSERT INTO jacket_references ({', '.join(reference_columns)}, "
                    "review_revision, manual_action_id, manual_note) "
                    f"VALUES ({placeholders}, 0, NULL, '')",
                    (
                        tuple(row[column] for column in reference_columns)
                        for row in source.execute(
                            "SELECT * FROM jacket_references ORDER BY reference_id"
                        )
                    ),
                )
                target.executemany(
                    "INSERT INTO reference_candidates "
                    "(reference_id, song_id, candidate_reason) VALUES (?, ?, ?)",
                    source.execute(
                        "SELECT reference_id, song_id, candidate_reason "
                        "FROM reference_candidates ORDER BY reference_id, song_id"
                    ),
                )
        validate_catalog(staging_path)
        try:
            os.link(staging_path, target_path)
        except FileExistsError as exc:
            raise ValueError(f"v2 target appeared during migration: {target_path}") from exc
        staging_path.unlink()
    except Exception:
        staging_path.unlink(missing_ok=True)
        raise


_OBSERVATION_MANIFEST_COMMON_KEYS = {
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
_OBSERVATION_MANIFEST_COMPOSITE_KEYS = {
    "title_line_feature_version",
    "title_line_hash",
    "title_line_detector_version",
    "title_line_roi_version",
    "title_line_source_sequence",
    "title_line_captured_at_utc",
    "composite_identity_version",
    "composite_identity_hash",
}


def _scaled_region(
    width: int, height: int, region: tuple[int, int, int, int]
) -> tuple[int, int, int, int]:
    x, y, region_width, region_height = region
    return (
        round(x * width / BASE_SIZE[0]),
        round(y * height / BASE_SIZE[1]),
        max(1, round(region_width * width / BASE_SIZE[0])),
        max(1, round(region_height * height / BASE_SIZE[1])),
    )


def _sample_rgb_feature_hash(image: Image.Image) -> tuple[str, tuple[int, int, int, int]]:
    rgb = image.convert("RGB")
    roi = _scaled_region(rgb.width, rgb.height, SONG_SELECT_JACKET_ROI)
    x, y, width, height = roi
    if x < 0 or y < 0 or x + width > rgb.width or y + height > rgb.height:
        raise ValueError("scaled jacket ROI is outside source image")
    sample = bytearray()
    for sample_y in range(16):
        source_y = y + min(height - 1, sample_y * height // 16)
        for sample_x in range(16):
            source_x = x + min(width - 1, sample_x * width // 16)
            sample.extend(rgb.getpixel((source_x, source_y)))
    return hashlib.sha256(sample).hexdigest(), roi


def _binary_mask(
    image: Image.Image, region: tuple[int, int, int, int]
) -> tuple[bytes, int]:
    rgb = image.convert("RGB")
    base_x, base_y, base_width, base_height = region
    x, y, width, height = _scaled_region(rgb.width, rgb.height, region)
    if x < 0 or y < 0 or x + width > rgb.width or y + height > rgb.height:
        raise ValueError("scaled INFORMATION ROI is outside source image")
    packed = bytearray((base_width * base_height + 7) // 8)
    white_count = 0
    bit_index = 0
    for target_y in range(base_height):
        source_y = y + min(height - 1, target_y * height // base_height)
        for target_x in range(base_width):
            source_x = x + min(width - 1, target_x * width // base_width)
            red, green, blue = rgb.getpixel((source_x, source_y))
            if min(red, green, blue) >= 170 and max(red, green, blue) - min(
                red, green, blue
            ) <= 45:
                packed[bit_index // 8] |= 1 << (7 - bit_index % 8)
                white_count += 1
            bit_index += 1
    return bytes(packed), white_count


def _title_line_hash_from_source(image: Image.Image) -> str:
    _marker_mask, marker_count = _binary_mask(image, (286, 35, 134, 23))
    title_mask, title_count = _binary_mask(image, (286, 64, 504, 25))
    if marker_count < 100 or title_count < 32:
        raise ValueError("INFORMATION panel/title line is not displayed")
    return hashlib.sha256(title_mask).hexdigest()


def _load_backfill_identity(
    manifest_path: Path, *, reference: sqlite3.Row
) -> tuple[str, str, str, str, str, str]:
    try:
        value = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("observation manifest is unreadable") from exc
    if not isinstance(value, dict):
        raise ValueError("observation manifest must be an object")
    manifest_version = value.get("manifest_version")
    expected_keys = _OBSERVATION_MANIFEST_COMMON_KEYS | (
        _OBSERVATION_MANIFEST_COMPOSITE_KEYS
        if manifest_version == "m5c-observation-manifest-v2"
        else set()
    )
    if manifest_version not in {
        "m5c-observation-manifest-v1",
        "m5c-observation-manifest-v2",
    } or set(value) != expected_keys:
        raise ValueError("observation manifest version or fields are unsupported")
    observation_id = str(value["observation_id"])
    if observation_id != str(reference["source_capture_id"]):
        raise ValueError("observation manifest id does not match catalog row")
    if value["source_image"] != "source.png" or value["jacket_crop"] != "jacket-crop.png":
        raise ValueError("observation manifest image names are invalid")
    source_path = manifest_path.parent / "source.png"
    crop_path = manifest_path.parent / "jacket-crop.png"
    try:
        source_bytes = source_path.read_bytes()
        crop_bytes = crop_path.read_bytes()
    except OSError as exc:
        raise ValueError("observation source artifact is missing") from exc
    if (
        not source_bytes
        or not crop_bytes
        or hashlib.sha256(source_bytes).hexdigest() != value["source_image_hash"]
        or hashlib.sha256(crop_bytes).hexdigest() != value["jacket_crop_hash"]
        or value["jacket_crop_hash"] != reference["source_image_hash"]
    ):
        raise ValueError("observation artifact image hash mismatch")
    try:
        with Image.open(BytesIO(source_bytes)) as source_image:
            source_image.load()
            if (
                source_image.width != value["source_width"]
                or source_image.height != value["source_height"]
            ):
                raise ValueError("observation source dimensions mismatch")
            jacket_hash, roi = _sample_rgb_feature_hash(source_image)
            title_hash = _title_line_hash_from_source(source_image)
    except (OSError, TypeError) as exc:
        raise ValueError("observation source image is invalid") from exc
    if (
        value["feature_version"] != JACKET_FRAME_FEATURE_VERSION
        or value["feature_hash"] != jacket_hash
        or value["sample_width"] != 16
        or value["sample_height"] != 16
        or tuple(value[key] for key in ("roi_x", "roi_y", "roi_width", "roi_height"))
        != roi
        or value["feature_extractor_version"] != reference["feature_extractor_version"]
        or value["master_version"] != reference["master_version"]
    ):
        raise ValueError("observation jacket feature identity mismatch")
    identity_hash = composite_identity_hash(
        JACKET_FRAME_FEATURE_VERSION,
        jacket_hash,
        TITLE_LINE_FEATURE_VERSION,
        title_hash,
    )
    if manifest_version == "m5c-observation-manifest-v2" and (
        value["title_line_feature_version"] != TITLE_LINE_FEATURE_VERSION
        or value["title_line_hash"] != title_hash
        or value["title_line_source_sequence"] != value["source_sequence"]
        or value["title_line_captured_at_utc"] != value["captured_at_utc"]
        or value["composite_identity_version"] != COMPOSITE_IDENTITY_VERSION
        or value["composite_identity_hash"] != identity_hash
    ):
        raise ValueError("observation composite identity mismatch")
    return (
        JACKET_FRAME_FEATURE_VERSION,
        jacket_hash,
        TITLE_LINE_FEATURE_VERSION,
        title_hash,
        COMPOSITE_IDENTITY_VERSION,
        identity_hash,
    )


def migrate_catalog_v2_to_v3(
    source_path: Path, target_path: Path, artifact_root: Path
) -> dict[str, Any]:
    ensure_data_path(source_path, argument_name="--source-catalog")
    ensure_data_path(target_path, argument_name="--target-catalog")
    ensure_data_path(artifact_root, argument_name="--artifact-root", directory=True)
    if _resolved(source_path) == _resolved(target_path):
        raise ValueError("v2 source and v3 target must be different paths")
    if target_path.exists():
        raise ValueError(f"v3 target already exists: {target_path}")
    if not target_path.parent.is_dir():
        raise ValueError(f"v3 target parent does not exist: {target_path.parent}")
    if not artifact_root.is_dir():
        raise ValueError(f"artifact root is not a directory: {artifact_root}")
    if catalog_schema_version(source_path) != CATALOG_SCHEMA_VERSION_V2:
        raise ValueError("migration source must be a catalog schema version 2 database")
    manifests: dict[str, list[Path]] = {}
    for manifest_path in sorted(artifact_root.rglob("observation.json")):
        try:
            raw = json.loads(manifest_path.read_text(encoding="utf-8"))
            observation_id = raw.get("observation_id") if isinstance(raw, dict) else None
        except (OSError, UnicodeDecodeError, json.JSONDecodeError):
            continue
        if isinstance(observation_id, str) and observation_id:
            manifests.setdefault(observation_id, []).append(manifest_path)

    staging_path = target_path.with_name(f".{target_path.name}.{uuid.uuid4().hex}.staging")
    rows: list[dict[str, str]] = []
    try:
        with closing(_connect_read_only(source_path)) as source:
            source.row_factory = sqlite3.Row
            source.execute("BEGIN")
            _validate_catalog(source)
            created_at = dict(
                source.execute("SELECT key, value FROM catalog_metadata")
            )["created_at"]
            _create_catalog_v3(staging_path, created_at=created_at)
            with closing(sqlite3.connect(staging_path)) as target, target:
                target.row_factory = sqlite3.Row
                target.execute("PRAGMA foreign_keys = ON")
                reference_columns = sorted(CATALOG_V2_TABLE_COLUMNS["jacket_references"])
                placeholders = ", ".join("?" for _ in reference_columns)
                target.executemany(
                    f"INSERT INTO jacket_references ({', '.join(reference_columns)}) "
                    f"VALUES ({placeholders})",
                    (
                        tuple(row[column] for column in reference_columns)
                        for row in source.execute(
                            "SELECT * FROM jacket_references ORDER BY reference_id"
                        )
                    ),
                )
                target.executemany(
                    "INSERT INTO reference_candidates "
                    "(reference_id, song_id, candidate_reason) VALUES (?, ?, ?)",
                    source.execute(
                        "SELECT reference_id, song_id, candidate_reason "
                        "FROM reference_candidates ORDER BY reference_id, song_id"
                    ),
                )
                history_columns = sorted(
                    CATALOG_V2_TABLE_COLUMNS["reference_review_history"]
                )
                history_placeholders = ", ".join("?" for _ in history_columns)
                target.executemany(
                    f"INSERT INTO reference_review_history ({', '.join(history_columns)}) "
                    f"VALUES ({history_placeholders})",
                    (
                        tuple(row[column] for column in history_columns)
                        for row in source.execute(
                            "SELECT * FROM reference_review_history ORDER BY history_id"
                        )
                    ),
                )
                seen_identities: set[tuple[str, str]] = set()
                for reference in source.execute(
                    "SELECT * FROM jacket_references ORDER BY reference_id"
                ):
                    reference_id = str(reference["reference_id"])
                    observation_id = reference["source_capture_id"]
                    matches = manifests.get(str(observation_id), []) if observation_id else []
                    if len(matches) != 1:
                        status = "artifact_missing" if not matches else "artifact_ambiguous"
                        rows.append({"reference_id": reference_id, "status": status})
                        continue
                    try:
                        identity = _load_backfill_identity(matches[0], reference=reference)
                    except (ValueError, KeyError) as exc:
                        rows.append(
                            {
                                "reference_id": reference_id,
                                "status": "artifact_invalid",
                                "reason": str(exc),
                            }
                        )
                        continue
                    identity_key = (identity[4], identity[5])
                    if identity_key in seen_identities:
                        rows.append(
                            {"reference_id": reference_id, "status": "identity_conflict"}
                        )
                        continue
                    target.execute(
                        "UPDATE jacket_references SET jacket_feature_version = ?, "
                        "jacket_feature_hash = ?, title_line_feature_version = ?, "
                        "title_line_hash = ?, composite_identity_version = ?, "
                        "composite_identity_hash = ? WHERE reference_id = ?",
                        (*identity, reference_id),
                    )
                    seen_identities.add(identity_key)
                    rows.append({"reference_id": reference_id, "status": "backfilled"})
        validate_catalog(staging_path)
        try:
            os.link(staging_path, target_path)
        except FileExistsError as exc:
            raise ValueError(f"v3 target appeared during migration: {target_path}") from exc
        staging_path.unlink()
    except Exception:
        staging_path.unlink(missing_ok=True)
        raise
    counts = dict(Counter(row["status"] for row in rows))
    return {
        "status": "migrated",
        "source_catalog": str(source_path.resolve()),
        "target_catalog": str(target_path.resolve()),
        "artifact_root": str(artifact_root.resolve()),
        "schema_version": CATALOG_SCHEMA_VERSION_V3,
        "counts": counts,
        "rows": rows,
    }


def _mutation_payload(request: ReviewMutationRequest) -> str:
    return json.dumps(
        {
            "action_id": request.action_id,
            "reference_id": request.reference_id,
            "action": request.action,
            "expected_revision": request.expected_revision,
            "expected_status": request.expected_status,
            "expected_song_id": request.expected_song_id,
            "song_id": request.song_id,
            "reason": request.reason,
            "note": request.note,
        },
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )


def _receipt_from_json(raw: str, *, idempotent: bool) -> ReviewMutationReceipt:
    value = json.loads(raw)
    return ReviewMutationReceipt(
        action_id=str(value["action_id"]),
        reference_id=str(value["reference_id"]),
        action=str(value["action"]),
        status=str(value["status"]),
        song_id=value["song_id"],
        revision=int(value["revision"]),
        idempotent=idempotent,
    )


def _existing_review_receipt(
    connection: sqlite3.Connection, *, action_id: str, payload: str
) -> ReviewMutationReceipt | None:
    prior = connection.execute(
        "SELECT request_payload_json, receipt_json FROM reference_review_history "
        "WHERE action_id = ?",
        (action_id,),
    ).fetchone()
    if prior is None:
        return None
    if str(prior["request_payload_json"]) != payload:
        raise ValueError("action_id was already used with a different payload")
    return _receipt_from_json(str(prior["receipt_json"]), idempotent=True)


def apply_review_mutation(
    catalog_path: Path,
    master_db: Path,
    request: ReviewMutationRequest,
    *,
    fail_after_current_update: bool = False,
) -> ReviewMutationReceipt:
    if catalog_schema_version(catalog_path) not in {
        CATALOG_SCHEMA_VERSION_V2,
        CATALOG_SCHEMA_VERSION_V3,
    }:
        raise ValueError("manual review requires catalog schema version 2 or 3")
    if not request.action_id.strip() or not request.reference_id.strip():
        raise ValueError("action_id and reference_id must be non-empty")
    if request.action not in REVIEW_ACTIONS:
        raise ValueError(f"unsupported review action: {request.action}")
    if request.expected_status not in REVIEW_STATUSES_V2 or request.expected_revision < 0:
        raise ValueError("invalid expected review state")
    payload = _mutation_payload(request)
    if request.action in {"manual_confirm", "reassign"}:
        if not request.song_id:
            raise ValueError(f"{request.action} requires an explicit song_id")
    elif request.song_id is not None:
        raise ValueError(f"{request.action} does not accept song_id")
    timestamp = request.action_at or utc_now()
    try:
        parsed_timestamp = datetime.fromisoformat(timestamp.replace("Z", "+00:00"))
    except ValueError as exc:
        raise ValueError("action_at must be an ISO-8601 timestamp") from exc
    if parsed_timestamp.tzinfo is None or parsed_timestamp.utcoffset() is None:
        raise ValueError("action_at must include a UTC offset")

    with closing(_connect_read_only(catalog_path)) as connection:
        connection.row_factory = sqlite3.Row
        receipt = _existing_review_receipt(
            connection, action_id=request.action_id, payload=payload
        )
        if receipt is not None:
            return receipt

    master = load_master_identity(master_db)
    songs_by_id = {song.song_id: song for song in master.songs}
    selected_song = None
    if request.action in {"manual_confirm", "reassign"}:
        selected_song = songs_by_id.get(request.song_id)
        if selected_song is None or not selected_song.grand_prix_play_available:
            raise ValueError("manual song must exist in the current master and be GP-available")

    with closing(_connect_read_write(catalog_path)) as connection:
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("BEGIN IMMEDIATE")
        try:
            _validate_catalog(connection)
            receipt = _existing_review_receipt(
                connection, action_id=request.action_id, payload=payload
            )
            if receipt is not None:
                connection.rollback()
                return receipt
            row = connection.execute(
                "SELECT * FROM jacket_references WHERE reference_id = ?",
                (request.reference_id,),
            ).fetchone()
            if row is None:
                raise ValueError("review reference does not exist")
            before_status = str(row["review_status"])
            before_song_id = row["song_id"]
            before_revision = int(row["review_revision"])
            if (
                before_revision != request.expected_revision
                or before_status != request.expected_status
                or before_song_id != request.expected_song_id
            ):
                raise ValueError("stale review state")
            if request.action == "manual_confirm" and before_status not in {
                "needs_review",
                "unresolved",
            }:
                raise ValueError("manual_confirm requires an unconfirmed reference")
            if request.action == "reassign" and before_status not in {
                "auto_confirmed",
                "manual_confirmed",
            }:
                raise ValueError("reassign requires a confirmed reference")
            if request.action == "reject" and before_status == "rejected":
                raise ValueError("reference is already rejected")
            if request.action == "reopen" and before_status != "rejected":
                raise ValueError("reopen requires a rejected reference")
            if request.action in {"manual_confirm", "reassign"}:
                if str(row["feature_extractor_version"]) != FEATURE_EXTRACTOR_VERSION:
                    raise ValueError("manual review requires the current feature extractor")
                try:
                    _decode_persisted_feature(row)
                except ValueError as exc:
                    raise ValueError(
                        "manual review requires complete persisted jacket features"
                    ) from exc

            after_status = {
                "manual_confirm": "manual_confirmed",
                "reassign": "manual_confirmed",
                "reject": "rejected",
                "reopen": "needs_review",
            }[request.action]
            after_song_id = (
                selected_song.song_id
                if selected_song is not None
                else (None if request.action == "reopen" else before_song_id)
            )
            after_revision = before_revision + 1
            title = str(row["canonical_title_snapshot"])
            artist = str(row["canonical_artist_snapshot"])
            master_version = str(row["master_version"])
            if selected_song is not None:
                title, artist, master_version = (
                    selected_song.title,
                    selected_song.artist,
                    master.version,
                )
            connection.execute(
                """
                UPDATE jacket_references
                SET master_version = ?, song_id = ?, canonical_title_snapshot = ?,
                    canonical_artist_snapshot = ?, review_status = ?, resolution_reason = ?,
                    resolution_basis = ?, review_revision = ?, manual_action_id = ?,
                    manual_note = ?, updated_at = ?
                WHERE reference_id = ?
                """,
                (
                    master_version,
                    after_song_id,
                    title,
                    artist,
                    after_status,
                    request.reason,
                    "manual_review",
                    after_revision,
                    request.action_id,
                    request.note,
                    timestamp,
                    request.reference_id,
                ),
            )
            if fail_after_current_update:
                raise RuntimeError("injected mutation failure")
            receipt_value = {
                "action_id": request.action_id,
                "reference_id": request.reference_id,
                "action": request.action,
                "status": after_status,
                "song_id": after_song_id,
                "revision": after_revision,
            }
            receipt_json = json.dumps(
                receipt_value, ensure_ascii=False, sort_keys=True, separators=(",", ":")
            )
            connection.execute(
                """
                INSERT INTO reference_review_history (
                  action_id, reference_id, action, before_status, after_status,
                  before_song_id, after_song_id, reason, note, action_at,
                  before_revision, after_revision, request_payload_json, receipt_json
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    request.action_id,
                    request.reference_id,
                    request.action,
                    before_status,
                    after_status,
                    before_song_id,
                    after_song_id,
                    request.reason,
                    request.note,
                    timestamp,
                    before_revision,
                    after_revision,
                    payload,
                    receipt_json,
                ),
            )
            connection.commit()
            return _receipt_from_json(receipt_json, idempotent=False)
        except Exception:
            connection.rollback()
            raise


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
    return MasterIdentity(
        version=version,
        songs=songs,
        aliases=aliases,
        source_hash=metadata["source_hash"],
    )


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


def _extract_feature_from_bytes(
    image_bytes: bytes, image_kind: str
) -> master_match.JacketFeature:
    with Image.open(BytesIO(image_bytes)) as image:
        rgb = image.convert("RGB")
        if image_kind == "full_frame":
            rgb = rgb.crop(_scaled_box(rgb))
        elif image_kind != "jacket_crop":
            raise ValueError(f"unsupported image_kind: {image_kind}")
        return master_match.extract_jacket_feature(rgb)


def _extract_feature(path: Path, image_kind: str) -> master_match.JacketFeature:
    return _extract_feature_from_bytes(path.read_bytes(), image_kind)


def _feature_json(values: np.ndarray) -> str:
    return json.dumps([float(value) for value in values], separators=(",", ":"))


def _reference_id(
    source_hash: str,
    *,
    capture_id: str | None,
    song_id: str | None,
    observed_title: str,
    observed_artist: str,
) -> str:
    if capture_id is not None:
        identity = f"capture:{capture_id}"
    elif song_id is not None:
        identity = f"song:{song_id}"
    else:
        identity = f"observation:{observed_title}\0{observed_artist}"
    material = f"{FEATURE_EXTRACTOR_VERSION}:{source_hash}:{identity}"
    return hashlib.sha256(material.encode()).hexdigest()


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
    expected_master_version: str | None = None,
    expected_master_source_hash: str | None = None,
    expected_feature_extractor_version: str | None = None,
    expected_catalog_identity: str | None = None,
    expected_catalog_schema_version: int | None = None,
    expected_catalog_created_at: str | None = None,
    strict_capture_payload: bool = False,
) -> IngestResult:
    if catalog_schema_version(catalog_path) != CATALOG_SCHEMA_VERSION:
        raise ValueError("observation ingest requires catalog schema version 1")
    master = load_master_identity(master_db)
    if expected_master_version is not None and master.version != expected_master_version:
        raise ValueError("master version drift detected during observation ingest")
    if expected_master_source_hash is not None and (
        master.source_hash != expected_master_source_hash
    ):
        raise ValueError("master source hash drift detected during observation ingest")
    if expected_feature_extractor_version is not None and (
        expected_feature_extractor_version != FEATURE_EXTRACTOR_VERSION
    ):
        raise ValueError("feature extractor version drift detected during observation ingest")
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
    timestamp = now or utc_now()
    capture_id = source_capture_id.strip() or None
    resolved_song_id = None if resolution.song is None else resolution.song.song_id
    reference_id = _reference_id(
        source_hash,
        capture_id=capture_id,
        song_id=resolved_song_id,
        observed_title=observed_title,
        observed_artist=observed_artist,
    )
    with closing(_connect_read_write(catalog_path)) as connection, connection:
        connection.execute("BEGIN IMMEDIATE")
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        write_schema_version = _validate_catalog(connection)
        if write_schema_version != CATALOG_SCHEMA_VERSION:
            raise ValueError("observation ingest requires catalog schema version 1")
        write_metadata = dict(
            connection.execute("SELECT key, value FROM catalog_metadata")
        )
        if expected_catalog_identity is not None and (
            write_metadata["catalog_identity"] != expected_catalog_identity
        ):
            raise ValueError("catalog identity drift detected during observation ingest")
        if expected_catalog_schema_version is not None and (
            write_schema_version != expected_catalog_schema_version
        ):
            raise ValueError("catalog schema drift detected during observation ingest")
        if expected_catalog_created_at is not None and (
            write_metadata["created_at"] != expected_catalog_created_at
        ):
            raise ValueError("catalog file identity drift detected during observation ingest")
        current_master = load_master_identity(master_db)
        if (
            current_master.version != master.version
            or current_master.source_hash != master.source_hash
        ):
            raise ValueError("master drift detected during observation ingest transaction")
        row = None
        if capture_id is not None:
            row = connection.execute(
                """
                SELECT *
                FROM jacket_references
                WHERE source_capture_id = ? AND feature_extractor_version = ?
                """,
                (capture_id, FEATURE_EXTRACTOR_VERSION),
            ).fetchone()
            if row is not None and str(row["source_image_hash"]) != source_hash:
                raise ValueError("source_capture_id was already used with different image bytes")
            if row is not None and strict_capture_payload and (
                observed_title != str(row["observed_title"])
                or observed_artist != str(row["observed_artist"])
                or observation_status != str(row["observation_status"])
                or image_kind != str(row["image_kind"])
                or expected_song_id != str(row["expected_song_id"])
            ):
                raise ValueError(
                    "source_capture_id was already used with different observation payload"
                )
        if row is None and not strict_capture_payload and resolved_song_id is not None:
            row = connection.execute(
                """
                SELECT *
                FROM jacket_references
                WHERE source_image_hash = ? AND feature_extractor_version = ?
                  AND song_id = ?
                """,
                (source_hash, FEATURE_EXTRACTOR_VERSION, resolved_song_id),
            ).fetchone()
        if row is None and not strict_capture_payload and resolved_song_id is None:
            row = connection.execute(
                """
                SELECT *
                FROM jacket_references
                WHERE source_image_hash = ? AND feature_extractor_version = ?
                  AND song_id IS NULL
                  AND observed_title = ? AND observed_artist = ?
                """,
                (
                    source_hash,
                    FEATURE_EXTRACTOR_VERSION,
                    observed_title,
                    observed_artist,
                ),
            ).fetchone()
        if row is not None:
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


def ingest_observation_v2(
    catalog_path: Path,
    master_db: Path,
    *,
    source_image_path: Path,
    observation_id: str,
    observed_title: str = "",
    observed_artist: str = "",
    observation_status: str = "unresolved",
    expected_song_id: str = "",
    image_kind: str = "jacket_crop",
    expected_image_hash: str | None = None,
    now: str | None = None,
    expected_master_version: str | None = None,
    expected_master_source_hash: str | None = None,
    expected_feature_extractor_version: str | None = None,
    expected_catalog_identity: str | None = None,
    expected_catalog_schema_version: int | None = None,
    expected_catalog_created_at: str | None = None,
    jacket_feature_version: str | None = None,
    jacket_feature_hash: str | None = None,
    title_line_feature_version: str | None = None,
    title_line_hash: str | None = None,
    composite_identity_version: str | None = None,
    expected_composite_identity_hash: str | None = None,
) -> IngestResult:
    """Ingest one unresolved collector observation into an existing v2/v3 catalog.

    The v2 path deliberately does not call the v1 resolver or create candidates.
    ``source_capture_id`` is the persisted observation ID because the v2 schema
    already provides its strict uniqueness boundary for the collector artifact.
    """
    if not observation_id or observation_id != observation_id.strip():
        raise ValueError("observation id must be non-empty")
    if observed_title != "" or observed_artist != "":
        raise ValueError("v2 observation ingest requires empty observed title and artist")
    if observation_status != "unresolved":
        raise ValueError("v2 observation ingest requires observation_status=unresolved")
    if expected_song_id is None:
        raise ValueError("expected song id must not be null")
    if image_kind not in {"full_frame", "jacket_crop"}:
        raise ValueError(f"unsupported image_kind: {image_kind}")
    if expected_feature_extractor_version is not None and (
        expected_feature_extractor_version != FEATURE_EXTRACTOR_VERSION
    ):
        raise ValueError("feature extractor version drift detected during v2 observation ingest")
    schema_version = catalog_schema_version(catalog_path)
    if schema_version not in {CATALOG_SCHEMA_VERSION_V2, CATALOG_SCHEMA_VERSION_V3}:
        raise ValueError("versioned observation ingest requires catalog schema version 2 or 3")
    if expected_catalog_schema_version is not None and (
        expected_catalog_schema_version != schema_version
    ):
        raise ValueError("catalog schema drift detected during versioned observation ingest")
    identity_values = (
        jacket_feature_version,
        jacket_feature_hash,
        title_line_feature_version,
        title_line_hash,
        composite_identity_version,
        expected_composite_identity_hash,
    )
    if schema_version == CATALOG_SCHEMA_VERSION_V2:
        if any(value is not None for value in identity_values):
            raise ValueError("catalog schema version 2 does not accept composite identity")
    else:
        if any(value is None for value in identity_values):
            raise ValueError("catalog schema version 3 requires composite identity")
        assert all(value is not None for value in identity_values)
        if (
            composite_identity_version != COMPOSITE_IDENTITY_VERSION
            or composite_identity_hash(
                jacket_feature_version,
                jacket_feature_hash,
                title_line_feature_version,
                title_line_hash,
            )
            != expected_composite_identity_hash
        ):
            raise ValueError("observation composite identity is invalid")
    if not source_image_path.is_file():
        raise ValueError(f"source image does not exist: {source_image_path}")
    image_bytes = source_image_path.read_bytes()
    if not image_bytes:
        raise ValueError("observation artifact image is empty")
    source_hash = hashlib.sha256(image_bytes).hexdigest()
    if expected_image_hash is not None and source_hash != expected_image_hash:
        raise ValueError("observation artifact image hash does not match its checkpoint")
    try:
        feature = _extract_feature_from_bytes(image_bytes, image_kind)
    except (OSError, ValueError) as exc:
        raise ValueError("observation artifact image is invalid") from exc

    master = load_master_identity(master_db)
    if expected_master_version is not None and master.version != expected_master_version:
        raise ValueError("master version drift detected during v2 observation ingest")
    if expected_master_source_hash is not None and (
        master.source_hash != expected_master_source_hash
    ):
        raise ValueError("master source hash drift detected during v2 observation ingest")
    reference_id = _reference_id(
        source_hash,
        capture_id=observation_id,
        song_id=None,
        observed_title="",
        observed_artist="",
    )
    timestamp = now or utc_now()
    with closing(_connect_read_write(catalog_path)) as connection, connection:
        connection.execute("BEGIN IMMEDIATE")
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        write_schema_version = _validate_catalog(connection)
        if write_schema_version != schema_version:
            raise ValueError("catalog schema drift detected during versioned observation ingest")
        write_metadata = dict(connection.execute("SELECT key, value FROM catalog_metadata"))
        if expected_catalog_identity is not None and (
            write_metadata["catalog_identity"] != expected_catalog_identity
        ):
            raise ValueError("catalog identity drift detected during v2 observation ingest")
        if expected_catalog_created_at is not None and (
            write_metadata["created_at"] != expected_catalog_created_at
        ):
            raise ValueError("catalog file identity drift detected during v2 observation ingest")
        current_master = load_master_identity(master_db)
        if (
            current_master.version != master.version
            or current_master.source_hash != master.source_hash
        ):
            raise ValueError("master drift detected during v2 observation ingest transaction")

        row = connection.execute(
            "SELECT * FROM jacket_references WHERE source_capture_id = ?",
            (observation_id,),
        ).fetchone()
        if row is not None:
            same_payload = (
                str(row["source_image_hash"]) == source_hash
                and str(row["master_version"]) == master.version
                and str(row["feature_extractor_version"]) == FEATURE_EXTRACTOR_VERSION
                and str(row["image_kind"]) == image_kind
                and str(row["observed_title"]) == ""
                and str(row["observed_artist"]) == ""
                and str(row["observation_status"]) == "unresolved"
                and str(row["expected_song_id"]) == expected_song_id
            )
            if schema_version == CATALOG_SCHEMA_VERSION_V3:
                same_payload = same_payload and tuple(
                    row[column]
                    for column in (
                        "jacket_feature_version",
                        "jacket_feature_hash",
                        "title_line_feature_version",
                        "title_line_hash",
                        "composite_identity_version",
                        "composite_identity_hash",
                    )
                ) == identity_values
            if not same_payload:
                raise ValueError(
                    "observation id was already used with different canonical payload"
                )
            candidate_count = connection.execute(
                "SELECT COUNT(*) FROM reference_candidates WHERE reference_id = ?",
                (str(row["reference_id"]),),
            ).fetchone()[0]
            history_count = connection.execute(
                "SELECT COUNT(*) FROM reference_review_history WHERE reference_id = ?",
                (str(row["reference_id"]),),
            ).fetchone()[0]
            if (
                str(row["review_status"]) != "unresolved"
                or row["song_id"] is not None
                or int(row["review_revision"]) != 0
                or row["manual_action_id"] is not None
                or str(row["manual_note"]) != ""
                or str(row["resolution_reason"]) != "observation_unresolved"
                or candidate_count != 0
                or history_count != 0
            ):
                raise ValueError(
                    "observation id collides with an existing reviewed catalog reference"
                )
            return IngestResult(
                str(row["reference_id"]),
                "existing",
                "unresolved",
                "observation_unresolved",
            )

        if schema_version == CATALOG_SCHEMA_VERSION_V3:
            existing_identity = connection.execute(
                "SELECT reference_id, review_status FROM jacket_references "
                "WHERE composite_identity_version = ? AND composite_identity_hash = ?",
                (composite_identity_version, expected_composite_identity_hash),
            ).fetchone()
            if existing_identity is not None:
                return IngestResult(
                    str(existing_identity["reference_id"]),
                    "existing",
                    str(existing_identity["review_status"]),
                    "composite_identity_already_cataloged",
                )

        if schema_version == CATALOG_SCHEMA_VERSION_V2:
            connection.execute(
                """
            INSERT INTO jacket_references (
              reference_id, source_capture_id, source_image_hash, master_version, song_id,
              canonical_title_snapshot, canonical_artist_snapshot, review_status,
              resolution_reason, resolution_basis, feature_extractor_version,
              image_kind, thumbnail_rgb_json, histogram_json, dhash_bits_json, dhash_hex,
              observed_title, observed_artist, observation_status, expected_song_id,
              created_at, updated_at, review_revision, manual_action_id, manual_note
            ) VALUES (?, ?, ?, ?, NULL, '', '', 'unresolved', 'observation_unresolved', '',
                      ?, ?, ?, ?, ?, ?, '', '', 'unresolved', ?, ?, ?, 0, NULL, '')
                """,
                (
                    reference_id,
                    observation_id,
                    source_hash,
                    master.version,
                    FEATURE_EXTRACTOR_VERSION,
                    image_kind,
                    _feature_json(feature.thumbnail),
                    _feature_json(feature.histogram),
                    _feature_json(feature.dhash_bits),
                    feature.dhash_hex,
                    expected_song_id,
                    timestamp,
                    timestamp,
                ),
            )
        else:
            connection.execute(
                """
                INSERT INTO jacket_references (
                  reference_id, source_capture_id, source_image_hash, master_version, song_id,
                  canonical_title_snapshot, canonical_artist_snapshot, review_status,
                  resolution_reason, resolution_basis, feature_extractor_version,
                  image_kind, thumbnail_rgb_json, histogram_json, dhash_bits_json, dhash_hex,
                  observed_title, observed_artist, observation_status, expected_song_id,
                  created_at, updated_at, review_revision, manual_action_id, manual_note,
                  jacket_feature_version, jacket_feature_hash, title_line_feature_version,
                  title_line_hash, composite_identity_version, composite_identity_hash
                ) VALUES (?, ?, ?, ?, NULL, '', '', 'unresolved', 'observation_unresolved', '',
                          ?, ?, ?, ?, ?, ?, '', '', 'unresolved', ?, ?, ?, 0, NULL, '',
                          ?, ?, ?, ?, ?, ?)
                """,
                (
                    reference_id,
                    observation_id,
                    source_hash,
                    master.version,
                    FEATURE_EXTRACTOR_VERSION,
                    image_kind,
                    _feature_json(feature.thumbnail),
                    _feature_json(feature.histogram),
                    _feature_json(feature.dhash_bits),
                    feature.dhash_hex,
                    expected_song_id,
                    timestamp,
                    timestamp,
                    *identity_values,
                ),
            )
    return IngestResult(reference_id, "created", "unresolved", "observation_unresolved")


def _decode_vector(raw: str | None, *, length: int, field_name: str) -> np.ndarray:
    if raw is None:
        raise ValueError(f"catalog reference is missing {field_name}")
    try:
        values = json.loads(raw)
    except (json.JSONDecodeError, TypeError, UnicodeDecodeError) as exc:
        raise ValueError(f"catalog reference has invalid {field_name}") from exc
    if not isinstance(values, list) or len(values) != length:
        raise ValueError(f"catalog reference has invalid {field_name} length")
    if any(isinstance(value, bool) or not isinstance(value, (int, float)) for value in values):
        raise ValueError(f"catalog reference has invalid {field_name} values")
    try:
        vector = np.asarray(values, dtype=np.float32)
    except (TypeError, ValueError, OverflowError) as exc:
        raise ValueError(f"catalog reference has invalid {field_name} values") from exc
    if not np.isfinite(vector).all() or ((vector < 0) | (vector > 1)).any():
        raise ValueError(f"catalog reference has out-of-range {field_name} values")
    return vector


def _decode_persisted_feature(row: sqlite3.Row) -> master_match.JacketFeature:
    dhash_hex = str(row["dhash_hex"])
    try:
        if len(dhash_hex) != 16:
            raise ValueError
        int(dhash_hex, 16)
    except ValueError as exc:
        raise ValueError("catalog reference has invalid dhash_hex") from exc
    return master_match.JacketFeature(
        thumbnail=_decode_vector(
            row["thumbnail_rgb_json"], length=768, field_name="thumbnail_rgb"
        ),
        histogram=_decode_vector(row["histogram_json"], length=24, field_name="histogram"),
        dhash_bits=_decode_vector(
            row["dhash_bits_json"], length=64, field_name="dhash_bits"
        ),
        dhash_hex=dhash_hex,
    )


def validate_observation_session(
    catalog_path: Path,
    master_db: Path,
    *,
    expected_catalog_identity: str,
    expected_catalog_schema_version: int,
    expected_catalog_created_at: str,
    expected_master_version: str,
    expected_master_source_hash: str,
    expected_feature_extractor_version: str,
) -> dict[str, str | int]:
    with closing(_connect_read_only(catalog_path)) as connection:
        schema_version = _validate_catalog(connection)
        catalog_created_at = str(
            connection.execute(
                "SELECT value FROM catalog_metadata WHERE key = 'created_at'"
            ).fetchone()[0]
        )
    if expected_catalog_identity != CATALOG_IDENTITY:
        raise ValueError("catalog identity drift detected for observation session")
    if schema_version != expected_catalog_schema_version:
        raise ValueError("catalog schema drift detected for observation session")
    if catalog_created_at != expected_catalog_created_at:
        raise ValueError("catalog file identity drift detected for observation session")
    master = load_master_identity(master_db)
    if master.version != expected_master_version:
        raise ValueError("master version drift detected for observation session")
    if master.source_hash != expected_master_source_hash:
        raise ValueError("master source hash drift detected for observation session")
    if expected_feature_extractor_version != FEATURE_EXTRACTOR_VERSION:
        raise ValueError("feature extractor version drift detected for observation session")
    return {
        "catalog_identity": CATALOG_IDENTITY,
        "catalog_schema_version": schema_version,
        "catalog_created_at": catalog_created_at,
        "master_version": master.version,
        "master_source_hash": master.source_hash,
        "feature_extractor_version": FEATURE_EXTRACTOR_VERSION,
    }


def validate_observation_receipt(
    catalog_path: Path,
    *,
    observation_id: str,
    catalog_status: str,
    catalog_reference_id: str | None,
    jacket_crop_hash: str,
    expected_feature_extractor_version: str,
    expected_catalog_schema_version: int,
    expected_catalog_created_at: str,
    composite_identity_version: str | None = None,
    composite_identity_hash_value: str | None = None,
) -> dict[str, str | int]:
    with closing(_connect_read_only(catalog_path)) as connection:
        connection.row_factory = sqlite3.Row
        schema_version = _validate_catalog(connection)
        created_at = str(
            connection.execute(
                "SELECT value FROM catalog_metadata WHERE key = 'created_at'"
            ).fetchone()[0]
        )
        if (
            schema_version != expected_catalog_schema_version
            or created_at != expected_catalog_created_at
        ):
            raise ValueError("catalog receipt identity drift detected")
        if expected_feature_extractor_version != FEATURE_EXTRACTOR_VERSION:
            raise ValueError("catalog receipt extractor drift detected")
        if catalog_status == "pending":
            if catalog_reference_id is not None:
                raise ValueError("pending catalog receipt must not have a reference id")
        elif catalog_status == "deferred":
            if schema_version != CATALOG_SCHEMA_VERSION_V2 or catalog_reference_id is not None:
                raise ValueError("deferred catalog receipt is incompatible")
        elif catalog_status == "ingested":
            if (
                schema_version
                not in {
                    CATALOG_SCHEMA_VERSION,
                    CATALOG_SCHEMA_VERSION_V2,
                    CATALOG_SCHEMA_VERSION_V3,
                }
                or not catalog_reference_id
            ):
                raise ValueError("ingested catalog receipt is incompatible")
            row = connection.execute(
                "SELECT * FROM jacket_references WHERE reference_id = ?",
                (catalog_reference_id,),
            ).fetchone()
            if row is None or str(row["feature_extractor_version"]) != (
                expected_feature_extractor_version
            ):
                raise ValueError("ingested catalog receipt does not match its observation")
            if schema_version == CATALOG_SCHEMA_VERSION_V3:
                if (
                    composite_identity_version != COMPOSITE_IDENTITY_VERSION
                    or not _is_sha256(composite_identity_hash_value)
                    or row["composite_identity_version"] != composite_identity_version
                    or row["composite_identity_hash"] != composite_identity_hash_value
                ):
                    raise ValueError("ingested catalog receipt composite identity mismatch")
            elif (
                composite_identity_version is not None
                or composite_identity_hash_value is not None
            ):
                raise ValueError("legacy catalog receipt contains composite identity")
            if schema_version != CATALOG_SCHEMA_VERSION_V3 and (
                str(row["source_capture_id"]) != observation_id
                or str(row["source_image_hash"]) != jacket_crop_hash
                or str(row["observed_title"]) != ""
                or str(row["observed_artist"]) != ""
                or str(row["observation_status"]) != "unresolved"
                or (
                    schema_version == CATALOG_SCHEMA_VERSION
                    and str(row["review_status"]) != "unresolved"
                )
            ):
                raise ValueError("ingested catalog receipt does not match its observation")
        else:
            raise ValueError("unsupported catalog receipt status")
    return {
        "catalog_status": catalog_status,
        "catalog_schema_version": schema_version,
        "catalog_created_at": created_at,
    }


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
    if str(row["feature_extractor_version"]) != FEATURE_EXTRACTOR_VERSION:
        return "needs_review", "feature_extractor_version_changed"
    review_status = str(row["review_status"])
    if review_status == "manual_confirmed":
        try:
            _decode_persisted_feature(row)
        except ValueError:
            return "needs_review", "persisted_feature_invalid"
        return review_status, str(row["resolution_reason"])
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
        for row in connection.execute(
            """
            SELECT *
            FROM jacket_references
            WHERE feature_extractor_version = ?
            ORDER BY reference_id
            """,
            (FEATURE_EXTRACTOR_VERSION,),
        ):
            state, _ = _reference_state(row, master)
            if state not in {"auto_confirmed", "manual_confirmed"}:
                continue
            song = songs_by_id[str(row["song_id"])]
            try:
                feature = _decode_persisted_feature(row)
            except ValueError:
                if state == "manual_confirmed":
                    continue
                raise
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
    schema_version = catalog_schema_version(catalog_path)
    master = load_master_identity(master_db)
    gp_songs = [song for song in master.songs if song.grand_prix_play_available]
    gp_song_ids = {song.song_id for song in gp_songs}
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
        candidate_song_ids_by_reference: dict[str, set[str]] = {}
        for candidate in connection.execute(
            "SELECT reference_id, song_id FROM reference_candidates"
        ):
            candidate_song_ids_by_reference.setdefault(
                str(candidate["reference_id"]), set()
            ).add(str(candidate["song_id"]))
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
                candidate_song_ids = candidate_song_ids_by_reference.get(
                    str(row["reference_id"]), set()
                ) & gp_song_ids
                if candidate_song_ids:
                    for candidate_song_id in candidate_song_ids:
                        references_by_song.setdefault(candidate_song_id, []).append(
                            (state, reason)
                        )
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
        if any(state in {"auto_confirmed", "manual_confirmed"} for state, _ in states):
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
        "feature_extractor_version_changed": (
            "re-extract the reference with the current feature extractor"
        ),
        "persisted_feature_invalid": (
            "recapture or re-extract the manually confirmed reference"
        ),
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
        "schema_version": schema_version,
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
    migrate = subparsers.add_parser("migrate-v2", help="Copy a v1 catalog to a new v2 catalog.")
    migrate.add_argument("--source-catalog", type=Path, required=True)
    migrate.add_argument("--target-catalog", type=Path, required=True)
    migrate_v3 = subparsers.add_parser(
        "migrate-v3",
        help="Copy a v2 catalog to v3 and backfill verified local observation artifacts.",
    )
    migrate_v3.add_argument("--source-catalog", type=Path, required=True)
    migrate_v3.add_argument("--target-catalog", type=Path, required=True)
    migrate_v3.add_argument("--artifact-root", type=Path, required=True)
    review = subparsers.add_parser("review", help="Apply one explicit v2 manual review action.")
    review.add_argument("--catalog", type=Path, required=True)
    review.add_argument("--master-db", type=Path, required=True)
    review.add_argument("--action-id", required=True)
    review.add_argument("--reference-id", required=True)
    review.add_argument("--action", choices=REVIEW_ACTIONS, required=True)
    review.add_argument("--expected-revision", type=int, required=True)
    review.add_argument("--expected-status", choices=REVIEW_STATUSES_V2, required=True)
    review.add_argument("--expected-song-id", default="")
    review.add_argument("--song-id", default="")
    review.add_argument("--reason", default="")
    review.add_argument("--note", default="")
    ingest = subparsers.add_parser(
        "ingest", help="Ingest one developer-only observation into catalog v1."
    )
    ingest.add_argument("--catalog", type=Path, required=True)
    ingest.add_argument("--master-db", type=Path, required=True)
    ingest.add_argument("--source-image", type=Path, required=True)
    ingest.add_argument("--source-capture-id", required=True)
    ingest.add_argument("--observed-title", default="")
    ingest.add_argument("--observed-artist", default="")
    ingest.add_argument("--observation-status", default="unresolved")
    ingest.add_argument(
        "--image-kind", choices=("full_frame", "jacket_crop"), default="jacket_crop"
    )
    ingest.add_argument("--expected-master-version", required=True)
    ingest.add_argument("--expected-master-source-hash", required=True)
    ingest.add_argument("--expected-feature-extractor-version", required=True)
    ingest.add_argument("--expected-catalog-identity", required=True)
    ingest.add_argument("--expected-catalog-schema-version", type=int, required=True)
    ingest.add_argument("--expected-catalog-created-at", required=True)
    ingest_v2 = subparsers.add_parser(
        "ingest-v2", help="Ingest one unresolved developer observation into catalog v2."
    )
    ingest_v2.add_argument("--catalog", type=Path, required=True)
    ingest_v2.add_argument("--master-db", type=Path, required=True)
    ingest_v2.add_argument("--source-image", type=Path, required=True)
    ingest_v2.add_argument("--observation-id", required=True)
    ingest_v2.add_argument("--observed-title", default="")
    ingest_v2.add_argument("--observed-artist", default="")
    ingest_v2.add_argument("--observation-status", default="unresolved")
    ingest_v2.add_argument("--expected-song-id", default="")
    ingest_v2.add_argument(
        "--image-kind", choices=("full_frame", "jacket_crop"), default="jacket_crop"
    )
    ingest_v2.add_argument("--expected-image-hash", required=True)
    ingest_v2.add_argument("--expected-master-version", required=True)
    ingest_v2.add_argument("--expected-master-source-hash", required=True)
    ingest_v2.add_argument("--expected-feature-extractor-version", required=True)
    ingest_v2.add_argument("--expected-catalog-identity", required=True)
    ingest_v2.add_argument("--expected-catalog-schema-version", type=int, required=True)
    ingest_v2.add_argument("--expected-catalog-created-at", required=True)
    ingest_v2.add_argument("--jacket-feature-version")
    ingest_v2.add_argument("--jacket-feature-hash")
    ingest_v2.add_argument("--title-line-feature-version")
    ingest_v2.add_argument("--title-line-hash")
    ingest_v2.add_argument("--composite-identity-version")
    ingest_v2.add_argument("--composite-identity-hash")
    validate_session = subparsers.add_parser(
        "validate-session", help="Strictly validate a developer observation session identity."
    )
    validate_session.add_argument("--catalog", type=Path, required=True)
    validate_session.add_argument("--master-db", type=Path, required=True)
    validate_session.add_argument("--expected-catalog-identity", required=True)
    validate_session.add_argument("--expected-catalog-schema-version", type=int, required=True)
    validate_session.add_argument("--expected-catalog-created-at", required=True)
    validate_session.add_argument("--expected-master-version", required=True)
    validate_session.add_argument("--expected-master-source-hash", required=True)
    validate_session.add_argument("--expected-feature-extractor-version", required=True)
    validate_receipt = subparsers.add_parser(
        "validate-receipt", help="Validate one persisted observation catalog receipt."
    )
    validate_receipt.add_argument("--catalog", type=Path, required=True)
    validate_receipt.add_argument("--observation-id", required=True)
    validate_receipt.add_argument(
        "--catalog-status", choices=("pending", "ingested", "deferred"), required=True
    )
    validate_receipt.add_argument("--catalog-reference-id")
    validate_receipt.add_argument("--jacket-crop-hash", required=True)
    validate_receipt.add_argument("--expected-feature-extractor-version", required=True)
    validate_receipt.add_argument("--expected-catalog-schema-version", type=int, required=True)
    validate_receipt.add_argument("--expected-catalog-created-at", required=True)
    validate_receipt.add_argument("--composite-identity-version")
    validate_receipt.add_argument("--composite-identity-hash")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "migrate-v2":
        migrate_catalog_v1_to_v2(args.source_catalog, args.target_catalog)
        print(
            json.dumps(
                {
                    "status": "migrated",
                    "source_catalog": str(args.source_catalog.resolve()),
                    "target_catalog": str(args.target_catalog.resolve()),
                    "schema_version": CATALOG_SCHEMA_VERSION_V2,
                },
                ensure_ascii=False,
                separators=(",", ":"),
            )
        )
        return 0
    if args.command == "migrate-v3":
        receipt = migrate_catalog_v2_to_v3(
            args.source_catalog, args.target_catalog, args.artifact_root
        )
        print(json.dumps(receipt, ensure_ascii=False, separators=(",", ":")))
        return 0
    if args.command == "review":
        receipt = apply_review_mutation(
            args.catalog,
            args.master_db,
            ReviewMutationRequest(
                action_id=args.action_id,
                reference_id=args.reference_id,
                action=args.action,
                expected_revision=args.expected_revision,
                expected_status=args.expected_status,
                expected_song_id=args.expected_song_id or None,
                song_id=args.song_id or None,
                reason=args.reason,
                note=args.note,
            ),
        )
        print(json.dumps(receipt.__dict__, ensure_ascii=False, separators=(",", ":")))
        return 0
    if args.command == "ingest":
        ensure_data_path(args.catalog, argument_name="--catalog")
        ensure_data_file_path(args.source_image, argument_name="--source-image")
        result = ingest_observation(
            args.catalog,
            args.master_db,
            source_image_path=args.source_image,
            source_capture_id=args.source_capture_id,
            observed_title=args.observed_title,
            observed_artist=args.observed_artist,
            observation_status=args.observation_status,
            image_kind=args.image_kind,
            expected_master_version=args.expected_master_version,
            expected_master_source_hash=args.expected_master_source_hash,
            expected_feature_extractor_version=args.expected_feature_extractor_version,
            expected_catalog_identity=args.expected_catalog_identity,
            expected_catalog_schema_version=args.expected_catalog_schema_version,
            expected_catalog_created_at=args.expected_catalog_created_at,
            strict_capture_payload=True,
        )
        print(json.dumps(result.__dict__, ensure_ascii=False, separators=(",", ":")))
        return 0
    if args.command == "ingest-v2":
        ensure_data_path(args.catalog, argument_name="--catalog")
        ensure_data_file_path(args.source_image, argument_name="--source-image")
        result = ingest_observation_v2(
            args.catalog,
            args.master_db,
            source_image_path=args.source_image,
            observation_id=args.observation_id,
            observed_title=args.observed_title,
            observed_artist=args.observed_artist,
            observation_status=args.observation_status,
            expected_song_id=args.expected_song_id,
            image_kind=args.image_kind,
            expected_image_hash=args.expected_image_hash,
            expected_master_version=args.expected_master_version,
            expected_master_source_hash=args.expected_master_source_hash,
            expected_feature_extractor_version=args.expected_feature_extractor_version,
            expected_catalog_identity=args.expected_catalog_identity,
            expected_catalog_schema_version=args.expected_catalog_schema_version,
            expected_catalog_created_at=args.expected_catalog_created_at,
            jacket_feature_version=args.jacket_feature_version,
            jacket_feature_hash=args.jacket_feature_hash,
            title_line_feature_version=args.title_line_feature_version,
            title_line_hash=args.title_line_hash,
            composite_identity_version=args.composite_identity_version,
            expected_composite_identity_hash=args.composite_identity_hash,
        )
        print(json.dumps(result.__dict__, ensure_ascii=False, separators=(",", ":")))
        return 0
    if args.command == "validate-session":
        ensure_data_path(args.catalog, argument_name="--catalog")
        result = validate_observation_session(
            args.catalog,
            args.master_db,
            expected_catalog_identity=args.expected_catalog_identity,
            expected_catalog_schema_version=args.expected_catalog_schema_version,
            expected_catalog_created_at=args.expected_catalog_created_at,
            expected_master_version=args.expected_master_version,
            expected_master_source_hash=args.expected_master_source_hash,
            expected_feature_extractor_version=args.expected_feature_extractor_version,
        )
        print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))
        return 0
    if args.command == "validate-receipt":
        ensure_data_path(args.catalog, argument_name="--catalog")
        result = validate_observation_receipt(
            args.catalog,
            observation_id=args.observation_id,
            catalog_status=args.catalog_status,
            catalog_reference_id=args.catalog_reference_id,
            jacket_crop_hash=args.jacket_crop_hash,
            expected_feature_extractor_version=args.expected_feature_extractor_version,
            expected_catalog_schema_version=args.expected_catalog_schema_version,
            expected_catalog_created_at=args.expected_catalog_created_at,
            composite_identity_version=args.composite_identity_version,
            composite_identity_hash_value=args.composite_identity_hash,
        )
        print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))
        return 0
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
