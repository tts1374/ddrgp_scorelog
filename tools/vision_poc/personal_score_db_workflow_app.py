from __future__ import annotations

import argparse
from collections.abc import Sequence
from pathlib import Path

from .personal_score_db_workflow import run_personal_score_db_workflow_cli


def main(argv: Sequence[str] | None = None) -> int:
    """Run the existing formal-save workflow for the local WPF application."""
    parser = argparse.ArgumentParser(add_help=True)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--database", type=Path, required=True)
    args = parser.parse_args(argv)
    return run_personal_score_db_workflow_cli(
        input_path=args.input,
        artifact_output=None,
        db_path=args.database,
        use_input_log_path=True,
    )


if __name__ == "__main__":
    raise SystemExit(main())
