from __future__ import annotations

import json
from pathlib import Path

import pytest

from tools.vision_poc import personal_score_db_migration_contract as contract

FIXTURE_PATH = (
    Path(__file__).parent
    / "fixtures"
    / "personal_score_db_migration"
    / "plan-matrix-v1.json"
)


@pytest.mark.parametrize(
    "case",
    json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))["cases"],
    ids=lambda case: case["name"],
)
def test_migration_plan_fixture_matrix(case: dict[str, object]) -> None:
    request_keys = {
        "database_state",
        "source_version",
        "target_version",
        "dry_run",
        "explicit_confirmation",
        "backup_path_is_safe",
        "backup_path_is_new",
    }
    request = contract.MigrationRequest(
        **{key: value for key, value in case.items() if key in request_keys}
    )
    plan = contract.plan_personal_score_db_migration(request)

    assert plan.status == case["status"]
    assert plan.reason == case["reason"]
    assert plan.exit_code == case["exit_code"]
    if plan.status != "ready":
        assert not plan.may_modify_source
    if plan.status == "dry_run_ready":
        assert not plan.may_create_backup
        assert plan.steps == contract.MIGRATION_EXECUTION_STEPS


def test_explicit_plan_orders_backup_verification_before_source_change(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(contract, "CURRENT_SCHEMA_VERSION", 2)
    plan = contract.plan_personal_score_db_migration(
        contract.MigrationRequest(
            database_state="older_supported",
            source_version=1,
            target_version=2,
            dry_run=False,
            explicit_confirmation=True,
            backup_path_is_safe=True,
            backup_path_is_new=True,
        )
    )

    assert plan.status == "ready"
    assert plan.steps.index("verify_backup_matches_source") < plan.steps.index(
        "begin_immediate_transaction"
    )
    assert plan.steps.index("insert_schema_migration_history") < plan.steps.index(
        "update_score_db_metadata_schema_version"
    ) < plan.steps.index("set_pragma_user_version")
    assert plan.steps[-2:] == ("commit_transaction", "reinspect_source_read_only")


def test_registered_transition_can_target_future_current_schema(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(contract, "CURRENT_SCHEMA_VERSION", 2)

    plan = contract.plan_personal_score_db_migration(
        contract.MigrationRequest(
            database_state="older_supported",
            source_version=1,
            target_version=2,
            dry_run=True,
            explicit_confirmation=False,
            backup_path_is_safe=True,
            backup_path_is_new=True,
        )
    )

    assert plan.status == "dry_run_ready"
    assert plan.steps == contract.MIGRATION_EXECUTION_STEPS


@pytest.mark.parametrize(
    ("backup_path_is_safe", "backup_path_is_new", "expected_status", "expected_reason"),
    [
        (True, True, "ready", "explicit_migration_preflight_passed"),
        (False, True, "rejected", "unsafe_backup_path"),
        (True, False, "rejected", "backup_path_conflict"),
    ],
)
def test_current_version_transition_checks_confirmation_and_backup(
    monkeypatch: pytest.MonkeyPatch,
    backup_path_is_safe: bool,
    backup_path_is_new: bool,
    expected_status: str,
    expected_reason: str,
) -> None:
    monkeypatch.setattr(contract, "CURRENT_SCHEMA_VERSION", 2)

    plan = contract.plan_personal_score_db_migration(
        contract.MigrationRequest(
            database_state="older_supported",
            source_version=1,
            target_version=2,
            dry_run=False,
            explicit_confirmation=True,
            backup_path_is_safe=backup_path_is_safe,
            backup_path_is_new=backup_path_is_new,
        )
    )

    assert plan.status == expected_status
    assert plan.reason == expected_reason


def test_unregistered_transition_is_rejected_when_target_is_current(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(contract, "CURRENT_SCHEMA_VERSION", 3)

    plan = contract.plan_personal_score_db_migration(
        contract.MigrationRequest(
            database_state="older_supported",
            source_version=1,
            target_version=3,
            dry_run=True,
            explicit_confirmation=False,
            backup_path_is_safe=True,
            backup_path_is_new=True,
        )
    )

    assert plan.status == "rejected"
    assert plan.reason == "unsupported_migration_transition"


@pytest.mark.parametrize("failed_step", contract.MIGRATION_EXECUTION_STEPS)
def test_each_failure_step_has_a_fixed_recovery_boundary(failed_step: str) -> None:
    result = contract.migration_failure_result(failed_step)

    assert result.exit_code == 3
    if contract.MIGRATION_EXECUTION_STEPS.index(failed_step) < (
        contract.MIGRATION_EXECUTION_STEPS.index("begin_immediate_transaction")
    ):
        assert result.status == "failed_before_source_change"
        assert not result.may_modify_source
    elif contract.MIGRATION_EXECUTION_STEPS.index(failed_step) < (
        contract.MIGRATION_EXECUTION_STEPS.index("commit_transaction")
    ):
        assert result.status == "rolled_back"
        assert result.may_create_backup
        assert not result.may_modify_source
    else:
        assert result.status == "manual_recovery_required"
        assert result.may_create_backup
        assert result.may_modify_source


def test_fixture_contract_version_matches_code() -> None:
    fixture = json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))
    assert fixture["contract_version"] == contract.MIGRATION_CONTRACT_VERSION
