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
from PIL import Image

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
PUNCTUATION_TO_DROP = frozenset(
    string.punctuation + "　、。，．・･：；！？（）［］【】『』「」‘’“”"
)
MIN_CONTAINMENT_MATCH_LENGTH = 5
TOP_CANDIDATE_REPORT_LIMIT = 5
JACKET_THUMBNAIL_SIZE = (16, 16)
JACKET_DHASH_SIZE = 8


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


@dataclass(frozen=True)
class JacketFeature:
    thumbnail: np.ndarray
    histogram: np.ndarray
    dhash_bits: np.ndarray
    dhash_hex: str


@dataclass(frozen=True)
class JacketFeatureMasterEntry:
    organized_file: str
    source_song_title: str
    song_id: str
    title: str
    artist: str
    feature: JacketFeature


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
            SELECT song_id, title, artist
            FROM songs
            ORDER BY title, artist, song_id
            """
        ).fetchall()
    return [
        MasterSong(song_id=str(row[0]), title=str(row[1]), artist=str(row[2]))
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
        return None, "title_not_found"
    if len(song_ids) > 1:
        return None, "ambiguous_title"
    return matches[0], ""


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


def jacket_feature_distance(left: JacketFeature, right: JacketFeature) -> float:
    thumbnail_distance = float(np.mean(np.abs(left.thumbnail - right.thumbnail)))
    histogram_distance = float(np.mean(np.abs(left.histogram - right.histogram)))
    dhash_distance = float(np.mean(left.dhash_bits != right.dhash_bits))
    return (0.70 * thumbnail_distance) + (0.20 * histogram_distance) + (
        0.10 * dhash_distance
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
    *,
    distance_threshold: float = DEFAULT_JACKET_DISTANCE_THRESHOLD,
    ambiguity_delta: float = DEFAULT_JACKET_AMBIGUITY_DELTA,
) -> dict[str, str]:
    base_result = {
        "frame_index": row.get("frame_index", ""),
        "organized_file": row.get("organized_file", ""),
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
        return {**base_result, "failure_reason": reason}
    if result_feature is None:
        return {
            **base_result,
            "jacket_match_status": "missing_feature",
            "failure_reason": "result_jacket_feature_unavailable",
        }

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
            "jacket_match_status": "not_found",
            "failure_reason": "no_chart_candidates",
        }

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
        return {
            **base_result,
            "jacket_match_status": "missing_feature",
            "failure_reason": "no_candidate_jacket_features",
        }

    scored_candidates.sort(
        key=lambda item: (item[0], item[1].title, item[1].artist, item[1].chart_id)
    )
    top_distance, top_candidate, top_feature_entry = scored_candidates[0]
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
    }
    if top_distance > distance_threshold:
        return {
            **result,
            "jacket_match_status": "not_found",
            "failure_reason": "above_distance_threshold",
        }

    near_top = [
        item
        for item in scored_candidates
        if item[0] - top_distance <= ambiguity_delta
        and item[1].song_id != top_candidate.song_id
    ]
    if near_top:
        return {
            **result,
            "jacket_match_status": "ambiguous",
            "failure_reason": "near_top_distance",
        }

    return {**result, "jacket_match_status": "matched", "failure_reason": ""}


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
    *,
    distance_threshold: float = DEFAULT_JACKET_DISTANCE_THRESHOLD,
    ambiguity_delta: float = DEFAULT_JACKET_AMBIGUITY_DELTA,
) -> list[dict[str, str]]:
    feature_master_entry_list = list(feature_master_entries)
    return [
        match_jacket_save_candidate_row(
            row,
            db_path,
            result_features_by_file.get(row.get("organized_file", "")),
            feature_master_entry_list,
            distance_threshold=distance_threshold,
            ambiguity_delta=ambiguity_delta,
        )
        for row in rows
    ]


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
            "Resolved song_id uses metadata labels matched to M4 songs.title.",
            "accepted feature rows are local PoC references, not a distributed jacket database.",
        ],
    }


def summarize_jacket_match_rows(rows: Iterable[dict[str, str]]) -> dict[str, Any]:
    row_list = list(rows)
    status_counts: dict[str, int] = {
        status: 0 for status in JACKET_MATCH_STATUS_VOCABULARY
    }
    failure_reason_counts: dict[str, int] = {}
    for row in row_list:
        status = row["jacket_match_status"]
        status_counts[status] = status_counts.get(status, 0) + 1
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
        "status_vocabulary": list(JACKET_MATCH_STATUS_VOCABULARY),
        "distance_threshold": DEFAULT_JACKET_DISTANCE_THRESHOLD,
        "ambiguity_delta": DEFAULT_JACKET_AMBIGUITY_DELTA,
        "reading_notes": [
            "matched means a unique PoC top jacket feature candidate, not DB-save readiness.",
            "missing_feature means the local feature master lacks a usable reference.",
            "chart fields still only narrow candidates; jacket matching is an observation signal.",
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
    fieldnames = [
        "frame_index",
        "organized_file",
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
        "jacket_match_status",
        "failure_reason",
    ]
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


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
        f"- failure_reason: `{json.dumps(summary['failure_reason_counts'], sort_keys=True)}`",
        "",
        "## Candidate Rows",
        "",
        "| organized_file | status | chart | top candidate | score | distance | "
        "feature source | reason |",
        "|---|---|---|---|---|---|---|---|",
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
            f"| `{row['organized_file']}` | `{row['jacket_match_status']}` | "
            f"`{chart_text}` | `{top_text}` | `{row['top_score']}` | "
            f"`{row['top_distance']}` | `{row['top_feature_source']}` | "
            f"`{row['failure_reason']}` |"
        )
    if len(row_list) > 20:
        lines.append("| ... | ... | ... | ... | ... | ... | ... | ... |")

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
            "- `matched` は保存可能を意味しません。",
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
