from __future__ import annotations

import re
import sqlite3
from collections import Counter
from dataclasses import replace
from pathlib import Path

import pytest
from PIL import Image, ImageDraw

from tools.vision_poc import runner
from tools.vision_poc.capture_save_workflow import (
    AutomaticFormalEvidence,
    CaptureAnalyzedEvent,
    promote_automatic_formal_values,
)
from tools.vision_poc.personal_score_db_save import write_personal_score_db_save
from tools.vision_poc.personal_score_db_save_adapter import (
    PersonalScoreDbFormalPlayValues,
    PersonalScoreDbSaveAdapterInput,
    adapt_personal_score_db_save_input,
)
from tools.vision_poc.result_fields import (
    RESULT_JUDGMENT_COUNT_FIELDS,
    clear_type_from_counts,
    rank_from_score,
    recognize_flare_rank,
    recognize_rank_roi,
    recognize_result_fields,
)


def _rank_roi(color: tuple[int, int, int]) -> Image.Image:
    image = Image.new("RGB", (160, 126), "black")
    ImageDraw.Draw(image).rectangle((20, 10, 135, 115), fill=color)
    return image


def _e_rank_roi() -> Image.Image:
    image = Image.new("RGB", (160, 126), "black")
    draw = ImageDraw.Draw(image)
    color = (240, 240, 240)
    draw.rectangle((20, 10, 35, 115), fill=color)
    draw.rectangle((20, 10, 105, 25), fill=color)
    draw.rectangle((20, 55, 95, 70), fill=color)
    draw.rectangle((20, 100, 105, 115), fill=color)
    return image


def _normal_rank_with_white_animation_roi() -> Image.Image:
    image = Image.new("RGB", (160, 126), "black")
    draw = ImageDraw.Draw(image)
    gold = (255, 220, 0)
    draw.rectangle((24, 12, 38, 112), fill=gold)
    draw.rectangle((24, 12, 102, 26), fill=gold)
    draw.rectangle((24, 98, 102, 112), fill=gold)
    draw.rectangle((88, 26, 102, 98), fill=gold)
    draw.rectangle((108, 10, 150, 74), fill=(240, 240, 240))
    return image


@pytest.mark.parametrize(
    ("score", "expected"),
    [
        (1_000_000, "AAA"),
        (990_000, "AAA"),
        (989_990, "AA+"),
        (950_000, "AA+"),
        (949_990, "AA"),
        (900_000, "AA"),
        (899_990, "AA-"),
        (890_000, "AA-"),
        (889_990, "A+"),
        (850_000, "A+"),
        (849_990, "A"),
        (800_000, "A"),
        (799_990, "A-"),
        (790_000, "A-"),
        (789_990, "B+"),
        (750_000, "B+"),
        (749_990, "B"),
        (700_000, "B"),
        (699_990, "B-"),
        (690_000, "B-"),
        (689_990, "C+"),
        (650_000, "C+"),
        (649_990, "C"),
        (600_000, "C"),
        (599_990, "C-"),
        (590_000, "C-"),
        (589_990, "D+"),
        (550_000, "D+"),
        (549_990, "D"),
        (0, "D"),
    ],
)
def test_rank_from_score_uses_issue_100_thresholds(score: int, expected: str) -> None:
    assert rank_from_score(score) == expected


@pytest.mark.parametrize("score", [-10, 1_000_010, 123, True, None, "990000"])
def test_rank_from_score_rejects_invalid_formal_score(score: object) -> None:
    assert rank_from_score(score) is None


def test_rank_roi_only_decides_failed_e_or_non_failed() -> None:
    assert recognize_rank_roi(_e_rank_roi()).is_failed is True
    assert recognize_rank_roi(_rank_roi((255, 220, 0))).is_failed is False

    ambiguous = Image.new("RGB", (160, 126), "black")
    assert recognize_rank_roi(ambiguous).is_failed is None


def test_rank_roi_does_not_treat_white_area_as_failed_e() -> None:
    white_animation = _rank_roi((240, 240, 240))
    recognition = recognize_rank_roi(white_animation)
    assert recognition.is_failed is None
    assert recognition.reason != "failed_e_glyph_shape"


def test_rank_roi_rejects_white_animation_around_normal_rank() -> None:
    recognition = recognize_rank_roi(_normal_rank_with_white_animation_roi())
    assert recognition.is_failed is not True


@pytest.mark.parametrize(
    ("counts", "expected"),
    [
        ({"marvelous": 120, "perfect": 0, "great": 0, "good": 0, "ok": 4, "miss": 0}, "MFC"),
        ({"marvelous": 120, "perfect": 2, "great": 0, "good": 0, "ok": 4, "miss": 0}, "PFC"),
        ({"marvelous": 120, "perfect": 2, "great": 3, "good": 0, "ok": 4, "miss": 0}, "GFC"),
        ({"marvelous": 120, "perfect": 2, "great": 3, "good": 4, "ok": 4, "miss": 0}, "FULL COMBO"),
        ({"marvelous": 120, "perfect": 2, "great": 3, "good": 4, "ok": 4, "miss": 1}, "CLEAR"),
    ],
)
def test_clear_type_uses_all_six_counts_and_priority(
    counts: dict[str, int], expected: str
) -> None:
    assert clear_type_from_counts(counts, failed=False) == expected


def test_clear_type_failed_has_priority_and_missing_counts_do_not_imply_clear() -> None:
    assert clear_type_from_counts({}, failed=True) == "FAILED"
    assert clear_type_from_counts({}, failed=False) is None
    assert clear_type_from_counts(
        {field_name: 0 for field_name in RESULT_JUDGMENT_COUNT_FIELDS} | {"miss": -1},
        failed=False,
    ) is None


def test_clear_type_does_not_read_rank_effects() -> None:
    counts = {
        "marvelous": 1,
        "perfect": 0,
        "great": 0,
        "good": 0,
        "ok": 9,
        "miss": 0,
    }
    assert clear_type_from_counts(counts, failed=False) == "MFC"


def test_failed_rank_overrides_score_and_count_in_result_fields() -> None:
    recognition = recognize_result_fields(
        rank_roi=_e_rank_roi(),
        flare_roi=Image.new("RGB", (120, 130), "black"),
        score=990_000,
        judgment_counts={},
    )
    assert (recognition.rank, recognition.clear_type) == ("E", "FAILED")


def test_flare_badge_is_optional_when_unrecognized() -> None:
    assert recognize_flare_rank(Image.new("RGB", (120, 130), "black")).value is None


def test_flare_samples_cover_ten_levels_three_times_when_local_assets_are_available() -> None:
    root = Path("samples/screenshots/organized/result")
    pattern = re.compile(
        r"^result_\d+_flare_(?P<flare>i{1,3}|iv|v|vi|vii|viii|ix|ex)\.png$",
        re.IGNORECASE,
    )
    files = [path for path in root.glob("result_*_flare_*.png") if pattern.match(path.name)]
    if not files:
        pytest.skip("Issue #100 local FLARE sample assets are not available")
    assert len(files) == 30
    assert Counter(pattern.match(path.name).group("flare").upper() for path in files) == {
        "I": 3,
        "II": 3,
        "III": 3,
        "IV": 3,
        "V": 3,
        "VI": 3,
        "VII": 3,
        "VIII": 3,
        "IX": 3,
        "EX": 3,
    }
    for path in files:
        expected = pattern.match(path.name).group("flare").upper()
        with Image.open(path) as image:
            actual = recognize_flare_rank(
                runner.crop_roi(image, runner.ROI_DEFINITIONS["flare_rank"])
            )
        assert actual.status == "recognized"
        assert actual.value == expected


def _formal_values(*, flare_rank: str | None) -> PersonalScoreDbFormalPlayValues:
    return PersonalScoreDbFormalPlayValues(
        play_id="play-issue-100",
        played_at="2026-07-28T12:00:00+09:00",
        master_version="master-v1",
        song_id="song-100",
        chart_id="chart-100",
        score=990_000,
        max_combo=500,
        marvelous=500,
        perfect=0,
        great=0,
        good=0,
        miss=0,
        ex_score=2_000,
        rank="AAA",
        clear_type="MFC",
        duplicate_key="capture-event:v1:issue-100",
        flare_rank=flare_rank,
    )


def _adapter_input(formal: PersonalScoreDbFormalPlayValues) -> PersonalScoreDbSaveAdapterInput:
    return PersonalScoreDbSaveAdapterInput(
        candidate_material={"flare_rank": "candidate-ignored"},
        capture_id="capture-issue-100",
        capture_hash="sha256:issue-100",
        captured_at="2026-07-28T12:00:00+09:00",
        source_kind="manual",
        source_path="fixture",
        analysis_id="analysis-issue-100",
        event_type="confirmed",
        confirmed_result=True,
        duplicate=False,
        confirmation_mode="manual",
        identity_signal_status="reviewed",
        digit_review_status="reviewed",
        analysis_confidence=0.99,
        analysis_summary_json='{"contract":"issue-100"}',
        app_version="0.1.0",
        formal_play=formal,
    )


def test_flare_rank_formal_evidence_to_db_round_trip() -> None:
    adapter = adapt_personal_score_db_save_input(_adapter_input(_formal_values(flare_rank="EX")))
    assert adapter.status == "ready"
    assert adapter.save_input is not None
    with sqlite3.connect(":memory:") as connection:
        result = write_personal_score_db_save(connection, adapter.save_input)
        assert result.saved
        assert connection.execute(
            "SELECT flare_rank FROM plays WHERE play_id = ?", ("play-issue-100",)
        ).fetchone() == ("EX",)


def test_null_flare_rank_is_valid_formal_value_and_candidate_does_not_replace_it() -> None:
    adapter = adapt_personal_score_db_save_input(_adapter_input(_formal_values(flare_rank=None)))
    assert adapter.status == "ready"
    assert adapter.save_input is not None
    with sqlite3.connect(":memory:") as connection:
        write_personal_score_db_save(connection, adapter.save_input)
        assert connection.execute(
            "SELECT flare_rank FROM plays WHERE play_id = ?", ("play-issue-100",)
        ).fetchone() == (None,)


def test_formal_score_rejects_non_ten_step_value() -> None:
    formal = replace(_formal_values(flare_rank=None), score=123)
    adapter = adapt_personal_score_db_save_input(_adapter_input(formal))
    assert adapter.status == "unresolved"
    assert "play.score_not_multiple_of_10" in adapter.reasons


def test_formal_flare_rank_rejects_values_outside_issue_100_set() -> None:
    formal = replace(_formal_values(flare_rank=None), flare_rank="X")
    adapter = adapt_personal_score_db_save_input(_adapter_input(formal))
    assert adapter.status == "unresolved"
    assert "play.flare_rank_invalid" in adapter.reasons


def test_flare_evidence_is_connected_only_when_recognized(tmp_path: Path) -> None:
    values = _formal_values(flare_rank="IX")
    sources = {
        "play_id": "capture_event_v1",
        "played_at": "capture_utc",
        "master_version": "master_metadata",
        "song_id": "m5_adopted_identity",
        "chart_id": "m5_adopted_identity",
        "score": "m7a_adopted_profile",
        "max_combo": "m7a_adopted_profile",
        "marvelous": "m7a_adopted_profile",
        "perfect": "m7a_adopted_profile",
        "great": "m7a_adopted_profile",
        "good": "m7a_adopted_profile",
        "miss": "m7a_adopted_profile",
        "ex_score": "m7a_adopted_profile",
        "rank": "adopted_rank_recognizer",
        "clear_type": "adopted_clear_type_recognizer",
        "flare_rank": "adopted_flare_rank_recognizer",
        "duplicate_key": "capture_event_v1",
    }
    evidence = AutomaticFormalEvidence(
        values=values,
        sources=sources,
        confidences={field_name: 0.99 for field_name in sources},
    )
    image = tmp_path / "frame.png"
    Image.new("RGB", (8, 8), "black").save(image)
    event = CaptureAnalyzedEvent(
        frame_index=0,
        manifest_image_path="frame.png",
        image_path=image,
        captured_at="2026-07-28T12:00:00+09:00",
        timestamp_ms=0,
        candidate_duration_ms=1000,
        event_type="confirmed",
        confirmed_result=True,
        duplicate=False,
        confirmation_mode="frames",
        identity_signal_status="reviewed",
        digit_review_status="reviewed",
        analysis_confidence=0.99,
        candidate_material={"flare_rank": "raw-candidate"},
        formal_evidence=evidence,
    )
    formal, reasons = promote_automatic_formal_values(event)
    assert reasons == ()
    assert formal is not None and formal.flare_rank == "IX"
