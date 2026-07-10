from __future__ import annotations

import sqlite3

from tools.vision_poc import personal_score_db_schema as score_schema
from tools.vision_poc import runner


def table_names(connection: sqlite3.Connection) -> set[str]:
    return {
        str(row[0])
        for row in connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table'"
        )
    }


def table_column_names(connection: sqlite3.Connection, table_name: str) -> list[str]:
    return [
        str(row[1])
        for row in connection.execute(f"PRAGMA table_info({table_name})").fetchall()
    ]


def test_personal_score_db_schema_creates_formal_tables_and_metadata() -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)

        assert int(connection.execute("PRAGMA user_version").fetchone()[0]) == 1
        assert table_names(connection) >= set(score_schema.PERSONAL_SCORE_DB_REQUIRED_TABLES)
        assert score_schema.read_score_db_metadata(connection) == (
            score_schema.PERSONAL_SCORE_DB_METADATA
        )
        assert (
            table_column_names(connection, "plays")
            == list(score_schema.PERSONAL_SCORE_DB_PLAYS_COLUMNS)
        )
        assert (
            table_column_names(connection, "analysis_logs")
            == list(score_schema.PERSONAL_SCORE_DB_ANALYSIS_LOG_COLUMNS)
        )
        assert score_schema.personal_score_db_compatibility_errors(connection) == []


def test_personal_score_db_schema_rejects_m8_preview_database() -> None:
    with sqlite3.connect(":memory:") as connection:
        runner.create_m8_score_db_schema(connection)

        errors = score_schema.personal_score_db_compatibility_errors(connection)

    assert "m8_preview_database_not_supported" in errors
    assert "score_db_metadata_missing" in errors
    assert "missing_table:schema_migrations" in errors
    assert "missing_table:source_captures" in errors
    assert "missing_table:analysis_logs" in errors


def test_personal_score_db_plays_keeps_preview_and_raw_candidate_columns_out() -> None:
    forbidden_play_columns = {
        "source_organized_file",
        "source_confirmation_mode",
        "analysis_payload_status",
        "identity_signal_source",
        "m5_identity_signal_status",
        "m5_jacket_match_status",
        "recognized_digits",
        "expected_value",
        "match",
        "ocr_raw",
        "ocr_normalized",
        "title_ocr_raw",
        "title_ocr_normalized",
    }

    assert forbidden_play_columns.isdisjoint(
        score_schema.PERSONAL_SCORE_DB_PLAYS_COLUMNS
    )
    assert {"song_id", "chart_id", "score", "capture_hash", "duplicate_key"} <= set(
        score_schema.PERSONAL_SCORE_DB_PLAYS_COLUMNS
    )


def test_personal_score_db_analysis_logs_hold_review_status_not_save_values() -> None:
    log_columns = set(score_schema.PERSONAL_SCORE_DB_ANALYSIS_LOG_COLUMNS)

    assert {"identity_signal_status", "digit_review_status", "skip_reason"} <= log_columns
    assert {"score", "chart_id", "recognized_digits"}.isdisjoint(log_columns)
