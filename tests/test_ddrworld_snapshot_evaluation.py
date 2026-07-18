from __future__ import annotations

import hashlib
import json
import sqlite3
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

import pytest
from PIL import Image

from tools.ddrworld_snapshot_evaluation.cli import main
from tools.ddrworld_snapshot_evaluation.evaluator import (
    EvaluationConfig,
    EvaluationError,
    evaluate_snapshot,
)
from tools.vision_poc import master_match

OFFICE = "urn:oasis:names:tc:opendocument:xmlns:office:1.0"
TABLE = "urn:oasis:names:tc:opendocument:xmlns:table:1.0"
TEXT = "urn:oasis:names:tc:opendocument:xmlns:text:1.0"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_image(path: Path, color: tuple[int, int, int]) -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.new("RGB", (64, 64), color).save(path, format="JPEG")
    return sha256(path)


def write_ods(path: Path, review_rows: list[list[object]]) -> None:
    ET.register_namespace("office", OFFICE)
    ET.register_namespace("table", TABLE)
    ET.register_namespace("text", TEXT)
    root = ET.Element(f"{{{OFFICE}}}document-content")
    body = ET.SubElement(root, f"{{{OFFICE}}}body")
    spreadsheet = ET.SubElement(body, f"{{{OFFICE}}}spreadsheet")
    table = ET.SubElement(
        spreadsheet,
        f"{{{TABLE}}}table",
        {f"{{{TABLE}}}name": "Review"},
    )
    headers = [
        "audit_no",
        "review_status",
        "truth_song_id",
        "truth_title",
        "truth_artist",
        "observation_id",
    ]
    for values in [headers, *review_rows]:
        row = ET.SubElement(table, f"{{{TABLE}}}table-row")
        for value in values:
            cell = ET.SubElement(
                row,
                f"{{{TABLE}}}table-cell",
                {f"{{{OFFICE}}}value-type": "string"},
            )
            paragraph = ET.SubElement(cell, f"{{{TEXT}}}p")
            paragraph.text = "" if value is None else str(value)
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("content.xml", ET.tostring(root, encoding="utf-8"))


def write_master(path: Path) -> None:
    with sqlite3.connect(path) as connection:
        connection.executescript(
            """
            CREATE TABLE songs (
              song_id TEXT PRIMARY KEY,
              title TEXT NOT NULL,
              artist TEXT NOT NULL,
              grand_prix_play_available INTEGER NOT NULL
            );
            CREATE TABLE song_aliases (
              song_id TEXT NOT NULL,
              alias_title TEXT NOT NULL,
              alias_artist TEXT NOT NULL
            );
            """
        )
        connection.executemany(
            "INSERT INTO songs VALUES (?, ?, ?, ?)",
            [
                ("song_red", "Red Song", "Red Artist", 1),
                ("song_blue", "Blue Song", "Blue Artist", 1),
                ("song_gp_only", "GP Song", "GP Artist", 1),
                ("song_other", "Other Song", "Other Artist", 0),
            ],
        )
        connection.execute(
            "INSERT INTO song_aliases VALUES (?, ?, ?)",
            ("song_blue", "Blue Alias", "Blue Alias Artist"),
        )


def feature_values(color: tuple[int, int, int]) -> tuple[str, str, str, str]:
    feature = master_match.extract_jacket_feature(Image.new("RGB", (64, 64), color))
    return (
        json.dumps(feature.thumbnail.tolist(), separators=(",", ":")),
        json.dumps(feature.histogram.tolist(), separators=(",", ":")),
        json.dumps(feature.dhash_bits.tolist(), separators=(",", ":")),
        feature.dhash_hex,
    )


def write_catalog(
    path: Path,
    observation_colors: dict[str, tuple[int, int, int]],
) -> None:
    with sqlite3.connect(path) as connection:
        connection.executescript(
            """
            CREATE TABLE catalog_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE jacket_references (
              source_capture_id TEXT NOT NULL,
              feature_extractor_version TEXT NOT NULL,
              jacket_feature_version TEXT NOT NULL,
              image_kind TEXT NOT NULL,
              thumbnail_rgb_json TEXT NOT NULL,
              histogram_json TEXT NOT NULL,
              dhash_bits_json TEXT NOT NULL,
              dhash_hex TEXT NOT NULL
            );
            """
        )
        connection.executemany(
            "INSERT INTO catalog_metadata VALUES (?, ?)",
            [
                ("catalog_identity", "ddrgp-local-jacket-reference-catalog"),
                ("schema_version", "1"),
            ],
        )
        for observation_id, color in observation_colors.items():
            thumbnail, histogram, dhash, dhash_hex = feature_values(color)
            connection.execute(
                "INSERT INTO jacket_references VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    observation_id,
                    "m5-jacket-v2",
                    "m5c-jacket-rgb-grid-v1",
                    "jacket_crop",
                    thumbnail,
                    histogram,
                    dhash,
                    dhash_hex,
                ),
            )


def write_snapshot(
    path: Path,
    *,
    blue_color: tuple[int, int, int] = (0, 0, 255),
    status: str = "complete",
) -> None:
    red_path = path / "jackets/red.jpg"
    blue_path = path / "jackets/blue.jpg"
    red_hash = write_image(red_path, (255, 0, 0))
    blue_hash = write_image(blue_path, blue_color)
    rows = [
        {
            "source_page": 0,
            "page_position": 0,
            "title": "Red  Song",
            "artist": "Red Artist",
            "jacket_source_url": "https://example.invalid/red",
            "jacket_local_path": "jackets/red.jpg",
            "jacket_sha256": red_hash,
            "jacket_error": None,
        },
        {
            "source_page": 0,
            "page_position": 1,
            "title": "Blue Alias",
            "artist": "Blue Alias Artist",
            "jacket_source_url": "https://example.invalid/blue",
            "jacket_local_path": "jackets/blue.jpg",
            "jacket_sha256": blue_hash,
            "jacket_error": None,
        },
    ]
    path.mkdir(parents=True, exist_ok=True)
    (path / "songs.jsonl").write_text(
        "".join(json.dumps(row) + "\n" for row in rows),
        encoding="utf-8",
        newline="\n",
    )
    manifest = {
        "schema_version": "ddrworld-music-snapshot-manifest-v1",
        "status": status,
        "images": [
            {
                "source_url": row["jacket_source_url"],
                "sha256": row["jacket_sha256"],
                "local_path": row["jacket_local_path"],
                "error": None,
            }
            for row in rows
        ],
        "failures": [] if status == "complete" else [{"resource": "page"}],
    }
    summary = {
        "schema_version": "ddrworld-music-snapshot-summary-v1",
        "status": status,
        "song_count": 2,
        "image_request_count": 2,
        "stored_jacket_count": 2,
    }
    (path / "manifest.json").write_text(
        json.dumps(manifest), encoding="utf-8", newline="\n"
    )
    (path / "summary.json").write_text(
        json.dumps(summary), encoding="utf-8", newline="\n"
    )


def build_inputs(
    tmp_path: Path,
    *,
    blue_color: tuple[int, int, int] = (0, 0, 255),
    snapshot_status: str = "complete",
) -> EvaluationConfig:
    master = tmp_path / "master.sqlite"
    catalog = tmp_path / "catalog.sqlite"
    truth = tmp_path / "truth.ods"
    snapshot = tmp_path / "snapshot"
    write_master(master)
    write_catalog(
        catalog,
        {
            "obs-red": (255, 0, 0),
            "obs-blue": (0, 0, 255),
            "obs-rejected": (10, 10, 10),
        },
    )
    write_ods(
        truth,
        [
            [1, "confirmed", "song_red", "Red Song", "Red Artist", "obs-red"],
            [2, "confirmed", "song_blue", "Blue Song", "Blue Artist", "obs-blue"],
            [3, "rejected", None, None, None, "obs-rejected"],
        ],
    )
    write_snapshot(snapshot, blue_color=blue_color, status=snapshot_status)
    return EvaluationConfig(
        snapshot=snapshot,
        truth_ods=truth,
        catalog=catalog,
        master=master,
        output=tmp_path / "output",
    )


def test_evaluate_snapshot_reports_top_k_and_preserves_inputs(tmp_path: Path) -> None:
    config = build_inputs(tmp_path)
    input_hashes = {
        path: sha256(path) for path in (config.truth_ods, config.catalog, config.master)
    }

    output = evaluate_snapshot(config)

    assert output == config.output.resolve()
    summary = json.loads((output / "summary.json").read_text(encoding="utf-8"))
    assert summary["snapshot_mapping_status_counts"] == {
        "alias_exact": 1,
        "canonical_notation_difference": 1,
    }
    assert summary["grand_prix_only_candidate_song_count"] == 1
    assert summary["not_in_ddrworld_candidate_song_count"] == 1
    metrics = summary["jacket_metrics"]
    assert metrics["confirmed_truth_count"] == 2
    assert metrics["rejected_count"] == 1
    assert metrics["top_k"]["1"]["correct"] == 2
    assert metrics["decision_status_counts"] == {"matched_correct": 2}
    assert metrics["decision_precision"] == 1.0
    assert {path: sha256(path) for path in input_hashes} == input_hashes


def test_equal_official_features_are_held_as_ambiguous(tmp_path: Path) -> None:
    config = build_inputs(tmp_path, blue_color=(255, 0, 0))

    output = evaluate_snapshot(config)

    summary = json.loads((output / "summary.json").read_text(encoding="utf-8"))
    statuses = summary["jacket_metrics"]["decision_status_counts"]
    assert statuses["hold_ambiguous"] == 1


def test_corrupt_snapshot_jacket_hash_is_rejected_without_output(tmp_path: Path) -> None:
    config = build_inputs(tmp_path)
    songs_path = config.snapshot / "songs.jsonl"
    rows = [json.loads(line) for line in songs_path.read_text(encoding="utf-8").splitlines()]
    rows[0]["jacket_sha256"] = "0" * 64
    songs_path.write_text(
        "".join(json.dumps(row) + "\n" for row in rows),
        encoding="utf-8",
        newline="\n",
    )

    with pytest.raises(EvaluationError, match="hash.*mismatch"):
        evaluate_snapshot(config)

    assert not config.output.exists()
    assert not config.output.with_name("output.incomplete").exists()


def test_incomplete_snapshot_is_rejected(tmp_path: Path) -> None:
    config = build_inputs(tmp_path, snapshot_status="incomplete")

    with pytest.raises(EvaluationError, match="not a complete"):
        evaluate_snapshot(config)


def test_catalog_truth_observation_mismatch_is_rejected(tmp_path: Path) -> None:
    config = build_inputs(tmp_path)
    with sqlite3.connect(config.catalog) as connection:
        connection.execute("DELETE FROM jacket_references WHERE source_capture_id = 'obs-rejected'")

    with pytest.raises(EvaluationError, match="observation sets do not match"):
        evaluate_snapshot(config)


def test_existing_output_is_rejected_before_input_processing(tmp_path: Path) -> None:
    config = build_inputs(tmp_path)
    config.output.mkdir()

    with pytest.raises(EvaluationError, match="already exists"):
        evaluate_snapshot(config)


def test_cli_returns_two_for_invalid_input(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    exit_code = main(
        [
            "--snapshot",
            str(tmp_path / "missing"),
            "--truth-ods",
            str(tmp_path / "missing.ods"),
            "--catalog",
            str(tmp_path / "missing.sqlite"),
            "--master",
            str(tmp_path / "missing-master.sqlite"),
            "--output",
            str(tmp_path / "output"),
        ]
    )

    assert exit_code == 2
    assert "does not exist" in capsys.readouterr().err
