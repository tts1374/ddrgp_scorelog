from __future__ import annotations

import argparse
import hashlib
import json
import re
import sqlite3
from dataclasses import dataclass
from pathlib import Path
from typing import Any

IDENTITY_CONTRACT = "stable_identity_id_v1"
REGISTRY_SCHEMA_VERSION = 1
DEFAULT_REGISTRY_PATH = Path(__file__).with_name("song_identity_registry.json")
SONG_ID_PATTERN = re.compile(r"^song_[0-9a-f]{16}$")


def stable_identity_id_v1(prefix: str, *parts: str) -> str:
    digest = hashlib.sha1("\0".join(parts).encode("utf-8")).hexdigest()[:16]
    return f"{prefix}_{digest}"


@dataclass(frozen=True)
class SongIdentityRegistry:
    identities: dict[tuple[str, str], str]

    @classmethod
    def load(cls, path: Path = DEFAULT_REGISTRY_PATH) -> SongIdentityRegistry:
        document = json.loads(path.read_text(encoding="utf-8"))
        if (
            not isinstance(document, dict)
            or document.get("schema_version") != REGISTRY_SCHEMA_VERSION
            or document.get("identity_contract") != IDENTITY_CONTRACT
            or not isinstance(document.get("songs"), list)
        ):
            raise ValueError("song identity registry header is invalid")

        identities: dict[tuple[str, str], str] = {}
        song_ids: set[str] = set()
        for song in document["songs"]:
            if not isinstance(song, dict):
                raise ValueError("song identity registry contains an invalid song entry")
            song_id = song.get("song_id")
            presentations = song.get("presentations")
            if (
                not isinstance(song_id, str)
                or SONG_ID_PATTERN.fullmatch(song_id) is None
                or song_id in song_ids
                or not isinstance(presentations, list)
                or not presentations
            ):
                raise ValueError("song identity registry contains an invalid identity entry")
            song_ids.add(song_id)
            for presentation in presentations:
                if not isinstance(presentation, dict):
                    raise ValueError("song identity registry contains an invalid presentation")
                title = presentation.get("title")
                artist = presentation.get("artist")
                if not isinstance(title, str) or not title or not isinstance(artist, str):
                    raise ValueError("song identity registry presentation is incomplete")
                key = (title, artist)
                previous = identities.get(key)
                if previous is not None and previous != song_id:
                    raise ValueError(
                        "song identity registry maps one presentation to multiple IDs: "
                        f"{title!r} / {artist!r}"
                    )
                identities[key] = song_id
        return cls(identities)

    def resolve(self, title: str, artist: str) -> str:
        song_id = self.identities.get((title, artist))
        if song_id is not None:
            return song_id
        candidate = {
            "song_id": stable_identity_id_v1("song", title, artist),
            "presentations": [{"title": title, "artist": artist}],
        }
        raise ValueError(
            "unregistered song identity; review and add this candidate to "
            f"{DEFAULT_REGISTRY_PATH.name}: "
            + json.dumps(candidate, ensure_ascii=False, sort_keys=True)
        )


def bootstrap_registry_document(master_db_path: Path) -> dict[str, Any]:
    uri = f"file:{master_db_path.resolve().as_posix()}?mode=ro"
    with sqlite3.connect(uri, uri=True) as connection:
        songs = connection.execute(
            "SELECT song_id, title, artist FROM songs ORDER BY song_id"
        ).fetchall()
        aliases = connection.execute(
            "SELECT song_id, alias_title, alias_artist FROM song_aliases "
            "ORDER BY song_id, alias_title, alias_artist"
        ).fetchall()

    presentations_by_song: dict[str, list[dict[str, str]]] = {
        song_id: [{"title": title, "artist": artist}]
        for song_id, title, artist in songs
    }
    for song_id, title, artist in aliases:
        presentation = {"title": title, "artist": artist}
        if presentation not in presentations_by_song[song_id]:
            presentations_by_song[song_id].append(presentation)
    return {
        "schema_version": REGISTRY_SCHEMA_VERSION,
        "identity_contract": IDENTITY_CONTRACT,
        "songs": [
            {"song_id": song_id, "presentations": presentations_by_song[song_id]}
            for song_id in sorted(presentations_by_song)
        ],
    }


def write_bootstrap_registry(master_db_path: Path, output_path: Path) -> None:
    document = bootstrap_registry_document(master_db_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(document, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    SongIdentityRegistry.load(output_path)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Bootstrap the reviewed song identity registry from a released master DB."
    )
    parser.add_argument("--master-db", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=DEFAULT_REGISTRY_PATH)
    args = parser.parse_args(argv)
    write_bootstrap_registry(args.master_db, args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
