from __future__ import annotations

import argparse
import json
from pathlib import Path

from .catalog_pipeline import (
    CatalogPipelineConfig,
    apply_plan,
    build_plan,
    export_manual_ods,
    write_plan,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Dry-run or atomically apply the developer-only jacket catalog policy"
    )
    parser.add_argument("--snapshot", type=Path, required=True)
    parser.add_argument("--observations-ods", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, required=True)
    parser.add_argument("--master", type=Path, required=True)
    parser.add_argument("--plan-output", type=Path)
    parser.add_argument("--plan-input", type=Path)
    parser.add_argument("--manual-ods-output", type=Path)
    parser.add_argument("--apply", action="store_true")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    config = CatalogPipelineConfig(
        snapshot=args.snapshot,
        observations_ods=args.observations_ods,
        catalog=args.catalog,
        master=args.master,
    )
    try:
        if args.apply:
            if args.plan_input is None or args.plan_output is not None:
                raise ValueError("--apply requires --plan-input and forbids --plan-output")
            if args.manual_ods_output is not None:
                raise ValueError("manual ODS export is a separate read-only dry-run operation")
            result = apply_plan(config, args.plan_input)
        else:
            if args.plan_output is None or args.plan_input is not None:
                raise ValueError("dry-run requires --plan-output and forbids --plan-input")
            plan = build_plan(config)
            write_plan(args.plan_output, plan)
            if args.manual_ods_output is not None:
                export_manual_ods(args.manual_ods_output, plan)
            result = {
                "plan_id": plan["plan_id"],
                "status": "dry_run",
                "plan_output": str(args.plan_output.resolve()),
                "manual_ods_output": (
                    None
                    if args.manual_ods_output is None
                    else str(args.manual_ods_output.resolve())
                ),
                **plan["counts"],
            }
    except (OSError, RuntimeError, ValueError) as exc:
        raise SystemExit(str(exc)) from exc
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
