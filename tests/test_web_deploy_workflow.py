from pathlib import Path

import pytest

WORKFLOW = Path(__file__).resolve().parents[1] / ".github/workflows/deploy-web.yml"


def test_shared_master_import_must_succeed_before_worker_deploy() -> None:
    workflow = WORKFLOW.read_text(encoding="utf-8")
    commands = [
        "gh run list",
        "gh api",
        "gh run download",
        "uv sync --frozen",
        "uv run python -m master.inspect data/master/ddrgp-master.sqlite",
        "uv run python -m master.d1_export",
        "npm run migrate:remote",
        "npx wrangler d1 execute DB --remote --file ../../data/master/ddrgp-web-master.sql",
        "npm run deploy",
    ]

    positions = [workflow.index(command) for command in commands]

    assert positions == sorted(positions)
    assert "--branch main --status success" in workflow
    assert 'test -n "$master_run_id"' in workflow
    assert 'test "$master_run_id" != "null"' in workflow
    assert 'test -n "$master_artifact_name"' in workflow
    assert '--name "$master_artifact_name" --dir data/master' in workflow
    assert "continue-on-error:" not in workflow
    assert "always()" not in workflow


@pytest.mark.parametrize("source", ["master/**", "pyproject.toml", "uv.lock"])
def test_shared_master_sources_trigger_production_deploy(source: str) -> None:
    workflow = WORKFLOW.read_text(encoding="utf-8")
    push_paths = workflow.split("    paths:\n", 1)[1].split("  workflow_dispatch:", 1)[0]

    assert f'      - "{source}"' in push_paths
