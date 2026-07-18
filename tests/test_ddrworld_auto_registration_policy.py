from __future__ import annotations

import json

from tools.ddrworld_snapshot_evaluation.evaluator import (
    MasterSong,
    TruthObservation,
    build_master_indexes,
)
from tools.ddrworld_snapshot_evaluation.policy import (
    evaluate_policy_rows,
    summarize_policy,
)
from tools.vision_poc import master_match


def _song(song_id: str, title: str, artist: str = "ARTIST") -> MasterSong:
    return MasterSong(song_id, title, artist, False)


def _observation(number: int, song_id: str, *, rejected: bool = False) -> TruthObservation:
    return TruthObservation(
        audit_no=number,
        observation_id=f"obs-{number}",
        review_status="rejected" if rejected else "confirmed",
        truth_song_id="" if rejected else song_id,
        truth_title="" if rejected else song_id,
        truth_artist="" if rejected else "ARTIST",
    )


def _field(raw: str, *, status: str = "ok") -> dict[str, object]:
    return {
        "raw": raw,
        "normalized": master_match.normalize_song_title(raw),
        "confidence": 0.9,
        "status": status,
        "failure_reason": "" if status == "ok" else "empty_ocr",
    }


def _ocr_row(
    configuration: str,
    title: str,
    artist: str = "ARTIST",
    *,
    title_status: str = "ok",
    artist_status: str = "ok",
) -> dict[str, object]:
    profile = {
        "one": "tesseract-autocontrast-v1",
        "two": "tesseract-white-threshold-v1",
    }.get(configuration, configuration)
    return {
        "configuration_id": profile,
        "title_profile_id": profile,
        "artist_profile_id": profile,
        "title": _field(title, status=title_status),
        "artist": _field(artist, status=artist_status),
    }


def _jacket_row(number: int, status: str, candidates: list[str]) -> dict[str, object]:
    return {
        "observation_id": f"obs-{number}",
        "truth_official_snapshot_available": True,
        "top_song_id": candidates[0],
        "top_distance": 0.1,
        "top_margin": 0.01,
        "decision_status": status,
        "top_candidates": json.dumps(
            [
                {"song_id": song_id, "distance": 0.1 + index / 100}
                for index, song_id in enumerate(candidates)
            ]
        ),
    }


def test_policy_routes_cover_normal_conflict_missing_ambiguous_top3_and_rejected() -> None:
    songs = {
        song.song_id: song
        for song in (
            _song("A", "A"),
            _song("B", "B"),
            _song("C", "C"),
            _song("D", "D"),
            _song("PAIR", "PAIR"),
            _song("AMB1", "AMB"),
            _song("AMB2", "AMB"),
        )
    }
    indexes = build_master_indexes(songs, [])
    observations = [
        _observation(1, "A", rejected=True),
        _observation(2, "A"),
        _observation(3, "B"),
        _observation(4, "PAIR"),
        _observation(5, "A"),
        _observation(6, "PAIR"),
        _observation(7, "AMB1"),
        _observation(8, "C"),
    ]
    jackets = [
        _jacket_row(2, "matched_correct", ["A", "B", "C"]),
        _jacket_row(3, "hold_ambiguous", ["A", "B", "C"]),
        _jacket_row(4, "hold_truth_not_in_snapshot", ["A", "B", "C"]),
        _jacket_row(5, "hold_ambiguous", ["A", "B", "C"]),
        _jacket_row(6, "hold_truth_not_in_snapshot", ["A", "B", "C"]),
        _jacket_row(7, "hold_ambiguous", ["A", "B", "C"]),
        _jacket_row(8, "hold_ambiguous", ["A", "B", "D"]),
    ]
    ocr = {
        "obs-2": [_ocr_row("one", "A")],
        "obs-3": [_ocr_row("one", "B"), _ocr_row("two", "B")],
        "obs-4": [_ocr_row("one", "PAIR"), _ocr_row("two", "PAIR")],
        "obs-5": [_ocr_row("one", "A"), _ocr_row("two", "B")],
        "obs-6": [
            _ocr_row("one", "PAIR", artist_status="empty"),
            _ocr_row("two", "PAIR", artist_status="empty"),
        ],
        "obs-7": [_ocr_row("one", "AMB"), _ocr_row("two", "AMB")],
        "obs-8": [_ocr_row("one", "C"), _ocr_row("two", "C")],
    }

    rows = evaluate_policy_rows(
        observations,
        jackets,
        ocr,
        indexes,
        {"A", "B", "C", "D"},
        snapshot_id="fixture",
    )

    assert [row["policy_decision"] for row in rows] == [
        "rejected_capture_mismatch",
        "auto_jacket_gate",
        "auto_jacket_top3_title_ocr",
        "auto_ocr_title_artist_pair",
        "manual_jacket_ocr_conflict",
        "manual_ocr_pair_incomplete",
        "manual_ocr_pair_ambiguous",
        "manual_jacket_top3_miss",
    ]
    metrics = summarize_policy(rows)
    assert metrics["auto_decisions"] == 3
    assert metrics["correct_decisions"] == 3
    assert metrics["false_decisions"] == 0
    assert metrics["manual_review_remaining"] == 4
    assert metrics["capture_mismatch_count"] == 1


def test_false_auto_decision_is_counted_and_enumerable() -> None:
    songs = {song.song_id: song for song in (_song("A", "A"), _song("B", "B"))}
    rows = evaluate_policy_rows(
        [_observation(1, "B")],
        [_jacket_row(1, "matched_false", ["A", "B"])],
        {"obs-1": [_ocr_row("one", "B")]},
        build_master_indexes(songs, []),
        set(songs),
        snapshot_id="fixture",
    )

    assert rows[0]["policy_decision"] == "auto_jacket_gate"
    assert rows[0]["outcome"] == "false"
    assert summarize_policy(rows)["by_route"]["auto_jacket_gate"]["false_decisions"] == 1


def test_missing_ocr_method_forces_manual_other() -> None:
    songs = {song.song_id: song for song in (_song("A", "A"), _song("B", "B"))}
    rows = evaluate_policy_rows(
        [_observation(1, "B")],
        [_jacket_row(1, "hold_ambiguous", ["A", "B"])],
        {"obs-1": [_ocr_row("one", "B")]},
        build_master_indexes(songs, []),
        set(songs),
        snapshot_id="fixture",
    )

    assert rows[0]["policy_decision"] == "manual_other"
    assert rows[0]["hold_reason"] == "ocr_profile_set_mismatch"
