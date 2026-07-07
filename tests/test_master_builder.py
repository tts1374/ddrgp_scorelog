from __future__ import annotations

import json
import sqlite3
from pathlib import Path

from master import builder
from master import inspect as master_inspect

FIXTURE_HTML = """
<!doctype html>
<html>
<body>
<table class="style_table">
  <tr>
    <td rowspan="2">分類</td>
    <td rowspan="2">曲名</td>
    <td rowspan="2">アーティスト</td>
    <td rowspan="2">出典</td>
    <td rowspan="2">BPM</td>
    <td rowspan="2">MV/St</td>
    <td colspan="5">SINGLE</td>
    <td colspan="4">DOUBLE</td>
  </tr>
  <tr>
    <td>Be</td><td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
    <td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
  </tr>
  <tr><td colspan="15">DDR 1st</td></tr>
  <tr>
    <td>F 譜2</td><td>MAKE IT BETTER</td><td>mitsu-O!</td>
    <td>DDR 1st IR ver.</td><td>119</td><td>-</td>
    <td>3</td><td>7</td><td>9</td><td>11</td><td>14</td>
    <td>7</td><td>9</td><td>11</td><td>14</td>
  </tr>
  <tr>
    <td></td><td>PARANOiA</td><td>180 (169-183)</td>
    <td>DDR 1st</td><td>180</td><td>○</td>
    <td>4</td><td>8</td><td>9</td><td>11</td><td>-</td>
    <td>8</td><td>13</td><td>11</td><td>-</td>
  </tr>
</table>
<table class="style_table">
  <tr>
    <td rowspan="2">分類</td>
    <td rowspan="2">曲名</td>
    <td rowspan="2">アーティスト</td>
    <td rowspan="2">出典</td>
    <td rowspan="2">BPM</td>
    <td rowspan="2">MV/St</td>
    <td colspan="5">SINGLE</td>
    <td colspan="4">DOUBLE</td>
  </tr>
  <tr>
    <td>Be</td><td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
    <td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
  </tr>
  <tr><td colspan="15">DanceDanceRevolution WORLD</td></tr>
  <tr>
    <td>GP37</td><td>踊るフィーバーロボ Eu-Robot mix</td>
    <td>D&amp;E&amp;Y Rmx by kors k as disconation</td>
    <td>pop'n 17 THE MOVIE／フィーバーロボREMIX</td><td>163</td><td>-</td>
    <td>3</td><td>7</td><td>12</td><td>16</td><td>-</td>
    <td>8</td><td>12</td><td>16</td><td>-</td>
  </tr>
</table>
</body>
</html>
"""

EDGE_FIXTURE_HTML = """
<!doctype html>
<html>
<body>
<table class="style_table">
  <tr>
    <td rowspan="2">分類</td>
    <td rowspan="2">曲名</td>
    <td rowspan="2">アーティスト</td>
    <td rowspan="2">出典</td>
    <td rowspan="2">BPM</td>
    <td rowspan="2">MV/St</td>
    <td colspan="5">SINGLE</td>
    <td colspan="4">DOUBLE</td>
  </tr>
  <tr>
    <td>Be</td><td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
    <td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
  </tr>
  <tr><td colspan="15">DDR Edge Cases</td></tr>
  <tr>
    <td>削 GP99</td><td>LIMITED TEST</td><td>Unit A</td>
    <td>DDR GP Test Pack</td><td>150</td><td>-</td>
    <td>10(旧9)</td><td>[SA] 12</td><td>10;</td><td>-</td><td>-</td>
    <td>-</td><td>-</td><td>-</td><td>-</td>
  </tr>
  <tr>
    <td></td><td>SIDE ONLY</td><td>Same Unit</td>
    <td>DDR GP Test Pack</td><td>140</td><td>-</td>
    <td>-</td><td>-</td><td>-</td><td>-</td><td>-</td>
    <td>6</td><td>8</td><td>-</td><td>-</td>
  </tr>
  <tr>
    <td></td><td>SIDE ONLY</td><td>Same Unit</td>
    <td>DDR GP Test Pack</td><td>140</td><td>-</td>
    <td>1</td><td>4</td><td>7</td><td>10</td><td>-</td>
    <td>-</td><td>-</td><td>-</td><td>-</td>
  </tr>
  <tr>
    <td></td><td>IX<a href="#note1">*2</a></td><td>dj TAKA VS DJ TOTTO feat.藍</td>
    <td>DDR GP Test Pack</td><td>198</td><td>-</td>
    <td>5</td><td>-</td><td>-</td><td>-</td><td>-</td>
    <td>-</td><td>-</td><td>-</td><td>-</td>
  </tr>
  <tr>
    <td></td><td>neko*neko</td><td>日向美ビタースイーツ♪</td>
    <td>DDR GP Test Pack</td><td>123</td><td>-</td>
    <td>2</td><td>-</td><td>-</td><td>-</td><td>-</td>
    <td>-</td><td>-</td><td>-</td><td>-</td>
  </tr>
</table>
</body>
</html>
"""

OFFICIAL_FIXTURE_HTML = """
<!doctype html>
<html>
<body>
<table class="m_list">
  <tr>
    <th>タイトル</th><th>アーティスト</th>
    <th>フリープレー</th><th>グランプリプレー</th>
  </tr>
  <tr><td>2026年4月3日追加</td></tr>
  <tr>
    <td>MAKE IT BETTER</td><td>mitsu-O!</td><td>〇　※１</td><td>〇</td>
  </tr>
  <tr>
    <td>PARANOiA</td><td>180 (169-183)</td><td>〇　※１</td><td></td>
  </tr>
</table>
<table class="m_list">
  <tr>
    <th>タイトル</th><th>アーティスト</th>
    <th>フリープレー</th><th>グランプリプレー</th><th>備考</th>
  </tr>
  <tr><td>グランプリ楽曲パック vol.37</td></tr>
  <tr>
    <td>踊るフィーバーロボ　Eu-Robot mix</td>
    <td>D&amp;E&amp;Y Rmx by kors k as disconation</td>
    <td></td><td>〇</td><td>先行プレー対象</td>
  </tr>
</table>
</body>
</html>
"""

ALIAS_FIXTURE_HTML = """
<!doctype html>
<html>
<body>
<table class="style_table">
  <tr>
    <td rowspan="2">分類</td>
    <td rowspan="2">曲名</td>
    <td rowspan="2">アーティスト</td>
    <td rowspan="2">出典</td>
    <td rowspan="2">BPM</td>
    <td rowspan="2">MV/St</td>
    <td colspan="5">SINGLE</td>
    <td colspan="4">DOUBLE</td>
  </tr>
  <tr>
    <td>Be</td><td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
    <td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
  </tr>
  <tr><td colspan="15">DDR Alias Cases</td></tr>
  <tr>
    <td>GP5</td><td>RËVOLUTIФN</td><td>TËЯRA</td>
    <td>DDR GP Test Pack</td><td>202</td><td>-</td>
    <td>-</td><td>-</td><td>-</td><td>-</td><td>17</td>
    <td>-</td><td>-</td><td>-</td><td>-</td>
  </tr>
</table>
</body>
</html>
"""

OFFICIAL_ALIAS_FIXTURE_HTML = """
<!doctype html>
<html>
<body>
<table class="m_list">
  <tr>
    <th>タイトル</th><th>アーティスト</th>
    <th>フリープレー</th><th>グランプリプレー</th>
  </tr>
  <tr>
    <td>RЁVOLUTIФN</td><td>TЁЯRA</td><td></td><td>〇</td>
  </tr>
</table>
</body>
</html>
"""


def test_parse_level_uses_first_numeric_token_without_joining_notes() -> None:
    assert builder.parse_level("10(旧9)") == 10
    assert builder.parse_level("[SA] 12") == 12
    assert builder.parse_level("10;") == 10
    assert builder.parse_level("-") is None


def test_parse_master_html_extracts_songs_and_available_charts() -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )

    assert len(build.songs) == 3
    assert len(build.charts) == 23
    assert build.snapshot.source_url == "https://example.test/source"
    assert len(build.snapshot.content_hash) == 64

    first_song = next(song for song in build.songs if song.title == "MAKE IT BETTER")
    assert first_song.version == "DDR 1st"
    assert first_song.artist == "mitsu-O!"
    assert first_song.availability == "F 譜2"

    first_song_charts = [chart for chart in build.charts if chart.song_id == first_song.song_id]
    assert len(first_song_charts) == 9
    assert {
        (chart.play_style, chart.difficulty, chart.level, chart.is_limited)
        for chart in first_song_charts
    } >= {
        ("SINGLE", "BEGINNER", 3, True),
        ("SINGLE", "CHALLENGE", 14, True),
        ("DOUBLE", "CHALLENGE", 14, True),
    }

    paranoia = next(song for song in build.songs if song.title == "PARANOiA")
    paranoia_charts = [chart for chart in build.charts if chart.song_id == paranoia.song_id]
    assert len(paranoia_charts) == 7
    assert ("SINGLE", "CHALLENGE") not in {
        (chart.play_style, chart.difficulty) for chart in paranoia_charts
    }


def test_parse_master_html_applies_official_grand_prix_availability() -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        official_html=OFFICIAL_FIXTURE_HTML,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )

    make = next(song for song in build.songs if song.title == "MAKE IT BETTER")
    paranoia = next(song for song in build.songs if song.title == "PARANOiA")
    fever = next(song for song in build.songs if song.title == "踊るフィーバーロボ Eu-Robot mix")

    assert make.free_play_available
    assert make.grand_prix_play_available
    assert make.official_availability_match == "title_artist"
    assert paranoia.free_play_available
    assert not paranoia.grand_prix_play_available
    assert fever.grand_prix_play_available
    assert build.official_snapshot is not None
    assert build.official_snapshot.source_url == "https://example.test/official"


def test_parse_master_html_uses_official_title_artist_as_canonical_alias_match() -> None:
    build = builder.parse_master_html(
        ALIAS_FIXTURE_HTML,
        source_url="https://example.test/source",
        official_html=OFFICIAL_ALIAS_FIXTURE_HTML,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )

    song = build.songs[0]

    assert song.title == "RЁVOLUTIФN"
    assert song.artist == "TЁЯRA"
    assert song.grand_prix_play_available
    assert song.official_availability_match == "alias_title_artist"
    assert build.song_aliases == (
        builder.MasterSongAlias(
            alias_id=builder.stable_id(
                "alias",
                song.song_id,
                "RËVOLUTIФN",
                "TËЯRA",
                "wiki_source",
            ),
            song_id=song.song_id,
            alias_title="RËVOLUTIФN",
            alias_artist="TËЯRA",
            alias_type="wiki_source",
            source="bemaniwiki",
        ),
    )


def test_write_master_database_creates_expected_schema_and_metadata(tmp_path: Path) -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"

    builder.write_master_database(
        output_path,
        build,
        master_version="fixture-v1",
        generated_at="2026-07-04T01:23:45+00:00",
    )

    with sqlite3.connect(output_path) as connection:
        song_count = connection.execute("SELECT COUNT(*) FROM songs").fetchone()[0]
        chart_count = connection.execute("SELECT COUNT(*) FROM charts").fetchone()[0]
        assert song_count == 3
        assert chart_count == 23

        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        assert metadata["master_version"] == "fixture-v1"
        assert metadata["source_url"] == "https://example.test/source"
        assert metadata["song_count"] == "3"
        assert metadata["chart_count"] == "23"
        assert metadata["song_alias_count"] == "0"
        assert metadata["generator_version"] == builder.PARSER_VERSION
        assert metadata["grand_prix_play_available_song_count"] == "0"

        rows = connection.execute(
            """
            SELECT s.title, c.play_style, c.difficulty, c.level, c.raw_level
            FROM charts c
            JOIN songs s ON s.song_id = c.song_id
            WHERE s.title = '踊るフィーバーロボ Eu-Robot mix'
            ORDER BY c.play_style, c.difficulty
            """
        ).fetchall()
        assert ("踊るフィーバーロボ Eu-Robot mix", "DOUBLE", "BASIC", 8, "8") in rows
        assert ("踊るフィーバーロボ Eu-Robot mix", "SINGLE", "CHALLENGE", 0, "-") not in rows

        snapshot = connection.execute(
            "SELECT source_url, content_hash, parser_version, html_content FROM source_snapshots"
        ).fetchone()
        assert snapshot[0] == "https://example.test/source"
        assert snapshot[1] == build.snapshot.content_hash
        assert snapshot[2] == builder.PARSER_VERSION
        assert "MAKE IT BETTER" in snapshot[3]


def test_write_master_database_records_official_availability_snapshot(
    tmp_path: Path,
) -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        official_html=OFFICIAL_FIXTURE_HTML,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"

    builder.write_master_database(
        output_path,
        build,
        master_version="fixture-v1",
        generated_at="2026-07-04T01:23:45+00:00",
    )

    with sqlite3.connect(output_path) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        assert metadata["official_source_url"] == "https://example.test/official"
        assert metadata["official_source_hash"] == build.official_snapshot.content_hash
        assert metadata["grand_prix_play_available_song_count"] == "2"
        assert metadata["free_play_available_song_count"] == "2"
        assert metadata["official_availability_matched_song_count"] == "3"
        assert metadata["song_alias_count"] == "0"
        rows = connection.execute(
            """
            SELECT title, free_play_available, grand_prix_play_available,
                   official_availability_match
            FROM songs
            ORDER BY title
            """
        ).fetchall()
        assert ("MAKE IT BETTER", 1, 1, "title_artist") in rows
        assert ("PARANOiA", 1, 0, "title_artist") in rows
        assert ("踊るフィーバーロボ Eu-Robot mix", 0, 1, "title_artist") in rows
        assert connection.execute("SELECT COUNT(*) FROM source_snapshots").fetchone()[0] == 2

    summary = master_inspect.inspect_master_database(output_path)
    assert summary["snapshot_count"] == 2
    assert summary["official_source_url"] == "https://example.test/official"
    assert summary["official_source_hash"] == build.official_snapshot.content_hash
    assert summary["grand_prix_play_available_song_count"] == "2"
    assert summary["song_alias_count"] == 0


def test_write_master_database_records_song_aliases_for_official_canonical_match(
    tmp_path: Path,
) -> None:
    build = builder.parse_master_html(
        ALIAS_FIXTURE_HTML,
        source_url="https://example.test/source",
        official_html=OFFICIAL_ALIAS_FIXTURE_HTML,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"

    builder.write_master_database(
        output_path,
        build,
        master_version="fixture-v1",
        generated_at="2026-07-04T01:23:45+00:00",
    )

    with sqlite3.connect(output_path) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        assert metadata["grand_prix_play_available_song_count"] == "1"
        assert metadata["official_availability_matched_song_count"] == "1"
        assert metadata["song_alias_count"] == "1"
        assert connection.execute(
            "SELECT title, artist, official_availability_match FROM songs"
        ).fetchone() == ("RЁVOLUTIФN", "TЁЯRA", "alias_title_artist")
        assert connection.execute(
            "SELECT alias_title, alias_artist, alias_type, source FROM song_aliases"
        ).fetchone() == ("RËVOLUTIФN", "TËЯRA", "wiki_source", "bemaniwiki")

    summary = master_inspect.inspect_master_database(output_path)
    assert summary["song_alias_count"] == 1


def test_inspect_master_database_writes_summary_for_valid_database(tmp_path: Path) -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"
    summary_path = tmp_path / "master-summary.json"
    builder.write_master_database(
        output_path,
        build,
        master_version="fixture-v1",
        generated_at="2026-07-04T01:23:45+00:00",
    )

    summary = master_inspect.inspect_master_database(output_path)
    master_inspect.write_summary(summary_path, summary)

    assert summary["song_count"] == 3
    assert summary["chart_count"] == 23
    assert summary["snapshot_count"] == 1
    assert summary["master_version"] == "fixture-v1"
    assert summary["source_hash"] == build.snapshot.content_hash
    assert summary["snapshot_source_hash"] == build.snapshot.content_hash
    assert summary["source_url"] == "https://example.test/source"
    assert summary["snapshot_source_url"] == "https://example.test/source"
    assert summary["snapshot_parser_version"] == builder.PARSER_VERSION
    assert json.loads(summary_path.read_text(encoding="utf-8")) == summary


def test_inspect_master_database_rejects_missing_required_metadata(tmp_path: Path) -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"
    builder.write_master_database(output_path, build, master_version="fixture-v1")

    with sqlite3.connect(output_path) as connection:
        connection.execute("DELETE FROM master_metadata WHERE key = 'generator_version'")

    try:
        master_inspect.inspect_master_database(output_path)
    except ValueError as exc:
        assert "missing required keys" in str(exc)
        assert "generator_version" in str(exc)
    else:
        raise AssertionError("inspect_master_database should reject missing metadata")


def test_inspect_master_database_rejects_metadata_count_mismatch(tmp_path: Path) -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"
    builder.write_master_database(output_path, build, master_version="fixture-v1")

    with sqlite3.connect(output_path) as connection:
        connection.execute(
            "UPDATE master_metadata SET value = '999' WHERE key = 'song_count'"
        )

    try:
        master_inspect.inspect_master_database(output_path)
    except ValueError as exc:
        assert "song_count" in str(exc)
    else:
        raise AssertionError("inspect_master_database should reject mismatched metadata")


def test_inspect_master_database_rejects_source_hash_mismatch(tmp_path: Path) -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"
    builder.write_master_database(output_path, build, master_version="fixture-v1")

    with sqlite3.connect(output_path) as connection:
        connection.execute(
            "UPDATE master_metadata SET value = 'mismatched' WHERE key = 'source_hash'"
        )

    try:
        master_inspect.inspect_master_database(output_path)
    except ValueError as exc:
        assert "source_hash" in str(exc)
    else:
        raise AssertionError("inspect_master_database should reject mismatched source hash")


def test_inspect_master_database_rejects_source_url_mismatch(tmp_path: Path) -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"
    builder.write_master_database(output_path, build, master_version="fixture-v1")

    with sqlite3.connect(output_path) as connection:
        connection.execute(
            "UPDATE master_metadata SET value = 'https://example.test/other' "
            "WHERE key = 'source_url'"
        )

    try:
        master_inspect.inspect_master_database(output_path)
    except ValueError as exc:
        assert "source_url" in str(exc)
    else:
        raise AssertionError("inspect_master_database should reject mismatched source URL")


def test_parse_master_html_handles_edge_level_and_chart_identity_cases() -> None:
    build = builder.parse_master_html(
        EDGE_FIXTURE_HTML,
        source_url="https://example.test/edge-source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )

    assert len(build.songs) == 4
    assert len(build.charts) == 11

    limited_song = next(song for song in build.songs if song.title == "LIMITED TEST")
    limited_charts = [chart for chart in build.charts if chart.song_id == limited_song.song_id]
    assert len(limited_charts) == 3
    assert {(chart.difficulty, chart.level, chart.raw_level) for chart in limited_charts} == {
        ("BEGINNER", 10, "10(旧9)"),
        ("BASIC", 12, "[SA] 12"),
        ("DIFFICULT", 10, "10;"),
    }
    assert all(chart.is_removed for chart in limited_charts)
    assert all(chart.is_limited for chart in limited_charts)
    assert all(chart.notes == "削 GP99" for chart in limited_charts)
    assert {
        (chart.difficulty, chart.shock_arrow) for chart in limited_charts
    } >= {
        ("BASIC", True),
        ("BEGINNER", False),
    }

    side_only_song = next(song for song in build.songs if song.title == "SIDE ONLY")
    side_only_charts = [chart for chart in build.charts if chart.song_id == side_only_song.song_id]
    assert len(side_only_charts) == 6
    assert {
        (chart.play_style, chart.difficulty, chart.level) for chart in side_only_charts
    } == {
        ("DOUBLE", "BASIC", 6),
        ("DOUBLE", "DIFFICULT", 8),
        ("SINGLE", "BEGINNER", 1),
        ("SINGLE", "BASIC", 4),
        ("SINGLE", "DIFFICULT", 7),
        ("SINGLE", "EXPERT", 10),
    }

    ix_song = next(song for song in build.songs if song.artist == "dj TAKA VS DJ TOTTO feat.藍")
    assert ix_song.title == "IX"
    assert any(song.title == "neko*neko" for song in build.songs)


def test_parse_master_html_rejects_conflicting_duplicate_chart_identity() -> None:
    conflicting_html = EDGE_FIXTURE_HTML.replace(
        "    <td>1</td><td>4</td><td>7</td><td>10</td><td>-</td>\n"
        "    <td>-</td><td>-</td><td>-</td><td>-</td>",
        "    <td>1</td><td>4</td><td>7</td><td>10</td><td>-</td>\n"
        "    <td>9</td><td>-</td><td>-</td><td>-</td>",
    )

    try:
        builder.parse_master_html(conflicting_html)
    except ValueError as exc:
        assert "conflicting chart rows" in str(exc)
    else:
        raise AssertionError("parse_master_html should reject conflicting chart rows")


def test_parse_master_html_rejects_missing_song_list_table() -> None:
    try:
        builder.parse_master_html("<html><table><tr><td>not songs</td></tr></table></html>")
    except ValueError as exc:
        assert "song list tables" in str(exc)
    else:
        raise AssertionError("parse_master_html should reject unrelated HTML")
