from __future__ import annotations

import argparse
import sqlite3
from pathlib import Path


def sql_text(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def export_shared_master_sql(master_db_path: Path) -> str:
    uri = f"file:{master_db_path.resolve().as_posix()}?mode=ro"
    with sqlite3.connect(uri, uri=True) as connection:
        required = {"songs", "charts", "master_metadata"}
        actual = {
            row[0]
            for row in connection.execute(
                "SELECT name FROM sqlite_master WHERE type = 'table'"
            )
        }
        missing = required - actual
        if missing:
            raise ValueError(f"master DB is missing required tables: {sorted(missing)}")
        songs = connection.execute(
            "SELECT song_id, title, artist, version FROM songs ORDER BY song_id"
        ).fetchall()
        charts = connection.execute(
            "SELECT chart_id, song_id, play_style, difficulty, level, is_removed "
            "FROM charts ORDER BY chart_id"
        ).fetchall()
        metadata = dict(
            connection.execute("SELECT key, value FROM master_metadata")
        )
    master_version = metadata.get("master_version")
    if not isinstance(master_version, str) or not master_version:
        raise ValueError("master DB does not contain master_version metadata")

    lines = ["PRAGMA foreign_keys = ON;", ""]
    for song_id, title, artist, version in songs:
        values = ", ".join(sql_text(value) for value in (song_id, title, artist, version))
        lines.extend(
            [
                "INSERT INTO songs (song_id, title, artist, version)",
                f"VALUES ({values})",
                "ON CONFLICT(song_id) DO UPDATE SET",
                "  title = excluded.title,",
                "  artist = excluded.artist,",
                "  version = excluded.version;",
            ]
        )
    lines.append("")
    for chart_id, song_id, play_style, difficulty, level, is_removed in charts:
        values = ", ".join(
            [
                sql_text(chart_id),
                sql_text(song_id),
                sql_text(play_style),
                sql_text(difficulty),
                str(level),
                str(is_removed),
            ]
        )
        lines.extend(
            [
                "INSERT INTO charts (chart_id, song_id, play_style, difficulty, level, is_removed)",
                f"VALUES ({values})",
                "ON CONFLICT(chart_id) DO UPDATE SET",
                "  song_id = excluded.song_id,",
                "  play_style = excluded.play_style,",
                "  difficulty = excluded.difficulty,",
                "  level = excluded.level,",
                "  is_removed = excluded.is_removed;",
            ]
        )
    lines.extend(
        [
            "",
            "INSERT INTO web_master_metadata (key, value)",
            f"VALUES ('master_version', {sql_text(master_version)})",
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            "",
        ]
    )
    return "\n".join(lines)


def write_shared_master_sql(master_db_path: Path, output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        export_shared_master_sql(master_db_path),
        encoding="utf-8",
        newline="\n",
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Export the public shared master subset for Cloudflare D1."
    )
    parser.add_argument("--master-db", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    write_shared_master_sql(args.master_db, args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
