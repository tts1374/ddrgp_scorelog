from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .evaluator import EvaluationError
from .policy import PolicyEvaluationConfig, evaluate_policy


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Evaluate jacket/OCR auto-registration policy without writes."
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
        output = evaluate_policy(
            PolicyEvaluationConfig(
                snapshot=args.snapshot,
                truth_ods=args.truth_ods,
                catalog=args.catalog,
                master=args.master,
                output=args.output,
            )
        )
        print(f"completed policy evaluation: {output}")
        return 0
    except EvaluationError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
