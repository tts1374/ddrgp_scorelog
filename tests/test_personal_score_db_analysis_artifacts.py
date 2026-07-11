from __future__ import annotations

import copy
import json
from pathlib import Path

import pytest

from tools.vision_poc import personal_score_db_analysis_artifacts as artifacts
from tools.vision_poc import personal_score_db_save as score_save

FIXTURE_ROOT = Path(__file__).parent / "fixtures/personal_score_db_analysis_artifacts"


def _fixture(name: str) -> dict[str, object]:
    return json.loads((FIXTURE_ROOT / name).read_text(encoding="utf-8"))


@pytest.mark.parametrize("name", ["low-confidence-v1.json", "error-v1.json"])
def test_analysis_detail_v1_fixtures_are_valid(name: str) -> None:
    artifacts.validate_analysis_detail(_fixture(name))


def test_analysis_detail_builder_constructs_low_confidence_contract() -> None:
    fixture = _fixture("low-confidence-v1.json")
    built = artifacts.build_analysis_detail(
        generated_at=fixture["generated_at"],
        app_version=fixture["app_version"],
        analysis_id=fixture["analysis_id"],
        source_capture_id=fixture["source_capture_id"],
        analysis_status=fixture["analysis_status"],
        save_boundary_status=fixture["save_boundary_status"],
        skip_reason=fixture["skip_reason"],
        event=fixture["event"],
        review=fixture["review"],
        candidate_material=fixture["investigation"]["candidate_material"],
        diagnostic_summary=fixture["investigation"]["diagnostic_summary"],
        failure_image_path=fixture["failure_image_path"],
        retention_class=fixture["retention"]["retention_class"],
    )

    assert built == fixture


def test_analysis_detail_retention_is_deterministic() -> None:
    assert artifacts.analysis_detail_retention_metadata(
        "short", "2026-07-12T01:00:00Z"
    ) == {
        "retention_class": "short",
        "basis_at": "2026-07-12T01:00:00Z",
        "expires_at": "2026-07-19T01:00:00Z",
    }
    assert artifacts.analysis_detail_retention_metadata(
        "indefinite", "2026-07-12T01:00:00+00:00"
    ) == {
        "retention_class": "indefinite",
        "basis_at": "2026-07-12T01:00:00Z",
        "expires_at": None,
    }


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        (lambda value: value.update(schema_version=2), "schema_version"),
        (lambda value: value.update(unknown=True), "unknown"),
        (lambda value: value.update(analysis_status=True), "analysis_status"),
        (lambda value: value.update(skip_reason=""), "skip_reason"),
        (lambda value: value["event"].update(duplicate=True), "duplicate"),
        (lambda value: value["retention"].update(expires_at=None), "retention"),
    ],
)
def test_analysis_detail_rejects_invalid_contract(mutate: object, message: str) -> None:
    payload = _fixture("low-confidence-v1.json")
    mutate(payload)
    with pytest.raises(ValueError, match=message):
        artifacts.validate_analysis_detail(payload)


@pytest.mark.parametrize(
    "forbidden_key",
    [
        "score",
        "song_id",
        "validation_result_schema_version",
        "diagnostic",
        "diagnostic_output_path",
    ],
)
def test_analysis_detail_rejects_formal_receipt_and_db_diagnostic_keys(
    forbidden_key: str,
) -> None:
    payload = _fixture("low-confidence-v1.json")
    payload["investigation"]["candidate_material"][0][forbidden_key] = "forbidden"
    with pytest.raises(ValueError, match="forbidden projection keys"):
        artifacts.validate_analysis_detail(payload)


@pytest.mark.parametrize(
    "path",
    [
        "../logs/analysis_details/detail.json",
        "data/analysis_details/detail.json",
        "logs/diagnostics/detail.json",
        "logs/analysis_details/detail.jsonl",
        "C:/logs/analysis_details/detail.json",
    ],
)
def test_analysis_log_path_rejects_unsafe_or_wrong_namespace(path: str) -> None:
    with pytest.raises(ValueError):
        artifacts.validate_analysis_detail_log_path(path)


def test_analysis_log_path_keeps_empty_compatibility_and_accepts_detail_json() -> None:
    artifacts.validate_analysis_detail_log_path("")
    artifacts.validate_analysis_detail_log_path("logs/analysis_details/2026/07/a.json")


def test_save_input_validation_applies_analysis_log_path_boundary() -> None:
    analysis = score_save.PersonalScoreDbAnalysisInput(
        analysis_id="analysis-001",
        play_id=None,
        source_capture_id="capture-001",
        analysis_status="low_confidence",
        save_boundary_status="excluded",
        skip_reason="identity_below_threshold",
        event_type="confirmed",
        confirmed_result=True,
        duplicate=False,
        confirmation_mode="time",
        timestamp_ms=1000,
        candidate_duration_ms=1000,
        identity_signal_status="ambiguous",
        digit_review_status="recognized",
        analysis_confidence=0.42,
        analysis_summary_json="{}",
        log_path="logs/diagnostics/db.jsonl",
        app_version="test-v1",
    )
    save_input = score_save.PersonalScoreDbSaveInput(
        source_capture=score_save.PersonalScoreDbSourceCaptureInput(
            capture_id="capture-001",
            capture_hash="sha256:001",
            captured_at="2026-07-12T01:00:00Z",
            source_kind="manual",
            source_path="capture.png",
        ),
        play=None,
        analysis=analysis,
    )

    assert "analysis.log_path_invalid" in score_save.personal_score_db_save_input_errors(
        save_input
    )


@pytest.mark.parametrize(
    "path",
    [
        "logs/analysis_details/failure.png",
        "logs/analysis_failures/../failure.png",
        "logs/analysis_failures/failure.json",
        "source.png",
    ],
)
def test_failure_image_path_rejects_other_responsibilities(path: str) -> None:
    with pytest.raises(ValueError):
        artifacts.validate_analysis_failure_image_path(path)


def test_pure_contract_does_not_change_filesystem(tmp_path: Path) -> None:
    before = sorted(tmp_path.rglob("*"))
    payload = copy.deepcopy(_fixture("low-confidence-v1.json"))
    artifacts.validate_analysis_detail(payload)
    artifacts.validate_analysis_detail_log_path(
        "logs/analysis_details/2026/07/analysis-low-001.json"
    )
    assert sorted(tmp_path.rglob("*")) == before
