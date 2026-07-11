from __future__ import annotations

import sqlite3
from dataclasses import replace
from pathlib import Path

import pytest

from tools.vision_poc import personal_score_db_file_save as file_save
from tools.vision_poc import personal_score_db_save_adapter as adapter
from tools.vision_poc import personal_score_db_schema as score_schema


def adapter_input(suffix: str = "one") -> adapter.PersonalScoreDbSaveAdapterInput:
    return adapter.PersonalScoreDbSaveAdapterInput(
        candidate_material={
            "identity_signal_song_id": "candidate-song",
            "recognized_digits": "111111",
            "played_at_ms": "0",
        },
        capture_id=f"capture-{suffix}",
        capture_hash=f"sha256:{suffix}",
        captured_at="2026-07-11T12:34:56+09:00",
        source_kind="manifest",
        source_path=f"samples/{suffix}.png",
        analysis_id=f"analysis-{suffix}",
        event_type="confirmed",
        confirmed_result=True,
        duplicate=False,
        confirmation_mode="time",
        identity_signal_status="composite_resolved_candidate",
        digit_review_status="reviewed",
        analysis_confidence=0.98,
        analysis_summary_json='{"contract": "formal-file-save-v1"}',
        app_version="0.1.0",
        formal_play=adapter.PersonalScoreDbFormalPlayValues(
            play_id=f"play-{suffix}",
            played_at="2026-07-11T12:34:56+09:00",
            master_version="2026-07-11",
            song_id="formal-song",
            chart_id="formal-chart",
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
            duplicate_key=f"play:v1:{suffix}",
        ),
    )


def row_counts(db_path: Path) -> tuple[int, int, int]:
    with sqlite3.connect(db_path) as connection:
        return tuple(
            int(connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0])
            for table in ("source_captures", "plays", "analysis_logs")
        )


@pytest.mark.parametrize("existing_empty_file", [False, True])
def test_file_save_writes_ready_event_to_new_or_zero_byte_database(
    tmp_path: Path,
    existing_empty_file: bool,
) -> None:
    db_path = tmp_path / "formal.sqlite"
    if existing_empty_file:
        db_path.touch()

    result = file_save.save_personal_score_db_file(db_path, adapter_input())

    assert result.db_path == db_path
    assert result.adapter_status == "ready"
    assert result.reasons == ()
    assert result.written
    assert result.source_capture_id == "capture-one"
    assert result.analysis_id == "analysis-one"
    assert result.play_id == "play-one"
    assert row_counts(db_path) == (1, 1, 1)


def test_file_save_appends_to_compatible_database(tmp_path: Path) -> None:
    db_path = tmp_path / "formal.sqlite"

    file_save.save_personal_score_db_file(db_path, adapter_input("first"))
    result = file_save.save_personal_score_db_file(db_path, adapter_input("second"))

    assert result.adapter_status == "ready"
    assert result.play_id == "play-second"
    assert row_counts(db_path) == (2, 2, 2)


@pytest.mark.parametrize(
    ("exclusion", "duplicate", "expected_status"),
    [
        (None, True, "skipped"),
        (
            adapter.PersonalScoreDbSaveExclusion(
                kind="low_confidence",
                reason="reviewed_confidence_too_low",
            ),
            False,
            "low_confidence",
        ),
        (
            adapter.PersonalScoreDbSaveExclusion(
                kind="skipped",
                reason="manual_skip",
            ),
            False,
            "skipped",
        ),
    ],
)
def test_file_save_writes_excluded_analysis_without_play(
    tmp_path: Path,
    exclusion: adapter.PersonalScoreDbSaveExclusion | None,
    duplicate: bool,
    expected_status: str,
) -> None:
    db_path = tmp_path / "formal.sqlite"
    request = replace(
        adapter_input(),
        duplicate=duplicate,
        exclusion=exclusion,
        formal_play=None,
    )

    result = file_save.save_personal_score_db_file(db_path, request)

    assert result.adapter_status == "excluded"
    assert result.written
    assert result.play_id is None
    assert row_counts(db_path) == (1, 0, 1)
    with sqlite3.connect(db_path) as connection:
        analysis_status = connection.execute(
            "SELECT analysis_status FROM analysis_logs"
        ).fetchone()[0]
    assert analysis_status == expected_status


def test_file_save_rejects_unresolved_before_creating_database_or_parent(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "missing-parent" / "formal.sqlite"
    request = replace(adapter_input(), formal_play=None)

    result = file_save.save_personal_score_db_file(db_path, request)

    assert result.adapter_status == "unresolved"
    assert result.reasons == ("formal_play_required",)
    assert not result.written
    assert result.source_capture_id is None
    assert result.analysis_id is None
    assert result.play_id is None
    assert not db_path.parent.exists()


def rejection_database(db_path: Path, rejection_kind: str) -> None:
    if rejection_kind == "preview":
        with sqlite3.connect(db_path) as connection:
            connection.execute("CREATE TABLE preview_metadata (key TEXT, value TEXT)")
            connection.commit()
    elif rejection_kind == "unknown":
        with sqlite3.connect(db_path) as connection:
            connection.execute("CREATE TABLE unknown_table (value TEXT)")
            connection.commit()
    elif rejection_kind == "identity_mismatch":
        with sqlite3.connect(db_path) as connection:
            score_schema.create_personal_score_db_schema(connection)
            connection.execute(
                "UPDATE score_db_metadata SET value = ? WHERE key = ?",
                ("other_schema", "schema_name"),
            )
            connection.commit()
    elif rejection_kind == "manual_migration":
        with sqlite3.connect(db_path) as connection:
            score_schema.create_personal_score_db_schema(connection)
            connection.execute("DROP TABLE analysis_logs")
            connection.commit()
    elif rejection_kind == "non_sqlite":
        db_path.write_text("not sqlite", encoding="utf-8", newline="\n")
    else:
        raise AssertionError(f"unsupported rejection fixture: {rejection_kind}")


@pytest.mark.parametrize(
    ("rejection_kind", "expected_error"),
    [
        ("preview", "reject_m8_preview_database"),
        ("unknown", "reject_unknown_database"),
        ("identity_mismatch", "reject_unknown_database"),
        ("manual_migration", "manual_migration_required"),
        ("non_sqlite", "invalid_sqlite_database"),
    ],
)
def test_file_save_rejects_incompatible_database_without_modifying_it(
    tmp_path: Path,
    rejection_kind: str,
    expected_error: str,
) -> None:
    db_path = tmp_path / "rejected.sqlite"
    rejection_database(db_path, rejection_kind)
    before = db_path.read_bytes()

    with pytest.raises(ValueError, match=expected_error):
        file_save.save_personal_score_db_file(db_path, adapter_input())

    assert db_path.read_bytes() == before


def test_file_save_rejects_directory(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="path is a directory"):
        file_save.save_personal_score_db_file(tmp_path, adapter_input())


def test_file_save_preserves_writer_rollback_on_insert_failure(tmp_path: Path) -> None:
    db_path = tmp_path / "formal.sqlite"
    first = adapter_input("first")
    second = adapter_input("second")
    assert first.formal_play is not None
    assert second.formal_play is not None
    second = replace(
        second,
        formal_play=replace(
            second.formal_play,
            duplicate_key=first.formal_play.duplicate_key,
        ),
    )
    file_save.save_personal_score_db_file(db_path, first)

    with pytest.raises(sqlite3.IntegrityError, match="UNIQUE constraint failed"):
        file_save.save_personal_score_db_file(db_path, second)

    assert row_counts(db_path) == (1, 1, 1)
