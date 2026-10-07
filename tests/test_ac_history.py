from __future__ import annotations

import json
import sqlite3
from dataclasses import replace
from pathlib import Path

import pytest
from test_master_builder import FIXTURE_HTML, OFFICIAL_FIXTURE_HTML

from master import builder, inspect
from master.ac_history import DEFAULT_HISTORY_PATH, classify_songs, load_history
from master.d1_export import export_shared_master_sql


def song(sid="test", title="Same title", artist="Original", version="DDR GRAND PRIX", gp=True):
    return builder.MasterSong(
        sid,
        title,
        artist,
        version,
        "not AC history",
        "",
        "",
        "",
        "",
        "",
        grand_prix_play_available=gp,
    )


def reviewed(status="classified", version="DDR X2", **changes):
    row = dict(
        song_id="test",
        title="Same title",
        artist="Original",
        status=status,
        ac_version=version,
        source_url="https://example.test/history",
        checked_on="2026-10-05",
        reason="Confirmed audio/version correspondence",
    )
    row.update(changes)
    return row


def classify(songs, history):
    return classify_songs(songs, history, source_url=builder.SOURCE_URL, checked_on="2026-10-05")


def test_first_release_categories_cover_current_and_past_ac_without_promoting_non_gp():
    history = load_history(DEFAULT_HISTORY_PATH)
    for title, expected in [
        ("resonance", "CLASSIC"),
        ("ポリリズム", "CLASSIC"),
        ("BRIGHT STREAM", "WHITE"),
        ("ミックスナッツ", "GOLD"),
        ("murmur twins (guitar pop ver.)", "GOLD"),
    ]:
        r = next(r for r in history.values() if r["title"] == title)
        row = classify([song(r["song_id"], r["title"], r["artist"])], history)[0]
        assert row["flare_category"] == expected
        assert (
            classify([song(r["song_id"], r["title"], r["artist"], gp=False)], history)[0]["status"]
            == "excluded_non_gp"
        )
    assert [
        r["flare_category"]
        for r in classify(
            [
                song("a", version="DDR X"),
                song("b", version="DanceDanceRevolution A"),
                song("c", version="DanceDanceRevolution WORLD"),
            ],
            {},
        )
    ] == ["CLASSIC", "WHITE", "GOLD"]


def test_deleted_list_and_cs_versions_do_not_promote_different_gp_audio():
    history = load_history(DEFAULT_HISTORY_PATH)
    for title, expected in [
        ("GUILTY DIAMONDS", "GOLD"),
        ("No Life Queen [DJ Command Remix]", "GOLD"),
        ("Help me, ERINNNNNN!!", "WHITE"),
        ("色は匂へど散りぬるを", "WHITE"),
        ("最終鬼畜妹フランドール・Ｓ", "GOLD"),
    ]:
        r = next(r for r in history.values() if r["title"] == title)
        assert classify([song(r["song_id"], r["title"], r["artist"])], history)[0][
            "flare_category"
        ] == expected
    for title in ["HOT LIMIT", "おどるポンポコリン", "創聖のアクエリオン", "空色デイズ"]:
        r = next(r for r in history.values() if r["title"] == title)
        assert r["status"] == "confirmed_no_ac" and not r["ac_version"]
        assert "CS" in r["reason"] and "WORLD/" in r["source_url"]
    for title in ["only my railgun", "Trickster", "オリオンをなぞる"]:
        r = next(r for r in history.values() if r["title"] == title)
        assert r["status"] == "confirmed_no_ac" and not r["ac_version"]
        assert "AC" in r["reason"]
        row = classify([song(r["song_id"], r["title"], r["artist"])], history)[0]
        assert row["flare_category"] is None


def test_gp_exclusive_pack_has_no_ddr_ac_category_despite_other_bemani_history():
    history = load_history(DEFAULT_HISTORY_PATH)
    titles = [
        "Black or Red?", "Butterfly Twist", "CARNIVOROUS", "Damage Per Second",
        "Dear Deer", "JOK3R", "Lo-Fi-M", "Midnight City Warfare", "Paradission", "Sulk",
    ]
    for title in titles:
        r = next(r for r in history.values() if r["title"] == title)
        assert r["status"] == "confirmed_no_ac" and "exclusive" in r["reason"]
        assert "vol-5" in r["source_url"]
        row = classify([song(r["song_id"], r["title"], r["artist"])], history)[0]
        assert row["flare_category"] is None


def test_song_version_identity_no_ac_and_unresolved_are_distinct():
    h = {"test": reviewed()}
    assert classify([song()], h)[0]["flare_category"] == "CLASSIC"
    assert classify([song(artist="Cover")], h)[0]["status"] == "unresolved"
    assert "identity" in classify([song(artist="Cover")], h)[0]["reason"]
    assert classify([song("cover", artist="Cover")], h)[0]["flare_category"] is None
    for status in ["confirmed_no_ac", "unresolved"]:
        r = classify([song()], {"test": reviewed(status, "")})[0]
        assert r["status"] == status and r["flare_category"] is None and r["reason"]
    changed = classify(
        [song(version="DanceDanceRevolution A3")], {"test": reviewed("confirmed_no_ac", "")}
    )[0]
    assert changed["status"] == "unresolved" and "conflicts" in changed["reason"]


@pytest.mark.parametrize(
    "change",
    [
        dict(ac_version="X9"),
        dict(checked_on="invalid"),
        dict(source_url=""),
        dict(status="fetch_failed"),
        dict(status="confirmed_no_ac"),
        dict(reason=""),
    ],
)
def test_bad_input_fails_instead_of_becoming_no_ac(tmp_path, change):
    path = tmp_path / "history.json"
    path.write_text(
        json.dumps(dict(schema_version=1, songs=[reviewed(**change)])), encoding="utf-8"
    )
    with pytest.raises(ValueError):
        load_history(path)


def test_missing_duplicate_input_and_fetch_failure_preserve_output(tmp_path, monkeypatch):
    with pytest.raises(FileNotFoundError):
        load_history(tmp_path / "missing.json")
    path = tmp_path / "duplicate.json"
    path.write_text(
        json.dumps(dict(schema_version=1, songs=[reviewed(), reviewed()])), encoding="utf-8"
    )
    with pytest.raises(ValueError, match="duplicate"):
        load_history(path)

    def fail(_):
        raise OSError("fixture fetch failed")

    monkeypatch.setattr(builder, "fetch_source_html", fail)
    with pytest.raises(OSError, match="fetch failed"):
        builder.main(["--output", str(tmp_path / "new" / "master.sqlite")])
    assert not (tmp_path / "new").exists()


@pytest.mark.parametrize("fallback_history", [False, True])
def test_auto_master_version_ignores_fetch_date_but_keeps_provenance(tmp_path, fallback_history):
    summaries = []
    metadata = []
    shared_sql = []
    for day in ["2026-10-05", "2026-10-06"]:
        build = builder.parse_master_html(
            FIXTURE_HTML,
            official_html=OFFICIAL_FIXTURE_HTML,
            fetched_at=f"{day}T00:00:00+00:00",
        )
        if fallback_history:
            build = replace(build, ac_history=())
        output = tmp_path / f"{day}.sqlite"
        builder.write_master_database(output, build)
        summaries.append(inspect.inspect_master_database(output))
        shared_sql.append(export_shared_master_sql(output))
        with sqlite3.connect(output) as connection:
            metadata.append(dict(connection.execute("SELECT key, value FROM master_metadata")))

    assert summaries[0]["source_hash"] == summaries[1]["source_hash"]
    assert summaries[0]["official_source_hash"] == summaries[1]["official_source_hash"]
    assert summaries[0]["master_version"] == summaries[1]["master_version"]
    assert shared_sql[0] == shared_sql[1]
    assert metadata[0]["ac_history_hash"] != metadata[1]["ac_history_hash"]
    for summary, day in zip(summaries, ["2026-10-05", "2026-10-06"], strict=True):
        assert {row["checked_on"] for row in summary["ac_history"]} == {day}


def test_auto_master_version_changes_when_only_ac_classification_changes(tmp_path):
    build = builder.parse_master_html(
        FIXTURE_HTML,
        official_html=OFFICIAL_FIXTURE_HTML,
        fetched_at="2026-10-05T00:00:00+00:00",
    )
    first = next(row for row in build.ac_history if row["flare_category"] == "CLASSIC")
    changed = replace(build, ac_history=tuple(
        {**row, "ac_version": "DanceDanceRevolution A3", "flare_category": "GOLD"}
        if row["song_id"] == first["song_id"] else row
        for row in build.ac_history
    ))
    versions = []
    for name, candidate in [("original", build), ("changed", changed)]:
        output = tmp_path / f"{name}.sqlite"
        builder.write_master_database(output, candidate)
        versions.append(inspect.inspect_master_database(output)["master_version"])
    assert versions[0] != versions[1]


def test_normal_update_addition_report_inspection_and_shared_output(tmp_path, monkeypatch):
    # Exercise the same CLI as collector/CI with deterministic HTML, not network.
    base = builder.parse_master_html(FIXTURE_HTML, official_html=OFFICIAL_FIXTURE_HTML)
    first = next(s for s in base.songs if s.grand_prix_play_available)
    official = OFFICIAL_FIXTURE_HTML.replace(
        "</table>",
        "".join(
            f"<tr><td>{title}</td><td>Cover</td><td></td><td>〇</td></tr>"
            for title in ["New cover", "New AC", "New exclusive"]
        )
        + "</table>",
        1,
    )
    updated = builder.parse_master_html(FIXTURE_HTML, official_html=official)
    new = next(s for s in updated.songs if s.title == "New cover")
    registry = tmp_path / "registry.json"
    registry.write_text(
        json.dumps(
            dict(
                schema_version=1,
                identity_contract="stable_identity_id_v1",
                songs=[
                    dict(song_id=s.song_id, presentations=[dict(title=s.title, artist=s.artist)])
                    for s in updated.songs
                ],
            )
        ),
        encoding="utf-8",
    )
    history = tmp_path / "history.json"
    entries = [reviewed(song_id=first.song_id, title=first.title, artist=first.artist)]
    for title, status, version in [
        ("New AC", "classified", "DanceDanceRevolution A3"),
        ("New exclusive", "confirmed_no_ac", ""),
    ]:
        s = next(s for s in updated.songs if s.title == title)
        entries.append(reviewed(status, version, song_id=s.song_id, title=s.title, artist=s.artist))
    history.write_text(
        json.dumps(
            dict(
                schema_version=1,
                songs=entries,
            )
        ),
        encoding="utf-8",
    )
    monkeypatch.setattr(
        builder,
        "fetch_source_html",
        lambda url: official if url == builder.OFFICIAL_MUSIC_LIST_URL else FIXTURE_HTML,
    )
    prior = tmp_path / "prior.sqlite"
    output = tmp_path / "candidate.sqlite"
    builder.write_master_database(prior, base)
    builder.main(
        [
            "--output",
            str(output),
            "--identity-registry",
            str(registry),
            "--ac-history",
            str(history),
            "--skip-ddrworld-charts",
        ]
    )
    summary = tmp_path / "summary.json"
    inspect.main([str(output), "--previous-master", str(prior), "--summary", str(summary)])
    report = json.loads(summary.read_text(encoding="utf-8"))
    review_ids = {row["song_id"] for row in report["gp_folder_ac_history"]}
    assert new.song_id in review_ids
    assert first.song_id not in review_ids  # AC folder requires no additional review.
    assert all(row["status"] != "excluded_non_gp" for row in report["gp_folder_ac_history"])
    added = {row["title"]: row for row in report["added_song_ac_history"]}
    assert added["New cover"]["song_id"] == new.song_id
    assert added["New cover"]["status"] == "unresolved"
    assert added["New AC"]["flare_category"] == "GOLD"
    assert added["New exclusive"]["status"] == "confirmed_no_ac"
    with sqlite3.connect(output) as c, sqlite3.connect(prior) as p:
        assert (
            c.execute("select chart_id from charts order by chart_id").fetchall()
            == p.execute("select chart_id from charts order by chart_id").fetchall()
        )
        assert c.execute(
            "select version from songs where song_id=?", (first.song_id,)
        ).fetchone() == (first.version,)
    target = sqlite3.connect(":memory:")
    # Only shared master schema is needed here; apply the actual category migration.
    target.executescript(
        "CREATE TABLE songs(song_id TEXT PRIMARY KEY,title TEXT,artist TEXT,"
        "version TEXT,title_search_key TEXT); CREATE TABLE charts(chart_id TEXT "
        "PRIMARY KEY,song_id TEXT,play_style TEXT,difficulty TEXT,level INTEGER,"
        "is_removed INTEGER);CREATE TABLE web_master_metadata(key TEXT PRIMARY KEY,value TEXT);"
        "CREATE TABLE song_title_search_aliases(song_id TEXT,search_key TEXT);"
    )
    target.executescript(
        Path("web/identity-api/migrations/0009_song_flare_category.sql").read_text()
    )
    sql = export_shared_master_sql(output)
    target.executescript(sql)
    target.executescript(sql)
    assert target.execute(
        "select version,flare_category from songs where song_id=?", (first.song_id,)
    ).fetchone() == (first.version, "CLASSIC")
    assert target.execute(
        "select flare_category from songs where song_id=?", (new.song_id,)
    ).fetchone() == (None,)
    with sqlite3.connect(output) as c:
        c.execute(
            "update song_ac_history set flare_category='GOLD' where song_id=?", (first.song_id,)
        )
    with pytest.raises(ValueError, match="mismatch"):
        inspect.inspect_master_database(output)
