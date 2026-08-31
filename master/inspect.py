from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
from contextlib import closing
from pathlib import Path
from typing import Any

from .builder import (
    DDRWORLD_BLOCKING_STATUSES,
    DDRWORLD_MERGE_REPORT_SCHEMA,
    DDRWORLD_MERGE_STATUSES,
    ddrworld_merge_report_hash,
)

REQUIRED_METADATA_KEYS = {
    "master_version",
    "source_url",
    "generated_at",
    "generator_version",
    "source_hash",
    "confirmed_challenge_chart_count",
    "confirmed_challenge_supplement_hash",
    "confirmed_challenge_supplement_json",
    "song_count",
    "chart_count",
}


def inspect_master_database(db_path: Path) -> dict[str, Any]:
    if not db_path.exists():
        raise FileNotFoundError(f"master database does not exist: {db_path}")

    with closing(sqlite3.connect(db_path)) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        song_count = connection.execute("SELECT COUNT(*) FROM songs").fetchone()[0]
        chart_count = connection.execute("SELECT COUNT(*) FROM charts").fetchone()[0]
        alias_count = connection.execute("SELECT COUNT(*) FROM song_aliases").fetchone()[0]
        snapshot_rows = connection.execute(
            """
            SELECT source_url, content_hash, parser_version
            FROM source_snapshots
            ORDER BY snapshot_id
            """
        ).fetchall()
        chart_rows_by_id = {
            row[0]: row
            for row in connection.execute(
                """
                SELECT c.chart_id, c.song_id, s.title, c.play_style,
                       c.difficulty, c.level, c.notes
                FROM charts c
                JOIN songs s ON s.song_id = c.song_id
                """
            )
        }
        chart_id_duplicate_count = connection.execute(
            """
            SELECT COUNT(*) FROM (
              SELECT chart_id FROM charts GROUP BY chart_id HAVING COUNT(*) > 1
            )
            """
        ).fetchone()[0]
        chart_identity_duplicate_count = connection.execute(
            """
            SELECT COUNT(*) FROM (
              SELECT song_id, play_style, difficulty
              FROM charts
              GROUP BY song_id, play_style, difficulty
              HAVING COUNT(*) > 1
            )
            """
        ).fetchone()[0]
        referential_integrity_errors = list(connection.execute("PRAGMA foreign_key_check"))

    snapshot_count = len(snapshot_rows)
    missing_metadata_keys = sorted(REQUIRED_METADATA_KEYS - metadata.keys())
    empty_metadata_keys = sorted(
        key for key in REQUIRED_METADATA_KEYS if key in metadata and not metadata[key]
    )
    if missing_metadata_keys:
        raise ValueError(
            "master_metadata is missing required keys: "
            + ", ".join(missing_metadata_keys)
        )
    if empty_metadata_keys:
        raise ValueError(
            "master_metadata contains empty required values: "
            + ", ".join(empty_metadata_keys)
        )
    if song_count <= 0 or chart_count <= 0:
        raise ValueError("generated database must contain songs and charts")
    if metadata.get("song_count") != str(song_count):
        raise ValueError("master_metadata song_count does not match songs table")
    if metadata.get("chart_count") != str(chart_count):
        raise ValueError("master_metadata chart_count does not match charts table")
    supplement_json = metadata["confirmed_challenge_supplement_json"]
    supplement_hash = hashlib.sha256(supplement_json.encode("utf-8")).hexdigest()
    if metadata["confirmed_challenge_supplement_hash"] != supplement_hash:
        raise ValueError("confirmed CHALLENGE supplement hash does not match manifest")
    try:
        supplement_rows = json.loads(supplement_json)
    except json.JSONDecodeError as exc:
        raise ValueError("confirmed CHALLENGE supplement manifest is invalid JSON") from exc
    if not isinstance(supplement_rows, list):
        raise ValueError("confirmed CHALLENGE supplement manifest must be a list")
    if metadata["confirmed_challenge_chart_count"] != str(len(supplement_rows)):
        raise ValueError("confirmed CHALLENGE supplement count does not match manifest")
    required_supplement_keys = {
        "chart_id",
        "song_id",
        "title",
        "play_style",
        "level",
        "source_url",
        "acquired_on",
    }
    seen_supplement_chart_ids: set[str] = set()
    for row in supplement_rows:
        if not isinstance(row, dict) or set(row) != required_supplement_keys:
            raise ValueError("confirmed CHALLENGE supplement row has invalid keys")
        chart_id = row["chart_id"]
        if not isinstance(chart_id, str) or chart_id in seen_supplement_chart_ids:
            raise ValueError("confirmed CHALLENGE supplement chart ID is invalid")
        seen_supplement_chart_ids.add(chart_id)
        database_row = chart_rows_by_id.get(chart_id)
        expected_note = (
            "confirmed CHALLENGE supplement; "
            f"source_url={row['source_url']}; acquired_on={row['acquired_on']}"
        )
        if database_row is None or database_row[1:6] != (
            row["song_id"],
            row["title"],
            row["play_style"],
            "CHALLENGE",
            row["level"],
        ):
            raise ValueError(
                "confirmed CHALLENGE supplement manifest does not match charts table"
            )
        if expected_note not in database_row[6]:
            raise ValueError(
                "confirmed CHALLENGE supplement chart note does not match provenance"
            )
    if snapshot_count not in {1, 2, 3, 4}:
        raise ValueError("generated database must contain one to four source snapshots")
    if chart_id_duplicate_count:
        raise ValueError("charts contains duplicate chart IDs")
    if chart_identity_duplicate_count:
        raise ValueError("charts contains duplicate song/style/difficulty identities")
    if referential_integrity_errors:
        raise ValueError("generated database has foreign-key integrity errors")

    snapshots_by_url = {row[0]: row for row in snapshot_rows}
    source_url = metadata.get("source_url")
    if source_url not in snapshots_by_url:
        raise ValueError("master_metadata source_url does not match source snapshot")
    snapshot_source_url, snapshot_content_hash, snapshot_parser_version = snapshots_by_url[
        source_url
    ]
    if metadata.get("source_hash") != snapshot_content_hash:
        raise ValueError("master_metadata source_hash does not match source snapshot")

    official_source_url = metadata.get("official_source_url")
    official_source_hash = metadata.get("official_source_hash")
    official_snapshot_source_hash = None
    official_snapshot_parser_version = None
    if official_source_url or official_source_hash:
        if not official_source_url or not official_source_hash:
            raise ValueError("official source metadata must include URL and hash")
        if official_source_url not in snapshots_by_url:
            raise ValueError(
                "master_metadata official_source_url does not match source snapshot"
            )
        _url, official_snapshot_source_hash, official_snapshot_parser_version = (
            snapshots_by_url[official_source_url]
        )
        if official_source_hash != official_snapshot_source_hash:
            raise ValueError(
                "master_metadata official_source_hash does not match source snapshot"
            )

    new_song_source_url = metadata.get("new_song_source_url")
    new_song_source_hash = metadata.get("new_song_source_hash")
    new_song_snapshot_source_hash = None
    new_song_snapshot_parser_version = None
    if new_song_source_url or new_song_source_hash:
        if not new_song_source_url or not new_song_source_hash:
            raise ValueError("new-song source metadata must include URL and hash")
        if new_song_source_url not in snapshots_by_url:
            raise ValueError(
                "master_metadata new_song_source_url does not match source snapshot"
            )
        _url, new_song_snapshot_source_hash, new_song_snapshot_parser_version = (
            snapshots_by_url[new_song_source_url]
        )
        if new_song_source_hash != new_song_snapshot_source_hash:
            raise ValueError(
                "master_metadata new_song_source_hash does not match source snapshot"
            )
    expected_snapshot_count = 1 + int(bool(official_source_url)) + int(
        bool(new_song_source_url)
    )
    ddrworld_source_url = metadata.get("ddrworld_source_url")
    ddrworld_source_hash = metadata.get("ddrworld_source_hash")
    ddrworld_report = None
    ddrworld_report_hash = None
    ddrworld_snapshot_parser_version = None
    ddrworld_metadata_present = any(
        key.startswith("ddrworld_") for key in metadata
    )
    if ddrworld_metadata_present:
        ddrworld_required = {
            "ddrworld_source_url",
            "ddrworld_source_hash",
            "ddrworld_snapshot_id",
            "ddrworld_fetched_at",
            "ddrworld_parser_version",
            "ddrworld_page_count",
            "ddrworld_song_count",
            "ddrworld_chart_count",
            "ddrworld_merge_report_hash",
            "ddrworld_merge_report_json",
        }
        missing = sorted(key for key in ddrworld_required if not metadata.get(key))
        if missing:
            raise ValueError(
                "DDR WORLD metadata is incomplete: " + ", ".join(missing)
            )
        if ddrworld_source_url not in snapshots_by_url:
            raise ValueError(
                "master_metadata ddrworld_source_url does not match source snapshot"
            )
        _url, snapshot_hash, ddrworld_snapshot_parser_version = snapshots_by_url[
            ddrworld_source_url
        ]
        if ddrworld_source_hash != snapshot_hash:
            raise ValueError(
                "master_metadata ddrworld_source_hash does not match source snapshot"
            )
        if metadata["ddrworld_parser_version"] != ddrworld_snapshot_parser_version:
            raise ValueError("DDR WORLD parser version does not match source snapshot")
        try:
            ddrworld_report = json.loads(metadata["ddrworld_merge_report_json"])
        except json.JSONDecodeError as exc:
            raise ValueError("DDR WORLD merge report is invalid JSON") from exc
        if not isinstance(ddrworld_report, dict):
            raise ValueError("DDR WORLD merge report must be an object")
        if ddrworld_report.get("schema_version") != DDRWORLD_MERGE_REPORT_SCHEMA:
            raise ValueError("DDR WORLD merge report schema is unsupported")
        if ddrworld_report.get("unit") != "song + play_style + difficulty":
            raise ValueError("DDR WORLD merge report unit is unsupported")
        if ddrworld_report.get("priority") != [
            "ddrworld_official",
            "bemaniwiki",
            "confirmed_challenge_supplement",
        ]:
            raise ValueError("DDR WORLD merge report priority is unsupported")
        ddrworld_report_hash = ddrworld_merge_report_hash(ddrworld_report)
        if metadata["ddrworld_merge_report_hash"] != ddrworld_report_hash:
            raise ValueError("DDR WORLD merge report hash does not match manifest")
        report_source = ddrworld_report.get("source")
        if not isinstance(report_source, dict):
            raise ValueError("DDR WORLD merge report source metadata is missing")
        for key, expected in (
            ("source_url", ddrworld_source_url),
            ("content_hash", ddrworld_source_hash),
            ("snapshot_id", metadata["ddrworld_snapshot_id"]),
            ("fetched_at", metadata["ddrworld_fetched_at"]),
            ("page_count", int(metadata["ddrworld_page_count"])),
            ("song_count", int(metadata["ddrworld_song_count"])),
            ("chart_count", int(metadata["ddrworld_chart_count"])),
        ):
            if report_source.get(key) != expected:
                raise ValueError(f"DDR WORLD merge report source {key} does not match metadata")
        report_rows = ddrworld_report.get("rows")
        report_counts = ddrworld_report.get("counts")
        if not isinstance(report_rows, list) or not isinstance(report_counts, dict):
            raise ValueError("DDR WORLD merge report rows/counts are invalid")
        for key in DDRWORLD_MERGE_STATUSES:
            if not isinstance(report_counts.get(key), int) or report_counts[key] < 0:
                raise ValueError(f"DDR WORLD merge report count is invalid: {key}")
        row_status_counts: dict[str, int] = {}
        row_keys: set[tuple[Any, ...]] = set()
        allowed_statuses = set(DDRWORLD_MERGE_STATUSES)
        for row in report_rows:
            if not isinstance(row, dict):
                raise ValueError("DDR WORLD merge report row is invalid")
            row_key = tuple(
                row.get(field)
                for field in (
                    "title",
                    "artist",
                    "source_page",
                    "page_position",
                    "play_style",
                    "difficulty",
                    "status",
                )
            )
            if row_key in row_keys:
                raise ValueError("DDR WORLD merge report contains duplicate chart rows")
            row_keys.add(row_key)
            status = row.get("status")
            if not isinstance(status, str) or status not in allowed_statuses:
                raise ValueError("DDR WORLD merge report row status is invalid")
            row_status_counts[status] = row_status_counts.get(status, 0) + 1
            if status in {
                "excluded_non_gp",
                "world_only_outside_gp",
                *DDRWORLD_BLOCKING_STATUSES,
            } and not row.get("reason"):
                raise ValueError(
                    "DDR WORLD excluded or blocking row is missing its reason"
                )
            if status == "world_only_outside_gp" and (
                row.get("song_id") or row.get("chart_id")
            ):
                raise ValueError(
                    "DDR WORLD outside-GP row must not reference master IDs"
                )
            if status in {
                "official_only",
                "official_override",
                "wiki_only",
                "supplement_only",
            }:
                chart_id = row.get("chart_id")
                database_row = chart_rows_by_id.get(chart_id)
                if database_row is None:
                    raise ValueError("DDR WORLD merge report references an unknown chart")
                if status == "wiki_only" and database_row[5] != row.get("wiki_level"):
                    raise ValueError("DDR WORLD Wiki-only row does not match charts table")
                if (
                    status == "supplement_only"
                    and database_row[5] != row.get("baseline_level")
                ):
                    raise ValueError(
                        "DDR WORLD supplement-only row does not match charts table"
                    )
                if status in {"official_only", "official_override"} and database_row[5] != row.get(
                    "official_level"
                ):
                    raise ValueError("DDR WORLD official row does not match charts table")
            if status == "excluded_non_gp":
                database_row = chart_rows_by_id.get(row.get("chart_id"))
                baseline_level = row.get("baseline_level")
                if baseline_level is None and database_row is not None:
                    raise ValueError(
                        "DDR WORLD excluded non-GP row unexpectedly references a chart"
                    )
                if baseline_level is not None and (
                    database_row is None or database_row[5] != baseline_level
                ):
                    raise ValueError(
                        "DDR WORLD excluded non-GP row changed its baseline chart"
                    )
        for key in DDRWORLD_MERGE_STATUSES:
            if report_counts[key] != row_status_counts.get(key, 0):
                raise ValueError(f"DDR WORLD merge report {key} count does not match rows")
        if sum(report_counts[key] for key in DDRWORLD_MERGE_STATUSES) != len(
            report_rows
        ):
            raise ValueError("DDR WORLD merge report status counts do not match rows")
        official_source_statuses = {
            "official_override",
            "official_only",
            "excluded_non_gp",
            "world_only_outside_gp",
            *DDRWORLD_BLOCKING_STATUSES,
        }
        if sum(report_counts[key] for key in official_source_statuses) != report_source.get(
            "chart_count"
        ):
            raise ValueError(
                "DDR WORLD merge report official chart counts do not match source"
            )
        override_rows = [
            row for row in report_rows if row.get("status") == "official_override"
        ]
        level_changed = sum(
            row.get("baseline_level") != row.get("official_level")
            for row in override_rows
        )
        if report_counts.get("level_changed") != level_changed:
            raise ValueError("DDR WORLD merge report level_changed count does not match rows")
        if report_counts.get("level_unchanged") != len(override_rows) - level_changed:
            raise ValueError(
                "DDR WORLD merge report level_unchanged count does not match rows"
            )
        blocking_counts = {
            status: report_counts[status]
            for status in DDRWORLD_BLOCKING_STATUSES
            if report_counts[status]
        }
        if blocking_counts:
            raise ValueError(
                "DDR WORLD merge report contains blocking GP candidates: "
                + ", ".join(
                    f"{status}={count}"
                    for status, count in sorted(blocking_counts.items())
                )
            )
        expected_snapshot_count += 1
    if snapshot_count != expected_snapshot_count:
        raise ValueError(
            "generated database source snapshot count does not match source metadata"
        )

    return {
        "database": str(db_path),
        "song_count": song_count,
        "chart_count": chart_count,
        "song_alias_count": alias_count,
        "snapshot_count": snapshot_count,
        "source_hash": metadata.get("source_hash"),
        "snapshot_source_hash": snapshot_content_hash,
        "snapshot_source_url": snapshot_source_url,
        "snapshot_parser_version": snapshot_parser_version,
        "official_source_hash": official_source_hash,
        "official_snapshot_source_hash": official_snapshot_source_hash,
        "official_source_url": official_source_url,
        "official_snapshot_parser_version": official_snapshot_parser_version,
        "new_song_source_hash": new_song_source_hash,
        "new_song_snapshot_source_hash": new_song_snapshot_source_hash,
        "new_song_source_url": new_song_source_url,
        "new_song_snapshot_parser_version": new_song_snapshot_parser_version,
        "confirmed_challenge_chart_count": len(supplement_rows),
        "confirmed_challenge_supplement_hash": supplement_hash,
        "free_play_available_song_count": metadata.get("free_play_available_song_count"),
        "grand_prix_play_available_song_count": metadata.get(
            "grand_prix_play_available_song_count"
        ),
        "official_availability_matched_song_count": metadata.get(
            "official_availability_matched_song_count"
        ),
        "master_version": metadata.get("master_version"),
        "source_url": metadata.get("source_url"),
        "generated_at": metadata.get("generated_at"),
        "generator_version": metadata.get("generator_version"),
        "chart_id_duplicate_count": chart_id_duplicate_count,
        "chart_identity_duplicate_count": chart_identity_duplicate_count,
        "referential_integrity_error_count": len(referential_integrity_errors),
        "ddrworld_source_hash": ddrworld_source_hash,
        "ddrworld_source_url": ddrworld_source_url,
        "ddrworld_snapshot_parser_version": ddrworld_snapshot_parser_version,
        "ddrworld_snapshot_id": metadata.get("ddrworld_snapshot_id"),
        "ddrworld_page_count": metadata.get("ddrworld_page_count"),
        "ddrworld_song_count": metadata.get("ddrworld_song_count"),
        "ddrworld_chart_count": metadata.get("ddrworld_chart_count"),
        "ddrworld_merge_report_hash": ddrworld_report_hash,
        "ddrworld_merge_counts": (
            None if ddrworld_report is None else ddrworld_report["counts"]
        ),
        "ddrworld_merge_report": ddrworld_report,
    }


def write_summary(summary_path: Path, summary: dict[str, Any]) -> None:
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Inspect a generated DDR GP master database.")
    parser.add_argument("database", type=Path, help="Generated SQLite master database path.")
    parser.add_argument("--summary", type=Path, help="Optional JSON summary output path.")
    parser.add_argument(
        "--merge-report",
        type=Path,
        help="Optional JSON output path for the DDR WORLD chart merge report.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    summary = inspect_master_database(args.database)
    if args.summary is not None:
        write_summary(args.summary, summary)
    if args.merge_report is not None:
        report = summary.get("ddrworld_merge_report")
        if report is None:
            raise ValueError("database does not contain a DDR WORLD merge report")
        args.merge_report.parent.mkdir(parents=True, exist_ok=True)
        args.merge_report.write_text(
            json.dumps(report, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
