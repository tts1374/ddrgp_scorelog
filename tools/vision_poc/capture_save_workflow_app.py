from __future__ import annotations

import argparse
import json
import sys
from collections.abc import Sequence
from pathlib import Path

from .capture_save_workflow import (
    capture_save_session_result_json,
    run_capture_save_session,
)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--master-database", type=Path, required=True)
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Optional transient analysis output directory.",
    )
    parser.add_argument(
        "--transient-source",
        default=None,
        help="Logical source reference used when the candidate image is not retained.",
    )
    args = parser.parse_args(argv)
    result = run_capture_save_session(
        manifest_path=args.manifest.resolve(),
        master_db_path=args.master_database.resolve(),
        db_path=args.database.resolve(),
        repository_root=Path.cwd().resolve(),
        output_dir=args.output.resolve() if args.output is not None else None,
        source_path=args.transient_source,
        retain_image_reference=args.transient_source is None,
    )
    payload = capture_save_session_result_json(result)
    exit_code = 0 if result.status == "completed" else 2
    print(
        json.dumps(payload, ensure_ascii=False, sort_keys=True),
        file=sys.stdout if exit_code == 0 else sys.stderr,
    )
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
