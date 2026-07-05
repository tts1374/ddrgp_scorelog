from __future__ import annotations

import json
import sqlite3
from pathlib import Path

from PIL import Image

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
        ("song_type1", "OSAKA TYPE1", "Unit O"),
        ("song_type2", "OSAKA TYPE2", "Unit O"),
        ("song_type3", "OSAKA TYPE3", "Unit O"),
    ]
    charts = [
        ("chart_make_single_difficult", "song_make", "SINGLE", "DIFFICULT", 9),
        ("chart_paranoia_single_expert", "song_paranoia", "SINGLE", "EXPERT", 16),
        ("chart_aa_bang_single_basic", "song_aa_bang", "SINGLE", "BASIC", 1),
        ("chart_aa_question_single_basic", "song_aa_question", "SINGLE", "BASIC", 1),
        ("chart_type1_single_challenge", "song_type1", "SINGLE", "CHALLENGE", 10),
        ("chart_type2_single_challenge", "song_type2", "SINGLE", "CHALLENGE", 10),
        ("chart_type3_single_challenge", "song_type3", "SINGLE", "CHALLENGE", 10),
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


def solid_feature(color: tuple[int, int, int]) -> master_match.JacketFeature:
    return master_match.extract_jacket_feature(Image.new("RGB", (64, 64), color))


def title_feature(color: int) -> master_match.TitleImageFeature:
    return master_match.extract_title_image_feature(
        Image.new("RGB", (160, 40), (color, color, color))
    )


def jacket_entry(
    *,
    song_id: str = "song_make",
    title: str = "MAKE IT BETTER",
    artist: str = "mitsu-O!",
    color: tuple[int, int, int] = (240, 20, 20),
) -> master_match.JacketFeatureMasterEntry:
    return master_match.JacketFeatureMasterEntry(
        organized_file="organized/song_select/song_select_fixture_grid.png",
        source_song_title=title,
        song_id=song_id,
        title=title,
        artist=artist,
        feature=solid_feature(color),
    )


def title_entry(
    *,
    song_id: str,
    title: str,
    artist: str,
    color: int,
    organized_file: str = "organized/result/result_title_reference.png",
) -> master_match.TitleFeatureMasterEntry:
    return master_match.TitleFeatureMasterEntry(
        organized_file=organized_file,
        source_song_title=title,
        song_id=song_id,
        title=title,
        artist=artist,
        feature=title_feature(color),
    )


def test_normalize_song_title_folds_width_case_space_and_punctuation() -> None:
    assert master_match.normalize_song_title(" ＭＡＫＥ　ＩＴ・ＢＥＴＴＥＲ!! ") == (
        "makeitbetter"
    )
    assert master_match.normalize_song_title("Poppin’ Soda “Remix”") == (
        "poppinsodaremix"
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


def test_title_similarity_only_boosts_master_title_inside_ocr_text() -> None:
    assert master_match.title_similarity("makeitbettermitsuo", "makeitbetter") == 1.0
    assert master_match.title_similarity("makeit", "makeitbetter") < 1.0


def test_extract_type_suffix_finds_osaka_type_suffix() -> None:
    assert master_match.extract_type_suffix("osaka EVOLVED (TYPE2)") == "TYPE2"
    assert master_match.extract_type_suffix("OSAKA EVOLVED  TYPE 3") == "TYPE3"
    assert master_match.extract_type_suffix("osaka evolved") == ""


def test_resolve_song_by_title_uses_normalized_exact_title(tmp_path: Path) -> None:
    db_path = write_fixture_master_db(tmp_path)

    song, failure_reason = master_match.resolve_song_by_title(db_path, " make it better!! ")

    assert failure_reason == ""
    assert song is not None
    assert song.song_id == "song_make"


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
    assert "MAKE IT BETTER / mitsu-O!" in result["top_candidates"]


def test_match_save_candidate_row_tolerates_artist_suffix_in_ocr(
    tmp_path: Path,
) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(title="MAKE IT BETTER mitsu-O!")

    result = master_match.match_save_candidate_row(row, db_path)

    assert result["match_status"] == "matched"
    assert result["top_song_id"] == "song_make"
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
            "top_candidates": "1.0000:MAKE IT BETTER / mitsu-O! [chart_make_single_difficult]",
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


def test_jacket_feature_distance_prefers_same_image() -> None:
    red = solid_feature((240, 20, 20))
    red_again = solid_feature((240, 20, 20))
    blue = solid_feature((20, 20, 240))

    assert master_match.jacket_feature_distance(red, red_again) == 0.0
    assert master_match.jacket_feature_distance(red, blue) > 0.1


def test_match_jacket_save_candidate_row_reports_unique_match(tmp_path: Path) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(title="")

    result = master_match.match_jacket_save_candidate_row(
        row,
        db_path,
        solid_feature((240, 20, 20)),
        [jacket_entry()],
    )

    assert result["jacket_match_status"] == "matched"
    assert result["top_song_id"] == "song_make"
    assert result["top_chart_id"] == "chart_make_single_difficult"
    assert result["candidate_song_count"] == "1"
    assert result["candidate_feature_count"] == "1"
    assert result["top_score"] == "1.0000"
    assert result["jacket_top_margin"] == ""
    assert result["title_rerank_status"] == "not_run"


def test_match_jacket_save_candidate_row_reports_expected_jacket_diagnostics(
    tmp_path: Path,
) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(title="")
    row["song_title_expected_value"] = "MAKE IT BETTER"

    result = master_match.match_jacket_save_candidate_row(
        row,
        db_path,
        solid_feature((240, 20, 20)),
        [jacket_entry()],
    )

    assert result["expected_song_title"] == "MAKE IT BETTER"
    assert result["expected_song_id"] == "song_make"
    assert result["expected_jacket_distance"] == "0.0000"
    assert result["expected_jacket_rank"] == "1"


def test_match_jacket_save_candidate_row_title_reranks_only_ambiguous_candidates(
    tmp_path: Path,
) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(
        title="",
        play_style="SINGLE",
        difficulty="CHALLENGE",
        level="10",
    )
    row["organized_file"] = "organized/result/result_type2.png"
    row["song_title_expected_value"] = "OSAKA TYPE2"

    result = master_match.match_jacket_save_candidate_row(
        row,
        db_path,
        solid_feature((120, 120, 120)),
        [
            jacket_entry(
                song_id="song_type1",
                title="OSAKA TYPE1",
                artist="Unit O",
                color=(120, 120, 120),
            ),
            jacket_entry(
                song_id="song_type2",
                title="OSAKA TYPE2",
                artist="Unit O",
                color=(120, 120, 120),
            ),
        ],
        title_feature(220),
        [
            title_entry(
                song_id="song_type1",
                title="OSAKA TYPE1",
                artist="Unit O",
                color=40,
            ),
            title_entry(
                song_id="song_type2",
                title="OSAKA TYPE2",
                artist="Unit O",
                color=220,
            ),
        ],
    )

    assert result["jacket_match_status"] == "ambiguous"
    assert result["failure_reason"] == "near_top_distance"
    assert result["jacket_top_margin"] == "0.0000"
    assert result["expected_song_id"] == "song_type2"
    assert result["expected_jacket_rank"] in {"1", "2"}
    assert result["title_rerank_status"] == "resolved_candidate"
    assert result["title_top_song_id"] == "song_type2"
    assert result["title_candidate_feature_count"] == "2"
    assert result["title_linehash_dict_status"] == "resolved_candidate"
    assert result["title_linehash_dict_top_song_id"] == "song_type2"
    assert result["title_linehash_dict_top_row_matches"] == "28"
    assert result["title_linehash_exact_status"] == "resolved_candidate"
    assert result["title_linehash_distance_status"] == "resolved_candidate"
    assert result["title_linehash_top_song_id"] == "song_type2"
    assert result["title_linehash_candidate_feature_count"] == "2"
    assert result["title_ocr_rerank_status"] == "missing_ocr"
    assert result["title_ocr_rerank_reason"] == "title_ocr_not_run"


def test_match_jacket_save_candidate_row_title_ocr_suffix_reranks_only_ambiguous_candidates(
    tmp_path: Path,
) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(
        title="",
        play_style="SINGLE",
        difficulty="CHALLENGE",
        level="10",
    )
    row["song_title_expected_value"] = "OSAKA TYPE2"

    result = master_match.match_jacket_save_candidate_row(
        row,
        db_path,
        solid_feature((120, 120, 120)),
        [
            jacket_entry(
                song_id="song_type1",
                title="OSAKA TYPE1",
                artist="Unit O",
                color=(120, 120, 120),
            ),
            jacket_entry(
                song_id="song_type2",
                title="OSAKA TYPE2",
                artist="Unit O",
                color=(120, 120, 120),
            ),
            jacket_entry(
                song_id="song_type3",
                title="OSAKA TYPE3",
                artist="Unit O",
                color=(120, 120, 120),
            ),
        ],
        title_ocr_observation=master_match.TitleOcrObservation(
            raw="osaka EVOLVED (TYPE2)",
            text="osaka EVOLVED (TYPE2)",
            status="ok",
            failure_reason="",
        ),
    )

    assert result["jacket_match_status"] == "ambiguous"
    assert result["title_rerank_status"] == "missing_feature"
    assert result["title_ocr_suffix"] == "TYPE2"
    assert result["title_ocr_rerank_status"] == "resolved_candidate"
    assert result["title_ocr_top_song_id"] == "song_type2"
    assert result["title_ocr_top_title"] == "OSAKA TYPE2"
    assert result["title_linehash_dict_status"] == "missing_feature"
    assert result["title_linehash_exact_status"] == "missing_feature"
    assert result["title_linehash_rerank_reason"] == "result_title_linehash_unavailable"


def test_match_jacket_save_candidate_row_title_ocr_suffix_does_not_expand_candidates(
    tmp_path: Path,
) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(
        title="",
        play_style="SINGLE",
        difficulty="CHALLENGE",
        level="10",
    )

    result = master_match.match_jacket_save_candidate_row(
        row,
        db_path,
        solid_feature((120, 120, 120)),
        [
            jacket_entry(
                song_id="song_type1",
                title="OSAKA TYPE1",
                artist="Unit O",
                color=(120, 120, 120),
            ),
            jacket_entry(
                song_id="song_type2",
                title="OSAKA TYPE2",
                artist="Unit O",
                color=(120, 120, 120),
            ),
        ],
        title_ocr_observation=master_match.TitleOcrObservation(
            raw="OSAKA TYPE3",
            text="OSAKA TYPE3",
            status="ok",
            failure_reason="",
        ),
    )

    assert result["jacket_match_status"] == "ambiguous"
    assert result["title_ocr_suffix"] == "TYPE3"
    assert result["title_ocr_rerank_status"] == "no_candidate_suffix_match"
    assert result["title_ocr_top_song_id"] == ""


def test_match_jacket_save_candidate_row_reports_missing_feature(tmp_path: Path) -> None:
    db_path = write_fixture_master_db(tmp_path)
    row = save_candidate_row(title="")

    result = master_match.match_jacket_save_candidate_row(
        row,
        db_path,
        solid_feature((240, 20, 20)),
        [jacket_entry(song_id="song_paranoia", title="PARANOiA", artist="180")],
    )

    assert result["jacket_match_status"] == "missing_feature"
    assert result["failure_reason"] == "no_candidate_jacket_features"


def test_write_jacket_match_outputs_records_observation_scope(tmp_path: Path) -> None:
    rows = [
        {
            "frame_index": "2",
            "organized_file": "organized/result/result_fixture.png",
            "expected_song_title": "MAKE IT BETTER",
            "expected_song_id": "song_make",
            "input_play_style": "SINGLE",
            "input_difficulty": "DIFFICULT",
            "input_level": "9",
            "candidate_song_count": "1",
            "candidate_chart_count": "1",
            "candidate_feature_count": "1",
            "top_song_id": "song_make",
            "top_chart_id": "chart_make_single_difficult",
            "top_title": "MAKE IT BETTER",
            "top_artist": "mitsu-O!",
            "top_score": "1.0000",
            "top_distance": "0.0000",
            "top_feature_source": "organized/song_select/song_select_fixture_grid.png",
            "top_candidates": (
                "1.0000:MAKE IT BETTER / mitsu-O! "
                "[chart_make_single_difficult; organized/song_select/song_select_fixture_grid.png]"
            ),
            "expected_jacket_distance": "0.0000",
            "expected_jacket_rank": "1",
            "jacket_top_margin": "",
            "title_candidate_feature_count": "0",
            "title_top_song_id": "",
            "title_top_chart_id": "",
            "title_top_title": "",
            "title_top_score": "",
            "title_top_distance": "",
            "title_top_feature_source": "",
            "title_top_candidates": "",
            "title_rerank_status": "not_run",
            "title_rerank_reason": "",
            "title_ocr_raw": "",
            "title_ocr_text": "",
            "title_ocr_suffix": "",
            "title_ocr_top_song_id": "",
            "title_ocr_top_chart_id": "",
            "title_ocr_top_title": "",
            "title_ocr_top_candidates": "",
            "title_ocr_rerank_status": "not_run",
            "title_ocr_rerank_reason": "",
            "title_linehash_candidate_feature_count": "0",
            "title_linehash_diff_bit_count": "0",
            "title_linehash_dict_status": "not_run",
            "title_linehash_dict_top_song_id": "",
            "title_linehash_dict_top_chart_id": "",
            "title_linehash_dict_top_title": "",
            "title_linehash_dict_top_row_matches": "",
            "title_linehash_dict_top_candidates": "",
            "title_linehash_exact_status": "not_run",
            "title_linehash_distance_status": "not_run",
            "title_linehash_top_song_id": "",
            "title_linehash_top_chart_id": "",
            "title_linehash_top_title": "",
            "title_linehash_top_distance": "",
            "title_linehash_top_candidates": "",
            "title_linehash_rerank_reason": "",
            "jacket_match_status": "matched",
            "failure_reason": "",
        }
    ]

    summary = master_match.write_jacket_match_outputs(tmp_path, rows)

    assert summary["scope"] == "M5 jacket match PoC"
    assert summary["status_counts"]["matched"] == 1
    assert "missing_feature" in summary["status_vocabulary"]
    assert (tmp_path / "jacket_match_candidates.csv").exists()
    assert json.loads((tmp_path / "jacket_match_summary.json").read_text(encoding="utf-8")) == (
        summary
    )
    assert summary["title_rerank_status_counts"]["not_run"] == 1
    assert summary["title_ocr_rerank_status_counts"]["not_run"] == 1
    assert summary["title_linehash_dict_status_counts"]["not_run"] == 1
    assert summary["title_linehash_exact_status_counts"]["not_run"] == 1
    assert summary["title_linehash_distance_status_counts"]["not_run"] == 1
    report = (tmp_path / "jacket_match_report.md").read_text(encoding="utf-8")
    assert "DB保存可能や本番採用済み照合ではありません" in report
