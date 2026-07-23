from __future__ import annotations

import csv
import hashlib
import io
import json
import os
import shutil
import tempfile
from collections import Counter
from collections.abc import Iterable
from datetime import datetime
from pathlib import Path
from typing import Any

from PIL import Image

from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import title_artist_evaluation as evaluation

EVALUATION_SCHEMA_VERSION = "m5c-unresolved-candidate-evaluation-v1"
REPORT_SCHEMA_VERSION = "m5c-unresolved-candidate-report-v1"
METHOD_VERSION = evaluation.METHOD_VERSIONS[0]

REPORT_FIELDS = [
    "reference_id",
    "observation_id",
    "persisted_status",
    "revision",
    "classification",
    "reason",
    "title_raw",
    "title_confidence",
    "title_status",
    "artist_raw",
    "artist_confidence",
    "artist_status",
    "candidate_song_ids",
]

CHECKPOINT_KEYS = {
    "checkpoint_version",
    "session",
    "last_stable_feature_hash",
    "stable_feature_hashes",
    "processed_frame_count",
    "dropped_frame_count",
    "observations",
    "updated_at_utc",
}
CHECKPOINT_OBSERVATION_KEYS_V1 = {
    "observation_id",
    "source_image_hash",
    "jacket_crop_hash",
    "feature_hash",
    "catalog_status",
    "catalog_reference_id",
    "artifact_path",
    "adopted_at_utc",
}
CHECKPOINT_OBSERVATION_KEYS_V2 = (CHECKPOINT_OBSERVATION_KEYS_V1 - {"feature_hash"}) | {
    "jacket_feature_version",
    "jacket_feature_hash",
    "title_line_feature_version",
    "title_line_hash",
    "composite_identity_version",
    "composite_identity_hash",
}
CHECKPOINT_SESSION_KEYS = {
    "session_id",
    "master_version",
    "master_source_hash",
    "catalog_identity",
    "catalog_schema_version",
    "catalog_created_at",
    "feature_extractor_version",
    "detector_version",
    "roi_version",
    "frame_clock_version",
    "window",
    "started_at_utc",
}


def _empty_field() -> dict[str, Any]:
    return {
        "raw": "",
        "normalized": "",
        "confidence": None,
        "status": "not_evaluated",
        "failure_reason": "",
    }


def _field(value: evaluation.FieldExtraction) -> dict[str, Any]:
    return {
        "raw": value.raw,
        "normalized": value.normalized,
        "confidence": value.confidence,
        "status": value.status,
        "failure_reason": value.failure_reason,
    }


def _result(
    classification: str,
    reason: str,
    *,
    observation_id: str,
    preview_path: str | None = None,
    title: evaluation.FieldExtraction | None = None,
    artist: evaluation.FieldExtraction | None = None,
    candidates: list[dict[str, str]] | None = None,
) -> dict[str, Any]:
    return {
        "evaluation_schema_version": EVALUATION_SCHEMA_VERSION,
        "method_version": METHOD_VERSION,
        "observation_id": observation_id,
        "classification": classification,
        "reason": reason,
        "jacket_preview_path": preview_path,
        "title": _empty_field() if title is None else _field(title),
        "artist": _empty_field() if artist is None else _field(artist),
        "candidates": candidates or [],
    }


def _timestamp(value: Any, label: str) -> datetime:
    if not isinstance(value, str):
        raise ValueError(f"{label} is invalid")
    try:
        parsed = datetime.fromisoformat(value)
    except ValueError as exc:
        raise ValueError(f"{label} is invalid") from exc
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise ValueError(f"{label} must include a timezone")
    return parsed


def _manifest_index(artifact_root: Path) -> dict[str, list[Path]]:
    if not artifact_root.is_dir():
        return {}
    result: dict[str, list[Path]] = {}
    for path in artifact_root.glob("*/observations/*/observation.json"):
        result.setdefault(path.parent.name, []).append(path)
    return result


def _file_fingerprint(path: Path) -> tuple[int, int, str]:
    stat = path.stat()
    return stat.st_size, stat.st_mtime_ns, hashlib.sha256(path.read_bytes()).hexdigest()


def _validate_checkpoint(
    manifest_path: Path,
    manifest: dict[str, Any],
    reference: dict[str, Any],
) -> None:
    checkpoint_path = manifest_path.parents[2] / "checkpoint.json"
    document = evaluation._read_strict_json(checkpoint_path)
    if not isinstance(document, dict):
        raise ValueError("checkpoint is not an object")
    evaluation._require_exact_keys(document, CHECKPOINT_KEYS, "checkpoint")
    version = document["checkpoint_version"]
    if version not in {"m5c-observation-checkpoint-v1", "m5c-observation-checkpoint-v2"}:
        raise ValueError("checkpoint version is unsupported")
    session = document["session"]
    if not isinstance(session, dict):
        raise ValueError("checkpoint session is invalid")
    evaluation._require_exact_keys(session, CHECKPOINT_SESSION_KEYS, "checkpoint session")
    identity_fields = (
        "session_id",
        "master_version",
        "master_source_hash",
        "catalog_identity",
        "catalog_schema_version",
        "catalog_created_at",
        "feature_extractor_version",
        "detector_version",
        "roi_version",
        "frame_clock_version",
    )
    if any(session.get(field) != manifest[field] for field in identity_fields):
        raise ValueError("checkpoint identity drift detected")
    if session["window"] != manifest["window"]:
        raise ValueError("checkpoint window identity drift detected")
    if _timestamp(session["started_at_utc"], "checkpoint started_at_utc") > _timestamp(
        manifest["captured_at_utc"], "manifest captured_at_utc"
    ):
        raise ValueError("checkpoint session time is invalid")
    observations = document["observations"]
    if not isinstance(observations, list):
        raise ValueError("checkpoint observations are invalid")
    expected_keys = (
        CHECKPOINT_OBSERVATION_KEYS_V2 if version.endswith("v2") else CHECKPOINT_OBSERVATION_KEYS_V1
    )
    observation_ids: set[str] = set()
    for observation in observations:
        if not isinstance(observation, dict):
            raise ValueError("checkpoint observation is invalid")
        evaluation._require_exact_keys(observation, expected_keys, "checkpoint observation")
        observation_id = observation["observation_id"]
        if not isinstance(observation_id, str) or observation_id in observation_ids:
            raise ValueError("checkpoint observation identity is invalid or duplicated")
        observation_ids.add(observation_id)
    matches = [
        item
        for item in observations
        if isinstance(item, dict) and item.get("observation_id") == manifest["observation_id"]
    ]
    if len(matches) != 1:
        raise ValueError("checkpoint observation ledger entry is missing or duplicated")
    item = matches[0]
    expected_artifact = str(manifest_path.parent.resolve())
    if (
        item["source_image_hash"] != manifest["source_image_hash"]
        or item["jacket_crop_hash"] != manifest["jacket_crop_hash"]
        or item["jacket_feature_hash" if version.endswith("v2") else "feature_hash"]
        != manifest["feature_hash"]
        or item["catalog_status"] != "ingested"
        or item["catalog_reference_id"] != reference["reference_id"]
        or not isinstance(item["artifact_path"], str)
        or str(Path(item["artifact_path"]).resolve()).casefold() != expected_artifact.casefold()
        or _timestamp(item["adopted_at_utc"], "checkpoint adopted_at_utc")
        != _timestamp(manifest["created_at_utc"], "manifest created_at_utc")
    ):
        raise ValueError("checkpoint ledger does not match artifact/catalog")
    composite_manifest_values = {
        "jacket_feature_version": manifest["feature_version"],
        "jacket_feature_hash": manifest["feature_hash"],
        "title_line_feature_version": manifest.get("title_line_feature_version"),
        "title_line_hash": manifest.get("title_line_hash"),
        "composite_identity_version": manifest.get("composite_identity_version"),
        "composite_identity_hash": manifest.get("composite_identity_hash"),
    }
    if version.endswith("v2") and any(
        item[field] != value for field, value in composite_manifest_values.items()
    ):
        raise ValueError("checkpoint composite identity drift detected")


def _unavailable_reason(exception: Exception) -> str:
    message = str(exception).casefold()
    if "changed during evaluation" in message:
        return "artifact_or_checkpoint_changed_during_evaluation"
    if "checkpoint" in message:
        return "checkpoint_invalid_or_drifted"
    if "master" in message:
        return "master_identity_drift"
    if "catalog" in message:
        return "catalog_identity_drift"
    if "extractor" in message or "feature" in message:
        return "extractor_identity_drift"
    if "image" in message or "artifact" in message:
        return "artifact_missing_corrupt_or_drifted"
    return "artifact_identity_or_version_invalid"


def _validated_artifact(
    reference: dict[str, Any],
    *,
    artifact_root: Path,
    manifest_path: Path,
    master: catalog.MasterIdentity,
    catalog_identity: evaluation.CatalogIdentity,
) -> tuple[evaluation.ArtifactInput, tuple[Path, ...], dict[Path, tuple[int, int, str]]]:
    observation_id = str(reference["source_capture_id"] or "")
    snapshot_paths = (
        manifest_path,
        manifest_path.parent / "source.png",
        manifest_path.parent / "jacket-crop.png",
        manifest_path.parents[2] / "checkpoint.json",
    )
    before_fingerprints = {path: _file_fingerprint(path) for path in snapshot_paths}
    relative = manifest_path.resolve().relative_to(artifact_root.resolve()).as_posix()
    entry = evaluation.DatasetEntry(relative, None, None, None)
    artifact = evaluation._validate_manifest(
        evaluation._read_strict_json(manifest_path),
        entry=entry,
        artifact_root=artifact_root,
        master=master,
        catalog=catalog_identity,
    )
    manifest = artifact.manifest
    if (
        manifest["observation_id"] != observation_id
        or manifest["jacket_crop_hash"] != reference["source_image_hash"]
        or manifest["feature_extractor_version"] != reference["feature_extractor_version"]
        or manifest["feature_version"] != reference["jacket_feature_version"]
        or manifest["feature_hash"] != reference["jacket_feature_hash"]
        or manifest.get("title_line_feature_version") != reference["title_line_feature_version"]
        or manifest.get("title_line_hash") != reference["title_line_hash"]
        or manifest.get("composite_identity_version") != reference["composite_identity_version"]
        or manifest.get("composite_identity_hash") != reference["composite_identity_hash"]
    ):
        raise ValueError("artifact/catalog identity drift detected")
    _validate_checkpoint(manifest_path, manifest, reference)
    return artifact, snapshot_paths, before_fingerprints


def resolve_source_image_path(
    reference: dict[str, Any],
    *,
    artifact_root: Path,
    manifest_paths: list[Path],
    master: catalog.MasterIdentity,
    catalog_identity: evaluation.CatalogIdentity,
) -> str | None:
    if not reference["source_capture_id"] or len(manifest_paths) != 1:
        return None
    try:
        _artifact, snapshot_paths, before_fingerprints = _validated_artifact(
            reference,
            artifact_root=artifact_root,
            manifest_path=manifest_paths[0],
            master=master,
            catalog_identity=catalog_identity,
        )
        if any(_file_fingerprint(path) != before_fingerprints[path] for path in snapshot_paths):
            raise ValueError("artifact/checkpoint changed during evaluation")
    except (OSError, ValueError):
        return None
    return str((manifest_paths[0].parent / "source.png").resolve())


def evaluate_reference(
    reference: dict[str, Any],
    *,
    artifact_root: Path,
    manifest_paths: list[Path],
    master: catalog.MasterIdentity,
    catalog_identity: evaluation.CatalogIdentity,
    extractor: evaluation.Extractor = evaluation.extract_field,
) -> dict[str, Any]:
    observation_id = str(reference["source_capture_id"] or "")
    if reference["review_status"] != "unresolved":
        return _result(
            "not_eligible",
            f"persisted_status_{reference['review_status']}",
            observation_id=observation_id,
        )
    if not observation_id:
        return _result("evaluation_unavailable", "source_capture_id_missing", observation_id="")
    if len(manifest_paths) != 1:
        reason = "artifact_not_found" if not manifest_paths else "duplicate_artifact_identity"
        return _result("evaluation_unavailable", reason, observation_id=observation_id)
    manifest_path = manifest_paths[0]
    try:
        artifact, snapshot_paths, before_fingerprints = _validated_artifact(
            reference,
            artifact_root=artifact_root,
            master=master,
            catalog_identity=catalog_identity,
            manifest_path=manifest_path,
        )
        with Image.open(io.BytesIO(artifact.source_bytes)) as opened:
            image = opened.convert("RGB")
        title = extractor(image, "title", METHOD_VERSION)
        artist = extractor(image, "artist", METHOD_VERSION)
        if any(_file_fingerprint(path) != before_fingerprints[path] for path in snapshot_paths):
            raise ValueError("artifact/checkpoint changed during evaluation")
    except (OSError, ValueError) as exception:
        return _result(
            "evaluation_unavailable",
            _unavailable_reason(exception),
            observation_id=observation_id,
        )

    preview_path = str((manifest_path.parent / "jacket-crop.png").resolve())
    if title.status != "ok" or artist.status != "ok":
        classification = (
            "low_confidence"
            if "low_confidence" in {title.status, artist.status}
            else "evaluation_failed"
        )
        reason = title.failure_reason or artist.failure_reason or "field_evaluation_failed"
        return _result(
            classification,
            reason,
            observation_id=observation_id,
            preview_path=preview_path,
            title=title,
            artist=artist,
        )
    resolution = catalog.resolve_observation(master, title.raw, artist.raw)
    songs_by_id = {song.song_id: song for song in master.songs}
    candidates = [
        {
            "song_id": song_id,
            "title": songs_by_id[song_id].title,
            "artist": songs_by_id[song_id].artist,
        }
        for song_id in resolution.candidate_song_ids
        if song_id in songs_by_id
    ]
    classification = {
        "canonical_title_artist_exact": "exact_unique",
        "unique_alias_title_artist_exact": "alias_unique",
        "ambiguous_canonical_title_artist": "ambiguous",
        "ambiguous_alias_title_artist": "ambiguous",
        "title_match_artist_mismatch": "ambiguous",
        "identity_not_found": "no_candidate",
    }.get(resolution.reason, "evaluation_failed")
    if classification not in {"exact_unique", "alias_unique", "ambiguous"}:
        candidates = []
    return _result(
        classification,
        resolution.reason,
        observation_id=observation_id,
        preview_path=preview_path,
        title=title,
        artist=artist,
        candidates=candidates,
    )


def evaluate_references(
    references: Iterable[dict[str, Any]],
    *,
    artifact_root: Path,
    master: catalog.MasterIdentity,
    catalog_identity: evaluation.CatalogIdentity,
    extractor: evaluation.Extractor = evaluation.extract_field,
) -> list[dict[str, Any]]:
    index = _manifest_index(artifact_root)
    return [
        evaluate_reference(
            reference,
            artifact_root=artifact_root,
            manifest_paths=index.get(str(reference["source_capture_id"] or ""), []),
            master=master,
            catalog_identity=catalog_identity,
            extractor=extractor,
        )
        for reference in references
    ]


def resolve_source_image_paths(
    references: Iterable[dict[str, Any]],
    *,
    artifact_root: Path,
    master: catalog.MasterIdentity,
    catalog_identity: evaluation.CatalogIdentity,
) -> list[str | None]:
    index = _manifest_index(artifact_root)
    return [
        resolve_source_image_path(
            reference,
            artifact_root=artifact_root,
            manifest_paths=index.get(str(reference["source_capture_id"] or ""), []),
            master=master,
            catalog_identity=catalog_identity,
        )
        for reference in references
    ]


def summarize(rows: Iterable[dict[str, Any]]) -> dict[str, Any]:
    rows = [row for row in rows if row["candidate_evaluation"]["observation_id"]]
    classifications = Counter(row["candidate_evaluation"]["classification"] for row in rows)
    reasons = Counter(row["candidate_evaluation"]["reason"] for row in rows)
    return {
        "report_schema_version": REPORT_SCHEMA_VERSION,
        "evaluation_schema_version": EVALUATION_SCHEMA_VERSION,
        "method_version": METHOD_VERSION,
        "total_observations": len(rows),
        "current_unresolved_observations": sum(
            row["stored_status"] == "unresolved" for row in rows
        ),
        "eligible_observations": sum(
            row["candidate_evaluation"]["classification"] != "not_eligible" for row in rows
        ),
        "evaluated_observations": sum(
            row["candidate_evaluation"]["classification"]
            in {
                "exact_unique",
                "alias_unique",
                "ambiguous",
                "no_candidate",
                "low_confidence",
                "evaluation_failed",
            }
            for row in rows
        ),
        "exact_unique_candidates": classifications["exact_unique"],
        "alias_unique_candidates": classifications["alias_unique"],
        "ambiguous_candidates": classifications["ambiguous"],
        "no_candidates": classifications["no_candidate"],
        "ocr_evaluation_failures": classifications["low_confidence"]
        + classifications["evaluation_failed"],
        "not_evaluated": classifications["evaluation_unavailable"]
        + classifications["not_eligible"],
        "classification_counts": dict(sorted(classifications.items())),
        "reason_counts": dict(sorted(reasons.items())),
        "field_status_counts": {
            field: dict(
                sorted(
                    Counter(row["candidate_evaluation"][field]["status"] for row in rows).items()
                )
            )
            for field in ("title", "artist")
        },
        "field_failure_reason_counts": {
            field: dict(
                sorted(
                    Counter(
                        row["candidate_evaluation"][field]["failure_reason"]
                        for row in rows
                        if row["candidate_evaluation"][field]["failure_reason"]
                    ).items()
                )
            )
            for field in ("title", "artist")
        },
    }


def _csv_value(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, float):
        return f"{value:.6f}"
    if isinstance(value, list):
        return "|".join(str(item) for item in value)
    return str(value)


def render_csv(rows: Iterable[dict[str, Any]]) -> str:
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=REPORT_FIELDS, lineterminator="\n")
    writer.writeheader()
    for row in rows:
        item = row["candidate_evaluation"]
        writer.writerow(
            {
                "reference_id": row["reference_id"],
                "observation_id": item["observation_id"],
                "persisted_status": row["stored_status"],
                "revision": row["revision"],
                "classification": item["classification"],
                "reason": item["reason"],
                "title_raw": item["title"]["raw"],
                "title_confidence": _csv_value(item["title"]["confidence"]),
                "title_status": item["title"]["status"],
                "artist_raw": item["artist"]["raw"],
                "artist_confidence": _csv_value(item["artist"]["confidence"]),
                "artist_status": item["artist"]["status"],
                "candidate_song_ids": _csv_value(
                    [candidate["song_id"] for candidate in item["candidates"]]
                ),
            }
        )
    return output.getvalue()


def render_markdown(summary: dict[str, Any]) -> str:
    return "\n".join(
        [
            "# M5c unresolved candidate evaluation",
            "",
            f"- total observations: `{summary['total_observations']}`",
            f"- current unresolved observations: `{summary['current_unresolved_observations']}`",
            f"- eligible observations: `{summary['eligible_observations']}`",
            f"- evaluated observations: `{summary['evaluated_observations']}`",
            f"- exact unique candidates: `{summary['exact_unique_candidates']}`",
            f"- alias unique candidates: `{summary['alias_unique_candidates']}`",
            f"- ambiguous candidates: `{summary['ambiguous_candidates']}`",
            f"- no candidates: `{summary['no_candidates']}`",
            f"- OCR/evaluation failures: `{summary['ocr_evaluation_failures']}`",
            f"- not evaluated: `{summary['not_evaluated']}`",
            "- title status: "
            f"`{json.dumps(summary['field_status_counts']['title'], sort_keys=True)}`",
            "- artist status: "
            f"`{json.dumps(summary['field_status_counts']['artist'], sort_keys=True)}`",
            "",
            "Candidate output is read-only and is not ground truth or an automatic confirmation.",
            "",
        ]
    )


def write_reports(output_dir: Path, rows: list[dict[str, Any]]) -> None:
    rows = [row for row in rows if row["candidate_evaluation"]["observation_id"]]
    summary = summarize(rows)
    output_dir = output_dir.resolve()
    output_dir.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".unresolved-candidate-", dir=output_dir.parent))
    try:
        (temporary / "unresolved_candidates.csv").write_text(
            render_csv(rows), encoding="utf-8", newline="\n"
        )
        (temporary / "unresolved_candidates.json").write_text(
            json.dumps({**summary, "rows": rows}, ensure_ascii=False, indent=2, sort_keys=True)
            + "\n",
            encoding="utf-8",
            newline="\n",
        )
        (temporary / "unresolved_candidates.md").write_text(
            render_markdown(summary), encoding="utf-8", newline="\n"
        )
        output_dir.mkdir(parents=True, exist_ok=True)
        for name in (
            "unresolved_candidates.csv",
            "unresolved_candidates.json",
            "unresolved_candidates.md",
        ):
            os.replace(temporary / name, output_dir / name)
    finally:
        shutil.rmtree(temporary, ignore_errors=True)
