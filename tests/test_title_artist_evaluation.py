from __future__ import annotations

import hashlib
import json
import sqlite3
import subprocess
from contextlib import closing
from pathlib import Path

import pytest
from PIL import Image

from master import builder as master_builder
from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import title_artist_evaluation as evaluation


def write_master(path: Path) -> None:
    songs = [
        ("song-alpha", "Alpha", "Artist A"),
        ("song-alpha-live", "Alpha", "Artist A Live"),
        ("song-beta", "Beta", "Artist B"),
    ]
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


def fixture_paths(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> tuple[Path, Path, Path]:
    monkeypatch.chdir(tmp_path)
    (tmp_path / "data").mkdir()
    master = tmp_path / "data" / "master.sqlite"
    write_master(master)
    catalog_path = tmp_path / "data" / "catalog.sqlite"
    catalog.create_catalog(catalog_path)
    artifact_root = tmp_path / "data" / "jacket_catalog_collector"
    return master, catalog_path, artifact_root


def catalog_created_at(path: Path) -> str:
    with closing(sqlite3.connect(path)) as connection:
        return str(
            connection.execute(
                "SELECT value FROM catalog_metadata WHERE key = 'created_at'"
            ).fetchone()[0]
        )


def write_artifact(
    artifact_root: Path,
    catalog_path: Path,
    *,
    index: int,
    source_color: tuple[int, int, int] = (10, 20, 30),
    composite: bool = False,
) -> str:
    observation_id = hashlib.sha256(f"observation-{index}".encode()).hexdigest()
    relative = Path("session-1") / "observations" / observation_id / "observation.json"
    directory = artifact_root / relative.parent
    directory.mkdir(parents=True, exist_ok=True)
    source = directory / "source.png"
    crop = directory / "jacket-crop.png"
    Image.new("RGB", (1280, 720), source_color).save(source)
    Image.new("RGB", (149, 149), source_color).save(crop)
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    crop_hash = hashlib.sha256(crop.read_bytes()).hexdigest()
    manifest = {
        "manifest_version": "m5c-observation-manifest-v1",
        "session_id": "session-1",
        "observation_id": observation_id,
        "source_image": "source.png",
        "jacket_crop": "jacket-crop.png",
        "source_image_hash": source_hash,
        "jacket_crop_hash": crop_hash,
        "source_width": 1280,
        "source_height": 720,
        "source_sequence": index,
        "captured_at_utc": "2026-07-16T00:00:00+00:00",
        "feature_version": "m5c-jacket-rgb-grid-v1",
        "roi_version": "m5c-song-select-jacket-roi-v2",
        "master_version": "master-v1",
        "master_source_hash": "fixture-source-hash",
        "catalog_identity": catalog.CATALOG_IDENTITY,
        "catalog_schema_version": 1,
        "catalog_created_at": catalog_created_at(catalog_path),
        "feature_extractor_version": catalog.FEATURE_EXTRACTOR_VERSION,
        "detector_version": "m5c-3b-jacket-detector-v1",
        "frame_clock_version": "m5c-capture-utc-clock-v1",
        "window": {
            "handle": "0000000000000001",
            "process_id": 1,
            "process_start_ticks": 1,
            "process_name": "fixture",
            "title": "fixture",
            "class_name": "fixture",
            "client_width": 1280,
            "client_height": 720,
            "is_visible": True,
            "is_minimized": False,
        },
        "change_threshold": 0.08,
        "stable_frame_count_required": 3,
        "minimum_stable_duration_milliseconds": 100,
        "roi_x": 809,
        "roi_y": 27,
        "roi_width": 149,
        "roi_height": 149,
        "feature_hash": hashlib.sha256(f"feature-{index}".encode()).hexdigest(),
        "mean_absolute_difference": 0.01,
        "sample_width": 16,
        "sample_height": 16,
        "observed_title": "",
        "observed_artist": "",
        "observation_status": "unresolved",
        "created_at_utc": "2026-07-16T00:00:01+00:00",
    }
    if composite:
        title_line_hash = hashlib.sha256(f"title-line-{index}".encode()).hexdigest()
        composite_version = catalog.COMPOSITE_IDENTITY_VERSION
        canonical = "\0".join(
            (
                composite_version,
                manifest["feature_version"],
                manifest["feature_hash"],
                "m5c-information-title-line-binary-sha256-v1",
                title_line_hash,
            )
        ).encode("utf-8")
        manifest.update(
            {
                "manifest_version": "m5c-observation-manifest-v2",
                "title_line_feature_version": "m5c-information-title-line-binary-sha256-v1",
                "title_line_hash": title_line_hash,
                "title_line_detector_version": "m5c-information-title-line-detector-v1",
                "title_line_roi_version": "m5c-song-select-information-panel-roi-v1",
                "title_line_source_sequence": index,
                "title_line_captured_at_utc": manifest["captured_at_utc"],
                "composite_identity_version": composite_version,
                "composite_identity_hash": hashlib.sha256(canonical).hexdigest(),
            }
        )
    (directory / "observation.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
    )
    return relative.as_posix()


def write_dataset(path: Path, entries: list[dict[str, str | None]]) -> None:
    path.write_text(
        json.dumps(
            {"dataset_schema_version": evaluation.DATASET_SCHEMA_VERSION, "entries": entries},
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
        newline="\n",
    )


def entry(
    manifest: str,
    *,
    title: str | None = "Alpha",
    artist: str | None = "Artist A",
    song_id: str | None = "song-alpha",
) -> dict[str, str | None]:
    return {
        "observation_manifest": manifest,
        "expected_title": title,
        "expected_artist": artist,
        "expected_song_id": song_id,
    }


def alpha_extractor(_image: Image.Image, field: str, _method: str) -> evaluation.FieldExtraction:
    raw = "Alpha" if field == "title" else "Artist A"
    return evaluation.FieldExtraction(
        raw, evaluation.master_match.normalize_song_title(raw), 0.99, "ok", ""
    )


def beta_extractor(_image: Image.Image, field: str, _method: str) -> evaluation.FieldExtraction:
    raw = "Beta" if field == "title" else "Artist B"
    return evaluation.FieldExtraction(
        raw, evaluation.master_match.normalize_song_title(raw), 0.99, "ok", ""
    )


def test_strict_loader_distinguishes_expected_coverage_and_preserves_same_image_ids(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifests = [write_artifact(artifact_root, catalog_path, index=index) for index in range(3)]
    dataset = tmp_path / "data" / "dataset.json"
    write_dataset(
        dataset,
        [
            entry(manifests[0]),
            entry(manifests[1], artist=None, song_id=None),
            entry(manifests[2], title=None, artist=None, song_id=None),
        ],
    )

    receipt = evaluation.run_evaluation(
        dataset_path=dataset,
        artifact_root=artifact_root,
        master_db=master_path,
        catalog_db=catalog_path,
        output_dir=tmp_path / "data" / "report",
        extractor=alpha_extractor,
    )

    report = json.loads((tmp_path / "data/report/title_artist_evaluation.json").read_text())
    assert {row["coverage_status"] for row in report["rows"]} == {
        "evaluated",
        "partially_evaluated",
        "no_expected_values",
    }
    assert len({row["observation_id"] for row in report["rows"]}) == 3
    assert receipt["adopted_methods"] == []


def test_current_roi_contract_excludes_jacket_and_artist_panel_boundaries() -> None:
    assert catalog.SONG_SELECT_JACKET_ROI_VERSION == "m5c-song-select-jacket-roi-v2"
    assert catalog.SONG_SELECT_JACKET_ROI == (809, 27, 149, 149)
    assert catalog.FEATURE_EXTRACTOR_VERSION == "m5-jacket-v2"
    assert evaluation.TITLE_ARTIST_ROI_VERSION == "m5c-song-select-title-artist-roi-v2"
    assert evaluation.FIELD_ROIS == {
        "title": (306, 58, 470, 34),
        "artist": (309, 97, 467, 23),
    }
    assert evaluation._scaled_roi(Image.new("RGB", (2560, 1440)), "artist") == (
        618,
        194,
        1552,
        240,
    )


def test_composite_manifest_v2_is_accepted_and_identity_drift_is_rejected(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifest = write_artifact(artifact_root, catalog_path, index=1, composite=True)
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry(manifest)])
    output = tmp_path / "data/report"

    evaluation.run_evaluation(
        dataset_path=dataset,
        artifact_root=artifact_root,
        master_db=master_path,
        catalog_db=catalog_path,
        output_dir=output,
        extractor=alpha_extractor,
    )
    assert (output / "title_artist_evaluation.json").is_file()

    manifest_path = artifact_root / manifest
    value = json.loads(manifest_path.read_text(encoding="utf-8"))
    value["composite_identity_hash"] = "0" * 64
    manifest_path.write_text(json.dumps(value), encoding="utf-8", newline="\n")
    drift_output = tmp_path / "data/drift-report"
    with pytest.raises(ValueError, match="composite identity"):
        evaluation.run_evaluation(
            dataset_path=dataset,
            artifact_root=artifact_root,
            master_db=master_path,
            catalog_db=catalog_path,
            output_dir=drift_output,
            extractor=alpha_extractor,
        )
    assert not drift_output.exists()


def test_title_is_primary_and_artist_mismatch_remains_review_candidate(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifest = write_artifact(artifact_root, catalog_path, index=1)
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry(manifest)])
    artifacts = evaluation.load_artifacts(
        evaluation.load_dataset(dataset),
        artifact_root=artifact_root,
        master=catalog.load_master_identity(master_path),
        catalog=evaluation.load_catalog_identity(catalog_path),
    )

    def mismatch_artist(
        _image: Image.Image, field: str, _method: str
    ) -> evaluation.FieldExtraction:
        raw = "Alpha" if field == "title" else "wrong artist"
        return evaluation.FieldExtraction(
            raw, evaluation.master_match.normalize_song_title(raw), 0.99, "ok", ""
        )

    rows = evaluation.evaluate(
        artifacts, master=catalog.load_master_identity(master_path), extractor=mismatch_artist
    )

    assert {row["candidate_status"] for row in rows} == {"needs_review"}
    assert {row["candidate_reason"] for row in rows} == {"title_match_artist_mismatch"}
    assert all(row["auto_confirm_eligible"] is False for row in rows)


def test_adoption_requires_real_minimum_and_rejects_known_false_candidate(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifests = [
        write_artifact(artifact_root, catalog_path, index=index)
        for index in range(evaluation.MINIMUM_EVALUATED_ARTIFACTS)
    ]
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry(manifest) for manifest in manifests])
    artifacts = evaluation.load_artifacts(
        evaluation.load_dataset(dataset),
        artifact_root=artifact_root,
        master=catalog.load_master_identity(master_path),
        catalog=evaluation.load_catalog_identity(catalog_path),
    )

    accepted = evaluation.summarize(
        evaluation.evaluate(
            artifacts, master=catalog.load_master_identity(master_path), extractor=alpha_extractor
        )
    )
    rejected = evaluation.summarize(
        evaluation.evaluate(
            artifacts, master=catalog.load_master_identity(master_path), extractor=beta_extractor
        )
    )

    assert accepted["adopted_methods"] == list(evaluation.METHOD_VERSIONS)
    assert rejected["adopted_methods"] == []
    assert all(
        method["known_false_auto_confirm_count"] == len(manifests) for method in rejected["methods"]
    )


def test_low_confidence_and_partial_failure_never_auto_confirm() -> None:
    low = evaluation.FieldExtraction("Alpha", "alpha", 0.5, "low_confidence", "below")
    empty = evaluation.FieldExtraction("", "", None, "empty", "empty_ocr")
    assert low.status != "ok"
    assert empty.status != "ok"


def test_expected_text_with_low_confidence_is_not_counted_as_pair_exact(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifest = write_artifact(artifact_root, catalog_path, index=1)
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry(manifest)])
    artifacts = evaluation.load_artifacts(
        evaluation.load_dataset(dataset),
        artifact_root=artifact_root,
        master=catalog.load_master_identity(master_path),
        catalog=evaluation.load_catalog_identity(catalog_path),
    )

    def low_confidence_expected(
        _image: Image.Image, field: str, _method: str
    ) -> evaluation.FieldExtraction:
        raw = "Alpha" if field == "title" else "Artist A"
        return evaluation.FieldExtraction(
            raw,
            evaluation.master_match.normalize_song_title(raw),
            0.5,
            "low_confidence",
            "below_confidence_gate",
        )

    rows = evaluation.evaluate(
        artifacts,
        master=catalog.load_master_identity(master_path),
        extractor=low_confidence_expected,
    )
    summary = evaluation.summarize(rows)

    assert all(row["pair_exact"] is False for row in rows)
    assert all(row["auto_confirm_eligible"] is False for row in rows)
    assert all(method["pair_exact_count"] == 0 for method in summary["methods"])


def test_tesseract_tsv_confidence_and_engine_failure_are_explicit(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    image = Image.new("RGB", (1280, 720), "black")
    tsv = (
        b"level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\t"
        b"height\tconf\ttext\n"
        b"5\t1\t1\t1\t1\t1\t0\t0\t10\t10\t95.0\tAlpha\n"
    )
    monkeypatch.setattr(evaluation.shutil, "which", lambda _name: "tesseract")
    monkeypatch.setattr(
        evaluation.subprocess,
        "run",
        lambda *args, **kwargs: subprocess.CompletedProcess(args[0], 0, tsv, b""),
    )

    extracted = evaluation.extract_field(
        image, "title", "tesseract-autocontrast-v1"
    )

    assert extracted == evaluation.FieldExtraction("Alpha", "alpha", 0.95, "ok", "")
    monkeypatch.setattr(evaluation.shutil, "which", lambda _name: None)
    unavailable = evaluation.extract_field(
        image, "title", "tesseract-autocontrast-v1"
    )
    assert unavailable.status == "engine_unavailable"


def test_corrupt_or_root_escape_input_has_no_report_side_effect(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifest = write_artifact(artifact_root, catalog_path, index=1)
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry("../escape/observation.json")])
    output = tmp_path / "data/report"

    with pytest.raises(ValueError, match="escapes artifact root"):
        evaluation.run_evaluation(
            dataset_path=dataset,
            artifact_root=artifact_root,
            master_db=master_path,
            catalog_db=catalog_path,
            output_dir=output,
            extractor=alpha_extractor,
        )
    assert not output.exists()

    write_dataset(dataset, [entry(manifest)])
    source = artifact_root / Path(manifest).parent / "source.png"
    source.write_bytes(source.read_bytes() + b"corrupt")
    with pytest.raises(ValueError, match="hash mismatch"):
        evaluation.run_evaluation(
            dataset_path=dataset,
            artifact_root=artifact_root,
            master_db=master_path,
            catalog_db=catalog_path,
            output_dir=output,
            extractor=alpha_extractor,
        )
    assert not output.exists()


def test_same_input_report_is_byte_stable_and_db_bytes_are_unchanged(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifest = write_artifact(artifact_root, catalog_path, index=1)
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry(manifest)])
    output = tmp_path / "data/report"
    before_dbs = (master_path.read_bytes(), catalog_path.read_bytes())

    for _ in range(2):
        evaluation.run_evaluation(
            dataset_path=dataset,
            artifact_root=artifact_root,
            master_db=master_path,
            catalog_db=catalog_path,
            output_dir=output,
            extractor=alpha_extractor,
        )
        current = tuple(
            (output / name).read_bytes()
            for name in (
                "title_artist_evaluation.csv",
                "title_artist_evaluation.json",
                "title_artist_evaluation.md",
            )
        )
        if "first" not in locals():
            first = current
        else:
            assert current == first

    assert (master_path.read_bytes(), catalog_path.read_bytes()) == before_dbs


def test_duplicate_manifest_and_master_drift_are_strictly_rejected(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifest = write_artifact(artifact_root, catalog_path, index=1)
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry(manifest), entry(manifest)])
    with pytest.raises(ValueError, match="duplicate observation manifest"):
        evaluation.load_dataset(dataset)

    document_path = artifact_root / manifest
    document = json.loads(document_path.read_text())
    document["master_version"] = "old-master"
    document_path.write_text(json.dumps(document), encoding="utf-8", newline="\n")
    write_dataset(dataset, [entry(manifest)])
    with pytest.raises(ValueError, match="reference identity drift"):
        evaluation.load_artifacts(
            evaluation.load_dataset(dataset),
            artifact_root=artifact_root,
            master=catalog.load_master_identity(master_path),
            catalog=evaluation.load_catalog_identity(catalog_path),
        )


def test_old_jacket_roi_manifest_is_rejected_without_artifact_side_effects(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifest = write_artifact(artifact_root, catalog_path, index=1)
    manifest_path = artifact_root / manifest
    value = json.loads(manifest_path.read_text(encoding="utf-8"))
    value.update(
        {
            "roi_version": "m5c-song-select-jacket-roi-v1",
            "roi_x": 812,
            "roi_y": 28,
            "roi_width": 150,
            "roi_height": 150,
        }
    )
    manifest_path.write_text(json.dumps(value), encoding="utf-8", newline="\n")
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry(manifest)])
    output = tmp_path / "data/report"

    with pytest.raises(ValueError, match="immutable identity"):
        evaluation.run_evaluation(
            dataset_path=dataset,
            artifact_root=artifact_root,
            master_db=master_path,
            catalog_db=catalog_path,
            output_dir=output,
            extractor=alpha_extractor,
        )

    assert not output.exists()


def test_expected_song_requires_complete_consistent_expected_pair(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    master_path, catalog_path, artifact_root = fixture_paths(tmp_path, monkeypatch)
    manifest = write_artifact(artifact_root, catalog_path, index=1)
    dataset = tmp_path / "data/dataset.json"
    write_dataset(dataset, [entry(manifest, artist=None)])
    with pytest.raises(ValueError, match="requires title and artist"):
        evaluation.load_dataset(dataset)

    write_dataset(dataset, [entry(manifest, song_id="song-beta")])
    artifacts = evaluation.load_artifacts(
        evaluation.load_dataset(dataset),
        artifact_root=artifact_root,
        master=catalog.load_master_identity(master_path),
        catalog=evaluation.load_catalog_identity(catalog_path),
    )
    with pytest.raises(ValueError, match="disagree with current master"):
        evaluation.evaluate(
            artifacts,
            master=catalog.load_master_identity(master_path),
            extractor=alpha_extractor,
        )
