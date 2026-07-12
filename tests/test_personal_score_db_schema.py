from __future__ import annotations

import json
import sqlite3
from pathlib import Path

import pytest

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
        assert tuple(
            connection.execute(
                "SELECT migration_id, schema_version FROM schema_migrations "
                "ORDER BY schema_version, migration_id"
            )
        ) == score_schema.PERSONAL_SCORE_DB_MIGRATION_HISTORY
        assert (
            table_column_names(connection, "plays")
            == list(score_schema.PERSONAL_SCORE_DB_PLAYS_COLUMNS)
        )
        assert (
            table_column_names(connection, "source_captures")
            == list(score_schema.PERSONAL_SCORE_DB_SOURCE_CAPTURE_COLUMNS)
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


def test_personal_score_db_schema_diagnostic_reports_compatible_database(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "ddrgp-scores.sqlite"
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        inspection = score_schema.inspect_personal_score_db_schema(connection)

    diagnostic = score_schema.personal_score_db_schema_inspection_diagnostic(
        inspection,
        path=db_path,
    )
    markdown = score_schema.format_personal_score_db_schema_diagnostic_markdown(
        diagnostic
    )

    assert diagnostic["path"] == str(db_path)
    assert diagnostic["is_compatible"] is True
    assert diagnostic["migration_plan_status"] == "compatible"
    assert diagnostic["compatibility_errors"] == []
    assert diagnostic["required_tables"] == {
        "present": list(score_schema.PERSONAL_SCORE_DB_REQUIRED_TABLES),
        "missing": [],
    }
    assert all(
        identity_diagnostic["status"] == "match"
        for identity_diagnostic in diagnostic["metadata_identity"].values()
    )
    assert "- migration_plan_status: `compatible`" in markdown
    assert "- `(none)`" in markdown
    assert f"- path: `{db_path}`" in markdown


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


@pytest.mark.parametrize("table_name", ["plays", "schema_migrations"])
def test_personal_score_db_schema_rejects_malformed_required_table(
    table_name: str,
) -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        connection.execute(f"DROP TABLE {table_name}")
        connection.execute(f"CREATE TABLE {table_name} (x TEXT)")

        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert inspection.is_compatible is False
    assert f"table_schema_mismatch:{table_name}" in inspection.compatibility_errors
    assert inspection.migration_plan_status == "manual_migration_required"


def test_personal_score_db_schema_diagnostic_reports_initializable_empty_database() -> None:
    with sqlite3.connect(":memory:") as connection:
        inspection = score_schema.inspect_personal_score_db_schema(connection)

    diagnostic = score_schema.personal_score_db_schema_inspection_diagnostic(inspection)
    markdown = score_schema.format_personal_score_db_schema_diagnostic_markdown(
        diagnostic
    )

    assert diagnostic["path"] == ""
    assert diagnostic["is_compatible"] is False
    assert diagnostic["migration_plan_status"] == "initialize_empty_database"
    assert diagnostic["migration_plan_reason"] == "no_user_tables"
    assert diagnostic["required_tables"]["present"] == []
    assert diagnostic["required_tables"]["missing"] == list(
        score_schema.PERSONAL_SCORE_DB_REQUIRED_TABLES
    )
    assert {
        identity_diagnostic["status"]
        for identity_diagnostic in diagnostic["metadata_identity"].values()
    } == {"missing"}
    assert "- path: `(none)`" in markdown
    assert "- migration_plan_status: `initialize_empty_database`" in markdown
    assert "`score_db_metadata`" in markdown


@pytest.mark.parametrize(
    ("fixture_name", "expected_status", "expected_reason", "expected_error"),
    [
        (
            "m8_preview",
            "reject_m8_preview_database",
            "preview_schema_is_not_production",
            "m8_preview_database_not_supported",
        ),
        (
            "unknown",
            "reject_unknown_database",
            "metadata_does_not_identify_formal_schema",
            "unknown_database_not_supported",
        ),
        (
            "manual_migration",
            "manual_migration_required",
            "formal_schema_contract_mismatch",
            "missing_table:analysis_logs",
        ),
    ],
)
def test_personal_score_db_schema_diagnostic_reports_rejection_fixtures(
    fixture_name: str,
    expected_status: str,
    expected_reason: str,
    expected_error: str,
) -> None:
    with sqlite3.connect(":memory:") as connection:
        if fixture_name == "m8_preview":
            runner.create_m8_score_db_schema(connection)
        elif fixture_name == "unknown":
            connection.execute("PRAGMA user_version = 1")
            connection.execute("CREATE TABLE unrelated_table (id INTEGER PRIMARY KEY)")
        elif fixture_name == "manual_migration":
            score_schema.create_personal_score_db_schema(connection)
            connection.execute("DROP TABLE analysis_logs")
        else:
            raise AssertionError(f"unknown fixture: {fixture_name}")
        inspection = score_schema.inspect_personal_score_db_schema(connection)

    diagnostic = score_schema.personal_score_db_schema_inspection_diagnostic(inspection)
    markdown = score_schema.format_personal_score_db_schema_diagnostic_markdown(
        diagnostic
    )

    assert diagnostic["is_compatible"] is False
    assert diagnostic["migration_plan_status"] == expected_status
    assert diagnostic["migration_plan_reason"] == expected_reason
    assert expected_error in diagnostic["compatibility_errors"]
    assert f"- migration_plan_status: `{expected_status}`" in markdown
    assert f"- `{expected_error}`" in markdown


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


def test_personal_score_db_initializes_empty_database_only() -> None:
    with sqlite3.connect(":memory:") as connection:
        result = score_schema.initialize_personal_score_db_if_empty(connection)
        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert result.initialized
    assert result.before.migration_plan_status == "initialize_empty_database"
    assert result.after == inspection
    assert inspection.is_compatible
    assert inspection.migration_plan_status == "compatible"
    assert inspection.table_names == tuple(
        sorted(score_schema.PERSONAL_SCORE_DB_REQUIRED_TABLES)
    )
    assert inspection.metadata == score_schema.PERSONAL_SCORE_DB_METADATA


def test_personal_score_db_prepare_for_write_initializes_empty_database() -> None:
    with sqlite3.connect(":memory:") as connection:
        inspection = score_schema.prepare_personal_score_db_for_write(connection)

    assert inspection.is_compatible
    assert inspection.migration_plan_status == "compatible"


def test_personal_score_db_file_prepare_initializes_new_database(tmp_path: Path) -> None:
    db_path = tmp_path / "ddrgp-scores.sqlite"

    result = score_schema.prepare_personal_score_db_file_for_write(db_path)

    assert not result.existed_before
    assert result.size_before is None
    assert result.initialized
    assert result.path == db_path
    assert result.inspection.is_compatible
    assert result.inspection.migration_plan_status == "compatible"
    with sqlite3.connect(db_path) as connection:
        assert table_names(connection) >= set(score_schema.PERSONAL_SCORE_DB_REQUIRED_TABLES)
        assert score_schema.read_score_db_metadata(connection) == (
            score_schema.PERSONAL_SCORE_DB_METADATA
        )


def test_personal_score_db_file_preparation_diagnostic_reports_file_summary(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "ddrgp-scores.sqlite"

    result = score_schema.prepare_personal_score_db_file_for_write(db_path)

    diagnostic = score_schema.personal_score_db_file_preparation_diagnostic(result)
    assert diagnostic["path"] == str(db_path)
    assert diagnostic["migration_plan_status"] == "compatible"
    assert diagnostic["file_preparation"] == {
        "existed_before": False,
        "size_before": None,
        "initialized": True,
        "initial_migration_plan_status": "initialize_empty_database",
        "final_migration_plan_status": "compatible",
    }
    markdown = score_schema.format_personal_score_db_schema_diagnostic_markdown(
        diagnostic
    )
    assert "## File Preparation" in markdown
    assert "- initialized: `true`" in markdown


def test_personal_score_db_cli_diagnostic_reports_compatible_database(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    db_path = tmp_path / "compatible.sqlite"
    with sqlite3.connect(db_path) as connection:
        score_schema.create_personal_score_db_schema(connection)

    exit_code = runner.main(["--personal-score-db-diagnostic", str(db_path)])
    output = capsys.readouterr().out

    assert exit_code == 0
    assert "# Personal Score DB Diagnostic" in output
    assert "- compatible: `true`" in output
    assert "- migration_plan_status: `compatible`" in output


def test_personal_score_db_cli_prepare_diagnostic_initializes_new_database(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    db_path = tmp_path / "new-formal.sqlite"

    exit_code = runner.main(
        [
            "--personal-score-db-diagnostic",
            str(db_path),
            "--personal-score-db-diagnostic-mode",
            "prepare-write",
        ]
    )
    output = capsys.readouterr().out

    assert exit_code == 0
    assert db_path.exists()
    assert "## File Preparation" in output
    assert "- initialized: `true`" in output
    assert "- initial_migration_plan_status: `initialize_empty_database`" in output
    with sqlite3.connect(db_path) as connection:
        assert score_schema.inspect_personal_score_db_schema(connection).is_compatible


@pytest.mark.parametrize(
    (
        "fixture_name",
        "diagnostic_mode",
        "expected_exit_code",
        "expected_fragment",
    ),
    [
        ("compatible", "inspect", 0, "- migration_plan_status: `compatible`"),
        ("empty_prepare", "prepare-write", 0, "- initialized: `true`"),
        (
            "m8_preview",
            "prepare-write",
            1,
            "- migration_plan_status: `reject_m8_preview_database`",
        ),
        (
            "unknown",
            "prepare-write",
            1,
            "- migration_plan_status: `reject_unknown_database`",
        ),
        (
            "manual_migration",
            "prepare-write",
            1,
            "- migration_plan_status: `manual_migration_required`",
        ),
        ("non_sqlite", "prepare-write", 1, "- `invalid_sqlite_database`"),
        ("directory", "prepare-write", 1, "- `path_is_directory`"),
    ],
)
def test_personal_score_db_cli_diagnostic_output_writes_markdown_under_data(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    fixture_name: str,
    diagnostic_mode: str,
    expected_exit_code: int,
    expected_fragment: str,
) -> None:
    db_path = tmp_path / f"{fixture_name}.sqlite"
    if fixture_name == "compatible":
        with sqlite3.connect(db_path) as connection:
            score_schema.create_personal_score_db_schema(connection)
    elif fixture_name == "empty_prepare":
        pass
    elif fixture_name == "m8_preview":
        with sqlite3.connect(db_path) as connection:
            runner.create_m8_score_db_schema(connection)
    elif fixture_name == "unknown":
        with sqlite3.connect(db_path) as connection:
            connection.execute("PRAGMA user_version = 1")
            connection.execute("CREATE TABLE unrelated_table (id INTEGER PRIMARY KEY)")
    elif fixture_name == "manual_migration":
        with sqlite3.connect(db_path) as connection:
            score_schema.create_personal_score_db_schema(connection)
            connection.execute("DROP TABLE analysis_logs")
    elif fixture_name == "non_sqlite":
        db_path.write_bytes(b"not a sqlite database")
    elif fixture_name == "directory":
        db_path.mkdir()
    else:
        raise AssertionError(f"unknown fixture: {fixture_name}")

    monkeypatch.chdir(tmp_path)
    output_path = Path("data/diagnostics") / f"{fixture_name}.md"
    log_output_path = Path("logs/diagnostics") / "personal-score-db.jsonl"

    exit_code = runner.main(
        [
            "--personal-score-db-diagnostic",
            str(db_path),
            "--personal-score-db-diagnostic-mode",
            diagnostic_mode,
            "--personal-score-db-diagnostic-output",
            str(output_path),
            "--personal-score-db-diagnostic-log-output",
            str(log_output_path),
        ]
    )
    stdout = capsys.readouterr().out
    output_text = output_path.read_text(encoding="utf-8")
    log_records = [
        json.loads(line)
        for line in log_output_path.read_text(encoding="utf-8").splitlines()
    ]

    assert exit_code == expected_exit_code
    assert "# Personal Score DB Diagnostic" in stdout
    assert "# Personal Score DB Diagnostic" in output_text
    assert expected_fragment in stdout
    assert expected_fragment in output_text
    assert len(log_records) == 1
    assert set(log_records[0]) == set(
        runner.PERSONAL_SCORE_DB_DIAGNOSTIC_LOG_REQUIRED_KEYS
    )
    assert runner.personal_score_db_diagnostic_log_schema_errors(log_records[0]) == []
    assert log_records[0]["event_type"] == "personal_score_db_diagnostic"
    assert log_records[0]["log_schema_version"] == 1
    assert log_records[0]["mode"] == diagnostic_mode
    assert log_records[0]["format"] == "markdown"
    assert log_records[0]["exit_code"] == expected_exit_code
    assert log_records[0]["status"] == (
        "compatible" if expected_exit_code == 0 else "rejected"
    )
    assert log_records[0]["db_path"] == str(db_path)
    assert log_records[0]["diagnostic_output_path"] == str(output_path)
    assert log_records[0]["diagnostic"]["migration_plan_status"] in stdout


def test_personal_score_db_cli_diagnostic_output_writes_json_under_data(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    db_path = tmp_path / "compatible.sqlite"
    with sqlite3.connect(db_path) as connection:
        score_schema.create_personal_score_db_schema(connection)

    monkeypatch.chdir(tmp_path)
    output_path = Path("data/diagnostics/compatible.json")
    log_output_path = Path("logs/diagnostics/personal-score-db.jsonl")

    exit_code = runner.main(
        [
            "--personal-score-db-diagnostic",
            str(db_path),
            "--personal-score-db-diagnostic-format",
            "json",
            "--personal-score-db-diagnostic-output",
            str(output_path),
            "--personal-score-db-diagnostic-log-output",
            str(log_output_path),
        ]
    )

    stdout_diagnostic = json.loads(capsys.readouterr().out)
    file_diagnostic = json.loads(output_path.read_text(encoding="utf-8"))
    log_record = json.loads(log_output_path.read_text(encoding="utf-8"))

    assert exit_code == 0
    assert stdout_diagnostic == file_diagnostic
    assert file_diagnostic["is_compatible"] is True
    assert file_diagnostic["migration_plan_status"] == "compatible"
    assert log_record["format"] == "json"
    assert log_record["exit_code"] == 0
    assert log_record["diagnostic_output_path"] == str(output_path)
    assert log_record["diagnostic"] == file_diagnostic
    assert runner.personal_score_db_diagnostic_log_schema_errors(log_record) == []


def test_personal_score_db_cli_diagnostic_log_output_appends_jsonl_under_logs(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    db_path = tmp_path / "compatible.sqlite"
    with sqlite3.connect(db_path) as connection:
        score_schema.create_personal_score_db_schema(connection)

    monkeypatch.chdir(tmp_path)
    log_output_path = Path("logs/diagnostics/personal-score-db.jsonl")

    for _ in range(2):
        assert (
            runner.main(
                [
                    "--personal-score-db-diagnostic",
                    str(db_path),
                    "--personal-score-db-diagnostic-log-output",
                    str(log_output_path),
                ]
            )
            == 0
        )
        capsys.readouterr()

    log_lines = log_output_path.read_text(encoding="utf-8").splitlines()
    assert all(log_lines)
    log_records = [json.loads(line) for line in log_lines]
    assert len(log_records) == 2
    assert all(
        runner.personal_score_db_diagnostic_log_schema_errors(record) == []
        for record in log_records
    )
    assert [record["event_type"] for record in log_records] == [
        "personal_score_db_diagnostic",
        "personal_score_db_diagnostic",
    ]
    assert [record["diagnostic"]["migration_plan_status"] for record in log_records] == [
        "compatible",
        "compatible",
    ]


def test_personal_score_db_diagnostic_log_schema_errors_reject_invalid_records(
    tmp_path: Path,
) -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        diagnostic = score_schema.personal_score_db_schema_inspection_diagnostic(
            score_schema.inspect_personal_score_db_schema(connection)
        )
    record = runner.personal_score_db_diagnostic_log_record(
        diagnostic,
        db_path=tmp_path / "compatible.sqlite",
        mode="inspect",
        output_format="markdown",
        diagnostic_output_path=None,
        exit_code=0,
    )

    assert set(record) == set(runner.PERSONAL_SCORE_DB_DIAGNOSTIC_LOG_REQUIRED_KEYS)
    assert runner.personal_score_db_diagnostic_log_schema_errors(record) == []

    missing_format = dict(record)
    del missing_format["format"]
    assert "missing_key:format" in (
        runner.personal_score_db_diagnostic_log_schema_errors(missing_format)
    )

    mismatched_exit_code = dict(record)
    mismatched_exit_code["exit_code"] = 1
    mismatched_exit_code["status"] = "rejected"
    assert "exit_code_diagnostic_mismatch" in (
        runner.personal_score_db_diagnostic_log_schema_errors(mismatched_exit_code)
    )
    assert "status_diagnostic_mismatch" in (
        runner.personal_score_db_diagnostic_log_schema_errors(mismatched_exit_code)
    )

    invalid_diagnostic = dict(record)
    invalid_diagnostic["diagnostic"] = {}
    assert "diagnostic.is_compatible_missing" in (
        runner.personal_score_db_diagnostic_log_schema_errors(invalid_diagnostic)
    )


def test_personal_score_db_diagnostic_log_output_rejects_invalid_record_before_write(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.chdir(tmp_path)
    log_output_path = Path("logs/diagnostics/personal-score-db.jsonl")

    with pytest.raises(
        ValueError,
        match="personal score DB diagnostic log record is invalid",
    ):
        runner.append_personal_score_db_diagnostic_log_output(
            log_output_path,
            record={
                "log_schema_version": 1,
                "event_type": "personal_score_db_diagnostic",
                "mode": "inspect",
                "format": "markdown",
                "exit_code": 0,
                "status": "rejected",
                "db_path": "db.sqlite",
                "diagnostic_output_path": "",
                "diagnostic": {"is_compatible": True},
            },
        )

    assert not log_output_path.exists()


def test_personal_score_db_cli_diagnostic_output_rejects_outside_data(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    db_path = tmp_path / "new-formal.sqlite"
    monkeypatch.chdir(tmp_path)

    with pytest.raises(
        ValueError,
        match="--personal-score-db-diagnostic-output must be under data/",
    ):
        runner.main(
            [
                "--personal-score-db-diagnostic",
                str(db_path),
                "--personal-score-db-diagnostic-mode",
                "prepare-write",
                "--personal-score-db-diagnostic-output",
                str(tmp_path / "diagnostic.md"),
            ]
        )

    assert not db_path.exists()


def test_personal_score_db_cli_diagnostic_log_output_rejects_outside_logs_before_prepare(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    db_path = tmp_path / "new-formal.sqlite"
    monkeypatch.chdir(tmp_path)

    with pytest.raises(
        ValueError,
        match="--personal-score-db-diagnostic-log-output must be under logs/",
    ):
        runner.main(
            [
                "--personal-score-db-diagnostic",
                str(db_path),
                "--personal-score-db-diagnostic-mode",
                "prepare-write",
                "--personal-score-db-diagnostic-log-output",
                str(tmp_path / "diagnostic.jsonl"),
            ]
        )

    assert not db_path.exists()


def test_personal_score_db_cli_diagnostic_log_output_rejects_non_jsonl_before_prepare(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    db_path = tmp_path / "new-formal.sqlite"
    monkeypatch.chdir(tmp_path)

    with pytest.raises(
        ValueError,
        match="--personal-score-db-diagnostic-log-output extension must be JSONL",
    ):
        runner.main(
            [
                "--personal-score-db-diagnostic",
                str(db_path),
                "--personal-score-db-diagnostic-mode",
                "prepare-write",
                "--personal-score-db-diagnostic-log-output",
                "logs/diagnostics/diagnostic.json",
            ]
        )

    assert not db_path.exists()


def test_personal_score_db_cli_diagnostic_output_rejects_format_extension_mismatch(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    db_path = tmp_path / "new-formal.sqlite"
    monkeypatch.chdir(tmp_path)

    with pytest.raises(
        ValueError,
        match=(
            "--personal-score-db-diagnostic-output extension must match "
            "--personal-score-db-diagnostic-format markdown"
        ),
    ):
        runner.main(
            [
                "--personal-score-db-diagnostic",
                str(db_path),
                "--personal-score-db-diagnostic-mode",
                "prepare-write",
                "--personal-score-db-diagnostic-output",
                "data/diagnostics/diagnostic.json",
            ]
        )

    assert not db_path.exists()


@pytest.mark.parametrize(
    ("fixture_name", "expected_status", "expected_error"),
    [
        (
            "m8_preview",
            "reject_m8_preview_database",
            "m8_preview_database_not_supported",
        ),
        (
            "unknown",
            "reject_unknown_database",
            "unknown_database_not_supported",
        ),
        (
            "manual_migration",
            "manual_migration_required",
            "missing_table:analysis_logs",
        ),
    ],
)
def test_personal_score_db_cli_prepare_diagnostic_rejects_without_modifying(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    fixture_name: str,
    expected_status: str,
    expected_error: str,
) -> None:
    db_path = tmp_path / f"{fixture_name}.sqlite"
    with sqlite3.connect(db_path) as connection:
        if fixture_name == "m8_preview":
            runner.create_m8_score_db_schema(connection)
        elif fixture_name == "unknown":
            connection.execute("PRAGMA user_version = 1")
            connection.execute("CREATE TABLE unrelated_table (id INTEGER PRIMARY KEY)")
        elif fixture_name == "manual_migration":
            score_schema.create_personal_score_db_schema(connection)
            connection.execute("DROP TABLE analysis_logs")
        else:
            raise AssertionError(f"unknown fixture: {fixture_name}")
        before = score_schema.inspect_personal_score_db_schema(connection)

    exit_code = runner.main(
        [
            "--personal-score-db-diagnostic",
            str(db_path),
            "--personal-score-db-diagnostic-mode",
            "prepare-write",
        ]
    )
    output = capsys.readouterr().out

    with sqlite3.connect(db_path) as connection:
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert exit_code == 1
    assert f"- migration_plan_status: `{expected_status}`" in output
    assert f"- `{expected_error}`" in output
    assert after == before


def test_personal_score_db_cli_prepare_diagnostic_rejects_non_sqlite_file(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    db_path = tmp_path / "not-sqlite.sqlite"
    original_bytes = b"not a sqlite database"
    db_path.write_bytes(original_bytes)

    exit_code = runner.main(
        [
            "--personal-score-db-diagnostic",
            str(db_path),
            "--personal-score-db-diagnostic-mode",
            "prepare-write",
        ]
    )
    output = capsys.readouterr().out

    assert exit_code == 1
    assert "- `invalid_sqlite_database`" in output
    assert db_path.read_bytes() == original_bytes


def test_personal_score_db_cli_prepare_diagnostic_rejects_directory(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    exit_code = runner.main(
        [
            "--personal-score-db-diagnostic",
            str(tmp_path),
            "--personal-score-db-diagnostic-mode",
            "prepare-write",
        ]
    )
    output = capsys.readouterr().out

    assert exit_code == 1
    assert "- `path_is_directory`" in output


def test_personal_score_db_file_prepare_initializes_empty_existing_file(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "ddrgp-scores.sqlite"
    db_path.write_bytes(b"")

    result = score_schema.prepare_personal_score_db_file_for_write(db_path)

    assert result.existed_before
    assert result.size_before == 0
    assert result.initialized
    assert result.initialization.before.migration_plan_status == "initialize_empty_database"
    assert result.inspection.is_compatible


def test_personal_score_db_file_prepare_keeps_compatible_database(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "ddrgp-scores.sqlite"
    with sqlite3.connect(db_path) as connection:
        score_schema.create_personal_score_db_schema(connection)
        before = score_schema.inspect_personal_score_db_schema(connection)

    result = score_schema.prepare_personal_score_db_file_for_write(db_path)

    assert result.existed_before
    assert not result.initialized
    assert result.initialization.before == before
    assert result.inspection == before
    with sqlite3.connect(db_path) as connection:
        assert score_schema.inspect_personal_score_db_schema(connection) == before


def test_personal_score_db_initialization_keeps_compatible_database_unchanged() -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        before = score_schema.inspect_personal_score_db_schema(connection)

        result = score_schema.initialize_personal_score_db_if_empty(connection)
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert not result.initialized
    assert result.before == before
    assert result.after == before
    assert after == before


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


def test_personal_score_db_initialization_does_not_modify_unknown_database() -> None:
    with sqlite3.connect(":memory:") as connection:
        connection.execute("PRAGMA user_version = 1")
        connection.execute("CREATE TABLE unrelated_table (id INTEGER PRIMARY KEY)")
        before = score_schema.inspect_personal_score_db_schema(connection)

        result = score_schema.initialize_personal_score_db_if_empty(connection)
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert not result.initialized
    assert result.before == before
    assert result.after == before
    assert after == before
    assert after.table_names == ("unrelated_table",)


def test_personal_score_db_file_prepare_does_not_modify_unknown_database(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "unknown.sqlite"
    with sqlite3.connect(db_path) as connection:
        connection.execute("PRAGMA user_version = 1")
        connection.execute("CREATE TABLE unrelated_table (id INTEGER PRIMARY KEY)")
        before = score_schema.inspect_personal_score_db_schema(connection)

    try:
        score_schema.prepare_personal_score_db_file_for_write(db_path)
    except ValueError as error:
        message = str(error)
    else:
        raise AssertionError("expected unknown DB to raise")

    with sqlite3.connect(db_path) as connection:
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert "reject_unknown_database" in message
    assert "unknown_database_not_supported" in message
    assert after == before
    assert after.table_names == ("unrelated_table",)


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


def test_personal_score_db_prepare_for_write_rejects_metadata_identity_mismatch() -> None:
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

        try:
            score_schema.prepare_personal_score_db_for_write(connection)
        except ValueError as error:
            message = str(error)
        else:
            raise AssertionError("expected incompatible DB to raise")

        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert "reject_unknown_database" in message
    assert "score_db_metadata.schema_contract_scope_mismatch" in message
    assert inspection.metadata["schema_contract_scope"] == "preview_minimal_plays"


def test_personal_score_db_file_prepare_does_not_modify_metadata_identity_mismatch(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "identity-mismatch.sqlite"
    with sqlite3.connect(db_path) as connection:
        score_schema.create_personal_score_db_schema(connection)
        connection.execute(
            """
            UPDATE score_db_metadata
            SET value = ?
            WHERE key = 'schema_contract_scope'
            """,
            ("preview_minimal_plays",),
        )
        before = score_schema.inspect_personal_score_db_schema(connection)

    try:
        score_schema.prepare_personal_score_db_file_for_write(db_path)
    except ValueError as error:
        message = str(error)
    else:
        raise AssertionError("expected identity mismatch DB to raise")

    with sqlite3.connect(db_path) as connection:
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert "reject_unknown_database" in message
    assert "score_db_metadata.schema_contract_scope_mismatch" in message
    assert after == before


def test_personal_score_db_schema_marks_missing_table_for_manual_migration() -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        connection.execute("DROP TABLE analysis_logs")

        inspection = score_schema.inspect_personal_score_db_schema(connection)

    assert inspection.missing_required_tables == ("analysis_logs",)
    assert inspection.compatibility_errors == ("missing_table:analysis_logs",)
    assert inspection.migration_plan_status == "manual_migration_required"
    assert inspection.migration_plan_reason == "formal_schema_contract_mismatch"


def test_personal_score_db_initialization_does_not_modify_manual_migration_candidate() -> None:
    with sqlite3.connect(":memory:") as connection:
        score_schema.create_personal_score_db_schema(connection)
        connection.execute("DROP TABLE analysis_logs")
        before = score_schema.inspect_personal_score_db_schema(connection)

        result = score_schema.initialize_personal_score_db_if_empty(connection)
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert before.migration_plan_status == "manual_migration_required"
    assert not result.initialized
    assert result.before == before
    assert result.after == before
    assert after == before
    assert "analysis_logs" not in after.table_names


def test_personal_score_db_file_prepare_does_not_modify_manual_migration_candidate(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "manual-migration.sqlite"
    with sqlite3.connect(db_path) as connection:
        score_schema.create_personal_score_db_schema(connection)
        connection.execute("DROP TABLE analysis_logs")
        before = score_schema.inspect_personal_score_db_schema(connection)

    try:
        score_schema.prepare_personal_score_db_file_for_write(db_path)
    except ValueError as error:
        message = str(error)
    else:
        raise AssertionError("expected manual migration candidate to raise")

    with sqlite3.connect(db_path) as connection:
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert "manual_migration_required" in message
    assert "missing_table:analysis_logs" in message
    assert after == before
    assert "analysis_logs" not in after.table_names


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


def test_personal_score_db_initialization_does_not_modify_m8_preview_database() -> None:
    with sqlite3.connect(":memory:") as connection:
        runner.create_m8_score_db_schema(connection)
        before = score_schema.inspect_personal_score_db_schema(connection)

        result = score_schema.initialize_personal_score_db_if_empty(connection)
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert before.migration_plan_status == "reject_m8_preview_database"
    assert not result.initialized
    assert result.before == before
    assert result.after == before
    assert after == before
    assert "preview_metadata" in after.table_names
    assert score_schema.PERSONAL_SCORE_DB_METADATA_TABLE not in after.table_names


def test_personal_score_db_file_prepare_does_not_modify_m8_preview_database(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "preview.sqlite"
    with sqlite3.connect(db_path) as connection:
        runner.create_m8_score_db_schema(connection)
        before = score_schema.inspect_personal_score_db_schema(connection)

    try:
        score_schema.prepare_personal_score_db_file_for_write(db_path)
    except ValueError as error:
        message = str(error)
    else:
        raise AssertionError("expected preview DB to raise")

    with sqlite3.connect(db_path) as connection:
        after = score_schema.inspect_personal_score_db_schema(connection)

    assert "reject_m8_preview_database" in message
    assert "m8_preview_database_not_supported" in message
    assert after == before
    assert "preview_metadata" in after.table_names
    assert score_schema.PERSONAL_SCORE_DB_METADATA_TABLE not in after.table_names


def test_personal_score_db_file_prepare_rejects_non_sqlite_file(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "not-sqlite.sqlite"
    original_bytes = b"not a sqlite database"
    db_path.write_bytes(original_bytes)

    try:
        score_schema.prepare_personal_score_db_file_for_write(db_path)
    except ValueError as error:
        message = str(error)
    else:
        raise AssertionError("expected non-SQLite file to raise")

    assert "invalid_sqlite_database" in message
    assert db_path.read_bytes() == original_bytes


def test_personal_score_db_file_prepare_rejects_directory(tmp_path: Path) -> None:
    try:
        score_schema.prepare_personal_score_db_file_for_write(tmp_path)
    except ValueError as error:
        message = str(error)
    else:
        raise AssertionError("expected directory path to raise")

    assert "personal score DB path is a directory" in message


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

    assert {
        "source_capture_id",
        "identity_signal_status",
        "digit_review_status",
        "skip_reason",
        "analysis_summary_json",
        "log_path",
    } <= log_columns
    assert {
        "score",
        "chart_id",
        "recognized_digits",
        "diagnostic",
        "diagnostic_output_path",
    }.isdisjoint(log_columns)


def test_personal_score_db_source_captures_hold_frame_references_not_logs() -> None:
    source_capture_columns = set(score_schema.PERSONAL_SCORE_DB_SOURCE_CAPTURE_COLUMNS)

    assert {
        "capture_id",
        "capture_hash",
        "captured_at",
        "source_kind",
        "source_path",
        "manifest_image_path",
        "frame_index",
    } <= source_capture_columns
    assert {
        "analysis_status",
        "skip_reason",
        "analysis_summary_json",
        "log_path",
        "diagnostic",
        "diagnostic_output_path",
    }.isdisjoint(source_capture_columns)
