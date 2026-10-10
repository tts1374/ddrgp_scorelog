from __future__ import annotations

import argparse
import json
import sqlite3
import unicodedata
from pathlib import Path

from .ac_history import VERSION_CATEGORIES


def sql_text(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def normalize_title_search(value: str) -> str:
    result: list[str] = []
    latin_base = False
    for char in unicodedata.normalize("NFD", value.lower()):
        if unicodedata.category(char).startswith("M"):
            if not latin_base:
                result.append(char)
            continue
        latin_base = unicodedata.name(char, "").startswith("LATIN ")
        result.append({"æ": "ae", "ø": "o"}.get(char, char))
    return unicodedata.normalize("NFC", "".join(result))


def export_shared_master_sql(master_db_path: Path) -> str:
    uri = f"file:{master_db_path.resolve().as_posix()}?mode=ro"
    with sqlite3.connect(uri, uri=True) as connection:
        required = {"songs", "charts", "song_aliases", "master_metadata"}
        actual = {
            row[0]
            for row in connection.execute(
                "SELECT name FROM sqlite_master WHERE type = 'table'"
            )
        }
        missing = required - actual
        if missing:
            raise ValueError(f"master DB is missing required tables: {sorted(missing)}")
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        history_table = "song_ac_history" in actual
        songs = connection.execute(
            "SELECT song_id, title, artist, version FROM songs ORDER BY song_id"
        ).fetchall()
        categories = dict(connection.execute(
            "SELECT song_id, flare_category FROM song_ac_history"
        )) if history_table else {}
        if history_table or "ac_history_json" in metadata or "ac_history_hash" in metadata:
            from .inspect import inspect_master_database
            inspect_master_database(master_db_path)
        else:
            columns = {row[1] for row in connection.execute("PRAGMA table_info(songs)")}
            eligible = (
                dict(connection.execute("SELECT song_id, grand_prix_play_available FROM songs"))
                if "grand_prix_play_available" in columns
                else dict.fromkeys((s[0] for s in songs), 1)
            )
            categories = {sid: VERSION_CATEGORIES.get(version) if eligible[sid] else None
                          for sid, _, _, version in songs}
        charts = connection.execute(
            "SELECT chart_id, song_id, play_style, difficulty, level, is_removed "
            "FROM charts ORDER BY chart_id"
        ).fetchall()
        source_aliases = connection.execute(
            "SELECT song_id, alias_title FROM song_aliases ORDER BY song_id, alias_title"
        ).fetchall()
    master_version = metadata.get("master_version")
    if not isinstance(master_version, str) or not master_version:
        raise ValueError("master DB does not contain master_version metadata")

    song_titles = {song_id: title for song_id, title, _, _ in songs}
    curated = json.loads(
        Path(__file__).with_name("title_search_aliases.json").read_text(encoding="utf-8")
    )
    search_aliases = {
        (song_id, normalize_title_search(alias))
        for song_id, alias in source_aliases + [
            (entry["song_id"], alias)
            for entry in curated
            for alias in entry["aliases"]
        ]
        if song_id in song_titles
        and normalize_title_search(alias) != normalize_title_search(song_titles[song_id])
    }

    lines = ["PRAGMA foreign_keys = ON;", ""]
    for song_id, title, artist, version in songs:
        values = ", ".join(
            sql_text(value)
            for value in (song_id, title, artist, version, normalize_title_search(title))
        )
        category = categories[song_id]
        values += ", " + ("NULL" if category is None else sql_text(category))
        lines.extend(
            [
                "INSERT INTO songs (song_id, title, artist, version, "
                "title_search_key, flare_category)",
                f"VALUES ({values})",
                "ON CONFLICT(song_id) DO UPDATE SET",
                "  title = excluded.title,",
                "  artist = excluded.artist,",
                "  version = excluded.version,",
                "  flare_category = excluded.flare_category,",
                "  title_search_key = excluded.title_search_key",
                "WHERE songs.title IS NOT excluded.title",
                "   OR songs.artist IS NOT excluded.artist",
                "   OR songs.version IS NOT excluded.version",
                "   OR songs.flare_category IS NOT excluded.flare_category",
                "   OR songs.title_search_key IS NOT excluded.title_search_key;",
            ]
        )
    lines.append("")
    if search_aliases:
        lines.append("WITH desired_aliases (song_id, search_key) AS (VALUES")
        lines.append(",\n".join(
            f"  ({sql_text(song_id)}, {sql_text(search_key)})"
            for song_id, search_key in sorted(search_aliases)
        ))
        lines.extend([
            ")",
            "DELETE FROM song_title_search_aliases",
            "WHERE NOT EXISTS (",
            "  SELECT 1 FROM desired_aliases",
            "  WHERE desired_aliases.song_id = song_title_search_aliases.song_id",
            "    AND desired_aliases.search_key = song_title_search_aliases.search_key",
            ");",
        ])
    else:
        lines.append("DELETE FROM song_title_search_aliases;")
    for song_id, search_key in sorted(search_aliases):
        lines.append(
            "INSERT INTO song_title_search_aliases (song_id, search_key) "
            f"VALUES ({sql_text(song_id)}, {sql_text(search_key)}) "
            "ON CONFLICT(song_id, search_key) DO NOTHING;"
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
                "  is_removed = excluded.is_removed",
                "WHERE charts.song_id IS NOT excluded.song_id",
                "   OR charts.play_style IS NOT excluded.play_style",
                "   OR charts.difficulty IS NOT excluded.difficulty",
                "   OR charts.level IS NOT excluded.level",
                "   OR charts.is_removed IS NOT excluded.is_removed;",
            ]
        )
    lines.extend(
        [
            "",
            "INSERT INTO web_master_metadata (key, value)",
            f"VALUES ('master_version', {sql_text(master_version)})",
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            "WHERE web_master_metadata.value IS NOT excluded.value;",
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
