from __future__ import annotations

from typing import Any

from PIL import Image

from . import master_match
from .runner import (
    ROI_DEFINITIONS,
    TESSERACT_CONFIG,
    canonical_ocr_digits,
    classify,
    crop_roi,
    normalize_digits,
    preprocess_score_roi,
    run_tesseract,
)

LIVE_SCORE_MAX = 1_000_000


def analyze_live_result(image: Image.Image) -> dict[str, Any]:
    """Analyze one in-memory frame for the live RESULT gate.

    This deliberately returns only a small observation. The full result workflow remains the
    existing capture-save path and is invoked only after the C# side confirms two samples.
    """
    classification = classify(
        image,
        {"organized_file": "live_result.png", "screen_type": "result"},
    )
    if not classification.header_signal.value:
        return {
            "result_screen": False,
            "score": "",
            "title_signature": "",
            "reason": "results_header_not_detected",
            "score_status": "not_result",
        }

    _, _, score_binary = preprocess_score_roi(image)
    raw, engine, status, error = run_tesseract(
        score_binary,
        "score_digits",
        TESSERACT_CONFIG,
    )
    normalized = normalize_digits(raw)
    score = canonical_ocr_digits(normalized)
    if not score:
        return {
            "result_screen": True,
            "score": "",
            "title_signature": "",
            "reason": error or "score_not_recognized",
            "score_status": status,
            "score_engine": engine,
        }
    if int(score) > LIVE_SCORE_MAX:
        return {
            "result_screen": True,
            "score": "",
            "title_signature": "",
            "reason": "score_out_of_range",
            "score_status": "out_of_range",
            "score_engine": engine,
        }

    title_feature = master_match.extract_title_image_feature(
        crop_roi(image, ROI_DEFINITIONS["song_title"])
    )
    title_signature = "|".join(
        (
            title_feature.dhash_hex,
            *title_feature.linehash_rows,
        )
    )
    return {
        "result_screen": True,
        "score": score,
        "title_signature": title_signature,
        "reason": "result_score_detected",
        "score_status": status,
        "score_engine": engine,
    }
