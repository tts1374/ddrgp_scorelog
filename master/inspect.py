from __future__ import annotations

import argparse
import json
import sqlite3
from pathlib import Path
from typing import Any

REQUIRED_METADATA_KEYS = {
    "master_version",
    "source_url",
    "generated_at",
    "generator_version",
    "source_hash",
    "song_count",
    "chart_count",
}


def inspect_master_database(db_path: Path) -> dict[str, Any]:
    if not db_path.exists():
        raise FileNotFoundError(f"master database does not exist: {db_path}")

    with sqlite3.connect(db_path) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        song_count = connection.execute("SELECT COUNT(*) FROM songs").fetchone()[0]
        chart_count = connection.execute("SELECT COUNT(*) FROM charts").fetchone()[0]
        snapshot_rows = connection.execute(
            """
            SELECT source_url, content_hash, parser_version
            FROM source_snapshots
            ORDER BY snapshot_id
            """
        ).fetchall()

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
    if snapshot_count not in {1, 2}:
        raise ValueError("generated database must contain one or two source snapshots")

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

    return {
        "database": str(db_path),
        "song_count": song_count,
        "chart_count": chart_count,
        "snapshot_count": snapshot_count,
        "source_hash": metadata.get("source_hash"),
        "snapshot_source_hash": snapshot_content_hash,
        "snapshot_source_url": snapshot_source_url,
        "snapshot_parser_version": snapshot_parser_version,
        "official_source_hash": official_source_hash,
        "official_snapshot_source_hash": official_snapshot_source_hash,
        "official_source_url": official_source_url,
        "official_snapshot_parser_version": official_snapshot_parser_version,
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
