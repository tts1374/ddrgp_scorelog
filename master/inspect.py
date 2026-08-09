from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
from contextlib import closing
from pathlib import Path
from typing import Any

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
        if not database_row[6].endswith(expected_note):
            raise ValueError(
                "confirmed CHALLENGE supplement chart note does not match provenance"
            )
    if snapshot_count not in {1, 2, 3}:
        raise ValueError("generated database must contain one to three source snapshots")

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
    }


def write_summary(summary_path: Path, summary: dict[str, Any]) -> None:
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Inspect a generated DDR GP master database.")
    parser.add_argument("database", type=Path, help="Generated SQLite master database path.")
    parser.add_argument("--summary", type=Path, help="Optional JSON summary output path.")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    summary = inspect_master_database(args.database)
    if args.summary is not None:
        write_summary(args.summary, summary)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
