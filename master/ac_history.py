"""Reviewed AC first-release history; no title-only or network inference."""

from __future__ import annotations

import json
from datetime import date
from pathlib import Path

DEFAULT_HISTORY_PATH = Path(__file__).with_name("ac_history.json")
VERSION_CATEGORIES = {
    **dict.fromkeys(
        [
            "DDR 1st",
            "DDR 2ndMIX",
            "DDR 3rdMIX",
            "DDR 4thMIX",
            "DDR 5thMIX",
            "DDRMAX",
            "DDRMAX2",
            "DDR EXTREME",
            "DDR SuperNOVA",
            "DDR SuperNOVA 2",
            "DDR X",
            "DDR X2",
            "DDR X3 VS 2ndMIX",
        ],
        "CLASSIC",
    ),
    **dict.fromkeys(
        [
            "DanceDanceRevolution (2013)",
            "DanceDanceRevolution (2014)",
            "DanceDanceRevolution A",
        ],
        "WHITE",
    ),
    **dict.fromkeys(
        [
            "DanceDanceRevolution A20",
            "DanceDanceRevolution A20 PLUS",
            "DanceDanceRevolution A20 PL US",
            "DanceDanceRevolution A3",
            "DanceDanceRevolution WORLD",
        ],
        "GOLD",
    ),
}
STATUSES = {"classified", "confirmed_no_ac", "unresolved", "excluded_non_gp"}


def load_history(path: Path) -> dict[str, dict]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict) or set(document) != {"schema_version", "songs"}:
        raise ValueError("AC history document has invalid keys")
    if type(document["schema_version"]) is not int or document["schema_version"] != 1:
        raise ValueError("AC history schema version is unsupported")
    if not isinstance(document["songs"], list):
        raise ValueError("AC history songs must be a list")
    result = {}
    for row in document["songs"]:
        keys = {
            "song_id",
            "title",
            "artist",
            "status",
            "ac_version",
            "source_url",
            "checked_on",
            "reason",
        }
        if not isinstance(row, dict) or set(row) != keys:
            raise ValueError("AC history row has invalid keys")
        if any(not isinstance(value, str) for value in row.values()):
            raise ValueError("AC history values must be strings")
        if (
            not row["song_id"]
            or not row["title"]
            or not row["reason"]
            or not row["source_url"].startswith("https://")
            or row["status"] not in STATUSES - {"excluded_non_gp"}
        ):
            raise ValueError("AC history row is incomplete")
        date.fromisoformat(row["checked_on"])
        if row["status"] == "classified":
            if row["ac_version"] not in VERSION_CATEGORIES:
                raise ValueError("AC history first version is invalid")
        elif row["ac_version"]:
            raise ValueError("unclassified AC history must not have a version")
        if row["song_id"] in result:
            raise ValueError("AC history contains duplicate song IDs")
        result[row["song_id"]] = row
    return result


def classify_songs(songs, history: dict[str, dict], *, source_url: str, checked_on: str):
    rows = []
    for song in sorted(songs, key=lambda s: s.song_id):
        row = dict(
            song_id=song.song_id,
            title=song.title,
            artist=song.artist,
            status="unresolved",
            ac_version="",
            flare_category=None,
            source_url=source_url,
            checked_on=checked_on,
            reason="AC history has not been reviewed for this song version",
        )
        reviewed = history.get(song.song_id)
        if not song.grand_prix_play_available:
            row.update(status="excluded_non_gp", reason="Not currently GP playable")
        elif reviewed is not None:
            if (song.title, song.artist) != (reviewed["title"], reviewed["artist"]):
                row["reason"] = "Reviewed song title/artist differs; verify audio/version identity"
            elif reviewed["status"] == "confirmed_no_ac" and song.version in VERSION_CATEGORIES:
                row["reason"] = (
                    "AC first-release folder conflicts with previous no-AC review; recheck history"
                )
            else:
                row.update(reviewed)
                row["flare_category"] = VERSION_CATEGORIES.get(row["ac_version"])
        elif song.version in VERSION_CATEGORIES:
            row.update(
                status="classified",
                ac_version=song.version,
                flare_category=VERSION_CATEGORIES[song.version],
                reason="AC first-release folder in GP source list",
            )
        rows.append(row)
    return rows


def validate_rows(rows):
    seen = set()
    for row in rows:
        if row["song_id"] in seen or row["status"] not in STATUSES:
            raise ValueError("Invalid AC history identity/status")
        seen.add(row["song_id"])
        if row["status"] == "classified":
            if (
                row["ac_version"] not in VERSION_CATEGORIES
                or row["flare_category"] != VERSION_CATEGORIES[row["ac_version"]]
            ):
                raise ValueError("AC history category/version mismatch")
        elif row["flare_category"] is not None or row["ac_version"]:
            raise ValueError("Unclassified AC history contains a category/version")
        if not row["reason"] or not row["source_url"].startswith("https://"):
            raise ValueError("AC history provenance is missing")
        date.fromisoformat(row["checked_on"])
