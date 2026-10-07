from __future__ import annotations

import json
import sqlite3
from pathlib import Path

import pytest

from master.builder import parse_song_list_rows
from master.d1_export import export_shared_master_sql, normalize_title_search
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
    assert len(registry.identities) == 1386


@pytest.mark.parametrize(
    ("title", "artist", "song_id"),
    [
        ("ZENDEGI DANCE", 'ARM × BEMANI Sound Team "U1 overground"', "song_0d58f6ea61e404e4"),
        ("Is this dance a Hakken?", "RoughSketch", "song_5c89d5d1203ed516"),
        ("Bye or not", "PSYQUI feat. Mikanzil", "song_61fc25aa94133533"),
        ("疾風迅雷", "KUMOKIRI", "song_70cecd853abc379a"),
        ("EYE OF THE HEAVEN", 'BEMANI Sound Team "U1-ASAMi"', "song_8b06d0cf133616cb"),
        ("Decryption", "Felysrator", "song_99f117ee2f346e9a"),
        ("eyesight", "タバサリサ", "song_9d7fb72874434523"),
        ("I'll Be With You", "ゆんゆん", "song_e74e9cf071c1206a"),
        ("Daisycutter", "ETIA.", "song_fb97eaa232785f2b"),
        (
            "チルノのパーフェクトさんすう学園【ビートまりお】",
            "ビートまりお",
            "song_23f9dc279a8dafb2",
        ),
        ("恋繋エピローグ", "Amateras Records feat. KUMI(ヲタみん)", "song_2e3e6552f7bdf8df"),
        ("TURNING DOWN", "U-ske feat. 石橋桃", "song_8615f5118db290ca"),
        ("Footnotes on BPM", "KirK+Tabi", "song_8f0caf96e9da8828"),
        ("MonstaFesta", "アカツキ チョータ", "song_ce8e9d5bec66975c"),
    ],
)
def test_reviewed_additions_keep_master_and_chart_identity(
    title: str, artist: str, song_id: str
) -> None:
    registry = SongIdentityRegistry.load(DEFAULT_REGISTRY_PATH)
    rows = [[], [], ["EX", title, artist, "", "180", "-"] + ["10"] * 9]

    songs, charts = parse_song_list_rows(rows, registry)

    assert registry.resolve(title, artist) == song_id
    assert songs[0].song_id == song_id
    assert len(charts) == 9
    assert all(
        chart.song_id == song_id
        and chart.chart_id
        == stable_identity_id_v1("chart", song_id, chart.play_style, chart.difficulty)
        for chart in charts
    )


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
    with sqlite3.connect(database) as connection:
        connection.executemany(
            "INSERT INTO songs (song_id, title, artist, version) VALUES (?, ?, 'Artist', 'DDR')",
            [
                ("song_2", "Übertreffen"),
                ("song_3", "ÆTHER"),
                ("song_06d308e2e7cdf168", "TRUE♥LOVE"),
            ],
        )

    first = export_shared_master_sql(database)
    second = export_shared_master_sql(database)

    assert first == second
    target = sqlite3.connect(":memory:")
    target.executescript(
        """
        PRAGMA foreign_keys = ON;
        CREATE TABLE songs (
          song_id TEXT PRIMARY KEY, title TEXT NOT NULL,
          artist TEXT NOT NULL, version TEXT NOT NULL,
          title_search_key TEXT NOT NULL DEFAULT '', flare_category TEXT
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
    alias_migration = (
        Path(__file__).resolve().parents[1]
        / "web/identity-api/migrations/0005_title_search_aliases.sql"
    )
    target.executescript(alias_migration.read_text(encoding="utf-8"))
    target.executescript(first)
    target.executescript(first)
    assert target.execute(
        "SELECT c.chart_id, s.title FROM charts c "
        "JOIN songs s ON s.song_id = c.song_id"
    ).fetchone() == ("chart_1", "Canonical")
    assert target.execute(
        "SELECT value FROM web_master_metadata WHERE key = 'master_version'"
    ).fetchone() == ("fixture-v1",)
    assert target.execute(
        "SELECT title_search_key FROM songs WHERE song_id = 'song_2'"
    ).fetchone() == ("ubertreffen",)
    assert target.execute(
        "SELECT title_search_key FROM songs WHERE song_id = 'song_3'"
    ).fetchone() == ("aether",)
    assert target.execute(
        "SELECT song_id, search_key FROM song_title_search_aliases ORDER BY song_id"
    ).fetchall() == [
        ("song_06d308e2e7cdf168", "true love"),
        ("song_1", "source"),
    ]
    with sqlite3.connect(database) as connection:
        connection.execute(
            "UPDATE songs SET title = 'Übertreffen II' WHERE song_id = 'song_2'"
        )
        connection.execute(
            "UPDATE song_aliases SET alias_title = 'Changed' WHERE song_id = 'song_1'"
        )
    target.executescript(export_shared_master_sql(database))
    assert target.execute(
        "SELECT title_search_key FROM songs WHERE song_id = 'song_2'"
    ).fetchone() == ("ubertreffen ii",)
    assert target.execute(
        "SELECT search_key FROM song_title_search_aliases WHERE song_id = 'song_1'"
    ).fetchall() == [("changed",)]


def test_title_search_normalization_and_existing_d1_backfill() -> None:
    assert normalize_title_search("Übertreffen") == "ubertreffen"
    assert normalize_title_search("ÆTHER") == "aether"
    assert normalize_title_search("ガ") == "ガ"

    target = sqlite3.connect(":memory:")
    target.execute("CREATE TABLE songs(song_id TEXT PRIMARY KEY, title TEXT NOT NULL)")
    target.executemany(
        "INSERT INTO songs VALUES (?, ?)",
        [("song_1", "Übertreffen"), ("song_2", "ÆTHER"), ("song_3", "ガ")],
    )
    migration = (
        Path(__file__).resolve().parents[1]
        / "web/identity-api/migrations/0004_title_search_key.sql"
    )
    target.executescript(migration.read_text(encoding="utf-8"))
    assert target.execute(
        "SELECT title_search_key FROM songs ORDER BY song_id"
    ).fetchall() == [("ubertreffen",), ("aether",), ("ガ",)]


def test_curated_title_search_aliases_keep_symbol_only_title_literal() -> None:
    manifest = (
        Path(__file__).resolve().parents[1] / "master/title_search_aliases.json"
    )
    entries = json.loads(manifest.read_text(encoding="utf-8"))
    assert len(entries) == 125
    assert sum(len(entry["aliases"]) for entry in entries) == 127
    assert len({entry["song_id"] for entry in entries}) == len(entries)
    assert "song_c55d8ffd1066e044" not in {
        entry["song_id"] for entry in entries
    }
