"""Export a read-only near-jacket inventory for confirmed GRID references.

The inventory is intentionally a developer-only report.  It compares the
persisted M5 jacket features for confirmed catalog references, then keeps
only song pairs that share at least one chart signature and are within the
requested jacket distance.  It never writes either input database.
"""

from __future__ import annotations

import argparse
import json
import os
import sqlite3
import tempfile
from collections import defaultdict
from contextlib import closing
from datetime import UTC, datetime
from itertools import combinations
from pathlib import Path
from typing import Any

from tools.ddrworld_snapshot_evaluation.xlsx_export import write_xlsx
from tools.vision_poc import jacket_reference_catalog, master_match

DEFAULT_DISTANCE_THRESHOLD = 0.12
OUTPUT_DIRECTORY = "data/near-jacket-inventory"
OUTPUT_FILENAME = "near-jacket-inventory.xlsx"
CONFIRMED_REFERENCE_STATUSES = ("auto_confirmed", "manual_confirmed")
RISK_BANDS = (
    (0.05, "A: extremely close"),
    (0.10, "B: high"),
    (0.11, "C: elevated"),
)
INVENTORY_DEFINITION = (
    "confirmed GRID jacket references; pair shares at least one "
    "play_style/difficulty/level; minimum jacket distance <= threshold"
)


def _repository_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _read_only_connection(path: Path) -> sqlite3.Connection:
    if not path.is_file():
        raise ValueError(f"database is not a file: {path}")
    connection = sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=ro", uri=True)
    connection.row_factory = sqlite3.Row
    return connection


def _feature_status(fields: set[str]) -> str:
    if fields == {"title", "artist"}:
        return "complete"
    if fields == {"title"}:
        return "missing artist"
    if fields == {"artist"}:
        return "missing title"
    return "missing title/artist"


def _risk_band(distance: float) -> str:
    for upper_bound, label in RISK_BANDS:
        if distance <= upper_bound:
            return label
    return "D: watch"


def _signature_label(signature: tuple[str, str, int]) -> str:
    play_style, difficulty, level = signature
    return f"{play_style} / {difficulty} / Lv{level}"


def _load_confirmed_jacket_features(
    catalog_path: Path,
    master: jacket_reference_catalog.MasterIdentity,
) -> tuple[dict[str, list[master_match.JacketFeature]], dict[str, int]]:
    """Load confirmed jacket features while preserving catalog history.

    A release catalog can retain references captured against older master
    versions.  Those references are still useful for this visual comparison,
    so the inventory uses the current persisted jacket feature extractor and
    resolves their song labels against the current master.  Orphaned and
    invalid rows are reported as hard errors instead of silently disappearing.
    """

    songs_by_id = {song.song_id: song for song in master.songs}
    by_song: dict[str, list[master_match.JacketFeature]] = defaultdict(list)
    master_versions: set[str] = set()
    counts: dict[str, Any] = {
        "reference_row_count": 0,
        "reference_song_count": 0,
        "skipped_orphan_reference_count": 0,
    }
    with closing(_read_only_connection(catalog_path)) as connection:
        rows = connection.execute(
            """
            SELECT *
            FROM jacket_references
            WHERE review_status IN (?, ?)
            ORDER BY reference_id
            """,
            CONFIRMED_REFERENCE_STATUSES,
        ).fetchall()
        for row in rows:
            song_id = str(row["song_id"] or "")
            song = songs_by_id.get(song_id)
            if song is None or not song.grand_prix_play_available:
                counts["skipped_orphan_reference_count"] += 1
                continue
            if str(row["feature_extractor_version"]) != (
                jacket_reference_catalog.FEATURE_EXTRACTOR_VERSION
            ):
                raise ValueError(
                    "confirmed reference uses an unsupported feature extractor: "
                    f"{row['reference_id']}"
                )
            try:
                feature = jacket_reference_catalog.decode_persisted_feature(row)
            except ValueError as exc:
                raise ValueError(
                    f"confirmed reference has invalid jacket feature: {row['reference_id']}"
                ) from exc
            by_song[song_id].append(feature)
            master_versions.add(str(row["master_version"]))

    counts["reference_row_count"] = sum(len(features) for features in by_song.values())
    counts["reference_song_count"] = len(by_song)
    if not by_song:
        raise ValueError("catalog has no confirmed GRID jacket references")
    counts["catalog_master_versions"] = len(master_versions)
    counts["_catalog_master_versions"] = sorted(master_versions)
    return by_song, counts


def _load_chart_signatures(
    master_db: Path,
    song_ids: set[str],
) -> dict[str, set[tuple[str, str, int]]]:
    charts_by_song: dict[str, set[tuple[str, str, int]]] = defaultdict(set)
    with closing(_read_only_connection(master_db)) as connection:
        for row in connection.execute(
            "SELECT song_id, play_style, difficulty, level FROM charts "
            "ORDER BY song_id, play_style, difficulty, level, chart_id"
        ):
            song_id = str(row["song_id"])
            if song_id in song_ids:
                charts_by_song[song_id].add(
                    (str(row["play_style"]), str(row["difficulty"]), int(row["level"]))
                )
    return charts_by_song


def _load_result_feature_fields(
    catalog_path: Path,
    master_db: Path,
) -> dict[str, set[str]]:
    fields_by_song: dict[str, set[str]] = defaultdict(set)
    for field_name in ("title", "artist"):
        entries = jacket_reference_catalog.load_m7_result_text_feature_entries(
            catalog_path,
            master_db,
            field_name=field_name,
        )
        for entry in entries:
            fields_by_song[entry.song_id].add(field_name)
    return fields_by_song


def _pair_rows(
    *,
    songs: dict[str, master_match.MasterSong],
    references_by_song: dict[str, list[master_match.JacketFeature]],
    charts_by_song: dict[str, set[tuple[str, str, int]]],
    feature_fields_by_song: dict[str, set[str]],
    threshold: float,
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    candidate_ids = sorted(set(references_by_song) & set(charts_by_song))
    for song_id_a, song_id_b in combinations(candidate_ids, 2):
        common_signatures = charts_by_song[song_id_a] & charts_by_song[song_id_b]
        if not common_signatures:
            continue
        distance = min(
            master_match.jacket_feature_distance(feature_a, feature_b)
            for feature_a in references_by_song[song_id_a]
            for feature_b in references_by_song[song_id_b]
        )
        if distance > threshold:
            continue
        song_a = songs[song_id_a]
        song_b = songs[song_id_b]
        status_a = _feature_status(feature_fields_by_song.get(song_id_a, set()))
        status_b = _feature_status(feature_fields_by_song.get(song_id_b, set()))
        rows.append(
            {
                "distance": round(distance, 6),
                "risk_band": _risk_band(distance),
                "song_id_a": song_id_a,
                "title_a": song_a.title,
                "artist_a": song_a.artist,
                "feature_status_a": status_a,
                "song_id_b": song_id_b,
                "title_b": song_b.title,
                "artist_b": song_b.artist,
                "feature_status_b": status_b,
                "common_chart_count": len(common_signatures),
                "common_chart_signatures": " | ".join(
                    _signature_label(signature) for signature in sorted(common_signatures)
                ),
                "collection_required": (
                    "yes" if status_a != "complete" or status_b != "complete" else "no"
                ),
            }
        )

    rows.sort(
        key=lambda row: (
            row["distance"],
            row["title_a"],
            row["title_b"],
            row["song_id_a"],
            row["song_id_b"],
        )
    )
    for rank, row in enumerate(rows, start=1):
        row["rank"] = rank
    return rows


def _song_rows(
    pairs: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    songs: dict[str, dict[str, Any]] = {}
    for pair in pairs:
        for side, partner_side in (("a", "b"), ("b", "a")):
            song_id = pair[f"song_id_{side}"]
            item = songs.setdefault(
                song_id,
                {
                    "song_id": song_id,
                    "title": pair[f"title_{side}"],
                    "artist": pair[f"artist_{side}"],
                    "feature_status": pair[f"feature_status_{side}"],
                    "min_distance": pair["distance"],
                    "partners": [],
                },
            )
            item["min_distance"] = min(item["min_distance"], pair["distance"])
            item["partners"].append(
                f"{pair[f'title_{partner_side}']} ({pair['distance']:.6f})"
            )
    ordered = sorted(
        songs.values(),
        key=lambda item: (item["min_distance"], item["title"], item["song_id"]),
    )
    for rank, item in enumerate(ordered, start=1):
        item["rank"] = rank
        item["partner_text"] = " | ".join(item.pop("partners"))
        item["collection_required"] = "yes" if item["feature_status"] != "complete" else "no"
    return ordered


def build_inventory(
    catalog_path: Path,
    master_db: Path,
    *,
    threshold: float = DEFAULT_DISTANCE_THRESHOLD,
) -> dict[str, Any]:
    """Build the inventory model from read-only catalog/master inputs."""

    if threshold <= 0:
        raise ValueError("distance threshold must be positive")
    jacket_reference_catalog.validate_catalog(catalog_path)
    master = jacket_reference_catalog.load_master_identity(master_db)
    songs = {song.song_id: song for song in master.songs if song.grand_prix_play_available}
    references_by_song, reference_counts = _load_confirmed_jacket_features(
        catalog_path,
        master,
    )
    charts_by_song = _load_chart_signatures(master_db, set(references_by_song))
    result_feature_fields = _load_result_feature_fields(catalog_path, master_db)
    pairs = _pair_rows(
        songs=songs,
        references_by_song=references_by_song,
        charts_by_song=charts_by_song,
        feature_fields_by_song=result_feature_fields,
        threshold=threshold,
    )
    song_rows = _song_rows(pairs)
    incomplete_song_count = sum(
        row["feature_status"] != "complete" for row in song_rows
    )
    metadata = {
        "threshold": threshold,
        "pair_count": len(pairs),
        "song_count": len(song_rows),
        "incomplete_song_count": incomplete_song_count,
        "reference_row_count": reference_counts["reference_row_count"],
        "reference_song_count": reference_counts["reference_song_count"],
        "skipped_orphan_reference_count": reference_counts[
            "skipped_orphan_reference_count"
        ],
        "catalog_master_versions": reference_counts["_catalog_master_versions"],
        "master_version": master.version,
        "definition": INVENTORY_DEFINITION.replace("threshold", f"{threshold:g}"),
        "generated_at": datetime.now(UTC).isoformat(),
    }
    return {"metadata": metadata, "pairs": pairs, "songs": song_rows}


def _summary_rows(inventory: dict[str, Any]) -> list[list[Any]]:
    metadata = inventory["metadata"]
    pairs = inventory["pairs"]
    songs = inventory["songs"]
    return [
        ["抽出条件", f"GRIDジャケット距離 <= {metadata['threshold']:g}"],
        ["共通譜面条件", "play_style / difficulty / level が1件以上一致"],
        ["参照画像数", metadata["reference_row_count"]],
        ["参照曲数", metadata["reference_song_count"]],
        ["カタログmaster_version", ", ".join(metadata["catalog_master_versions"])],
        ["近似ペア数", len(pairs)],
        ["対象曲数", len(songs)],
        ["特徴量不足曲数", metadata["incomplete_song_count"]],
        ["RESULT収集が必要なペア数", sum(row["collection_required"] == "yes" for row in pairs)],
        ["孤児・GP対象外で除外した参照数", metadata["skipped_orphan_reference_count"]],
        ["生成日時（UTC）", metadata["generated_at"]],
        ["定義", metadata["definition"]],
    ]


def export_inventory_xlsx(path: Path, inventory: dict[str, Any]) -> None:
    """Write the inventory workbook atomically to ``path``."""

    pair_headers = [
        "順位",
        "距離",
        "リスク区分",
        "曲A",
        "アーティストA",
        "曲A song_id",
        "曲A 特徴量",
        "曲B",
        "アーティストB",
        "曲B song_id",
        "曲B 特徴量",
        "共通譜面数",
        "共通譜面条件",
        "RESULT収集要否",
    ]
    pair_rows = [
        [
            row["rank"],
            row["distance"],
            row["risk_band"],
            row["title_a"],
            row["artist_a"],
            row["song_id_a"],
            row["feature_status_a"],
            row["title_b"],
            row["artist_b"],
            row["song_id_b"],
            row["feature_status_b"],
            row["common_chart_count"],
            row["common_chart_signatures"],
            row["collection_required"],
        ]
        for row in inventory["pairs"]
    ]
    song_headers = [
        "順位",
        "曲名",
        "アーティスト",
        "song_id",
        "title/artist特徴量",
        "最小距離",
        "近似相手数",
        "近似相手",
        "RESULT収集要否",
    ]
    song_rows = [
        [
            row["rank"],
            row["title"],
            row["artist"],
            row["song_id"],
            row["feature_status"],
            row["min_distance"],
            len(row["partner_text"].split(" | ")) if row["partner_text"] else 0,
            row["partner_text"],
            row["collection_required"],
        ]
        for row in inventory["songs"]
    ]
    sheets = [
        ("概要", ["項目", "値"], _summary_rows(inventory)),
        ("近似ペア", pair_headers, pair_rows),
        ("対象曲", song_headers, song_rows),
    ]
    write_xlsx(
        path,
        sheets,
        column_widths_by_sheet={
            "概要": [30.0, 90.0],
            "近似ペア": [
                8.0,
                12.0,
                20.0,
                32.0,
                30.0,
                24.0,
                20.0,
                32.0,
                30.0,
                24.0,
                20.0,
                12.0,
                70.0,
                16.0,
            ],
            "対象曲": [8.0, 36.0, 30.0, 24.0, 20.0, 12.0, 12.0, 70.0, 16.0],
        },
    )


def _write_json(path: Path, value: dict[str, Any]) -> None:
    path = path.resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".tmp", dir=path.parent
    )
    os.close(descriptor)
    temporary = Path(temporary_name)
    try:
        temporary.write_text(
            json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def build_parser() -> argparse.ArgumentParser:
    root = _repository_root()
    parser = argparse.ArgumentParser(
        description="Export a read-only near-jacket inventory workbook"
    )
    parser.add_argument(
        "--catalog",
        type=Path,
        default=root / "databases" / "jacket-catalog-release.sqlite",
        help="M5b jacket catalog SQLite path",
    )
    parser.add_argument(
        "--master",
        type=Path,
        default=root / "databases" / "ddrgp-master.sqlite",
        help="M4 master SQLite path",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=root / OUTPUT_DIRECTORY / OUTPUT_FILENAME,
        help="output XLSX path",
    )
    parser.add_argument(
        "--json-output",
        type=Path,
        help="optional inventory JSON path for automation/debugging",
    )
    parser.add_argument(
        "--threshold",
        type=float,
        default=DEFAULT_DISTANCE_THRESHOLD,
        help="maximum jacket distance (default: 0.12)",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        inventory = build_inventory(
            args.catalog,
            args.master,
            threshold=args.threshold,
        )
        export_inventory_xlsx(args.output, inventory)
        if args.json_output is not None:
            _write_json(args.json_output, inventory)
    except (OSError, RuntimeError, sqlite3.DatabaseError, ValueError) as exc:
        raise SystemExit(str(exc)) from exc
    print(
        json.dumps(
            {
                **inventory["metadata"],
                "output": str(args.output.resolve()),
                "json_output": (
                    None if args.json_output is None else str(args.json_output.resolve())
                ),
            },
            ensure_ascii=False,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
