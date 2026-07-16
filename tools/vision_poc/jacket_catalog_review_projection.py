from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
from contextlib import closing
from pathlib import Path
from typing import Any

from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import title_artist_evaluation, unresolved_candidate_evaluation

PROJECTION_SCHEMA_VERSION = 4


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
        candidate_evaluations = unresolved_candidate_evaluation.evaluate_references(
            reference_rows,
            artifact_root=evaluation_root,
            master=master,
            catalog_identity=title_artist_evaluation.CatalogIdentity(
                catalog_metadata["catalog_identity"],
                int(catalog_metadata["schema_version"]),
                catalog_metadata["created_at"],
            ),
            extractor=extractor,
        )
        review_references: list[dict[str, Any]] = []
        for row, candidate_evaluation in zip(reference_rows, candidate_evaluations, strict=True):
            status, reason = catalog._reference_state(row, master)
            assigned_song_id = str(row["song_id"] or "")
            assigned_song = songs_by_id.get(assigned_song_id)
            item = {
                "reference_id": str(row["reference_id"]),
                "review_status": status,
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
            }
            item.update(
                {
                    "stored_status": str(row["review_status"]),
                    "revision": int(row["review_revision"]),
                    "manual_action_id": row["manual_action_id"],
                    "manual_note": str(row["manual_note"]),
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


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Print the strict read-only jacket catalog review projection."
    )
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--master-db", required=True, type=Path)
    parser.add_argument("--artifact-root", type=Path)
    parser.add_argument("--report-output-dir", type=Path)
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
    if args.report_output_dir is not None:
        unresolved_candidate_evaluation.write_reports(
            args.report_output_dir, projection["review_references"]
        )
    print(json.dumps(projection, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
