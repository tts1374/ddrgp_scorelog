from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

from PIL import Image

from tests.test_capture_save_workflow import _event
from tools.vision_poc import capture_save_workflow_app, live_result
from tools.vision_poc.capture_save_workflow import CaptureSaveSessionResult


def _signal(value: bool) -> SimpleNamespace:
    return SimpleNamespace(value=value)


def _score_binary() -> tuple[Image.Image, Image.Image, Image.Image]:
    return (
        Image.new("L", (10, 10)),
        Image.new("L", (10, 10)),
        Image.new("L", (10, 10)),
    )


def test_live_result_requires_existing_result_screen_signal(
    monkeypatch: object,
) -> None:
    classification = SimpleNamespace(
        result_candidate=False,
        header_signal=_signal(False),
        detail_panel_signal=_signal(False),
    )
    monkeypatch.setattr(live_result, "classify", lambda image, row: classification)  # type: ignore[attr-defined]

    result = live_result.analyze_live_result(Image.new("RGB", (1280, 720)))

    assert result == {
        "result_screen": False,
        "score": "",
        "title_signature": "",
        "reason": "results_header_not_detected",
        "score_status": "not_result",
    }


def test_live_result_returns_canonical_score_and_title_roi_signature(
    monkeypatch: object,
) -> None:
    classification = SimpleNamespace(
        result_candidate=False,
        header_signal=_signal(True),
        detail_panel_signal=_signal(False),
    )
    feature = SimpleNamespace(dhash_hex="abcd", linehash_rows=("f0", "0f"))
    monkeypatch.setattr(live_result, "classify", lambda image, row: classification)  # type: ignore[attr-defined]
    monkeypatch.setattr(  # type: ignore[attr-defined]
        live_result,
        "preprocess_score_roi",
        lambda image: _score_binary(),
    )
    monkeypatch.setattr(  # type: ignore[attr-defined]
        live_result,
        "run_tesseract",
        lambda binary, roi_name, config: ("001234", "tesseract", "ok", ""),
    )
    monkeypatch.setattr(  # type: ignore[attr-defined]
        live_result.master_match,
        "extract_title_image_feature",
        lambda image: feature,
    )

    result = live_result.analyze_live_result(Image.new("RGB", (1280, 720)))

    assert result["result_screen"] is True
    assert result["score"] == "1234"
    assert result["title_signature"] == "abcd|f0|0f"
    assert result["reason"] == "result_score_detected"


def test_transient_capture_event_does_not_retain_image_reference(tmp_path: Path) -> None:
    from tools.vision_poc.capture_save_workflow import run_capture_save_events

    manifest = tmp_path / "frame_manifest.csv"
    manifest.write_text("image_path,timestamp_ms\nframe.png,2000\n", encoding="utf-8")
    db_path = tmp_path / "score.sqlite"

    result = run_capture_save_events(
        [_event(tmp_path)],
        manifest_path=manifest,
        db_path=db_path,
        source_path="live-memory://candidate-1",
        retain_image_reference=False,
    )[0]

    assert result.event_status == "saved"
    import sqlite3

    with sqlite3.connect(db_path) as connection:
        source = connection.execute(
            "SELECT source_path, manifest_image_path FROM source_captures"
        ).fetchone()
    assert source == ("live-memory://candidate-1", "")


def test_transient_app_forwards_output_and_logical_source(
    tmp_path: Path, monkeypatch: object
) -> None:
    captured: dict[str, object] = {}

    def fake_run_capture_save_session(**kwargs: object) -> CaptureSaveSessionResult:
        captured.update(kwargs)
        return CaptureSaveSessionResult(
            "completed",
            kwargs["output_dir"],  # type: ignore[arg-type]
            (),
        )

    monkeypatch.setattr(
        capture_save_workflow_app,
        "run_capture_save_session",
        fake_run_capture_save_session,
    )

    output_dir = tmp_path / "analysis"
    exit_code = capture_save_workflow_app.main(
        [
            "--manifest",
            str(tmp_path / "manifest.csv"),
            "--master-database",
            str(tmp_path / "master.sqlite"),
            "--database",
            str(tmp_path / "score.sqlite"),
            "--output",
            str(output_dir),
            "--transient-source",
            "live-memory://candidate-2",
        ]
    )

    assert exit_code == 0
    assert captured["output_dir"] == output_dir.resolve()
    assert captured["source_path"] == "live-memory://candidate-2"
    assert captured["retain_image_reference"] is False
