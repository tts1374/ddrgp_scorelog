from __future__ import annotations

import sqlite3
from dataclasses import dataclass
from pathlib import Path

from .personal_score_db_save import write_personal_score_db_save
from .personal_score_db_save_adapter import (
    PersonalScoreDbSaveAdapterInput,
    adapt_personal_score_db_save_input,
)
from .personal_score_db_schema import prepare_personal_score_db_file_for_write


@dataclass(frozen=True)
class PersonalScoreDbFileSaveResult:
    db_path: Path
    adapter_status: str
    reasons: tuple[str, ...]
    written: bool
    source_capture_id: str | None
    analysis_id: str | None
    play_id: str | None


def save_personal_score_db_file(
    db_path: Path,
    adapter_input: PersonalScoreDbSaveAdapterInput,
) -> PersonalScoreDbFileSaveResult:
    """Adapt and write one explicit event to a formal personal score DB file."""
    path = Path(db_path)
    adapter_result = adapt_personal_score_db_save_input(adapter_input)
    if adapter_result.status == "unresolved":
        return PersonalScoreDbFileSaveResult(
            db_path=path,
            adapter_status=adapter_result.status,
            reasons=adapter_result.reasons,
            written=False,
            source_capture_id=None,
            analysis_id=None,
            play_id=None,
        )

    save_input = adapter_result.save_input
    if save_input is None:
        raise AssertionError("ready or excluded adapter result requires save input")

    prepare_personal_score_db_file_for_write(path)
    with sqlite3.connect(path) as connection:
        write_result = write_personal_score_db_save(connection, save_input)

    adapter_status = adapter_result.status
    reasons = adapter_result.reasons
    if adapter_status == "ready" and write_result.duplicate:
        adapter_status = "excluded"
        reasons = (write_result.skip_reason,)

    return PersonalScoreDbFileSaveResult(
        db_path=path,
        adapter_status=adapter_status,
        reasons=reasons,
        written=True,
        source_capture_id=write_result.source_capture_id,
        analysis_id=write_result.analysis_id,
        play_id=write_result.play_id,
    )
