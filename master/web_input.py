from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sqlite3
from contextlib import closing
from pathlib import Path
from urllib.request import urlopen

from .d1_export import export_shared_master_sql
from .inspect import inspect_master_database

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PIN = ROOT / ".github/web-master.json"
MANIFEST_FIELDS = (
    "content_version", "master_schema_version", "catalog_schema_version",
    "master_content_version", "catalog_master_content_version",
    "master_sha256", "catalog_sha256",
)


def validate_pin(pin: dict) -> None:
    # Resolve an explicit Release tag, never latest or an Actions artifact.
    if not re.fullmatch(
        r"https://github\.com/[^/]+/[^/]+/releases/download/[^/?#]+",
        pin["release_base_url"],
    ) or pin["release_base_url"].rsplit("/", 1)[1] == "latest":
        raise ValueError("release_base_url must name an explicit GitHub Release tag")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:\.\d+)?", pin["content_version"]):
        raise ValueError("invalid reference content_version")
    if pin["master_schema_version"] != 1 or pin["catalog_schema_version"] != 1:
        raise ValueError("unsupported reference schema version")
    if not pin["master_content_version"] or (
        pin["catalog_master_content_version"] != pin["master_content_version"]
    ):
        raise ValueError("reference master/catalog version mismatch")
    for key in ("manifest_sha256", "master_sha256", "catalog_sha256"):
        if not re.fullmatch(r"[0-9a-f]{64}", pin[key]):
            raise ValueError(f"invalid {key}")


def download_asset(base_url: str, name: str, destination: Path) -> None:
    with urlopen(f"{base_url}/{name}", timeout=60) as source:
        with destination.open("xb") as output:
            shutil.copyfileobj(source, output)


def validate_checksum(path: Path, expected: str) -> None:
    with path.open("rb") as source:
        actual = hashlib.file_digest(source, "sha256").hexdigest()
    if actual != expected:
        raise ValueError(f"checksum mismatch: {path.name}")


def prepare_web_master(pin_path: Path, output_dir: Path) -> Path:
    pin = json.loads(pin_path.read_text(encoding="utf-8"))
    validate_pin(pin)
    # A fresh directory prevents stale SQL from being mistaken for this run's output.
    output_dir.mkdir(parents=True, exist_ok=False)
    manifest_path = output_dir / "reference-set.json"
    download_asset(pin["release_base_url"], manifest_path.name, manifest_path)
    validate_checksum(manifest_path, pin["manifest_sha256"])
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if any(manifest.get(key) != pin[key] for key in MANIFEST_FIELDS):
        raise ValueError("reference manifest does not match the pinned metadata")

    master_path = output_dir / "ddrgp-master.sqlite"
    download_asset(pin["release_base_url"], master_path.name, master_path)
    validate_checksum(master_path, manifest["master_sha256"])
    uri = f"file:{master_path.resolve().as_posix()}?mode=ro"
    with closing(sqlite3.connect(uri, uri=True)) as connection:
        if connection.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
            raise ValueError("master DB integrity check failed")
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
    if metadata.get("master_version") != manifest["master_content_version"]:
        raise ValueError("master DB version does not match the reference manifest")
    summary = inspect_master_database(master_path)
    sql = export_shared_master_sql(master_path)
    # Validate the export against the checkout's actual additive migrations before D1.
    with closing(sqlite3.connect(":memory:")) as target:
        for migration in sorted((ROOT / "web/identity-api/migrations").glob("*.sql")):
            target.executescript(migration.read_text(encoding="utf-8"))
        target.executescript(sql)
        if target.execute("PRAGMA foreign_key_check").fetchall():
            raise ValueError("export has foreign-key integrity errors")
    (output_dir / "master-summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
    )
    sql_path = output_dir / "ddrgp-web-master.sql"
    sql_path.write_text(sql, encoding="utf-8", newline="\n")
    return sql_path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Fetch and validate the pinned reference master, then export Web D1 SQL."
    )
    parser.add_argument("--pin", type=Path, default=DEFAULT_PIN)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args(argv)
    print(prepare_web_master(args.pin, args.output_dir))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
