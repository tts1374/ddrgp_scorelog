from __future__ import annotations

import json
import subprocess
from collections import Counter
from pathlib import Path

import pytest
from PIL import Image

from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import master_match
from tools.vision_poc import title_artist_evaluation as evaluation
from tools.vision_poc import title_artist_ocr_diagnostics as diagnostics


def _master() -> catalog.MasterIdentity:
    return catalog.MasterIdentity(
        version="master-v1",
        songs=(
            master_match.MasterSong("song-alpha", "Alpha", "Artist A", True),
        ),
        aliases=(),
        source_hash="a" * 64,
    )


def _ok_extractor(
    _image: Image.Image,
    profile: diagnostics.OcrProfile,
    _executable: str | None,
    _languages: set[str],
) -> evaluation.FieldExtraction:
    raw = "Alpha" if profile.field == "title" else "Artist A"
    return evaluation.FieldExtraction(raw, raw.casefold(), 0.99, "ok", "")


def test_profile_matrix_and_missing_japanese_language_are_explicit() -> None:
    assert len(diagnostics.TITLE_PROFILES) == 4
    assert len(diagnostics.ARTIST_PROFILES) == 12
    japanese = next(
        profile for profile in diagnostics.PROFILES if profile.language == "jpn+eng"
    )

    available, reason = diagnostics.profile_availability(
        japanese, "tesseract", {"eng", "osd"}, ""
    )

    assert available is False
    assert reason == "tesseract_language_unavailable_v1:jpn"
    assert {profile.psm for profile in diagnostics.TITLE_PROFILES} == {6, 7}
    assert {profile.scale for profile in diagnostics.ARTIST_PROFILES} == {5, 10, 15}
    assert {profile.sharpen for profile in diagnostics.ARTIST_PROFILES} == {True, False}


def test_evaluation_compares_eng_and_does_not_fallback_for_missing_jpn() -> None:
    calls: list[str] = []

    def extractor(
        image: Image.Image,
        profile: diagnostics.OcrProfile,
        executable: str | None,
        languages: set[str],
    ) -> evaluation.FieldExtraction:
        calls.append(profile.profile_id)
        return _ok_extractor(image, profile, executable, languages)

    rows = diagnostics.evaluate_images(
        [("observation-1", Image.new("RGB", (1280, 720), "black"))],
        master=_master(),
        executable="tesseract",
        languages={"eng", "osd"},
        extractor=extractor,
    )
    summary = diagnostics.summarize(
        rows,
        total_references=1,
        eligible_observations=1,
        skipped_reasons=Counter(),
        executable="tesseract",
        languages={"eng", "osd"},
        probe_failure="",
    )

    assert len(calls) == 8
    assert all("jpn-eng" not in profile_id for profile_id in calls)
    assert len(rows) == 15
    baseline = next(
        row
        for row in rows
        if row["configuration_id"] == diagnostics.BASELINE_TITLE_PROFILE
    )
    assert baseline["candidate_status"] == "auto_confirmed"
    assert baseline["candidate_song_ids"] == ["song-alpha"]
    japanese_profiles = [
        profile for profile in summary["profiles"] if profile["language"] == "jpn+eng"
    ]
    assert all(profile["observation_count"] == 1 for profile in japanese_profiles)
    assert all(
        profile["status_counts"] == {"ocr_unavailable": 1}
        for profile in japanese_profiles
    )


def test_tesseract_probe_parses_languages_and_rejects_invalid_output(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(diagnostics.shutil, "which", lambda _name: "tesseract")
    monkeypatch.setattr(
        diagnostics.subprocess,
        "run",
        lambda *args, **kwargs: subprocess.CompletedProcess(
            args[0], 0, b"List of available languages (2):\neng\njpn\n", b""
        ),
    )
    assert diagnostics.inspect_tesseract() == ("tesseract", {"eng", "jpn"}, "")

    monkeypatch.setattr(
        diagnostics.subprocess,
        "run",
        lambda *args, **kwargs: subprocess.CompletedProcess(args[0], 0, b"\xff", b""),
    )
    assert diagnostics.inspect_tesseract()[2] == "tesseract_language_probe_invalid"


def test_tsv_contract_probe_rejects_plain_text_fallback(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    header = (
        b"level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\t"
        b"width\theight\tconf\ttext\n"
        b"1\t1\t0\t0\t0\t0\t0\t0\t32\t32\t-1\t\n"
    )
    monkeypatch.setattr(
        diagnostics.subprocess,
        "run",
        lambda *args, **kwargs: subprocess.CompletedProcess(args[0], 0, header, b""),
    )
    assert diagnostics.probe_tsv_contract("tesseract", "eng") == ""

    monkeypatch.setattr(
        diagnostics.subprocess,
        "run",
        lambda *args, **kwargs: subprocess.CompletedProcess(args[0], 0, b"plain text\n", b""),
    )
    assert (
        diagnostics.probe_tsv_contract("tesseract", "jpn+eng")
        == "tesseract_output_invalid"
    )


def test_reports_are_byte_stable_and_contact_sheet_contains_no_db_write(
    tmp_path: Path,
) -> None:
    image_path = tmp_path / "source.png"
    Image.new("RGB", (1280, 720), "black").save(image_path)
    rows = diagnostics.evaluate_images(
        [("observation-1", Image.open(image_path).convert("RGB"))],
        master=_master(),
        executable="tesseract",
        languages={"eng"},
        extractor=_ok_extractor,
    )
    summary = diagnostics.summarize(
        rows,
        total_references=1,
        eligible_observations=1,
        skipped_reasons=Counter(),
        executable="tesseract",
        languages={"eng"},
        probe_failure="",
    )
    output = tmp_path / "report"

    diagnostics.write_reports(
        output,
        rows,
        summary,
        image_paths={"observation-1": image_path},
        representative_limit=1,
    )
    first = {path.name: path.read_bytes() for path in output.iterdir()}
    diagnostics.write_reports(
        output,
        rows,
        summary,
        image_paths={"observation-1": image_path},
        representative_limit=1,
    )
    second = {path.name: path.read_bytes() for path in output.iterdir()}

    assert first == second
    assert set(first) == {
        "ocr_diagnostics.csv",
        "ocr_diagnostics.json",
        "ocr_diagnostics.md",
        "representative_contact_sheet.png",
    }
    report = json.loads((output / "ocr_diagnostics.json").read_text(encoding="utf-8"))
    assert report["report_schema_version"] == diagnostics.REPORT_SCHEMA_VERSION


def test_zero_representative_limit_suppresses_source_rows() -> None:
    rows = diagnostics.evaluate_images(
        [("observation-1", Image.new("RGB", (1280, 720), "black"))],
        master=_master(),
        executable="tesseract",
        languages={"eng"},
        extractor=_ok_extractor,
    )

    assert diagnostics._representative_rows(rows, 0) == []
    assert diagnostics.render_contact_sheet([], {}).size == (1200, 100)
