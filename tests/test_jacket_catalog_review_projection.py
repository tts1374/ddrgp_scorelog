from __future__ import annotations

import hashlib
import io
import sqlite3
import zipfile
from pathlib import Path

import pytest
from PIL import Image, ImageDraw
from test_jacket_reference_catalog import created_at, identity, write_master

from tools.ddrworld_snapshot_evaluation.evaluator import load_ods_sheets, rows_as_dicts
from tools.vision_poc import jacket_catalog_review_projection as projection
from tools.vision_poc import jacket_reference_catalog as catalog


def setup_projection(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> tuple[Path, Path, str]:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    (tmp_path / "databases").mkdir()
    master_db = tmp_path / "master.sqlite"
    write_master(master_db)
    catalog_db = tmp_path / "databases/catalog.sqlite"
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

    assert result["projection_schema_version"] == 6
    assert result["review_references"][0]["candidate_evaluation"]["classification"] == (
        "not_eligible"
    )
    assert result["catalog"]["schema_version"] == 1
    assert "migration_required" not in result["catalog"]
    assert "mutation_capability" not in result["catalog"]
    reviewed = result["review_references"][0]
    assert reviewed["stored_status"] == "manual_confirmed"
    assert reviewed["current_status"] == "manual_confirmed"
    assert reviewed["current_song_id"] == "song-1"
    assert reviewed["notes"] == "opaque / 日本語"
    assert reviewed["registered_route"] == "manual_review"
    assert reviewed["processed_at"] == reviewed["history"][0]["action_at"]
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


def _export_projection(master_db: Path, sources: list[Path]) -> dict[str, object]:
    return {
        "master": {
            "path": str(master_db.resolve()),
            "master_version": "master-v1",
        },
        "catalog": {"schema_version": 1},
        "review_references": [
            {
                "reference_id": f"reference-{index}",
                "stored_status": "unresolved",
                "source_image_path": str(source.resolve()),
                "notes": f"note-{index}",
                "candidate_evaluation": {"observation_id": f"observation-{index}"},
            }
            for index, source in enumerate(sources, start=1)
        ],
    }


def _write_export_source(path: Path) -> None:
    image = Image.new("RGB", (1280, 720), "black")
    draw = ImageDraw.Draw(image)
    draw.rectangle((306, 58, 775, 91), fill=(220, 20, 60))
    draw.rectangle((309, 97, 775, 119), fill=(30, 144, 255))
    image.save(path)


def test_manual_review_ods_exports_empty_target_and_all_master_songs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master_db = tmp_path / "master.sqlite"
    write_master(master_db)
    path = tmp_path / "data/manual-review-empty.ods"

    metadata = projection.export_manual_review_ods(
        path,
        _export_projection(master_db, []),
        export_id="export-empty",
        exported_at="2026-07-24T00:00:00+00:00",
    )

    sheets = load_ods_sheets(path)
    assert set(sheets) == {"Manual Review", "Master Songs", "Metadata"}
    assert sheets["Manual Review"] == [projection.MANUAL_REVIEW_ODS_HEADERS]
    assert sheets["Master Songs"] == [
        ["song_id", "title", "artist"],
        ["song-1", "Alpha", "Artist A"],
        ["song-2", "Beta", "Artist B"],
    ]
    metadata_rows = {
        row["key"]: row["value"]
        for row in rows_as_dicts(sheets["Metadata"], sheet_name="Metadata")
    }
    assert metadata_rows == {
        "schema_version": projection.MANUAL_REVIEW_ODS_SCHEMA_VERSION,
        "export_id": "export-empty",
        "catalog_version": "1",
        "master_version": "master-v1",
        "exported_at": "2026-07-24T00:00:00+00:00",
        "target_count": 0,
    }
    assert metadata["target_count"] == 0


@pytest.mark.parametrize("target_count", [1, 3])
def test_manual_review_ods_embeds_rois_and_protects_existing_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, target_count: int
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master_db = tmp_path / "master.sqlite"
    write_master(master_db)
    sources = []
    for index in range(target_count):
        source = tmp_path / f"source-{index}.png"
        _write_export_source(source)
        sources.append(source)
    path = tmp_path / f"data/manual-review-{target_count}.ods"

    projection.export_manual_review_ods(
        path,
        _export_projection(master_db, sources),
        export_id=f"export-{target_count}",
        exported_at="2026-07-24T00:00:00+00:00",
    )

    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        content = archive.read("content.xml").decode("utf-8")
        manifest = archive.read("META-INF/manifest.xml").decode("utf-8")
        for index in range(1, target_count + 1):
            for field in ("title", "artist"):
                image_name = f"Pictures/{index:04d}-{field}.png"
                assert image_name in names
                assert f'xlink:href="{image_name}"' in content
                assert f'manifest:full-path="{image_name}"' in manifest
                with Image.open(io.BytesIO(archive.read(image_name))) as image:
                    assert image.size == ((470, 34) if field == "title" else (467, 23))

    sheets = load_ods_sheets(path)
    rows = rows_as_dicts(sheets["Manual Review"], sheet_name="Manual Review")
    assert len(rows) == target_count
    assert [row["status"] for row in rows] == ["unreviewed"] * target_count
    assert [row["truth_song_id"] for row in rows] == [None] * target_count
    assert [row["notes"] for row in rows] == [
        f"note-{index}" for index in range(1, target_count + 1)
    ]
    assert content.count('table:style-name="input"') == target_count * 3

    original = path.read_bytes()
    with pytest.raises(ValueError, match="already exists"):
        projection.export_manual_review_ods(path, _export_projection(master_db, sources))
    assert path.read_bytes() == original
