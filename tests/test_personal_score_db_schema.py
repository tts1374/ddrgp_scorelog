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
        inspection = score_schema.inspect_personal_score_db_schema(connection)
        assert inspection.is_compatible
        assert inspection.user_version == score_schema.PERSONAL_SCORE_DB_SCHEMA_VERSION
        assert inspection.missing_required_tables == ()
        assert inspection.compatibility_errors == ()
        assert inspection.migration_plan_status == "compatible"
        assert inspection.migration_plan_reason == "schema_compatible"
        assert score_schema.assert_personal_score_db_compatible(connection) == inspection


def test_personal_score_db_schema_rejects_m8_preview_database() -> None:
    with sqlite3.connect(":memory:") as connection:
        runner.create_m8_score_db_schema(connection)

        errors = score_schema.personal_score_db_compatibility_errors(connection)
        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert "m8_preview_database_not_supported" in errors
    assert "score_db_metadata_missing" in errors
    assert "missing_table:schema_migrations" in errors
    assert "missing_table:source_captures" in errors
    assert "missing_table:analysis_logs" in errors
    assert inspection.migration_plan_status == "reject_m8_preview_database"
    assert inspection.migration_plan_reason == "preview_schema_is_not_production"


def test_personal_score_db_schema_inspects_empty_database_as_initializable() -> None:
    with sqlite3.connect(":memory:") as connection:
        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert inspection.table_names == ()
    assert inspection.missing_required_tables == (
        score_schema.PERSONAL_SCORE_DB_METADATA_TABLE,
        score_schema.PERSONAL_SCORE_DB_MIGRATIONS_TABLE,
        "source_captures",
        "plays",
        "analysis_logs",
    )
    assert "schema_version_mismatch" in inspection.compatibility_errors
    assert "score_db_metadata_missing" in inspection.compatibility_errors
    assert "unknown_database_not_supported" not in inspection.compatibility_errors
    assert inspection.migration_plan_status == "initialize_empty_database"
    assert inspection.migration_plan_reason == "no_user_tables"


def test_personal_score_db_schema_rejects_unknown_database_without_metadata() -> None:
    with sqlite3.connect(":memory:") as connection:
        connection.execute("PRAGMA user_version = 1")
        connection.execute("CREATE TABLE unrelated_table (id INTEGER PRIMARY KEY)")

        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert "unknown_database_not_supported" in inspection.compatibility_errors
    assert "score_db_metadata_missing" in inspection.compatibility_errors
    assert inspection.migration_plan_status == "reject_unknown_database"
    assert inspection.migration_plan_reason == (
        "metadata_does_not_identify_formal_schema"
    )


def test_personal_score_db_schema_rejects_metadata_identity_mismatch() -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        connection.execute(
            """
            UPDATE score_db_metadata
            SET value = ?
            WHERE key = 'schema_contract_scope'
            """,
            ("preview_minimal_plays",),
        )

        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert "score_db_metadata.schema_contract_scope_mismatch" in (
        inspection.compatibility_errors
    )
    assert inspection.migration_plan_status == "reject_unknown_database"


def test_personal_score_db_schema_marks_missing_table_for_manual_migration() -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        connection.execute("DROP TABLE analysis_logs")

        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert inspection.missing_required_tables == ("analysis_logs",)
    assert inspection.compatibility_errors == ("missing_table:analysis_logs",)
    assert inspection.migration_plan_status == "manual_migration_required"
    assert inspection.migration_plan_reason == "formal_schema_contract_mismatch"


def test_personal_score_db_schema_marks_user_version_mismatch_for_manual_migration() -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        connection.execute(
            f"PRAGMA user_version = {score_schema.PERSONAL_SCORE_DB_SCHEMA_VERSION + 1}"
        )

        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert inspection.compatibility_errors == ("schema_version_mismatch",)
    assert inspection.migration_plan_status == "manual_migration_required"


def test_personal_score_db_schema_assertion_reports_rejection_reasons() -> None:
    with sqlite3.connect(":memory:") as connection:
        connection.execute("CREATE TABLE unrelated_table (id INTEGER PRIMARY KEY)")

        try:
            score_schema.assert_personal_score_db_compatible(connection)
        except ValueError as error:
            message = str(error)
        else:
            raise AssertionError("expected incompatible DB to raise")

    assert "personal score DB is not compatible" in message
    assert "unknown_database_not_supported" in message
    assert "score_db_metadata_missing" in message


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
