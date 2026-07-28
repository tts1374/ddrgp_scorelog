from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass

import numpy as np
from PIL import Image

RESULT_RANK_VALUES = (
    "AAA",
    "AA+",
    "AA",
    "AA-",
    "A+",
    "A",
    "A-",
    "B+",
    "B",
    "B-",
    "C+",
    "C",
    "C-",
    "D+",
    "D",
    "E",
)
RESULT_CLEAR_TYPE_VALUES = (
    "FAILED",
    "MFC",
    "PFC",
    "GFC",
    "FULL COMBO",
    "CLEAR",
)
RESULT_FLARE_RANK_VALUES = (
    "I",
    "II",
    "III",
    "IV",
    "V",
    "VI",
    "VII",
    "VIII",
    "IX",
    "EX",
)
RESULT_JUDGMENT_COUNT_FIELDS = (
    "marvelous",
    "perfect",
    "great",
    "good",
    "ok",
    "miss",
)

RANK_FORMAL_EVIDENCE_SOURCE = "adopted_rank_recognizer"
CLEAR_TYPE_FORMAL_EVIDENCE_SOURCE = "adopted_clear_type_recognizer"
FLARE_RANK_FORMAL_EVIDENCE_SOURCE = "adopted_flare_rank_recognizer"


@dataclass(frozen=True)
class RankRoiRecognition:
    status: str
    is_failed: bool | None
    confidence: float | None
    reason: str

    @property
    def formal_rank(self) -> str | None:
        return "E" if self.is_failed is True else None


@dataclass(frozen=True)
class FlareRankRecognition:
    status: str
    value: str | None
    confidence: float | None
    reason: str


@dataclass(frozen=True)
class ResultFieldRecognition:
    rank: str | None
    rank_status: str
    rank_confidence: float | None
    rank_reason: str
    clear_type: str | None
    clear_type_status: str
    clear_type_confidence: float | None
    clear_type_reason: str
    flare_rank: str | None
    flare_rank_status: str
    flare_rank_confidence: float | None
    flare_rank_reason: str


def rank_from_score(score: object) -> str | None:
    """Return the Issue #100 rank for a valid formal DDR score."""
    if isinstance(score, bool) or not isinstance(score, int):
        return None
    if score < 0 or score > 1_000_000 or score % 10 != 0:
        return None
    if score >= 990_000:
        return "AAA"
    if score >= 950_000:
        return "AA+"
    if score >= 900_000:
        return "AA"
    if score >= 890_000:
        return "AA-"
    if score >= 850_000:
        return "A+"
    if score >= 800_000:
        return "A"
    if score >= 790_000:
        return "A-"
    if score >= 750_000:
        return "B+"
    if score >= 700_000:
        return "B"
    if score >= 690_000:
        return "B-"
    if score >= 650_000:
        return "C+"
    if score >= 600_000:
        return "C"
    if score >= 590_000:
        return "C-"
    if score >= 550_000:
        return "D+"
    return "D"


def calculate_rank_from_score(score: object) -> str | None:
    """Descriptive alias for callers that make the score-derived rule explicit."""
    return rank_from_score(score)


def clear_type_from_counts(
    counts: Mapping[str, object],
    *,
    failed: bool | None,
) -> str | None:
    """Derive clear_type from all six judgment counts, never from UI effects."""
    if failed is True:
        return "FAILED"
    if failed is not False:
        return None

    normalized: dict[str, int] = {}
    for field_name in RESULT_JUDGMENT_COUNT_FIELDS:
        value = counts.get(field_name)
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            return None
        normalized[field_name] = value

    if all(normalized[field_name] == 0 for field_name in ("perfect", "great", "good", "miss")):
        return "MFC"
    if all(normalized[field_name] == 0 for field_name in ("great", "good", "miss")):
        return "PFC"
    if all(normalized[field_name] == 0 for field_name in ("good", "miss")):
        return "GFC"
    if normalized["miss"] == 0:
        return "FULL COMBO"
    return "CLEAR"


def calculate_clear_type(
    counts: Mapping[str, object],
    *,
    failed: bool | None,
) -> str | None:
    """Descriptive alias for the count-only clear type rule."""
    return clear_type_from_counts(counts, failed=failed)


def recognize_rank_roi(image: Image.Image) -> RankRoiRecognition:
    """Classify only FAILED/E versus formally non-FAILED in the rank ROI.

    Normal rank glyphs are intentionally not decoded here.  The visual gate uses
    the stable white FAILED glyph shape versus a saturated normal-rank glyph; an
    unclear ROI remains unresolved so score cannot imply FAILED.
    """
    rgb = np.asarray(image.convert("RGB")).astype(np.float32)
    if rgb.size == 0:
        return RankRoiRecognition("ambiguous", None, None, "empty_rank_roi")

    red, green, blue = (rgb[:, :, index] for index in range(3))
    luma = red * 0.299 + green * 0.587 + blue * 0.114
    channel_spread = rgb.max(axis=2) - rgb.min(axis=2)
    white = (
        (luma > 180)
        & (channel_spread < 55)
        & (red > 175)
        & (green > 175)
        & (blue > 175)
    )
    yellow = (
        (red > 150)
        & (green > 115)
        & (blue < 155)
        & (red > blue * 1.35)
    )
    maximum = rgb.max(axis=2)
    minimum = rgb.min(axis=2)
    delta = maximum - minimum
    saturation = np.zeros_like(maximum)
    np.divide(delta, maximum, out=saturation, where=maximum != 0)
    chromatic = (saturation >= 0.25) & (maximum >= 64) & (luma >= 50)
    white_ratio = float(white.mean())
    yellow_ratio = float(yellow.mean())
    chromatic_ratio = float(chromatic.mean())
    foreground_ratio = float((white | yellow | chromatic).mean())

    if foreground_ratio < 0.035:
        return RankRoiRecognition(
            "ambiguous", None, None, "rank_glyph_not_detected"
        )
    e_shape_score = _rank_e_shape_score(white)
    if (
        e_shape_score is not None
        and yellow_ratio <= 0.10
        and chromatic_ratio <= 0.20
    ):
        return RankRoiRecognition(
            "failed",
            True,
            min(1.0, 0.985 + min(0.015, max(0.0, e_shape_score - 0.55) * 0.05)),
            "failed_e_glyph_shape",
        )
    if (yellow_ratio >= 0.12 or chromatic_ratio >= 0.20) and white_ratio <= 0.10:
        normal_strength = max(yellow_ratio / 0.12, chromatic_ratio / 0.20)
        margin = max(0.0, min(1.0, normal_strength - 1.0))
        return RankRoiRecognition(
            "non_failed",
            False,
            min(1.0, 0.985 + max(0.0, min(1.0, margin)) * 0.015),
            "non_failed_rank_glyph",
        )
    return RankRoiRecognition("ambiguous", None, None, "rank_glyph_ambiguous")


def _rank_e_shape_score(white_mask: np.ndarray) -> float | None:
    """Return a shape-match score only for a sufficiently complete E glyph.

    A large white area is not enough: RESULT animations can cover much of the
    rank ROI.  Components are normalized to a small E template and checked for
    the E's stem and three horizontal bars, with a centered, rank-sized bbox.
    """
    roi_height, roi_width = white_mask.shape
    best_score: float | None = None
    templates = _rank_e_templates()
    components = _rank_white_components(white_mask)
    candidates = list(components)
    if len(components) > 1:
        candidates.append(_rank_white_component_union(components))
    for component, top, left in candidates:
        component_height, component_width = component.shape
        height_ratio = component_height / roi_height
        width_ratio = component_width / roi_width
        center_x = (left + component_width / 2) / roi_width
        center_y = (top + component_height / 2) / roi_height
        if (
            height_ratio < 0.45
            or width_ratio < 0.10
            or not 0.15 <= center_x <= 0.85
            or not 0.20 <= center_y <= 0.80
            or not 0.20 <= component_width / component_height <= 1.35
        ):
            continue

        occupancy = float(component.mean())
        if not 0.12 <= occupancy <= 0.78:
            continue

        normalized = np.asarray(
            Image.fromarray(component.astype(np.uint8) * 255).resize(
                (32, 48), resample=Image.Resampling.NEAREST
            )
        ) > 0
        iou = max(
            float(np.logical_and(normalized, template).sum())
            / float(np.logical_or(normalized, template).sum())
            for template in templates
        )
        if not np.isfinite(iou):
            continue
        row_occupancy = normalized.mean(axis=1)
        bar_presence = (
            float(row_occupancy[0:12].max()),
            float(row_occupancy[16:32].max()),
            float(row_occupancy[36:48].max()),
        )
        stem_presence = float(normalized[:, 2:10].mean())
        if iou < 0.55 or min(bar_presence) < 0.55 or stem_presence < 0.55:
            continue
        score = 0.70 * iou + 0.20 * min(bar_presence) + 0.10 * stem_presence
        best_score = score if best_score is None else max(best_score, score)
    return best_score


def _rank_e_template() -> np.ndarray:
    template = np.zeros((48, 32), dtype=bool)
    template[:, 2:10] = True
    template[0:9, 2:30] = True
    template[19:29, 2:26] = True
    template[39:48, 2:30] = True
    return template


def _rank_e_disconnected_template() -> np.ndarray:
    template = np.zeros((48, 32), dtype=bool)
    template[:, 0:10] = True
    template[0:12, 14:32] = True
    template[16:32, 14:32] = True
    template[36:48, 14:32] = True
    return template


def _rank_e_templates() -> tuple[np.ndarray, np.ndarray]:
    return _rank_e_template(), _rank_e_disconnected_template()


def _rank_white_component_union(
    components: list[tuple[np.ndarray, int, int]],
) -> tuple[np.ndarray, int, int]:
    top = min(component_top for _component, component_top, _left in components)
    left = min(component_left for _component, _top, component_left in components)
    bottom = max(
        component_top + component.shape[0] - 1
        for component, component_top, _left in components
    )
    right = max(
        component_left + component.shape[1] - 1
        for component, _top, component_left in components
    )
    union = np.zeros((bottom - top + 1, right - left + 1), dtype=bool)
    for component, component_top, component_left in components:
        top_offset = component_top - top
        left_offset = component_left - left
        union[
            top_offset : top_offset + component.shape[0],
            left_offset : left_offset + component.shape[1],
        ] |= component
    return union, top, left


def _rank_white_components(
    white_mask: np.ndarray,
) -> list[tuple[np.ndarray, int, int]]:
    """Return connected white components as cropped masks with top/left offsets."""
    height, width = white_mask.shape
    visited = np.zeros_like(white_mask, dtype=bool)
    components: list[tuple[np.ndarray, int, int]] = []
    for start_y, start_x in zip(*np.nonzero(white_mask), strict=True):
        if visited[start_y, start_x]:
            continue
        stack = [(int(start_y), int(start_x))]
        visited[start_y, start_x] = True
        points: list[tuple[int, int]] = []
        while stack:
            y, x = stack.pop()
            points.append((y, x))
            for offset_y in (-1, 0, 1):
                for offset_x in (-1, 0, 1):
                    if offset_y == 0 and offset_x == 0:
                        continue
                    neighbor_y = y + offset_y
                    neighbor_x = x + offset_x
                    if not (
                        0 <= neighbor_y < height
                        and 0 <= neighbor_x < width
                        and white_mask[neighbor_y, neighbor_x]
                        and not visited[neighbor_y, neighbor_x]
                    ):
                        continue
                    visited[neighbor_y, neighbor_x] = True
                    stack.append((neighbor_y, neighbor_x))
        if len(points) < 24:
            continue
        coordinates = np.asarray(points, dtype=np.int32)
        top = int(coordinates[:, 0].min())
        left = int(coordinates[:, 1].min())
        bottom = int(coordinates[:, 0].max())
        right = int(coordinates[:, 1].max())
        component = np.zeros((bottom - top + 1, right - left + 1), dtype=bool)
        component[coordinates[:, 0] - top, coordinates[:, 1] - left] = True
        components.append((component, top, left))
    return components


def recognize_failed_rank(image: Image.Image) -> RankRoiRecognition:
    """Compatibility name for the rank-ROI E/non-E classifier."""
    return recognize_rank_roi(image)


def recognize_flare_rank(image: Image.Image) -> FlareRankRecognition:
    """Recognize the independent FLARE badge by its palette and silhouette.

    The badge has a stable saturated-color footprint even when its Roman numeral
    is stylized.  Values are accepted only when the footprint and palette are
    sufficiently distinct; an absent or ambiguous badge returns null evidence.
    """
    rgb = np.asarray(image.convert("RGB")).astype(np.float32)
    if rgb.size == 0:
        return FlareRankRecognition("unrecognized", None, None, "empty_flare_roi")
    hue, saturation, value = _rgb_to_hsv(rgb)
    colored = (saturation >= 0.45) & (value >= 0.35)
    area_ratio = float(colored.mean())
    if area_ratio < 0.095:
        return FlareRankRecognition("unrecognized", None, None, "flare_badge_not_detected")

    hues = hue[colored]
    values = value[colored]
    histogram, _ = np.histogram(hues, bins=np.arange(0.0, 361.0, 15.0))
    active_bins = int((histogram >= max(8, int(len(hues) * 0.01))).sum())
    dominant_bin = int(histogram.argmax())
    dominant_hue = dominant_bin * 15.0 + 7.5
    dominant_ratio = float(histogram[dominant_bin] / len(hues))
    median_value = float(np.median(values) * 255.0)

    if median_value >= 245.0 and active_bins >= 8:
        return FlareRankRecognition(
            "recognized",
            "EX",
            min(1.0, 0.99 + min(0.01, (area_ratio - 0.19) / 10.0)),
            "flare_ex_palette",
        )

    profiles = {
        "I": (239.0, 52.5),
        "II": (231.0, 37.5),
        "III": (222.0, 37.5),
        "IV": (220.0, 22.5),
        "V": (209.0, 7.5),
        "VI": (200.0, 352.5),
        "VII": (156.0, 337.5),
        "VIII": (123.0, 337.5),
        "IX": (107.0, 307.5),
    }
    distances = {
        flare: _flare_profile_distance(
            median_value,
            dominant_hue,
            profile_value,
            profile_hue,
        )
        for flare, (profile_value, profile_hue) in profiles.items()
    }
    ordered = sorted(distances.items(), key=lambda item: item[1])
    best_flare, best_distance = ordered[0]
    second_distance = ordered[1][1]
    margin = second_distance - best_distance
    if best_distance > 1.75 or margin < 0.10 or dominant_ratio < 0.20:
        return FlareRankRecognition(
            "unrecognized", None, None, "flare_palette_ambiguous"
        )
    confidence = min(1.0, 0.985 + min(0.015, margin / 6.0))
    return FlareRankRecognition(
        "recognized",
        best_flare,
        confidence,
        "flare_palette_recognized",
    )


def recognize_result_fields(
    *,
    rank_roi: Image.Image,
    flare_roi: Image.Image,
    score: object,
    judgment_counts: Mapping[str, object],
) -> ResultFieldRecognition:
    rank_evidence = recognize_rank_roi(rank_roi)
    flare_evidence = recognize_flare_rank(flare_roi)

    if rank_evidence.status == "failed":
        rank = "E"
        rank_status = "recognized"
        rank_reason = rank_evidence.reason
        clear_type = "FAILED"
        clear_status = "recognized"
        clear_reason = "failed_rank_has_priority"
        clear_confidence = rank_evidence.confidence
    elif rank_evidence.status == "non_failed":
        rank = rank_from_score(score)
        rank_status = "recognized" if rank is not None else "unresolved"
        rank_reason = "score_threshold" if rank is not None else "invalid_score_evidence"
        clear_type = clear_type_from_counts(judgment_counts, failed=False)
        clear_status = "recognized" if clear_type is not None else "unresolved"
        clear_reason = "judgment_counts" if clear_type is not None else "judgment_counts_incomplete"
        clear_confidence = 0.99 if clear_type is not None else None
        return ResultFieldRecognition(
            rank,
            rank_status,
            rank_evidence.confidence if rank is not None else None,
            rank_reason,
            clear_type,
            clear_status,
            clear_confidence,
            clear_reason,
            flare_evidence.value,
            flare_evidence.status,
            flare_evidence.confidence,
            flare_evidence.reason,
        )
    else:
        rank = None
        rank_status = "unresolved"
        rank_reason = "rank_evidence_ambiguous"
        clear_type = None
        clear_status = "unresolved"
        clear_reason = "rank_evidence_required"
        clear_confidence = None

    return ResultFieldRecognition(
        rank,
        rank_status,
        rank_evidence.confidence if rank is not None else None,
        rank_reason,
        clear_type,
        clear_status,
        clear_confidence,
        clear_reason,
        flare_evidence.value,
        flare_evidence.status,
        flare_evidence.confidence,
        flare_evidence.reason,
    )


def _flare_profile_distance(
    observed_value: float,
    observed_hue: float,
    profile_value: float,
    profile_hue: float,
) -> float:
    value_distance = (observed_value - profile_value) / 18.0
    hue_delta = abs(observed_hue - profile_hue) % 360.0
    hue_distance = min(hue_delta, 360.0 - hue_delta) / 18.0
    return float(np.hypot(value_distance, hue_distance))


def _rgb_to_hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    normalized = rgb / 255.0
    red, green, blue = (normalized[:, :, index] for index in range(3))
    maximum = normalized.max(axis=2)
    minimum = normalized.min(axis=2)
    delta = maximum - minimum
    hue = np.zeros_like(maximum)
    nonzero = delta > 1e-6
    red_maximum = nonzero & (maximum == red)
    green_maximum = nonzero & (maximum == green)
    blue_maximum = nonzero & (maximum == blue)
    hue[red_maximum] = ((green[red_maximum] - blue[red_maximum]) / delta[red_maximum]) % 6
    hue[green_maximum] = (
        (blue[green_maximum] - red[green_maximum]) / delta[green_maximum] + 2
    )
    hue[blue_maximum] = (
        (red[blue_maximum] - green[blue_maximum]) / delta[blue_maximum] + 4
    )
    hue *= 60.0
    saturation = np.zeros_like(maximum)
    np.divide(delta, maximum, out=saturation, where=maximum != 0)
    return hue, saturation, maximum
