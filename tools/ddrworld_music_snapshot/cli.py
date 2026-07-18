from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .collector import (
    DEFAULT_DELAY_SECONDS,
    DEFAULT_PAGE_COUNT,
    SnapshotCollector,
    SnapshotConfig,
    SnapshotError,
    iter_request_plan,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Plan or fetch a one-time local DDR WORLD music snapshot."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    plan = subparsers.add_parser("plan", help="Print a network-free request and wait estimate.")
    plan.add_argument("--page-count", type=int, default=DEFAULT_PAGE_COUNT)
    plan.add_argument("--estimated-songs", type=int, default=1300)
    plan.add_argument("--delay-seconds", type=float, default=DEFAULT_DELAY_SECONDS)
    plan.add_argument("--snapshot-id", default="YYYYMMDDTHHMMSSZ")
    plan.add_argument("--output-root", type=Path, default=Path("data/ddrworld_music_snapshot"))

    fetch = subparsers.add_parser("fetch", help="Fetch pages and jackets serially.")
    fetch.add_argument("--allow-network", action="store_true", help="Required network opt-in.")
    fetch.add_argument("--snapshot-id", required=True)
    fetch.add_argument("--output-root", type=Path, default=Path("data/ddrworld_music_snapshot"))
    fetch.add_argument("--page-count", type=int, default=DEFAULT_PAGE_COUNT)
    fetch.add_argument("--delay-seconds", type=float, default=DEFAULT_DELAY_SECONDS)
    fetch.add_argument("--connect-timeout-seconds", type=float, default=10.0)
    fetch.add_argument("--read-timeout-seconds", type=float, default=30.0)
    return parser


def config_from_args(args: argparse.Namespace) -> SnapshotConfig:
    return SnapshotConfig(
        snapshot_id=args.snapshot_id,
        output_root=args.output_root,
        page_count=args.page_count,
        delay_seconds=args.delay_seconds,
        connect_timeout_seconds=getattr(args, "connect_timeout_seconds", 10.0),
        read_timeout_seconds=getattr(args, "read_timeout_seconds", 30.0),
    )


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        config = config_from_args(args)
        config.validate()
        if args.command == "plan":
            if args.estimated_songs < 0:
                raise SnapshotError("estimated song count must not be negative")
            request_plan = list(iter_request_plan(config, args.estimated_songs))
            total_requests = sum(count for _, count in request_plan)
            minimum_wait_seconds = max(total_requests - 1, 0) * config.delay_seconds
            print(f"snapshot output: {(config.output_root / config.snapshot_id).resolve()}")
            for label, count in request_plan:
                print(f"{label} requests: {count}")
            print(f"maximum requests: {total_requests}")
            print(f"minimum inter-request wait: {minimum_wait_seconds:.1f} seconds")
            print("concurrency: 1; automatic retries: 0; existing outputs: never overwritten")
            return 0
        if not args.allow_network:
            raise SnapshotError("fetch requires the explicit --allow-network option")
        output = SnapshotCollector(config).collect()
        print(f"completed snapshot: {output}")
        return 0
    except SnapshotError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
