from __future__ import annotations

import hashlib
import json
import sqlite3
from pathlib import Path

import pytest
from PIL import Image
from test_title_artist_evaluation import alpha_extractor, fixture_paths, write_artifact
from test_unresolved_candidate_evaluation import _ingest_artifact

from tools.vision_poc import collection_auto_confirmation as auto_confirmation
from tools.vision_poc import title_artist_evaluation as evaluation
from tools.vision_poc import unresolved_candidate_evaluation


def _write_checkpoint(
    artifact_root: Path,
    manifests: list[dict[str, object]],
    reference_ids: list[str],
) -> None:
    first = manifests[0]
    session_dir = artifact_root / str(first["session_id"])
    observations = []
    for manifest, reference_id in zip(manifests, reference_ids, strict=True):
        artifact_path = session_dir / "observations" / str(manifest["observation_id"])
        observations.append(
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
        )
    checkpoint = {
        "checkpoint_version": "m5c-observation-checkpoint-v2",
        "session": {
            field: first[field]
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
        | {"window": first["window"], "started_at_utc": first["captured_at_utc"]},
        "last_stable_feature_hash": first["feature_hash"],
        "stable_feature_hashes": [manifest["feature_hash"] for manifest in manifests],
        "processed_frame_count": len(manifests),
        "dropped_frame_count": 0,
        "observations": observations,
        "updated_at_utc": first["created_at_utc"],
    }
    (session_dir / "checkpoint.json").write_text(
        json.dumps(checkpoint, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def _setup_collection(tmp_path: Path, monkeypatch, count: int = 1):
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    snapshot_path = tmp_path / "data" / "ddrworld_music_snapshot" / "fixture-v1"
    _write_snapshot(snapshot_path, [("Unmapped", "Unknown Artist", (200, 200, 200))])
    manifests = []
    reference_ids = []
    for index in range(1, count + 1):
        relative = write_artifact(
            artifact_root,
            catalog_path,
            index=index,
            source_color=(10 + index, 20, 30),
            composite=True,
        )
        manifest, reference_id = _ingest_artifact(
            master_path, catalog_path, artifact_root, relative
        )
        manifests.append(manifest)
        reference_ids.append(reference_id)
    _write_checkpoint(artifact_root, manifests, reference_ids)
    return master_path, catalog_path, artifact_root, manifests, snapshot_path


def _write_snapshot(
    snapshot_path: Path,
    entries: list[tuple[str, str, tuple[int, int, int]]],
) -> None:
    jacket_directory = snapshot_path / "jackets"
    jacket_directory.mkdir(parents=True, exist_ok=True)
    images = []
    songs = []
    for index, (title, artist, color) in enumerate(entries):
        source_url = f"https://fixture.test/jacket/{index}"
        image_path = jacket_directory / f"{index}.png"
        Image.new("RGB", (32, 32), color).save(image_path)
        image_hash = hashlib.sha256(image_path.read_bytes()).hexdigest()
        relative_path = image_path.relative_to(snapshot_path).as_posix()
        images.append(
            {
                "source_url": source_url,
                "local_path": relative_path,
                "sha256": image_hash,
                "error": None,
            }
        )
        songs.append(
            {
                "source_page": 0,
                "page_position": index,
                "title": title,
                "artist": artist,
                "jacket_source_url": source_url,
                "jacket_local_path": relative_path,
                "jacket_sha256": image_hash,
                "jacket_error": None,
            }
        )
    (snapshot_path / "manifest.json").write_text(
        json.dumps(
            {
                "schema_version": "ddrworld-music-snapshot-manifest-v1",
                "status": "complete",
                "snapshot_id": "fixture-v1",
                "failures": [],
                "images": images,
            },
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
        newline="\n",
    )
    (snapshot_path / "summary.json").write_text(
        json.dumps(
            {
                "schema_version": "ddrworld-music-snapshot-summary-v1",
                "status": "complete",
                "snapshot_id": "fixture-v1",
                "image_request_count": len(images),
                "stored_jacket_count": len(images),
                "song_count": len(songs),
            },
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
        newline="\n",
    )
    (snapshot_path / "songs.jsonl").write_text(
        "".join(json.dumps(song, ensure_ascii=False) + "\n" for song in songs),
        encoding="utf-8",
        newline="\n",
    )


def test_collection_end_prefers_unique_jacket_gate_over_ocr_pair(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root, manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch
    )
    _write_snapshot(
        snapshot_path,
        [("Alpha", "Artist A", (11, 20, 30))],
    )

    def low_confidence_extractor(image, field: str, method: str):
        return evaluation.FieldExtraction(
            "", "", 0.1, "low_confidence", "below threshold"
        )

    result = auto_confirmation.apply_collection_auto_confirmation(
        catalog_path,
        master_path,
        artifact_root,
        "session-1",
        snapshot_path=snapshot_path,
        extractor=low_confidence_extractor,
        applied_at="2026-07-23T00:00:00+00:00",
    )

    assert (result.requested_count, result.applied_count) == (1, 1)
    observation_id = str(manifests[0]["observation_id"])
    with sqlite3.connect(catalog_path) as connection:
        row = connection.execute(
            "SELECT review_status, resolution_basis, resolution_reason "
            "FROM jacket_references WHERE source_capture_id = ?",
            (observation_id,),
        ).fetchone()
    assert row[0:2] == ("auto_confirmed", "jacket_gate")
    evidence = json.loads(row[2])
    assert evidence["confirmation_source"] == "jacket_gate"
    assert evidence["matched_song_id"] == "song-alpha"
    assert 0.0 <= evidence["jacket_distance"] < 0.01
    assert evidence["jacket_rank"] == 1
    assert evidence["snapshot_id"] == "fixture-v1"
    assert evidence["jacket_feature_source"].startswith("ddrworld:jackets/")


def test_collection_end_keeps_ambiguous_jacket_for_existing_ocr_policy(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root, manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch
    )
    _write_snapshot(
        snapshot_path,
        [
            ("Alpha", "Artist A", (11, 20, 30)),
            ("Beta", "Artist B", (11, 20, 30)),
        ],
    )

    auto_confirmation.apply_collection_auto_confirmation(
        catalog_path,
        master_path,
        artifact_root,
        "session-1",
        snapshot_path=snapshot_path,
        extractor=alpha_extractor,
    )

    observation_id = str(manifests[0]["observation_id"])
    with sqlite3.connect(catalog_path) as connection:
        row = connection.execute(
            "SELECT resolution_basis, resolution_reason "
            "FROM jacket_references WHERE source_capture_id = ?",
            (observation_id,),
        ).fetchone()
    assert row[0] == "ocr_title_artist_pair"
    evidence = json.loads(row[1])
    assert evidence["confirmation_source"] == "ocr_title_artist_pair"
    assert evidence["jacket_distance"] is None


def test_collection_end_auto_confirms_all_safe_rows_and_reports_zero_remaining(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root, manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch, count=2
    )

    result = auto_confirmation.apply_collection_auto_confirmation(
        catalog_path,
        master_path,
        artifact_root,
        "session-1",
        snapshot_path=snapshot_path,
        extractor=alpha_extractor,
        applied_at="2026-07-23T00:00:00+00:00",
    )

    assert result.as_dict() == {
        "collection_auto_confirmation_schema_version": "m5c-collection-end-auto-confirmation-v1",
        "session_id": "session-1",
        "requested_count": 2,
        "applied_count": 2,
        "no_op_count": 0,
        "auto_confirmed_count": 2,
        "remaining_count": 0,
    }
    with sqlite3.connect(catalog_path) as connection:
        assert connection.execute(
            "SELECT COUNT(*) FROM jacket_references WHERE review_status = 'auto_confirmed'"
        ).fetchone()[0] == len(manifests)
        assert connection.execute(
            "SELECT COUNT(*) FROM reference_review_history"
        ).fetchone()[0] == 0


def test_collection_end_leaves_non_auto_rows_for_manual_review(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root, _manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch, count=2
    )

    def mixed_extractor(image, field: str, method: str):
        if image.getpixel((0, 0)) == (11, 20, 30):
            return alpha_extractor(image, field, method)
        return evaluation.FieldExtraction("", "", 0.1, "low_confidence", "below threshold")

    result = auto_confirmation.apply_collection_auto_confirmation(
        catalog_path,
        master_path,
        artifact_root,
        "session-1",
        snapshot_path=snapshot_path,
        extractor=mixed_extractor,
        applied_at="2026-07-23T00:00:00+00:00",
    )

    assert (result.requested_count, result.applied_count) == (1, 1)
    assert (result.auto_confirmed_count, result.remaining_count) == (1, 1)
    with sqlite3.connect(catalog_path) as connection:
        rows = connection.execute(
            "SELECT source_capture_id, review_status FROM jacket_references "
            "ORDER BY source_capture_id"
        ).fetchall()
    assert sorted(status for _observation_id, status in rows) == [
        "auto_confirmed",
        "unresolved",
    ]


def test_collection_end_does_not_auto_confirm_a_non_gp_master_song(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root, _manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch
    )
    with sqlite3.connect(master_path) as connection, connection:
        connection.execute(
            "UPDATE songs SET grand_prix_play_available = 0 WHERE song_id = ?",
            ("song-alpha",),
        )

    result = auto_confirmation.apply_collection_auto_confirmation(
        catalog_path,
        master_path,
        artifact_root,
        "session-1",
        snapshot_path=snapshot_path,
        extractor=alpha_extractor,
    )

    assert (result.requested_count, result.auto_confirmed_count, result.remaining_count) == (
        0,
        0,
        1,
    )


def test_collection_end_repeat_is_a_no_op_without_history_or_artifact_changes(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root, _manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch
    )
    first_artifacts = sorted(
        path.read_bytes()
        for path in artifact_root.rglob("*")
        if path.is_file()
    )

    first = auto_confirmation.apply_collection_auto_confirmation(
        catalog_path,
        master_path,
        artifact_root,
        "session-1",
        snapshot_path=snapshot_path,
        extractor=alpha_extractor,
        applied_at="2026-07-23T00:00:00+00:00",
    )
    second = auto_confirmation.apply_collection_auto_confirmation(
        catalog_path,
        master_path,
        artifact_root,
        "session-1",
        snapshot_path=snapshot_path,
        extractor=alpha_extractor,
        applied_at="2026-07-23T00:00:01+00:00",
    )

    assert (first.applied_count, first.no_op_count) == (1, 0)
    assert (second.applied_count, second.no_op_count) == (0, 0)
    assert (second.auto_confirmed_count, second.remaining_count) == (1, 0)
    assert first_artifacts == sorted(
        path.read_bytes()
        for path in artifact_root.rglob("*")
        if path.is_file()
    )
    with sqlite3.connect(catalog_path) as connection:
        assert connection.execute(
            "SELECT COUNT(*) FROM reference_review_history"
        ).fetchone()[0] == 0


def test_collection_end_rejects_pending_checkpoint_before_matching(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root, _manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch
    )
    checkpoint_path = artifact_root / "session-1" / "checkpoint.json"
    checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))
    checkpoint["observations"][0]["catalog_status"] = "pending"
    checkpoint_path.write_text(
        json.dumps(checkpoint, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    before = hashlib.sha256(catalog_path.read_bytes()).hexdigest()

    try:
        auto_confirmation.apply_collection_auto_confirmation(
            catalog_path,
            master_path,
            artifact_root,
            "session-1",
            snapshot_path=snapshot_path,
            extractor=alpha_extractor,
        )
    except ValueError as exception:
        assert "pending" in str(exception)
    else:
        raise AssertionError("pending checkpoint must prevent auto-confirmation")
    assert hashlib.sha256(catalog_path.read_bytes()).hexdigest() == before


@pytest.mark.parametrize(
    ("classification", "reason", "candidates"),
    [
        ("ambiguous", "title_match_artist_mismatch", [{"song_id": "song-alpha"}]),
        ("no_candidate", "identity_not_found", []),
        ("low_confidence", "below_threshold", []),
        ("evaluation_failed", "ocr_failed", []),
        ("evaluation_unavailable", "artifact_missing_corrupt_or_drifted", []),
    ],
)
def test_collection_end_keeps_non_auto_evaluation_classes_manual(
    tmp_path: Path,
    monkeypatch,
    classification: str,
    reason: str,
    candidates: list[dict[str, str]],
) -> None:
    master_path, catalog_path, artifact_root, manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch
    )
    observation_id = str(manifests[0]["observation_id"])
    candidate_evaluation = unresolved_candidate_evaluation._result(
        classification,
        reason,
        observation_id=observation_id,
        candidates=candidates,
    )
    monkeypatch.setattr(
        auto_confirmation.projection,
        "build_review_projection",
        lambda *_args, **_kwargs: {
            "review_references": [
                {
                    "reference_id": "reference-1",
                    "stored_status": "unresolved",
                    "review_status": "unresolved",
                    "candidate_evaluation": candidate_evaluation,
                }
            ]
        },
    )

    result = auto_confirmation.apply_collection_auto_confirmation(
        catalog_path,
        master_path,
        artifact_root,
        "session-1",
        snapshot_path=snapshot_path,
    )

    assert result.requested_count == 0
    assert (result.auto_confirmed_count, result.remaining_count) == (0, 1)


def test_collection_end_uses_the_writer_transaction_for_all_targets(
    tmp_path: Path, monkeypatch
) -> None:
    master_path, catalog_path, artifact_root, _manifests, snapshot_path = _setup_collection(
        tmp_path, monkeypatch, count=2
    )
    original_apply = auto_confirmation.catalog.apply_auto_confirmation_batch

    def fail_after_first(*args, **kwargs):
        kwargs["fail_after_updates"] = 1
        return original_apply(*args, **kwargs)

    monkeypatch.setattr(
        auto_confirmation.catalog,
        "apply_auto_confirmation_batch",
        fail_after_first,
    )
    before = hashlib.sha256(catalog_path.read_bytes()).hexdigest()

    with pytest.raises(RuntimeError, match="injected auto confirmation batch failure"):
        auto_confirmation.apply_collection_auto_confirmation(
            catalog_path,
            master_path,
            artifact_root,
            "session-1",
            snapshot_path=snapshot_path,
            extractor=alpha_extractor,
        )

    assert hashlib.sha256(catalog_path.read_bytes()).hexdigest() == before
