from __future__ import annotations

import argparse
import hashlib
import sqlite3
from collections import defaultdict
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path

from bs4 import BeautifulSoup, FeatureNotFound

SOURCE_URL = (
    "https://bemaniwiki.com/index.php?"
    "DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88"
)
PARSER_VERSION = "m4-initial-html-table-v1"
DIFFICULTIES_BY_STYLE = {
    "SINGLE": ("BEGINNER", "BASIC", "DIFFICULT", "EXPERT", "CHALLENGE"),
    "DOUBLE": ("BASIC", "DIFFICULT", "EXPERT", "CHALLENGE"),
}


@dataclass(frozen=True)
class MasterSong:
    song_id: str
    title: str
    artist: str
    version: str
    source_version: str
    bpm: str
    category: str
    movie_stage: str
    availability: str
    notes: str


@dataclass(frozen=True)
class MasterChart:
    chart_id: str
    song_id: str
    play_style: str
    difficulty: str
    level: int | None
    raw_level: str
    shock_arrow: bool
    is_removed: bool
    is_limited: bool
    notes: str


@dataclass(frozen=True)
class SourceSnapshot:
    source_url: str
    fetched_at: str
    content_hash: str
    parser_version: str
    html_content: str


@dataclass(frozen=True)
class MasterBuild:
    songs: tuple[MasterSong, ...]
    charts: tuple[MasterChart, ...]
    snapshot: SourceSnapshot


def normalize_text(value: str) -> str:
    return " ".join(value.replace("\xa0", " ").replace("\u2003", " ").split())


def stable_id(prefix: str, *parts: str) -> str:
    digest = hashlib.sha1("\0".join(parts).encode("utf-8")).hexdigest()[:16]
    return f"{prefix}_{digest}"


def parse_level(raw_level: str) -> int | None:
    normalized = normalize_text(raw_level)
    if normalized in {"", "-"}:
        return None
    digits = "".join(ch for ch in normalized if ch.isdigit())
    if not digits:
        return None
    return int(digits)


def has_shock_arrow(raw_level: str) -> bool:
    return any(token in raw_level for token in ("→", "SA", "Shock", "ショック"))


def parse_soup(html: str) -> BeautifulSoup:
    try:
        return BeautifulSoup(html, "lxml")
    except FeatureNotFound:
        return BeautifulSoup(html, "html.parser")


def expanded_table_rows(table) -> list[list[str]]:
    rows: list[list[str]] = []
    spans: dict[int, list[object]] = {}
    for tr in table.find_all("tr"):
        row: list[str] = []
        col_index = 0
        cells = tr.find_all(["th", "td"])
        for cell in cells:
            while col_index in spans:
                text, remaining = spans[col_index]
                row.append(str(text))
                remaining_count = int(remaining) - 1
                if remaining_count:
                    spans[col_index] = [text, remaining_count]
                else:
                    del spans[col_index]
                col_index += 1

            text = normalize_text(cell.get_text(" ", strip=True))
            rowspan = int(cell.get("rowspan", 1))
            colspan = int(cell.get("colspan", 1))
            for offset in range(colspan):
                row.append(text)
                if rowspan > 1:
                    spans[col_index + offset] = [text, rowspan - 1]
            col_index += colspan

        while col_index in spans:
            text, remaining = spans[col_index]
            row.append(str(text))
            remaining_count = int(remaining) - 1
            if remaining_count:
                spans[col_index] = [text, remaining_count]
            else:
                del spans[col_index]
            col_index += 1
        if row:
            rows.append(row)
    return rows


def is_song_list_table(rows: list[list[str]]) -> bool:
    if len(rows) < 3:
        return False
    header = rows[0]
    subheader = rows[1]
    return (
        len(header) >= 15
        and header[:6] == ["分類", "曲名", "アーティスト", "出典", "BPM", "MV/St"]
        and "SINGLE" in header
        and "DOUBLE" in header
        and subheader[6:15] == ["Be", "Ba", "Di", "Ex", "Ch", "Ba", "Di", "Ex", "Ch"]
    )


def is_section_row(row: list[str]) -> bool:
    non_empty = [value for value in row if value]
    return bool(non_empty) and len(set(non_empty)) == 1 and len(row) >= 15


def chart_values_from_row(row: list[str]) -> list[tuple[str, str, str]]:
    values: list[tuple[str, str, str]] = []
    offset = 6
    for play_style, difficulties in DIFFICULTIES_BY_STYLE.items():
        for difficulty in difficulties:
            values.append((play_style, difficulty, row[offset] if offset < len(row) else ""))
            offset += 1
    return values


def parse_song_list_rows(rows: list[list[str]]) -> tuple[list[MasterSong], list[MasterChart]]:
    songs: list[MasterSong] = []
    charts: list[MasterChart] = []
    current_version = ""
    seen_song_ids: set[str] = set()

    for row in rows[2:]:
        if is_section_row(row):
            current_version = row[0]
            continue
        if len(row) < 15:
            continue

        title = normalize_text(row[1])
        artist = normalize_text(row[2])
        if not title or title == "曲名":
            continue

        availability = normalize_text(row[0])
        source_version = normalize_text(row[3])
        bpm = normalize_text(row[4])
        movie_stage = normalize_text(row[5])
        song_id = stable_id("song", title, artist)
        if song_id not in seen_song_ids:
            songs.append(
                MasterSong(
                    song_id=song_id,
                    title=title,
                    artist=artist,
                    version=current_version,
                    source_version=source_version,
                    bpm=bpm,
                    category=current_version,
                    movie_stage=movie_stage,
                    availability=availability,
                    notes="",
                )
            )
            seen_song_ids.add(song_id)

        for play_style, difficulty, raw_level in chart_values_from_row(row):
            raw_level = normalize_text(raw_level)
            level = parse_level(raw_level)
            if level is None:
                continue
            chart_id = stable_id("chart", song_id, play_style, difficulty)
            charts.append(
                MasterChart(
                    chart_id=chart_id,
                    song_id=song_id,
                    play_style=play_style,
                    difficulty=difficulty,
                    level=level,
                    raw_level=raw_level,
                    shock_arrow=has_shock_arrow(raw_level),
                    is_removed=("削" in availability or "×" in availability),
                    is_limited=bool(availability),
                    notes=availability,
                )
            )
    return songs, charts


def parse_master_html(
    html: str,
    *,
    source_url: str = SOURCE_URL,
    fetched_at: str | None = None,
) -> MasterBuild:
    soup = parse_soup(html)
    songs_by_id: dict[str, MasterSong] = {}
    charts_by_id: dict[str, MasterChart] = {}
    song_table_count = 0

    for table in soup.find_all("table"):
        rows = expanded_table_rows(table)
        if not is_song_list_table(rows):
            continue
        song_table_count += 1
        songs, charts = parse_song_list_rows(rows)
        songs_by_id.update({song.song_id: song for song in songs})
        charts_by_id.update({chart.chart_id: chart for chart in charts})

    if song_table_count == 0:
        raise ValueError("source HTML does not contain DDR GP song list tables")
    if not songs_by_id or not charts_by_id:
        raise ValueError("source HTML did not produce songs and charts")

    snapshot = SourceSnapshot(
        source_url=source_url,
        fetched_at=fetched_at or datetime.now(UTC).isoformat(timespec="seconds"),
        content_hash=hashlib.sha256(html.encode("utf-8")).hexdigest(),
        parser_version=PARSER_VERSION,
        html_content=html,
    )
    return MasterBuild(
        songs=tuple(songs_by_id.values()),
        charts=tuple(charts_by_id.values()),
        snapshot=snapshot,
    )


def create_schema(connection: sqlite3.Connection) -> None:
    connection.executescript(
        """
        PRAGMA foreign_keys = ON;

        CREATE TABLE songs (
          song_id TEXT PRIMARY KEY,
          title TEXT NOT NULL,
          artist TEXT NOT NULL,
          version TEXT NOT NULL,
          source_version TEXT NOT NULL,
          bpm TEXT NOT NULL,
          category TEXT NOT NULL,
          movie_stage TEXT NOT NULL,
          availability TEXT NOT NULL,
          notes TEXT NOT NULL,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL
        );

        CREATE TABLE charts (
          chart_id TEXT PRIMARY KEY,
          song_id TEXT NOT NULL REFERENCES songs(song_id) ON DELETE CASCADE,
          play_style TEXT NOT NULL CHECK (play_style IN ('SINGLE', 'DOUBLE')),
          difficulty TEXT NOT NULL CHECK (
            difficulty IN ('BEGINNER', 'BASIC', 'DIFFICULT', 'EXPERT', 'CHALLENGE')
          ),
          level INTEGER NOT NULL CHECK (level BETWEEN 1 AND 19),
          raw_level TEXT NOT NULL,
          shock_arrow INTEGER NOT NULL CHECK (shock_arrow IN (0, 1)),
          is_removed INTEGER NOT NULL CHECK (is_removed IN (0, 1)),
          is_limited INTEGER NOT NULL CHECK (is_limited IN (0, 1)),
          notes TEXT NOT NULL,
          UNIQUE (song_id, play_style, difficulty)
        );

        CREATE TABLE master_metadata (
          key TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );

        CREATE TABLE source_snapshots (
          snapshot_id TEXT PRIMARY KEY,
          source_url TEXT NOT NULL,
          fetched_at TEXT NOT NULL,
          content_hash TEXT NOT NULL,
          parser_version TEXT NOT NULL,
          html_content TEXT NOT NULL
        );

        CREATE INDEX idx_songs_title ON songs(title);
        CREATE INDEX idx_charts_song_id ON charts(song_id);
        CREATE INDEX idx_charts_identity ON charts(play_style, difficulty, level);
        """
    )


def write_master_database(
    output_path: Path,
    build: MasterBuild,
    *,
    master_version: str | None = None,
    generated_at: str | None = None,
    generator_version: str = PARSER_VERSION,
) -> None:
    generated_at = generated_at or datetime.now(UTC).isoformat(timespec="seconds")
    master_version = master_version or build.snapshot.content_hash[:12]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.exists():
        output_path.unlink()

    with sqlite3.connect(output_path) as connection:
        create_schema(connection)
        connection.executemany(
            """
            INSERT INTO songs (
              song_id, title, artist, version, source_version, bpm, category,
              movie_stage, availability, notes, created_at, updated_at
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (
                    song.song_id,
                    song.title,
                    song.artist,
                    song.version,
                    song.source_version,
                    song.bpm,
                    song.category,
                    song.movie_stage,
                    song.availability,
                    song.notes,
                    generated_at,
                    generated_at,
                )
                for song in build.songs
            ],
        )
        connection.executemany(
            """
            INSERT INTO charts (
              chart_id, song_id, play_style, difficulty, level, raw_level,
              shock_arrow, is_removed, is_limited, notes
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (
                    chart.chart_id,
                    chart.song_id,
                    chart.play_style,
                    chart.difficulty,
                    chart.level,
                    chart.raw_level,
                    int(chart.shock_arrow),
                    int(chart.is_removed),
                    int(chart.is_limited),
                    chart.notes,
                )
                for chart in build.charts
            ],
        )
        metadata = {
            "master_version": master_version,
            "source_url": build.snapshot.source_url,
            "generated_at": generated_at,
            "generator_version": generator_version,
            "source_hash": build.snapshot.content_hash,
            "song_count": str(len(build.songs)),
            "chart_count": str(len(build.charts)),
        }
        connection.executemany(
            "INSERT INTO master_metadata (key, value) VALUES (?, ?)",
            sorted(metadata.items()),
        )
        snapshot_id = stable_id(
            "snapshot",
            build.snapshot.source_url,
            build.snapshot.content_hash,
            build.snapshot.parser_version,
        )
        connection.execute(
            """
            INSERT INTO source_snapshots (
              snapshot_id, source_url, fetched_at, content_hash, parser_version, html_content
            )
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            (
                snapshot_id,
                build.snapshot.source_url,
                build.snapshot.fetched_at,
                build.snapshot.content_hash,
                build.snapshot.parser_version,
                build.snapshot.html_content,
            ),
        )


def summarize_build(build: MasterBuild) -> dict[str, object]:
    by_style: dict[str, int] = defaultdict(int)
    by_difficulty: dict[str, int] = defaultdict(int)
    for chart in build.charts:
        by_style[chart.play_style] += 1
        by_difficulty[chart.difficulty] += 1
    return {
        "songs": len(build.songs),
        "charts": len(build.charts),
        "source_hash": build.snapshot.content_hash,
        "by_play_style": dict(sorted(by_style.items())),
        "by_difficulty": dict(sorted(by_difficulty.items())),
    }


def fetch_source_html(url: str) -> str:
    import requests

    response = requests.get(url, timeout=30)
    response.raise_for_status()
    return response.text


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Build the DDR GP master SQLite database.")
    parser.add_argument(
        "--input",
        type=Path,
        help="Local BEMANIWiki HTML snapshot. If omitted, the current source URL is fetched.",
    )
    parser.add_argument(
        "--source-url",
        default=SOURCE_URL,
        help="Source URL recorded in master_metadata and source_snapshots.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("data/master/ddrgp-master.sqlite"),
        help="Output SQLite path. Local generated DBs are normally written under data/.",
    )
    parser.add_argument(
        "--master-version",
        help="Optional master version string. Defaults to the first 12 chars of source hash.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    html = (
        args.input.read_text(encoding="utf-8")
        if args.input is not None
        else fetch_source_html(args.source_url)
    )
    build = parse_master_html(html, source_url=args.source_url)
    write_master_database(args.output, build, master_version=args.master_version)
    summary = summarize_build(build)
    print(
        "Wrote master DB: "
        f"{args.output} ({summary['songs']} songs, {summary['charts']} charts, "
        f"source_hash={str(summary['source_hash'])[:12]})"
    )
    return 0
