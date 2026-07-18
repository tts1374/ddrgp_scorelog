from __future__ import annotations

import hashlib
import sqlite3
from pathlib import Path

import pytest
from PIL import Image
from test_jacket_reference_catalog import created_at, identity, write_master

from tools.vision_poc import jacket_catalog_review_projection as projection
from tools.vision_poc import jacket_reference_catalog as catalog


def setup_projection(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> tuple[Path, Path, str]:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master_db = tmp_path / "master.sqlite"
    write_master(master_db)
    catalog_db = tmp_path / "data/catalog.sqlite"
    catalog.create_catalog(catalog_db)
    image = tmp_path / "data/jacket.png"
    Image.new("RGB", (64, 64), (20, 30, 40)).save(image)
    result = catalog.ingest_observation(
        catalog_db,
        master_db,
        source_image_path=image,
        observation_id="projection-observation",
        expected_image_hash=hashlib.sha256(image.read_bytes()).hexdigest(),
        expected_master_version="master-v1",
        expected_master_source_hash="fixture-source-hash",
        expected_feature_extractor_version=catalog.FEATURE_EXTRACTOR_VERSION,
        expected_catalog_identity=catalog.CATALOG_IDENTITY,
        expected_catalog_schema_version=1,
        expected_catalog_created_at=created_at(catalog_db),
        **identity("projection"),
    )
    return master_db, catalog_db, result.reference_id


def test_current_projection_exposes_manual_review_without_legacy_capabilities(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_db, reference_id = setup_projection(tmp_path, monkeypatch)
    catalog.apply_review_mutation(
        catalog_db,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="projection-confirm",
            reference_id=reference_id,
            action="manual_confirm",
            expected_revision=0,
            expected_status="unresolved",
            expected_song_id=None,
            song_id="song-1",
            note="opaque / 日本語",
        ),
    )
    master_before = master_db.read_bytes()
    catalog_before = catalog_db.read_bytes()

    result = projection.build_review_projection(catalog_db, master_db)

    assert result["projection_schema_version"] == 4
    assert result["review_references"][0]["candidate_evaluation"]["classification"] == (
        "not_eligible"
    )
    assert result["catalog"]["schema_version"] == 1
    assert "migration_required" not in result["catalog"]
    assert "mutation_capability" not in result["catalog"]
    reviewed = result["review_references"][0]
    assert reviewed["stored_status"] == "manual_confirmed"
    assert reviewed["revision"] == 1
    assert reviewed["history"][0]["action"] == "manual_confirm"
    assert master_db.read_bytes() == master_before
    assert catalog_db.read_bytes() == catalog_before


def test_projection_rejects_unsupported_catalog_without_writing(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_db, _reference_id = setup_projection(tmp_path, monkeypatch)
    with sqlite3.connect(catalog_db) as connection:
        connection.execute("PRAGMA user_version = 3")
        connection.execute("UPDATE catalog_metadata SET value = '3' WHERE key = 'schema_version'")
    before = catalog_db.read_bytes()

    with pytest.raises(ValueError, match="unsupported"):
        projection.build_review_projection(catalog_db, master_db)

    assert catalog_db.read_bytes() == before


def test_projection_rejects_concurrent_catalog_change(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_db, _reference_id = setup_projection(tmp_path, monkeypatch)
    original = projection.catalog.build_coverage

    def mutate_during_projection(catalog_path: Path, master_path: Path):
        result = original(catalog_path, master_path)
        with sqlite3.connect(catalog_path) as connection:
            connection.execute(
                "UPDATE catalog_metadata SET value = ? WHERE key = 'created_at'",
                ("changed-during-projection",),
            )
        return result

    monkeypatch.setattr(projection.catalog, "build_coverage", mutate_during_projection)
    with pytest.raises(ValueError, match="changed while"):
        projection.build_review_projection(catalog_db, master_db)
