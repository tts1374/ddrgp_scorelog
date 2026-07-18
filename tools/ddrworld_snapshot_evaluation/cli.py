from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .evaluator import EvaluationConfig, EvaluationError, evaluate_snapshot


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Evaluate a completed DDR WORLD snapshot against confirmed grid jackets."
    )
    parser.add_argument("--snapshot", type=Path, required=True)
    parser.add_argument("--truth-ods", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, required=True)
    parser.add_argument("--master", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        output = evaluate_snapshot(
            EvaluationConfig(
                snapshot=args.snapshot,
                truth_ods=args.truth_ods,
                catalog=args.catalog,
                master=args.master,
                output=args.output,
            )
        )
        print(f"completed evaluation: {output}")
        return 0
    except EvaluationError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
