from __future__ import annotations

import hashlib
import json
import os
import sqlite3
import tempfile
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from tools.vision_poc import jacket_reference_catalog

from .evaluator import (
    EvaluationError,
    load_ods_sheets,
    load_snapshot,
    load_snapshot_features,
    rows_as_dicts,
    sha256_file,
    verify_snapshot_images_unchanged,
)
from .ods_export import write_ods
from .policy import (
    AUTO_ROUTES,
    POLICY_ROUTES,
    POLICY_VERSION,
    PolicyEvaluationConfig,
    PolicyEvaluationResult,
    evaluate_policy_inputs,
)

PLAN_SCHEMA_VERSION = "jacket-catalog-auto-registration-plan-v1"
EVIDENCE_SCHEMA_VERSION = "jacket-catalog-auto-confirmation-evidence-v1"
MANUAL_ODS_SCHEMA_VERSION = "jacket-catalog-manual-review-ods-v1"

MANUAL_ODS_HEADERS = [
    "schema_version",
    "export_id",
    "source_catalog_revision",
    "source_master_revision",
    "policy_version",
    "observation_id",
    "audit_no",
    "current_review_status",
    "current_review_revision",
    "capture_validity",
    "image_reference",
    "jacket_top1_song_id",
    "jacket_top1_title",
    "jacket_top1_artist",
    "jacket_top1_rank",
    "jacket_top1_distance",
    "jacket_top2_song_id",
    "jacket_top2_title",
    "jacket_top2_artist",
    "jacket_top2_rank",
    "jacket_top2_distance",
    "jacket_top3_song_id",
    "jacket_top3_title",
    "jacket_top3_artist",
    "jacket_top3_rank",
    "jacket_top3_distance",
    "rank",
    "distance",
    "margin",
    "title_ocr_raw_normalized_candidate_json",
    "artist_ocr_raw_normalized_candidate_json",
    "hold_reason",
    "recommended_song_id",
    "truth_song_id",
    "notes",
]


@dataclass(frozen=True)
class CatalogPipelineConfig:
    snapshot: Path
    observations_ods: Path
    catalog: Path
    master: Path


def _canonical_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def _sha256_json(value: Any) -> str:
    return hashlib.sha256(_canonical_json(value).encode("utf-8")).hexdigest()


def _is_sha256(value: Any) -> bool:
    return (
        isinstance(value, str)
        and len(value) == 64
        and all(character in "0123456789abcdef" for character in value)
    )


def _ensure_data_output(path: Path, *, label: str, suffix: str) -> Path:
    path = path.resolve()
    root = (Path.cwd() / "data").resolve()
    try:
        relative = path.relative_to(root)
    except ValueError as exc:
        raise ValueError(f"{label} must be under data/") from exc
    if not relative.parts or path.suffix.lower() != suffix:
        raise ValueError(f"{label} must be a {suffix} file below data/")
    return path


def _policy_config(config: CatalogPipelineConfig) -> PolicyEvaluationConfig:
    return PolicyEvaluationConfig(
        snapshot=config.snapshot,
        truth_ods=config.observations_ods,
        catalog=config.catalog,
        master=config.master,
        output=Path("unused"),
    )


def _policy_revision(rows: list[dict[str, Any]]) -> str:
    return _sha256_json(rows)


def _catalog_connection(path: Path) -> sqlite3.Connection:
    return sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=ro", uri=True)


def _catalog_rows(
    catalog: Path, connection: sqlite3.Connection | None = None
) -> dict[str, dict[str, Any]]:
    owned = connection is None
    active = connection or _catalog_connection(catalog)
    active.row_factory = sqlite3.Row
    try:
        rows = [dict(row) for row in active.execute("SELECT * FROM jacket_references")]
    finally:
        if owned:
            active.close()
    by_observation: dict[str, dict[str, Any]] = {}
    for row in rows:
        observation_id = str(row["source_capture_id"] or "")
        if not observation_id or observation_id in by_observation:
            raise ValueError("catalog contains an invalid or duplicate observation id")
        by_observation[observation_id] = row
    return by_observation


def catalog_guard_revision(
    catalog: Path,
    auto_observation_ids: set[str],
    *,
    connection: sqlite3.Connection | None = None,
) -> str:
    owned = connection is None
    active = connection or _catalog_connection(catalog)
    active.row_factory = sqlite3.Row
    mutable = set(jacket_reference_catalog.AUTO_CONFIRMATION_STATE_FIELDS)
    try:
        metadata = [
            list(row)
            for row in active.execute("SELECT key, value FROM catalog_metadata ORDER BY key")
        ]
        references = []
        for raw in active.execute("SELECT * FROM jacket_references ORDER BY reference_id"):
            row = dict(raw)
            if str(row["source_capture_id"] or "") in auto_observation_ids:
                for field in mutable:
                    row[field] = "<auto-confirmation-managed>"
            references.append(row)
        candidates = [
            dict(row)
            for row in active.execute(
                "SELECT * FROM reference_candidates ORDER BY reference_id, song_id"
            )
        ]
        history = [
            dict(row)
            for row in active.execute(
                "SELECT * FROM reference_review_history ORDER BY history_id"
            )
        ]
    finally:
        if owned:
            active.close()
    return _sha256_json(
        {
            "metadata": metadata,
            "references": references,
            "candidates": candidates,
            "history": history,
        }
    )


def _review_ods_rows(path: Path) -> dict[str, dict[str, Any]]:
    sheets = load_ods_sheets(path)
    if "Review" not in sheets:
        raise EvaluationError("observations ODS is missing the Review sheet")
    rows = rows_as_dicts(sheets["Review"], sheet_name="Review")
    by_observation: dict[str, dict[str, Any]] = {}
    for row in rows:
        observation_id = str(row.get("observation_id") or "")
        if not observation_id or observation_id in by_observation:
            raise EvaluationError("observations ODS contains invalid duplicate observations")
        by_observation[observation_id] = row
    return by_observation


def _profile_candidates(raw: str) -> list[str]:
    values = json.loads(raw)
    candidates: set[str] = set()
    for value in values:
        candidates.update(str(item) for item in value.get("candidate_song_ids", []))
    return sorted(candidates)


def _recommended_song_id(row: dict[str, Any]) -> str:
    title = _profile_candidates(str(row["title_ocr_profiles"]))
    if len(title) == 1:
        return title[0]
    pair = json.loads(str(row["title_artist_pair_resolutions"]))
    candidates = {
        str(song_id)
        for value in pair
        for song_id in value.get("candidate_song_ids", [])
    }
    return next(iter(candidates)) if len(candidates) == 1 else ""


def _evidence(row: dict[str, Any]) -> dict[str, Any]:
    return {
        "evidence_schema_version": EVIDENCE_SCHEMA_VERSION,
        "observation_id": row["observation_id"],
        "confirmation_source": row["confirmation_source"],
        "matched_song_id": row["matched_song_id"],
        "policy_version": row["policy_version"],
        "snapshot_id": row["snapshot_id"],
        "feature_extractor_version": row["feature_extractor_version"],
        "jacket_feature_version": row["jacket_feature_version"],
        "jacket_distance": row["jacket_distance"],
        "jacket_margin": row["jacket_margin"],
        "jacket_rank": row["jacket_rank"],
        "ocr_profile": row["ocr_profile"],
        "title_ocr_profiles": json.loads(str(row["title_ocr_profiles"])),
        "artist_ocr_profiles": json.loads(str(row["artist_ocr_profiles"])),
        "title_artist_pair_resolutions": json.loads(
            str(row["title_artist_pair_resolutions"])
        ),
    }


def _desired_matches(
    catalog_row: dict[str, Any],
    policy_row: dict[str, Any],
    evidence_json: str,
    master_song: Any,
    master_version: str,
) -> bool:
    return (
        str(catalog_row["master_version"]) == master_version
        and catalog_row["song_id"] == policy_row["matched_song_id"]
        and str(catalog_row["canonical_title_snapshot"]) == master_song.title
        and str(catalog_row["canonical_artist_snapshot"]) == master_song.artist
        and str(catalog_row["review_status"]) == "auto_confirmed"
        and str(catalog_row["resolution_reason"]) == evidence_json
        and str(catalog_row["resolution_basis"]) == policy_row["confirmation_source"]
        and int(catalog_row["review_revision"]) == 0
        and catalog_row["manual_action_id"] is None
        and str(catalog_row["manual_note"]) == ""
    )


def _manual_row(
    row: dict[str, Any],
    catalog_row: dict[str, Any],
    ods_row: dict[str, Any],
    master_by_id: dict[str, Any],
) -> dict[str, Any]:
    value: dict[str, Any] = {
        "observation_id": row["observation_id"],
        "audit_no": row["audit_no"],
        "current_review_status": catalog_row["review_status"],
        "current_review_revision": catalog_row["review_revision"],
        "capture_validity": row["capture_validity"],
        "image_reference": str(ods_row.get("source_image") or ""),
        "rank": row["jacket_rank"],
        "distance": row["jacket_distance"],
        "margin": row["jacket_margin"],
        "title_ocr_raw_normalized_candidate_json": row["title_ocr_profiles"],
        "artist_ocr_raw_normalized_candidate_json": row["artist_ocr_profiles"],
        "hold_reason": row["hold_reason"],
        "recommended_song_id": _recommended_song_id(row),
        "truth_song_id": "",
        "notes": "",
    }
    for rank in range(1, 4):
        song_id = str(row[f"jacket_top{rank}_song_id"] or "")
        song = master_by_id.get(song_id)
        value[f"jacket_top{rank}_song_id"] = song_id
        value[f"jacket_top{rank}_title"] = "" if song is None else song.title
        value[f"jacket_top{rank}_artist"] = "" if song is None else song.artist
        value[f"jacket_top{rank}_rank"] = rank if song_id else None
        value[f"jacket_top{rank}_distance"] = row[f"jacket_top{rank}_distance"]
    return value


def build_plan(config: CatalogPipelineConfig) -> dict[str, Any]:
    result = evaluate_policy_inputs(_policy_config(config))
    if result.metrics["false_decisions"] != 0:
        raise EvaluationError("policy produced false decisions; production apply is disabled")
    auto_rows = [row for row in result.rows if row["policy_decision"] in AUTO_ROUTES]
    manual_rows = [row for row in result.rows if row["outcome"] == "hold"]
    rejected_rows = [row for row in result.rows if row["outcome"] == "rejected"]
    auto_ids = {str(row["observation_id"]) for row in auto_rows}
    if len(auto_ids) != len(auto_rows):
        raise EvaluationError("policy produced duplicate auto-registration observations")
    jacket_reference_catalog.validate_catalog(config.catalog)
    catalog_rows = _catalog_rows(config.catalog)
    ods_rows = _review_ods_rows(config.observations_ods)
    expected_ids = {str(row["observation_id"]) for row in result.rows}
    if set(catalog_rows) != expected_ids or set(ods_rows) != expected_ids:
        raise EvaluationError("policy, catalog, and observations ODS sets differ")
    master_identity = jacket_reference_catalog.load_master_identity(config.master)
    confirmations = []
    apply_count = 0
    no_op_count = 0
    applied_at = datetime.now(UTC).isoformat(timespec="seconds")
    for row in sorted(auto_rows, key=lambda item: str(item["observation_id"])):
        observation_id = str(row["observation_id"])
        catalog_row = catalog_rows[observation_id]
        song_id = str(row["matched_song_id"])
        master_song = result.master_by_id.get(song_id)
        if master_song is None:
            raise EvaluationError(f"policy matched an invalid master song: {song_id}")
        evidence_json = _canonical_json(_evidence(row))
        no_op = _desired_matches(
            catalog_row, row, evidence_json, master_song, master_identity.version
        )
        if not no_op and (
            str(catalog_row["review_status"]) not in {"unresolved", "needs_review"}
            or catalog_row["song_id"] is not None
            or int(catalog_row["review_revision"]) != 0
            or catalog_row["manual_action_id"] is not None
            or str(catalog_row["manual_note"]) != ""
        ):
            raise EvaluationError(
                f"auto-registration conflicts with existing confirmed state: {observation_id}"
            )
        disposition = "no_op" if no_op else "apply"
        no_op_count += int(no_op)
        apply_count += int(not no_op)
        confirmations.append(
            {
                "observation_id": observation_id,
                "matched_song_id": song_id,
                "confirmation_source": row["confirmation_source"],
                "evidence_json": evidence_json,
                "expected_state_sha256": jacket_reference_catalog.auto_confirmation_state_sha256(
                    catalog_row
                ),
                "applied_at": applied_at,
                "disposition": disposition,
            }
        )
    guard_revision = catalog_guard_revision(config.catalog, auto_ids)
    manual = [
        _manual_row(
            row,
            catalog_rows[str(row["observation_id"])],
            ods_rows[str(row["observation_id"])],
            result.master_by_id,
        )
        for row in sorted(manual_rows, key=lambda item: int(item["audit_no"]))
    ]
    export_id = _sha256_json(
        {
            "schema_version": MANUAL_ODS_SCHEMA_VERSION,
            "catalog_guard_revision": guard_revision,
            "master_sha256": sha256_file(config.master),
            "policy_version": POLICY_VERSION,
            "rows": manual,
        }
    )
    plan: dict[str, Any] = {
        "schema_version": PLAN_SCHEMA_VERSION,
        "policy_version": POLICY_VERSION,
        "inputs": result.input_revisions,
        "source_revisions": {
            "catalog_guard_revision": guard_revision,
            "catalog_file_sha256": sha256_file(config.catalog),
            "master_version": master_identity.version,
            "policy_rows_sha256": _policy_revision(result.rows),
        },
        "counts": {
            "observations": len(result.rows),
            "auto": len(auto_rows),
            "apply": apply_count,
            "no_op": no_op_count,
            "manual": len(manual_rows),
            "rejected": len(rejected_rows),
            "false_decisions": result.metrics["false_decisions"],
        },
        "route_counts": result.metrics["route_counts"],
        "auto_confirmations": confirmations,
        "manual_reviews": manual,
        "rejected_observation_ids": sorted(
            str(row["observation_id"]) for row in rejected_rows
        ),
        "manual_export_id": export_id,
    }
    plan["plan_id"] = _sha256_json(plan)
    return plan


def write_plan(path: Path, plan: dict[str, Any]) -> None:
    path = _ensure_data_output(path, label="plan output", suffix=".json")
    if path.exists():
        raise ValueError(f"plan output already exists: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".tmp", dir=path.parent
    )
    os.close(descriptor)
    temporary = Path(temporary_name)
    try:
        temporary.write_text(
            json.dumps(plan, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        try:
            os.link(temporary, path)
        except FileExistsError as exc:
            raise ValueError(f"plan output already exists: {path}") from exc
    finally:
        temporary.unlink(missing_ok=True)


def load_plan(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    required = {
        "schema_version",
        "policy_version",
        "inputs",
        "source_revisions",
        "counts",
        "route_counts",
        "auto_confirmations",
        "manual_reviews",
        "rejected_observation_ids",
        "manual_export_id",
        "plan_id",
    }
    if not isinstance(value, dict) or set(value) != required:
        raise ValueError("registration plan has invalid fields")
    if value["schema_version"] != PLAN_SCHEMA_VERSION or value["policy_version"] != POLICY_VERSION:
        raise ValueError("registration plan version is unsupported")
    if not isinstance(value["inputs"], dict) or set(value["inputs"]) != {
        "snapshot",
        "truth_ods",
        "catalog",
        "master",
        "input_sha256",
        "manifest_sha256",
        "summary_sha256",
        "songs_sha256",
    }:
        raise ValueError("registration plan input revision fields are invalid")
    input_paths = {
        str(value["inputs"][key])
        for key in ("truth_ods", "catalog", "master")
    }
    input_hashes = value["inputs"]["input_sha256"]
    if (
        not isinstance(input_hashes, dict)
        or set(input_hashes) != input_paths
        or not all(_is_sha256(item) for item in input_hashes.values())
        or not all(
            _is_sha256(value["inputs"][key])
            for key in ("manifest_sha256", "summary_sha256", "songs_sha256")
        )
    ):
        raise ValueError("registration plan input hashes are invalid")
    if not isinstance(value["source_revisions"], dict) or set(value["source_revisions"]) != {
        "catalog_guard_revision",
        "catalog_file_sha256",
        "master_version",
        "policy_rows_sha256",
    }:
        raise ValueError("registration plan source revision fields are invalid")
    if not all(
        _is_sha256(value["source_revisions"][key])
        for key in (
            "catalog_guard_revision",
            "catalog_file_sha256",
            "policy_rows_sha256",
        )
    ):
        raise ValueError("registration plan source revision hashes are invalid")
    if not isinstance(value["counts"], dict) or set(value["counts"]) != {
        "observations",
        "auto",
        "apply",
        "no_op",
        "manual",
        "rejected",
        "false_decisions",
    } or any(type(item) is not int or item < 0 for item in value["counts"].values()):
        raise ValueError("registration plan counts are invalid")
    plan_id = value.pop("plan_id")
    try:
        valid_plan_id = isinstance(plan_id, str) and plan_id == _sha256_json(value)
    finally:
        value["plan_id"] = plan_id
    if not valid_plan_id:
        raise ValueError("registration plan identity mismatch")
    confirmations = value["auto_confirmations"]
    if not isinstance(confirmations, list):
        raise ValueError("registration plan auto confirmations must be a list")
    confirmation_fields = {
        "observation_id",
        "matched_song_id",
        "confirmation_source",
        "evidence_json",
        "expected_state_sha256",
        "applied_at",
        "disposition",
    }
    if any(
        not isinstance(item, dict) or set(item) != confirmation_fields
        for item in confirmations
    ):
        raise ValueError("registration plan auto confirmation fields are invalid")
    ids = [str(item.get("observation_id") or "") for item in confirmations]
    if not all(ids) or len(ids) != len(set(ids)):
        raise ValueError("registration plan contains duplicate observations")
    manual_reviews = value["manual_reviews"]
    if not isinstance(manual_reviews, list) or any(
        not isinstance(item, dict) for item in manual_reviews
    ):
        raise ValueError("registration plan manual reviews must be a list of objects")
    manual_ids = [str(item.get("observation_id") or "") for item in manual_reviews]
    rejected_ids = value["rejected_observation_ids"]
    if (
        not isinstance(value["rejected_observation_ids"], list)
        or not all(manual_ids)
        or len(manual_ids) != len(set(manual_ids))
        or not all(isinstance(item, str) and item for item in rejected_ids)
        or len(rejected_ids) != len(set(rejected_ids))
        or set(ids) & set(manual_ids)
        or set(ids) & set(rejected_ids)
        or set(manual_ids) & set(rejected_ids)
        or not isinstance(value["route_counts"], dict)
        or not set(value["route_counts"]).issubset(POLICY_ROUTES)
        or any(
            type(item) is not int or item < 0
            for item in value["route_counts"].values()
        )
        or not _is_sha256(value["manual_export_id"])
    ):
        raise ValueError("registration plan review fields are invalid")
    disposition_counts = {
        disposition: sum(item["disposition"] == disposition for item in confirmations)
        for disposition in ("apply", "no_op")
    }
    if (
        any(
            item["confirmation_source"]
            not in jacket_reference_catalog.AUTO_CONFIRMATION_SOURCES
            or item["disposition"] not in disposition_counts
            or not _is_sha256(item["expected_state_sha256"])
            for item in confirmations
        )
        or value["counts"]["observations"]
        != len(confirmations) + len(manual_ids) + len(rejected_ids)
        or value["counts"]["auto"] != len(confirmations)
        or value["counts"]["apply"] != disposition_counts["apply"]
        or value["counts"]["no_op"] != disposition_counts["no_op"]
        or value["counts"]["manual"] != len(manual_ids)
        or value["counts"]["rejected"] != len(rejected_ids)
        or sum(value["route_counts"].values()) != value["counts"]["observations"]
    ):
        raise ValueError("registration plan counts or routes are inconsistent")
    return value


def _assert_input_paths(config: CatalogPipelineConfig, plan: dict[str, Any]) -> None:
    expected = {
        "snapshot": str(config.snapshot.resolve()),
        "truth_ods": str(config.observations_ods.resolve()),
        "catalog": str(config.catalog.resolve()),
        "master": str(config.master.resolve()),
    }
    if any(plan["inputs"].get(key) != value for key, value in expected.items()):
        raise ValueError("registration plan input paths do not match apply inputs")


def _assert_policy_unchanged(
    config: CatalogPipelineConfig, plan: dict[str, Any]
) -> PolicyEvaluationResult:
    result = evaluate_policy_inputs(_policy_config(config))
    if _policy_revision(result.rows) != plan["source_revisions"]["policy_rows_sha256"]:
        raise ValueError("policy inputs changed after dry-run")
    hashes = plan["inputs"]["input_sha256"]
    for path in (config.observations_ods.resolve(), config.master.resolve()):
        if hashes.get(str(path)) != sha256_file(path):
            raise ValueError("non-catalog input changed after dry-run")
    for key, name in (
        ("manifest_sha256", "manifest.json"),
        ("summary_sha256", "summary.json"),
        ("songs_sha256", "songs.jsonl"),
    ):
        if plan["inputs"][key] != sha256_file(config.snapshot.resolve() / name):
            raise ValueError("snapshot input changed after dry-run")
    return result


def _assert_external_inputs_unchanged(
    config: CatalogPipelineConfig, plan: dict[str, Any]
) -> None:
    hashes = plan["inputs"]["input_sha256"]
    for path in (config.observations_ods.resolve(), config.master.resolve()):
        if hashes.get(str(path)) != sha256_file(path):
            raise ValueError("non-catalog input changed after dry-run")
    snapshot = config.snapshot.resolve()
    rows, fingerprints = load_snapshot(snapshot)
    expected = {
        key: plan["inputs"][key]
        for key in ("manifest_sha256", "summary_sha256", "songs_sha256")
    }
    if fingerprints != expected:
        raise ValueError("snapshot input changed after dry-run")
    verify_snapshot_images_unchanged(snapshot, load_snapshot_features(snapshot, rows))


def _assert_plan_semantics(
    config: CatalogPipelineConfig,
    plan: dict[str, Any],
    result: PolicyEvaluationResult,
) -> None:
    if result.metrics["false_decisions"] != 0:
        raise ValueError("current policy has false decisions; production apply is disabled")
    auto_rows = {
        str(row["observation_id"]): row
        for row in result.rows
        if row["policy_decision"] in AUTO_ROUTES
    }
    manual_ids = {
        str(row["observation_id"]) for row in result.rows if row["outcome"] == "hold"
    }
    rejected_ids = sorted(
        str(row["observation_id"]) for row in result.rows if row["outcome"] == "rejected"
    )
    confirmations = {
        str(item["observation_id"]): item for item in plan["auto_confirmations"]
    }
    if set(confirmations) != set(auto_rows):
        raise ValueError("registration plan auto target set does not match current policy")
    if {str(item["observation_id"]) for item in plan["manual_reviews"]} != manual_ids:
        raise ValueError("registration plan manual set does not match current policy")
    if plan["rejected_observation_ids"] != rejected_ids:
        raise ValueError("registration plan rejected set does not match current policy")
    expected_counts = {
        "observations": len(result.rows),
        "auto": len(auto_rows),
        "manual": len(manual_ids),
        "rejected": len(rejected_ids),
        "false_decisions": result.metrics["false_decisions"],
    }
    if any(plan["counts"].get(key) != value for key, value in expected_counts.items()):
        raise ValueError("registration plan counts do not match current policy")
    if plan["route_counts"] != result.metrics["route_counts"]:
        raise ValueError("registration plan routes do not match current policy")
    catalog_rows = _catalog_rows(config.catalog)
    ods_rows = _review_ods_rows(config.observations_ods)
    master_identity = jacket_reference_catalog.load_master_identity(config.master)
    for observation_id, row in auto_rows.items():
        item = confirmations[observation_id]
        evidence_json = _canonical_json(_evidence(row))
        if (
            item.get("matched_song_id") != row["matched_song_id"]
            or item.get("confirmation_source") != row["confirmation_source"]
            or item.get("evidence_json") != evidence_json
            or item.get("disposition") not in {"apply", "no_op"}
        ):
            raise ValueError("registration plan evidence does not match current policy")
        catalog_row = catalog_rows[observation_id]
        master_song = result.master_by_id[str(row["matched_song_id"])]
        if not _desired_matches(
            catalog_row, row, evidence_json, master_song, master_identity.version
        ) and item.get(
            "expected_state_sha256"
        ) != jacket_reference_catalog.auto_confirmation_state_sha256(catalog_row):
            raise ValueError("registration plan expected state is stale")
    expected_manual = [
        _manual_row(
            row,
            catalog_rows[str(row["observation_id"])],
            ods_rows[str(row["observation_id"])],
            result.master_by_id,
        )
        for row in sorted(
            (item for item in result.rows if item["outcome"] == "hold"),
            key=lambda item: int(item["audit_no"]),
        )
    ]
    if plan["manual_reviews"] != expected_manual:
        raise ValueError("registration plan manual review rows do not match current policy")
    expected_export_id = _sha256_json(
        {
            "schema_version": MANUAL_ODS_SCHEMA_VERSION,
            "catalog_guard_revision": plan["source_revisions"][
                "catalog_guard_revision"
            ],
            "master_sha256": sha256_file(config.master),
            "policy_version": POLICY_VERSION,
            "rows": expected_manual,
        }
    )
    if plan["manual_export_id"] != expected_export_id:
        raise ValueError("registration plan manual export identity does not match")


def apply_plan(
    config: CatalogPipelineConfig,
    plan_path: Path,
    *,
    fail_after_updates: int | None = None,
) -> dict[str, Any]:
    plan = load_plan(plan_path)
    _assert_input_paths(config, plan)
    result = _assert_policy_unchanged(config, plan)
    _assert_plan_semantics(config, plan, result)
    confirmations = plan["auto_confirmations"]
    auto_ids = {str(item["observation_id"]) for item in confirmations}
    expected_guard = str(plan["source_revisions"]["catalog_guard_revision"])

    def transaction_guard(connection: sqlite3.Connection) -> None:
        actual = catalog_guard_revision(
            config.catalog, auto_ids, connection=connection
        )
        if actual != expected_guard:
            raise ValueError("catalog revision changed after dry-run")

    def before_commit() -> None:
        _assert_external_inputs_unchanged(config, plan)

    requests = [
        jacket_reference_catalog.AutoConfirmationRequest(
            observation_id=str(item["observation_id"]),
            song_id=str(item["matched_song_id"]),
            confirmation_source=str(item["confirmation_source"]),
            evidence_json=str(item["evidence_json"]),
            expected_state_sha256=str(item["expected_state_sha256"]),
            applied_at=str(item["applied_at"]),
        )
        for item in confirmations
    ]
    receipt = jacket_reference_catalog.apply_auto_confirmation_batch(
        config.catalog,
        config.master,
        requests,
        transaction_guard=transaction_guard,
        before_commit=before_commit,
        fail_after_updates=fail_after_updates,
    )
    return {
        "plan_id": plan["plan_id"],
        "status": "applied" if receipt.applied_count else "no_op",
        "auto_count": receipt.requested_count,
        "applied_count": receipt.applied_count,
        "no_op_count": receipt.no_op_count,
        "manual_count": plan["counts"]["manual"],
        "rejected_count": plan["counts"]["rejected"],
    }


def export_manual_ods(path: Path, plan: dict[str, Any]) -> None:
    path = _ensure_data_output(path, label="manual ODS output", suffix=".ods")
    metadata = [
        ["schema_version", MANUAL_ODS_SCHEMA_VERSION],
        ["export_id", plan["manual_export_id"]],
        ["source_catalog_revision", plan["source_revisions"]["catalog_guard_revision"]],
        ["source_master_revision", plan["source_revisions"]["master_version"]],
        ["policy_version", plan["policy_version"]],
        ["manual_count", plan["counts"]["manual"]],
        ["rejected_count", plan["counts"]["rejected"]],
    ]
    rows = []
    for item in plan["manual_reviews"]:
        complete = {
            "schema_version": MANUAL_ODS_SCHEMA_VERSION,
            "export_id": plan["manual_export_id"],
            "source_catalog_revision": plan["source_revisions"]["catalog_guard_revision"],
            "source_master_revision": plan["source_revisions"]["master_version"],
            "policy_version": plan["policy_version"],
            **item,
        }
        rows.append([complete.get(header) for header in MANUAL_ODS_HEADERS])
    write_ods(
        path,
        [
            ("Metadata", ["key", "value"], metadata),
            ("Manual Review", MANUAL_ODS_HEADERS, rows),
        ],
    )
