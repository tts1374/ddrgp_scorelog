from __future__ import annotations

import csv
import hashlib
import json
import sqlite3
from contextlib import closing
from pathlib import Path

import pytest
from PIL import Image

from master import builder as master_builder
from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import master_match, runner


def write_master(
    path: Path,
    *,
    version: str = "master-v1",
    gp_overrides: dict[str, bool] | None = None,
) -> None:
    songs = [
        ("song-1", "Alpha", "Artist A"),
        ("song-2", "Beta", "Artist B"),
        ("song-3", "Gamma", "Artist C"),
        ("song-4", "Delta", "Artist D"),
        ("song-5", "Epsilon", "Artist E"),
        ("song-alias", "RЁVOLUTION", "TЁЯRA"),
    ]
    gp_overrides = gp_overrides or {}
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
                    int(gp_overrides.get(song_id, True)),
                    "title_artist",
                    "",
                    "2026-07-14T00:00:00+00:00",
                    "2026-07-14T00:00:00+00:00",
                )
                for song_id, title, artist in songs
            ],
        )
        connection.executemany(
            "INSERT INTO charts VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [
                (
                    f"chart-{song_id}",
                    song_id,
                    "SINGLE",
                    "BASIC",
                    1,
                    "1",
                    0,
                    0,
                    0,
                    "",
                )
                for song_id, _title, _artist in songs
            ],
        )
        connection.execute(
            "INSERT INTO song_aliases VALUES (?, ?, ?, ?, ?, ?)",
            ("alias-1", "song-alias", "RËVOLUTION", "TËЯRA", "wiki", "fixture"),
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


def write_image(path: Path, color: tuple[int, int, int]) -> None:
    Image.new("RGB", (64, 64), color).save(path)


def setup_paths(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> tuple[Path, Path]:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master_db = tmp_path / "master.sqlite"
    write_master(master_db)
    catalog_path = tmp_path / "data" / "catalog.sqlite"
    catalog.create_catalog(catalog_path)
    return master_db, catalog_path


def migrate_fixture_catalog(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> tuple[Path, Path, str]:
    master_db, source = setup_paths(tmp_path, monkeypatch)
    image = tmp_path / "data" / "review.png"
    write_image(image, (10, 20, 30))
    result = catalog.ingest_observation(
        source,
        master_db,
        source_image_path=image,
        source_capture_id="review-capture",
        observed_title="Alpha",
        observed_artist="wrong",
        image_kind="jacket_crop",
    )
    target = tmp_path / "data" / "catalog-v2.sqlite"
    catalog.migrate_catalog_v1_to_v2(source, target)
    return master_db, target, result.reference_id


def test_v1_to_v2_migration_is_copy_on_write_and_strict(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, source = setup_paths(tmp_path, monkeypatch)
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    target = tmp_path / "data" / "catalog-v2.sqlite"

    catalog.migrate_catalog_v1_to_v2(source, target)

    assert catalog.catalog_schema_version(target) == 2
    assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash
    with sqlite3.connect(target) as connection:
        assert connection.execute("SELECT COUNT(*) FROM jacket_references").fetchone()[0] == 0
        assert (
            connection.execute("SELECT COUNT(*) FROM reference_review_history").fetchone()[0]
            == 0
        )
    target_hash = hashlib.sha256(target.read_bytes()).hexdigest()
    with pytest.raises(ValueError, match="already exists"):
        catalog.migrate_catalog_v1_to_v2(source, target)
    assert hashlib.sha256(target.read_bytes()).hexdigest() == target_hash
    with pytest.raises(ValueError, match="version 1"):
        catalog.migrate_catalog_v1_to_v2(target, tmp_path / "data" / "other.sqlite")


def test_v1_to_v2_migration_holds_one_source_read_snapshot(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    _master_db, source = setup_paths(tmp_path, monkeypatch)
    target = tmp_path / "data" / "catalog-v2.sqlite"
    original_create = catalog._create_catalog_v2
    exclusive_blocked = False

    def create_while_checking_lock(path: Path, *, created_at: str) -> None:
        nonlocal exclusive_blocked
        with sqlite3.connect(source, timeout=0) as competing:
            with pytest.raises(sqlite3.OperationalError, match="locked"):
                competing.execute("BEGIN EXCLUSIVE")
            exclusive_blocked = True
        original_create(path, created_at=created_at)

    monkeypatch.setattr(catalog, "_create_catalog_v2", create_while_checking_lock)

    catalog.migrate_catalog_v1_to_v2(source, target)

    assert exclusive_blocked is True
    assert catalog.catalog_schema_version(target) == 2


def test_manual_review_is_transactional_idempotent_and_runtime_eligible(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_v2, reference_id = migrate_fixture_catalog(tmp_path, monkeypatch)
    request = catalog.ReviewMutationRequest(
        action_id="action-confirm",
        reference_id=reference_id,
        action="manual_confirm",
        expected_revision=0,
        expected_status="needs_review",
        expected_song_id=None,
        song_id="song-2",
        reason="developer selected / 日本語",
        note="opaque note",
        action_at="2026-07-15T00:00:00+00:00",
    )

    receipt = catalog.apply_review_mutation(catalog_v2, master_db, request)
    replay = catalog.apply_review_mutation(catalog_v2, master_db, request)

    assert (receipt.status, receipt.song_id, receipt.revision, receipt.idempotent) == (
        "manual_confirmed",
        "song-2",
        1,
        False,
    )
    assert replay.idempotent is True
    with sqlite3.connect(catalog_v2) as connection:
        row = connection.execute(
            "SELECT review_status, song_id, review_revision, manual_note "
            "FROM jacket_references WHERE reference_id = ?",
            (reference_id,),
        ).fetchone()
        assert row == ("manual_confirmed", "song-2", 1, "opaque note")
        assert (
            connection.execute("SELECT COUNT(*) FROM reference_review_history").fetchone()[0]
            == 1
        )
    assert [entry.song_id for entry in catalog.load_m5_feature_entries(catalog_v2, master_db)] == [
        "song-2"
    ]

    before_ingest = hashlib.sha256(catalog_v2.read_bytes()).hexdigest()
    with pytest.raises(ValueError, match="schema version 1"):
        catalog.ingest_observation(
            catalog_v2,
            master_db,
            source_image_path=tmp_path / "data" / "review.png",
            source_capture_id="review-capture",
            observed_title="Gamma",
            observed_artist="Artist C",
            image_kind="jacket_crop",
        )
    assert hashlib.sha256(catalog_v2.read_bytes()).hexdigest() == before_ingest

    before = hashlib.sha256(catalog_v2.read_bytes()).hexdigest()
    with pytest.raises(ValueError, match="different payload"):
        catalog.apply_review_mutation(
            catalog_v2,
            master_db,
            catalog.ReviewMutationRequest(**{**request.__dict__, "note": "changed"}),
        )
    with pytest.raises(ValueError, match="stale"):
        catalog.apply_review_mutation(
            catalog_v2,
            master_db,
            catalog.ReviewMutationRequest(
                action_id="action-stale",
                reference_id=reference_id,
                action="reassign",
                expected_revision=0,
                expected_status="manual_confirmed",
                expected_song_id="song-2",
                song_id="song-3",
            ),
        )
    assert hashlib.sha256(catalog_v2.read_bytes()).hexdigest() == before

    with pytest.raises(RuntimeError, match="injected"):
        catalog.apply_review_mutation(
            catalog_v2,
            master_db,
            catalog.ReviewMutationRequest(
                action_id="action-fail",
                reference_id=reference_id,
                action="reject",
                expected_revision=1,
                expected_status="manual_confirmed",
                expected_song_id="song-2",
            ),
            fail_after_current_update=True,
        )
    assert hashlib.sha256(catalog_v2.read_bytes()).hexdigest() == before


def test_idempotent_review_replay_precedes_current_master_validation(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_v2, reference_id = migrate_fixture_catalog(tmp_path, monkeypatch)
    request = catalog.ReviewMutationRequest(
        action_id="action-replay-without-master",
        reference_id=reference_id,
        action="manual_confirm",
        expected_revision=0,
        expected_status="needs_review",
        expected_song_id=None,
        song_id="song-2",
        reason="developer selected",
    )
    original = catalog.apply_review_mutation(catalog_v2, master_db, request)

    with sqlite3.connect(master_db) as connection:
        connection.execute(
            "UPDATE songs SET grand_prix_play_available = 0 WHERE song_id = 'song-2'"
        )
    replay_after_gp_removal = catalog.apply_review_mutation(catalog_v2, master_db, request)
    missing_master = tmp_path / "missing-master.sqlite"
    replay_without_master = catalog.apply_review_mutation(
        catalog_v2, missing_master, request
    )

    assert original.idempotent is False
    assert replay_after_gp_removal == replay_without_master
    assert replay_without_master == catalog.ReviewMutationReceipt(
        action_id=request.action_id,
        reference_id=reference_id,
        action="manual_confirm",
        status="manual_confirmed",
        song_id="song-2",
        revision=1,
        idempotent=True,
    )
    with pytest.raises(ValueError, match="different payload"):
        catalog.apply_review_mutation(
            catalog_v2,
            missing_master,
            catalog.ReviewMutationRequest(**{**request.__dict__, "note": "changed"}),
        )
    with pytest.raises(ValueError, match="master DB is not a file"):
        catalog.apply_review_mutation(
            catalog_v2,
            missing_master,
            catalog.ReviewMutationRequest(
                **{**request.__dict__, "action_id": "new-action-without-master"}
            ),
        )
    with sqlite3.connect(catalog_v2) as connection:
        assert connection.execute(
            "SELECT review_status, review_revision FROM jacket_references "
            "WHERE reference_id = ?",
            (reference_id,),
        ).fetchone() == ("manual_confirmed", 1)
        assert (
            connection.execute("SELECT COUNT(*) FROM reference_review_history").fetchone()[0]
            == 1
        )


def test_manual_confirm_rejects_featureless_reference_without_side_effects(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, source = setup_paths(tmp_path, monkeypatch)
    broken = tmp_path / "data" / "broken.png"
    broken.write_bytes(b"not an image")
    ingested = catalog.ingest_observation(
        source,
        master_db,
        source_image_path=broken,
        source_capture_id="featureless",
        observed_title="Beta",
        observed_artist="Artist B",
        image_kind="jacket_crop",
    )
    assert (ingested.review_status, ingested.reason) == (
        "unresolved",
        "feature_extraction_failed",
    )
    target = tmp_path / "data" / "catalog-v2.sqlite"
    catalog.migrate_catalog_v1_to_v2(source, target)
    before = hashlib.sha256(target.read_bytes()).hexdigest()

    with pytest.raises(ValueError, match="complete persisted jacket features"):
        catalog.apply_review_mutation(
            target,
            master_db,
            catalog.ReviewMutationRequest(
                action_id="featureless-confirm",
                reference_id=ingested.reference_id,
                action="manual_confirm",
                expected_revision=0,
                expected_status="unresolved",
                expected_song_id="song-2",
                song_id="song-2",
            ),
        )

    assert hashlib.sha256(target.read_bytes()).hexdigest() == before
    with sqlite3.connect(target) as connection:
        assert connection.execute(
            "SELECT review_status, review_revision FROM jacket_references "
            "WHERE reference_id = ?",
            (ingested.reference_id,),
        ).fetchone() == ("unresolved", 0)
        assert (
            connection.execute("SELECT COUNT(*) FROM reference_review_history").fetchone()[0]
            == 0
        )


def test_manual_confirm_rejects_typed_vector_corruption_without_side_effects(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, target, reference_id = migrate_fixture_catalog(tmp_path, monkeypatch)
    with sqlite3.connect(target) as connection:
        connection.execute(
            "UPDATE jacket_references SET thumbnail_rgb_json = 123 WHERE reference_id = ?",
            (reference_id,),
        )
    before = hashlib.sha256(target.read_bytes()).hexdigest()

    with pytest.raises(ValueError, match="complete persisted jacket features"):
        catalog.apply_review_mutation(
            target,
            master_db,
            catalog.ReviewMutationRequest(
                action_id="typed-corruption",
                reference_id=reference_id,
                action="manual_confirm",
                expected_revision=0,
                expected_status="needs_review",
                expected_song_id=None,
                song_id="song-1",
            ),
        )

    assert hashlib.sha256(target.read_bytes()).hexdigest() == before


@pytest.mark.parametrize(
    ("action", "observed_artist", "expected_status", "expected_song_id"),
    [
        ("manual_confirm", "wrong", "needs_review", None),
        ("reassign", "Artist A", "auto_confirmed", "song-1"),
    ],
)
def test_manual_review_rejects_stale_extractor_without_side_effects(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    action: str,
    observed_artist: str,
    expected_status: str,
    expected_song_id: str | None,
) -> None:
    master_db, source = setup_paths(tmp_path, monkeypatch)
    image = tmp_path / "data" / "stale.png"
    write_image(image, (12, 34, 56))
    ingested = catalog.ingest_observation(
        source,
        master_db,
        source_image_path=image,
        source_capture_id=f"stale-{action}",
        observed_title="Alpha",
        observed_artist=observed_artist,
        image_kind="jacket_crop",
    )
    target = tmp_path / "data" / "catalog-v2.sqlite"
    catalog.migrate_catalog_v1_to_v2(source, target)
    with sqlite3.connect(target) as connection:
        connection.execute(
            "UPDATE jacket_references SET feature_extractor_version = 'm5-jacket-v0' "
            "WHERE reference_id = ?",
            (ingested.reference_id,),
        )
    before = hashlib.sha256(target.read_bytes()).hexdigest()

    with pytest.raises(ValueError, match="current feature extractor"):
        catalog.apply_review_mutation(
            target,
            master_db,
            catalog.ReviewMutationRequest(
                action_id=f"stale-{action}",
                reference_id=ingested.reference_id,
                action=action,
                expected_revision=0,
                expected_status=expected_status,
                expected_song_id=expected_song_id,
                song_id="song-2",
            ),
        )

    assert hashlib.sha256(target.read_bytes()).hexdigest() == before
    with sqlite3.connect(target) as connection:
        assert connection.execute(
            "SELECT review_status, review_revision FROM jacket_references "
            "WHERE reference_id = ?",
            (ingested.reference_id,),
        ).fetchone() == (expected_status, 0)
        assert (
            connection.execute("SELECT COUNT(*) FROM reference_review_history").fetchone()[0]
            == 0
        )


@pytest.mark.parametrize(
    "raw",
    [123, b"\xff", json.dumps([10**1000])],
)
def test_decode_vector_normalizes_external_type_unicode_and_overflow_errors(raw: object) -> None:
    with pytest.raises(ValueError, match="invalid test_vector"):
        catalog._decode_vector(raw, length=1, field_name="test_vector")  # type: ignore[arg-type]


def test_runtime_loader_skips_corrupt_manual_reference_without_hiding_valid_auto_reference(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, source = setup_paths(tmp_path, monkeypatch)
    alpha = tmp_path / "data" / "alpha.png"
    beta = tmp_path / "data" / "beta.png"
    write_image(alpha, (10, 20, 30))
    write_image(beta, (40, 50, 60))
    catalog.ingest_observation(
        source,
        master_db,
        source_image_path=alpha,
        source_capture_id="alpha-auto",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
    )
    review = catalog.ingest_observation(
        source,
        master_db,
        source_image_path=beta,
        source_capture_id="beta-review",
        observed_title="Beta",
        observed_artist="wrong",
        image_kind="jacket_crop",
    )
    target = tmp_path / "data" / "catalog-v2.sqlite"
    catalog.migrate_catalog_v1_to_v2(source, target)
    catalog.apply_review_mutation(
        target,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="beta-confirm",
            reference_id=review.reference_id,
            action="manual_confirm",
            expected_revision=0,
            expected_status="needs_review",
            expected_song_id=None,
            song_id="song-2",
        ),
    )
    with sqlite3.connect(target) as connection:
        connection.execute(
            "UPDATE jacket_references SET thumbnail_rgb_json = 123 WHERE reference_id = ?",
            (review.reference_id,),
        )

    entries = catalog.load_m5_feature_entries(target, master_db)
    coverage_rows, summary = catalog.build_coverage(target, master_db)
    beta_coverage = next(row for row in coverage_rows if row["song_id"] == "song-2")

    assert [entry.song_id for entry in entries] == ["song-1"]
    assert beta_coverage["coverage_status"] == "needs_review"
    assert beta_coverage["reason"] == "persisted_feature_invalid"
    assert summary["current_reference_state_reason_counts"] == {
        "persisted_feature_invalid": 1
    }


def test_reject_and_reopen_preserve_evidence_and_append_history(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_v2, reference_id = migrate_fixture_catalog(tmp_path, monkeypatch)
    before_evidence: tuple[object, ...]
    with sqlite3.connect(catalog_v2) as connection:
        before_evidence = connection.execute(
            "SELECT source_image_hash, thumbnail_rgb_json, histogram_json, dhash_bits_json "
            "FROM jacket_references WHERE reference_id = ?",
            (reference_id,),
        ).fetchone()
    rejected = catalog.apply_review_mutation(
        catalog_v2,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="reject-1",
            reference_id=reference_id,
            action="reject",
            expected_revision=0,
            expected_status="needs_review",
            expected_song_id=None,
            reason="not useful",
        ),
    )
    reopened = catalog.apply_review_mutation(
        catalog_v2,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="reopen-1",
            reference_id=reference_id,
            action="reopen",
            expected_revision=1,
            expected_status="rejected",
            expected_song_id=None,
            note="try again",
        ),
    )
    assert (rejected.status, reopened.status, reopened.song_id, reopened.revision) == (
        "rejected",
        "needs_review",
        None,
        2,
    )
    with sqlite3.connect(catalog_v2) as connection:
        after_evidence = connection.execute(
            "SELECT source_image_hash, thumbnail_rgb_json, histogram_json, dhash_bits_json "
            "FROM jacket_references WHERE reference_id = ?",
            (reference_id,),
        ).fetchone()
        assert after_evidence == before_evidence
        assert (
            connection.execute("SELECT COUNT(*) FROM reference_review_history").fetchone()[0]
            == 2
        )


def test_v2_validator_rejects_audit_chain_and_payload_drift(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_v2, reference_id = migrate_fixture_catalog(tmp_path, monkeypatch)
    catalog.apply_review_mutation(
        catalog_v2,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="audit-1",
            reference_id=reference_id,
            action="manual_confirm",
            expected_revision=0,
            expected_status="needs_review",
            expected_song_id=None,
            song_id="song-1",
        ),
    )
    with sqlite3.connect(catalog_v2) as connection:
        connection.execute(
            "UPDATE reference_review_history SET receipt_json = ?",
            ('{"action_id":"audit-1"}',),
        )
    with pytest.raises(ValueError, match="review receipt fields"):
        catalog.validate_catalog(catalog_v2)


def test_v2_validator_rejects_discontinuous_history_and_manual_state_without_history(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_v2, reference_id = migrate_fixture_catalog(tmp_path, monkeypatch)
    catalog.apply_review_mutation(
        catalog_v2,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="reject-chain",
            reference_id=reference_id,
            action="reject",
            expected_revision=0,
            expected_status="needs_review",
            expected_song_id=None,
        ),
    )
    catalog.apply_review_mutation(
        catalog_v2,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="reopen-chain",
            reference_id=reference_id,
            action="reopen",
            expected_revision=1,
            expected_status="rejected",
            expected_song_id=None,
        ),
    )
    with sqlite3.connect(catalog_v2) as connection:
        connection.execute(
            "UPDATE reference_review_history SET before_status = 'unresolved' "
            "WHERE action_id = 'reopen-chain'"
        )
    with pytest.raises(ValueError, match="state discontinuity"):
        catalog.validate_catalog(catalog_v2)

    second = tmp_path / "second"
    second.mkdir()
    _master_db, clean_v2, clean_reference_id = migrate_fixture_catalog(second, monkeypatch)
    with sqlite3.connect(clean_v2) as connection:
        connection.execute(
            "UPDATE jacket_references SET review_status = 'manual_confirmed' "
            "WHERE reference_id = ?",
            (clean_reference_id,),
        )
    with pytest.raises(ValueError, match="manual state without history"):
        catalog.validate_catalog(clean_v2)


def test_v2_validator_rejects_history_action_from_impossible_source_state(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_v2, reference_id = migrate_fixture_catalog(tmp_path, monkeypatch)
    catalog.apply_review_mutation(
        catalog_v2,
        master_db,
        catalog.ReviewMutationRequest(
            action_id="invalid-source",
            reference_id=reference_id,
            action="manual_confirm",
            expected_revision=0,
            expected_status="needs_review",
            expected_song_id=None,
            song_id="song-1",
        ),
    )
    with sqlite3.connect(catalog_v2) as connection:
        raw = connection.execute(
            "SELECT request_payload_json FROM reference_review_history "
            "WHERE action_id = 'invalid-source'"
        ).fetchone()[0]
        payload = json.loads(raw)
        payload["expected_status"] = "rejected"
        connection.execute(
            "UPDATE reference_review_history SET before_status = 'rejected', "
            "request_payload_json = ? WHERE action_id = 'invalid-source'",
            (json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")),),
        )
    with pytest.raises(ValueError, match="action source"):
        catalog.validate_catalog(catalog_v2)


def test_catalog_create_is_strict_and_safe(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    catalog_path = tmp_path / "data" / "catalog.sqlite"

    catalog.create_catalog(catalog_path)
    catalog.validate_catalog(catalog_path)

    with sqlite3.connect(catalog_path) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM catalog_metadata"))
        assert metadata["catalog_identity"] == catalog.CATALOG_IDENTITY
        assert metadata["schema_version"] == "1"
        assert connection.execute("PRAGMA user_version").fetchone()[0] == 1

    with pytest.raises(ValueError, match="already exists"):
        catalog.create_catalog(catalog_path)
    with pytest.raises(ValueError, match="under data"):
        catalog.create_catalog(tmp_path / "outside.sqlite")

    with sqlite3.connect(catalog_path) as connection:
        connection.execute("CREATE TABLE plays (play_id TEXT)")
    with pytest.raises(ValueError, match="exact schema mismatch"):
        catalog.validate_catalog(catalog_path)

    noncatalog = tmp_path / "data" / "other.sqlite"
    with sqlite3.connect(noncatalog) as connection:
        connection.execute("CREATE TABLE plays (play_id TEXT)")
    with pytest.raises(ValueError, match="not a jacket reference catalog"):
        catalog.validate_catalog(noncatalog)
    with pytest.raises(ValueError, match="not a compatible M4 master database"):
        catalog.load_master_identity(noncatalog)

    wrong_fk = tmp_path / "data" / "wrong-fk.sqlite"
    wrong_fk_sql = catalog.CATALOG_SCHEMA_SQL.replace(
        "reference_id TEXT NOT NULL REFERENCES jacket_references(reference_id)\n"
        "    ON DELETE CASCADE",
        "reference_id TEXT NOT NULL REFERENCES jacket_references(song_id)",
    )
    with sqlite3.connect(wrong_fk) as connection:
        connection.executescript(wrong_fk_sql)
        connection.executemany(
            "INSERT INTO catalog_metadata VALUES (?, ?)",
            (
                ("catalog_identity", catalog.CATALOG_IDENTITY),
                ("schema_version", str(catalog.CATALOG_SCHEMA_VERSION)),
                ("created_at", "2026-07-14T00:00:00+00:00"),
            ),
        )
    with pytest.raises(ValueError, match="exact schema mismatch"):
        catalog.validate_catalog(wrong_fk)


def test_resolver_only_auto_confirms_exact_canonical_or_unique_alias(tmp_path: Path) -> None:
    master_db = tmp_path / "master.sqlite"
    write_master(master_db)
    master = catalog.load_master_identity(master_db)

    exact = catalog.resolve_observation(master, "Alpha", "Artist A")
    alias = catalog.resolve_observation(master, "RËVOLUTION", "TËЯRA")
    artist_mismatch = catalog.resolve_observation(master, "Alpha", "wrong")
    missing = catalog.resolve_observation(master, "", "Artist A")
    ambiguous_alias_master = catalog.MasterIdentity(
        version=master.version,
        songs=master.songs,
        aliases=(*master.aliases, ("song-1", "RËVOLUTION", "TËЯRA")),
    )
    ambiguous_alias = catalog.resolve_observation(ambiguous_alias_master, "RËVOLUTION", "TËЯRA")

    assert (exact.status, exact.song.song_id, exact.basis) == (
        "auto_confirmed",
        "song-1",
        "canonical_exact",
    )
    assert (alias.status, alias.song.song_id, alias.basis) == (
        "auto_confirmed",
        "song-alias",
        "unique_alias",
    )
    assert artist_mismatch.status == "needs_review"
    assert artist_mismatch.candidate_song_ids == ("song-1",)
    assert missing.status == "unresolved"
    assert ambiguous_alias.status == "needs_review"
    assert ambiguous_alias.reason == "ambiguous_alias_title_artist"

    with sqlite3.connect(master_db) as connection:
        connection.execute("UPDATE master_metadata SET value = '999' WHERE key = 'song_count'")
    with pytest.raises(ValueError, match="metadata count mismatch"):
        catalog.load_master_identity(master_db)
    with sqlite3.connect(master_db) as connection:
        connection.execute(
            "UPDATE master_metadata SET value = ? WHERE key = 'song_count'",
            ("6",),
        )
        connection.execute(
            "INSERT INTO master_metadata VALUES ('official_source_url', 'https://official.test')"
        )
    with pytest.raises(ValueError, match="complete pair"):
        catalog.load_master_identity(master_db)


def test_ingest_is_idempotent_supports_one_to_many_and_rejects_capture_conflict(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path = setup_paths(tmp_path, monkeypatch)
    first_image = tmp_path / "first.png"
    second_image = tmp_path / "second.png"
    write_image(first_image, (255, 0, 0))
    write_image(second_image, (250, 0, 0))

    first = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=first_image,
        source_capture_id="capture-1",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
    )
    repeated = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=first_image,
        source_capture_id="capture-1",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
    )
    second = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=second_image,
        source_capture_id="capture-2",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
    )

    assert (first.disposition, repeated.disposition, second.disposition) == (
        "created",
        "existing",
        "created",
    )
    with sqlite3.connect(catalog_path) as connection:
        assert connection.execute("SELECT COUNT(*) FROM jacket_references").fetchone()[0] == 2
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM jacket_references WHERE song_id = 'song-1'"
            ).fetchone()[0]
            == 2
        )

    with pytest.raises(ValueError, match="different image bytes"):
        catalog.ingest_observation(
            catalog_path,
            master_db,
            source_image_path=second_image,
            source_capture_id="capture-1",
            observed_title="Alpha",
            observed_artist="Artist A",
            image_kind="jacket_crop",
        )


def test_identical_jacket_pixels_can_reference_distinct_songs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path = setup_paths(tmp_path, monkeypatch)
    shared_image = tmp_path / "shared-jacket.png"
    write_image(shared_image, (20, 40, 80))

    alpha = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=shared_image,
        source_capture_id="",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
    )
    beta = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=shared_image,
        source_capture_id="",
        observed_title="Beta",
        observed_artist="Artist B",
        image_kind="jacket_crop",
    )
    alpha_repeated = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=shared_image,
        source_capture_id="",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
    )
    beta_repeated = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=shared_image,
        source_capture_id="",
        observed_title="Beta",
        observed_artist="Artist B",
        image_kind="jacket_crop",
    )

    assert (alpha.disposition, beta.disposition) == ("created", "created")
    assert (alpha_repeated.disposition, beta_repeated.disposition) == (
        "existing",
        "existing",
    )
    assert alpha.reference_id != beta.reference_id
    with sqlite3.connect(catalog_path) as connection:
        rows = connection.execute(
            "SELECT song_id, source_image_hash FROM jacket_references ORDER BY song_id"
        ).fetchall()
    assert [row[0] for row in rows] == ["song-1", "song-2"]
    assert rows[0][1] == rows[1][1]


def test_reingest_updates_corrected_identity_and_audit_without_adding_reference(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path = setup_paths(tmp_path, monkeypatch)
    image_path = tmp_path / "corrected.png"
    write_image(image_path, (40, 80, 120))

    created = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="capture-correction",
        observed_title="Alpha",
        observed_artist="wrong artist",
        image_kind="jacket_crop",
        now="2026-07-14T01:00:00+00:00",
    )
    audit_updated = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="capture-correction",
        observed_title="Alpha",
        observed_artist="wrong artist",
        image_kind="jacket_crop",
        expected_song_id="song-1",
        now="2026-07-14T01:30:00+00:00",
    )
    updated = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="capture-correction",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
        expected_song_id="song-1",
        now="2026-07-14T02:00:00+00:00",
    )
    repeated = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="capture-correction",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
        expected_song_id="song-1",
        now="2026-07-14T03:00:00+00:00",
    )

    assert (created.disposition, created.review_status) == ("created", "needs_review")
    assert (audit_updated.disposition, audit_updated.review_status) == (
        "updated",
        "needs_review",
    )
    assert (updated.disposition, updated.review_status) == ("updated", "auto_confirmed")
    assert repeated.disposition == "existing"
    with sqlite3.connect(catalog_path) as connection:
        connection.row_factory = sqlite3.Row
        row = connection.execute("SELECT * FROM jacket_references").fetchone()
        assert connection.execute("SELECT COUNT(*) FROM jacket_references").fetchone()[0] == 1
        assert row["song_id"] == "song-1"
        assert row["observed_artist"] == "Artist A"
        assert row["expected_song_id"] == "song-1"
        assert row["review_status"] == "auto_confirmed"
        assert row["updated_at"] == "2026-07-14T02:00:00+00:00"
        assert [
            candidate["song_id"]
            for candidate in connection.execute(
                "SELECT song_id FROM reference_candidates"
            ).fetchall()
        ] == ["song-1"]

    _rows, summary = catalog.build_coverage(catalog_path, master_db)
    assert summary["auto_confirm_rate"] == 1.0
    assert summary["known_false_auto_confirm_audit_status"] == "evaluated"
    assert summary["known_false_auto_confirm_audit_passed"]


def test_reingest_recomputes_features_when_image_kind_changes(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path = setup_paths(tmp_path, monkeypatch)
    image_path = tmp_path / "full-frame.png"
    image = Image.new("RGB", (1280, 720), (255, 0, 0))
    image.paste((0, 0, 255), (812, 28, 962, 178))
    image.save(image_path)

    created = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="capture-image-kind",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="full_frame",
        now="2026-07-14T04:00:00+00:00",
    )
    with sqlite3.connect(catalog_path) as connection:
        initial_thumbnail = connection.execute(
            "SELECT thumbnail_rgb_json FROM jacket_references"
        ).fetchone()[0]

    updated = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="capture-image-kind",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
        now="2026-07-14T05:00:00+00:00",
    )
    repeated = catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="capture-image-kind",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
        now="2026-07-14T06:00:00+00:00",
    )

    with Image.open(image_path) as source_image:
        expected_feature = master_match.extract_jacket_feature(source_image)
    with sqlite3.connect(catalog_path) as connection:
        connection.row_factory = sqlite3.Row
        row = connection.execute("SELECT * FROM jacket_references").fetchone()
        assert connection.execute("SELECT COUNT(*) FROM jacket_references").fetchone()[0] == 1
        assert row["image_kind"] == "jacket_crop"
        assert row["thumbnail_rgb_json"] != initial_thumbnail
        assert json.loads(row["thumbnail_rgb_json"]) == pytest.approx(
            expected_feature.thumbnail.tolist()
        )
        assert json.loads(row["histogram_json"]) == pytest.approx(
            expected_feature.histogram.tolist()
        )
        assert json.loads(row["dhash_bits_json"]) == pytest.approx(
            expected_feature.dhash_bits.tolist()
        )
        assert row["dhash_hex"] == expected_feature.dhash_hex
        assert row["updated_at"] == "2026-07-14T05:00:00+00:00"

    assert created.disposition == "created"
    assert updated.disposition == "updated"
    assert repeated.disposition == "existing"


def test_persisted_features_replay_existing_m5_match_after_reference_image_deleted(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path = setup_paths(tmp_path, monkeypatch)
    image_path = tmp_path / "reference.png"
    write_image(image_path, (30, 120, 240))
    expected_feature = master_match.extract_jacket_feature(Image.open(image_path))
    catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="capture-replay",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
    )
    image_path.unlink()

    entries = catalog.load_m5_feature_entries(catalog_path, master_db)

    assert len(entries) == 1
    assert entries[0].song_id == "song-1"
    assert master_match.jacket_feature_distance(entries[0].feature, expected_feature) == 0.0
    match_row = {
        "frame_index": "0",
        "organized_file": "result.png",
        "song_title_status": "ready",
        "song_title_extracted_value": "Alpha",
        "song_title_expected_value": "Alpha",
        "play_style_status": "ready",
        "play_style_extracted_value": "SINGLE",
        "difficulty_status": "ready",
        "difficulty_extracted_value": "BASIC",
        "level_status": "ready",
        "level_extracted_value": "1",
    }
    match = master_match.match_jacket_save_candidate_row(
        match_row,
        master_db,
        expected_feature,
        entries,
    )
    assert match["jacket_match_status"] == "matched"
    assert match["top_song_id"] == "song-1"
    assert match["identity_signal_status"] == "jacket_resolved_candidate"

    with sqlite3.connect(catalog_path) as connection:
        connection.execute("UPDATE jacket_references SET thumbnail_rgb_json = 123")
    with pytest.raises(ValueError, match="invalid thumbnail_rgb"):
        catalog.load_m5_feature_entries(catalog_path, master_db)


def test_m5_feature_loader_ignores_stale_extractor_versions(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path = setup_paths(tmp_path, monkeypatch)
    image_path = tmp_path / "versioned-reference.png"
    write_image(image_path, (60, 90, 120))
    catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="current-version",
        observed_title="Alpha",
        observed_artist="Artist A",
        image_kind="jacket_crop",
    )
    with sqlite3.connect(catalog_path) as connection:
        connection.execute(
            """
            INSERT INTO jacket_references (
              reference_id, source_capture_id, source_image_hash, master_version, song_id,
              canonical_title_snapshot, canonical_artist_snapshot, review_status,
              resolution_reason, resolution_basis, feature_extractor_version,
              image_kind, thumbnail_rgb_json, histogram_json, dhash_bits_json, dhash_hex,
              observed_title, observed_artist, observation_status, expected_song_id,
              created_at, updated_at
            )
            SELECT
              'stale-reference', NULL, source_image_hash, master_version, 'song-2',
              'Beta', 'Artist B', review_status, resolution_reason, resolution_basis,
              'm5-jacket-v0', image_kind, thumbnail_rgb_json, histogram_json,
              dhash_bits_json, dhash_hex, 'Beta', 'Artist B', observation_status, '',
              created_at, updated_at
            FROM jacket_references
            WHERE feature_extractor_version = ?
            """,
            (catalog.FEATURE_EXTRACTOR_VERSION,),
        )
        assert connection.execute("SELECT COUNT(*) FROM jacket_references").fetchone()[0] == 2

    entries = catalog.load_m5_feature_entries(catalog_path, master_db)

    assert len(entries) == 1
    assert entries[0].song_id == "song-1"
    assert entries[0].organized_file != "catalog:stale-reference"
    coverage_rows, summary = catalog.build_coverage(catalog_path, master_db)
    beta = next(row for row in coverage_rows if row["song_id"] == "song-2")
    assert beta["coverage_status"] == "needs_review"
    assert beta["reason"] == "feature_extractor_version_changed"
    assert summary["current_reference_state_reason_counts"] == {
        "feature_extractor_version_changed": 1
    }


def test_coverage_counts_all_gp_songs_failures_and_read_only_master_drift(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path = setup_paths(tmp_path, monkeypatch)
    alpha = tmp_path / "alpha.png"
    gamma = tmp_path / "gamma.png"
    epsilon = tmp_path / "epsilon.png"
    broken = tmp_path / "broken.png"
    write_image(alpha, (255, 0, 0))
    write_image(gamma, (0, 255, 0))
    write_image(epsilon, (0, 0, 255))
    broken.write_bytes(b"not an image")

    for path, capture_id, title, artist, expected in (
        (alpha, "a", "Alpha", "Artist A", "song-1"),
        (broken, "b", "Beta", "Artist B", "song-2"),
        (epsilon, "e", "Epsilon", "Artist E", "song-5"),
    ):
        catalog.ingest_observation(
            catalog_path,
            master_db,
            source_image_path=path,
            source_capture_id=capture_id,
            observed_title=title,
            observed_artist=artist,
            image_kind="jacket_crop",
            expected_song_id=expected,
        )

    master_db.unlink()
    write_master(master_db, version="master-v2", gp_overrides={"song-5": False})
    catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=gamma,
        source_capture_id="g",
        observed_title="Gamma",
        observed_artist="Artist C",
        image_kind="jacket_crop",
        expected_song_id="song-3",
    )
    before = master_db.read_bytes()

    rows, summary = catalog.build_coverage(catalog_path, master_db)

    assert master_db.read_bytes() == before
    statuses = {row["song_id"]: row["coverage_status"] for row in rows}
    assert statuses == {
        "song-1": "needs_review",
        "song-2": "unresolved",
        "song-3": "referenced",
        "song-4": "uncollected",
        "song-alias": "uncollected",
    }
    assert summary["grand_prix_song_count"] == 5
    assert summary["coverage_status_counts"] == {
        "needs_review": 1,
        "referenced": 1,
        "uncollected": 2,
        "unresolved": 1,
    }
    assert summary["captured_observation_count"] == 4
    assert summary["auto_confirmed_observation_count"] == 1
    assert summary["auto_confirm_rate"] == 0.25
    assert summary["ingest_auto_confirmed_observation_count"] == 3
    assert summary["ingest_auto_confirm_rate"] == 0.75
    assert summary["orphan_reason_counts"] == {"song_not_grand_prix_available": 1}
    assert summary["known_false_auto_confirm_count"] == 0
    assert summary["known_false_auto_confirm_audit_status"] == "evaluated"
    assert summary["known_false_auto_confirm_audit_passed"]
    assert {item["reason"] for item in summary["improvement_candidates"]} >= {
        "feature_extraction_failed",
        "master_version_changed",
        "song_not_grand_prix_available",
    }


def test_coverage_marks_candidate_songs_as_needing_review(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, catalog_path = setup_paths(tmp_path, monkeypatch)
    image_path = tmp_path / "artist-mismatch.png"
    write_image(image_path, (80, 40, 20))
    catalog.ingest_observation(
        catalog_path,
        master_db,
        source_image_path=image_path,
        source_capture_id="candidate-alpha",
        observed_title="Alpha",
        observed_artist="wrong artist",
        image_kind="jacket_crop",
    )

    rows, summary = catalog.build_coverage(catalog_path, master_db)

    alpha = next(row for row in rows if row["song_id"] == "song-1")
    assert alpha["coverage_status"] == "needs_review"
    assert alpha["reference_count"] == "1"
    assert alpha["reason"] == "title_match_artist_mismatch"
    assert summary["coverage_status_counts"] == {
        "needs_review": 1,
        "uncollected": 5,
    }
    assert summary["unassigned_unresolved_observation_count"] == 0


def test_build_cli_writes_local_catalog_and_coverage(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master_db = tmp_path / "master.sqlite"
    write_master(master_db)
    image_path = tmp_path / "alpha.png"
    write_image(image_path, (10, 20, 30))
    observations = tmp_path / "observations.csv"
    with observations.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(
            file,
            fieldnames=[
                "source_image_path",
                "source_capture_id",
                "observed_title",
                "observed_artist",
                "image_kind",
                "expected_song_id",
            ],
        )
        writer.writeheader()
        writer.writerow(
            {
                "source_image_path": str(image_path),
                "source_capture_id": "cli-1",
                "observed_title": "Alpha",
                "observed_artist": "Artist A",
                "image_kind": "jacket_crop",
                "expected_song_id": "song-1",
            }
        )

    exit_code = catalog.main(
        [
            "build",
            "--catalog",
            "data/catalog.sqlite",
            "--master-db",
            str(master_db),
            "--observations",
            str(observations),
            "--coverage-output",
            "data/coverage",
        ]
    )

    assert exit_code == 0
    summary = json.loads(
        (tmp_path / "data/coverage/jacket_catalog_coverage_summary.json").read_text(
            encoding="utf-8"
        )
    )
    assert summary["auto_confirm_rate"] == 1.0
    assert summary["known_false_auto_confirm_audit_status"] == "evaluated"
    assert summary["known_false_auto_confirm_audit_passed"]
    assert (tmp_path / "data/coverage/jacket_catalog_song_coverage.csv").is_file()
    assert (tmp_path / "data/coverage/jacket_catalog_coverage.md").is_file()


def test_csv_build_does_not_publish_partial_new_or_existing_catalog(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_db, existing_catalog = setup_paths(tmp_path, monkeypatch)
    image_path = tmp_path / "alpha.png"
    write_image(image_path, (10, 20, 30))
    observations = tmp_path / "observations.csv"
    with observations.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(
            file,
            fieldnames=["source_image_path", "observed_title", "observed_artist"],
        )
        writer.writeheader()
        writer.writerows(
            [
                {
                    "source_image_path": str(image_path),
                    "observed_title": "Alpha",
                    "observed_artist": "Artist A",
                },
                {
                    "source_image_path": str(tmp_path / "missing.png"),
                    "observed_title": "Beta",
                    "observed_artist": "Artist B",
                },
            ]
        )

    before = existing_catalog.read_bytes()
    with pytest.raises(ValueError, match="source image does not exist"):
        catalog.build_from_observation_csv(existing_catalog, master_db, observations)
    assert existing_catalog.read_bytes() == before

    new_catalog = tmp_path / "data" / "new.sqlite"
    with pytest.raises(ValueError, match="source image does not exist"):
        catalog.build_from_observation_csv(new_catalog, master_db, observations)
    assert not new_catalog.exists()
    assert not list((tmp_path / "data").glob(".*.sqlite"))


def test_runner_catalog_option_requires_explicit_m5_jacket_match(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()

    with pytest.raises(ValueError, match="requires --m5-jacket-match"):
        runner.main(["--m5-jacket-catalog", "data/catalog.sqlite"])
