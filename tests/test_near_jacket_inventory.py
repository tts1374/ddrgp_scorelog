from __future__ import annotations

import sqlite3
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

from PIL import Image
from test_jacket_reference_catalog import (
    auto_confirmation_request,
    ingest,
    setup_paths,
)

from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import master_match
from tools.vision_poc import near_jacket_inventory as inventory

XLSX_NS = {
    "main": "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
}


def _jacket_feature(color: tuple[int, int, int]) -> master_match.JacketFeature:
    return master_match.extract_jacket_feature(Image.new("RGB", (64, 64), color))


def _synthetic_inventory() -> dict[str, object]:
    songs = {
        "song-a": master_match.MasterSong(
            song_id="song-a",
            title="Alpha",
            artist="Artist A",
            grand_prix_play_available=True,
        ),
        "song-b": master_match.MasterSong(
            song_id="song-b",
            title="Beta",
            artist="Artist B",
            grand_prix_play_available=True,
        ),
    }
    pairs = inventory._pair_rows(
        songs=songs,
        references_by_song={
            "song-a": [_jacket_feature((10, 20, 30))],
            "song-b": [_jacket_feature((11, 20, 30))],
        },
        charts_by_song={
            "song-a": {("SINGLE", "BASIC", 1)},
            "song-b": {("SINGLE", "BASIC", 1)},
        },
        feature_fields_by_song={"song-a": {"title"}},
        threshold=0.12,
    )
    return {
        "metadata": {
            "threshold": 0.12,
            "reference_row_count": 2,
            "reference_song_count": 2,
            "catalog_master_versions": ["master-v1"],
            "pair_count": len(pairs),
            "song_count": 2,
            "incomplete_song_count": 2,
            "skipped_orphan_reference_count": 0,
            "generated_at": "2026-08-10T00:00:00+00:00",
            "definition": "fixture",
        },
        "pairs": pairs,
        "songs": inventory._song_rows(pairs),
    }


def test_pair_builder_requires_shared_chart_and_marks_feature_gap() -> None:
    model = _synthetic_inventory()
    pairs = model["pairs"]
    assert len(pairs) == 1
    row = pairs[0]
    assert row["distance"] < 0.12
    assert row["common_chart_signatures"] == "SINGLE / BASIC / Lv1"
    assert row["feature_status_a"] == "missing artist"
    assert row["feature_status_b"] == "missing title/artist"
    assert row["collection_required"] == "yes"

    no_shared_chart = inventory._pair_rows(
        songs={
            "song-a": master_match.MasterSong("song-a", "Alpha", "Artist A", True),
            "song-b": master_match.MasterSong("song-b", "Beta", "Artist B", True),
        },
        references_by_song={
            "song-a": [_jacket_feature((10, 20, 30))],
            "song-b": [_jacket_feature((11, 20, 30))],
        },
        charts_by_song={
            "song-a": {("SINGLE", "BASIC", 1)},
            "song-b": {("SINGLE", "DIFFICULT", 2)},
        },
        feature_fields_by_song={},
        threshold=0.12,
    )
    assert no_shared_chart == []


def test_export_inventory_xlsx_writes_three_readable_sheets(tmp_path: Path) -> None:
    output = tmp_path / "near-jacket-inventory.xlsx"
    inventory.export_inventory_xlsx(output, _synthetic_inventory())

    assert zipfile.is_zipfile(output)
    with zipfile.ZipFile(output) as archive:
        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        sheet_names = [
            sheet.attrib["name"]
            for sheet in workbook.findall("main:sheets/main:sheet", XLSX_NS)
        ]
        assert sheet_names == [
            "概要",
            "近似ペア",
            "対象曲",
        ]
        pair_sheet = archive.read("xl/worksheets/sheet2.xml").decode("utf-8")
        assert "SINGLE / BASIC / Lv1" in pair_sheet
        assert 'width="70"' in pair_sheet


def test_build_inventory_reads_confirmed_catalog_without_mutating_inputs(
    tmp_path: Path,
    monkeypatch,
) -> None:
    master_db, catalog_path, image_path = setup_paths(tmp_path, monkeypatch)
    second_image = tmp_path / "data/jacket-second.png"
    Image.new("RGB", (64, 64), (11, 20, 30)).save(second_image)
    ingest(
        catalog_path,
        master_db,
        image_path,
        observation_id="inventory-observation-1",
        seed="inventory-1",
    )
    ingest(
        catalog_path,
        master_db,
        second_image,
        observation_id="inventory-observation-2",
        seed="inventory-2",
    )
    catalog.apply_auto_confirmation_batch(
        catalog_path,
        master_db,
        [
            auto_confirmation_request(
                catalog_path,
                observation_id="inventory-observation-1",
                song_id="song-1",
            ),
            auto_confirmation_request(
                catalog_path,
                observation_id="inventory-observation-2",
                song_id="song-2",
            ),
        ],
    )

    before_catalog_mtime = catalog_path.stat().st_mtime_ns
    before_master_mtime = master_db.stat().st_mtime_ns
    with sqlite3.connect(catalog_path) as connection:
        before_reference_count = connection.execute(
            "SELECT COUNT(*) FROM jacket_references"
        ).fetchone()[0]

    result = inventory.build_inventory(catalog_path, master_db)

    assert result["metadata"]["reference_row_count"] == 2
    assert result["metadata"]["pair_count"] == 1
    assert result["metadata"]["incomplete_song_count"] == 2
    assert catalog_path.stat().st_mtime_ns == before_catalog_mtime
    assert master_db.stat().st_mtime_ns == before_master_mtime
    with sqlite3.connect(catalog_path) as connection:
        actual_reference_count = connection.execute(
            "SELECT COUNT(*) FROM jacket_references"
        ).fetchone()[0]
        assert actual_reference_count == before_reference_count
