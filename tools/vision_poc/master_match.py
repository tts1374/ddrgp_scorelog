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

MATCH_STATUS_VOCABULARY = (
    "matched",
    "ambiguous",
    "not_found",
    "insufficient_input",
)
DEFAULT_SCORE_THRESHOLD = 0.92
PUNCTUATION_TO_DROP = frozenset(
    string.punctuation + "　、。，．・･：；！？（）［］【】『』「」‘’“”"
)
MIN_CONTAINMENT_MATCH_LENGTH = 5
TOP_CANDIDATE_REPORT_LIMIT = 5


@dataclass(frozen=True)
class MasterChartCandidate:
    song_id: str
    chart_id: str
    title: str
    artist: str
    play_style: str
    difficulty: str
    level: int


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
