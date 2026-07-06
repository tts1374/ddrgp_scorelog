from __future__ import annotations

import argparse
import hashlib
import re
import sqlite3
import unicodedata
from collections import defaultdict
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path

from bs4 import BeautifulSoup, FeatureNotFound

SOURCE_URL = (
    "https://bemaniwiki.com/index.php?"
    "DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88"
)
OFFICIAL_MUSIC_LIST_URL = "https://p.eagate.573.jp/game/eacddr/konaddr/info/mlist.html"
PARSER_VERSION = "m4-initial-html-table-v3"
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
    free_play_available: bool = False
    grand_prix_play_available: bool = False
    official_availability_match: str = "not_checked"


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
class OfficialSongAvailability:
    title: str
    artist: str
    free_play_available: bool
    grand_prix_play_available: bool


@dataclass(frozen=True)
class MasterBuild:
    songs: tuple[MasterSong, ...]
    charts: tuple[MasterChart, ...]
    snapshot: SourceSnapshot
    official_snapshot: SourceSnapshot | None = None


def normalize_text(value: str) -> str:
    return " ".join(value.replace("\xa0", " ").replace("\u2003", " ").split())


def normalize_table_cell_text(cell) -> str:
    cell_copy = BeautifulSoup(str(cell), "html.parser")
    for anchor in cell_copy.find_all("a"):
        if re.fullmatch(r"\*\d+", anchor.get_text(strip=True)):
            anchor.decompose()
    return normalize_text(cell_copy.get_text(" ", strip=True))


def normalize_availability_key(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", normalize_text(value)).casefold()
    return "".join(char for char in normalized if not char.isspace())


def stable_id(prefix: str, *parts: str) -> str:
    digest = hashlib.sha1("\0".join(parts).encode("utf-8")).hexdigest()[:16]
    return f"{prefix}_{digest}"


def parse_level(raw_level: str) -> int | None:
    normalized = normalize_text(raw_level)
    if normalized in {"", "-"}:
        return None
    match = re.search(r"\d+", normalized)
    if match is None:
        return None
    return int(match.group())


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

            text = normalize_table_cell_text(cell)
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


def parse_official_music_list_html(html: str) -> tuple[OfficialSongAvailability, ...]:
    soup = parse_soup(html)
    entries: dict[tuple[str, str], OfficialSongAvailability] = {}

    for table in soup.find_all("table"):
        rows = expanded_table_rows(table)
        if not rows:
            continue
        header = rows[0]
        if "タイトル" not in header or "アーティスト" not in header:
            continue
        if "グランプリプレー" not in header:
            continue
        title_index = header.index("タイトル")
        artist_index = header.index("アーティスト")
        grand_prix_index = header.index("グランプリプレー")
        free_play_index = header.index("フリープレー") if "フリープレー" in header else None

        for row in rows[1:]:
            if len(row) <= max(title_index, artist_index, grand_prix_index):
                continue
            title = normalize_text(row[title_index])
            artist = normalize_text(row[artist_index])
            if not title:
                continue
            free_play_available = (
                False
                if free_play_index is None or len(row) <= free_play_index
                else "〇" in row[free_play_index]
            )
            grand_prix_play_available = "〇" in row[grand_prix_index]
            key = (normalize_availability_key(title), normalize_availability_key(artist))
            previous = entries.get(key)
            entries[key] = OfficialSongAvailability(
                title=title,
                artist=artist,
                free_play_available=free_play_available
                or (previous.free_play_available if previous is not None else False),
                grand_prix_play_available=grand_prix_play_available
                or (
                    previous.grand_prix_play_available
                    if previous is not None
                    else False
                ),
            )

    if not entries:
        raise ValueError("official music list did not produce availability rows")
    return tuple(entries.values())


def apply_official_availability(
    songs: tuple[MasterSong, ...],
    availability_entries: tuple[OfficialSongAvailability, ...],
) -> tuple[MasterSong, ...]:
    by_title_artist = {
        (
            normalize_availability_key(entry.title),
            normalize_availability_key(entry.artist),
        ): entry
        for entry in availability_entries
        if entry.artist
    }
    by_title: dict[str, list[OfficialSongAvailability]] = {}
    for entry in availability_entries:
        by_title.setdefault(normalize_availability_key(entry.title), []).append(entry)

    updated_songs: list[MasterSong] = []
    for song in songs:
        title_key = normalize_availability_key(song.title)
        artist_key = normalize_availability_key(song.artist)
        entry = by_title_artist.get((title_key, artist_key))
        match_status = "title_artist"
        if entry is None:
            title_matches = by_title.get(title_key, [])
            if len(title_matches) == 1:
                entry = title_matches[0]
                match_status = "unique_title"
            elif title_matches:
                match_status = "ambiguous_title"
            else:
                match_status = "not_found"
        updated_songs.append(
            MasterSong(
                song_id=song.song_id,
                title=song.title,
                artist=song.artist,
                version=song.version,
                source_version=song.source_version,
                bpm=song.bpm,
                category=song.category,
                movie_stage=song.movie_stage,
                availability=song.availability,
                free_play_available=(
                    False if entry is None else entry.free_play_available
                ),
                grand_prix_play_available=(
                    False if entry is None else entry.grand_prix_play_available
                ),
                official_availability_match=match_status,
                notes=song.notes,
            )
        )
    return tuple(updated_songs)


def parse_master_html(
    html: str,
    *,
    source_url: str = SOURCE_URL,
    fetched_at: str | None = None,
    official_html: str | None = None,
    official_source_url: str = OFFICIAL_MUSIC_LIST_URL,
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
        for song in songs:
            songs_by_id.setdefault(song.song_id, song)
        for chart in charts:
            existing_chart = charts_by_id.get(chart.chart_id)
            if existing_chart is not None and existing_chart != chart:
                raise ValueError(
                    "source HTML contains conflicting chart rows for "
                    f"{chart.song_id} {chart.play_style} {chart.difficulty}"
                )
            charts_by_id[chart.chart_id] = chart

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
    official_snapshot = None
    songs = tuple(songs_by_id.values())
    if official_html is not None:
        official_snapshot = SourceSnapshot(
            source_url=official_source_url,
            fetched_at=fetched_at or datetime.now(UTC).isoformat(timespec="seconds"),
            content_hash=hashlib.sha256(official_html.encode("utf-8")).hexdigest(),
            parser_version=PARSER_VERSION,
            html_content=official_html,
        )
        songs = apply_official_availability(
            songs,
            parse_official_music_list_html(official_html),
        )
    return MasterBuild(
        songs=songs,
        charts=tuple(charts_by_id.values()),
        snapshot=snapshot,
        official_snapshot=official_snapshot,
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
          free_play_available INTEGER NOT NULL DEFAULT 0 CHECK (free_play_available IN (0, 1)),
          grand_prix_play_available INTEGER NOT NULL DEFAULT 0 CHECK (
            grand_prix_play_available IN (0, 1)
          ),
          official_availability_match TEXT NOT NULL DEFAULT 'not_checked',
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
              movie_stage, availability, free_play_available, grand_prix_play_available,
              official_availability_match, notes, created_at, updated_at
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
                    int(song.free_play_available),
                    int(song.grand_prix_play_available),
                    song.official_availability_match,
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
            "free_play_available_song_count": str(
                sum(1 for song in build.songs if song.free_play_available)
            ),
            "grand_prix_play_available_song_count": str(
                sum(1 for song in build.songs if song.grand_prix_play_available)
            ),
            "official_availability_matched_song_count": str(
                sum(
                    1
                    for song in build.songs
                    if song.official_availability_match
                    in {"title_artist", "unique_title"}
                )
            ),
        }
        if build.official_snapshot is not None:
            metadata.update(
                {
                    "official_source_url": build.official_snapshot.source_url,
                    "official_source_hash": build.official_snapshot.content_hash,
                }
            )
        connection.executemany(
            "INSERT INTO master_metadata (key, value) VALUES (?, ?)",
            sorted(metadata.items()),
        )
        for snapshot in (build.snapshot, build.official_snapshot):
            if snapshot is None:
                continue
            snapshot_id = stable_id(
                "snapshot",
                snapshot.source_url,
                snapshot.content_hash,
                snapshot.parser_version,
            )
            connection.execute(
                """
                INSERT INTO source_snapshots (
                  snapshot_id, source_url, fetched_at, content_hash,
                  parser_version, html_content
                )
                VALUES (?, ?, ?, ?, ?, ?)
                """,
                (
                    snapshot_id,
                    snapshot.source_url,
                    snapshot.fetched_at,
                    snapshot.content_hash,
                    snapshot.parser_version,
                    snapshot.html_content,
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
        "official_source_hash": (
            None if build.official_snapshot is None else build.official_snapshot.content_hash
        ),
        "free_play_available_songs": sum(
            1 for song in build.songs if song.free_play_available
        ),
        "grand_prix_play_available_songs": sum(
            1 for song in build.songs if song.grand_prix_play_available
        ),
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
        "--official-input",
        type=Path,
        help="Local official music list HTML snapshot for play availability.",
    )
    parser.add_argument(
        "--official-source-url",
        default=OFFICIAL_MUSIC_LIST_URL,
        help="Official music list URL recorded in metadata and source snapshots.",
    )
    parser.add_argument(
        "--skip-official-availability",
        action="store_true",
        help="Do not fetch or apply official free/grand-prix play availability.",
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
    official_html = None
    if not args.skip_official_availability:
        official_html = (
            args.official_input.read_text(encoding="utf-8")
            if args.official_input is not None
            else fetch_source_html(args.official_source_url)
        )
    build = parse_master_html(
        html,
        source_url=args.source_url,
        official_html=official_html,
        official_source_url=args.official_source_url,
    )
    write_master_database(args.output, build, master_version=args.master_version)
    summary = summarize_build(build)
    print(
        "Wrote master DB: "
        f"{args.output} ({summary['songs']} songs, {summary['charts']} charts, "
        f"source_hash={str(summary['source_hash'])[:12]})"
    )
    return 0
