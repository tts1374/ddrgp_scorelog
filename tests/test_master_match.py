from __future__ import annotations

import json
import sqlite3
from pathlib import Path

from master import builder
from tools.vision_poc import master_match


def write_fixture_master_db(tmp_path: Path) -> Path:
    db_path = tmp_path / "ddrgp-master.sqlite"
    generated_at = "2026-07-05T00:00:00+00:00"
    songs = [
        ("song_make", "MAKE IT BETTER", "mitsu-O!"),
        ("song_paranoia", "PARANOiA", "180"),
        ("song_aa_bang", "AA!!", "Unit A"),
        ("song_aa_question", "AA??", "Unit B"),
    ]
    charts = [
        ("chart_make_single_difficult", "song_make", "SINGLE", "DIFFICULT", 9),
        ("chart_paranoia_single_expert", "song_paranoia", "SINGLE", "EXPERT", 16),
        ("chart_aa_bang_single_basic", "song_aa_bang", "SINGLE", "BASIC", 1),
        ("chart_aa_question_single_basic", "song_aa_question", "SINGLE", "BASIC", 1),
    ]
    with sqlite3.connect(db_path) as connection:
        builder.create_schema(connection)
        connection.executemany(
            """
            INSERT INTO songs (
              song_id, title, artist, version, source_version, bpm, category,
              movie_stage, availability, notes, created_at, updated_at
            )
            VALUES (?, ?, ?, 'fixture', 'fixture', '', 'fixture', '', '', '', ?, ?)
            """,
            [
                (song_id, title, artist, generated_at, generated_at)
                for song_id, title, artist in songs
            ],
        )
        connection.executemany(
            """
            INSERT INTO charts (
              chart_id, song_id, play_style, difficulty, level, raw_level,
              shock_arrow, is_removed, is_limited, notes
            )
            VALUES (?, ?, ?, ?, ?, ?, 0, 0, 0, '')
            """,
            [
                (chart_id, song_id, play_style, difficulty, level, str(level))
                for chart_id, song_id, play_style, difficulty, level in charts
            ],
        )
    return db_path


def save_candidate_row(
    *,
    title: str,
    play_style: str = "SINGLE",
    difficulty: str = "DIFFICULT",
    level: str = "9",
    title_status: str = "ready",
    chart_status: str = "ready",
) -> dict[str, str]:
    return {
        "frame_index": "2",
        "organized_file": "organized/result/result_fixture.png",
        "song_title_status": title_status,
        "song_title_failure_reason": "" if title_status == "ready" else "empty_ocr",
        "song_title_extracted_value": title,
        "play_style_status": chart_status,
        "play_style_extracted_value": play_style,
        "difficulty_status": chart_status,
        "difficulty_extracted_value": difficulty,
        "level_status": chart_status,
        "level_extracted_value": level,
    }


def test_normalize_song_title_folds_width_case_space_and_punctuation() -> None:
    assert master_match.normalize_song_title(" ＭＡＫＥ　ＩＴ・ＢＥＴＴＥＲ!! ") == (
        "makeitbetter"
    )
    assert master_match.normalize_song_title("PARANOiA") == "paranoia"


def test_chart_filter_normalizes_m3_chart_fields() -> None:
    row = save_candidate_row(
        title="MAKE IT BETTER",
        play_style="sp",
        difficulty="dif",
        level="Lv09",
    )

    assert master_match.chart_filter_from_save_candidate(row) == (
        "SINGLE",
        "DIFFICULT",
        9,
    )


def test_match_save_candidate_row_reports_unique_matched_candidate(tmp_path: Path) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(title="MAKE IT BETTER")

    result = master_match.match_save_candidate_row(row, db_path)

    assert result["match_status"] == "matched"
    assert result["top_song_id"] == "song_make"
    assert result["top_chart_id"] == "chart_make_single_difficult"
    assert result["candidate_song_count"] == "1"
    assert result["candidate_chart_count"] == "1"
    assert result["top_score"] == "1.0000"


def test_match_save_candidate_row_reports_insufficient_input(tmp_path: Path) -> None:
    db_path = write_fixture_master_db(tmp_path)
    missing_title = save_candidate_row(title="", title_status="empty_ocr")
    missing_chart = save_candidate_row(title="MAKE IT BETTER", chart_status="missing_reference")

    title_result = master_match.match_save_candidate_row(missing_title, db_path)
    chart_result = master_match.match_save_candidate_row(missing_chart, db_path)

    assert title_result["match_status"] == "insufficient_input"
    assert title_result["failure_reason"] == "empty_ocr"
    assert chart_result["match_status"] == "insufficient_input"
    assert chart_result["failure_reason"] == (
        "chart_fields_not_ready:play_style,difficulty,level"
    )


def test_match_save_candidate_row_reports_not_found_reasons(tmp_path: Path) -> None:
    db_path = write_fixture_master_db(tmp_path)
    low_score = save_candidate_row(title="UNKNOWN TITLE")
    no_chart = save_candidate_row(
        title="MAKE IT BETTER",
        difficulty="CHALLENGE",
        level="19",
    )

    low_score_result = master_match.match_save_candidate_row(low_score, db_path)
    no_chart_result = master_match.match_save_candidate_row(no_chart, db_path)

    assert low_score_result["match_status"] == "not_found"
    assert low_score_result["failure_reason"] == "below_score_threshold"
    assert low_score_result["candidate_chart_count"] == "1"
    assert no_chart_result["match_status"] == "not_found"
    assert no_chart_result["failure_reason"] == "no_chart_candidates"
    assert no_chart_result["candidate_chart_count"] == "0"


def test_match_save_candidate_row_reports_ambiguous_tied_top_score(tmp_path: Path) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(
        title="AA",
        play_style="SINGLE",
        difficulty="BASIC",
        level="1",
    )

    result = master_match.match_save_candidate_row(row, db_path)

    assert result["match_status"] == "ambiguous"
    assert result["failure_reason"] == "tied_top_score"
    assert result["candidate_song_count"] == "2"
    assert result["candidate_chart_count"] == "2"
    assert result["top_score"] == "1.0000"


def test_write_master_match_outputs_records_observation_scope(tmp_path: Path) -> None:
    rows = [
        {
            "frame_index": "2",
            "organized_file": "organized/result/result_fixture.png",
            "input_song_title": "MAKE IT BETTER",
            "normalized_song_title": "makeitbetter",
            "input_play_style": "SINGLE",
            "input_difficulty": "DIFFICULT",
            "input_level": "9",
            "candidate_song_count": "1",
            "candidate_chart_count": "1",
            "top_song_id": "song_make",
            "top_chart_id": "chart_make_single_difficult",
            "top_title": "MAKE IT BETTER",
            "top_artist": "mitsu-O!",
            "top_score": "1.0000",
            "match_status": "matched",
            "failure_reason": "",
        }
    ]

    summary = master_match.write_master_match_outputs(tmp_path, rows)

    assert summary["scope"] == "M5 master match PoC"
    assert summary["status_counts"]["matched"] == 1
    assert (tmp_path / "master_match_candidates.csv").exists()
    assert json.loads((tmp_path / "master_match_summary.json").read_text(encoding="utf-8")) == (
        summary
    )
    report = (tmp_path / "master_match_report.md").read_text(encoding="utf-8")
    assert "DB保存可能や本番採用済み照合ではありません" in report
