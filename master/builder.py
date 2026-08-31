from __future__ import annotations

import argparse
import hashlib
import json
import re
import sqlite3
import unicodedata
from collections import defaultdict
from contextlib import closing
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any
from urllib.parse import urlencode

from bs4 import BeautifulSoup, FeatureNotFound

SOURCE_URL = (
    "https://bemaniwiki.com/index.php?"
    "DanceDanceRevolution+GRAND+PRIX/%E5%85%A8%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88"
)
NEW_SONGS_SOURCE_URL = (
    "https://bemaniwiki.com/?"
    "DanceDanceRevolution+GRAND+PRIX/%E6%96%B0%E6%9B%B2%E3%83%AA%E3%82%B9%E3%83%88"
)
OFFICIAL_MUSIC_LIST_URL = "https://p.eagate.573.jp/game/eacddr/konaddr/info/mlist.html"
DDRWORLD_MUSIC_SOURCE_URL = (
    "https://p.eagate.573.jp/game/ddr/ddrworld/music/index.html"
    "?filter=7&filtertype=0&playmode=2"
)
DDRWORLD_SOURCE_ORIGIN = "https://p.eagate.573.jp"
DDRWORLD_SOURCE_PATH = "/game/ddr/ddrworld/music/index.html"
DDRWORLD_MAX_PAGE_COUNT = 100
PARSER_VERSION = "m4-ddrworld-chart-priority-v1"
DDRWORLD_MERGE_REPORT_SCHEMA = "ddrworld-chart-merge-report-v1"
DDRWORLD_MERGE_STATUSES = (
    "official_override",
    "official_only",
    "wiki_only",
    "supplement_only",
    "excluded_non_gp",
    "world_only_outside_gp",
    "unmatchable_gp_candidate",
    "ambiguous_gp_candidate",
)
DDRWORLD_BLOCKING_STATUSES = (
    "unmatchable_gp_candidate",
    "ambiguous_gp_candidate",
)
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
class MasterSongAlias:
    alias_id: str
    song_id: str
    alias_title: str
    alias_artist: str
    alias_type: str
    source: str


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
class DdrWorldChart:
    play_style: str
    difficulty: str
    level: int
    raw_level: str
    shock_arrow: bool = False


@dataclass(frozen=True)
class DdrWorldSong:
    source_page: int
    page_position: int
    title: str
    artist: str
    charts: tuple[DdrWorldChart, ...]
    source_url: str


@dataclass(frozen=True)
class DdrWorldSnapshot:
    songs: tuple[DdrWorldSong, ...]
    snapshot: SourceSnapshot
    snapshot_id: str
    page_count: int
    chart_count: int
    collector_version: str


@dataclass(frozen=True)
class ConfirmedChallengeSource:
    title: str
    single_level: int
    double_level: int
    source_url: str
    acquired_on: str


@dataclass(frozen=True)
class AppliedChallengeSupplement:
    chart_id: str
    song_id: str
    title: str
    play_style: str
    level: int
    source_url: str
    acquired_on: str


@dataclass(frozen=True)
class MasterBuild:
    songs: tuple[MasterSong, ...]
    charts: tuple[MasterChart, ...]
    snapshot: SourceSnapshot
    song_aliases: tuple[MasterSongAlias, ...] = ()
    official_snapshot: SourceSnapshot | None = None
    new_song_snapshot: SourceSnapshot | None = None
    confirmed_challenge_supplements: tuple[AppliedChallengeSupplement, ...] = ()
    ddrworld_snapshot: DdrWorldSnapshot | None = None
    ddrworld_merge_report: dict[str, Any] | None = None


DDRWORLD_CHALLENGE_SOURCE_URL = DDRWORLD_MUSIC_SOURCE_URL
BEMANIWIKI_PACK_SOURCE_URL = (
    "https://bemaniwiki.com/?"
    "DanceDanceRevolution+GRAND+PRIX/%E6%A5%BD%E6%9B%B2%E3%83%91%E3%83%83%E3%82%AF"
)


def _confirmed_challenge_source(
    title: str,
    single_level: int,
    double_level: int,
    *,
    source_url: str = DDRWORLD_CHALLENGE_SOURCE_URL,
    acquired_on: str = "2026-07-25",
) -> ConfirmedChallengeSource:
    return ConfirmedChallengeSource(
        title=title,
        single_level=single_level,
        double_level=double_level,
        source_url=source_url,
        acquired_on=acquired_on,
    )


CONFIRMED_CHALLENGE_SOURCES = (
    _confirmed_challenge_source("Ace out", 14, 14),
    _confirmed_challenge_source("ALPACORE", 17, 17),
    _confirmed_challenge_source("BITTER CHOCOLATE STRIKER", 18, 18),
    _confirmed_challenge_source("Come Back To Me", 16, 16),
    _confirmed_challenge_source("CyberConnect", 17, 17),
    _confirmed_challenge_source("DIGITALIZER", 18, 18),
    _confirmed_challenge_source("Din Don Dan (にじさんじダンス部 ver.)", 16, 16),
    _confirmed_challenge_source("Draw the Savage", 15, 14),
    _confirmed_challenge_source("Give Me", 16, 16),
    _confirmed_challenge_source("Glitch Angel", 18, 18),
    _confirmed_challenge_source("Going Hypersonic", 17, 17),
    _confirmed_challenge_source("Golden Arrow", 17, 17),
    _confirmed_challenge_source("Good Looking", 17, 18),
    _confirmed_challenge_source("Lightspeed", 18, 18),
    _confirmed_challenge_source("MUTEKI BUFFALO", 17, 17),
    _confirmed_challenge_source("New Era", 18, 18),
    _confirmed_challenge_source("Rampage Hero", 17, 17),
    _confirmed_challenge_source("Run The Show", 16, 16),
    _confirmed_challenge_source("Starlight in the Snow", 16, 16),
    _confirmed_challenge_source("Step This Way", 17, 17),
    _confirmed_challenge_source("Take A Step Forward", 15, 15),
    _confirmed_challenge_source("The World Ends Now", 18, 18),
    _confirmed_challenge_source("Yuni's Nocturnal Days", 18, 18),
    _confirmed_challenge_source(
        "打打打打打打打打打打 (にじさんじダンス部 ver.)", 16, 16
    ),
    _confirmed_challenge_source("灼熱Beach Side Bunny", 18, 18),
    _confirmed_challenge_source(
        "7 Colors",
        16,
        16,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
    _confirmed_challenge_source(
        "Harmonia",
        16,
        16,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
    _confirmed_challenge_source(
        "In The Breeze",
        14,
        14,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
    _confirmed_challenge_source(
        "Superior MAXXX",
        19,
        19,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
    _confirmed_challenge_source(
        "Touch My Body",
        14,
        14,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
    _confirmed_challenge_source(
        "True Blue",
        17,
        17,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
    _confirmed_challenge_source(
        "クリムゾンゲイト",
        16,
        16,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
    _confirmed_challenge_source(
        "パ→ピ→プ→Yeah!",
        15,
        16,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
    _confirmed_challenge_source(
        "和風インザ洋風",
        17,
        17,
        source_url=BEMANIWIKI_PACK_SOURCE_URL,
        acquired_on="2026-08-09",
    ),
)


def normalize_text(value: str) -> str:
    return " ".join(value.replace("\xa0", " ").replace("\u2003", " ").split())


def normalize_table_cell_text(cell) -> str:
    cell_copy = BeautifulSoup(str(cell), "html.parser")
    for anchor in cell_copy.find_all("a"):
        if re.fullmatch(r"\*\d+", anchor.get_text(strip=True)):
            anchor.decompose()
    return normalize_text(cell_copy.get_text(" ", strip=True))


def normalize_availability_key(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", normalize_text(value)).translate(
        str.maketrans(
            {
                "’": "'",
                "‘": "'",
                "＇": "'",
                "“": '"',
                "”": '"',
                "＂": '"',
                "－": "-",
                "–": "-",
                "—": "-",
                "～": "~",
                "〜": "~",
            }
        )
    ).casefold()
    normalized = re.sub(r"(?:\.{2,}|[…‥⋯]+|[・･]{2,})", "...", normalized)
    return "".join(char for char in normalized if not char.isspace())


CYRILLIC_CONFUSABLES_FOR_ALIAS = str.maketrans(
    {
        "А": "a",
        "В": "b",
        "Е": "e",
        "Ё": "e",
        "К": "k",
        "М": "m",
        "Н": "h",
        "О": "o",
        "Р": "p",
        "С": "c",
        "Т": "t",
        "Х": "x",
        "а": "a",
        "в": "b",
        "е": "e",
        "ё": "e",
        "к": "k",
        "м": "m",
        "н": "h",
        "о": "o",
        "р": "p",
        "с": "c",
        "т": "t",
        "х": "x",
    }
)


def normalize_availability_alias_key(value: str) -> str:
    normalized = unicodedata.normalize("NFKD", normalize_text(value)).casefold()
    without_marks = "".join(
        char for char in normalized if unicodedata.category(char) != "Mn"
    )
    return normalize_availability_key(
        without_marks.translate(CYRILLIC_CONFUSABLES_FOR_ALIAS)
    )


def stable_id(prefix: str, *parts: str) -> str:
    digest = hashlib.sha1("\0".join(parts).encode("utf-8")).hexdigest()[:16]
    return f"{prefix}_{digest}"


def confirmed_challenge_supplements_json(
    supplements: tuple[AppliedChallengeSupplement, ...],
) -> str:
    return json.dumps(
        [
            {
                "chart_id": supplement.chart_id,
                "song_id": supplement.song_id,
                "title": supplement.title,
                "play_style": supplement.play_style,
                "level": supplement.level,
                "source_url": supplement.source_url,
                "acquired_on": supplement.acquired_on,
            }
            for supplement in supplements
        ],
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    )


def confirmed_challenge_supplements_hash(
    supplements: tuple[AppliedChallengeSupplement, ...],
) -> str:
    manifest = confirmed_challenge_supplements_json(supplements)
    return hashlib.sha256(manifest.encode("utf-8")).hexdigest()


CONFIRMED_CHALLENGE_NOTE_MARKER = "confirmed CHALLENGE supplement;"


def confirmed_challenge_note(source_url: str, acquired_on: str) -> str:
    return (
        f"{CONFIRMED_CHALLENGE_NOTE_MARKER} "
        f"source_url={source_url}; acquired_on={acquired_on}"
    )


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


def parse_soup(html: str | bytes) -> BeautifulSoup:
    try:
        return BeautifulSoup(html, "lxml")
    except FeatureNotFound:
        return BeautifulSoup(html, "html.parser")


def parse_ddrworld_music_page(
    html: str | bytes,
    *,
    page_offset: int = 0,
    page_url: str = DDRWORLD_MUSIC_SOURCE_URL,
    allow_empty: bool = False,
) -> tuple[DdrWorldSong, ...]:
    """Parse one current DDR WORLD music page, including SP/DP chart levels."""
    soup = parse_soup(html)
    table = soup.find("table", class_="table-ui")
    if table is None:
        raise ValueError(f"DDR WORLD page {page_offset} is missing the official music table")

    rows = table.select("tr.data")
    if not rows:
        unexpected_rows = table.select("tr:not(.data):not(.column)")
        if unexpected_rows:
            raise ValueError(
                f"DDR WORLD page {page_offset} contains unexpected rows in the official music table"
            )
        if allow_empty:
            return ()
        raise ValueError(f"DDR WORLD page {page_offset} contains no music rows")

    songs: list[DdrWorldSong] = []
    for position, row in enumerate(rows):
        title_cell = row.select_one(".music-title")
        artist_cell = row.select_one(".artist")
        title = normalize_text(title_cell.get_text(" ", strip=True)) if title_cell else ""
        artist = normalize_text(artist_cell.get_text(" ", strip=True)) if artist_cell else ""
        missing = [
            name
            for name, value in (("title", title), ("artist", artist))
            if not value
        ]
        if missing:
            raise ValueError(
                f"DDR WORLD page {page_offset} row {position} is missing {', '.join(missing)}"
            )

        charts: list[DdrWorldChart] = []
        seen_chart_keys: set[tuple[str, str]] = set()
        containers = row.select(".diff-style-container")
        if not containers:
            raise ValueError(
                f"DDR WORLD page {page_offset} row {position} is missing chart containers"
            )
        for container in containers:
            label = container.select_one(".label")
            style_label = normalize_text(label.get_text(" ", strip=True)) if label else ""
            play_style = {"SP": "SINGLE", "DP": "DOUBLE"}.get(style_label)
            if play_style is None:
                raise ValueError(
                    f"DDR WORLD page {page_offset} row {position} has an invalid play style"
                )
            for difficulty in DIFFICULTIES_BY_STYLE[play_style]:
                diff_nodes = container.select(f".diff.{difficulty}")
                if len(diff_nodes) > 1:
                    raise ValueError(
                        f"DDR WORLD page {page_offset} row {position} repeats "
                        f"{style_label} {difficulty}"
                    )
                if not diff_nodes:
                    continue
                level_node = diff_nodes[0].select_one(".level")
                if level_node is None:
                    raise ValueError(
                        f"DDR WORLD page {page_offset} row {position} is missing "
                        f"{style_label} {difficulty} level"
                    )
                raw_level = normalize_text(level_node.get_text(" ", strip=True))
                level = parse_level(raw_level)
                if level is None:
                    continue
                if not 1 <= level <= 19:
                    raise ValueError(
                        f"DDR WORLD page {page_offset} row {position} has an invalid "
                        f"{style_label} {difficulty} level: {raw_level}"
                    )
                chart_key = (play_style, difficulty)
                if chart_key in seen_chart_keys:
                    raise ValueError(
                        f"DDR WORLD page {page_offset} row {position} repeats "
                        f"{style_label} {difficulty}"
                    )
                seen_chart_keys.add(chart_key)
                charts.append(
                    DdrWorldChart(
                        play_style=play_style,
                        difficulty=difficulty,
                        level=level,
                        raw_level=raw_level,
                        shock_arrow=has_shock_arrow(raw_level),
                    )
                )
        songs.append(
            DdrWorldSong(
                source_page=page_offset,
                page_position=position,
                title=title,
                artist=artist,
                charts=tuple(charts),
                source_url=page_url,
            )
        )
    return tuple(songs)


def _read_json_object(path: Path, label: str) -> tuple[dict[str, Any], bytes]:
    try:
        raw = path.read_bytes()
        value = json.loads(raw.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid DDR WORLD snapshot {label}: {path}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"DDR WORLD snapshot {label} must be an object: {path}")
    return value, raw


def _read_snapshot_relative_file(root: Path, value: object, label: str) -> tuple[Path, bytes]:
    relative = Path(str(value or ""))
    if not str(value) or relative.is_absolute() or ".." in relative.parts:
        raise ValueError(f"DDR WORLD snapshot {label} has an unsafe path")
    path = (root / relative).resolve()
    try:
        path.relative_to(root.resolve())
    except ValueError as exc:
        raise ValueError(f"DDR WORLD snapshot {label} escapes the snapshot root") from exc
    try:
        return path, path.read_bytes()
    except OSError as exc:
        raise ValueError(f"DDR WORLD snapshot {label} cannot be read: {path}") from exc


def _ddrworld_snapshot_hash(
    manifest_raw: bytes,
    summary_raw: bytes,
    songs_raw: bytes,
    page_records: list[dict[str, Any]],
) -> str:
    material = {
        "manifest_sha256": hashlib.sha256(manifest_raw).hexdigest(),
        "summary_sha256": hashlib.sha256(summary_raw).hexdigest(),
        "songs_sha256": hashlib.sha256(songs_raw).hexdigest(),
        "pages": [
            {
                "offset": record["offset"],
                "source_url": record["source_url"],
                "fetched_at": record["fetched_at"],
                "sha256": record["sha256"],
            }
            for record in page_records
        ],
    }
    canonical = json.dumps(material, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def load_ddrworld_snapshot(path: Path) -> DdrWorldSnapshot:
    """Load and validate a complete snapshot produced by the #193 collector."""
    root = path.resolve()
    if not root.is_dir():
        raise ValueError(f"DDR WORLD snapshot directory does not exist: {path}")
    manifest, manifest_raw = _read_json_object(root / "manifest.json", "manifest")
    summary, summary_raw = _read_json_object(root / "summary.json", "summary")
    _songs_path, songs_raw = _read_snapshot_relative_file(
        root, "songs.jsonl", "songs.jsonl"
    )

    if manifest.get("schema_version") != "ddrworld-music-snapshot-manifest-v1":
        raise ValueError("DDR WORLD snapshot manifest schema is unsupported")
    if summary.get("schema_version") != "ddrworld-music-snapshot-summary-v1":
        raise ValueError("DDR WORLD snapshot summary schema is unsupported")
    if manifest.get("status") != "complete" or summary.get("status") != "complete":
        raise ValueError("DDR WORLD snapshot is not complete")
    if not isinstance(manifest.get("failures"), list) or manifest["failures"]:
        raise ValueError("DDR WORLD snapshot contains failures")
    snapshot_id = manifest.get("snapshot_id")
    if not isinstance(snapshot_id, str) or not snapshot_id:
        raise ValueError("DDR WORLD snapshot ID is invalid")
    if summary.get("snapshot_id") != snapshot_id:
        raise ValueError("DDR WORLD snapshot IDs do not match")
    collector_version = manifest.get("collector_version")
    if not isinstance(collector_version, str) or not collector_version:
        raise ValueError("DDR WORLD snapshot collector version is invalid")

    source = manifest.get("source")
    if not isinstance(source, dict):
        raise ValueError("DDR WORLD snapshot source metadata is missing")
    if (
        source.get("origin") != DDRWORLD_SOURCE_ORIGIN
        or source.get("path") != DDRWORLD_SOURCE_PATH
        or source.get("filter") != 7
        or source.get("filter_type") != 0
        or source.get("play_mode") != 2
    ):
        raise ValueError("DDR WORLD snapshot source query is unsupported")
    query = urlencode(
        {
            "filter": source["filter"],
            "filtertype": source["filter_type"],
            "playmode": source["play_mode"],
        }
    )
    source_url = f"{source['origin']}{source['path']}?{query}"

    page_records = manifest.get("pages")
    offsets = source.get("offsets")
    if not isinstance(page_records, list) or not all(
        isinstance(record, dict) for record in page_records
    ):
        raise ValueError("DDR WORLD snapshot page metadata is invalid")
    if not isinstance(offsets, list) or offsets != list(range(len(page_records))):
        raise ValueError("DDR WORLD snapshot page offsets are not contiguous")
    if len(page_records) <= 0 or len(page_records) >= DDRWORLD_MAX_PAGE_COUNT:
        raise ValueError("DDR WORLD snapshot page count is outside the safe range")
    pagination = manifest.get("pagination")
    if not isinstance(pagination, dict):
        raise ValueError("DDR WORLD snapshot pagination metadata is missing")
    if (
        pagination.get("max_page_count") != DDRWORLD_MAX_PAGE_COUNT
        or summary.get("page_request_count") != len(page_records)
        or summary.get("terminal_offset") != len(page_records)
        or summary.get("failure_count") != 0
    ):
        raise ValueError("DDR WORLD snapshot page count does not match summary")
    if (
        pagination.get("strategy") != "empty_page"
        or pagination.get("terminal_offset") != len(page_records)
        or pagination.get("terminal_validation") != "normal_empty_page"
    ):
        raise ValueError("DDR WORLD snapshot terminal metadata is invalid")
    terminal_page = pagination.get("terminal_page")
    if (
        not isinstance(terminal_page, dict)
        or terminal_page.get("offset") != len(page_records)
        or terminal_page.get("local_path") is not None
        or terminal_page.get("validation") != "normal_empty_page"
    ):
        raise ValueError("DDR WORLD snapshot terminal page metadata is invalid")

    parsed_songs: list[DdrWorldSong] = []
    page_html_parts: list[str] = []
    for expected_offset, record in enumerate(page_records):
        expected_page_url = (
            f"{DDRWORLD_SOURCE_ORIGIN}{DDRWORLD_SOURCE_PATH}?"
            f"{urlencode({'offset': expected_offset, 'filter': 7, 'filtertype': 0, 'playmode': 2})}"
        )
        if (
            record.get("offset") != expected_offset
            or record.get("source_url") != expected_page_url
            or not isinstance(record.get("fetched_at"), str)
            or not isinstance(record.get("sha256"), str)
            or record.get("status_code") != 200
            or record.get("content_type") not in {"text/html", "application/xhtml+xml"}
            or record.get("error") is not None
        ):
            raise ValueError("DDR WORLD snapshot page record is invalid")
        page_path, page_raw = _read_snapshot_relative_file(
            root, record.get("local_path"), f"page {expected_offset}"
        )
        actual_hash = hashlib.sha256(page_raw).hexdigest()
        if actual_hash != record["sha256"]:
            raise ValueError(f"DDR WORLD snapshot page hash mismatch: {page_path}")
        if record.get("byte_size") != len(page_raw):
            raise ValueError(f"DDR WORLD snapshot page size mismatch: {page_path}")
        try:
            page_songs = parse_ddrworld_music_page(
                page_raw,
                page_offset=expected_offset,
                page_url=record["source_url"],
            )
        except ValueError:
            raise
        parsed_songs.extend(page_songs)
        try:
            page_text = page_raw.decode("utf-8")
        except UnicodeDecodeError as exc:
            raise ValueError(f"DDR WORLD snapshot page is not UTF-8: {page_path}") from exc
        page_html_parts.append(
            f"<!-- source_page={expected_offset}; source_url={record['source_url']}; "
            f"sha256={actual_hash} -->\n"
            f"{page_text}"
        )

    try:
        song_rows = [
            json.loads(line)
            for line in songs_raw.decode("utf-8").splitlines()
            if line
        ]
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError("DDR WORLD snapshot songs.jsonl is invalid") from exc
    if (
        not isinstance(summary.get("song_count"), int)
        or summary["song_count"] < 0
        or len(song_rows) != summary["song_count"]
        or len(song_rows) != len(parsed_songs)
    ):
        raise ValueError("DDR WORLD snapshot song count does not match pages")
    for index, (parsed, row) in enumerate(zip(parsed_songs, song_rows, strict=True)):
        if not isinstance(row, dict) or any(
            row.get(field) != expected
            for field, expected in (
                ("source_page", parsed.source_page),
                ("page_position", parsed.page_position),
                ("title", parsed.title),
                ("artist", parsed.artist),
            )
        ):
            raise ValueError(f"DDR WORLD snapshot song row {index} does not match page HTML")

    page_chart_count = sum(len(song.charts) for song in parsed_songs)
    snapshot_hash = _ddrworld_snapshot_hash(
        manifest_raw,
        summary_raw,
        songs_raw,
        page_records,
    )
    fetched_at = manifest.get("completed_at") or manifest.get("started_at")
    if not isinstance(fetched_at, str) or not fetched_at:
        raise ValueError("DDR WORLD snapshot acquisition time is missing")
    snapshot = SourceSnapshot(
        source_url=source_url,
        fetched_at=fetched_at,
        content_hash=snapshot_hash,
        parser_version=PARSER_VERSION,
        html_content="\n".join(page_html_parts),
    )
    return DdrWorldSnapshot(
        songs=tuple(parsed_songs),
        snapshot=snapshot,
        snapshot_id=snapshot_id,
        page_count=len(page_records),
        chart_count=page_chart_count,
        collector_version=collector_version,
    )


def ddrworld_source_from_html(
    html: str | bytes,
    *,
    source_url: str = DDRWORLD_MUSIC_SOURCE_URL,
    fetched_at: str | None = None,
) -> DdrWorldSnapshot:
    content = html.decode("utf-8") if isinstance(html, bytes) else html
    songs = parse_ddrworld_music_page(content, page_url=source_url)
    snapshot = SourceSnapshot(
        source_url=source_url,
        fetched_at=fetched_at or datetime.now(UTC).isoformat(timespec="seconds"),
        content_hash=hashlib.sha256(content.encode("utf-8")).hexdigest(),
        parser_version=PARSER_VERSION,
        html_content=content,
    )
    return DdrWorldSnapshot(
        songs=songs,
        snapshot=snapshot,
        snapshot_id="inline",
        page_count=1,
        chart_count=sum(len(song.charts) for song in songs),
        collector_version="inline",
    )


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


def parse_song_list_html(html: str) -> tuple[list[MasterSong], list[MasterChart]]:
    """Parse every DDR GP song-list table in one Wiki page."""
    soup = parse_soup(html)
    songs: list[MasterSong] = []
    charts: list[MasterChart] = []
    song_table_count = 0
    for table in soup.find_all("table"):
        rows = expanded_table_rows(table)
        if not is_song_list_table(rows):
            continue
        song_table_count += 1
        table_songs, table_charts = parse_song_list_rows(rows)
        songs.extend(table_songs)
        charts.extend(table_charts)

    if song_table_count == 0:
        raise ValueError("source HTML does not contain DDR GP song list tables")
    if not songs or not charts:
        raise ValueError("source HTML did not produce songs and charts")
    return songs, charts


def _find_existing_song_id(
    songs_by_id: dict[str, MasterSong],
    candidate: MasterSong,
) -> str | None:
    exact_key = (
        normalize_availability_key(candidate.title),
        normalize_availability_key(candidate.artist),
    )
    exact_matches = [
        song_id
        for song_id, song in songs_by_id.items()
        if (
            normalize_availability_key(song.title),
            normalize_availability_key(song.artist),
        )
        == exact_key
    ]
    if len(exact_matches) == 1:
        return exact_matches[0]

    alias_key = (
        normalize_availability_alias_key(candidate.title),
        normalize_availability_alias_key(candidate.artist),
    )
    alias_matches = [
        song_id
        for song_id, song in songs_by_id.items()
        if (
            normalize_availability_alias_key(song.title),
            normalize_availability_alias_key(song.artist),
        )
        == alias_key
    ]
    return alias_matches[0] if len(alias_matches) == 1 else None


def merge_song_list_data(
    songs_by_id: dict[str, MasterSong],
    charts_by_id: dict[str, MasterChart],
    songs: list[MasterSong],
    charts: list[MasterChart],
) -> None:
    """Merge a second Wiki list while retaining existing song IDs."""
    song_id_map: dict[str, str] = {}
    for song in songs:
        target_song_id = _find_existing_song_id(songs_by_id, song)
        if target_song_id is None:
            target_song_id = song.song_id
            songs_by_id.setdefault(target_song_id, song)
        song_id_map[song.song_id] = target_song_id

    for chart in charts:
        target_song_id = song_id_map[chart.song_id]
        chart_id = stable_id(
            "chart", target_song_id, chart.play_style, chart.difficulty
        )
        merged_chart = MasterChart(
            chart_id=chart_id,
            song_id=target_song_id,
            play_style=chart.play_style,
            difficulty=chart.difficulty,
            level=chart.level,
            raw_level=chart.raw_level,
            shock_arrow=chart.shock_arrow,
            is_removed=chart.is_removed,
            is_limited=chart.is_limited,
            notes=chart.notes,
        )
        existing_chart = charts_by_id.get(chart_id)
        if existing_chart is not None:
            if (
                existing_chart.level,
                existing_chart.raw_level,
                existing_chart.shock_arrow,
            ) != (
                merged_chart.level,
                merged_chart.raw_level,
                merged_chart.shock_arrow,
            ):
                raise ValueError(
                    "source HTML contains conflicting chart levels for "
                    f"{target_song_id} {chart.play_style} {chart.difficulty}"
                )
            # The new-song list is a level supplement.  Keep the primary
            # all-song row's availability/annotation fields when the level is
            # already present; those fields are not part of the Wiki level
            # contract and may differ by list section.
            continue
        charts_by_id[chart_id] = merged_chart


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


def add_official_only_songs(
    songs: tuple[MasterSong, ...],
    availability_entries: tuple[OfficialSongAvailability, ...],
) -> tuple[MasterSong, ...]:
    """Keep every official GP-available row even when Wiki has no song row."""
    existing_keys = {
        (
            normalize_availability_key(song.title),
            normalize_availability_key(song.artist),
        )
        for song in songs
    }
    result = list(songs)
    for entry in availability_entries:
        if not entry.grand_prix_play_available:
            continue
        key = (
            normalize_availability_key(entry.title),
            normalize_availability_key(entry.artist),
        )
        if key in existing_keys:
            continue
        result.append(
            MasterSong(
                song_id=stable_id("song", entry.title, entry.artist),
                title=entry.title,
                artist=entry.artist,
                version="",
                source_version="",
                bpm="",
                category="",
                movie_stage="",
                availability="",
                free_play_available=entry.free_play_available,
                grand_prix_play_available=True,
                official_availability_match="official_only",
                notes="official GP entry without BEMANIWiki chart data",
            )
        )
        existing_keys.add(key)
    return tuple(result)


def apply_official_availability(
    songs: tuple[MasterSong, ...],
    availability_entries: tuple[OfficialSongAvailability, ...],
) -> tuple[tuple[MasterSong, ...], tuple[MasterSongAlias, ...]]:
    by_title_artist = {
        (
            normalize_availability_key(entry.title),
            normalize_availability_key(entry.artist),
        ): entry
        for entry in availability_entries
    }
    alias_by_title_artist: dict[tuple[str, str], list[OfficialSongAvailability]] = {}
    for entry in availability_entries:
        alias_by_title_artist.setdefault(
            (
                normalize_availability_alias_key(entry.title),
                normalize_availability_alias_key(entry.artist),
            ),
            [],
        ).append(entry)
    by_title: dict[str, list[OfficialSongAvailability]] = {}
    alias_by_title: dict[str, list[OfficialSongAvailability]] = {}
    for entry in availability_entries:
        by_title.setdefault(normalize_availability_key(entry.title), []).append(entry)
        alias_by_title.setdefault(
            normalize_availability_alias_key(entry.title),
            [],
        ).append(entry)

    updated_songs: list[MasterSong] = []
    aliases: list[MasterSongAlias] = []
    for song in songs:
        title_key = normalize_availability_key(song.title)
        artist_key = normalize_availability_key(song.artist)
        title_alias_key = normalize_availability_alias_key(song.title)
        artist_alias_key = normalize_availability_alias_key(song.artist)
        entry = by_title_artist.get((title_key, artist_key))
        match_status = "title_artist"
        if entry is None:
            alias_title_artist_matches = alias_by_title_artist.get(
                (title_alias_key, artist_alias_key),
                [],
            )
            if len(alias_title_artist_matches) == 1:
                entry = alias_title_artist_matches[0]
                match_status = "alias_title_artist"
            elif alias_title_artist_matches:
                match_status = "ambiguous_alias_title_artist"
            else:
                title_matches = by_title.get(title_key, [])
                if len(title_matches) == 1:
                    entry = title_matches[0]
                    match_status = "unique_title"
                elif title_matches:
                    match_status = "ambiguous_title"
                else:
                    alias_title_matches = alias_by_title.get(title_alias_key, [])
                    if len(alias_title_matches) == 1:
                        entry = alias_title_matches[0]
                        match_status = "alias_unique_title"
                    elif alias_title_matches:
                        match_status = "ambiguous_alias_title"
                    else:
                        match_status = "not_found"
        title = song.title if entry is None else entry.title
        # An empty official artist is intentional for some licensed songs.  Do
        # not fall back to the Wiki/copyright artist in that case.
        artist = song.artist if entry is None else entry.artist
        if entry is not None and (title != song.title or artist != song.artist):
            aliases.append(
                MasterSongAlias(
                    alias_id=stable_id(
                        "alias",
                        song.song_id,
                        song.title,
                        song.artist,
                        "wiki_source",
                    ),
                    song_id=song.song_id,
                    alias_title=song.title,
                    alias_artist=song.artist,
                    alias_type="wiki_source",
                    source="bemaniwiki",
                )
            )
        updated_songs.append(
            MasterSong(
                song_id=song.song_id,
                title=title,
                artist=artist,
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
    return tuple(updated_songs), tuple(aliases)


DDRWORLD_CHART_NOTE_MARKER = "DDR WORLD official chart;"


def _resolve_ddrworld_song(
    title: str,
    artist: str,
    songs: tuple[MasterSong, ...],
    aliases: tuple[MasterSongAlias, ...],
) -> tuple[MasterSong | None, str, str, tuple[MasterSong, ...]]:
    canonical_pair: dict[tuple[str, str], set[str]] = defaultdict(set)
    canonical_title: dict[str, set[str]] = defaultdict(set)
    alias_pair: dict[tuple[str, str], set[str]] = defaultdict(set)
    alias_title: dict[str, set[str]] = defaultdict(set)
    for song in songs:
        canonical_pair[
            (
                normalize_availability_key(song.title),
                normalize_availability_key(song.artist),
            )
        ].add(song.song_id)
        canonical_title[normalize_availability_key(song.title)].add(song.song_id)
    for alias in aliases:
        alias_pair[
            (
                normalize_availability_alias_key(alias.alias_title),
                normalize_availability_alias_key(alias.alias_artist),
            )
        ].add(alias.song_id)
        alias_title[normalize_availability_alias_key(alias.alias_title)].add(alias.song_id)

    songs_by_id = {song.song_id: song for song in songs}

    def resolve(
        candidates: set[str],
        success_status: str,
        ambiguous_status: str,
    ) -> tuple[MasterSong | None, str, str, tuple[MasterSong, ...]] | None:
        candidate_songs = tuple(
            songs_by_id[song_id] for song_id in sorted(candidates)
        )
        if len(candidates) == 1:
            return candidate_songs[0], success_status, "", candidate_songs
        if len(candidates) > 1:
            return None, ambiguous_status, ambiguous_status, candidate_songs
        return None

    title_key = normalize_availability_key(title)
    artist_key = normalize_availability_key(artist)
    alias_title_key = normalize_availability_alias_key(title)
    alias_artist_key = normalize_availability_alias_key(artist)
    for result in (
        resolve(
            canonical_pair.get((title_key, artist_key), set()),
            "title_artist",
            "ambiguous_title_artist",
        ),
        resolve(
            alias_pair.get((alias_title_key, alias_artist_key), set()),
            "alias_title_artist",
            "ambiguous_alias_title_artist",
        ),
        resolve(
            canonical_title.get(title_key, set()),
            "unique_title",
            "ambiguous_title",
        ),
        resolve(
            alias_title.get(alias_title_key, set()),
            "alias_unique_title",
            "ambiguous_alias_title",
        ),
    ):
        if result is not None:
            return result
    return None, "not_found", "title_not_found", ()


def _resolve_ddrworld_availability(
    title: str,
    artist: str,
    entries: tuple[OfficialSongAvailability, ...],
) -> tuple[tuple[OfficialSongAvailability, ...], str]:
    title_key = normalize_availability_key(title)
    artist_key = normalize_availability_key(artist)
    alias_title_key = normalize_availability_alias_key(title)
    alias_artist_key = normalize_availability_alias_key(artist)
    match_steps = (
        (
            tuple(
                entry
                for entry in entries
                if (
                    normalize_availability_key(entry.title),
                    normalize_availability_key(entry.artist),
                )
                == (title_key, artist_key)
            ),
            "official_availability_title_artist",
        ),
        (
            tuple(
                entry
                for entry in entries
                if (
                    normalize_availability_alias_key(entry.title),
                    normalize_availability_alias_key(entry.artist),
                )
                == (alias_title_key, alias_artist_key)
            ),
            "official_availability_alias_title_artist",
        ),
        (
            tuple(
                entry
                for entry in entries
                if normalize_availability_key(entry.title) == title_key
            ),
            "official_availability_title",
        ),
        (
            tuple(
                entry
                for entry in entries
                if normalize_availability_alias_key(entry.title) == alias_title_key
            ),
            "official_availability_alias_title",
        ),
    )
    for matches, reason in match_steps:
        if matches:
            return matches, reason
    return (), "official_availability_not_found"


def _ddrworld_chart_note(
    source: DdrWorldSong,
    snapshot: DdrWorldSnapshot,
) -> str:
    return (
        f"{DDRWORLD_CHART_NOTE_MARKER} source_url={source.source_url}; "
        f"source_page={source.source_page}; page_position={source.page_position}; "
        f"fetched_at={snapshot.snapshot.fetched_at}"
    )


def _ddrworld_report_row(
    *,
    source: DdrWorldSong,
    chart: DdrWorldChart,
    status: str,
    reason: str,
    song: MasterSong | None = None,
    wiki_chart: MasterChart | None = None,
) -> dict[str, Any]:
    chart_id = (
        ""
        if song is None
        else stable_id("chart", song.song_id, chart.play_style, chart.difficulty)
    )
    baseline_source = None
    baseline_level = None
    if wiki_chart is not None:
        baseline_source = (
            "confirmed_challenge_supplement"
            if CONFIRMED_CHALLENGE_NOTE_MARKER in wiki_chart.notes
            else "bemaniwiki"
        )
        baseline_level = wiki_chart.level
    return {
        "title": source.title,
        "artist": source.artist,
        "source_page": source.source_page,
        "page_position": source.page_position,
        "source_url": source.source_url,
        "play_style": chart.play_style,
        "difficulty": chart.difficulty,
        "official_level": chart.level,
        "wiki_level": (
            baseline_level if baseline_source == "bemaniwiki" else None
        ),
        "baseline_level": baseline_level,
        "baseline_source": baseline_source,
        "song_id": "" if song is None else song.song_id,
        "chart_id": chart_id,
        "status": status,
        "reason": reason,
    }


def merge_ddrworld_chart_data(
    songs: tuple[MasterSong, ...],
    charts: tuple[MasterChart, ...],
    ddrworld: DdrWorldSnapshot,
    *,
    song_aliases: tuple[MasterSongAlias, ...] = (),
    official_availability_entries: tuple[OfficialSongAvailability, ...] = (),
) -> tuple[tuple[MasterChart, ...], dict[str, Any]]:
    """Apply official DDR WORLD chart levels within the existing GP boundary."""
    original_charts = tuple(charts)
    charts_by_id = {chart.chart_id: chart for chart in original_charts}
    source_key_counts: dict[tuple[str, str], int] = defaultdict(int)
    for source in ddrworld.songs:
        source_key_counts[
            (
                normalize_availability_key(source.title),
                normalize_availability_key(source.artist),
            )
        ] += 1

    rows: list[dict[str, Any]] = []
    counts: defaultdict[str, int] = defaultdict(int)
    for key in (
        *DDRWORLD_MERGE_STATUSES,
        "level_changed",
        "level_unchanged",
        "world_only_outside_gp_song_count",
        "unmatchable_gp_candidate_song_count",
        "ambiguous_gp_candidate_song_count",
        "excluded_non_gp_song_count",
    ):
        counts[key] = 0
    resolved_sources: list[tuple[MasterSong | None, str, str]] = []
    resolved_song_sources: dict[str, list[int]] = defaultdict(list)
    for source in ddrworld.songs:
        source_key = (
            normalize_availability_key(source.title),
            normalize_availability_key(source.artist),
        )
        song, match_status, reason, candidate_songs = _resolve_ddrworld_song(
            source.title,
            source.artist,
            songs,
            song_aliases,
        )
        availability_matches, availability_reason = _resolve_ddrworld_availability(
            source.title,
            source.artist,
            official_availability_entries,
        )
        gp_candidate = any(
            candidate.grand_prix_play_available for candidate in candidate_songs
        ) or any(entry.grand_prix_play_available for entry in availability_matches)
        ambiguous_gp_candidate = gp_candidate and (
            len(candidate_songs) > 1
            or sum(
                entry.grand_prix_play_available for entry in availability_matches
            )
            > 1
            or source_key_counts[source_key] > 1
        )
        if song is None:
            if ambiguous_gp_candidate:
                status = "ambiguous_gp_candidate"
                if source_key_counts[source_key] > 1:
                    reason = "duplicate_official_song_entry"
                elif len(candidate_songs) > 1:
                    reason = match_status
                else:
                    reason = f"ambiguous_{availability_reason}"
            elif gp_candidate:
                status = "unmatchable_gp_candidate"
                reason = "gp_candidate_not_found_in_master"
            else:
                status = "world_only_outside_gp"
                reason = "song_not_confirmed_for_grand_prix"
        elif source_key_counts[source_key] > 1 and song.grand_prix_play_available:
            song = None
            status = "ambiguous_gp_candidate"
            reason = "duplicate_official_song_entry"
        else:
            status = match_status
        resolved_sources.append((song, status, reason))
        if song is not None:
            resolved_song_sources[song.song_id].append(len(resolved_sources) - 1)

    for source_indexes in resolved_song_sources.values():
        if len(source_indexes) <= 1:
            continue
        song = resolved_sources[source_indexes[0]][0]
        if song is None or not song.grand_prix_play_available:
            continue
        for source_index in source_indexes:
            resolved_sources[source_index] = (
                None,
                "ambiguous_gp_candidate",
                "multiple_official_song_entries_for_master_song",
            )

    official_keys_by_song: dict[str, set[tuple[str, str]]] = defaultdict(set)
    matched_song_ids: set[str] = set()
    for source, (song, status, reason) in zip(
        ddrworld.songs, resolved_sources, strict=True
    ):

        if song is None:
            counts[status] += len(source.charts)
            counts[f"{status}_song_count"] += 1
            for chart in source.charts:
                rows.append(
                    _ddrworld_report_row(
                        source=source,
                        chart=chart,
                        status=status,
                        reason=reason,
                    )
                )
            continue

        if not song.grand_prix_play_available:
            counts["excluded_non_gp"] += len(source.charts)
            counts["excluded_non_gp_song_count"] += 1
            for chart in source.charts:
                chart_id = stable_id(
                    "chart", song.song_id, chart.play_style, chart.difficulty
                )
                rows.append(
                    _ddrworld_report_row(
                        source=source,
                        chart=chart,
                        status="excluded_non_gp",
                        reason="song_is_not_grand_prix_play_available",
                        song=song,
                        wiki_chart=charts_by_id.get(chart_id),
                    )
                )
            continue

        matched_song_ids.add(song.song_id)
        note = _ddrworld_chart_note(source, ddrworld)
        for official_chart in source.charts:
            chart_key = (official_chart.play_style, official_chart.difficulty)
            official_keys_by_song[song.song_id].add(chart_key)
            chart_id = stable_id("chart", song.song_id, *chart_key)
            existing = charts_by_id.get(chart_id)
            if existing is None:
                notes = "; ".join(
                    value for value in (song.availability, note) if value
                )
                charts_by_id[chart_id] = MasterChart(
                    chart_id=chart_id,
                    song_id=song.song_id,
                    play_style=official_chart.play_style,
                    difficulty=official_chart.difficulty,
                    level=official_chart.level,
                    raw_level=official_chart.raw_level,
                    shock_arrow=official_chart.shock_arrow,
                    is_removed=("削" in song.availability or "×" in song.availability),
                    is_limited=bool(song.availability),
                    notes=notes,
                )
                counts["official_only"] += 1
                rows.append(
                    _ddrworld_report_row(
                        source=source,
                        chart=official_chart,
                        status="official_only",
                        reason="official_chart_added_for_gp_song",
                        song=song,
                    )
                )
                continue

            charts_by_id[chart_id] = MasterChart(
                chart_id=existing.chart_id,
                song_id=existing.song_id,
                play_style=existing.play_style,
                difficulty=existing.difficulty,
                level=official_chart.level,
                raw_level=official_chart.raw_level,
                shock_arrow=official_chart.shock_arrow,
                is_removed=existing.is_removed,
                is_limited=existing.is_limited,
                notes=(
                    f"{existing.notes}; {note}" if existing.notes else note
                ),
            )
            if existing.level == official_chart.level:
                counts["level_unchanged"] += 1
            else:
                counts["level_changed"] += 1
            counts["official_override"] += 1
            rows.append(
                _ddrworld_report_row(
                    source=source,
                    chart=official_chart,
                    status="official_override",
                    reason=(
                        "official_level_matches_baseline"
                        if existing.level == official_chart.level
                        else "official_level_replaced_baseline"
                    ),
                    song=song,
                    wiki_chart=existing,
                )
            )

    original_by_song = defaultdict(list)
    for chart in original_charts:
        original_by_song[chart.song_id].append(chart)
    for song in songs:
        if not song.grand_prix_play_available:
            continue
        for wiki_chart in original_by_song[song.song_id]:
            chart_key = (wiki_chart.play_style, wiki_chart.difficulty)
            if chart_key in official_keys_by_song.get(song.song_id, set()):
                continue
            baseline_source = (
                "confirmed_challenge_supplement"
                if CONFIRMED_CHALLENGE_NOTE_MARKER in wiki_chart.notes
                else "bemaniwiki"
            )
            status = (
                "supplement_only"
                if baseline_source == "confirmed_challenge_supplement"
                else "wiki_only"
            )
            counts[status] += 1
            rows.append(
                {
                    "title": song.title,
                    "artist": song.artist,
                    "source_page": None,
                    "page_position": None,
                    "source_url": ddrworld.snapshot.source_url,
                    "play_style": wiki_chart.play_style,
                    "difficulty": wiki_chart.difficulty,
                    "official_level": None,
                    "wiki_level": (
                        wiki_chart.level if baseline_source == "bemaniwiki" else None
                    ),
                    "baseline_level": wiki_chart.level,
                    "baseline_source": baseline_source,
                    "song_id": song.song_id,
                    "chart_id": wiki_chart.chart_id,
                    "status": status,
                    "reason": (
                        "official_chart_not_present"
                        if song.song_id in matched_song_ids
                        else "official_song_not_uniquely_matched"
                    ),
                }
            )

    rows.sort(
        key=lambda row: (
            str(row["title"]),
            str(row["artist"]),
            str(row["play_style"]),
            str(row["difficulty"]),
            str(row["status"]),
        )
    )
    counts["matched_gp_song_count"] = len(matched_song_ids)
    counts["official_chart_count"] = sum(len(song.charts) for song in ddrworld.songs)
    counts["official_song_count"] = len(ddrworld.songs)
    report = {
        "schema_version": DDRWORLD_MERGE_REPORT_SCHEMA,
        "unit": "song + play_style + difficulty",
        "priority": [
            "ddrworld_official",
            "bemaniwiki",
            "confirmed_challenge_supplement",
        ],
        "source": {
            "source_url": ddrworld.snapshot.source_url,
            "content_hash": ddrworld.snapshot.content_hash,
            "snapshot_id": ddrworld.snapshot_id,
            "fetched_at": ddrworld.snapshot.fetched_at,
            "page_count": ddrworld.page_count,
            "song_count": len(ddrworld.songs),
            "chart_count": sum(len(song.charts) for song in ddrworld.songs),
            "collector_version": ddrworld.collector_version,
        },
        "counts": dict(sorted(counts.items())),
        "rows": rows,
    }
    return tuple(charts_by_id.values()), report


def ddrworld_merge_report_json(report: dict[str, Any]) -> str:
    return json.dumps(report, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def ddrworld_merge_report_hash(report: dict[str, Any]) -> str:
    return hashlib.sha256(ddrworld_merge_report_json(report).encode("utf-8")).hexdigest()


def apply_confirmed_challenge_supplements(
    songs: tuple[MasterSong, ...],
    charts: tuple[MasterChart, ...],
) -> tuple[tuple[MasterChart, ...], tuple[AppliedChallengeSupplement, ...]]:
    songs_by_title: dict[str, list[MasterSong]] = defaultdict(list)
    for song in songs:
        songs_by_title[song.title].append(song)

    charts_by_id = {chart.chart_id: chart for chart in charts}
    applied: list[AppliedChallengeSupplement] = []
    for source in CONFIRMED_CHALLENGE_SOURCES:
        matching_songs = songs_by_title.get(source.title, [])
        if not matching_songs:
            continue
        if len(matching_songs) != 1:
            raise ValueError(
                "confirmed CHALLENGE supplement title is not unique: "
                f"{source.title}"
            )
        song = matching_songs[0]
        for play_style, level in (
            ("SINGLE", source.single_level),
            ("DOUBLE", source.double_level),
        ):
            chart_id = stable_id("chart", song.song_id, play_style, "CHALLENGE")
            existing = charts_by_id.get(chart_id)
            if existing is not None:
                if existing.level != level:
                    if DDRWORLD_CHART_NOTE_MARKER not in existing.notes:
                        raise ValueError(
                            "confirmed CHALLENGE supplement conflicts with source chart: "
                            f"{source.title} {play_style} expected {level}, "
                            f"found {existing.level}"
                        )
                continue
            provenance_note = confirmed_challenge_note(
                source.source_url,
                source.acquired_on,
            )
            notes = "; ".join(
                value for value in (song.availability, provenance_note) if value
            )
            charts_by_id[chart_id] = MasterChart(
                chart_id=chart_id,
                song_id=song.song_id,
                play_style=play_style,
                difficulty="CHALLENGE",
                level=level,
                raw_level=str(level),
                shock_arrow=False,
                is_removed=("削" in song.availability or "×" in song.availability),
                is_limited=bool(song.availability),
                notes=notes,
            )
            applied.append(
                AppliedChallengeSupplement(
                    chart_id=chart_id,
                    song_id=song.song_id,
                    title=song.title,
                    play_style=play_style,
                    level=level,
                    source_url=source.source_url,
                    acquired_on=source.acquired_on,
                )
            )
    return tuple(charts_by_id.values()), tuple(applied)


def parse_master_html(
    html: str,
    *,
    source_url: str = SOURCE_URL,
    fetched_at: str | None = None,
    new_song_html: str | None = None,
    new_song_source_url: str = NEW_SONGS_SOURCE_URL,
    official_html: str | None = None,
    official_source_url: str = OFFICIAL_MUSIC_LIST_URL,
    ddrworld_html: str | bytes | None = None,
    ddrworld_source: DdrWorldSnapshot | None = None,
    ddrworld_snapshot_path: Path | None = None,
    ddrworld_source_url: str = DDRWORLD_MUSIC_SOURCE_URL,
    ddrworld_fetched_at: str | None = None,
) -> MasterBuild:
    if sum(
        value is not None
        for value in (ddrworld_html, ddrworld_source, ddrworld_snapshot_path)
    ) > 1:
        raise ValueError("DDR WORLD source must be specified by only one input")
    if ddrworld_snapshot_path is not None:
        ddrworld_source = load_ddrworld_snapshot(ddrworld_snapshot_path)
    elif ddrworld_html is not None:
        ddrworld_source = ddrworld_source_from_html(
            ddrworld_html,
            source_url=ddrworld_source_url,
            fetched_at=ddrworld_fetched_at or fetched_at,
        )

    songs_by_id: dict[str, MasterSong] = {}
    charts_by_id: dict[str, MasterChart] = {}
    songs, charts = parse_song_list_html(html)
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

    snapshot = SourceSnapshot(
        source_url=source_url,
        fetched_at=fetched_at or datetime.now(UTC).isoformat(timespec="seconds"),
        content_hash=hashlib.sha256(html.encode("utf-8")).hexdigest(),
        parser_version=PARSER_VERSION,
        html_content=html,
    )
    new_song_snapshot = None
    if new_song_html is not None:
        new_songs, new_charts = parse_song_list_html(new_song_html)
        merge_song_list_data(songs_by_id, charts_by_id, new_songs, new_charts)
        new_song_snapshot = SourceSnapshot(
            source_url=new_song_source_url,
            fetched_at=fetched_at or datetime.now(UTC).isoformat(timespec="seconds"),
            content_hash=hashlib.sha256(new_song_html.encode("utf-8")).hexdigest(),
            parser_version=PARSER_VERSION,
            html_content=new_song_html,
        )
    official_snapshot = None
    official_entries: tuple[OfficialSongAvailability, ...] = ()
    songs = tuple(songs_by_id.values())
    song_aliases: tuple[MasterSongAlias, ...] = ()
    if official_html is not None:
        official_snapshot = SourceSnapshot(
            source_url=official_source_url,
            fetched_at=fetched_at or datetime.now(UTC).isoformat(timespec="seconds"),
            content_hash=hashlib.sha256(official_html.encode("utf-8")).hexdigest(),
            parser_version=PARSER_VERSION,
            html_content=official_html,
        )
        official_entries = parse_official_music_list_html(official_html)
        songs, song_aliases = apply_official_availability(
            songs,
            official_entries,
        )
        songs = add_official_only_songs(songs, official_entries)
    charts, confirmed_challenge_supplements = apply_confirmed_challenge_supplements(
        songs,
        tuple(charts_by_id.values()),
    )
    ddrworld_merge_report = None
    if ddrworld_source is not None:
        charts, ddrworld_merge_report = merge_ddrworld_chart_data(
            songs,
            charts,
            ddrworld_source,
            song_aliases=song_aliases,
            official_availability_entries=official_entries,
        )
        charts_by_id = {chart.chart_id: chart for chart in charts}
        confirmed_challenge_supplements = tuple(
            supplement
            for supplement in confirmed_challenge_supplements
            if charts_by_id[supplement.chart_id].level == supplement.level
        )
    return MasterBuild(
        songs=songs,
        charts=charts,
        snapshot=snapshot,
        song_aliases=song_aliases,
        official_snapshot=official_snapshot,
        new_song_snapshot=new_song_snapshot,
        confirmed_challenge_supplements=confirmed_challenge_supplements,
        ddrworld_snapshot=ddrworld_source,
        ddrworld_merge_report=ddrworld_merge_report,
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

        CREATE TABLE song_aliases (
          alias_id TEXT PRIMARY KEY,
          song_id TEXT NOT NULL REFERENCES songs(song_id) ON DELETE CASCADE,
          alias_title TEXT NOT NULL,
          alias_artist TEXT NOT NULL,
          alias_type TEXT NOT NULL,
          source TEXT NOT NULL,
          UNIQUE (song_id, alias_title, alias_artist, alias_type)
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
        CREATE INDEX idx_song_aliases_song_id ON song_aliases(song_id);
        CREATE INDEX idx_song_aliases_title ON song_aliases(alias_title);
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
    supplement_json = confirmed_challenge_supplements_json(
        build.confirmed_challenge_supplements
    )
    supplement_hash = confirmed_challenge_supplements_hash(
        build.confirmed_challenge_supplements
    )
    if master_version is None:
        version_parts = [
            f"{source_kind}\0{snapshot.content_hash}"
            for source_kind, snapshot in (
                ("primary", build.snapshot),
                ("new-song", build.new_song_snapshot),
                ("official", build.official_snapshot),
                (
                    "ddrworld",
                    None
                    if build.ddrworld_snapshot is None
                    else build.ddrworld_snapshot.snapshot,
                ),
            )
            if snapshot is not None
        ]
        version_parts.append(f"confirmed-challenge\0{supplement_hash}")
        version_material = "\0".join(version_parts)
        master_version = hashlib.sha256(version_material.encode("ascii")).hexdigest()[:12]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.exists():
        output_path.unlink()

    with closing(sqlite3.connect(output_path)) as connection, connection:
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
        connection.executemany(
            """
            INSERT INTO song_aliases (
              alias_id, song_id, alias_title, alias_artist, alias_type, source
            )
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            [
                (
                    alias.alias_id,
                    alias.song_id,
                    alias.alias_title,
                    alias.alias_artist,
                    alias.alias_type,
                    alias.source,
                )
                for alias in build.song_aliases
            ],
        )
        metadata = {
            "master_version": master_version,
            "source_url": build.snapshot.source_url,
            "generated_at": generated_at,
            "generator_version": generator_version,
            "source_hash": build.snapshot.content_hash,
            "confirmed_challenge_chart_count": str(
                len(build.confirmed_challenge_supplements)
            ),
            "confirmed_challenge_supplement_hash": supplement_hash,
            "confirmed_challenge_supplement_json": supplement_json,
            "song_count": str(len(build.songs)),
            "chart_count": str(len(build.charts)),
            "song_alias_count": str(len(build.song_aliases)),
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
                    in {
                        "title_artist",
                        "unique_title",
                        "alias_title_artist",
                        "alias_unique_title",
                        "official_only",
                    }
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
        if build.new_song_snapshot is not None:
            metadata.update(
                {
                    "new_song_source_url": build.new_song_snapshot.source_url,
                    "new_song_source_hash": build.new_song_snapshot.content_hash,
                }
            )
        if build.ddrworld_snapshot is not None:
            if build.ddrworld_merge_report is None:
                raise ValueError("DDR WORLD snapshot is missing its merge report")
            report_json = ddrworld_merge_report_json(build.ddrworld_merge_report)
            metadata.update(
                {
                    "ddrworld_source_url": build.ddrworld_snapshot.snapshot.source_url,
                    "ddrworld_source_hash": build.ddrworld_snapshot.snapshot.content_hash,
                    "ddrworld_snapshot_id": build.ddrworld_snapshot.snapshot_id,
                    "ddrworld_fetched_at": build.ddrworld_snapshot.snapshot.fetched_at,
                    "ddrworld_parser_version": build.ddrworld_snapshot.snapshot.parser_version,
                    "ddrworld_collector_version": build.ddrworld_snapshot.collector_version,
                    "ddrworld_page_count": str(build.ddrworld_snapshot.page_count),
                    "ddrworld_song_count": str(len(build.ddrworld_snapshot.songs)),
                    "ddrworld_chart_count": str(build.ddrworld_snapshot.chart_count),
                    "ddrworld_merge_report_hash": ddrworld_merge_report_hash(
                        build.ddrworld_merge_report
                    ),
                    "ddrworld_merge_report_json": report_json,
                }
            )
        connection.executemany(
            "INSERT INTO master_metadata (key, value) VALUES (?, ?)",
            sorted(metadata.items()),
        )
        for snapshot in (
            build.snapshot,
            build.official_snapshot,
            build.new_song_snapshot,
        ):
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
        if build.ddrworld_snapshot is not None:
            snapshot = build.ddrworld_snapshot.snapshot
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
        "song_aliases": len(build.song_aliases),
        "source_hash": build.snapshot.content_hash,
        "official_source_hash": (
            None if build.official_snapshot is None else build.official_snapshot.content_hash
        ),
        "new_song_source_hash": (
            None if build.new_song_snapshot is None else build.new_song_snapshot.content_hash
        ),
        "ddrworld_source_hash": (
            None
            if build.ddrworld_snapshot is None
            else build.ddrworld_snapshot.snapshot.content_hash
        ),
        "ddrworld_merge_report_hash": (
            None
            if build.ddrworld_merge_report is None
            else ddrworld_merge_report_hash(build.ddrworld_merge_report)
        ),
        "ddrworld_merge_counts": (
            None
            if build.ddrworld_merge_report is None
            else build.ddrworld_merge_report["counts"]
        ),
        "confirmed_challenge_chart_count": len(
            build.confirmed_challenge_supplements
        ),
        "confirmed_challenge_supplement_hash": confirmed_challenge_supplements_hash(
            build.confirmed_challenge_supplements
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


def fetch_ddrworld_source(
    *,
    delay_seconds: float = 2.0,
    connect_timeout_seconds: float = 10.0,
    read_timeout_seconds: float = 30.0,
) -> DdrWorldSnapshot:
    """Fetch DDR WORLD pages through the same bounded serial policy as #193."""
    from tools.ddrworld_music_snapshot.collector import (
        MAX_PAGE_COUNT,
        SerialFetcher,
        SnapshotConfig,
        build_page_url,
    )

    config = SnapshotConfig(snapshot_id="master-ddrworld-fetch")
    fetcher = SerialFetcher(
        delay_seconds=delay_seconds,
        timeout=(connect_timeout_seconds, read_timeout_seconds),
    )
    songs: list[DdrWorldSong] = []
    page_parts: list[str] = []
    fetched_at = ""
    page_count = 0
    for offset in range(MAX_PAGE_COUNT):
        page_url = build_page_url(config, offset)
        result = fetcher.get(page_url, accept="text/html,application/xhtml+xml")
        if result.error is not None:
            raise ValueError(f"DDR WORLD page {offset} fetch failed: {result.error}")
        content_type = (result.content_type or "").split(";", 1)[0].strip().lower()
        if content_type not in {"text/html", "application/xhtml+xml"}:
            raise ValueError(
                f"DDR WORLD page {offset} has unexpected content type: "
                f"{content_type or 'missing'}"
            )
        if not result.content:
            raise ValueError(f"DDR WORLD page {offset} returned an empty response")
        page_songs = parse_ddrworld_music_page(
            result.content,
            page_offset=offset,
            page_url=page_url,
            allow_empty=True,
        )
        fetched_at = result.fetched_at
        if not page_songs:
            if not songs:
                raise ValueError("DDR WORLD page 0 is empty")
            break
        songs.extend(page_songs)
        page_parts.append(
            f"<!-- source_page={offset}; source_url={page_url}; "
            f"fetched_at={result.fetched_at} -->\n{result.content.decode('utf-8')}"
        )
        page_count += 1
    else:
        raise ValueError(
            f"DDR WORLD pages did not reach a normal empty page within {MAX_PAGE_COUNT} requests"
        )

    html_content = "\n".join(page_parts)
    snapshot = SourceSnapshot(
        source_url=DDRWORLD_MUSIC_SOURCE_URL,
        fetched_at=fetched_at,
        content_hash=hashlib.sha256(html_content.encode("utf-8")).hexdigest(),
        parser_version=PARSER_VERSION,
        html_content=html_content,
    )
    return DdrWorldSnapshot(
        songs=tuple(songs),
        snapshot=snapshot,
        snapshot_id=f"network-{snapshot.content_hash[:12]}",
        page_count=page_count,
        chart_count=sum(len(song.charts) for song in songs),
        collector_version="ddrworld-music-snapshot-v1",
    )


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
        "--new-song-input",
        type=Path,
        help=(
            "Local BEMANIWiki new-song-list HTML snapshot. "
            "If omitted, the current URL is fetched."
        ),
    )
    parser.add_argument(
        "--new-song-source-url",
        default=NEW_SONGS_SOURCE_URL,
        help="New-song-list URL recorded in master_metadata and source_snapshots.",
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
        "--ddrworld-input",
        type=Path,
        help=(
            "Complete DDR WORLD snapshot directory from the snapshot collector. "
            "If omitted, the current pages are fetched serially."
        ),
    )
    parser.add_argument(
        "--skip-ddrworld-charts",
        action="store_true",
        help="Do not fetch or apply official DDR WORLD chart levels.",
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
        help=(
            "Optional master version string. Defaults to a 12-character hash of "
            "all source snapshots and supplement metadata."
        ),
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    html = (
        args.input.read_text(encoding="utf-8")
        if args.input is not None
        else fetch_source_html(args.source_url)
    )
    new_song_html = (
        args.new_song_input.read_text(encoding="utf-8")
        if args.new_song_input is not None
        else fetch_source_html(args.new_song_source_url)
    )
    official_html = None
    if not args.skip_official_availability:
        official_html = (
            args.official_input.read_text(encoding="utf-8")
            if args.official_input is not None
            else fetch_source_html(args.official_source_url)
        )
    ddrworld_source = None
    if not args.skip_official_availability and not args.skip_ddrworld_charts:
        ddrworld_source = (
            load_ddrworld_snapshot(args.ddrworld_input)
            if args.ddrworld_input is not None
            else fetch_ddrworld_source()
        )
    build = parse_master_html(
        html,
        source_url=args.source_url,
        new_song_html=new_song_html,
        new_song_source_url=args.new_song_source_url,
        official_html=official_html,
        official_source_url=args.official_source_url,
        ddrworld_source=ddrworld_source,
    )
    write_master_database(args.output, build, master_version=args.master_version)
    summary = summarize_build(build)
    print(
        "Wrote master DB: "
        f"{args.output} ({summary['songs']} songs, {summary['charts']} charts, "
        f"source_hash={str(summary['source_hash'])[:12]})"
    )
    return 0
