from __future__ import annotations

import hashlib
import sqlite3
from contextlib import closing
from pathlib import Path

import pytest
from PIL import Image

from master import builder as master_builder
from tools.vision_poc import jacket_reference_catalog as catalog


def write_master(path: Path) -> None:
    songs = [("song-1", "Alpha", "Artist A"), ("song-2", "Beta", "Artist B")]
    with closing(sqlite3.connect(path)) as connection, connection:
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
                    1,
                    "title_artist",
                    "",
                    "2026-07-16T00:00:00+00:00",
                    "2026-07-16T00:00:00+00:00",
                )
                for song_id, title, artist in songs
            ],
        )
        connection.executemany(
            "INSERT INTO charts VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [
                (f"chart-{song_id}", song_id, "SINGLE", "BASIC", 1, "1", 0, 0, 0, "")
                for song_id, _title, _artist in songs
            ],
        )
        source_url = "https://example.test/master"
        source_hash = "fixture-source-hash"
        connection.executemany(
            "INSERT INTO master_metadata VALUES (?, ?)",
            (
                ("master_version", "master-v1"),
                ("source_url", source_url),
                ("generated_at", "2026-07-16T00:00:00+00:00"),
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
                "2026-07-16T00:00:00+00:00",
                source_hash,
                "fixture-v1",
                "<html></html>",
            ),
        )


def setup_paths(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> tuple[Path, Path, Path]:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master_db = tmp_path / "master.sqlite"
    write_master(master_db)
    catalog_path = tmp_path / "data/catalog.sqlite"
    catalog.create_catalog(catalog_path)
    image_path = tmp_path / "data/jacket.png"
    Image.new("RGB", (64, 64), (10, 20, 30)).save(image_path)
    return master_db, catalog_path, image_path


def identity(seed: str) -> dict[str, str]:
    jacket_hash = hashlib.sha256(f"jacket:{seed}".encode()).hexdigest()
    title_hash = hashlib.sha256(f"title:{seed}".encode()).hexdigest()
    return {
        "jacket_feature_version": catalog.JACKET_FRAME_FEATURE_VERSION,
        "jacket_feature_hash": jacket_hash,
        "title_line_feature_version": catalog.TITLE_LINE_FEATURE_VERSION,
        "title_line_hash": title_hash,
        "composite_identity_version": catalog.COMPOSITE_IDENTITY_VERSION,
        "expected_composite_identity_hash": catalog.composite_identity_hash(
            catalog.JACKET_FRAME_FEATURE_VERSION,
            jacket_hash,
            catalog.TITLE_LINE_FEATURE_VERSION,
            title_hash,
        ),
    }


def created_at(path: Path) -> str:
    with sqlite3.connect(path) as connection:
        return str(
            connection.execute(
                "SELECT value FROM catalog_metadata WHERE key = 'created_at'"
            ).fetchone()[0]
        )


def ingest(
    catalog_path: Path,
    master_db: Path,
    image_path: Path,
    *,
    observation_id: str,
    seed: str,
) -> catalog.IngestResult:
    return catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        observation_id=observation_id,
        expected_image_hash=hashlib.sha256(image_path.read_bytes()).hexdigest(),
        expected_master_version="master-v1",
        expected_master_source_hash="fixture-source-hash",
        expected_feature_extractor_version=catalog.FEATURE_EXTRACTOR_VERSION,
        expected_catalog_identity=catalog.CATALOG_IDENTITY,
        expected_catalog_schema_version=1,
        expected_catalog_created_at=created_at(catalog_path),
        **identity(seed),
    )


def test_current_create_has_exact_composite_schema(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    _master, catalog_path, _image = setup_paths(tmp_path, monkeypatch)

    catalog.validate_catalog(catalog_path)

    with sqlite3.connect(catalog_path) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM catalog_metadata"))
        columns = {
            row[1] for row in connection.execute("PRAGMA table_info(jacket_references)")
        }
        indexes = {
            row[1] for row in connection.execute("PRAGMA index_list(jacket_references)")
        }
        assert connection.execute("PRAGMA user_version").fetchone()[0] == 1
    assert metadata["schema_version"] == "1"
    assert {
        "jacket_feature_version",
        "jacket_feature_hash",
        "title_line_feature_version",
        "title_line_hash",
        "composite_identity_version",
        "composite_identity_hash",
    } <= columns
    assert "idx_jacket_references_composite_identity" in indexes
    assert catalog.load_composite_identities(catalog_path) == frozenset()


def test_old_catalog_version_is_read_only_rejected(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path, image_path = setup_paths(tmp_path, monkeypatch)
    with sqlite3.connect(catalog_path) as connection:
        connection.execute("PRAGMA user_version = 3")
        connection.execute(
            "UPDATE catalog_metadata SET value = '3' WHERE key = 'schema_version'"
        )
    before = catalog_path.read_bytes()

    with pytest.raises(ValueError, match="unsupported"):
        catalog.validate_catalog(catalog_path)
    with pytest.raises(ValueError, match="unsupported"):
        catalog.ingest_observation(
            catalog_path,
            master_db,
            source_image_path=image_path,
            observation_id="old-schema",
            expected_image_hash=hashlib.sha256(image_path.read_bytes()).hexdigest(),
            **identity("old-schema"),
        )
    assert catalog_path.read_bytes() == before


def test_ingest_rejects_incomplete_or_noncanonical_identity_before_write(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path, image_path = setup_paths(tmp_path, monkeypatch)
    base = identity("strict")
    before = catalog_path.read_bytes()

    for field in base:
        invalid = dict(base)
        invalid[field] = None  # type: ignore[assignment]
        with pytest.raises(ValueError, match="requires composite identity"):
            catalog.ingest_observation(
                catalog_path,
                master_db,
                source_image_path=image_path,
                observation_id=f"missing-{field}",
                **invalid,
            )
        assert catalog_path.read_bytes() == before

    for field, value, message in (
        ("jacket_feature_hash", "A" * 64, "feature fields are invalid"),
        ("title_line_feature_version", "unknown", "feature fields are invalid"),
        ("expected_composite_identity_hash", "0" * 64, "composite identity is invalid"),
    ):
        invalid = dict(base)
        invalid[field] = value
        with pytest.raises(ValueError, match=message):
            catalog.ingest_observation(
                catalog_path,
                master_db,
                source_image_path=image_path,
                observation_id=f"invalid-{field}",
                **invalid,
            )
        assert catalog_path.read_bytes() == before


def apply_status(
    catalog_path: Path, master_db: Path, reference_id: str, target: str
) -> None:
    if target == "unresolved":
        return
    if target == "auto_confirmed":
        with sqlite3.connect(catalog_path) as connection:
            connection.execute(
                "UPDATE jacket_references SET review_status = 'auto_confirmed', "
                "song_id = 'song-1', canonical_title_snapshot = 'Alpha', "
                "canonical_artist_snapshot = 'Artist A' WHERE reference_id = ?",
                (reference_id,),
            )
        return
    catalog.apply_review_mutation(
        catalog_path,
        master_db,
        catalog.ReviewMutationRequest(
            action_id=f"confirm-{target}",
            reference_id=reference_id,
            action="manual_confirm",
            expected_revision=0,
            expected_status="unresolved",
            expected_song_id=None,
            song_id="song-1",
        ),
    )
    if target == "manual_confirmed":
        return
    catalog.apply_review_mutation(
        catalog_path,
        master_db,
        catalog.ReviewMutationRequest(
            action_id=f"reject-{target}",
            reference_id=reference_id,
            action="reject",
            expected_revision=1,
            expected_status="manual_confirmed",
            expected_song_id="song-1",
        ),
    )
    if target == "rejected":
        return
    catalog.apply_review_mutation(
        catalog_path,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="reopen-needs-review",
            reference_id=reference_id,
            action="reopen",
            expected_revision=2,
            expected_status="rejected",
            expected_song_id="song-1",
        ),
    )


@pytest.mark.parametrize(
    "status",
    ["unresolved", "auto_confirmed", "manual_confirmed", "needs_review", "rejected"],
)
def test_duplicate_identity_converges_for_all_review_statuses(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, status: str
) -> None:
    master_db, catalog_path, image_path = setup_paths(tmp_path, monkeypatch)
    first = ingest(
        catalog_path,
        master_db,
        image_path,
        observation_id="first-observation",
        seed="shared",
    )
    apply_status(catalog_path, master_db, first.reference_id, status)

    duplicate = ingest(
        catalog_path,
        master_db,
        image_path,
        observation_id="second-observation",
        seed="shared",
    )

    assert duplicate.reference_id == first.reference_id
    assert duplicate.disposition == "existing"
    assert duplicate.review_status == status
    with sqlite3.connect(catalog_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM jacket_references").fetchone()[0] == 1


def test_replay_conflict_and_review_failure_leave_catalog_bytes_unchanged(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path, image_path = setup_paths(tmp_path, monkeypatch)
    result = ingest(
        catalog_path,
        master_db,
        image_path,
        observation_id="same-observation",
        seed="same",
    )
    replay = ingest(
        catalog_path,
        master_db,
        image_path,
        observation_id="same-observation",
        seed="same",
    )
    assert replay.disposition == "existing"
    before_conflict = catalog_path.read_bytes()
    with pytest.raises(ValueError, match="different canonical payload"):
        ingest(
            catalog_path,
            master_db,
            image_path,
            observation_id="same-observation",
            seed="different",
        )
    assert catalog_path.read_bytes() == before_conflict

    request = catalog.ReviewMutationRequest(
        action_id="manual-confirm",
        reference_id=result.reference_id,
        action="manual_confirm",
        expected_revision=0,
        expected_status="unresolved",
        expected_song_id=None,
        song_id="song-2",
        note="日本語 note",
    )
    receipt = catalog.apply_review_mutation(catalog_path, master_db, request)
    assert receipt.status == "manual_confirmed"
    assert catalog.apply_review_mutation(catalog_path, master_db, request).idempotent
    before_failure = catalog_path.read_bytes()
    with pytest.raises(RuntimeError, match="injected"):
        catalog.apply_review_mutation(
            catalog_path,
            master_db,
            catalog.ReviewMutationRequest(
                action_id="reassign-rollback",
                reference_id=result.reference_id,
                action="reassign",
                expected_revision=1,
                expected_status="manual_confirmed",
                expected_song_id="song-2",
                song_id="song-1",
            ),
            fail_after_current_update=True,
        )
    assert catalog_path.read_bytes() == before_failure
    entries = catalog.load_m5_feature_entries(catalog_path, master_db)
    assert [entry.song_id for entry in entries] == ["song-2"]


def test_current_receipt_coverage_and_cli_contract(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path, image_path = setup_paths(tmp_path, monkeypatch)
    result = ingest(
        catalog_path,
        master_db,
        image_path,
        observation_id="original-observation",
        seed="receipt",
    )
    identity_values = identity("receipt")

    receipt = catalog.validate_observation_receipt(
        catalog_path,
        observation_id="duplicate-observation",
        catalog_status="ingested",
        catalog_reference_id=result.reference_id,
        jacket_crop_hash=hashlib.sha256(image_path.read_bytes()).hexdigest(),
        expected_feature_extractor_version=catalog.FEATURE_EXTRACTOR_VERSION,
        expected_catalog_schema_version=1,
        expected_catalog_created_at=created_at(catalog_path),
        composite_identity_version=catalog.COMPOSITE_IDENTITY_VERSION,
        composite_identity_hash_value=identity_values["expected_composite_identity_hash"],
    )
    rows, summary = catalog.build_coverage(catalog_path, master_db)
    subcommands = catalog.build_parser()._subparsers._group_actions[0].choices

    assert receipt["catalog_schema_version"] == 1
    assert len(rows) == 2
    assert summary["schema_version"] == 1
    assert {
        "create",
        "coverage",
        "review",
        "ingest",
        "validate-session",
        "validate-receipt",
    } == set(subcommands)
    assert {"migrate-v2", "migrate-v3", "ingest-v2"}.isdisjoint(subcommands)
