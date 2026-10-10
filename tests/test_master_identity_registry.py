from __future__ import annotations

import json
import sqlite3
from collections.abc import Iterator
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
    assert len(registry.identities) == 1394


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
        ("三妖精SAY YA!!!", "森羅万象", "song_297d4a623731b96e"),
        ("天狗の落とし文 (feat. ｙｔｒ)", "魂音泉", "song_1e25d5f48c780cea"),
        ("マツヨイナイトバグ", "ビートまりおとまろん", "song_c9f3c1939c55b58c"),
        ("Ultimate taste", "ぱらどっと", "song_0da01c1432715c12"),
        ("アワデコノヨヲ", "AMAZE", "song_c53f356ede572199"),
        ("ILL-STARRED Diver", "polysha", "song_385cbf90f5ea775f"),
        ("Nostalgic Blood of the Strife", "Laur", "song_563b7bc934ac8d92"),
        ("Princess K", "Retropolitaliens(Ms.+駄々子)", "song_dbe24db47ee70161"),
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
    before_reimport = target.total_changes
    target.executescript(first)
    assert target.total_changes == before_reimport
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


@pytest.fixture
def shared_master_target(tmp_path: Path) -> Iterator[tuple[Path, sqlite3.Connection]]:
    database = tmp_path / "master.sqlite"
    create_master_fixture(database)
    with sqlite3.connect(database) as source:
        source.executescript(
            """
            INSERT INTO songs VALUES ('song_2', 'Other', 'Artist', 'DDR');
            INSERT INTO charts VALUES ('chart_2', 'song_2', 'DOUBLE', 'BASIC', 5, 0);
            INSERT INTO song_aliases VALUES ('song_2', 'Keeper', 'Artist');
            """
        )
    target = sqlite3.connect(":memory:")
    migrations = Path(__file__).resolve().parents[1] / "web/identity-api/migrations"
    for migration in sorted(migrations.glob("*.sql")):
        target.executescript(migration.read_text(encoding="utf-8"))
    target.executescript(export_shared_master_sql(database))
    try:
        yield database, target
    finally:
        target.close()


@pytest.mark.parametrize(
    ("table", "id_column", "row_id", "column", "different_value"),
    [
        ("songs", "song_id", "song_1", "title", "Changed"),
        ("songs", "song_id", "song_1", "artist", "Changed artist"),
        ("songs", "song_id", "song_1", "version", "DDR 1st"),
        ("songs", "song_id", "song_1", "title_search_key", "stale key"),
        ("songs", "song_id", "song_1", "flare_category", "WHITE"),
        ("charts", "chart_id", "chart_1", "song_id", "song_2"),
        ("charts", "chart_id", "chart_1", "play_style", "DOUBLE"),
        ("charts", "chart_id", "chart_1", "difficulty", "BASIC"),
        ("charts", "chart_id", "chart_1", "level", 16),
        ("charts", "chart_id", "chart_1", "is_removed", 1),
    ],
)
def test_d1_export_repairs_only_the_changed_row(
    shared_master_target: tuple[Path, sqlite3.Connection],
    table: str,
    id_column: str,
    row_id: str,
    column: str,
    different_value: str | int,
) -> None:
    database, target = shared_master_target
    select = f"SELECT {column} FROM {table} WHERE {id_column} = ?"
    original = target.execute(select, (row_id,)).fetchone()
    target.execute(
        f"UPDATE {table} SET {column} = ? WHERE {id_column} = ?",
        (different_value, row_id),
    )
    before = target.total_changes
    sql = export_shared_master_sql(database)
    target.executescript(sql)
    assert target.total_changes - before == 1
    assert target.execute(select, (row_id,)).fetchone() == original
    before = target.total_changes
    target.executescript(sql)
    assert target.total_changes == before


def test_d1_export_reflects_null_category_transitions(
    shared_master_target: tuple[Path, sqlite3.Connection],
) -> None:
    database, target = shared_master_target
    with sqlite3.connect(database) as source:
        source.executescript(
            "ALTER TABLE songs ADD COLUMN grand_prix_play_available INTEGER NOT NULL DEFAULT 1;"
            "UPDATE songs SET version = 'DDR 1st' WHERE song_id = 'song_1';"
        )
    for available, category in [(1, "CLASSIC"), (0, None), (1, "CLASSIC")]:
        with sqlite3.connect(database) as source:
            source.execute(
                "UPDATE songs SET grand_prix_play_available = ? WHERE song_id = 'song_1'",
                (available,),
            )
        before = target.total_changes
        target.executescript(export_shared_master_sql(database))
        assert target.total_changes - before == 1
        assert target.execute(
            "SELECT flare_category FROM songs WHERE song_id = 'song_1'"
        ).fetchone() == (category,)


def test_d1_export_inserts_new_rows_and_updates_only_changed_metadata(
    shared_master_target: tuple[Path, sqlite3.Connection],
) -> None:
    database, target = shared_master_target
    with sqlite3.connect(database) as source:
        source.executescript(
            """
            INSERT INTO songs VALUES ('song_3', 'New song', 'Artist', 'DDR');
            INSERT INTO charts VALUES ('chart_3', 'song_3', 'SINGLE', 'EXPERT', 12, 0);
            INSERT INTO song_aliases VALUES ('song_3', 'New alias', 'Artist');
            """
        )
    before = target.total_changes
    target.executescript(export_shared_master_sql(database))
    assert target.total_changes - before == 3
    assert target.execute(
        "SELECT c.chart_id, c.level, a.search_key FROM charts c "
        "JOIN song_title_search_aliases a USING(song_id) WHERE c.chart_id = 'chart_3'"
    ).fetchone() == ("chart_3", 12, "new alias")

    with sqlite3.connect(database) as source:
        source.execute("UPDATE master_metadata SET value = 'fixture-v2'")
    before = target.total_changes
    sql = export_shared_master_sql(database)
    target.executescript(sql)
    assert target.total_changes - before == 1
    assert target.execute("SELECT value FROM web_master_metadata").fetchone() == ("fixture-v2",)
    before = target.total_changes
    target.executescript(sql)
    assert target.total_changes == before


@pytest.mark.parametrize(
    ("aliases", "expected", "changes"),
    [
        (["Source", "New's alias", "NEW'S ALIAS", "Canonical"], ["new's alias", "source"], 1),
        (["Changed"], ["changed"], 2),
        ([], [], 1),
    ],
)
def test_d1_export_applies_only_alias_differences_with_the_same_version(
    shared_master_target: tuple[Path, sqlite3.Connection],
    aliases: list[str],
    expected: list[str],
    changes: int,
) -> None:
    database, target = shared_master_target
    with sqlite3.connect(database) as source:
        source.execute("DELETE FROM song_aliases WHERE song_id = 'song_1'")
        source.executemany(
            "INSERT INTO song_aliases VALUES ('song_1', ?, 'Artist')",
            [(alias,) for alias in aliases],
        )
    before = target.total_changes
    sql = export_shared_master_sql(database)
    target.executescript(sql)
    assert target.total_changes - before == changes
    assert target.execute(
        "SELECT song_id, search_key FROM song_title_search_aliases ORDER BY song_id, search_key"
    ).fetchall() == [("song_1", alias) for alias in expected] + [("song_2", "keeper")]
    assert target.execute("SELECT value FROM web_master_metadata").fetchone() == ("fixture-v1",)
    before = target.total_changes
    target.executescript(sql)
    assert target.total_changes == before


def test_d1_export_clears_an_empty_alias_set_once(
    shared_master_target: tuple[Path, sqlite3.Connection],
) -> None:
    database, target = shared_master_target
    with sqlite3.connect(database) as source:
        source.execute("DELETE FROM song_aliases")
    before = target.total_changes
    sql = export_shared_master_sql(database)
    target.executescript(sql)
    assert target.total_changes - before == 2
    assert target.execute("SELECT * FROM song_title_search_aliases").fetchall() == []
    before = target.total_changes
    target.executescript(sql)
    assert target.total_changes == before


def test_d1_export_preserves_player_data_and_existing_chart_references(
    shared_master_target: tuple[Path, sqlite3.Connection],
) -> None:
    database, target = shared_master_target
    target.executescript(
        """
        INSERT INTO players (id, public_player_id, display_name, created_at, updated_at)
        VALUES ('player_1', 'public_1', 'Player', '2026-10-10', '2026-10-10');
        INSERT INTO player_credentials (id, player_id, type, secret_digest, created_at)
        VALUES ('credential_1', 'player_1', 'app',
                'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', '2026-10-10');
        INSERT INTO player_chart_bests
          (player_id, chart_id, best_score, best_ex_score, best_clear_type, updated_at)
        VALUES ('player_1', 'chart_2', 900000, 1000, 'CLEAR', '2026-10-10');
        """
    )
    protected = {
        table: target.execute(f"SELECT * FROM {table}").fetchall()
        for table in ["players", "player_credentials", "player_chart_bests"]
    }
    retained_chart = target.execute("SELECT * FROM charts WHERE chart_id = 'chart_2'").fetchone()
    with sqlite3.connect(database) as source:
        source.executescript(
            """
            UPDATE songs SET title = 'Renamed' WHERE song_id = 'song_1';
            UPDATE charts SET is_removed = 1 WHERE chart_id = 'chart_1';
            DELETE FROM charts WHERE chart_id = 'chart_2';
            DELETE FROM songs WHERE song_id = 'song_2';
            DELETE FROM song_aliases WHERE song_id = 'song_2';
            """
        )
    target.executescript(export_shared_master_sql(database))
    assert target.execute("SELECT title FROM songs WHERE song_id = 'song_1'").fetchone() == (
        "Renamed",
    )
    assert target.execute(
        "SELECT is_removed FROM charts WHERE chart_id = 'chart_1'"
    ).fetchone() == (1,)
    assert target.execute(
        "SELECT * FROM charts WHERE chart_id = 'chart_2'"
    ).fetchone() == retained_chart
    assert target.execute("SELECT song_id FROM songs WHERE song_id = 'song_2'").fetchone() == (
        "song_2",
    )
    for table, rows in protected.items():
        assert target.execute(f"SELECT * FROM {table}").fetchall() == rows
    assert target.execute("PRAGMA foreign_key_check").fetchall() == []


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
