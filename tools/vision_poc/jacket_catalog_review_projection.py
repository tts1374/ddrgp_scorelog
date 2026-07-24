from __future__ import annotations

import argparse
import hashlib
import io
import json
import posixpath
import sqlite3
import uuid
import xml.etree.ElementTree as ET
import zipfile
from contextlib import closing
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from PIL import Image

from tools.ddrworld_snapshot_evaluation.xlsx_export import EmbeddedImage, write_xlsx
from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import title_artist_evaluation, unresolved_candidate_evaluation

PROJECTION_SCHEMA_VERSION = 6
MANUAL_REVIEW_XLSX_SCHEMA_VERSION = "m5c-manual-review-xlsx-v1"
MANUAL_REVIEW_XLSX_HEADERS = [
    "observation_id",
    "title_roi",
    "artist_roi",
    "status",
    "truth_song_id",
    "notes",
]
MANUAL_REVIEW_IMPORT_FIELDS = ("observation_id", "status", "truth_song_id", "notes")
MANUAL_REVIEW_METADATA_KEYS = (
    "schema_version",
    "export_id",
    "catalog_version",
    "master_version",
    "exported_at",
    "target_count",
)
UNREFLECTED_REVIEW_STATUSES = frozenset({"needs_review", "unresolved"})
MANUAL_REVIEW_DRAFT_STATUSES = frozenset({"unreviewed", "confirmed", "rejected", "hold"})
ROI_WIDTH_CM = 12.0
XLSX_MAIN_NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
XLSX_OFFICE_REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
XLSX_PACKAGE_REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"
XLSX_NS = {
    "main": XLSX_MAIN_NS,
    "office_rel": XLSX_OFFICE_REL_NS,
    "package_rel": XLSX_PACKAGE_REL_NS,
}


def _database_fingerprint(path: Path) -> tuple[int, int, int, str]:
    stat = path.stat()
    return (
        stat.st_ino,
        stat.st_size,
        stat.st_mtime_ns,
        hashlib.sha256(path.read_bytes()).hexdigest(),
    )


def _read_master_metadata(path: Path) -> dict[str, str]:
    with closing(
        sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=ro", uri=True)
    ) as connection:
        return dict(connection.execute("SELECT key, value FROM master_metadata"))


def _candidate_projection(row: sqlite3.Row, songs_by_id: dict[str, Any]) -> dict[str, Any]:
    song_id = str(row["song_id"])
    song = songs_by_id.get(song_id)
    return {
        "song_id": song_id,
        "title": None if song is None else song.title,
        "artist": None if song is None else song.artist,
        "reason": str(row["candidate_reason"]),
        "master_song_missing": song is None,
    }


def build_review_projection(
    catalog_path: Path,
    master_db: Path,
    *,
    artifact_root: Path | None = None,
    extractor: title_artist_evaluation.Extractor = title_artist_evaluation.extract_field,
) -> dict[str, Any]:
    initial_catalog_fingerprint = _database_fingerprint(catalog_path)
    initial_master_fingerprint = _database_fingerprint(master_db)
    catalog.validate_catalog(catalog_path)
    master = catalog.load_master_identity(master_db)
    coverage_rows, coverage_summary = catalog.build_coverage(catalog_path, master_db)
    master_metadata = _read_master_metadata(master_db)
    songs_by_id = {song.song_id: song for song in master.songs}
    aliases_by_song: dict[str, list[str]] = {}
    for song_id, alias_title, _alias_artist in master.aliases:
        if alias_title and alias_title not in aliases_by_song.setdefault(song_id, []):
            aliases_by_song[song_id].append(alias_title)
    for row in coverage_rows:
        row["aliases"] = aliases_by_song.get(str(row["song_id"]), [])

    with closing(catalog._connect_read_only(catalog_path)) as connection:
        connection.row_factory = sqlite3.Row
        catalog._validate_catalog(connection)
        catalog_metadata = dict(connection.execute("SELECT key, value FROM catalog_metadata"))
        candidates_by_reference: dict[str, list[dict[str, Any]]] = {}
        for candidate_row in connection.execute(
            """
            SELECT reference_id, song_id, candidate_reason
            FROM reference_candidates
            ORDER BY reference_id, song_id
            """
        ):
            candidates_by_reference.setdefault(str(candidate_row["reference_id"]), []).append(
                _candidate_projection(candidate_row, songs_by_id)
            )

        history_by_reference: dict[str, list[dict[str, Any]]] = {}
        for history_row in connection.execute(
            "SELECT * FROM reference_review_history ORDER BY reference_id, history_id"
        ):
            history_by_reference.setdefault(str(history_row["reference_id"]), []).append(
                {
                    "action_id": str(history_row["action_id"]),
                    "action": str(history_row["action"]),
                    "before_status": str(history_row["before_status"]),
                    "after_status": str(history_row["after_status"]),
                    "before_song_id": history_row["before_song_id"],
                    "after_song_id": history_row["after_song_id"],
                    "reason": str(history_row["reason"]),
                    "note": str(history_row["note"]),
                    "action_at": str(history_row["action_at"]),
                    "before_revision": int(history_row["before_revision"]),
                    "after_revision": int(history_row["after_revision"]),
                }
            )
        reference_rows = [
            dict(row)
            for row in connection.execute("SELECT * FROM jacket_references ORDER BY reference_id")
        ]
        evaluation_root = artifact_root or Path("__artifact_root_not_configured__")
        evaluation_catalog_identity = title_artist_evaluation.CatalogIdentity(
            catalog_metadata["catalog_identity"],
            int(catalog_metadata["schema_version"]),
            catalog_metadata["created_at"],
        )
        candidate_evaluations = unresolved_candidate_evaluation.evaluate_references(
            reference_rows,
            artifact_root=evaluation_root,
            master=master,
            catalog_identity=evaluation_catalog_identity,
            extractor=extractor,
        )
        source_image_paths = unresolved_candidate_evaluation.resolve_source_image_paths(
            reference_rows,
            artifact_root=evaluation_root,
            master=master,
            catalog_identity=evaluation_catalog_identity,
        )
        review_references: list[dict[str, Any]] = []
        for row, candidate_evaluation, source_image_path in zip(
            reference_rows, candidate_evaluations, source_image_paths, strict=True
        ):
            status, reason = catalog._reference_state(row, master)
            assigned_song_id = str(row["song_id"] or "")
            assigned_song = songs_by_id.get(assigned_song_id)
            item = {
                "reference_id": str(row["reference_id"]),
                "review_status": status,
                "current_status": str(row["review_status"]),
                "current_song_id": row["song_id"],
                "reason": reason,
                "observed_title": str(row["observed_title"]),
                "observed_artist": str(row["observed_artist"]),
                "observation_status": str(row["observation_status"]),
                "master_drift": reason
                in {
                    "master_song_missing",
                    "song_not_grand_prix_available",
                    "master_identity_changed",
                    "master_version_changed",
                },
                "feature_extractor_version": str(row["feature_extractor_version"]),
                "assigned_song": None
                if not assigned_song_id
                else {
                    "song_id": assigned_song_id,
                    "title": None if assigned_song is None else assigned_song.title,
                    "artist": None if assigned_song is None else assigned_song.artist,
                    "master_song_missing": assigned_song is None,
                },
                "candidates": candidates_by_reference.get(str(row["reference_id"]), []),
                "candidate_evaluation": candidate_evaluation,
                "source_image_path": source_image_path,
            }
            item.update(
                {
                    "stored_status": str(row["review_status"]),
                    "revision": int(row["review_revision"]),
                    "manual_action_id": row["manual_action_id"],
                    "manual_note": str(row["manual_note"]),
                    "notes": str(row["manual_note"]),
                    "registered_route": str(row["resolution_basis"]),
                    "processed_at": str(row["updated_at"]),
                    "history": history_by_reference.get(str(row["reference_id"]), []),
                }
            )
            review_references.append(item)

    result = {
        "projection_schema_version": PROJECTION_SCHEMA_VERSION,
        "master": {
            "path": str(master_db.resolve()),
            "master_version": master.version,
            "source_hash": master_metadata["source_hash"],
            "song_count": int(master_metadata["song_count"]),
            "chart_count": int(master_metadata["chart_count"]),
            "grand_prix_song_count": coverage_summary["grand_prix_song_count"],
        },
        "catalog": {
            "path": str(catalog_path.resolve()),
            "catalog_identity": catalog_metadata["catalog_identity"],
            "schema_version": int(catalog_metadata["schema_version"]),
            "created_at": catalog_metadata["created_at"],
            "current_feature_extractor_version": catalog.FEATURE_EXTRACTOR_VERSION,
        },
        "coverage": {
            "grand_prix_song_count": coverage_summary["grand_prix_song_count"],
            "status_counts": coverage_summary["coverage_status_counts"],
            "orphaned_reference_count": coverage_summary["orphaned_reference_count"],
            "orphan_reason_counts": coverage_summary["orphan_reason_counts"],
            "unassigned_unresolved_observation_count": coverage_summary[
                "unassigned_unresolved_observation_count"
            ],
        },
        "songs": coverage_rows,
        "review_references": review_references,
    }
    if _database_fingerprint(catalog_path) != initial_catalog_fingerprint:
        raise ValueError("catalog changed while the read-only projection was generated")
    if _database_fingerprint(master_db) != initial_master_fingerprint:
        raise ValueError("master DB changed while the read-only projection was generated")
    return result


def _manual_review_targets(projection: dict[str, Any]) -> list[dict[str, Any]]:
    references = projection.get("review_references")
    if not isinstance(references, list):
        raise ValueError("review projection has no review references")
    targets: list[dict[str, Any]] = []
    seen_observation_ids: set[str] = set()
    for reference in references:
        if not isinstance(reference, dict):
            raise ValueError("review projection contains an invalid review reference")
        if reference.get("stored_status") not in UNREFLECTED_REVIEW_STATUSES:
            continue
        evaluation = reference.get("candidate_evaluation")
        observation_id = "" if not isinstance(evaluation, dict) else str(
            evaluation.get("observation_id") or ""
        )
        if not observation_id:
            raise ValueError("manual review target has no observation ID")
        if observation_id in seen_observation_ids:
            raise ValueError(
                f"manual review target has a duplicate observation ID: {observation_id}"
            )
        seen_observation_ids.add(observation_id)
        targets.append(
            {
                "observation_id": observation_id,
                "source_image_path": reference.get("source_image_path"),
                "notes": str(reference.get("notes") or ""),
                "reference_id": str(reference.get("reference_id") or ""),
            }
        )
    return sorted(
        targets,
        key=lambda item: (item["observation_id"], item["reference_id"]),
    )


def _embed_roi(
    source_path: Path | None,
    *,
    field: str,
    observation_id: str,
    image_name: str,
) -> EmbeddedImage:
    if source_path is None:
        raise ValueError(
            f"manual review observation has no validated source image: {observation_id}"
        )
    try:
        with Image.open(source_path) as source:
            source.load()
            box = title_artist_evaluation._scaled_roi(source, field)
            crop = source.crop(box).convert("RGB")
            payload = io.BytesIO()
            crop.save(payload, format="PNG")
            width, height = crop.size
    except (OSError, ValueError) as exc:
        raise ValueError(
            f"cannot create {field} ROI for manual review observation "
            f"{observation_id}: {exc}"
        ) from exc
    return EmbeddedImage(
        name=image_name,
        data=payload.getvalue(),
        width_cm=ROI_WIDTH_CM,
        height_cm=ROI_WIDTH_CM * height / width,
    )


def export_manual_review_xlsx(
    path: Path,
    projection: dict[str, Any],
    *,
    master_path: Path | None = None,
    export_id: str | None = None,
    exported_at: str | None = None,
) -> dict[str, Any]:
    """Export current unreflected review rows with their ROI images embedded."""
    if path.suffix.lower() != ".xlsx":
        raise ValueError("manual review XLSX output must be a .xlsx file")

    master_info = projection.get("master")
    catalog_info = projection.get("catalog")
    if not isinstance(master_info, dict) or not isinstance(catalog_info, dict):
        raise ValueError("review projection has incomplete master/catalog metadata")
    if master_path is None:
        raw_master_path = master_info.get("path")
        if not raw_master_path:
            raise ValueError("review projection has no master database path")
        master_path = Path(str(raw_master_path))
    master = catalog.load_master_identity(master_path)
    projected_master_version = str(master_info.get("master_version") or "")
    if projected_master_version != master.version:
        raise ValueError("master changed after the review projection was generated")

    targets = _manual_review_targets(projection)
    rows: list[list[Any]] = []
    for index, target in enumerate(targets, start=1):
        source_path = (
            None
            if not target["source_image_path"]
            else Path(str(target["source_image_path"]))
        )
        rows.append(
            [
                target["observation_id"],
                _embed_roi(
                    source_path,
                    field="title",
                    observation_id=target["observation_id"],
                    image_name=f"Pictures/{index:04d}-title.png",
                ),
                _embed_roi(
                    source_path,
                    field="artist",
                    observation_id=target["observation_id"],
                    image_name=f"Pictures/{index:04d}-artist.png",
                ),
                "unreviewed",
                None,
                target["notes"],
            ]
        )

    metadata = [
        ["schema_version", MANUAL_REVIEW_XLSX_SCHEMA_VERSION],
        ["export_id", export_id or uuid.uuid4().hex],
        ["catalog_version", str(catalog_info.get("schema_version") or "")],
        ["master_version", master.version],
        ["exported_at", exported_at or datetime.now(UTC).isoformat(timespec="seconds")],
        ["target_count", len(rows)],
    ]
    write_xlsx(
        path,
        [
            ("Manual Review", MANUAL_REVIEW_XLSX_HEADERS, rows),
            (
                "Master Songs",
                ["song_id", "title", "artist"],
                [[song.song_id, song.title, song.artist] for song in master.songs],
            ),
            ("Metadata", ["key", "value"], metadata),
        ],
    )
    return {key: value for key, value in metadata}


export_manual_xlsx = export_manual_review_xlsx


def _xlsx_part_path(base_path: str, target: str) -> str:
    target = target.replace("\\", "/")
    part_path = target.lstrip("/") if target.startswith("/") else posixpath.normpath(
        posixpath.join(posixpath.dirname(base_path), target)
    )
    if part_path in {"", ".", ".."} or part_path.startswith("../"):
        raise ValueError(f"manual review XLSX relationship escapes the package: {target}")
    return part_path


def _xlsx_xml(archive: zipfile.ZipFile, part_path: str) -> ET.Element:
    try:
        payload = archive.read(part_path)
    except KeyError as exc:
        raise ValueError(f"manual review XLSX is missing package part: {part_path}") from exc
    try:
        return ET.fromstring(payload)
    except ET.ParseError as exc:
        raise ValueError(f"manual review XLSX contains invalid XML: {part_path}") from exc


def _xlsx_column_index(reference: str) -> int:
    letters = "".join(
        character for character in reference if "A" <= character.upper() <= "Z"
    )
    if not letters:
        raise ValueError(f"manual review XLSX cell reference is invalid: {reference}")
    result = 0
    for character in letters:
        result = result * 26 + ord(character.upper()) - ord("A") + 1
    return result - 1


def _xlsx_shared_strings(archive: zipfile.ZipFile) -> list[str]:
    if "xl/sharedStrings.xml" not in archive.namelist():
        return []
    root = _xlsx_xml(archive, "xl/sharedStrings.xml")
    return ["".join(item.itertext()) for item in root.findall("main:si", XLSX_NS)]


def _xlsx_cell_value(
    cell: ET.Element,
    shared_strings: list[str],
) -> str | None:
    cell_type = cell.attrib.get("t")
    if cell_type == "inlineStr":
        inline = cell.find("main:is", XLSX_NS)
        return "" if inline is None else "".join(inline.itertext())
    value = cell.find("main:v", XLSX_NS)
    if value is None:
        return None
    raw = "".join(value.itertext())
    if cell_type == "s":
        try:
            return shared_strings[int(raw)]
        except (IndexError, ValueError) as exc:
            raise ValueError(f"manual review XLSX shared string index is invalid: {raw}") from exc
    return raw


def _xlsx_sheet_rows(
    archive: zipfile.ZipFile,
    part_path: str,
    shared_strings: list[str],
) -> list[tuple[int, list[str | None]]]:
    root = _xlsx_xml(archive, part_path)
    rows: list[tuple[int, list[str | None]]] = []
    for fallback_row_number, row in enumerate(root.findall("main:sheetData/main:row", XLSX_NS), 1):
        values: dict[int, str | None] = {}
        for cell in row.findall("main:c", XLSX_NS):
            reference = cell.attrib.get("r")
            if reference is None:
                raise ValueError(f"manual review XLSX cell in {part_path} has no reference")
            column_index = _xlsx_column_index(reference)
            if column_index in values:
                raise ValueError(
                    f"manual review XLSX row has duplicate cell reference: {reference}"
                )
            values[column_index] = _xlsx_cell_value(cell, shared_strings)
        if not values:
            continue
        row_number = int(row.attrib.get("r", fallback_row_number))
        rows.append((row_number, [values.get(index) for index in range(max(values) + 1)]))
    return rows


def _load_xlsx_sheets(
    path: Path,
) -> dict[str, list[tuple[int, list[str | None]]]]:
    try:
        archive = zipfile.ZipFile(path)
    except (OSError, zipfile.BadZipFile) as exc:
        raise ValueError(f"manual review XLSX cannot be opened: {path}") from exc
    with archive:
        workbook = _xlsx_xml(archive, "xl/workbook.xml")
        relationships = _xlsx_xml(archive, "xl/_rels/workbook.xml.rels")
        targets = {
            relationship.attrib["Id"]: relationship.attrib["Target"]
            for relationship in relationships.findall(
                f"{{{XLSX_PACKAGE_REL_NS}}}Relationship"
            )
            if "Id" in relationship.attrib and "Target" in relationship.attrib
        }
        shared_strings = _xlsx_shared_strings(archive)
        sheets: dict[str, list[tuple[int, list[str | None]]]] = {}
        for sheet in workbook.findall("main:sheets/main:sheet", XLSX_NS):
            name = sheet.attrib.get("name")
            relationship_id = sheet.attrib.get(f"{{{XLSX_OFFICE_REL_NS}}}id")
            if not name or not relationship_id or relationship_id not in targets:
                raise ValueError("manual review XLSX has an invalid worksheet relationship")
            if name in sheets:
                raise ValueError(f"manual review XLSX has duplicate sheet: {name}")
            part_path = _xlsx_part_path("xl/workbook.xml", targets[relationship_id])
            sheets[name] = _xlsx_sheet_rows(archive, part_path, shared_strings)
        return sheets


def _xlsx_text(value: str | None) -> str:
    return "" if value is None else value


def _xlsx_header_positions(
    sheets: dict[str, list[tuple[int, list[str | None]]]],
    sheet_name: str,
    required_columns: tuple[str, ...],
) -> tuple[int, dict[str, int]]:
    rows = sheets.get(sheet_name)
    if rows is None:
        raise ValueError(f"manual review XLSX is missing sheet: {sheet_name}")
    if not rows:
        raise ValueError(f"manual review XLSX sheet is empty: {sheet_name}")
    row_number, header = rows[0]
    positions: dict[str, int] = {}
    for index, value in enumerate(header):
        column = _xlsx_text(value)
        if not column:
            continue
        if column in positions:
            raise ValueError(
                f"manual review XLSX {sheet_name} row {row_number} has duplicate column: {column}"
            )
        positions[column] = index
    missing = [column for column in required_columns if column not in positions]
    if missing:
        raise ValueError(
            f"manual review XLSX {sheet_name} is missing required columns: {', '.join(missing)}"
        )
    return row_number, positions


def _xlsx_row_value(row: list[str | None], positions: dict[str, int], column: str) -> str | None:
    index = positions[column]
    return row[index] if index < len(row) else None


def _parse_manual_review_xlsx_metadata(
    sheets: dict[str, list[tuple[int, list[str | None]]]],
) -> tuple[dict[str, str], int]:
    _header_row, positions = _xlsx_header_positions(sheets, "Metadata", ("key", "value"))
    metadata: dict[str, str] = {}
    for row_number, row in sheets["Metadata"][1:]:
        key = _xlsx_text(_xlsx_row_value(row, positions, "key"))
        value = _xlsx_text(_xlsx_row_value(row, positions, "value"))
        if not key and not value:
            continue
        if not key:
            raise ValueError(f"manual review XLSX Metadata row {row_number} has no key")
        if key in metadata:
            raise ValueError(f"manual review XLSX Metadata has duplicate key: {key}")
        metadata[key] = value

    missing = [key for key in MANUAL_REVIEW_METADATA_KEYS if key not in metadata]
    if missing:
        raise ValueError(
            f"manual review XLSX Metadata is missing required keys: {', '.join(missing)}"
        )
    if metadata["schema_version"] != MANUAL_REVIEW_XLSX_SCHEMA_VERSION:
        raise ValueError(
            "manual review XLSX has unsupported schema version: "
            + metadata["schema_version"]
        )
    if any(not metadata[key] for key in MANUAL_REVIEW_METADATA_KEYS if key != "target_count"):
        raise ValueError("manual review XLSX Metadata contains an empty required value")
    try:
        target_count = int(metadata["target_count"])
    except ValueError as exc:
        raise ValueError(
            f"manual review XLSX Metadata target_count is invalid: {metadata['target_count']}"
        ) from exc
    if target_count < 0:
        raise ValueError("manual review XLSX Metadata target_count must not be negative")
    return metadata, target_count


def _manual_review_projection_observations(
    projection: dict[str, Any],
) -> dict[str, dict[str, Any]]:
    references = projection.get("review_references")
    if not isinstance(references, list):
        raise ValueError("review projection has no review references")
    observations: dict[str, dict[str, Any]] = {}
    for reference in references:
        if not isinstance(reference, dict):
            raise ValueError("review projection contains an invalid review reference")
        evaluation = reference.get("candidate_evaluation")
        observation_id = "" if not isinstance(evaluation, dict) else str(
            evaluation.get("observation_id") or ""
        )
        if not observation_id:
            continue
        if observation_id in observations:
            raise ValueError(
                f"review projection has a duplicate observation ID: {observation_id}"
            )
        observations[observation_id] = reference
    return observations


def _manual_review_xlsx_row_error(
    row_number: int,
    observation_id: str,
    reason: str,
) -> ValueError:
    observation = f" (observation={observation_id})" if observation_id else ""
    return ValueError(
        f"manual review XLSX row {row_number}{observation}: {reason}"
    )


def import_manual_review_xlsx(
    path: Path,
    projection: dict[str, Any],
    *,
    master_path: Path | None = None,
) -> dict[str, Any]:
    """Validate an edited Manual Review workbook and return draft payloads only."""
    if path.suffix.lower() != ".xlsx":
        raise ValueError("manual review XLSX input must be a .xlsx file")

    master_info = projection.get("master")
    if not isinstance(master_info, dict):
        raise ValueError("review projection has incomplete master metadata")
    if master_path is None:
        raw_master_path = master_info.get("path")
        if not raw_master_path:
            raise ValueError("review projection has no master database path")
        master_path = Path(str(raw_master_path))
    master = catalog.load_master_identity(master_path)

    sheets = _load_xlsx_sheets(path)
    _xlsx_header_positions(
        sheets,
        "Manual Review",
        MANUAL_REVIEW_IMPORT_FIELDS,
    )
    _xlsx_header_positions(sheets, "Master Songs", ("song_id", "title", "artist"))
    metadata, target_count = _parse_manual_review_xlsx_metadata(sheets)
    _header_row, positions = _xlsx_header_positions(
        sheets,
        "Manual Review",
        MANUAL_REVIEW_IMPORT_FIELDS,
    )
    current_observations = _manual_review_projection_observations(projection)
    master_song_ids = {song.song_id for song in master.songs}
    drafts: list[dict[str, Any]] = []
    seen_observation_ids: set[str] = set()
    for row_number, row in sheets["Manual Review"][1:]:
        if not any(_xlsx_text(value) for value in row):
            continue
        observation_id = _xlsx_text(_xlsx_row_value(row, positions, "observation_id"))
        if not observation_id:
            raise _manual_review_xlsx_row_error(
                row_number,
                "",
                "observation_id is required",
            )
        if observation_id in seen_observation_ids:
            raise _manual_review_xlsx_row_error(
                row_number,
                observation_id,
                "observation_id is duplicated",
            )
        seen_observation_ids.add(observation_id)
        reference = current_observations.get(observation_id)
        if reference is None:
            raise _manual_review_xlsx_row_error(
                row_number,
                observation_id,
                "observation_id is not present in the current projection",
            )
        if reference.get("stored_status") not in UNREFLECTED_REVIEW_STATUSES:
            raise _manual_review_xlsx_row_error(
                row_number,
                observation_id,
                "observation is not an unreflected manual-review target",
            )

        status = _xlsx_text(_xlsx_row_value(row, positions, "status"))
        truth_song_id = _xlsx_text(_xlsx_row_value(row, positions, "truth_song_id"))
        notes = _xlsx_text(_xlsx_row_value(row, positions, "notes"))
        if status not in MANUAL_REVIEW_DRAFT_STATUSES:
            raise _manual_review_xlsx_row_error(
                row_number,
                observation_id,
                f"status is invalid: {status}",
            )
        if status == "confirmed" and not truth_song_id:
            raise _manual_review_xlsx_row_error(
                row_number,
                observation_id,
                "confirmed status requires truth_song_id",
            )
        if truth_song_id and truth_song_id not in master_song_ids:
            raise _manual_review_xlsx_row_error(
                row_number,
                observation_id,
                f"truth_song_id is not present in the current Master: {truth_song_id}",
            )
        if status == "rejected" and truth_song_id:
            raise _manual_review_xlsx_row_error(
                row_number,
                observation_id,
                "rejected status requires an empty truth_song_id",
            )
        drafts.append(
            {
                "observation_id": observation_id,
                "status": status,
                "truth_song_id": truth_song_id or None,
                "notes": notes,
            }
        )

    if len(drafts) != target_count:
        raise ValueError(
            "manual review XLSX Manual Review data row count does not match Metadata "
            f"target_count: {len(drafts)} != {target_count}"
        )
    return {"metadata": metadata, "drafts": drafts}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Print the strict read-only jacket catalog review projection."
    )
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--master-db", required=True, type=Path)
    parser.add_argument("--artifact-root", type=Path)
    parser.add_argument("--report-output-dir", type=Path)
    manual_xlsx = parser.add_mutually_exclusive_group()
    manual_xlsx.add_argument("--manual-xlsx-output", type=Path)
    manual_xlsx.add_argument("--manual-xlsx-input", type=Path)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.report_output_dir is not None and args.artifact_root is None:
        raise ValueError("--report-output-dir requires --artifact-root")
    if args.artifact_root is not None:
        args.artifact_root = title_artist_evaluation._require_under_data(
            args.artifact_root, "artifact root"
        )
    if args.report_output_dir is not None:
        args.report_output_dir = title_artist_evaluation._require_under_data(
            args.report_output_dir, "candidate report output"
        )
    projection = build_review_projection(
        args.catalog,
        args.master_db,
        artifact_root=args.artifact_root,
    )
    if args.manual_xlsx_input is not None:
        imported = import_manual_review_xlsx(
            args.manual_xlsx_input,
            projection,
            master_path=args.master_db,
        )
        print(json.dumps(imported, ensure_ascii=False, separators=(",", ":")))
        return 0
    if args.report_output_dir is not None:
        unresolved_candidate_evaluation.write_reports(
            args.report_output_dir, projection["review_references"]
        )
    if args.manual_xlsx_output is not None:
        export_manual_review_xlsx(
            args.manual_xlsx_output,
            projection,
            master_path=args.master_db,
        )
    print(json.dumps(projection, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
