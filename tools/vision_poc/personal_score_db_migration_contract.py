from __future__ import annotations

from dataclasses import dataclass

MIGRATION_CONTRACT_VERSION = 1
CURRENT_SCHEMA_VERSION = 1
# A transition is actionable only after its schema steps are explicitly registered.
# Version 1 -> 2 is the design contract exercised by this phase; its writer is not
# implemented here.
SUPPORTED_MIGRATION_TRANSITIONS = ((1, 2),)

DATABASE_STATES = (
    "compatible_current",
    "older_supported",
    "newer_unsupported",
    "unknown",
    "preview",
    "identity_mismatch",
    "partial_migration",
)
MIGRATION_STATUSES = (
    "current",
    "ready",
    "confirmation_required",
    "dry_run_ready",
    "rejected",
    "completed",
    "failed_before_source_change",
    "rolled_back",
    "manual_recovery_required",
)


@dataclass(frozen=True)
class MigrationRequest:
    database_state: str
    source_version: int | None
    target_version: int
    dry_run: bool
    explicit_confirmation: bool
    backup_path_is_safe: bool
    backup_path_is_new: bool


@dataclass(frozen=True)
class MigrationPlan:
    status: str
    reason: str
    exit_code: int
    may_create_backup: bool
    may_modify_source: bool
    steps: tuple[str, ...]


MIGRATION_EXECUTION_STEPS = (
    "inspect_source_read_only",
    "validate_formal_identity_and_history",
    "validate_target_and_supported_path",
    "validate_new_backup_path",
    "create_backup_exclusively",
    "flush_backup",
    "verify_backup_matches_source",
    "begin_immediate_transaction",
    "apply_schema_steps",
    "insert_schema_migration_history",
    "update_score_db_metadata_schema_version",
    "set_pragma_user_version",
    "verify_target_contract_inside_transaction",
    "commit_transaction",
    "reinspect_source_read_only",
)


def plan_personal_score_db_migration(request: MigrationRequest) -> MigrationPlan:
    if request.database_state not in DATABASE_STATES:
        raise ValueError(f"unknown database state: {request.database_state}")
    rejection_reasons = {
        "newer_unsupported": "newer_schema_not_supported",
        "unknown": "unknown_database_not_supported",
        "preview": "preview_database_not_supported",
        "identity_mismatch": "formal_database_identity_mismatch",
        "partial_migration": "partial_migration_state_requires_manual_recovery",
    }
    if request.database_state in rejection_reasons:
        return _plan("rejected", rejection_reasons[request.database_state], 2)
    if request.target_version <= CURRENT_SCHEMA_VERSION:
        if (
            request.database_state == "compatible_current"
            and request.source_version == request.target_version
        ):
            return _plan("current", "already_at_target_version", 0)
        return _plan("rejected", "target_version_must_be_newer", 2)
    if request.database_state == "compatible_current":
        return _plan("rejected", "no_supported_migration_path", 2)
    if request.source_version is None:
        return _plan("rejected", "source_version_missing", 2)
    if request.source_version >= request.target_version:
        return _plan("rejected", "source_version_must_be_older", 2)
    if (request.source_version, request.target_version) not in (
        SUPPORTED_MIGRATION_TRANSITIONS
    ):
        return _plan("rejected", "unsupported_migration_transition", 2)
    if not request.backup_path_is_safe:
        return _plan("rejected", "unsafe_backup_path", 2)
    if not request.backup_path_is_new:
        return _plan("rejected", "backup_path_conflict", 2)
    if request.dry_run:
        return _plan("dry_run_ready", "preflight_passed_without_side_effects", 0)
    if not request.explicit_confirmation:
        return _plan("confirmation_required", "explicit_confirmation_required", 1)
    return _plan(
        "ready",
        "explicit_migration_preflight_passed",
        0,
        may_create_backup=True,
        steps=MIGRATION_EXECUTION_STEPS,
    )


def migration_failure_result(failed_step: str) -> MigrationPlan:
    if failed_step not in MIGRATION_EXECUTION_STEPS:
        raise ValueError(f"unknown migration step: {failed_step}")
    source_change_step = MIGRATION_EXECUTION_STEPS.index("begin_immediate_transaction")
    failed_index = MIGRATION_EXECUTION_STEPS.index(failed_step)
    commit_step = MIGRATION_EXECUTION_STEPS.index("commit_transaction")
    if failed_index < source_change_step:
        return _plan(
            "failed_before_source_change",
            f"{failed_step}_failed",
            3,
            may_create_backup=failed_index >= MIGRATION_EXECUTION_STEPS.index(
                "create_backup_exclusively"
            ),
        )
    if failed_index < commit_step:
        return _plan(
            "rolled_back",
            f"{failed_step}_failed_transaction_rolled_back",
            3,
            may_create_backup=True,
        )
    return _plan(
        "manual_recovery_required",
        f"{failed_step}_failed_source_state_requires_verification",
        3,
        may_create_backup=True,
    )


def _plan(
    status: str,
    reason: str,
    exit_code: int,
    *,
    may_create_backup: bool = False,
    steps: tuple[str, ...] = (),
) -> MigrationPlan:
    return MigrationPlan(
        status=status,
        reason=reason,
        exit_code=exit_code,
        may_create_backup=may_create_backup,
        may_modify_source=status == "ready",
        steps=steps,
    )
