from __future__ import annotations

import argparse
import json
import sqlite3
from collections.abc import Iterable
from contextlib import closing
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from tools.ddrworld_snapshot_evaluation.policy import AUTO_ROUTES, POLICY_VERSION
from tools.vision_poc import (
    jacket_catalog_review_projection as projection,
)
from tools.vision_poc import (
    jacket_reference_catalog as catalog,
)
from tools.vision_poc import (
    title_artist_evaluation,
    unresolved_candidate_evaluation,
)

COLLECTION_AUTO_CONFIRMATION_SCHEMA_VERSION = "m5c-collection-end-auto-confirmation-v1"
AUTO_POLICY_ROUTE = "auto_ocr_title_artist_pair"
if AUTO_POLICY_ROUTE not in AUTO_ROUTES:
    raise RuntimeError(f"existing auto-confirmation policy route is missing: {AUTO_POLICY_ROUTE}")
CONFIRMATION_SOURCE = AUTO_POLICY_ROUTE.removeprefix("auto_")
AUTO_CLASSIFICATIONS = {"exact_unique", "alias_unique"}
MANUAL_REVIEW_STATUSES = {"unresolved", "needs_review", "orphaned"}


@dataclass(frozen=True)
class CollectionAutoConfirmationResult:
    session_id: str
    requested_count: int
    applied_count: int
    no_op_count: int
    auto_confirmed_count: int
    remaining_count: int

    def as_dict(self) -> dict[str, Any]:
        return {
            "collection_auto_confirmation_schema_version": (
                COLLECTION_AUTO_CONFIRMATION_SCHEMA_VERSION
            ),
            "session_id": self.session_id,
            "requested_count": self.requested_count,
            "applied_count": self.applied_count,
            "no_op_count": self.no_op_count,
            "auto_confirmed_count": self.auto_confirmed_count,
            "remaining_count": self.remaining_count,
        }


def _canonical_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def _session_directory(artifact_root: Path, session_id: str) -> Path:
    relative = Path(session_id)
    if (
        not session_id
        or relative.is_absolute()
        or session_id in {".", ".."}
        or relative.name != session_id
    ):
        raise ValueError("collection session id must be a single path component")
    root = artifact_root.resolve()
    session_directory = (root / relative).resolve()
    try:
        session_directory.relative_to(root)
    except ValueError as exception:
        raise ValueError("collection session id escapes the artifact root") from exception
    return session_directory


def _checkpoint_observation_ids(artifact_root: Path, session_id: str) -> tuple[str, ...]:
    checkpoint_path = _session_directory(artifact_root, session_id) / "checkpoint.json"
    document = title_artist_evaluation._read_strict_json(checkpoint_path)
    if not isinstance(document, dict):
        raise ValueError("collection checkpoint is not an object")
    title_artist_evaluation._require_exact_keys(
        document, unresolved_candidate_evaluation.CHECKPOINT_KEYS, "collection checkpoint"
    )
    version = document["checkpoint_version"]
    if version not in {
        "m5c-observation-checkpoint-v1",
        "m5c-observation-checkpoint-v2",
    }:
        raise ValueError("collection checkpoint version is unsupported")
    session = document["session"]
    if not isinstance(session, dict):
        raise ValueError("collection checkpoint session is invalid")
    title_artist_evaluation._require_exact_keys(
        session,
        unresolved_candidate_evaluation.CHECKPOINT_SESSION_KEYS,
        "collection checkpoint session",
    )
    if session.get("session_id") != session_id:
        raise ValueError("collection checkpoint session id does not match the request")
    observations = document["observations"]
    if not isinstance(observations, list):
        raise ValueError("collection checkpoint observations are invalid")
    expected_keys = (
        unresolved_candidate_evaluation.CHECKPOINT_OBSERVATION_KEYS_V2
        if version.endswith("v2")
        else unresolved_candidate_evaluation.CHECKPOINT_OBSERVATION_KEYS_V1
    )
    observation_ids: list[str] = []
    for observation in observations:
        if not isinstance(observation, dict):
            raise ValueError("collection checkpoint observation is invalid")
        title_artist_evaluation._require_exact_keys(
            observation, expected_keys, "collection checkpoint observation"
        )
        observation_id = observation["observation_id"]
        if not isinstance(observation_id, str) or not observation_id.strip():
            raise ValueError("collection checkpoint observation id is invalid")
        if observation_id in observation_ids:
            raise ValueError("collection checkpoint observation id is duplicated")
        if observation["catalog_status"] != "ingested":
            raise ValueError("collection checkpoint still has pending catalog observations")
        observation_ids.append(observation_id)
    return tuple(observation_ids)


def _evidence(row: sqlite3.Row, evaluation: dict[str, Any]) -> str:
    candidate = evaluation["candidates"][0]
    song_id = candidate["song_id"]
    match_kind = (
        "canonical_exact"
        if evaluation["reason"] == "canonical_title_artist_exact"
        else "unique_alias"
    )

    def profile(field: str) -> dict[str, Any]:
        value = evaluation[field]
        return {
            "profile_id": evaluation["method_version"],
            "raw": value["raw"],
            "normalized": value["normalized"],
            "confidence": value["confidence"],
            "status": value["status"],
            "failure_reason": value["failure_reason"],
            "resolution_status": "resolved",
            "candidate_song_ids": [song_id],
            "match_kind": match_kind,
        }

    value = {
        "evidence_schema_version": catalog.AUTO_CONFIRMATION_EVIDENCE_SCHEMA_VERSION,
        "observation_id": evaluation["observation_id"],
        "confirmation_source": CONFIRMATION_SOURCE,
        "matched_song_id": song_id,
        "policy_version": POLICY_VERSION,
        "snapshot_id": None,
        "feature_extractor_version": str(row["feature_extractor_version"]),
        "jacket_feature_version": str(row["jacket_feature_version"]),
        "jacket_distance": None,
        "jacket_margin": None,
        "jacket_rank": None,
        "ocr_profile": evaluation["method_version"],
        "title_ocr_profiles": [profile("title")],
        "artist_ocr_profiles": [profile("artist")],
        "title_artist_pair_resolutions": [
            {
                "configuration_id": evaluation["method_version"],
                "title_profile_id": evaluation["method_version"],
                "artist_profile_id": evaluation["method_version"],
                "title_raw": evaluation["title"]["raw"],
                "title_normalized": evaluation["title"]["normalized"],
                "artist_raw": evaluation["artist"]["raw"],
                "artist_normalized": evaluation["artist"]["normalized"],
                "resolution_status": "resolved",
                "candidate_song_ids": [song_id],
                "match_kind": match_kind,
            }
        ],
    }
    return _canonical_json(value)


def _session_rows(
    catalog_path: Path,
    observation_ids: Iterable[str],
) -> tuple[int, int]:
    observation_ids = tuple(observation_ids)
    if not observation_ids:
        return 0, 0
    placeholders = ",".join("?" for _ in observation_ids)
    with closing(catalog._connect_read_only(catalog_path)) as connection:
        connection.row_factory = sqlite3.Row
        catalog._validate_catalog(connection)
        rows = list(
            connection.execute(
                f"SELECT * FROM jacket_references WHERE source_capture_id IN ({placeholders})",
                observation_ids,
            )
        )
    if len(rows) != len(observation_ids):
        raise ValueError("collection checkpoint and catalog observations do not match")
    # The WPF manual-review list uses the persisted status as its visibility
    # boundary.  Keep this count aligned with that existing projection/UI
    # contract even if a previously confirmed row is now master-drifted.
    statuses = [str(row["review_status"]) for row in rows]
    return statuses.count("auto_confirmed"), sum(
        status in MANUAL_REVIEW_STATUSES for status in statuses
    )


def apply_collection_auto_confirmation(
    catalog_path: Path,
    master_db: Path,
    artifact_root: Path,
    session_id: str,
    *,
    extractor: title_artist_evaluation.Extractor = title_artist_evaluation.extract_field,
    applied_at: str | None = None,
) -> CollectionAutoConfirmationResult:
    """Evaluate one finalized collection and atomically apply its safe matches."""
    observation_ids = _checkpoint_observation_ids(artifact_root, session_id)
    current_master = catalog.load_master_identity(master_db)
    current_projection = projection.build_review_projection(
        catalog_path,
        master_db,
        artifact_root=artifact_root,
        extractor=extractor,
    )
    selected_ids = set(observation_ids)
    requests: list[catalog.AutoConfirmationRequest] = []
    placeholders = ",".join("?" for _ in observation_ids)
    with closing(catalog._connect_read_only(catalog_path)) as connection:
        connection.row_factory = sqlite3.Row
        catalog._validate_catalog(connection)
        rows_by_observation = {
            str(row["source_capture_id"]): row
            for row in connection.execute(
                "SELECT * FROM jacket_references "
                f"WHERE source_capture_id IN ({placeholders})",
                observation_ids,
            )
        } if observation_ids else {}
        for reference in current_projection["review_references"]:
            candidate = reference["candidate_evaluation"]
            observation_id = str(candidate["observation_id"])
            if (
                observation_id not in selected_ids
                or reference["stored_status"] != "unresolved"
                or reference["review_status"] != "unresolved"
                or candidate["classification"] not in AUTO_CLASSIFICATIONS
                or len(candidate["candidates"]) != 1
            ):
                continue
            row = rows_by_observation.get(observation_id)
            if row is None:
                raise ValueError("projection and catalog observations do not match")
            song_id = str(candidate["candidates"][0]["song_id"])
            if not any(
                song.song_id == song_id and song.grand_prix_play_available
                for song in current_master.songs
            ):
                continue
            requests.append(
                catalog.AutoConfirmationRequest(
                    observation_id=observation_id,
                    song_id=song_id,
                    confirmation_source=CONFIRMATION_SOURCE,
                    evidence_json=_evidence(row, candidate),
                    expected_state_sha256=catalog.auto_confirmation_state_sha256(row),
                    applied_at=applied_at or datetime.now(UTC).isoformat(timespec="seconds"),
                )
            )

    receipt = catalog.apply_auto_confirmation_batch(catalog_path, master_db, requests)
    auto_confirmed_count, remaining_count = _session_rows(catalog_path, observation_ids)
    return CollectionAutoConfirmationResult(
        session_id=session_id,
        requested_count=receipt.requested_count,
        applied_count=receipt.applied_count,
        no_op_count=receipt.no_op_count,
        auto_confirmed_count=auto_confirmed_count,
        remaining_count=remaining_count,
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Evaluate and atomically auto-confirm one finalized jacket collection."
    )
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--master-db", required=True, type=Path)
    parser.add_argument("--artifact-root", required=True, type=Path)
    parser.add_argument("--session-id", required=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    catalog.ensure_catalog_path(args.catalog, argument_name="--catalog")
    catalog.ensure_catalog_path(args.master_db, argument_name="--master-db")
    artifact_root = title_artist_evaluation._require_under_data(
        args.artifact_root, "artifact root"
    )
    result = apply_collection_auto_confirmation(
        args.catalog,
        args.master_db,
        artifact_root,
        args.session_id,
    )
    print(json.dumps(result.as_dict(), ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
