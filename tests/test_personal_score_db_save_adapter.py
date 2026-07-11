from __future__ import annotations

from dataclasses import replace

from tools.vision_poc import personal_score_db_save_adapter as adapter


def adapter_input() -> adapter.PersonalScoreDbSaveAdapterInput:
    return adapter.PersonalScoreDbSaveAdapterInput(
        candidate_material={
            "payload_preview_status": "payload_ready",
            "identity_signal_song_id": "candidate-song",
            "identity_signal_chart_id": "candidate-chart",
            "score_digits": "111111",
            "played_at_ms": "0",
        },
        capture_id="capture-one",
        capture_hash="sha256:one",
        captured_at="2026-07-11T12:34:56+09:00",
        source_kind="manifest",
        source_path="samples/one.png",
        manifest_image_path="frames/one.png",
        frame_index=12,
        analysis_id="analysis-one",
        event_type="confirmed",
        confirmed_result=True,
        duplicate=False,
        confirmation_mode="time",
        timestamp_ms=1_234,
        candidate_duration_ms=1_000,
        identity_signal_status="composite_resolved_candidate",
        digit_review_status="reviewed",
        analysis_confidence=0.98,
        analysis_summary_json='{"contract": "formal-save-adapter-v1"}',
        app_version="0.1.0",
        formal_play=adapter.PersonalScoreDbFormalPlayValues(
            play_id="play-one",
            played_at="2026-07-11T12:34:56+09:00",
            master_version="2026-07-11",
            song_id="formal-song",
            chart_id="formal-chart",
            score=987_650,
            max_combo=456,
            marvelous=400,
            perfect=40,
            great=10,
            good=4,
            miss=2,
            ex_score=1_750,
            rank="AAA",
            clear_type="CLEAR",
            duplicate_key="play:v1:one",
        ),
    )


def test_adapter_returns_ready_from_explicit_formal_values_only() -> None:
    result = adapter.adapt_personal_score_db_save_input(adapter_input())

    assert result.status == "ready"
    assert result.reasons == ()
    assert result.save_input is not None
    assert result.save_input.play is not None
    assert result.save_input.play.song_id == "formal-song"
    assert result.save_input.play.chart_id == "formal-chart"
    assert result.save_input.play.score == 987_650
    assert result.save_input.play.played_at == "2026-07-11T12:34:56+09:00"


def test_adapter_does_not_promote_preview_ids_digits_or_relative_time() -> None:
    request = adapter_input()
    assert request.formal_play is not None
    request = replace(
        request,
        formal_play=replace(
            request.formal_play,
            played_at="",
            song_id="",
            chart_id="",
            score=None,
        ),
    )

    result = adapter.adapt_personal_score_db_save_input(request)

    assert result.status == "unresolved"
    assert result.save_input is None
    assert "formal_play.played_at_required" in result.reasons
    assert "formal_play.song_id_required" in result.reasons
    assert "formal_play.chart_id_required" in result.reasons
    assert "formal_play.score_required" in result.reasons


def test_adapter_does_not_default_rank_clear_master_or_duplicate_key() -> None:
    request = adapter_input()
    assert request.formal_play is not None
    request = replace(
        request,
        formal_play=replace(
            request.formal_play,
            master_version="",
            rank="",
            clear_type="",
            duplicate_key="",
        ),
    )

    result = adapter.adapt_personal_score_db_save_input(request)

    assert result.status == "unresolved"
    assert result.save_input is None
    assert result.reasons == (
        "formal_play.master_version_required",
        "formal_play.rank_required",
        "formal_play.clear_type_required",
        "formal_play.duplicate_key_required",
    )


def test_adapter_keeps_invalid_formal_values_unresolved() -> None:
    request = adapter_input()
    assert request.formal_play is not None
    request = replace(
        request,
        captured_at="2026-07-11T12:34:56",
        formal_play=replace(
            request.formal_play,
            played_at="2026-07-11T12:34:56",
            duplicate_key="score:987650",
        ),
    )

    result = adapter.adapt_personal_score_db_save_input(request)

    assert result.status == "unresolved"
    assert result.save_input is None
    assert "source_capture.captured_at_timezone_required" in result.reasons
    assert "play.played_at_timezone_required" in result.reasons
    assert "play.duplicate_key_uses_preview_format" in result.reasons


def test_adapter_returns_duplicate_exclusion_without_play() -> None:
    request = replace(adapter_input(), duplicate=True)

    result = adapter.adapt_personal_score_db_save_input(request)

    assert result.status == "excluded"
    assert result.reasons == ("duplicate_result",)
    assert result.save_input is not None
    assert result.save_input.play is None
    assert result.save_input.analysis.analysis_status == "skipped"
    assert result.save_input.analysis.save_boundary_status == "duplicate"
    assert result.save_input.analysis.duplicate


def test_adapter_returns_low_confidence_exclusion_without_play() -> None:
    request = replace(
        adapter_input(),
        formal_play=None,
        analysis_confidence=0.42,
        exclusion=adapter.PersonalScoreDbSaveExclusion(
            kind="low_confidence",
            reason="confidence_below_reviewed_threshold",
        ),
    )

    result = adapter.adapt_personal_score_db_save_input(request)

    assert result.status == "excluded"
    assert result.save_input is not None
    assert result.save_input.play is None
    assert result.save_input.analysis.analysis_status == "low_confidence"
    assert result.save_input.analysis.skip_reason == (
        "confidence_below_reviewed_threshold"
    )


def test_adapter_requires_valid_exclusion_analysis_input() -> None:
    request = replace(
        adapter_input(),
        formal_play=None,
        app_version="",
        exclusion=adapter.PersonalScoreDbSaveExclusion(
            kind="skipped",
            reason="manual_review_required",
        ),
    )

    result = adapter.adapt_personal_score_db_save_input(request)

    assert result.status == "unresolved"
    assert result.save_input is None
    assert "analysis.app_version_required" in result.reasons
