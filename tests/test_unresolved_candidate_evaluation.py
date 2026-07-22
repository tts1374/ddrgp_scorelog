from __future__ import annotations

import json
import sqlite3
from pathlib import Path

from test_title_artist_evaluation import alpha_extractor, fixture_paths, write_artifact

from tools.vision_poc import jacket_catalog_review_projection as projection
from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import unresolved_candidate_evaluation as candidate_evaluation


def _ingest_artifact(
    master_path: Path,
    catalog_path: Path,
    artifact_root: Path,
    relative_manifest: str,
) -> tuple[dict[str, object], str]:
    manifest_path = artifact_root / relative_manifest
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    receipt = catalog.ingest_observation(
        catalog_path,
        master_path,
        source_image_path=manifest_path.parent / "jacket-crop.png",
        observation_id=manifest["observation_id"],
        expected_image_hash=manifest["jacket_crop_hash"],
        expected_master_version=manifest["master_version"],
        expected_master_source_hash=manifest["master_source_hash"],
        expected_feature_extractor_version=manifest["feature_extractor_version"],
        expected_catalog_identity=manifest["catalog_identity"],
        expected_catalog_schema_version=manifest["catalog_schema_version"],
        expected_catalog_created_at=manifest["catalog_created_at"],
        image_kind="jacket_crop",
        jacket_feature_version=manifest["feature_version"],
        jacket_feature_hash=manifest["feature_hash"],
        title_line_feature_version=manifest["title_line_feature_version"],
        title_line_hash=manifest["title_line_hash"],
        composite_identity_version=manifest["composite_identity_version"],
        expected_composite_identity_hash=manifest["composite_identity_hash"],
    )
    return manifest, receipt.reference_id


def _write_checkpoint(
    artifact_root: Path,
    manifest: dict[str, object],
    reference_id: str,
) -> None:
    session_dir = artifact_root / str(manifest["session_id"])
    artifact_path = session_dir / "observations" / str(manifest["observation_id"])
    checkpoint = {
        "checkpoint_version": "m5c-observation-checkpoint-v2",
        "session": {
            field: manifest[field]
            for field in (
                "session_id",
                "master_version",
                "master_source_hash",
                "catalog_identity",
                "catalog_schema_version",
                "catalog_created_at",
                "feature_extractor_version",
                "detector_version",
                "roi_version",
                "frame_clock_version",
            )
        }
        | {
            "window": manifest["window"],
            "started_at_utc": manifest["captured_at_utc"],
        },
        "last_stable_feature_hash": manifest["feature_hash"],
        "stable_feature_hashes": [manifest["feature_hash"]],
        "processed_frame_count": 1,
        "dropped_frame_count": 0,
        "observations": [
            {
                "observation_id": manifest["observation_id"],
                "source_image_hash": manifest["source_image_hash"],
                "jacket_crop_hash": manifest["jacket_crop_hash"],
                "jacket_feature_version": manifest["feature_version"],
                "jacket_feature_hash": manifest["feature_hash"],
                "title_line_feature_version": manifest["title_line_feature_version"],
                "title_line_hash": manifest["title_line_hash"],
                "composite_identity_version": manifest["composite_identity_version"],
                "composite_identity_hash": manifest["composite_identity_hash"],
                "catalog_status": "ingested",
                "catalog_reference_id": reference_id,
                "artifact_path": str(artifact_path.resolve()),
                "adopted_at_utc": manifest["created_at_utc"],
            }
        ],
        "updated_at_utc": manifest["created_at_utc"],
    }
    (session_dir / "checkpoint.json").write_text(
        json.dumps(checkpoint, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def test_projection_evaluates_unresolved_artifact_without_writing_databases(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    relative = write_artifact(artifact_root, catalog_path, index=1, composite=True)
    manifest, reference_id = _ingest_artifact(master_path, catalog_path, artifact_root, relative)
    _write_checkpoint(artifact_root, manifest, reference_id)
    master_before = master_path.read_bytes()
    catalog_before = catalog_path.read_bytes()

    result = projection.build_review_projection(
        catalog_path,
        master_path,
        artifact_root=artifact_root,
        extractor=alpha_extractor,
    )

    evaluated = result["review_references"][0]["candidate_evaluation"]
    assert evaluated["classification"] == "exact_unique"
    assert evaluated["candidates"][0]["song_id"] == "song-alpha"
    assert evaluated["title"]["confidence"] == 0.99
    assert master_path.read_bytes() == master_before
    assert catalog_path.read_bytes() == catalog_before


def test_needs_review_projection_keeps_validated_source_image_path(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    relative = write_artifact(artifact_root, catalog_path, index=1, composite=True)
    manifest, reference_id = _ingest_artifact(master_path, catalog_path, artifact_root, relative)
    _write_checkpoint(artifact_root, manifest, reference_id)
    with sqlite3.connect(catalog_path) as connection:
        connection.execute(
            "UPDATE jacket_references SET review_status = 'needs_review' WHERE reference_id = ?",
            (reference_id,),
        )
    master_before = master_path.read_bytes()
    catalog_before = catalog_path.read_bytes()

    result = projection.build_review_projection(
        catalog_path,
        master_path,
        artifact_root=artifact_root,
        extractor=alpha_extractor,
    )

    reviewed = result["review_references"][0]
    assert reviewed["candidate_evaluation"]["classification"] == "not_eligible"
    assert reviewed["source_image_path"] == str(
        (artifact_root / relative).parent.joinpath("source.png").resolve()
    )
    assert master_path.read_bytes() == master_before
    assert catalog_path.read_bytes() == catalog_before


def test_checkpoint_drift_is_classified_without_candidate_or_database_write(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    relative = write_artifact(artifact_root, catalog_path, index=1, composite=True)
    manifest, reference_id = _ingest_artifact(master_path, catalog_path, artifact_root, relative)
    _write_checkpoint(artifact_root, manifest, reference_id)
    checkpoint_path = artifact_root / "session-1" / "checkpoint.json"
    checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))
    checkpoint["observations"][0]["catalog_reference_id"] = "drifted"
    checkpoint_path.write_text(json.dumps(checkpoint), encoding="utf-8", newline="\n")
    catalog_before = catalog_path.read_bytes()

    result = projection.build_review_projection(
        catalog_path,
        master_path,
        artifact_root=artifact_root,
        extractor=alpha_extractor,
    )

    evaluated = result["review_references"][0]["candidate_evaluation"]
    assert evaluated["classification"] == "evaluation_unavailable"
    assert evaluated["reason"] == "checkpoint_invalid_or_drifted"
    assert evaluated["candidates"] == []
    assert catalog_path.read_bytes() == catalog_before


def test_report_summary_keeps_candidate_counts_without_treating_them_as_truth() -> None:
    rows = [
        {
            "reference_id": "ref-1",
            "stored_status": "unresolved",
            "revision": 0,
            "candidate_evaluation": candidate_evaluation._result(
                "no_candidate", "identity_not_found", observation_id="observation-1"
            ),
        }
    ]

    summary = candidate_evaluation.summarize(rows)

    assert summary["total_observations"] == 1
    assert summary["no_candidates"] == 1
    assert summary["field_status_counts"]["title"] == {"not_evaluated": 1}
    assert "precision" not in summary


def test_reports_are_byte_stable_and_stay_under_requested_output(tmp_path: Path) -> None:
    output = tmp_path / "report"
    rows = [
        {
            "reference_id": "ref-1",
            "stored_status": "unresolved",
            "revision": 0,
            "candidate_evaluation": candidate_evaluation._result(
                "no_candidate", "identity_not_found", observation_id="observation-1"
            ),
        }
    ]

    candidate_evaluation.write_reports(output, rows)
    first = {path.name: path.read_bytes() for path in output.iterdir()}
    candidate_evaluation.write_reports(output, rows)
    second = {path.name: path.read_bytes() for path in output.iterdir()}

    assert first == second
    assert set(first) == {
        "unresolved_candidates.csv",
        "unresolved_candidates.json",
        "unresolved_candidates.md",
    }
