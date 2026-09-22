from __future__ import annotations

import json
import sqlite3
from pathlib import Path

import pytest

from master.d1_export import export_shared_master_sql
from master.identity_registry import (
    DEFAULT_REGISTRY_PATH,
    SongIdentityRegistry,
    bootstrap_registry_document,
    stable_identity_id_v1,
)


def test_stable_identity_v1_golden_vectors() -> None:
    assert stable_identity_id_v1("song", "MAKE IT BETTER", "mitsu-O!") == (
        "song_ae7b5a066f2f8429"
    )
    assert stable_identity_id_v1("song", "RËVOLUTIФN", "TËЯRA") == (
        "song_177d950f607b1894"
    )
    assert stable_identity_id_v1(
        "chart", "song_177d950f607b1894", "SINGLE", "EXPERT"
    ) == "chart_ac28192bffe0aafd"


def test_released_registry_keeps_canonical_and_source_presentations_on_one_id() -> None:
    registry = SongIdentityRegistry.load(DEFAULT_REGISTRY_PATH)

    assert registry.resolve("RËVOLUTIФN", "TËЯRA") == "song_177d950f607b1894"
    assert registry.resolve("RЁVOLUTIФN", "TЁЯRA") == "song_177d950f607b1894"
    assert len(registry.identities) == 1372


def test_registry_rejects_unreviewed_identity_and_duplicate_mapping(tmp_path: Path) -> None:
    registry = SongIdentityRegistry.load(DEFAULT_REGISTRY_PATH)
    with pytest.raises(ValueError, match="unregistered song identity"):
        registry.resolve("Brand New Song", "New Artist")

    invalid = tmp_path / "registry.json"
    invalid.write_text(
        json.dumps(
            {
                "schema_version": 1,
                "identity_contract": "stable_identity_id_v1",
                "songs": [
                    {
                        "song_id": "song_0000000000000001",
                        "presentations": [{"title": "Same", "artist": "Artist"}],
                    },
                    {
                        "song_id": "song_0000000000000002",
                        "presentations": [{"title": "Same", "artist": "Artist"}],
                    },
                ],
            }
        ),
        encoding="utf-8",
    )
    with pytest.raises(ValueError, match="multiple IDs"):
        SongIdentityRegistry.load(invalid)


def create_master_fixture(path: Path) -> None:
    with sqlite3.connect(path) as connection:
        connection.executescript(
            """
            CREATE TABLE songs (
              song_id TEXT PRIMARY KEY, title TEXT, artist TEXT, version TEXT
            );
            CREATE TABLE charts (
              chart_id TEXT PRIMARY KEY, song_id TEXT, play_style TEXT,
              difficulty TEXT, level INTEGER, is_removed INTEGER
            );
            CREATE TABLE song_aliases (
              song_id TEXT, alias_title TEXT, alias_artist TEXT
            );
            CREATE TABLE master_metadata (key TEXT PRIMARY KEY, value TEXT);
            INSERT INTO songs VALUES ('song_1', 'Canonical', 'Artist', 'DDR');
            INSERT INTO song_aliases VALUES ('song_1', 'Source', 'Artist');
            INSERT INTO charts VALUES (
              'chart_1', 'song_1', 'SINGLE', 'EXPERT', 15, 0
            );
            INSERT INTO master_metadata VALUES ('master_version', 'fixture-v1');
            """
        )


def test_registry_bootstrap_includes_alias_presentations(tmp_path: Path) -> None:
    database = tmp_path / "master.sqlite"
    create_master_fixture(database)

    document = bootstrap_registry_document(database)

    assert document["songs"] == [
        {
            "song_id": "song_1",
            "presentations": [
                {"title": "Canonical", "artist": "Artist"},
                {"title": "Source", "artist": "Artist"},
            ],
        }
    ]


def test_d1_export_is_deterministic_idempotent_and_resolves_metadata(tmp_path: Path) -> None:
    database = tmp_path / "master.sqlite"
    create_master_fixture(database)

    first = export_shared_master_sql(database)
    second = export_shared_master_sql(database)

    assert first == second
    target = sqlite3.connect(":memory:")
    target.executescript(
        """
        PRAGMA foreign_keys = ON;
        CREATE TABLE songs (
          song_id TEXT PRIMARY KEY, title TEXT NOT NULL,
          artist TEXT NOT NULL, version TEXT NOT NULL
        );
        CREATE TABLE charts (
          chart_id TEXT PRIMARY KEY, song_id TEXT NOT NULL,
          play_style TEXT NOT NULL, difficulty TEXT NOT NULL,
          level INTEGER NOT NULL, is_removed INTEGER NOT NULL,
          FOREIGN KEY (song_id) REFERENCES songs(song_id)
        );
        CREATE TABLE web_master_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        """
    )
    target.executescript(first)
    target.executescript(first)
    assert target.execute(
        "SELECT c.chart_id, s.title FROM charts c "
        "JOIN songs s ON s.song_id = c.song_id"
    ).fetchone() == ("chart_1", "Canonical")
    assert target.execute(
        "SELECT value FROM web_master_metadata WHERE key = 'master_version'"
    ).fetchone() == ("fixture-v1",)
