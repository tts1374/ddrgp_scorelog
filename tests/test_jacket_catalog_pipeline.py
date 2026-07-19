from __future__ import annotations

import hashlib
import json
from pathlib import Path

import pytest

from tools.ddrworld_snapshot_evaluation import catalog_pipeline as pipeline
from tools.ddrworld_snapshot_evaluation.evaluator import load_ods_sheets
from tools.ddrworld_snapshot_evaluation.policy import PolicyEvaluationResult


def _manual_item() -> dict[str, object]:
    return {
        "observation_id": "obs-manual",
        "audit_no": 7,
        "current_review_status": "unresolved",
        "current_review_revision": 0,
        "capture_validity": "valid",
        "image_reference": "data/captures/obs-manual/source.png",
        "rank": None,
        "distance": 0.25,
        "margin": 0.01,
        "title_ocr_raw_normalized_candidate_json": "[]",
        "artist_ocr_raw_normalized_candidate_json": "[]",
        "hold_reason": "title_ocr_unresolved",
        "recommended_song_id": "",
        "truth_song_id": "",
        "notes": "",
        "jacket_top1_song_id": "song-1",
        "jacket_top1_title": "Alpha",
        "jacket_top1_artist": "Artist A",
        "jacket_top1_rank": 1,
        "jacket_top1_distance": 0.25,
        "jacket_top2_song_id": "song-2",
        "jacket_top2_title": "Beta",
        "jacket_top2_artist": "Artist B",
        "jacket_top2_rank": 2,
        "jacket_top2_distance": 0.26,
        "jacket_top3_song_id": "",
        "jacket_top3_title": "",
        "jacket_top3_artist": "",
        "jacket_top3_rank": None,
        "jacket_top3_distance": None,
    }


def _plan() -> dict[str, object]:
    value: dict[str, object] = {
        "schema_version": pipeline.PLAN_SCHEMA_VERSION,
        "policy_version": pipeline.POLICY_VERSION,
        "inputs": {
            "snapshot": "C:/data/snapshot",
            "truth_ods": "C:/data/observations.ods",
            "catalog": "C:/data/catalog.sqlite",
            "master": "C:/data/master.sqlite",
            "input_sha256": {
                "C:/data/observations.ods": "4" * 64,
                "C:/data/catalog.sqlite": "5" * 64,
                "C:/data/master.sqlite": "6" * 64,
            },
            "manifest_sha256": "1" * 64,
            "summary_sha256": "2" * 64,
            "songs_sha256": "3" * 64,
        },
        "source_revisions": {
            "catalog_guard_revision": "a" * 64,
            "catalog_file_sha256": "b" * 64,
            "master_version": "master-v1",
            "policy_rows_sha256": "c" * 64,
        },
        "counts": {
            "observations": 2,
            "auto": 0,
            "apply": 0,
            "no_op": 0,
            "manual": 1,
            "rejected": 1,
            "false_decisions": 0,
        },
        "route_counts": {
            "manual_other": 1,
            "rejected_capture_mismatch": 1,
        },
        "auto_confirmations": [],
        "manual_reviews": [_manual_item()],
        "rejected_observation_ids": ["obs-rejected"],
        "manual_export_id": "d" * 64,
    }
    value["plan_id"] = pipeline._sha256_json(value)
    return value


def test_manual_ods_export_is_stable_editable_and_excludes_rejected(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    first = tmp_path / "data/manual-review.ods"
    second = tmp_path / "data/manual-review-repeat.ods"
    plan = _plan()

    pipeline.export_manual_ods(first, plan)
    pipeline.export_manual_ods(second, plan)

    assert first.read_bytes() == second.read_bytes()
    sheets = load_ods_sheets(first)
    assert set(sheets) == {"Metadata", "Manual Review"}
    assert sheets["Manual Review"][0] == pipeline.MANUAL_ODS_HEADERS
    rows = pipeline.rows_as_dicts(sheets["Manual Review"], sheet_name="Manual Review")
    assert len(rows) == 1
    assert rows[0]["observation_id"] == "obs-manual"
    assert rows[0]["truth_song_id"] is None
    assert "obs-rejected" not in first.read_text(encoding="latin-1", errors="ignore")
    with pytest.raises(ValueError, match="already exists"):
        pipeline.export_manual_ods(first, plan)


def test_plan_loader_rejects_tampering(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    path = tmp_path / "data/plan.json"
    plan = _plan()
    pipeline.write_plan(path, plan)
    assert pipeline.load_plan(path)["plan_id"] == plan["plan_id"]

    tampered = json.loads(path.read_text(encoding="utf-8"))
    tampered["counts"]["auto"] = 2
    path.write_text(json.dumps(tampered), encoding="utf-8")
    with pytest.raises(ValueError, match="identity mismatch"):
        pipeline.load_plan(path)


def test_plan_loader_rejects_rehashed_inconsistent_counts(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    path = tmp_path / "data/plan.json"
    plan = _plan()
    plan["counts"]["manual"] = 2
    plan["plan_id"] = pipeline._sha256_json(
        {key: value for key, value in plan.items() if key != "plan_id"}
    )
    pipeline.write_plan(path, plan)

    with pytest.raises(ValueError, match="counts or routes are inconsistent"):
        pipeline.load_plan(path)


def test_plan_loader_rejects_non_object_manual_row(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    path = tmp_path / "data/plan.json"
    plan = _plan()
    plan["manual_reviews"] = ["invalid"]
    plan["plan_id"] = pipeline._sha256_json(
        {key: value for key, value in plan.items() if key != "plan_id"}
    )
    pipeline.write_plan(path, plan)

    with pytest.raises(ValueError, match="list of objects"):
        pipeline.load_plan(path)


def test_plan_publish_race_preserves_competing_output(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    path = tmp_path / "data/plan.json"
    original_link = pipeline.os.link

    def competing_link(source: Path, target: Path) -> None:
        target.write_bytes(b"competing plan")
        original_link(source, target)

    monkeypatch.setattr(pipeline.os, "link", competing_link)
    with pytest.raises(ValueError, match="already exists"):
        pipeline.write_plan(path, _plan())
    assert path.read_bytes() == b"competing plan"


def test_ods_bytes_have_stable_sha256(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    path = tmp_path / "data/manual-review.ods"
    pipeline.export_manual_ods(path, _plan())
    first = hashlib.sha256(path.read_bytes()).hexdigest()
    assert len(first) == 64


def test_ods_publish_race_preserves_competing_output(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    path = tmp_path / "data/manual-review.ods"
    original_link = pipeline.os.link

    def competing_link(source: Path, target: Path) -> None:
        target.write_bytes(b"competing ODS")
        original_link(source, target)

    monkeypatch.setattr(pipeline.os, "link", competing_link)
    with pytest.raises(ValueError, match="already exists"):
        pipeline.export_manual_ods(path, _plan())
    assert path.read_bytes() == b"competing ODS"


def test_commit_guard_rejects_input_changed_after_dry_run(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    observations = tmp_path / "observations.ods"
    master = tmp_path / "master.sqlite"
    observations.write_bytes(b"observations-v1")
    master.write_bytes(b"master-v1")
    snapshot = tmp_path / "snapshot"
    snapshot.mkdir()
    fingerprints = {
        "manifest_sha256": "1" * 64,
        "summary_sha256": "2" * 64,
        "songs_sha256": "3" * 64,
    }
    config = pipeline.CatalogPipelineConfig(
        snapshot=snapshot,
        observations_ods=observations,
        catalog=tmp_path / "catalog.sqlite",
        master=master,
    )
    plan = {
        "inputs": {
            "input_sha256": {
                str(observations.resolve()): hashlib.sha256(
                    observations.read_bytes()
                ).hexdigest(),
                str(master.resolve()): hashlib.sha256(master.read_bytes()).hexdigest(),
            },
            **fingerprints,
        }
    }
    monkeypatch.setattr(pipeline, "load_snapshot", lambda _path: ([], fingerprints))
    monkeypatch.setattr(pipeline, "load_snapshot_features", lambda _path, _rows: [])
    monkeypatch.setattr(pipeline, "verify_snapshot_images_unchanged", lambda *_args: None)

    pipeline._assert_external_inputs_unchanged(config, plan)
    observations.write_bytes(b"observations-v2")
    with pytest.raises(ValueError, match="changed after dry-run"):
        pipeline._assert_external_inputs_unchanged(config, plan)


def test_production_plan_rejects_any_false_policy_decision(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    result = PolicyEvaluationResult(
        rows=[],
        metrics={"false_decisions": 1},
        jacket_metrics={},
        input_revisions={},
        master_by_id={},
    )
    monkeypatch.setattr(pipeline, "evaluate_policy_inputs", lambda _config: result)
    config = pipeline.CatalogPipelineConfig(
        snapshot=tmp_path / "snapshot",
        observations_ods=tmp_path / "observations.ods",
        catalog=tmp_path / "catalog.sqlite",
        master=tmp_path / "master.sqlite",
    )

    with pytest.raises(pipeline.EvaluationError, match="false decisions"):
        pipeline.build_plan(config)
    with pytest.raises(ValueError, match="false decisions"):
        pipeline._assert_plan_semantics(config, {}, result)
