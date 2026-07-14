from __future__ import annotations

import csv
import json
import sqlite3
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
        connection.execute("UPDATE jacket_references SET thumbnail_rgb_json = '[0.0]'")
    with pytest.raises(ValueError, match="thumbnail_rgb length"):
        catalog.load_m5_feature_entries(catalog_path, master_db)


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
