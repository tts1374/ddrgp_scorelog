from __future__ import annotations

import sqlite3
from dataclasses import replace

import pytest

from tools.vision_poc import personal_score_db_save as score_save


def saved_input(suffix: str = "one") -> score_save.PersonalScoreDbSaveInput:
    capture_id = f"capture-{suffix}"
    capture_hash = f"sha256:{suffix}"
    play_id = f"play-{suffix}"
    return score_save.PersonalScoreDbSaveInput(
        source_capture=score_save.PersonalScoreDbSourceCaptureInput(
            capture_id=capture_id,
            capture_hash=capture_hash,
            captured_at="2026-07-11T12:34:56+09:00",
            source_kind="manifest",
            source_path=f"samples/{suffix}.png",
            manifest_image_path=f"frames/{suffix}.png",
            frame_index=12,
        ),
        play=score_save.PersonalScoreDbPlayInput(
            play_id=play_id,
            played_at="2026-07-11T12:34:56+09:00",
            master_version="2026-07-11",
            song_id="song-001",
            chart_id="chart-001-sp-basic",
            score=987_650,
            max_combo=456,
            marvelous=400,
            perfect=40,
            great=10,
            good=4,
            miss=2,
            ex_score=1_750,
            rank="AAA",
            clear_type="CLEAR",
            capture_hash=capture_hash,
            source_capture_id=capture_id,
            duplicate_key=f"play:v1:{suffix}",
            analysis_confidence=0.98,
            app_version="0.1.0",
        ),
        analysis=score_save.PersonalScoreDbAnalysisInput(
            analysis_id=f"analysis-{suffix}",
            play_id=play_id,
            source_capture_id=capture_id,
            analysis_status="saved",
            save_boundary_status="save_ready",
            skip_reason="",
            event_type="confirmed",
            confirmed_result=True,
            duplicate=False,
            confirmation_mode="time",
            timestamp_ms=1_234,
            candidate_duration_ms=1_000,
            identity_signal_status="composite_resolved_candidate",
            digit_review_status="reviewable",
            analysis_confidence=0.98,
            analysis_summary_json='{"contract": "formal-save-input-v1"}',
            log_path="",
            app_version="0.1.0",
        ),
    )


def excluded_input(
    *,
    suffix: str,
    analysis_status: str,
    save_boundary_status: str,
    skip_reason: str,
    event_type: str,
    duplicate: bool,
) -> score_save.PersonalScoreDbSaveInput:
    source = saved_input(suffix).source_capture
    return score_save.PersonalScoreDbSaveInput(
        source_capture=source,
        play=None,
        analysis=score_save.PersonalScoreDbAnalysisInput(
            analysis_id=f"analysis-{suffix}",
            play_id=None,
            source_capture_id=source.capture_id,
            analysis_status=analysis_status,
            save_boundary_status=save_boundary_status,
            skip_reason=skip_reason,
            event_type=event_type,
            confirmed_result=True,
            duplicate=duplicate,
            confirmation_mode="time",
            timestamp_ms=1_234,
            candidate_duration_ms=1_000,
            identity_signal_status="unresolved_missing_feature",
            digit_review_status="reviewable",
            analysis_confidence=0.42,
            analysis_summary_json='{"contract": "formal-save-input-v1"}',
            log_path="",
            app_version="0.1.0",
        ),
    )


def row_count(connection: sqlite3.Connection, table_name: str) -> int:
    return int(connection.execute(f"SELECT COUNT(*) FROM {table_name}").fetchone()[0])


def test_personal_score_db_save_input_accepts_resolved_formal_values() -> None:
    save_input = saved_input()

    assert score_save.personal_score_db_save_input_errors(save_input) == ()
    score_save.validate_personal_score_db_save_input(save_input)


def test_personal_score_db_save_input_rejects_unresolved_formal_fields() -> None:
    save_input = saved_input()
    assert save_input.play is not None
    invalid_source = replace(save_input.source_capture, source_kind="unknown")
    invalid_play = replace(
        save_input.play,
        played_at="2026-07-11T12:34:56",
        master_version="",
        rank="",
        clear_type="",
        duplicate_key="score:987650",
    )
    invalid_input = replace(
        save_input,
        source_capture=invalid_source,
        play=invalid_play,
    )

    errors = score_save.personal_score_db_save_input_errors(invalid_input)

    assert "source_capture.source_kind_not_writable" in errors
    assert "play.played_at_timezone_required" in errors
    assert "play.master_version_required" in errors
    assert "play.rank_required" in errors
    assert "play.clear_type_required" in errors
    assert "play.duplicate_key_uses_preview_format" in errors


def test_personal_score_db_save_input_rejects_cross_record_mismatches() -> None:
    save_input = saved_input()
    assert save_input.play is not None
    mismatched_input = replace(
        save_input,
        play=replace(
            save_input.play,
            play_id="play-other",
            capture_hash="sha256:other",
            source_capture_id="capture-other",
            analysis_confidence=0.8,
            app_version="0.2.0",
        ),
        analysis=replace(
            save_input.analysis,
            source_capture_id="capture-analysis-other",
            analysis_confidence=0.7,
            app_version="0.3.0",
        ),
    )

    errors = score_save.personal_score_db_save_input_errors(mismatched_input)

    assert "analysis.source_capture_id_mismatch" in errors
    assert "analysis.play_id_mismatch" in errors
    assert "play.source_capture_id_mismatch" in errors
    assert "play.capture_hash_mismatch" in errors
    assert "analysis.app_version_mismatch" in errors
    assert "analysis.analysis_confidence_mismatch" in errors


def test_personal_score_db_writer_saves_formal_rows_in_one_transaction() -> None:
    save_input = saved_input()
    with sqlite3.connect(":memory:") as connection:
        result = score_save.write_personal_score_db_save(connection, save_input)

        source_row = connection.execute(
            """
            SELECT capture_id, capture_hash, source_kind, source_path
            FROM source_captures
            """
        ).fetchone()
        play_row = connection.execute(
            """
            SELECT play_id, song_id, chart_id, score, rank, clear_type,
                   source_capture_id, duplicate_key
            FROM plays
            """
        ).fetchone()
        analysis_row = connection.execute(
            """
            SELECT analysis_id, play_id, analysis_status, save_boundary_status,
                   duplicate, source_capture_id
            FROM analysis_logs
            """
        ).fetchone()

    assert result.saved
    assert result.play_id == "play-one"
    assert source_row == (
        "capture-one",
        "sha256:one",
        "manifest",
        "samples/one.png",
    )
    assert play_row == (
        "play-one",
        "song-001",
        "chart-001-sp-basic",
        987_650,
        "AAA",
        "CLEAR",
        "capture-one",
        "play:v1:one",
    )
    assert analysis_row == (
        "analysis-one",
        "play-one",
        "saved",
        "save_ready",
        0,
        "capture-one",
    )


@pytest.mark.parametrize(
    "save_input",
    [
        excluded_input(
            suffix="duplicate",
            analysis_status="skipped",
            save_boundary_status="duplicate",
            skip_reason="duplicate_key_already_seen",
            event_type="duplicate",
            duplicate=True,
        ),
        excluded_input(
            suffix="low-confidence",
            analysis_status="low_confidence",
            save_boundary_status="blocked_low_confidence",
            skip_reason="identity_signal_unresolved",
            event_type="confirmed",
            duplicate=False,
        ),
    ],
)
def test_personal_score_db_writer_logs_excluded_analysis_without_play(
    save_input: score_save.PersonalScoreDbSaveInput,
) -> None:
    with sqlite3.connect(":memory:") as connection:
        result = score_save.write_personal_score_db_save(connection, save_input)

        assert row_count(connection, "source_captures") == 1
        assert row_count(connection, "plays") == 0
        assert row_count(connection, "analysis_logs") == 1
        analysis_row = connection.execute(
            """
            SELECT play_id, analysis_status, save_boundary_status, skip_reason
            FROM analysis_logs
            """
        ).fetchone()

    assert not result.saved
    assert result.play_id is None
    assert analysis_row == (
        None,
        save_input.analysis.analysis_status,
        save_input.analysis.save_boundary_status,
        save_input.analysis.skip_reason,
    )


def test_personal_score_db_writer_rejects_invalid_input_before_schema_creation() -> None:
    save_input = saved_input()
    assert save_input.play is not None
    invalid_input = replace(
        save_input,
        play=replace(save_input.play, rank=""),
    )
    with sqlite3.connect(":memory:") as connection:
        with pytest.raises(ValueError, match="play.rank_required"):
            score_save.write_personal_score_db_save(connection, invalid_input)

        table_names = connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table'"
        ).fetchall()

    assert table_names == []


def test_personal_score_db_writer_rolls_back_source_when_play_insert_fails() -> None:
    first_input = saved_input("first")
    second_input = saved_input("second")
    assert first_input.play is not None
    assert second_input.play is not None
    conflicting_input = replace(
        second_input,
        play=replace(
            second_input.play,
            duplicate_key=first_input.play.duplicate_key,
        ),
    )

    with sqlite3.connect(":memory:") as connection:
        score_save.write_personal_score_db_save(connection, first_input)

        with pytest.raises(sqlite3.IntegrityError, match="UNIQUE constraint failed"):
            score_save.write_personal_score_db_save(connection, conflicting_input)

        assert row_count(connection, "source_captures") == 1
        assert row_count(connection, "plays") == 1
        assert row_count(connection, "analysis_logs") == 1
        assert connection.execute(
            "SELECT capture_id FROM source_captures ORDER BY capture_id"
        ).fetchall() == [("capture-first",)]
        assert connection.execute(
            "SELECT analysis_id FROM analysis_logs ORDER BY analysis_id"
        ).fetchall() == [("analysis-first",)]
