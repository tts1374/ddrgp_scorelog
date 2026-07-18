from __future__ import annotations

import csv
import json
import shutil
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .evaluator import (
    FEATURE_EXTRACTOR_VERSION,
    JACKET_FEATURE_VERSION,
    EvaluationError,
    SnapshotSong,
    TruthObservation,
    build_master_indexes,
    evaluate_rows,
    load_catalog_features,
    load_master,
    load_snapshot,
    load_snapshot_features,
    load_truth,
    normalize,
    read_json,
    resolve_snapshot_song,
    sha256_file,
    verify_snapshot_images_unchanged,
    write_csv,
)

POLICY_VERSION = "jacket-auto-registration-read-only-policy-v1"
SUMMARY_SCHEMA = "jacket-auto-registration-evaluation-summary-v1"
KNOWN_OCR_PROFILES = {
    "tesseract-autocontrast-v1",
    "tesseract-white-threshold-v1",
}
AUTO_ROUTES = (
    "auto_jacket_gate",
    "auto_jacket_top3_title_ocr",
    "auto_ocr_title_artist_pair",
)
POLICY_ROUTES = (
    "rejected_capture_mismatch",
    *AUTO_ROUTES,
    "manual_jacket_ocr_unresolved",
    "manual_jacket_ocr_conflict",
    "manual_jacket_top3_miss",
    "manual_ocr_pair_incomplete",
    "manual_ocr_pair_ambiguous",
    "manual_other",
)


@dataclass(frozen=True)
class PolicyEvaluationConfig:
    snapshot: Path
    truth_ods: Path
    catalog: Path
    master: Path
    output: Path


@dataclass(frozen=True)
class StrictResolution:
    status: str
    song_ids: tuple[str, ...]
    match_kind: str


def add_artist_indexes(
    indexes: dict[str, dict[Any, set[str]]],
    songs: dict[str, Any],
    aliases: list[dict[str, str]],
) -> None:
    for name in (
        "canonical_exact_artist",
        "canonical_normalized_artist",
        "alias_exact_artist",
        "alias_normalized_artist",
    ):
        indexes[name] = defaultdict(set)
    for song in songs.values():
        indexes["canonical_exact_artist"][song.artist].add(song.song_id)
        indexes["canonical_normalized_artist"][normalize(song.artist)].add(song.song_id)
    for alias in aliases:
        indexes["alias_exact_artist"][alias["artist"]].add(alias["song_id"])
        indexes["alias_normalized_artist"][normalize(alias["artist"])].add(alias["song_id"])


def _safe_profiles(rows: list[dict[str, Any]], field: str) -> list[dict[str, Any]]:
    by_profile: dict[str, dict[str, Any]] = {}
    profile_key = f"{field}_profile_id"
    for row in rows:
        profile_id = str(row[profile_key])
        value = row[field]
        previous = by_profile.setdefault(profile_id, value)
        if previous != value:
            raise EvaluationError(
                f"OCR report repeats {profile_id} with inconsistent {field} values"
            )
    return [{"profile_id": profile_id, **value} for profile_id, value in sorted(by_profile.items())]


def load_truth_ocr_profiles(
    path: Path, expected_observation_ids: set[str]
) -> dict[str, list[dict[str, Any]]]:
    from .evaluator import load_ods_sheets, normalize, rows_as_dicts

    sheets = load_ods_sheets(path)
    if "Profile Details" not in sheets:
        raise EvaluationError("truth ODS is missing the Profile Details sheet")
    raw_rows = rows_as_dicts(sheets["Profile Details"], sheet_name="Profile Details")
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    required = {
        "observation_id",
        "method_version",
        "title_raw",
        "title_status",
        "artist_raw",
        "artist_status",
    }
    for index, row in enumerate(raw_rows):
        if not required <= set(row):
            raise EvaluationError(f"truth OCR profile row {index} is incomplete")
        observation_id = str(row.get("observation_id") or "")
        method = str(row.get("method_version") or "")
        if not observation_id or not method:
            raise EvaluationError(f"truth OCR profile row {index} has an invalid identity")
        values = {}
        for field in ("title", "artist"):
            raw = str(row.get(f"{field}_raw") or "")
            status = str(row.get(f"{field}_status") or "")
            if status not in {"ok", "empty", "low_confidence", "ocr_failed"}:
                raise EvaluationError(
                    f"truth OCR profile row {index} has unsupported {field} status"
                )
            values[field] = {
                "raw": raw,
                "normalized": normalize(raw),
                "confidence": row.get(f"{field}_confidence"),
                "status": status,
                "failure_reason": "" if status == "ok" else status,
            }
        grouped[observation_id].append(
            {
                "configuration_id": method,
                "title_profile_id": method,
                "artist_profile_id": method,
                **values,
            }
        )
    if set(grouped) != expected_observation_ids:
        raise EvaluationError("truth and Profile Details observation sets do not match")
    for observation_id, rows in grouped.items():
        methods = [str(row["configuration_id"]) for row in rows]
        if len(methods) != len(set(methods)):
            raise EvaluationError(f"truth OCR profiles repeat a method for {observation_id}")
    return dict(grouped)


def _strict_lookup(
    raw: str,
    normalized: str,
    indexes: dict[str, dict[Any, set[str]]],
    *,
    field: str,
    artist_raw: str = "",
    artist_normalized: str = "",
) -> StrictResolution:
    if not raw or not normalized:
        return StrictResolution("incomplete", (), "")
    suffix = field
    exact_key: Any = raw if field in {"title", "artist"} else (raw, artist_raw)
    normalized_key: Any = (
        normalized if field in {"title", "artist"} else (normalized, artist_normalized)
    )
    if field == "pair" and (not artist_raw or not artist_normalized):
        return StrictResolution("incomplete", (), "")
    for match_kind, names, key in (
        (
            "exact",
            (f"canonical_exact_{suffix}", f"alias_exact_{suffix}"),
            exact_key,
        ),
        (
            "safe_normalized",
            (f"canonical_normalized_{suffix}", f"alias_normalized_{suffix}"),
            normalized_key,
        ),
    ):
        song_ids: set[str] = set()
        for name in names:
            song_ids.update(indexes.get(name, {}).get(key, set()))
        if len(song_ids) == 1:
            return StrictResolution("resolved", tuple(sorted(song_ids)), match_kind)
        if len(song_ids) > 1:
            return StrictResolution("ambiguous", tuple(sorted(song_ids)), match_kind)
    return StrictResolution("unresolved", (), "")


def _resolve_ocr(
    rows: list[dict[str, Any]], indexes: dict[str, dict[Any, set[str]]]
) -> dict[str, Any]:
    title_profiles = _safe_profiles(rows, "title")
    artist_profiles = []
    for item in _safe_profiles(rows, "artist"):
        resolution = (
            _strict_lookup(item["raw"], item["normalized"], indexes, field="artist")
            if item["status"] == "ok"
            else StrictResolution("incomplete", (), "")
        )
        artist_profiles.append(
            {
                **item,
                "resolution_status": resolution.status,
                "candidate_song_ids": list(resolution.song_ids),
                "match_kind": resolution.match_kind,
            }
        )
    title_resolutions = []
    title_song_ids: set[str] = set()
    title_ambiguous = False
    for item in title_profiles:
        resolution = (
            _strict_lookup(item["raw"], item["normalized"], indexes, field="title")
            if item["status"] == "ok"
            else StrictResolution("incomplete", (), "")
        )
        title_song_ids.update(resolution.song_ids if resolution.status == "resolved" else ())
        title_ambiguous |= resolution.status == "ambiguous"
        title_resolutions.append(
            {
                **item,
                "resolution_status": resolution.status,
                "candidate_song_ids": list(resolution.song_ids),
                "match_kind": resolution.match_kind,
            }
        )

    pair_resolutions = []
    pair_song_ids: set[str] = set()
    pair_ambiguous = False
    pair_complete = False
    for row in sorted(rows, key=lambda item: str(item["configuration_id"])):
        title = row["title"]
        artist = row["artist"]
        if title["status"] == "ok" and artist["status"] == "ok":
            pair_complete = True
            resolution = _strict_lookup(
                title["raw"],
                title["normalized"],
                indexes,
                field="pair",
                artist_raw=artist["raw"],
                artist_normalized=artist["normalized"],
            )
        else:
            resolution = StrictResolution("incomplete", (), "")
        pair_song_ids.update(resolution.song_ids if resolution.status == "resolved" else ())
        pair_ambiguous |= resolution.status == "ambiguous"
        pair_resolutions.append(
            {
                "configuration_id": row["configuration_id"],
                "title_profile_id": row["title_profile_id"],
                "artist_profile_id": row["artist_profile_id"],
                "title_raw": title["raw"],
                "title_normalized": title["normalized"],
                "artist_raw": artist["raw"],
                "artist_normalized": artist["normalized"],
                "resolution_status": resolution.status,
                "candidate_song_ids": list(resolution.song_ids),
                "match_kind": resolution.match_kind,
            }
        )
    return {
        "title_profiles": title_resolutions,
        "artist_profiles": artist_profiles,
        "title_song_ids": tuple(sorted(title_song_ids)),
        "title_conflict": len(title_song_ids) > 1,
        "title_ambiguous": title_ambiguous,
        "pair_resolutions": pair_resolutions,
        "pair_song_ids": tuple(sorted(pair_song_ids)),
        "pair_conflict": len(pair_song_ids) > 1,
        "pair_ambiguous": pair_ambiguous,
        "pair_complete": pair_complete,
        "unknown_version": any(
            str(row["configuration_id"]) not in KNOWN_OCR_PROFILES for row in rows
        ),
    }


def _route_hold(
    base: dict[str, Any], ocr: dict[str, Any], snapshot_song_ids: set[str]
) -> tuple[str, str, str]:
    if ocr["unknown_version"]:
        return "manual_other", "", "unknown_ocr_profile_version"
    top3 = {item["song_id"] for item in json.loads(base["top_candidates"])[:3]}
    if ocr["title_conflict"] or ocr["pair_conflict"]:
        return "manual_jacket_ocr_conflict", "", "ocr_profile_song_id_conflict"
    title_ids = ocr["title_song_ids"]
    if len(title_ids) == 1 and not ocr["title_ambiguous"] and title_ids[0] in top3:
        return "auto_jacket_top3_title_ocr", title_ids[0], "unique_title_in_jacket_top3"
    pair_ids = ocr["pair_song_ids"]
    if len(pair_ids) == 1 and not ocr["pair_ambiguous"] and pair_ids[0] not in snapshot_song_ids:
        return "auto_ocr_title_artist_pair", pair_ids[0], "unique_pair_without_snapshot_reference"
    if ocr["pair_ambiguous"]:
        return "manual_ocr_pair_ambiguous", "", "title_artist_pair_ambiguous"
    if len(title_ids) == 1 and title_ids[0] not in top3:
        if title_ids[0] not in snapshot_song_ids and not ocr["pair_complete"]:
            return "manual_ocr_pair_incomplete", "", "snapshot_missing_pair_incomplete"
        return "manual_jacket_top3_miss", "", "title_candidate_outside_jacket_top3"
    if ocr["title_ambiguous"]:
        return "manual_jacket_ocr_unresolved", "", "title_ocr_ambiguous"
    if not ocr["pair_complete"] and len(title_ids) == 1 and title_ids[0] not in snapshot_song_ids:
        return "manual_ocr_pair_incomplete", "", "snapshot_missing_pair_incomplete"
    return "manual_jacket_ocr_unresolved", "", "title_ocr_unresolved"


def _candidate_at(candidates: list[dict[str, Any]], rank: int) -> str:
    return candidates[rank - 1]["song_id"] if len(candidates) >= rank else ""


def evaluate_policy_rows(
    observations: list[TruthObservation],
    jacket_rows: list[dict[str, Any]],
    ocr_rows_by_observation: dict[str, list[dict[str, Any]]],
    indexes: dict[str, dict[Any, set[str]]],
    snapshot_song_ids: set[str],
    *,
    snapshot_id: str,
) -> list[dict[str, Any]]:
    jacket_by_id = {row["observation_id"]: row for row in jacket_rows}
    output = []
    for observation in sorted(observations, key=lambda item: item.audit_no):
        if observation.review_status == "rejected":
            route, matched_song_id, reason = (
                "rejected_capture_mismatch",
                "",
                "capture_review_rejected",
            )
            base: dict[str, Any] = {}
            ocr: dict[str, Any] = {
                "title_profiles": [],
                "artist_profiles": [],
                "pair_resolutions": [],
            }
        else:
            base = jacket_by_id[observation.observation_id]
            ocr = _resolve_ocr(ocr_rows_by_observation[observation.observation_id], indexes)
            if base["decision_status"] in {"matched_correct", "matched_false"}:
                route, matched_song_id, reason = (
                    "auto_jacket_gate",
                    base["top_song_id"],
                    "existing_m5_jacket_gate",
                )
            else:
                route, matched_song_id, reason = _route_hold(base, ocr, snapshot_song_ids)
        is_auto = route in AUTO_ROUTES
        outcome = (
            "rejected"
            if route == "rejected_capture_mismatch"
            else (
                "correct"
                if is_auto and matched_song_id == observation.truth_song_id
                else "false"
                if is_auto
                else "hold"
            )
        )
        candidates = json.loads(base.get("top_candidates", "[]"))[:3]
        matched_rank = next(
            (rank for rank, item in enumerate(candidates, 1) if item["song_id"] == matched_song_id),
            None,
        )
        output.append(
            {
                "observation_id": observation.observation_id,
                "audit_no": observation.audit_no,
                "truth_song_id": observation.truth_song_id,
                "capture_validity": "invalid"
                if observation.review_status == "rejected"
                else "valid",
                "review_status": observation.review_status,
                "snapshot_mapping_status": (
                    "available"
                    if base.get("truth_official_snapshot_available")
                    else "not_available"
                )
                if base
                else "not_evaluated",
                "jacket_top1_song_id": _candidate_at(candidates, 1),
                "jacket_top2_song_id": _candidate_at(candidates, 2),
                "jacket_top3_song_id": _candidate_at(candidates, 3),
                "jacket_distance": base.get("top_distance"),
                "jacket_margin": base.get("top_margin"),
                "title_ocr_profiles": json.dumps(
                    ocr["title_profiles"], ensure_ascii=False, separators=(",", ":")
                ),
                "artist_ocr_profiles": json.dumps(
                    ocr["artist_profiles"], ensure_ascii=False, separators=(",", ":")
                ),
                "title_artist_pair_resolutions": json.dumps(
                    ocr["pair_resolutions"], ensure_ascii=False, separators=(",", ":")
                ),
                "policy_decision": route,
                "decision_source": route.removeprefix("auto_") if is_auto else "manual_or_rejected",
                "outcome": outcome,
                "hold_reason": "" if is_auto or outcome == "rejected" else reason,
                "conflict_reason": reason if "conflict" in reason or "ambiguous" in reason else "",
                "confirmation_source": route.removeprefix("auto_") if is_auto else "",
                "policy_version": POLICY_VERSION,
                "snapshot_id": snapshot_id,
                "feature_extractor_version": FEATURE_EXTRACTOR_VERSION,
                "jacket_feature_version": JACKET_FEATURE_VERSION,
                "jacket_rank": matched_rank,
                "ocr_profile": "multiple_existing_diagnostic_profiles"
                if is_auto and route != "auto_jacket_gate"
                else "",
                "matched_song_id": matched_song_id,
            }
        )
    return output


def summarize_policy(rows: list[dict[str, Any]]) -> dict[str, Any]:
    confirmed = [row for row in rows if row["review_status"] == "confirmed"]
    by_route = {}
    for route in POLICY_ROUTES:
        route_rows = [row for row in rows if row["policy_decision"] == route]
        auto = sum(row["outcome"] in {"correct", "false"} for row in route_rows)
        correct = sum(row["outcome"] == "correct" for row in route_rows)
        false = sum(row["outcome"] == "false" for row in route_rows)
        by_route[route] = {
            "evaluated": len(route_rows),
            "auto_decisions": auto,
            "correct_decisions": correct,
            "false_decisions": false,
            "decision_precision": correct / auto if auto else None,
            "decision_coverage": auto / len(confirmed) if confirmed else None,
        }
    auto_rows = [row for row in confirmed if row["outcome"] in {"correct", "false"}]
    correct = sum(row["outcome"] == "correct" for row in auto_rows)
    false = sum(row["outcome"] == "false" for row in auto_rows)
    return {
        "evaluated": len(rows),
        "confirmed_truth_count": len(confirmed),
        "auto_decisions": len(auto_rows),
        "correct_decisions": correct,
        "false_decisions": false,
        "decision_precision": correct / len(auto_rows) if auto_rows else None,
        "decision_coverage": len(auto_rows) / len(confirmed) if confirmed else None,
        "manual_review_remaining": sum(row["outcome"] == "hold" for row in confirmed),
        "jacket_gate_auto_count": by_route["auto_jacket_gate"]["auto_decisions"],
        "jacket_top3_title_ocr_auto_count": by_route["auto_jacket_top3_title_ocr"][
            "auto_decisions"
        ],
        "ocr_title_artist_pair_auto_count": by_route["auto_ocr_title_artist_pair"][
            "auto_decisions"
        ],
        "ocr_method_conflict_count": sum(bool(row["conflict_reason"]) for row in confirmed),
        "capture_mismatch_count": by_route["rejected_capture_mismatch"]["evaluated"],
        "route_counts": dict(Counter(row["policy_decision"] for row in rows)),
        "by_route": by_route,
    }


def _percent(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.4%}"


def build_policy_report(summary: dict[str, Any]) -> str:
    metrics = summary["metrics"]
    lines = [
        "# Jacket auto-registration policy read-only evaluation",
        "",
        "This report evaluates truth only. It does not mutate the catalog, master, "
        "ODS, snapshot, images, or manual-review state.",
        "",
        "## Policy routes",
        "",
        "- `rejected_capture_mismatch`: invalid capture; jacket and OCR decisions are skipped.",
        "- `auto_jacket_gate`: existing M5 distance/margin gate accepted the jacket top-1.",
        "- `auto_jacket_top3_title_ocr`: gate held; one non-fuzzy title result "
        "resolved consistently inside jacket top-3.",
        "- `auto_ocr_title_artist_pair`: one non-fuzzy title/artist pair resolved "
        "consistently to a song with no snapshot reference.",
        "- `manual_*`: missing, ambiguous, conflicting, or top-3-miss evidence remains for review.",
        "",
        "## Result",
        "",
        f"- Evaluated: {metrics['evaluated']} ({metrics['confirmed_truth_count']} confirmed)",
        f"- Auto decisions: {metrics['auto_decisions']}",
        f"- Correct / false: {metrics['correct_decisions']} / {metrics['false_decisions']}",
        f"- Precision: {_percent(metrics['decision_precision'])}",
        f"- Coverage of confirmed: {_percent(metrics['decision_coverage'])}",
        f"- Manual review remaining: {metrics['manual_review_remaining']}",
        f"- Capture mismatches: {metrics['capture_mismatch_count']}",
        "",
        "| route | evaluated | auto | correct | false | precision | coverage |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for route in POLICY_ROUTES:
        item = metrics["by_route"][route]
        lines.append(
            f"| `{route}` | {item['evaluated']} | {item['auto_decisions']} | "
            f"{item['correct_decisions']} | {item['false_decisions']} | "
            f"{_percent(item['decision_precision'])} | "
            f"{_percent(item['decision_coverage'])} |"
        )
    lines.extend(
        [
            "",
            "Any route with a false decision remains ineligible for production auto-registration.",
            "The report preserves the future evidence contract, including "
            "policy/snapshot/feature versions, jacket distance/margin/rank, OCR profile "
            "observations, and matched song ID; no schema change is made here.",
            "",
        ]
    )
    return "\n".join(lines)


def evaluate_policy(config: PolicyEvaluationConfig) -> Path:
    snapshot = config.snapshot.resolve()
    truth_ods = config.truth_ods.resolve()
    catalog = config.catalog.resolve()
    master = config.master.resolve()
    output = config.output.resolve()
    incomplete = output.with_name(f"{output.name}.incomplete")
    if output.exists() or incomplete.exists():
        raise EvaluationError(f"evaluation output already exists; refusing to overwrite: {output}")
    inputs = (truth_ods, catalog, master)
    for path in (snapshot, *inputs):
        if not path.exists():
            raise EvaluationError(f"input does not exist: {path}")
    hashes_before = {str(path): sha256_file(path) for path in inputs}
    master_by_id, aliases = load_master(master)
    observations = load_truth(truth_ods, master_by_id)
    catalog_features = load_catalog_features(catalog, observations)
    snapshot_rows, snapshot_fingerprints = load_snapshot(snapshot)
    snapshot_songs = load_snapshot_features(snapshot, snapshot_rows)
    indexes = build_master_indexes(master_by_id, aliases)
    add_artist_indexes(indexes, master_by_id, aliases)
    official_by_song: dict[str, list[SnapshotSong]] = defaultdict(list)
    for snapshot_song in snapshot_songs:
        mapping = resolve_snapshot_song(
            snapshot_song.title, snapshot_song.artist, master_by_id, indexes
        )
        if mapping.song is not None:
            official_by_song[mapping.song.song_id].append(snapshot_song)
    jacket_rows, jacket_metrics = evaluate_rows(
        observations, catalog_features, official_by_song, master_by_id
    )
    ocr_by_observation = load_truth_ocr_profiles(
        truth_ods, {item.observation_id for item in observations}
    )
    manifest = read_json(snapshot / "manifest.json")
    policy_rows = evaluate_policy_rows(
        observations,
        jacket_rows,
        ocr_by_observation,
        indexes,
        set(official_by_song),
        snapshot_id=str(manifest.get("snapshot_id") or snapshot.name),
    )
    metrics = summarize_policy(policy_rows)
    hashes_after = {str(path): sha256_file(path) for path in inputs}
    if hashes_after != hashes_before:
        raise EvaluationError(
            "read-only inputs changed during evaluation; output was not published"
        )
    if {
        "manifest_sha256": sha256_file(snapshot / "manifest.json"),
        "summary_sha256": sha256_file(snapshot / "summary.json"),
        "songs_sha256": sha256_file(snapshot / "songs.jsonl"),
    } != snapshot_fingerprints:
        raise EvaluationError(
            "snapshot metadata changed during evaluation; output was not published"
        )
    verify_snapshot_images_unchanged(snapshot, snapshot_songs)
    summary = {
        "schema_version": SUMMARY_SCHEMA,
        "policy_version": POLICY_VERSION,
        "inputs": {
            "snapshot": str(snapshot),
            "truth_ods": str(truth_ods),
            "catalog": str(catalog),
            "master": str(master),
            "input_sha256": hashes_before,
            **snapshot_fingerprints,
        },
        "jacket_baseline": jacket_metrics,
        "metrics": metrics,
        "production_adoption": {
            route: metrics["by_route"][route]["false_decisions"] == 0 for route in AUTO_ROUTES
        },
        "output_files": [
            "policy_evaluation.csv",
            "false_decisions.csv",
            "summary.json",
            "report.md",
        ],
    }
    fieldnames = list(policy_rows[0])
    try:
        incomplete.mkdir(parents=True)
        write_csv(incomplete / "policy_evaluation.csv", policy_rows, fieldnames)
        false_rows = [row for row in policy_rows if row["outcome"] == "false"]
        write_csv(incomplete / "false_decisions.csv", false_rows, fieldnames)
        (incomplete / "summary.json").write_text(
            json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        (incomplete / "report.md").write_text(
            build_policy_report(summary), encoding="utf-8", newline="\n"
        )
        incomplete.rename(output)
    except (OSError, csv.Error) as exc:
        shutil.rmtree(incomplete, ignore_errors=True)
        raise EvaluationError(f"failed to publish policy evaluation output: {exc}") from exc
    return output
