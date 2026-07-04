from __future__ import annotations

import sqlite3
from pathlib import Path

from master import builder

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
        assert metadata["generator_version"] == builder.PARSER_VERSION

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


def test_parse_master_html_handles_edge_level_and_chart_identity_cases() -> None:
    build = builder.parse_master_html(
        EDGE_FIXTURE_HTML,
        source_url="https://example.test/edge-source",
        fetched_at="2026-07-04T00:00:00+00:00",
    )

    assert len(build.songs) == 2
    assert len(build.charts) == 9

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
