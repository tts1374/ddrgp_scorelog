from __future__ import annotations

import csv
import json
import sqlite3
import string
import unicodedata
from collections.abc import Iterable
from dataclasses import dataclass
from difflib import SequenceMatcher
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageFilter, ImageOps

MATCH_STATUS_VOCABULARY = (
    "matched",
    "ambiguous",
    "not_found",
    "insufficient_input",
)
JACKET_MATCH_STATUS_VOCABULARY = (
    "matched",
    "ambiguous",
    "not_found",
    "insufficient_input",
    "missing_feature",
)
DEFAULT_SCORE_THRESHOLD = 0.92
DEFAULT_JACKET_DISTANCE_THRESHOLD = 0.24
DEFAULT_JACKET_AMBIGUITY_DELTA = 0.015
DEFAULT_TITLE_AMBIGUITY_DELTA = 0.01
DEFAULT_TITLE_LINEHASH_AMBIGUITY_DELTA = 0.01
PUNCTUATION_TO_DROP = frozenset(
    string.punctuation + "　、。，．・･：；！？（）［］【】『』「」‘’“”"
)
MIN_CONTAINMENT_MATCH_LENGTH = 5
TOP_CANDIDATE_REPORT_LIMIT = 5
JACKET_REPORT_REPRESENTATIVE_LIMIT = 3
JACKET_THUMBNAIL_SIZE = (16, 16)
JACKET_DHASH_SIZE = 8
TITLE_THUMBNAIL_SIZE = (96, 16)
TITLE_LINEHASH_SIZE = (304, 28)
TITLE_LINEHASH_SOURCE_HEIGHT = 28
TITLE_LINEHASH_LUMA_THRESHOLD = 180
TITLE_LINEHASH_CHANNEL_SPREAD_MAX = 80
TITLE_LINEHASH_VARIABLE_BIT_WEIGHT = 5.0
JACKET_MATCH_FIELDNAMES = [
    "frame_index",
    "organized_file",
    "expected_song_title",
    "expected_song_id",
    "expected_song_resolution_status",
    "expected_song_resolution_reason",
    "expected_song_grand_prix_play_available",
    "expected_song_official_availability_match",
    "input_play_style",
    "input_difficulty",
    "input_level",
    "candidate_song_count",
    "candidate_chart_count",
    "candidate_feature_count",
    "top_song_id",
    "top_chart_id",
    "top_title",
    "top_artist",
    "top_score",
    "top_distance",
    "top_feature_source",
    "top_candidates",
    "expected_jacket_distance",
    "expected_jacket_rank",
    "jacket_top_margin",
    "title_candidate_feature_count",
    "title_top_song_id",
    "title_top_chart_id",
    "title_top_title",
    "title_top_score",
    "title_top_distance",
    "title_top_feature_source",
    "title_top_candidates",
    "title_rerank_status",
    "title_rerank_reason",
    "title_ocr_raw",
    "title_ocr_text",
    "title_ocr_suffix",
    "title_ocr_top_song_id",
    "title_ocr_top_chart_id",
    "title_ocr_top_title",
    "title_ocr_top_candidates",
    "title_ocr_rerank_status",
    "title_ocr_rerank_reason",
    "title_linehash_candidate_feature_count",
    "title_linehash_diff_bit_count",
    "title_linehash_dict_status",
    "title_linehash_dict_top_song_id",
    "title_linehash_dict_top_chart_id",
    "title_linehash_dict_top_title",
    "title_linehash_dict_top_row_matches",
    "title_linehash_dict_top_candidates",
    "title_linehash_exact_status",
    "title_linehash_distance_status",
    "title_linehash_top_song_id",
    "title_linehash_top_chart_id",
    "title_linehash_top_title",
    "title_linehash_top_distance",
    "title_linehash_top_candidates",
    "title_linehash_rerank_reason",
    "identity_signal_status",
    "identity_signal_source",
    "identity_signal_song_id",
    "identity_signal_chart_id",
    "identity_signal_title",
    "identity_signal_reason",
    "jacket_match_status",
    "failure_reason",
]
JACKET_MATCH_DIAGNOSTIC_FIELDNAMES = [
    "diagnostic_scope",
    "m5_target_boundary_reason",
    "screen_type",
    "event_type",
    "confirmed_result",
    "duplicate",
    "duplicate_key",
    "timestamp_ms",
    "confirmation_mode",
    *JACKET_MATCH_FIELDNAMES,
]
JACKET_REFERENCE_COVERAGE_FIELDNAMES = [
    "coverage_scope",
    "coverage_row_id",
    "m5_target_boundary_reason",
    "frame_index",
    "organized_file",
    "expected_song_title",
    "expected_song_id",
    "expected_song_resolution_status",
    "expected_song_resolution_reason",
    "expected_song_reference_status",
    "expected_song_reference_reason",
    "expected_song_grand_prix_play_available",
    "input_play_style",
    "input_difficulty",
    "input_level",
    "chart_filter_status",
    "chart_filter_failure_reason",
    "result_jacket_feature_status",
    "candidate_song_count",
    "candidate_chart_count",
    "candidate_referenced_song_count",
    "candidate_missing_feature_song_count",
    "row_reference_status",
    "candidate_song_id",
    "candidate_title",
    "candidate_artist",
    "candidate_chart_ids",
    "candidate_chart_count_for_song",
    "reference_feature_count",
    "reference_sources",
    "candidate_reference_status",
]


@dataclass(frozen=True)
class MasterChartCandidate:
    song_id: str
    chart_id: str
    title: str
    artist: str
    play_style: str
    difficulty: str
    level: int


@dataclass(frozen=True)
class MasterSong:
    song_id: str
    title: str
    artist: str
    grand_prix_play_available: bool = False
    official_availability_match: str = ""


@dataclass(frozen=True)
class JacketFeature:
    thumbnail: np.ndarray
    histogram: np.ndarray
    dhash_bits: np.ndarray
    dhash_hex: str


@dataclass(frozen=True)
class TitleImageFeature:
    luma: np.ndarray
    edge: np.ndarray
    suffix_luma: np.ndarray
    suffix_edge: np.ndarray
    dhash_bits: np.ndarray
    dhash_hex: str
    linehash_bits: np.ndarray
    linehash_rows: tuple[str, ...]


@dataclass(frozen=True)
class JacketFeatureMasterEntry:
    organized_file: str
    source_song_title: str
    song_id: str
    title: str
    artist: str
    feature: JacketFeature


@dataclass(frozen=True)
class TitleFeatureMasterEntry:
    organized_file: str
    source_song_title: str
    song_id: str
    title: str
    artist: str
    feature: TitleImageFeature


@dataclass(frozen=True)
class TitleOcrObservation:
    raw: str
    text: str
    status: str
    failure_reason: str


def normalize_song_title(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value).casefold()
    return "".join(
        char
        for char in normalized
        if not char.isspace() and char not in PUNCTUATION_TO_DROP
    )


def normalize_play_style(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value).strip().casefold()
    aliases = {
        "sp": "SINGLE",
        "single": "SINGLE",
        "singleplay": "SINGLE",
        "dp": "DOUBLE",
        "double": "DOUBLE",
        "doubleplay": "DOUBLE",
    }
    compact = "".join(char for char in normalized if not char.isspace() and char != "_")
    return aliases.get(compact, value.strip().upper())


def normalize_difficulty(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value).strip().casefold()
    aliases = {
        "beginner": "BEGINNER",
        "basic": "BASIC",
        "difficult": "DIFFICULT",
        "expert": "EXPERT",
        "challenge": "CHALLENGE",
        "beg": "BEGINNER",
        "bas": "BASIC",
        "dif": "DIFFICULT",
        "exp": "EXPERT",
        "cha": "CHALLENGE",
    }
    return aliases.get(normalized, value.strip().upper())


def normalize_level(value: str) -> int | None:
    normalized = unicodedata.normalize("NFKC", value).strip()
    digits = "".join(char for char in normalized if char.isdigit())
    if not digits:
        return None
    return int(digits)


def load_songs(db_path: Path) -> list[MasterSong]:
    with sqlite3.connect(db_path) as connection:
        rows = connection.execute(
            """
            SELECT
              song_id, title, artist,
              grand_prix_play_available, official_availability_match
            FROM songs
            ORDER BY title, artist, song_id
            """
        ).fetchall()
    return [
        MasterSong(
            song_id=str(row[0]),
            title=str(row[1]),
            artist=str(row[2]),
            grand_prix_play_available=bool(row[3]),
            official_availability_match=str(row[4] or ""),
        )
        for row in rows
    ]


def resolve_song_by_title(
    db_path: Path,
    source_title: str,
) -> tuple[MasterSong | None, str]:
    normalized_source_title = normalize_song_title(source_title)
    if not normalized_source_title:
        return None, "missing_label"

    matches = [
        song
        for song in load_songs(db_path)
        if normalize_song_title(song.title) == normalized_source_title
    ]
    song_ids = {song.song_id for song in matches}
    if not matches:
        alias_matches = load_songs_by_alias_title(db_path, source_title)
        alias_song_ids = {song.song_id for song in alias_matches}
        if not alias_matches:
            return None, "title_not_found"
        if len(alias_song_ids) > 1:
            return None, "ambiguous_alias_title"
        return alias_matches[0], ""
    if len(song_ids) > 1:
        return None, "ambiguous_title"
    return matches[0], ""


def load_songs_by_alias_title(db_path: Path, source_title: str) -> list[MasterSong]:
    normalized_source_title = normalize_song_title(source_title)
    if not normalized_source_title:
        return []
    with sqlite3.connect(db_path) as connection:
        rows = connection.execute(
            """
            SELECT
              s.song_id, s.title, s.artist,
              s.grand_prix_play_available, s.official_availability_match,
              a.alias_title
            FROM song_aliases a
            JOIN songs s ON s.song_id = a.song_id
            ORDER BY s.title, s.artist, s.song_id
            """
        ).fetchall()
    return [
        MasterSong(
            song_id=str(row[0]),
            title=str(row[1]),
            artist=str(row[2]),
            grand_prix_play_available=bool(row[3]),
            official_availability_match=str(row[4] or ""),
        )
        for row in rows
        if normalize_song_title(str(row[5])) == normalized_source_title
    ]


def chart_filter_from_save_candidate(row: dict[str, str]) -> tuple[str, str, int] | None:
    required_fields = ("play_style", "difficulty", "level")
    for field_name in required_fields:
        if row.get(f"{field_name}_status") != "ready":
            return None

    play_style = normalize_play_style(row.get("play_style_extracted_value", ""))
    difficulty = normalize_difficulty(row.get("difficulty_extracted_value", ""))
    level = normalize_level(row.get("level_extracted_value", ""))
    if not play_style or not difficulty or level is None:
        return None
    return play_style, difficulty, level


def load_chart_candidates(
    db_path: Path,
    *,
    play_style: str,
    difficulty: str,
    level: int,
) -> list[MasterChartCandidate]:
    with sqlite3.connect(db_path) as connection:
        rows = connection.execute(
            """
            SELECT
              s.song_id, c.chart_id, s.title, s.artist,
              c.play_style, c.difficulty, c.level
            FROM charts c
            JOIN songs s ON s.song_id = c.song_id
            WHERE c.play_style = ?
              AND c.difficulty = ?
              AND c.level = ?
              AND s.grand_prix_play_available = 1
            ORDER BY s.title, s.artist, c.chart_id
            """,
            (play_style, difficulty, level),
        ).fetchall()
    return [
        MasterChartCandidate(
            song_id=str(row[0]),
            chart_id=str(row[1]),
            title=str(row[2]),
            artist=str(row[3]),
            play_style=str(row[4]),
            difficulty=str(row[5]),
            level=int(row[6]),
        )
        for row in rows
    ]


def title_similarity(left: str, right: str) -> float:
    if not left or not right:
        return 0.0
    full_ratio = SequenceMatcher(None, left, right).ratio()
    if len(right) >= MIN_CONTAINMENT_MATCH_LENGTH and right in left:
        return 1.0
    return full_ratio


def format_top_candidates(
    scored_candidates: Iterable[tuple[float, MasterChartCandidate]],
    *,
    limit: int = TOP_CANDIDATE_REPORT_LIMIT,
) -> str:
    parts = []
    for score, candidate in list(scored_candidates)[:limit]:
        parts.append(
            f"{score:.4f}:{candidate.title} / {candidate.artist} [{candidate.chart_id}]"
    )
    return " | ".join(parts)


def center_square(image: Image.Image) -> Image.Image:
    width, height = image.size
    side = min(width, height)
    left = (width - side) // 2
    top = (height - side) // 2
    return image.crop((left, top, left + side, top + side))


def dhash_bits(image: Image.Image, *, hash_size: int = JACKET_DHASH_SIZE) -> np.ndarray:
    grayscale = image.convert("L").resize((hash_size + 1, hash_size), Image.Resampling.LANCZOS)
    pixels = np.asarray(grayscale, dtype=np.int16)
    return (pixels[:, 1:] > pixels[:, :-1]).astype(np.float32).reshape(-1)


def bits_to_hex(bits: np.ndarray) -> str:
    bit_string = "".join("1" if bit >= 0.5 else "0" for bit in bits)
    return f"{int(bit_string, 2):0{len(bit_string) // 4}x}"


def row_bits_to_hex(bits: np.ndarray) -> str:
    padded_length = ((len(bits) + 3) // 4) * 4
    if padded_length != len(bits):
        bits = np.pad(bits, (0, padded_length - len(bits)))
    bit_string = "".join("1" if bit >= 0.5 else "0" for bit in bits)
    return f"{int(bit_string, 2):0{padded_length // 4}x}"


def title_linehash_from_image(image: Image.Image) -> tuple[np.ndarray, tuple[str, ...]]:
    title_line = image.convert("RGB").crop(
        (0, 0, image.width, min(image.height, TITLE_LINEHASH_SOURCE_HEIGHT))
    )
    resized = title_line.resize(TITLE_LINEHASH_SIZE, Image.Resampling.LANCZOS)
    rgb = np.asarray(resized, dtype=np.int16)
    red = rgb[:, :, 0]
    green = rgb[:, :, 1]
    blue = rgb[:, :, 2]
    luma = (red + green + blue) / 3
    channel_spread = np.maximum.reduce(
        [np.abs(red - green), np.abs(green - blue), np.abs(red - blue)]
    )
    bit_rows = (
        (luma >= TITLE_LINEHASH_LUMA_THRESHOLD)
        & (channel_spread <= TITLE_LINEHASH_CHANNEL_SPREAD_MAX)
    ).astype(np.float32)
    row_hexes = tuple(row_bits_to_hex(row) for row in bit_rows)
    return bit_rows.reshape(-1), row_hexes


def extract_jacket_feature(image: Image.Image) -> JacketFeature:
    square = center_square(image.convert("RGB"))
    thumbnail = np.asarray(
        square.resize(JACKET_THUMBNAIL_SIZE, Image.Resampling.LANCZOS),
        dtype=np.float32,
    ).reshape(-1) / 255.0
    rgb = np.asarray(square, dtype=np.uint8)
    histogram_parts = [
        np.histogram(rgb[:, :, channel], bins=8, range=(0, 256), density=True)[0]
        for channel in range(3)
    ]
    histogram = np.concatenate(histogram_parts).astype(np.float32)
    histogram_sum = float(histogram.sum())
    if histogram_sum:
        histogram = histogram / histogram_sum
    hash_bits = dhash_bits(square)
    return JacketFeature(
        thumbnail=thumbnail,
        histogram=histogram,
        dhash_bits=hash_bits,
        dhash_hex=bits_to_hex(hash_bits),
    )


def extract_title_image_feature(image: Image.Image) -> TitleImageFeature:
    grayscale = ImageOps.autocontrast(image.convert("L"))
    resized = grayscale.resize(TITLE_THUMBNAIL_SIZE, Image.Resampling.LANCZOS)
    suffix_left = round(grayscale.width * 0.62)
    suffix = grayscale.crop((suffix_left, 0, grayscale.width, grayscale.height))
    suffix_resized = suffix.resize((40, TITLE_THUMBNAIL_SIZE[1]), Image.Resampling.LANCZOS)
    luma = np.asarray(resized, dtype=np.float32).reshape(-1) / 255.0
    edge = np.asarray(
        resized.filter(ImageFilter.FIND_EDGES),
        dtype=np.float32,
    ).reshape(-1) / 255.0
    suffix_luma = np.asarray(suffix_resized, dtype=np.float32).reshape(-1) / 255.0
    suffix_edge = np.asarray(
        suffix_resized.filter(ImageFilter.FIND_EDGES),
        dtype=np.float32,
    ).reshape(-1) / 255.0
    hash_bits = dhash_bits(grayscale)
    linehash_bits, linehash_rows = title_linehash_from_image(image)
    return TitleImageFeature(
        luma=luma,
        edge=edge,
        suffix_luma=suffix_luma,
        suffix_edge=suffix_edge,
        dhash_bits=hash_bits,
        dhash_hex=bits_to_hex(hash_bits),
        linehash_bits=linehash_bits,
        linehash_rows=linehash_rows,
    )


def jacket_feature_distance(left: JacketFeature, right: JacketFeature) -> float:
    thumbnail_distance = float(np.mean(np.abs(left.thumbnail - right.thumbnail)))
    histogram_distance = float(np.mean(np.abs(left.histogram - right.histogram)))
    dhash_distance = float(np.mean(left.dhash_bits != right.dhash_bits))
    return (0.70 * thumbnail_distance) + (0.20 * histogram_distance) + (
        0.10 * dhash_distance
    )


def title_image_feature_distance(
    left: TitleImageFeature,
    right: TitleImageFeature,
) -> float:
    luma_distance = float(np.mean(np.abs(left.luma - right.luma)))
    edge_distance = float(np.mean(np.abs(left.edge - right.edge)))
    suffix_luma_distance = float(np.mean(np.abs(left.suffix_luma - right.suffix_luma)))
    suffix_edge_distance = float(np.mean(np.abs(left.suffix_edge - right.suffix_edge)))
    dhash_distance = float(np.mean(left.dhash_bits != right.dhash_bits))
    return (
        (0.35 * luma_distance)
        + (0.15 * edge_distance)
        + (0.30 * suffix_luma_distance)
        + (0.10 * suffix_edge_distance)
        + (0.10 * dhash_distance)
    )


def serialize_float_vector(values: np.ndarray, *, limit: int | None = None) -> str:
    vector = values if limit is None else values[:limit]
    return " ".join(f"{float(value):.4f}" for value in vector)


def format_jacket_top_candidates(
    scored_candidates: Iterable[tuple[float, MasterChartCandidate, JacketFeatureMasterEntry]],
    *,
    limit: int = TOP_CANDIDATE_REPORT_LIMIT,
) -> str:
    parts = []
    for distance, candidate, feature_entry in list(scored_candidates)[:limit]:
        score = max(0.0, 1.0 - distance)
        parts.append(
            f"{score:.4f}:{candidate.title} / {candidate.artist} "
            f"[{candidate.chart_id}; {feature_entry.organized_file}]"
        )
    return " | ".join(parts)


def format_title_top_candidates(
    scored_candidates: Iterable[tuple[float, MasterChartCandidate, TitleFeatureMasterEntry]],
    *,
    limit: int = TOP_CANDIDATE_REPORT_LIMIT,
) -> str:
    parts = []
    for distance, candidate, feature_entry in list(scored_candidates)[:limit]:
        score = max(0.0, 1.0 - distance)
        parts.append(
            f"{score:.4f}:{candidate.title} / {candidate.artist} "
            f"[{candidate.chart_id}; {feature_entry.organized_file}]"
        )
    return " | ".join(parts)


def format_title_linehash_top_candidates(
    scored_candidates: Iterable[tuple[float, MasterChartCandidate, TitleFeatureMasterEntry]],
    *,
    limit: int = TOP_CANDIDATE_REPORT_LIMIT,
) -> str:
    parts = []
    for distance, candidate, feature_entry in list(scored_candidates)[:limit]:
        score = max(0.0, 1.0 - distance)
        parts.append(
            f"{score:.4f}:{candidate.title} / {candidate.artist} "
            f"[{candidate.chart_id}; {feature_entry.organized_file}]"
        )
    return " | ".join(parts)


def format_title_linehash_dict_candidates(
    scored_candidates: Iterable[tuple[int, MasterChartCandidate, TitleFeatureMasterEntry]],
    *,
    limit: int = TOP_CANDIDATE_REPORT_LIMIT,
) -> str:
    parts = []
    for row_matches, candidate, feature_entry in list(scored_candidates)[:limit]:
        parts.append(
            f"{row_matches}:{candidate.title} / {candidate.artist} "
            f"[{candidate.chart_id}; {feature_entry.organized_file}]"
        )
    return " | ".join(parts)


def best_distances_by_song_id(
    scored_candidates: Iterable[tuple[float, MasterChartCandidate, JacketFeatureMasterEntry]],
) -> dict[str, float]:
    distances: dict[str, float] = {}
    for distance, candidate, _feature_entry in scored_candidates:
        current = distances.get(candidate.song_id)
        if current is None or distance < current:
            distances[candidate.song_id] = distance
    return distances


def top_margin_from_song_distances(
    distances_by_song_id: dict[str, float],
    top_song_id: str,
) -> float | None:
    top_distance = distances_by_song_id.get(top_song_id)
    if top_distance is None:
        return None
    other_distances = [
        distance
        for song_id, distance in distances_by_song_id.items()
        if song_id != top_song_id
    ]
    if not other_distances:
        return None
    return min(other_distances) - top_distance


def expected_rank_from_song_distances(
    distances_by_song_id: dict[str, float],
    expected_song_id: str,
) -> tuple[float | None, int | None]:
    if expected_song_id not in distances_by_song_id:
        return None, None
    ordered = sorted(distances_by_song_id.items(), key=lambda item: (item[1], item[0]))
    for index, (song_id, distance) in enumerate(ordered, start=1):
        if song_id == expected_song_id:
            return distance, index
    return None, None


def title_ocr_empty_fields(status: str = "not_run", reason: str = "") -> dict[str, str]:
    return {
        "title_ocr_raw": "",
        "title_ocr_text": "",
        "title_ocr_suffix": "",
        "title_ocr_top_song_id": "",
        "title_ocr_top_chart_id": "",
        "title_ocr_top_title": "",
        "title_ocr_top_candidates": "",
        "title_ocr_rerank_status": status,
        "title_ocr_rerank_reason": reason,
    }


def title_linehash_empty_fields(
    status: str = "not_run",
    reason: str = "",
) -> dict[str, str]:
    return {
        "title_linehash_candidate_feature_count": "0",
        "title_linehash_diff_bit_count": "0",
        "title_linehash_dict_status": status,
        "title_linehash_dict_top_song_id": "",
        "title_linehash_dict_top_chart_id": "",
        "title_linehash_dict_top_title": "",
        "title_linehash_dict_top_row_matches": "",
        "title_linehash_dict_top_candidates": "",
        "title_linehash_exact_status": status,
        "title_linehash_distance_status": status,
        "title_linehash_top_song_id": "",
        "title_linehash_top_chart_id": "",
        "title_linehash_top_title": "",
        "title_linehash_top_distance": "",
        "title_linehash_top_candidates": "",
        "title_linehash_rerank_reason": reason,
    }


def empty_title_rerank_fields(status: str = "not_run", reason: str = "") -> dict[str, str]:
    return {
        "title_candidate_feature_count": "0",
        "title_top_song_id": "",
        "title_top_chart_id": "",
        "title_top_title": "",
        "title_top_score": "",
        "title_top_distance": "",
        "title_top_feature_source": "",
        "title_top_candidates": "",
        "title_rerank_status": status,
        "title_rerank_reason": reason,
        **title_ocr_empty_fields(),
        **title_linehash_empty_fields(),
    }


def identity_signal_empty_fields(
    status: str,
    reason: str,
) -> dict[str, str]:
    return {
        "identity_signal_status": status,
        "identity_signal_source": "",
        "identity_signal_song_id": "",
        "identity_signal_chart_id": "",
        "identity_signal_title": "",
        "identity_signal_reason": reason,
    }


def identity_signal_fields(row: dict[str, str]) -> dict[str, str]:
    jacket_status = row.get("jacket_match_status", "")
    if jacket_status == "matched":
        return {
            "identity_signal_status": "jacket_resolved_candidate",
            "identity_signal_source": "jacket_feature",
            "identity_signal_song_id": row.get("top_song_id", ""),
            "identity_signal_chart_id": row.get("top_chart_id", ""),
            "identity_signal_title": row.get("top_title", ""),
            "identity_signal_reason": "jacket_feature_unique_candidate",
        }

    if jacket_status == "ambiguous":
        if row.get("title_linehash_dict_status") == "resolved_candidate":
            return {
                "identity_signal_status": "composite_resolved_candidate",
                "identity_signal_source": "title_linehash_dict",
                "identity_signal_song_id": row.get("title_linehash_dict_top_song_id", ""),
                "identity_signal_chart_id": row.get("title_linehash_dict_top_chart_id", ""),
                "identity_signal_title": row.get("title_linehash_dict_top_title", ""),
                "identity_signal_reason": (
                    "jacket_ambiguous_title_linehash_dict_resolved"
                ),
            }
        if row.get("title_ocr_rerank_status") == "resolved_candidate":
            return {
                "identity_signal_status": "composite_resolved_candidate",
                "identity_signal_source": "title_ocr_suffix",
                "identity_signal_song_id": row.get("title_ocr_top_song_id", ""),
                "identity_signal_chart_id": row.get("title_ocr_top_chart_id", ""),
                "identity_signal_title": row.get("title_ocr_top_title", ""),
                "identity_signal_reason": "jacket_ambiguous_title_ocr_suffix_resolved",
            }
        if row.get("title_rerank_status") == "resolved_candidate":
            return {
                "identity_signal_status": "composite_resolved_candidate",
                "identity_signal_source": "title_image_feature",
                "identity_signal_song_id": row.get("title_top_song_id", ""),
                "identity_signal_chart_id": row.get("title_top_chart_id", ""),
                "identity_signal_title": row.get("title_top_title", ""),
                "identity_signal_reason": "jacket_ambiguous_title_image_resolved",
            }
        return identity_signal_empty_fields(
            "unresolved_ambiguous",
            "jacket_ambiguous_without_auxiliary_resolution",
        )

    if jacket_status in {"insufficient_input", "missing_feature", "not_found"}:
        return identity_signal_empty_fields(
            f"unresolved_{jacket_status}",
            row.get("failure_reason", "") or jacket_status,
        )

    return identity_signal_empty_fields(
        "unresolved_unknown",
        jacket_status or "missing_jacket_match_status",
    )


def with_identity_signal_fields(row: dict[str, str]) -> dict[str, str]:
    return {**row, **identity_signal_fields(row)}


def extract_type_suffix(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value).casefold()
    compact = "".join(char for char in normalized if not char.isspace())
    for suffix in ("type1", "type2", "type3"):
        if suffix in compact:
            return suffix.upper()
    return ""


def title_ocr_rerank_fields_for_ambiguous_candidates(
    *,
    title_ocr_observation: TitleOcrObservation | None,
    candidates: Iterable[MasterChartCandidate],
    ambiguous_song_ids: set[str],
) -> dict[str, str]:
    if title_ocr_observation is None:
        return title_ocr_empty_fields(
            status="missing_ocr",
            reason="title_ocr_not_run",
        )

    base_fields = {
        "title_ocr_raw": title_ocr_observation.raw,
        "title_ocr_text": title_ocr_observation.text,
        "title_ocr_suffix": "",
        "title_ocr_top_song_id": "",
        "title_ocr_top_chart_id": "",
        "title_ocr_top_title": "",
        "title_ocr_top_candidates": "",
    }
    if title_ocr_observation.status != "ok":
        return {
            **base_fields,
            "title_ocr_rerank_status": "missing_ocr",
            "title_ocr_rerank_reason": (
                title_ocr_observation.failure_reason
                or title_ocr_observation.status
                or "title_ocr_not_ok"
            ),
        }
    if not title_ocr_observation.text:
        return {
            **base_fields,
            "title_ocr_rerank_status": "missing_ocr",
            "title_ocr_rerank_reason": "empty_ocr",
        }

    suffix = extract_type_suffix(title_ocr_observation.text)
    if not suffix:
        return {
            **base_fields,
            "title_ocr_rerank_status": "no_suffix",
            "title_ocr_rerank_reason": "type_suffix_not_detected",
        }

    suffix_token = suffix.casefold()
    candidate_matches = [
        candidate
        for candidate in candidates
        if candidate.song_id in ambiguous_song_ids
        and suffix_token in normalize_song_title(candidate.title)
    ]
    candidate_matches.sort(
        key=lambda candidate: (candidate.title, candidate.artist, candidate.chart_id)
    )
    top_candidates = " | ".join(
        f"{candidate.title} / {candidate.artist} [{candidate.chart_id}]"
        for candidate in candidate_matches[:TOP_CANDIDATE_REPORT_LIMIT]
    )
    matched_song_ids = {candidate.song_id for candidate in candidate_matches}
    status = "resolved_candidate"
    reason = ""
    if not candidate_matches:
        status = "no_candidate_suffix_match"
        reason = "suffix_not_in_ambiguous_candidates"
    elif len(matched_song_ids) > 1:
        status = "ambiguous_candidate"
        reason = "suffix_matches_multiple_ambiguous_songs"

    top_candidate = candidate_matches[0] if candidate_matches else None
    return {
        **base_fields,
        "title_ocr_suffix": suffix,
        "title_ocr_top_song_id": "" if top_candidate is None else top_candidate.song_id,
        "title_ocr_top_chart_id": "" if top_candidate is None else top_candidate.chart_id,
        "title_ocr_top_title": "" if top_candidate is None else top_candidate.title,
        "title_ocr_top_candidates": top_candidates,
        "title_ocr_rerank_status": status,
        "title_ocr_rerank_reason": reason,
    }


def title_rerank_fields_for_ambiguous_candidates(
    *,
    result_title_feature: TitleImageFeature | None,
    title_feature_master_entries: Iterable[TitleFeatureMasterEntry],
    candidates: Iterable[MasterChartCandidate],
    ambiguous_song_ids: set[str],
    target_organized_file: str,
    ambiguity_delta: float = DEFAULT_TITLE_AMBIGUITY_DELTA,
) -> dict[str, str]:
    if result_title_feature is None:
        return empty_title_rerank_fields(
            status="missing_feature",
            reason="result_title_feature_unavailable",
        )

    entries_by_song_id: dict[str, list[TitleFeatureMasterEntry]] = {}
    for entry in title_feature_master_entries:
        if entry.organized_file == target_organized_file:
            continue
        if entry.song_id in ambiguous_song_ids:
            entries_by_song_id.setdefault(entry.song_id, []).append(entry)

    scored_candidates: list[tuple[float, MasterChartCandidate, TitleFeatureMasterEntry]] = []
    for candidate in candidates:
        if candidate.song_id not in ambiguous_song_ids:
            continue
        for entry in entries_by_song_id.get(candidate.song_id, []):
            scored_candidates.append(
                (
                    title_image_feature_distance(result_title_feature, entry.feature),
                    candidate,
                    entry,
                )
            )

    if not scored_candidates:
        return empty_title_rerank_fields(
            status="missing_feature",
            reason="no_candidate_title_features",
        )

    scored_candidates.sort(
        key=lambda item: (item[0], item[1].title, item[1].artist, item[1].chart_id)
    )
    top_distance, top_candidate, top_feature_entry = scored_candidates[0]
    near_top = [
        item
        for item in scored_candidates
        if item[0] - top_distance <= ambiguity_delta
        and item[1].song_id != top_candidate.song_id
    ]
    status = "ambiguous_candidate" if near_top else "resolved_candidate"
    reason = "near_top_title_distance" if near_top else ""
    return {
        "title_candidate_feature_count": str(len(scored_candidates)),
        "title_top_song_id": top_candidate.song_id,
        "title_top_chart_id": top_candidate.chart_id,
        "title_top_title": top_candidate.title,
        "title_top_score": f"{max(0.0, 1.0 - top_distance):.4f}",
        "title_top_distance": f"{top_distance:.4f}",
        "title_top_feature_source": top_feature_entry.organized_file,
        "title_top_candidates": format_title_top_candidates(scored_candidates),
        "title_rerank_status": status,
        "title_rerank_reason": reason,
    }


def title_linehash_distance(
    left: TitleImageFeature,
    right: TitleImageFeature,
    variable_bit_mask: np.ndarray,
) -> float:
    if len(left.linehash_bits) != len(right.linehash_bits):
        return 1.0
    weights = np.ones(len(left.linehash_bits), dtype=np.float32)
    if len(variable_bit_mask) == len(weights):
        weights = weights + (
            variable_bit_mask.astype(np.float32) * (TITLE_LINEHASH_VARIABLE_BIT_WEIGHT - 1.0)
        )
    mismatches = left.linehash_bits != right.linehash_bits
    return float(np.sum(weights * mismatches) / np.sum(weights))


def title_linehash_row_match_count(
    left: TitleImageFeature,
    right: TitleImageFeature,
) -> int:
    return sum(
        1
        for left_row, right_row in zip(
            left.linehash_rows,
            right.linehash_rows,
            strict=True,
        )
        if left_row == right_row and int(left_row, 16) != 0
    )


def title_linehash_fields_for_ambiguous_candidates(
    *,
    result_title_feature: TitleImageFeature | None,
    title_feature_master_entries: Iterable[TitleFeatureMasterEntry],
    candidates: Iterable[MasterChartCandidate],
    ambiguous_song_ids: set[str],
    target_organized_file: str,
    ambiguity_delta: float = DEFAULT_TITLE_LINEHASH_AMBIGUITY_DELTA,
) -> dict[str, str]:
    if result_title_feature is None:
        return title_linehash_empty_fields(
            status="missing_feature",
            reason="result_title_linehash_unavailable",
        )

    entries_by_song_id: dict[str, list[TitleFeatureMasterEntry]] = {}
    for entry in title_feature_master_entries:
        if entry.organized_file == target_organized_file:
            continue
        if entry.song_id in ambiguous_song_ids:
            entries_by_song_id.setdefault(entry.song_id, []).append(entry)

    candidate_entries: list[tuple[MasterChartCandidate, TitleFeatureMasterEntry]] = []
    for candidate in candidates:
        if candidate.song_id not in ambiguous_song_ids:
            continue
        for entry in entries_by_song_id.get(candidate.song_id, []):
            candidate_entries.append((candidate, entry))

    if not candidate_entries:
        return title_linehash_empty_fields(
            status="missing_feature",
            reason="no_candidate_title_linehash_features",
        )

    reference_bits = np.asarray(
        [entry.feature.linehash_bits for _candidate, entry in candidate_entries],
        dtype=np.float32,
    )
    variable_bit_mask = reference_bits.max(axis=0) != reference_bits.min(axis=0)
    diff_bit_count = int(np.sum(variable_bit_mask))

    exact_matches = [
        (candidate, entry)
        for candidate, entry in candidate_entries
        if entry.feature.linehash_rows == result_title_feature.linehash_rows
    ]
    exact_song_ids = {candidate.song_id for candidate, _entry in exact_matches}
    exact_status = "no_exact_match"
    if exact_matches and len(exact_song_ids) == 1:
        exact_status = "resolved_candidate"
    elif len(exact_song_ids) > 1:
        exact_status = "ambiguous_candidate"

    dict_scored_candidates = [
        (
            title_linehash_row_match_count(result_title_feature, entry.feature),
            candidate,
            entry,
        )
        for candidate, entry in candidate_entries
    ]
    dict_scored_candidates.sort(
        key=lambda item: (-item[0], item[1].title, item[1].artist, item[1].chart_id)
    )
    top_dict_row_matches, top_dict_candidate, _top_dict_feature_entry = (
        dict_scored_candidates[0]
    )
    dict_best_by_song_id: dict[str, tuple[int, MasterChartCandidate]] = {}
    for row_matches, candidate, _entry in dict_scored_candidates:
        current = dict_best_by_song_id.get(candidate.song_id)
        if current is None or row_matches > current[0]:
            dict_best_by_song_id[candidate.song_id] = (row_matches, candidate)
    top_song_row_matches = dict_best_by_song_id[top_dict_candidate.song_id][0]
    tied_top_songs = [
        song_id
        for song_id, (row_matches, _candidate) in dict_best_by_song_id.items()
        if song_id != top_dict_candidate.song_id
        and row_matches == top_song_row_matches
    ]
    dict_status = "resolved_candidate"
    dict_reason = ""
    if top_dict_row_matches <= 0:
        dict_status = "no_dict_match"
        dict_reason = "no_matching_title_linehash_rows"
    elif tied_top_songs:
        dict_status = "ambiguous_candidate"
        dict_reason = "tied_title_linehash_dict_row_matches"

    scored_candidates = [
        (
            title_linehash_distance(
                result_title_feature,
                entry.feature,
                variable_bit_mask,
            ),
            candidate,
            entry,
        )
        for candidate, entry in candidate_entries
    ]
    scored_candidates.sort(
        key=lambda item: (item[0], item[1].title, item[1].artist, item[1].chart_id)
    )
    top_distance, top_candidate, _top_feature_entry = scored_candidates[0]
    near_top = [
        item
        for item in scored_candidates
        if item[0] - top_distance <= ambiguity_delta
        and item[1].song_id != top_candidate.song_id
    ]
    distance_status = "ambiguous_candidate" if near_top else "resolved_candidate"
    distance_reason = "near_top_title_linehash_distance" if near_top else ""
    reason = dict_reason or distance_reason
    return {
        "title_linehash_candidate_feature_count": str(len(scored_candidates)),
        "title_linehash_diff_bit_count": str(diff_bit_count),
        "title_linehash_dict_status": dict_status,
        "title_linehash_dict_top_song_id": top_dict_candidate.song_id,
        "title_linehash_dict_top_chart_id": top_dict_candidate.chart_id,
        "title_linehash_dict_top_title": top_dict_candidate.title,
        "title_linehash_dict_top_row_matches": str(top_dict_row_matches),
        "title_linehash_dict_top_candidates": format_title_linehash_dict_candidates(
            dict_scored_candidates
        ),
        "title_linehash_exact_status": exact_status,
        "title_linehash_distance_status": distance_status,
        "title_linehash_top_song_id": top_candidate.song_id,
        "title_linehash_top_chart_id": top_candidate.chart_id,
        "title_linehash_top_title": top_candidate.title,
        "title_linehash_top_distance": f"{top_distance:.4f}",
        "title_linehash_top_candidates": format_title_linehash_top_candidates(
            scored_candidates
        ),
        "title_linehash_rerank_reason": reason,
    }


def match_save_candidate_row(
    row: dict[str, str],
    db_path: Path,
    *,
    score_threshold: float = DEFAULT_SCORE_THRESHOLD,
) -> dict[str, str]:
    raw_title = row.get("song_title_extracted_value", "")
    normalized_title = normalize_song_title(raw_title)
    base_result = {
        "frame_index": row.get("frame_index", ""),
        "organized_file": row.get("organized_file", ""),
        "input_song_title": raw_title,
        "normalized_song_title": normalized_title,
        "input_play_style": row.get("play_style_extracted_value", ""),
        "input_difficulty": row.get("difficulty_extracted_value", ""),
        "input_level": row.get("level_extracted_value", ""),
        "candidate_song_count": "0",
        "candidate_chart_count": "0",
        "top_song_id": "",
        "top_chart_id": "",
        "top_title": "",
        "top_artist": "",
        "top_score": "",
        "top_candidates": "",
        "match_status": "insufficient_input",
        "failure_reason": "",
    }

    if row.get("song_title_status") != "ready" or not normalized_title:
        return {
            **base_result,
            "failure_reason": row.get("song_title_failure_reason") or "song_title_not_ready",
        }

    chart_filter = chart_filter_from_save_candidate(row)
    if chart_filter is None:
        missing = [
            field_name
            for field_name in ("play_style", "difficulty", "level")
            if row.get(f"{field_name}_status") != "ready"
        ]
        reason = "chart_fields_not_ready"
        if missing:
            reason = "chart_fields_not_ready:" + ",".join(missing)
        return {**base_result, "failure_reason": reason}

    play_style, difficulty, level = chart_filter
    candidates = load_chart_candidates(
        db_path,
        play_style=play_style,
        difficulty=difficulty,
        level=level,
    )
    song_ids = {candidate.song_id for candidate in candidates}
    base_result = {
        **base_result,
        "input_play_style": play_style,
        "input_difficulty": difficulty,
        "input_level": str(level),
        "candidate_song_count": str(len(song_ids)),
        "candidate_chart_count": str(len(candidates)),
    }
    if not candidates:
        return {
            **base_result,
            "match_status": "not_found",
            "failure_reason": "no_chart_candidates",
        }

    scored_candidates = sorted(
        (
            (title_similarity(normalized_title, normalize_song_title(candidate.title)), candidate)
            for candidate in candidates
        ),
        key=lambda item: (-item[0], item[1].title, item[1].artist, item[1].chart_id),
    )
    top_score, top_candidate = scored_candidates[0]
    result = {
        **base_result,
        "top_song_id": top_candidate.song_id,
        "top_chart_id": top_candidate.chart_id,
        "top_title": top_candidate.title,
        "top_artist": top_candidate.artist,
        "top_score": f"{top_score:.4f}",
        "top_candidates": format_top_candidates(scored_candidates),
    }
    if top_score < score_threshold:
        return {
            **result,
            "match_status": "not_found",
            "failure_reason": "below_score_threshold",
        }

    tied_top = [
        candidate
        for score, candidate in scored_candidates
        if round(score, 6) == round(top_score, 6)
    ]
    if len(tied_top) > 1:
        return {
            **result,
            "match_status": "ambiguous",
            "failure_reason": "tied_top_score",
        }

    return {**result, "match_status": "matched", "failure_reason": ""}


def match_jacket_save_candidate_row(
    row: dict[str, str],
    db_path: Path,
    result_feature: JacketFeature | None,
    feature_master_entries: Iterable[JacketFeatureMasterEntry],
    result_title_feature: TitleImageFeature | None = None,
    title_feature_master_entries: Iterable[TitleFeatureMasterEntry] = (),
    title_ocr_observation: TitleOcrObservation | None = None,
    *,
    distance_threshold: float = DEFAULT_JACKET_DISTANCE_THRESHOLD,
    ambiguity_delta: float = DEFAULT_JACKET_AMBIGUITY_DELTA,
) -> dict[str, str]:
    expected_song_title = row.get("song_title_expected_value", "")
    expected_song, expected_failure_reason = resolve_song_by_title(
        db_path,
        expected_song_title,
    )
    expected_song_resolution_status = "resolved" if expected_song is not None else "unresolved"
    base_result = {
        "frame_index": row.get("frame_index", ""),
        "organized_file": row.get("organized_file", ""),
        "expected_song_title": expected_song_title,
        "expected_song_id": "" if expected_song is None else expected_song.song_id,
        "expected_song_resolution_status": expected_song_resolution_status,
        "expected_song_resolution_reason": expected_failure_reason,
        "expected_song_grand_prix_play_available": (
            "" if expected_song is None else str(expected_song.grand_prix_play_available)
        ),
        "expected_song_official_availability_match": (
            "" if expected_song is None else expected_song.official_availability_match
        ),
        "input_play_style": row.get("play_style_extracted_value", ""),
        "input_difficulty": row.get("difficulty_extracted_value", ""),
        "input_level": row.get("level_extracted_value", ""),
        "candidate_song_count": "0",
        "candidate_chart_count": "0",
        "candidate_feature_count": "0",
        "top_song_id": "",
        "top_chart_id": "",
        "top_title": "",
        "top_artist": "",
        "top_score": "",
        "top_distance": "",
        "top_feature_source": "",
        "top_candidates": "",
        "expected_jacket_distance": "",
        "expected_jacket_rank": "",
        "jacket_top_margin": "",
        **empty_title_rerank_fields(),
        "jacket_match_status": "insufficient_input",
        "failure_reason": "",
    }

    chart_filter = chart_filter_from_save_candidate(row)
    if chart_filter is None:
        missing = [
            field_name
            for field_name in ("play_style", "difficulty", "level")
            if row.get(f"{field_name}_status") != "ready"
        ]
        reason = "chart_fields_not_ready"
        if missing:
            reason = "chart_fields_not_ready:" + ",".join(missing)
        return with_identity_signal_fields({**base_result, "failure_reason": reason})
    if result_feature is None:
        return with_identity_signal_fields(
            {
                **base_result,
                "jacket_match_status": "missing_feature",
                "failure_reason": "result_jacket_feature_unavailable",
            }
        )

    play_style, difficulty, level = chart_filter
    candidates = load_chart_candidates(
        db_path,
        play_style=play_style,
        difficulty=difficulty,
        level=level,
    )
    song_ids = {candidate.song_id for candidate in candidates}
    base_result = {
        **base_result,
        "input_play_style": play_style,
        "input_difficulty": difficulty,
        "input_level": str(level),
        "candidate_song_count": str(len(song_ids)),
        "candidate_chart_count": str(len(candidates)),
    }
    if not candidates:
        return with_identity_signal_fields(
            {
                **base_result,
                "jacket_match_status": "not_found",
                "failure_reason": "no_chart_candidates",
            }
        )

    features_by_song_id: dict[str, list[JacketFeatureMasterEntry]] = {}
    for entry in feature_master_entries:
        features_by_song_id.setdefault(entry.song_id, []).append(entry)

    scored_candidates: list[tuple[float, MasterChartCandidate, JacketFeatureMasterEntry]] = []
    for candidate in candidates:
        for entry in features_by_song_id.get(candidate.song_id, []):
            scored_candidates.append(
                (jacket_feature_distance(result_feature, entry.feature), candidate, entry)
            )
    base_result = {
        **base_result,
        "candidate_feature_count": str(len(scored_candidates)),
    }
    if not scored_candidates:
        return with_identity_signal_fields(
            {
                **base_result,
                "jacket_match_status": "missing_feature",
                "failure_reason": "no_candidate_jacket_features",
            }
        )

    scored_candidates.sort(
        key=lambda item: (item[0], item[1].title, item[1].artist, item[1].chart_id)
    )
    top_distance, top_candidate, top_feature_entry = scored_candidates[0]
    distances_by_song_id = best_distances_by_song_id(scored_candidates)
    expected_distance, expected_rank = expected_rank_from_song_distances(
        distances_by_song_id,
        base_result["expected_song_id"],
    )
    top_margin = top_margin_from_song_distances(distances_by_song_id, top_candidate.song_id)
    result = {
        **base_result,
        "top_song_id": top_candidate.song_id,
        "top_chart_id": top_candidate.chart_id,
        "top_title": top_candidate.title,
        "top_artist": top_candidate.artist,
        "top_score": f"{max(0.0, 1.0 - top_distance):.4f}",
        "top_distance": f"{top_distance:.4f}",
        "top_feature_source": top_feature_entry.organized_file,
        "top_candidates": format_jacket_top_candidates(scored_candidates),
        "expected_jacket_distance": (
            "" if expected_distance is None else f"{expected_distance:.4f}"
        ),
        "expected_jacket_rank": "" if expected_rank is None else str(expected_rank),
        "jacket_top_margin": "" if top_margin is None else f"{top_margin:.4f}",
    }
    if top_distance > distance_threshold:
        return with_identity_signal_fields(
            {
                **result,
                "jacket_match_status": "not_found",
                "failure_reason": "above_distance_threshold",
            }
        )

    near_top = [
        item
        for item in scored_candidates
        if item[0] - top_distance <= ambiguity_delta
        and item[1].song_id != top_candidate.song_id
    ]
    if near_top:
        ambiguous_song_ids = {top_candidate.song_id}
        ambiguous_song_ids.update(item[1].song_id for item in near_top)
        title_rerank_fields = title_rerank_fields_for_ambiguous_candidates(
            result_title_feature=result_title_feature,
            title_feature_master_entries=title_feature_master_entries,
            candidates=candidates,
            ambiguous_song_ids=ambiguous_song_ids,
            target_organized_file=row.get("organized_file", ""),
        )
        title_ocr_rerank_fields = title_ocr_rerank_fields_for_ambiguous_candidates(
            title_ocr_observation=title_ocr_observation,
            candidates=candidates,
            ambiguous_song_ids=ambiguous_song_ids,
        )
        title_linehash_rerank_fields = title_linehash_fields_for_ambiguous_candidates(
            result_title_feature=result_title_feature,
            title_feature_master_entries=title_feature_master_entries,
            candidates=candidates,
            ambiguous_song_ids=ambiguous_song_ids,
            target_organized_file=row.get("organized_file", ""),
        )
        return with_identity_signal_fields(
            {
                **result,
                **title_rerank_fields,
                **title_ocr_rerank_fields,
                **title_linehash_rerank_fields,
                "jacket_match_status": "ambiguous",
                "failure_reason": "near_top_distance",
            }
        )

    return with_identity_signal_fields(
        {**result, "jacket_match_status": "matched", "failure_reason": ""}
    )


def match_save_candidate_rows(
    rows: Iterable[dict[str, str]],
    db_path: Path,
    *,
    score_threshold: float = DEFAULT_SCORE_THRESHOLD,
) -> list[dict[str, str]]:
    return [
        match_save_candidate_row(row, db_path, score_threshold=score_threshold)
        for row in rows
    ]


def match_jacket_save_candidate_rows(
    rows: Iterable[dict[str, str]],
    db_path: Path,
    result_features_by_file: dict[str, JacketFeature],
    feature_master_entries: Iterable[JacketFeatureMasterEntry],
    result_title_features_by_file: dict[str, TitleImageFeature] | None = None,
    title_feature_master_entries: Iterable[TitleFeatureMasterEntry] = (),
    title_ocr_observations_by_file: dict[str, TitleOcrObservation] | None = None,
    *,
    distance_threshold: float = DEFAULT_JACKET_DISTANCE_THRESHOLD,
    ambiguity_delta: float = DEFAULT_JACKET_AMBIGUITY_DELTA,
) -> list[dict[str, str]]:
    feature_master_entry_list = list(feature_master_entries)
    title_feature_master_entry_list = list(title_feature_master_entries)
    result_title_features = result_title_features_by_file or {}
    title_ocr_observations = title_ocr_observations_by_file or {}
    return [
        match_jacket_save_candidate_row(
            row,
            db_path,
            result_features_by_file.get(row.get("organized_file", "")),
            feature_master_entry_list,
            result_title_features.get(row.get("organized_file", "")),
            title_feature_master_entry_list,
            title_ocr_observations.get(row.get("organized_file", "")),
            distance_threshold=distance_threshold,
            ambiguity_delta=ambiguity_delta,
        )
        for row in rows
    ]


def group_jacket_features_by_song_id(
    feature_master_entries: Iterable[JacketFeatureMasterEntry],
) -> dict[str, list[JacketFeatureMasterEntry]]:
    features_by_song_id: dict[str, list[JacketFeatureMasterEntry]] = {}
    for entry in feature_master_entries:
        features_by_song_id.setdefault(entry.song_id, []).append(entry)
    return features_by_song_id


def group_chart_candidates_by_song_id(
    candidates: Iterable[MasterChartCandidate],
) -> dict[str, list[MasterChartCandidate]]:
    candidates_by_song_id: dict[str, list[MasterChartCandidate]] = {}
    for candidate in candidates:
        candidates_by_song_id.setdefault(candidate.song_id, []).append(candidate)
    return candidates_by_song_id


def expected_song_reference_status(
    *,
    expected_song: MasterSong | None,
    expected_failure_reason: str,
    chart_filter_status: str,
    candidate_song_ids: set[str],
    features_by_song_id: dict[str, list[JacketFeatureMasterEntry]],
) -> tuple[str, str]:
    if expected_song is None:
        reason = expected_failure_reason or "missing_label"
        return "expected_unresolved", reason
    if chart_filter_status != "ready":
        return "not_evaluated", chart_filter_status
    if expected_song.song_id not in candidate_song_ids:
        return "expected_not_in_chart_candidates", "chart_filter_excluded_expected_song"
    if not features_by_song_id.get(expected_song.song_id):
        return "expected_missing_feature", "expected_song_has_no_jacket_reference"
    return "expected_referenced", ""


def reference_sources_for_song(
    entries: Iterable[JacketFeatureMasterEntry],
) -> str:
    return " | ".join(entry.organized_file for entry in entries)


def jacket_reference_coverage_rows(
    rows: Iterable[dict[str, str]],
    db_path: Path,
    result_features_by_file: dict[str, JacketFeature],
    feature_master_entries: Iterable[JacketFeatureMasterEntry],
    *,
    coverage_scope: str = "m5_jacket_save_candidate_reference_coverage",
) -> list[dict[str, str]]:
    features_by_song_id = group_jacket_features_by_song_id(feature_master_entries)
    coverage_rows: list[dict[str, str]] = []
    for row_index, row in enumerate(rows):
        expected_song_title = row.get("song_title_expected_value", "")
        expected_song, expected_failure_reason = resolve_song_by_title(
            db_path,
            expected_song_title,
        )
        expected_song_resolution_status = (
            "resolved" if expected_song is not None else "unresolved"
        )
        base_row = {
            "coverage_scope": coverage_scope,
            "coverage_row_id": str(row_index),
            "m5_target_boundary_reason": row.get(
                "m5_target_boundary_reason",
                "save_candidate",
            ),
            "frame_index": row.get("frame_index", ""),
            "organized_file": row.get("organized_file", ""),
            "expected_song_title": expected_song_title,
            "expected_song_id": "" if expected_song is None else expected_song.song_id,
            "expected_song_resolution_status": expected_song_resolution_status,
            "expected_song_resolution_reason": expected_failure_reason,
            "expected_song_grand_prix_play_available": (
                "" if expected_song is None else str(expected_song.grand_prix_play_available)
            ),
            "input_play_style": row.get("play_style_extracted_value", ""),
            "input_difficulty": row.get("difficulty_extracted_value", ""),
            "input_level": row.get("level_extracted_value", ""),
            "chart_filter_status": "ready",
            "chart_filter_failure_reason": "",
            "result_jacket_feature_status": (
                "available"
                if row.get("organized_file", "") in result_features_by_file
                else "missing"
            ),
            "candidate_song_count": "0",
            "candidate_chart_count": "0",
            "candidate_referenced_song_count": "0",
            "candidate_missing_feature_song_count": "0",
            "row_reference_status": "",
        }

        chart_filter = chart_filter_from_save_candidate(row)
        if chart_filter is None:
            missing = [
                field_name
                for field_name in ("play_style", "difficulty", "level")
                if row.get(f"{field_name}_status") != "ready"
            ]
            reason = "chart_fields_not_ready"
            if missing:
                reason = "chart_fields_not_ready:" + ",".join(missing)
            expected_status, expected_reason = expected_song_reference_status(
                expected_song=expected_song,
                expected_failure_reason=expected_failure_reason,
                chart_filter_status="insufficient_input",
                candidate_song_ids=set(),
                features_by_song_id=features_by_song_id,
            )
            coverage_rows.append(
                {
                    **base_row,
                    "chart_filter_status": "insufficient_input",
                    "chart_filter_failure_reason": reason,
                    "row_reference_status": "insufficient_input",
                    "expected_song_reference_status": expected_status,
                    "expected_song_reference_reason": expected_reason,
                    "candidate_song_id": "",
                    "candidate_title": "",
                    "candidate_artist": "",
                    "candidate_chart_ids": "",
                    "candidate_chart_count_for_song": "0",
                    "reference_feature_count": "0",
                    "reference_sources": "",
                    "candidate_reference_status": "not_evaluated",
                }
            )
            continue

        play_style, difficulty, level = chart_filter
        candidates = load_chart_candidates(
            db_path,
            play_style=play_style,
            difficulty=difficulty,
            level=level,
        )
        candidates_by_song_id = group_chart_candidates_by_song_id(candidates)
        candidate_song_ids = set(candidates_by_song_id)
        referenced_song_ids = {
            song_id
            for song_id in candidate_song_ids
            if features_by_song_id.get(song_id)
        }
        missing_feature_song_ids = candidate_song_ids - referenced_song_ids
        if not candidates:
            row_reference_status = "no_chart_candidates"
        elif not referenced_song_ids:
            row_reference_status = "no_candidate_features"
        elif missing_feature_song_ids:
            row_reference_status = "partial_referenced"
        else:
            row_reference_status = "all_referenced"
        expected_status, expected_reason = expected_song_reference_status(
            expected_song=expected_song,
            expected_failure_reason=expected_failure_reason,
            chart_filter_status="ready",
            candidate_song_ids=candidate_song_ids,
            features_by_song_id=features_by_song_id,
        )
        base_candidate_row = {
            **base_row,
            "input_play_style": play_style,
            "input_difficulty": difficulty,
            "input_level": str(level),
            "candidate_song_count": str(len(candidate_song_ids)),
            "candidate_chart_count": str(len(candidates)),
            "candidate_referenced_song_count": str(len(referenced_song_ids)),
            "candidate_missing_feature_song_count": str(len(missing_feature_song_ids)),
            "row_reference_status": row_reference_status,
            "expected_song_reference_status": expected_status,
            "expected_song_reference_reason": expected_reason,
        }
        if not candidates:
            coverage_rows.append(
                {
                    **base_candidate_row,
                    "candidate_song_id": "",
                    "candidate_title": "",
                    "candidate_artist": "",
                    "candidate_chart_ids": "",
                    "candidate_chart_count_for_song": "0",
                    "reference_feature_count": "0",
                    "reference_sources": "",
                    "candidate_reference_status": "not_evaluated",
                }
            )
            continue

        for song_id, song_candidates in sorted(
            candidates_by_song_id.items(),
            key=lambda item: (
                item[1][0].title,
                item[1][0].artist,
                item[0],
            ),
        ):
            candidate = song_candidates[0]
            feature_entries = features_by_song_id.get(song_id, [])
            coverage_rows.append(
                {
                    **base_candidate_row,
                    "candidate_song_id": song_id,
                    "candidate_title": candidate.title,
                    "candidate_artist": candidate.artist,
                    "candidate_chart_ids": " ".join(
                        chart.chart_id for chart in song_candidates
                    ),
                    "candidate_chart_count_for_song": str(len(song_candidates)),
                    "reference_feature_count": str(len(feature_entries)),
                    "reference_sources": reference_sources_for_song(feature_entries),
                    "candidate_reference_status": (
                        "referenced" if feature_entries else "missing_feature"
                    ),
                }
            )
    return coverage_rows


def summarize_master_match_rows(rows: Iterable[dict[str, str]]) -> dict[str, Any]:
    row_list = list(rows)
    status_counts: dict[str, int] = {status: 0 for status in MATCH_STATUS_VOCABULARY}
    failure_reason_counts: dict[str, int] = {}
    for row in row_list:
        status = row["match_status"]
        status_counts[status] = status_counts.get(status, 0) + 1
        failure_reason = row["failure_reason"]
        if failure_reason:
            failure_reason_counts[failure_reason] = (
                failure_reason_counts.get(failure_reason, 0) + 1
            )

    return {
        "scope": "M5 master match PoC",
        "target_boundary": "M3 save candidates from confirmed_result=true and duplicate=false",
        "target_count": len(row_list),
        "status_counts": dict(sorted(status_counts.items())),
        "failure_reason_counts": dict(sorted(failure_reason_counts.items())),
        "status_vocabulary": list(MATCH_STATUS_VOCABULARY),
        "reading_notes": [
            "matched means a unique PoC top candidate above threshold, not DB-save readiness.",
            "ambiguous, not_found, and insufficient_input remain save-blocking observations.",
            "song_title OCR text and M3 chart-field ready values are inputs, not proof.",
        ],
    }


def summarize_jacket_feature_master_rows(rows: Iterable[dict[str, str]]) -> dict[str, Any]:
    row_list = list(rows)
    status_counts: dict[str, int] = {}
    failure_reason_counts: dict[str, int] = {}
    for row in row_list:
        status = row.get("feature_status", "")
        status_counts[status] = status_counts.get(status, 0) + 1
        failure_reason = row.get("failure_reason", "")
        if failure_reason:
            failure_reason_counts[failure_reason] = (
                failure_reason_counts.get(failure_reason, 0) + 1
            )
    return {
        "scope": "M5 jacket feature master PoC",
        "source_boundary": "screen_type=song_select grid rows with metadata title labels",
        "target_count": len(row_list),
        "status_counts": dict(sorted(status_counts.items())),
        "failure_reason_counts": dict(sorted(failure_reason_counts.items())),
        "reading_notes": [
            "Feature rows come from local song_select grid preview images.",
            "Feature rows are not bundled assets.",
            "Resolved song_id uses metadata labels matched to M4 songs.title or song_aliases.",
            "accepted feature rows are local PoC references, not a distributed jacket database.",
        ],
    }


def summarize_jacket_match_rows(rows: Iterable[dict[str, str]]) -> dict[str, Any]:
    row_list = list(rows)
    status_counts: dict[str, int] = {
        status: 0 for status in JACKET_MATCH_STATUS_VOCABULARY
    }
    failure_reason_counts: dict[str, int] = {}
    title_rerank_status_counts: dict[str, int] = {}
    title_ocr_rerank_status_counts: dict[str, int] = {}
    title_linehash_dict_status_counts: dict[str, int] = {}
    title_linehash_exact_status_counts: dict[str, int] = {}
    title_linehash_distance_status_counts: dict[str, int] = {}
    identity_signal_status_counts: dict[str, int] = {}
    identity_signal_source_counts: dict[str, int] = {}
    expected_song_resolution_status_counts: dict[str, int] = {}
    expected_song_resolution_reason_counts: dict[str, int] = {}
    expected_song_grand_prix_play_available_counts: dict[str, int] = {}
    for row in row_list:
        status = row["jacket_match_status"]
        status_counts[status] = status_counts.get(status, 0) + 1
        expected_song_resolution_status = row.get("expected_song_resolution_status", "")
        if expected_song_resolution_status:
            expected_song_resolution_status_counts[expected_song_resolution_status] = (
                expected_song_resolution_status_counts.get(
                    expected_song_resolution_status,
                    0,
                )
                + 1
            )
        expected_song_resolution_reason = row.get("expected_song_resolution_reason", "")
        if expected_song_resolution_reason:
            expected_song_resolution_reason_counts[expected_song_resolution_reason] = (
                expected_song_resolution_reason_counts.get(
                    expected_song_resolution_reason,
                    0,
                )
                + 1
            )
        expected_song_grand_prix_play_available = row.get(
            "expected_song_grand_prix_play_available",
            "",
        )
        if expected_song_grand_prix_play_available:
            expected_song_grand_prix_play_available_counts[
                expected_song_grand_prix_play_available
            ] = (
                expected_song_grand_prix_play_available_counts.get(
                    expected_song_grand_prix_play_available,
                    0,
                )
                + 1
            )
        identity_status = row.get("identity_signal_status", "")
        if identity_status:
            identity_signal_status_counts[identity_status] = (
                identity_signal_status_counts.get(identity_status, 0) + 1
            )
        identity_source = row.get("identity_signal_source", "")
        if identity_source:
            identity_signal_source_counts[identity_source] = (
                identity_signal_source_counts.get(identity_source, 0) + 1
            )
        title_status = row.get("title_rerank_status", "")
        if title_status:
            title_rerank_status_counts[title_status] = (
                title_rerank_status_counts.get(title_status, 0) + 1
            )
        title_ocr_status = row.get("title_ocr_rerank_status", "")
        if title_ocr_status:
            title_ocr_rerank_status_counts[title_ocr_status] = (
                title_ocr_rerank_status_counts.get(title_ocr_status, 0) + 1
            )
        title_linehash_dict_status = row.get("title_linehash_dict_status", "")
        if title_linehash_dict_status:
            title_linehash_dict_status_counts[title_linehash_dict_status] = (
                title_linehash_dict_status_counts.get(title_linehash_dict_status, 0) + 1
            )
        title_linehash_exact_status = row.get("title_linehash_exact_status", "")
        if title_linehash_exact_status:
            title_linehash_exact_status_counts[title_linehash_exact_status] = (
                title_linehash_exact_status_counts.get(title_linehash_exact_status, 0) + 1
            )
        title_linehash_distance_status = row.get("title_linehash_distance_status", "")
        if title_linehash_distance_status:
            title_linehash_distance_status_counts[title_linehash_distance_status] = (
                title_linehash_distance_status_counts.get(title_linehash_distance_status, 0)
                + 1
            )
        failure_reason = row["failure_reason"]
        if failure_reason:
            failure_reason_counts[failure_reason] = (
                failure_reason_counts.get(failure_reason, 0) + 1
            )

    return {
        "scope": "M5 jacket match PoC",
        "target_boundary": "M3 save candidates from confirmed_result=true and duplicate=false",
        "target_count": len(row_list),
        "status_counts": dict(sorted(status_counts.items())),
        "failure_reason_counts": dict(sorted(failure_reason_counts.items())),
        "title_rerank_status_counts": dict(sorted(title_rerank_status_counts.items())),
        "title_ocr_rerank_status_counts": dict(
            sorted(title_ocr_rerank_status_counts.items())
        ),
        "title_linehash_dict_status_counts": dict(
            sorted(title_linehash_dict_status_counts.items())
        ),
        "title_linehash_exact_status_counts": dict(
            sorted(title_linehash_exact_status_counts.items())
        ),
        "title_linehash_distance_status_counts": dict(
            sorted(title_linehash_distance_status_counts.items())
        ),
        "identity_signal_status_counts": dict(
            sorted(identity_signal_status_counts.items())
        ),
        "identity_signal_source_counts": dict(
            sorted(identity_signal_source_counts.items())
        ),
        "expected_song_resolution_status_counts": dict(
            sorted(expected_song_resolution_status_counts.items())
        ),
        "expected_song_resolution_reason_counts": dict(
            sorted(expected_song_resolution_reason_counts.items())
        ),
        "expected_song_grand_prix_play_available_counts": dict(
            sorted(expected_song_grand_prix_play_available_counts.items())
        ),
        "status_vocabulary": list(JACKET_MATCH_STATUS_VOCABULARY),
        "distance_threshold": DEFAULT_JACKET_DISTANCE_THRESHOLD,
        "ambiguity_delta": DEFAULT_JACKET_AMBIGUITY_DELTA,
        "title_ambiguity_delta": DEFAULT_TITLE_AMBIGUITY_DELTA,
        "title_linehash_ambiguity_delta": DEFAULT_TITLE_LINEHASH_AMBIGUITY_DELTA,
        "reading_notes": [
            "matched means a unique PoC top jacket feature candidate, not DB-save readiness.",
            "missing_feature means the local feature master lacks a usable reference.",
            "chart fields still only narrow candidates; jacket matching is an observation signal.",
            "title_rerank_status is a diagnostic for jacket ambiguous candidates only.",
            "title_ocr_rerank_status only observes TYPE suffixes inside jacket ambiguous "
            "candidates.",
            "title_linehash dict/exact/distance statuses only rerank inside jacket "
            "ambiguous candidates.",
            "identity_signal_* columns are M5 handoff observations, not save-ready "
            "decisions.",
            "expected_song_* columns are local metadata diagnostics for review, not "
            "candidate promotion logic.",
        ],
    }


def summarize_jacket_match_diagnostic_rows(
    rows: Iterable[dict[str, str]],
) -> dict[str, Any]:
    row_list = list(rows)
    summary = summarize_jacket_match_rows(row_list)
    event_type_counts: dict[str, int] = {}
    boundary_reason_counts: dict[str, int] = {}
    confirmed_result_counts: dict[str, int] = {}
    duplicate_counts: dict[str, int] = {}
    for row in row_list:
        event_type = row.get("event_type", "")
        event_type_counts[event_type] = event_type_counts.get(event_type, 0) + 1
        boundary_reason = row.get("m5_target_boundary_reason", "")
        boundary_reason_counts[boundary_reason] = (
            boundary_reason_counts.get(boundary_reason, 0) + 1
        )
        confirmed_result = row.get("confirmed_result", "")
        confirmed_result_counts[confirmed_result] = (
            confirmed_result_counts.get(confirmed_result, 0) + 1
        )
        duplicate = row.get("duplicate", "")
        duplicate_counts[duplicate] = duplicate_counts.get(duplicate, 0) + 1

    return {
        **summary,
        "scope": "M5 jacket match diagnostic PoC",
        "target_boundary": (
            "diagnostic result-like rows including save candidates, duplicate, "
            "and unconfirmed result frames"
        ),
        "event_type_counts": dict(sorted(event_type_counts.items())),
        "m5_target_boundary_reason_counts": dict(sorted(boundary_reason_counts.items())),
        "confirmed_result_counts": dict(sorted(confirmed_result_counts.items())),
        "duplicate_counts": dict(sorted(duplicate_counts.items())),
        "reading_notes": [
            *summary["reading_notes"],
            "Diagnostic rows are not save candidates and are not mixed into "
            "jacket_match_candidates.csv.",
            "duplicate and unconfirmed rows are observation material for M5, not DB-save "
            "readiness.",
            "Chart/title inputs for diagnostics come from local metadata expected values.",
        ],
    }


def increment_count(counts: dict[str, int], value: str) -> None:
    counts[value] = counts.get(value, 0) + 1


def unique_coverage_rows_by_id(
    rows: Iterable[dict[str, str]],
) -> dict[str, dict[str, str]]:
    unique_rows: dict[str, dict[str, str]] = {}
    for row in rows:
        row_id = row.get("coverage_row_id", "")
        if row_id not in unique_rows:
            unique_rows[row_id] = row
    return unique_rows


def summarize_jacket_reference_coverage_rows(
    rows: Iterable[dict[str, str]],
) -> dict[str, Any]:
    row_list = list(rows)
    unique_rows = unique_coverage_rows_by_id(row_list)
    row_reference_status_counts: dict[str, int] = {}
    expected_reference_status_counts: dict[str, int] = {}
    expected_reference_reason_counts: dict[str, int] = {}
    candidate_reference_status_counts: dict[str, int] = {}
    boundary_reason_counts: dict[str, int] = {}
    total_candidate_songs = 0
    referenced_candidate_songs = 0
    missing_feature_candidate_songs = 0
    result_jacket_feature_missing_rows = 0
    for row in unique_rows.values():
        increment_count(row_reference_status_counts, row.get("row_reference_status", ""))
        increment_count(
            expected_reference_status_counts,
            row.get("expected_song_reference_status", ""),
        )
        expected_reason = row.get("expected_song_reference_reason", "")
        if expected_reason:
            increment_count(expected_reference_reason_counts, expected_reason)
        increment_count(
            boundary_reason_counts,
            row.get("m5_target_boundary_reason", ""),
        )
        if row.get("result_jacket_feature_status") == "missing":
            result_jacket_feature_missing_rows += 1
    for row in row_list:
        candidate_status = row.get("candidate_reference_status", "")
        if candidate_status in {"referenced", "missing_feature"}:
            total_candidate_songs += 1
            increment_count(candidate_reference_status_counts, candidate_status)
        if candidate_status == "referenced":
            referenced_candidate_songs += 1
        if candidate_status == "missing_feature":
            missing_feature_candidate_songs += 1

    return {
        "scope": "M5 jacket reference coverage PoC",
        "coverage_scope": row_list[0].get("coverage_scope", "") if row_list else "",
        "target_count": len(unique_rows),
        "coverage_row_count": len(row_list),
        "total_candidate_songs": total_candidate_songs,
        "referenced_candidate_songs": referenced_candidate_songs,
        "missing_feature_candidate_songs": missing_feature_candidate_songs,
        "result_jacket_feature_missing_rows": result_jacket_feature_missing_rows,
        "row_reference_status_counts": dict(sorted(row_reference_status_counts.items())),
        "candidate_reference_status_counts": dict(
            sorted(candidate_reference_status_counts.items())
        ),
        "expected_song_reference_status_counts": dict(
            sorted(expected_reference_status_counts.items())
        ),
        "expected_song_reference_reason_counts": dict(
            sorted(expected_reference_reason_counts.items())
        ),
        "m5_target_boundary_reason_counts": dict(sorted(boundary_reason_counts.items())),
        "reading_notes": [
            "Rows are diagnostic coverage observations, not save candidates.",
            "candidate_reference_status=missing_feature means a chart-filtered candidate "
            "song lacks a local jacket feature reference.",
            "expected_song_reference_status separates unresolved expected labels, "
            "chart-filter exclusion, and missing local references.",
            "partial_referenced and no_candidate_features are reference coverage gaps; "
            "they are not OCR failures or DB-save decisions.",
        ],
    }


def write_master_match_csv(path: Path, rows: Iterable[dict[str, str]]) -> None:
    fieldnames = [
        "frame_index",
        "organized_file",
        "input_song_title",
        "normalized_song_title",
        "input_play_style",
        "input_difficulty",
        "input_level",
        "candidate_song_count",
        "candidate_chart_count",
        "top_song_id",
        "top_chart_id",
        "top_title",
        "top_artist",
        "top_score",
        "top_candidates",
        "match_status",
        "failure_reason",
    ]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def write_master_match_summary(path: Path, summary: dict[str, Any]) -> None:
    path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_jacket_feature_master_csv(path: Path, rows: Iterable[dict[str, str]]) -> None:
    fieldnames = [
        "organized_file",
        "source_song_title",
        "normalized_song_title",
        "song_id",
        "title",
        "artist",
        "feature_status",
        "failure_reason",
        "dhash_hex",
        "histogram",
        "thumbnail_rgb",
    ]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def write_jacket_feature_label_template(
    path: Path,
    rows: Iterable[dict[str, str]],
) -> None:
    fieldnames = [
        "organized_file",
        "screen_type",
        "song_select_view",
        "preview_visible",
        "song_title",
        "expected_song_title",
        "note",
    ]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def write_jacket_feature_master_summary(path: Path, summary: dict[str, Any]) -> None:
    path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_jacket_match_csv(path: Path, rows: Iterable[dict[str, str]]) -> None:
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=JACKET_MATCH_FIELDNAMES)
        writer.writeheader()
        writer.writerows(rows)


def write_jacket_match_diagnostic_csv(
    path: Path,
    rows: Iterable[dict[str, str]],
) -> None:
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=JACKET_MATCH_DIAGNOSTIC_FIELDNAMES)
        writer.writeheader()
        writer.writerows(rows)


def write_jacket_reference_coverage_csv(
    path: Path,
    rows: Iterable[dict[str, str]],
) -> None:
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=JACKET_REFERENCE_COVERAGE_FIELDNAMES)
        writer.writeheader()
        writer.writerows(rows)


def jacket_reference_missing_representatives(
    rows: Iterable[dict[str, str]],
    *,
    limit: int = 50,
) -> list[dict[str, str]]:
    row_list = list(rows)
    representatives: list[dict[str, str]] = []
    seen_keys: set[tuple[str, str, str]] = set()

    def add_representative(row: dict[str, str], key: tuple[str, str, str]) -> None:
        if key in seen_keys or len(representatives) >= limit:
            return
        representatives.append(row)
        seen_keys.add(key)

    for row in row_list:
        expected_status = row.get("expected_song_reference_status", "")
        if expected_status in {
            "expected_missing_feature",
            "expected_not_in_chart_candidates",
            "expected_unresolved",
        }:
            add_representative(
                row,
                ("expected", row.get("coverage_row_id", ""), expected_status),
            )
    for row in row_list:
        row_status = row.get("row_reference_status", "")
        if row_status in {
            "insufficient_input",
            "no_chart_candidates",
            "no_candidate_features",
            "partial_referenced",
        }:
            add_representative(
                row,
                ("row", row.get("coverage_row_id", ""), row_status),
            )
    for row in row_list:
        if row.get("candidate_reference_status") == "missing_feature":
            add_representative(
                row,
                ("candidate", row.get("coverage_row_id", ""), "missing_feature"),
            )
    return representatives


def write_jacket_reference_coverage_summary(
    path: Path,
    summary: dict[str, Any],
) -> None:
    path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_jacket_match_summary(path: Path, summary: dict[str, Any]) -> None:
    path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_master_match_report(
    path: Path,
    rows: Iterable[dict[str, str]],
    summary: dict[str, Any],
) -> None:
    row_list = list(rows)
    lines = [
        "# M5 Master Match PoC",
        "",
        "M3保存候補行のOCR生文字列とchart-field観測値を、M4マスタDBへ照合する入口です。",
        "ここでの `matched` はPoC上の一意候補であり、DB保存可能や本番採用済み照合ではありません。",
        "",
        f"- target boundary: `{summary['target_boundary']}`",
        f"- target candidates: {summary['target_count']}",
        "- status vocabulary: `matched` / `ambiguous` / `not_found` / `insufficient_input`",
        "",
        "## Status Counts",
        "",
        f"- match_status: `{json.dumps(summary['status_counts'], sort_keys=True)}`",
        f"- failure_reason: `{json.dumps(summary['failure_reason_counts'], sort_keys=True)}`",
        "",
        "## Candidate Rows",
        "",
        "| organized_file | status | title | chart | top candidate | score | reason |",
        "|---|---|---|---|---|---|---|",
    ]
    for row in row_list[:20]:
        chart_text = " ".join(
            value
            for value in (
                row["input_play_style"],
                row["input_difficulty"],
                row["input_level"],
            )
            if value
        )
        top_text = row["top_title"]
        if row["top_artist"]:
            top_text = f"{top_text} / {row['top_artist']}"
        lines.append(
            f"| `{row['organized_file']}` | `{row['match_status']}` | "
            f"`{row['input_song_title']}` | `{chart_text}` | `{top_text}` | "
            f"`{row['top_score']}` | `{row['failure_reason']}` |"
        )
    if len(row_list) > 20:
        lines.append("| ... | ... | ... | ... | ... | ... | ... |")

    lines.extend(
        [
            "",
            "## Reading Notes",
            "",
            "- `song_title` はOCR入口の生文字列を最小正規化したものです。",
            "- `top_candidates` は上位候補の観察用で、保存可能判定ではありません。",
            "- `play_style` / `difficulty` / `level` は候補絞り込み条件です。",
            "- `ambiguous`、`not_found`、`insufficient_input` は保存不可理由へ渡す観測語彙です。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def markdown_code_cell(value: object) -> str:
    text = str(value or "").replace("|", "\\|")
    return f"`{text}`"


def jacket_report_top_candidate(row: dict[str, str]) -> str:
    top_text = row.get("top_title", "")
    if row.get("top_artist"):
        top_text = f"{top_text} / {row['top_artist']}"
    return top_text


def representative_rows_by_field(
    rows: Iterable[dict[str, str]],
    field_name: str,
    *,
    limit_per_value: int = JACKET_REPORT_REPRESENTATIVE_LIMIT,
    value_order: tuple[str, ...] = (),
) -> list[dict[str, str]]:
    counts: dict[str, int] = {}
    representatives_by_value: dict[str, list[dict[str, str]]] = {}
    first_seen_values: list[str] = []
    for row in rows:
        value = row.get(field_name, "")
        if not value:
            continue
        current_count = counts.get(value, 0)
        if current_count >= limit_per_value:
            continue
        if value not in counts:
            first_seen_values.append(value)
        counts[value] = current_count + 1
        representatives_by_value.setdefault(value, []).append(row)

    ordered_values = [
        value for value in value_order if value in representatives_by_value
    ]
    ordered_values.extend(
        value
        for value in first_seen_values
        if value not in set(ordered_values)
    )
    representatives: list[dict[str, str]] = []
    for value in ordered_values:
        representatives.extend(representatives_by_value[value])
    return representatives


def append_identity_signal_representatives(
    lines: list[str],
    row_list: list[dict[str, str]],
) -> None:
    lines.extend(
        [
            "",
            "## Identity Signal Representatives",
            "",
            "| identity signal | organized_file | expected title | jacket status | "
            "top candidate | reason |",
            "|---|---|---|---|---|---|",
        ]
    )
    representatives = representative_rows_by_field(row_list, "identity_signal_status")
    if not representatives:
        lines.append("|  |  |  |  |  |  |")
        return
    for row in representatives:
        identity_signal = " / ".join(
            value
            for value in (
                row.get("identity_signal_status", ""),
                row.get("identity_signal_source", ""),
            )
            if value
        )
        reason = row.get("identity_signal_reason") or row.get("failure_reason", "")
        lines.append(
            f"| {markdown_code_cell(identity_signal)} | "
            f"{markdown_code_cell(row.get('organized_file', ''))} | "
            f"{markdown_code_cell(row.get('expected_song_title', ''))} | "
            f"{markdown_code_cell(row.get('jacket_match_status', ''))} | "
            f"{markdown_code_cell(jacket_report_top_candidate(row))} | "
            f"{markdown_code_cell(reason)} |"
        )


def append_unresolved_identity_signal_representatives(
    lines: list[str],
    row_list: list[dict[str, str]],
) -> None:
    lines.extend(
        [
            "",
            "## Unresolved Identity Signal Representatives",
            "",
            "| identity signal | organized_file | expected title | expected song | "
            "GP | official match | jacket status | expected rank | margin | "
            "linehash dict | top candidate | reason |",
            "|---|---|---|---|---|---|---|---|---|---|---|---|",
        ]
    )
    unresolved_rows = [
        row
        for row in row_list
        if row.get("identity_signal_status", "").startswith("unresolved_")
    ]
    representatives = representative_rows_by_field(
        unresolved_rows,
        "identity_signal_status",
    )
    if not representatives:
        lines.append("|  |  |  |  |  |  |  |  |  |  |  |  |")
        return
    for row in representatives:
        expected_song = " / ".join(
            value
            for value in (
                row.get("expected_song_id", ""),
                row.get("expected_song_resolution_status", ""),
                row.get("expected_song_resolution_reason", ""),
            )
            if value
        )
        linehash_dict = " / ".join(
            value
            for value in (
                row.get("title_linehash_dict_status", ""),
                row.get("title_linehash_dict_top_title", ""),
            )
            if value
        )
        reason = row.get("identity_signal_reason") or row.get("failure_reason", "")
        lines.append(
            f"| {markdown_code_cell(row.get('identity_signal_status', ''))} | "
            f"{markdown_code_cell(row.get('organized_file', ''))} | "
            f"{markdown_code_cell(row.get('expected_song_title', ''))} | "
            f"{markdown_code_cell(expected_song)} | "
            f"{markdown_code_cell(row.get('expected_song_grand_prix_play_available', ''))} | "
            f"{markdown_code_cell(row.get('expected_song_official_availability_match', ''))} | "
            f"{markdown_code_cell(row.get('jacket_match_status', ''))} | "
            f"{markdown_code_cell(row.get('expected_jacket_rank', ''))} | "
            f"{markdown_code_cell(row.get('jacket_top_margin', ''))} | "
            f"{markdown_code_cell(linehash_dict)} | "
            f"{markdown_code_cell(jacket_report_top_candidate(row))} | "
            f"{markdown_code_cell(reason)} |"
        )


def append_boundary_representatives(
    lines: list[str],
    row_list: list[dict[str, str]],
) -> None:
    lines.extend(
        [
            "",
            "## Boundary Representatives",
            "",
            "| boundary | event | duplicate | organized_file | expected title | "
            "jacket status | identity signal | top candidate | reason |",
            "|---|---|---|---|---|---|---|---|---|",
        ]
    )
    representatives = representative_rows_by_field(
        row_list,
        "m5_target_boundary_reason",
        value_order=(
            "save_candidate",
            "unconfirmed",
            "duplicate",
            "metadata_result_not_candidate",
        ),
    )
    if not representatives:
        lines.append("|  |  |  |  |  |  |  |  |  |")
        return
    for row in representatives:
        identity_signal = " / ".join(
            value
            for value in (
                row.get("identity_signal_status", ""),
                row.get("identity_signal_source", ""),
            )
            if value
        )
        reason = row.get("identity_signal_reason") or row.get("failure_reason", "")
        lines.append(
            f"| {markdown_code_cell(row.get('m5_target_boundary_reason', ''))} | "
            f"{markdown_code_cell(row.get('event_type', ''))} | "
            f"{markdown_code_cell(row.get('duplicate', ''))} | "
            f"{markdown_code_cell(row.get('organized_file', ''))} | "
            f"{markdown_code_cell(row.get('expected_song_title', ''))} | "
            f"{markdown_code_cell(row.get('jacket_match_status', ''))} | "
            f"{markdown_code_cell(identity_signal)} | "
            f"{markdown_code_cell(jacket_report_top_candidate(row))} | "
            f"{markdown_code_cell(reason)} |"
        )


def write_jacket_reference_coverage_report(
    path: Path,
    rows: Iterable[dict[str, str]],
    summary: dict[str, Any],
) -> None:
    row_list = list(rows)
    missing_representatives = jacket_reference_missing_representatives(row_list)
    lines = [
        "# M5 Jacket Reference Coverage",
        "",
        "chart-fieldで絞った候補song_idに、ローカルjacket特徴量参照があるかを確認する診断レポートです。",
        "参照不足は参照不足として読み、近傍の別曲へ寄せた解消扱いにはしません。",
        "",
        f"- coverage scope: `{summary['coverage_scope']}`",
        f"- target rows: {summary['target_count']}",
        f"- candidate song rows: {summary['total_candidate_songs']}",
        f"- referenced candidate songs: {summary['referenced_candidate_songs']}",
        f"- missing feature candidate songs: {summary['missing_feature_candidate_songs']}",
        "",
        "## Status Counts",
        "",
        "- row_reference_status: `"
        + json.dumps(summary["row_reference_status_counts"], sort_keys=True)
        + "`",
        "- candidate_reference_status: `"
        + json.dumps(summary["candidate_reference_status_counts"], sort_keys=True)
        + "`",
        "- expected_song_reference_status: `"
        + json.dumps(summary["expected_song_reference_status_counts"], sort_keys=True)
        + "`",
        "- expected_song_reference_reason: `"
        + json.dumps(summary["expected_song_reference_reason_counts"], sort_keys=True)
        + "`",
        "- m5_target_boundary_reason: `"
        + json.dumps(summary["m5_target_boundary_reason_counts"], sort_keys=True)
        + "`",
        "",
        "## Missing Representatives",
        "",
        "| boundary | organized_file | expected title | expected reference | row coverage | "
        "candidate | candidate reference | reason |",
        "|---|---|---|---|---|---|---|---|",
    ]
    if not missing_representatives:
        lines.append("|  |  |  |  |  |  |  |  |")
    for row in missing_representatives:
        candidate_text = " / ".join(
            value
            for value in (
                row.get("candidate_song_id", ""),
                row.get("candidate_title", ""),
            )
            if value
        )
        reason = row.get("expected_song_reference_reason") or row.get(
            "chart_filter_failure_reason",
            "",
        )
        lines.append(
            f"| {markdown_code_cell(row.get('m5_target_boundary_reason', ''))} | "
            f"{markdown_code_cell(row.get('organized_file', ''))} | "
            f"{markdown_code_cell(row.get('expected_song_title', ''))} | "
            f"{markdown_code_cell(row.get('expected_song_reference_status', ''))} | "
            f"{markdown_code_cell(row.get('row_reference_status', ''))} | "
            f"{markdown_code_cell(candidate_text)} | "
            f"{markdown_code_cell(row.get('candidate_reference_status', ''))} | "
            f"{markdown_code_cell(reason)} |"
        )
    lines.extend(
        [
            "",
            "## Reading Notes",
            "",
            "- `candidate_reference_status=missing_feature` は候補song_id側の"
            "ローカル参照不足です。",
            "- `expected_unresolved` は期待曲名がM4 canonical/aliasへ解決できない状態です。",
            "- `expected_not_in_chart_candidates` は期待曲がM4で解決していても、"
            "chart-field条件の候補集合に入っていない状態です。",
            "- `expected_missing_feature` は期待曲が候補集合にあるが、"
            "song_select由来のjacket参照がない状態です。",
            "- duplicate / unconfirmed を含む診断coverageは保存候補ではありません。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_jacket_match_report(
    path: Path,
    rows: Iterable[dict[str, str]],
    summary: dict[str, Any],
) -> None:
    row_list = list(rows)
    lines = [
        "# M5 Jacket Match PoC",
        "",
        "song_select grid右上プレビュー由来のローカル特徴量マスタと、"
        "resultジャケットROIを比較する入口です。",
        "ここでの `matched` はPoC上の一意候補であり、DB保存可能や本番採用済み照合ではありません。",
        "",
        f"- target boundary: `{summary['target_boundary']}`",
        f"- target candidates: {summary['target_count']}",
        "- status vocabulary: "
        "`matched` / `ambiguous` / `not_found` / `insufficient_input` / `missing_feature`",
        "",
        "## Status Counts",
        "",
        f"- jacket_match_status: `{json.dumps(summary['status_counts'], sort_keys=True)}`",
        f"- title_rerank_status: "
        f"`{json.dumps(summary['title_rerank_status_counts'], sort_keys=True)}`",
        f"- title_ocr_rerank_status: "
        f"`{json.dumps(summary['title_ocr_rerank_status_counts'], sort_keys=True)}`",
        f"- title_linehash_dict_status: "
        f"`{json.dumps(summary['title_linehash_dict_status_counts'], sort_keys=True)}`",
        f"- title_linehash_exact_status: "
        f"`{json.dumps(summary['title_linehash_exact_status_counts'], sort_keys=True)}`",
        f"- title_linehash_distance_status: "
        f"`{json.dumps(summary['title_linehash_distance_status_counts'], sort_keys=True)}`",
        f"- identity_signal_status: "
        f"`{json.dumps(summary['identity_signal_status_counts'], sort_keys=True)}`",
        f"- identity_signal_source: "
        f"`{json.dumps(summary['identity_signal_source_counts'], sort_keys=True)}`",
        f"- expected_song_resolution_status: "
        f"`{json.dumps(summary['expected_song_resolution_status_counts'], sort_keys=True)}`",
        "- expected_song_grand_prix_play_available: `"
        + json.dumps(
            summary["expected_song_grand_prix_play_available_counts"],
            sort_keys=True,
        )
        + "`",
        f"- failure_reason: `{json.dumps(summary['failure_reason_counts'], sort_keys=True)}`",
    ]
    append_identity_signal_representatives(lines, row_list)
    append_unresolved_identity_signal_representatives(lines, row_list)
    lines.extend(
        [
            "",
            "## Candidate Rows",
            "",
            "| organized_file | status | chart | top candidate | score | distance | "
            "expected rank | margin | identity signal | reason |",
            "|---|---|---|---|---|---|---|---|---|---|",
        ]
    )
    for row in row_list[:20]:
        chart_text = " ".join(
            value
            for value in (
                row["input_play_style"],
                row["input_difficulty"],
                row["input_level"],
            )
            if value
        )
        top_text = jacket_report_top_candidate(row)
        lines.append(
            f"| `{row['organized_file']}` | `{row['jacket_match_status']}` | "
            f"`{chart_text}` | `{top_text}` | `{row['top_score']}` | "
            f"`{row['top_distance']}` | `{row.get('expected_jacket_rank', '')}` | "
            f"`{row.get('jacket_top_margin', '')}` | "
            f"`{row.get('identity_signal_status', '')}`"
            f" / `{row.get('identity_signal_source', '')}` | "
            f"`{row['failure_reason']}` |"
        )
    if len(row_list) > 20:
        lines.append("| ... | ... | ... | ... | ... | ... | ... | ... | ... | ... |")

    lines.extend(
        [
            "",
            "## Reading Notes",
            "",
            "- 特徴量マスタはローカル `song_select` gridプレビューから作るPoC出力です。",
            "- `play_style` / `difficulty` / `level` で候補songを絞った後、"
            "その候補にある特徴量だけを比較します。",
            "- `missing_feature` はローカル特徴量参照不足であり、"
            "OCR失敗や保存可否とは別に読みます。",
            "- `expected_jacket_*` はローカル期待値に基づく診断列です。",
            "- `expected_song_*` はローカル期待値をM4曲へ突き合わせたレビュー列で、"
            "保存候補化やGP対象外曲の復帰には使いません。",
            "- `title_rerank_status` はjacketが曖昧な候補集合内だけの補助観測です。",
            "- `title_ocr_rerank_status` はjacketが曖昧な候補集合内だけでTYPE suffixを観測します。",
            "- `title_linehash_dict_status` はjacketが曖昧な候補集合内だけで"
            "曲名行のhexキー辞書を観測します。",
            "- `title_linehash_exact_status` / `title_linehash_distance_status` は"
            "旧来の比較観測です。",
            "- `identity_signal_*` はM5から後続保存判定へ渡す候補観測で、"
            "保存可能や曲ID/譜面ID確定を意味しません。",
            "- `matched` は保存可能を意味しません。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_jacket_match_diagnostic_report(
    path: Path,
    rows: Iterable[dict[str, str]],
    summary: dict[str, Any],
) -> None:
    row_list = list(rows)
    lines = [
        "# M5 Jacket Match Diagnostics",
        "",
        "通常の保存候補境界とは別に、duplicate と未確定resultも含めて"
        "M5の曲同定候補観測を確認する診断レポートです。",
        "`jacket_match_candidates.csv` には混ぜず、保存OK/NG判定として扱いません。",
        "",
        f"- target boundary: `{summary['target_boundary']}`",
        f"- diagnostic candidates: {summary['target_count']}",
        f"- event_type: `{json.dumps(summary['event_type_counts'], sort_keys=True)}`",
        "- m5_target_boundary_reason: "
        f"`{json.dumps(summary['m5_target_boundary_reason_counts'], sort_keys=True)}`",
        f"- confirmed_result: "
        f"`{json.dumps(summary['confirmed_result_counts'], sort_keys=True)}`",
        f"- duplicate: `{json.dumps(summary['duplicate_counts'], sort_keys=True)}`",
        "",
        "## Status Counts",
        "",
        f"- jacket_match_status: `{json.dumps(summary['status_counts'], sort_keys=True)}`",
        f"- identity_signal_status: "
        f"`{json.dumps(summary['identity_signal_status_counts'], sort_keys=True)}`",
        f"- identity_signal_source: "
        f"`{json.dumps(summary['identity_signal_source_counts'], sort_keys=True)}`",
        f"- expected_song_resolution_status: "
        f"`{json.dumps(summary['expected_song_resolution_status_counts'], sort_keys=True)}`",
        "- expected_song_grand_prix_play_available: `"
        + json.dumps(
            summary["expected_song_grand_prix_play_available_counts"],
            sort_keys=True,
        )
        + "`",
        f"- failure_reason: `{json.dumps(summary['failure_reason_counts'], sort_keys=True)}`",
    ]
    append_boundary_representatives(lines, row_list)
    append_identity_signal_representatives(lines, row_list)
    append_unresolved_identity_signal_representatives(lines, row_list)
    lines.extend(
        [
            "",
            "## Diagnostic Rows",
            "",
            "| organized_file | boundary | event | duplicate | status | top candidate | "
            "identity signal | reason |",
            "|---|---|---|---|---|---|---|---|",
        ]
    )
    for row in row_list[:30]:
        top_text = jacket_report_top_candidate(row)
        lines.append(
            f"| `{row['organized_file']}` | "
            f"`{row.get('m5_target_boundary_reason', '')}` | "
            f"`{row.get('event_type', '')}` | `{row.get('duplicate', '')}` | "
            f"`{row['jacket_match_status']}` | `{top_text}` | "
            f"`{row.get('identity_signal_status', '')}`"
            f" / `{row.get('identity_signal_source', '')}` | "
            f"`{row['failure_reason']}` |"
        )
    if len(row_list) > 30:
        lines.append("| ... | ... | ... | ... | ... | ... | ... | ... |")

    lines.extend(
        [
            "",
            "## Reading Notes",
            "",
            "- この診断出力はM5同定能力とイベント境界の観察材料です。",
            "- `confirmed_result=true` かつ `duplicate=false` 以外の行は保存候補ではありません。",
            "- 0点リザルトやduplicate行をここで観察しても、保存候補への昇格を意味しません。",
            "- 通常候補CSVと診断CSVは混ぜて読みません。",
        ]
    )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_master_match_outputs(
    output_dir: Path,
    rows: Iterable[dict[str, str]],
) -> dict[str, Any]:
    row_list = list(rows)
    summary = summarize_master_match_rows(row_list)
    write_master_match_csv(output_dir / "master_match_candidates.csv", row_list)
    write_master_match_summary(output_dir / "master_match_summary.json", summary)
    write_master_match_report(output_dir / "master_match_report.md", row_list, summary)
    return summary


def write_jacket_feature_master_outputs(
    output_dir: Path,
    rows: Iterable[dict[str, str]],
) -> dict[str, Any]:
    row_list = list(rows)
    summary = summarize_jacket_feature_master_rows(row_list)
    write_jacket_feature_master_csv(output_dir / "jacket_feature_master.csv", row_list)
    write_jacket_feature_master_summary(
        output_dir / "jacket_feature_master_summary.json",
        summary,
    )
    return summary


def write_jacket_match_outputs(
    output_dir: Path,
    rows: Iterable[dict[str, str]],
) -> dict[str, Any]:
    row_list = list(rows)
    summary = summarize_jacket_match_rows(row_list)
    write_jacket_match_csv(output_dir / "jacket_match_candidates.csv", row_list)
    write_jacket_match_summary(output_dir / "jacket_match_summary.json", summary)
    write_jacket_match_report(output_dir / "jacket_match_report.md", row_list, summary)
    return summary


def write_jacket_match_diagnostic_outputs(
    output_dir: Path,
    rows: Iterable[dict[str, str]],
) -> dict[str, Any]:
    row_list = list(rows)
    summary = summarize_jacket_match_diagnostic_rows(row_list)
    write_jacket_match_diagnostic_csv(
        output_dir / "jacket_match_diagnostics.csv",
        row_list,
    )
    write_jacket_match_summary(
        output_dir / "jacket_match_diagnostics_summary.json",
        summary,
    )
    write_jacket_match_diagnostic_report(
        output_dir / "jacket_match_diagnostics.md",
        row_list,
        summary,
    )
    return summary


def write_jacket_reference_coverage_outputs(
    output_dir: Path,
    rows: Iterable[dict[str, str]],
    *,
    file_stem: str = "jacket_reference_coverage",
) -> dict[str, Any]:
    row_list = list(rows)
    summary = summarize_jacket_reference_coverage_rows(row_list)
    missing_rows = jacket_reference_missing_representatives(row_list)
    write_jacket_reference_coverage_csv(output_dir / f"{file_stem}.csv", row_list)
    write_jacket_reference_coverage_summary(
        output_dir / f"{file_stem}_summary.json",
        summary,
    )
    write_jacket_reference_coverage_csv(
        output_dir / f"{file_stem}_missing_representatives.csv",
        missing_rows,
    )
    write_jacket_reference_coverage_report(
        output_dir / f"{file_stem}_report.md",
        row_list,
        summary,
    )
    return summary
