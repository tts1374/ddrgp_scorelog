from __future__ import annotations

import json
import sqlite3
from pathlib import Path

import pytest
from PIL import Image

from master import builder as master_builder
from tools.vision_poc import jacket_catalog_review_projection as projection
from tools.vision_poc import jacket_reference_catalog as catalog


def _write_master(path: Path, *, version: str = "master-v1") -> None:
    songs = [
        ("song-1", "Alpha", "Artist A", 1),
        ("song-2", "Beta", "Artist B", 1),
        ("song-3", "Gamma", "Artist C", 1),
    ]
    with sqlite3.connect(path) as connection:
        master_builder.create_schema(connection)
        connection.executemany(
            "INSERT INTO songs VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [
                (
                    song_id,
                    title,
                    artist,
                    "fixture",
                    "fixture",
                    "120",
                    "fixture",
                    "",
                    "",
                    0,
                    gp,
                    "title_artist",
                    "",
                    "2026-07-14T00:00:00+00:00",
                    "2026-07-14T00:00:00+00:00",
                )
                for song_id, title, artist, gp in songs
            ],
        )
        connection.executemany(
            "INSERT INTO charts VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [
                (f"chart-{song_id}", song_id, "SINGLE", "BASIC", 1, "1", 0, 0, 0, "")
                for song_id, _title, _artist, _gp in songs
            ],
        )
        source_url = "https://example.test/master"
        source_hash = "fixture-source-hash"
        connection.executemany(
            "INSERT INTO master_metadata VALUES (?, ?)",
            (
                ("master_version", version),
                ("source_url", source_url),
                ("generated_at", "2026-07-14T00:00:00+00:00"),
                ("generator_version", "fixture-v1"),
                ("source_hash", source_hash),
                ("song_count", str(len(songs))),
                ("chart_count", str(len(songs))),
            ),
        )
        connection.execute(
            "INSERT INTO source_snapshots VALUES (?, ?, ?, ?, ?, ?)",
            (
                "snapshot-1",
                source_url,
                "2026-07-14T00:00:00+00:00",
                source_hash,
                "fixture-v1",
                "<html></html>",
            ),
        )


def _setup(tmp_path: Path, monkeypatch) -> tuple[Path, Path]:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master_db = tmp_path / "master.sqlite"
    catalog_db = tmp_path / "data" / "catalog.sqlite"
    image = tmp_path / "data" / "jacket.png"
    _write_master(master_db)
    catalog.create_catalog(catalog_db)
    Image.new("RGB", (64, 64), (12, 34, 56)).save(image)
    catalog.ingest_observation(
        catalog_db,
        master_db,
        source_image_path=image,
        source_capture_id="capture-review",
        observed_title="Alpha",
        observed_artist="wrong artist",
        observation_status="ok",
        image_kind="jacket_crop",
    )
    return master_db, catalog_db


def test_projection_contains_strict_read_only_review_contract(tmp_path: Path, monkeypatch) -> None:
    master_db, catalog_db = _setup(tmp_path, monkeypatch)
    master_before = master_db.read_bytes()
    catalog_before = catalog_db.read_bytes()

    result = projection.build_review_projection(catalog_db, master_db)

    assert result["projection_schema_version"] == 2
    assert result["master"]["master_version"] == "master-v1"
    assert result["master"]["source_hash"] == "fixture-source-hash"
    assert result["catalog"]["catalog_identity"] == catalog.CATALOG_IDENTITY
    assert result["catalog"]["migration_required"] is True
    assert result["catalog"]["mutation_capability"] == "read_only"
    assert result["coverage"]["grand_prix_song_count"] == 3
    assert sum(result["coverage"]["status_counts"].values()) == 3
    assert {row["coverage_status"] for row in result["songs"]} <= {
        "referenced",
        "needs_review",
        "uncollected",
        "unresolved",
    }
    review = result["review_references"][0]
    assert review["review_status"] == "needs_review"
    assert review["reason"] == "title_match_artist_mismatch"
    assert review["observed_title"] == "Alpha"
    assert review["candidates"][0]["song_id"] == "song-1"
    assert review["revision"] == 0
    assert review["history"] == []
    assert master_db.read_bytes() == master_before
    assert catalog_db.read_bytes() == catalog_before


def test_projection_preserves_opaque_reason_and_reports_master_drift(
    tmp_path: Path, monkeypatch
) -> None:
    master_db, catalog_db = _setup(tmp_path, monkeypatch)
    with sqlite3.connect(catalog_db) as connection:
        connection.execute(
            "UPDATE jacket_references SET resolution_reason = ?",
            ("future opaque reason / 日本語",),
        )
        connection.execute(
            "UPDATE reference_candidates SET candidate_reason = ?",
            ("candidate opaque value",),
        )

    opaque = projection.build_review_projection(catalog_db, master_db)
    assert opaque["review_references"][0]["reason"] == "future opaque reason / 日本語"
    assert opaque["review_references"][0]["candidates"][0]["reason"] == "candidate opaque value"

    with sqlite3.connect(catalog_db) as connection:
        connection.execute(
            "UPDATE jacket_references SET song_id = ?, review_status = ?, "
            "canonical_title_snapshot = ?, canonical_artist_snapshot = ?",
            ("song-1", "auto_confirmed", "Alpha", "Artist A"),
        )
    with sqlite3.connect(master_db) as connection:
        connection.execute(
            "UPDATE master_metadata SET value = ? WHERE key = 'master_version'",
            ("master-v2",),
        )

    drifted = projection.build_review_projection(catalog_db, master_db)
    assert drifted["review_references"][0]["reason"] == "master_version_changed"
    assert drifted["review_references"][0]["master_drift"] is True


def test_projection_cli_writes_one_json_document(tmp_path: Path, monkeypatch, capsys) -> None:
    master_db, catalog_db = _setup(tmp_path, monkeypatch)

    assert projection.main(["--catalog", str(catalog_db), "--master-db", str(master_db)]) == 0
    output = capsys.readouterr().out
    assert output.count("\n") == 1
    assert json.loads(output)["projection_schema_version"] == 2


def test_projection_v2_exposes_revision_capability_and_append_only_history(
    tmp_path: Path, monkeypatch
) -> None:
    master_db, catalog_db = _setup(tmp_path, monkeypatch)
    target = tmp_path / "data" / "catalog-v2.sqlite"
    catalog.migrate_catalog_v1_to_v2(catalog_db, target)
    initial = projection.build_review_projection(target, master_db)
    reference = initial["review_references"][0]
    catalog.apply_review_mutation(
        target,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="projection-confirm",
            reference_id=reference["reference_id"],
            action="manual_confirm",
            expected_revision=0,
            expected_status="needs_review",
            expected_song_id=None,
            song_id="song-1",
            reason="explicit selection",
            note="opaque / 日本語",
            action_at="2026-07-15T00:00:00+00:00",
        ),
    )

    result = projection.build_review_projection(target, master_db)

    assert result["catalog"]["schema_version"] == 2
    assert result["catalog"]["migration_required"] is False
    assert result["catalog"]["mutation_capability"] == "manual_review_v2"
    reviewed = next(
        item
        for item in result["review_references"]
        if item["reference_id"] == reference["reference_id"]
    )
    assert (reviewed["stored_status"], reviewed["revision"], reviewed["manual_note"]) == (
        "manual_confirmed",
        1,
        "opaque / 日本語",
    )
    assert reviewed["history"][0]["action"] == "manual_confirm"


def test_projection_handles_empty_catalog_orphan_old_extractor_and_unassigned(
    tmp_path: Path, monkeypatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master_db = tmp_path / "master.sqlite"
    catalog_db = tmp_path / "data" / "catalog.sqlite"
    _write_master(master_db)
    catalog.create_catalog(catalog_db)

    empty = projection.build_review_projection(catalog_db, master_db)
    assert empty["coverage"]["status_counts"] == {"uncollected": 3}
    assert empty["review_references"] == []

    for index, (title, artist) in enumerate(
        (("Alpha", "Artist A"), ("Beta", "Artist B"), ("Unknown", "Nobody")),
        start=1,
    ):
        image = tmp_path / "data" / f"jacket-{index}.png"
        Image.new("RGB", (64, 64), (index * 30, 10, 20)).save(image)
        catalog.ingest_observation(
            catalog_db,
            master_db,
            source_image_path=image,
            source_capture_id=f"capture-{index}",
            observed_title=title,
            observed_artist=artist,
            image_kind="jacket_crop",
        )
    with sqlite3.connect(catalog_db) as connection:
        connection.execute(
            "UPDATE jacket_references SET song_id = ? WHERE observed_title = ?",
            ("missing-song", "Alpha"),
        )
        connection.execute(
            "UPDATE jacket_references SET feature_extractor_version = ? WHERE observed_title = ?",
            ("old-extractor", "Beta"),
        )

    result = projection.build_review_projection(catalog_db, master_db)
    reasons = {row["reason"] for row in result["review_references"]}
    assert {
        "master_song_missing",
        "feature_extractor_version_changed",
        "identity_not_found",
    } <= reasons
    assert result["coverage"]["orphaned_reference_count"] == 1
    assert result["coverage"]["unassigned_unresolved_observation_count"] == 1


def test_projection_rejects_non_catalog_and_concurrent_change(
    tmp_path: Path, monkeypatch
) -> None:
    master_db, catalog_db = _setup(tmp_path, monkeypatch)
    non_catalog = tmp_path / "data" / "other.sqlite"
    with sqlite3.connect(non_catalog) as connection:
        connection.execute("CREATE TABLE other (value TEXT)")
    with pytest.raises(ValueError, match="not a jacket reference catalog"):
        projection.build_review_projection(non_catalog, master_db)

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
