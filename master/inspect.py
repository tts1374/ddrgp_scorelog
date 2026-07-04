from __future__ import annotations

import argparse
import json
import sqlite3
from pathlib import Path
from typing import Any


def inspect_master_database(db_path: Path) -> dict[str, Any]:
    if not db_path.exists():
        raise FileNotFoundError(f"master database does not exist: {db_path}")

    with sqlite3.connect(db_path) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        song_count = connection.execute("SELECT COUNT(*) FROM songs").fetchone()[0]
        chart_count = connection.execute("SELECT COUNT(*) FROM charts").fetchone()[0]
        snapshot_rows = connection.execute(
            "SELECT content_hash FROM source_snapshots ORDER BY snapshot_id"
        ).fetchall()

    snapshot_count = len(snapshot_rows)
    if song_count <= 0 or chart_count <= 0:
        raise ValueError("generated database must contain songs and charts")
    if metadata.get("song_count") != str(song_count):
        raise ValueError("master_metadata song_count does not match songs table")
    if metadata.get("chart_count") != str(chart_count):
        raise ValueError("master_metadata chart_count does not match charts table")
    if snapshot_count != 1:
        raise ValueError("generated database must contain exactly one source snapshot")
    if metadata.get("source_hash") != snapshot_rows[0][0]:
        raise ValueError("master_metadata source_hash does not match source snapshot")

    return {
        "database": str(db_path),
        "song_count": song_count,
        "chart_count": chart_count,
        "source_hash": metadata.get("source_hash"),
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
