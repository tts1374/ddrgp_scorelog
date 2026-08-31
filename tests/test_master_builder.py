from __future__ import annotations

import hashlib
import json
import sqlite3
from dataclasses import replace
from html import escape
from pathlib import Path

from master import builder
from master import inspect as master_inspect

EXPECTED_CONFIRMED_CHALLENGE_LEVELS = {
    "7 Colors": (16, 16),
    "Ace out": (14, 14),
    "ALPACORE": (17, 17),
    "BITTER CHOCOLATE STRIKER": (18, 18),
    "Come Back To Me": (16, 16),
    "CyberConnect": (17, 17),
    "DIGITALIZER": (18, 18),
    "Din Don Dan (にじさんじダンス部 ver.)": (16, 16),
    "Draw the Savage": (15, 14),
    "Give Me": (16, 16),
    "Glitch Angel": (18, 18),
    "Going Hypersonic": (17, 17),
    "Golden Arrow": (17, 17),
    "Good Looking": (17, 18),
    "Harmonia": (16, 16),
    "In The Breeze": (14, 14),
    "Lightspeed": (18, 18),
    "MUTEKI BUFFALO": (17, 17),
    "New Era": (18, 18),
    "Rampage Hero": (17, 17),
    "Run The Show": (16, 16),
    "Starlight in the Snow": (16, 16),
    "Step This Way": (17, 17),
    "Superior MAXXX": (19, 19),
    "Take A Step Forward": (15, 15),
    "The World Ends Now": (18, 18),
    "Touch My Body": (14, 14),
    "True Blue": (17, 17),
    "Yuni's Nocturnal Days": (18, 18),
    "クリムゾンゲイト": (16, 16),
    "パ→ピ→プ→Yeah!": (15, 16),
    "和風インザ洋風": (17, 17),
    "打打打打打打打打打打 (にじさんじダンス部 ver.)": (16, 16),
    "灼熱Beach Side Bunny": (18, 18),
}

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

NEW_SONG_FIXTURE_HTML = """
<!doctype html>
<html>
<body>
<table class="style_table">
  <tr>
    <td rowspan="2">分類</td><td rowspan="2">曲名</td><td rowspan="2">アーティスト</td>
    <td rowspan="2">出典</td><td rowspan="2">BPM</td><td rowspan="2">MV/St</td>
    <td colspan="5">SINGLE</td><td colspan="4">DOUBLE</td>
  </tr>
  <tr>
    <td>Be</td><td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
    <td>Ba</td><td>Di</td><td>Ex</td><td>Ch</td>
  </tr>
  <tr><td colspan="15">DDR GP New Songs</td></tr>
  <tr>
    <td>GP100</td><td>NEW ONLY</td><td>New Artist</td><td>DDR GP</td><td>150</td><td>-</td>
    <td>5</td><td>9</td><td>-</td><td>13</td><td>-</td>
    <td>9</td><td>-</td><td>13</td><td>-</td>
  </tr>
  <tr>
    <td>GP101</td><td>Party Lights (Tommie Sunshine’s Brooklyn Fire Remix)</td>
    <td>Tommie Sunshine</td><td>DDR GP</td><td>128</td><td>-</td>
    <td>6</td><td>-</td><td>10</td><td>-</td><td>-</td>
    <td>7</td><td>-</td><td>11</td><td>-</td>
  </tr>
  <tr>
    <td>GP102</td><td>LICENSED EMPTY ARTIST</td><td>Copyright Artist</td>
    <td>DDR GP</td><td>128</td><td>-</td>
    <td>4</td><td>-</td><td>8</td><td>-</td><td>-</td>
    <td>5</td><td>-</td><td>9</td><td>-</td>
  </tr>
</table>
</body>
</html>
"""

OFFICIAL_CANONICAL_FIXTURE_HTML = """
<!doctype html>
<html>
<body>
<table class="m_list">
  <tr>
    <th>タイトル</th><th>アーティスト</th><th>フリープレー</th><th>グランプリプレー</th>
  </tr>
  <tr>
    <td>Party Lights (Tommie Sunshine's Brooklyn Fire Remix)</td>
    <td>Tommie Sunshine</td><td></td><td>〇</td>
  </tr>
  <tr>
    <td>LICENSED EMPTY ARTIST</td><td></td><td></td><td>〇</td>
  </tr>
</table>
</body>
</html>
"""

OFFICIAL_GP_ONLY_FIXTURE_HTML = """
<!doctype html>
<html>
<body>
<table class="m_list">
  <tr>
    <th>タイトル</th><th>アーティスト</th><th>フリープレー</th><th>グランプリプレー</th>
  </tr>
  <tr>
    <td>Din Don Dan (にじさんじダンス部 ver.)</td>
    <td>レイン・パターソン &amp; 山神カルタ &amp; 東堂コハク</td>
    <td>〇</td><td>〇</td>
  </tr>
  <tr>
    <td>打打打打打打打打打打 (にじさんじダンス部 ver.)</td>
    <td>長尾景 &amp; 倉持めると &amp; セラフ・ダズルガーデン</td>
    <td>〇</td><td>〇</td>
  </tr>
  <tr><td>創聖のアクエリオン</td><td></td><td>〇</td><td>〇</td></tr>
  <tr>
    <td>Leaving…</td><td>seiya-murai meets “eimy”</td><td></td><td>〇</td>
  </tr>
</table>
</body>
</html>
"""


def _ddrworld_row(
    title: str,
    artist: str,
    single: tuple[int | None, ...],
    double: tuple[int | None, ...],
) -> str:
    def style_row(label: str, levels: tuple[int | None, ...]) -> str:
        style = {"SP": "SINGLE", "DP": "DOUBLE"}[label]
        values = "".join(
            f'<div class="diff {difficulty}"><span>Lv. </span><div class="level">'
            f'{"-" if level is None else level}</div></div>'
            for difficulty, level in zip(
                builder.DIFFICULTIES_BY_STYLE[style], levels, strict=True
            )
        )
        return f'<div class="diff-style-container"><div class="label">{label}</div>{values}</div>'

    return (
        '<tr class="data"><td class="chart"><div class="music-container">'
        f'<div class="music-title">{escape(title)}</div><div class="artist">{escape(artist)}</div>'
        f'{style_row("SP", single)}{style_row("DP", double)}'
        "</div></td></tr>"
    )


DDRWORLD_REGRESSION_HTML = (
    '<!doctype html><html><body><table class="table-ui"><tbody>'
    + _ddrworld_row(
        "Sucka Luva",
        "Harmony Machine",
        (2, 5, 8, 11, None),
        (5, 9, 11, None),
    )
    + _ddrworld_row(
        "Din Don Dan (にじさんじダンス部 ver.)",
        "レイン・パターソン & 山神カルタ & 東堂コハク",
        (1, 3, 9, 12, 16),
        (3, 9, 12, 16),
    )
    + _ddrworld_row(
        "打打打打打打打打打打 (にじさんじダンス部 ver.)",
        "長尾景 & 倉持めると & セラフ・ダズルガーデン",
        (2, 5, 10, 14, 16),
        (6, 11, 14, 16),
    )
    + _ddrworld_row("WORLD ONLY", "World Artist", (1, 3, 5, 7, None), (3, 5, 7, None))
    + "</tbody></table></body></html>"
)

REGRESSION_WIKI_HTML = FIXTURE_HTML.replace(
    '<tr><td colspan="15">DanceDanceRevolution WORLD</td></tr>',
    """
  <tr><td colspan="15">DanceDanceRevolution WORLD</td></tr>
  <tr>
    <td>GP</td><td>7 Colors</td><td>kors k feat.吉河順央</td><td>DDR GP</td><td>150</td><td>-</td>
    <td>2</td><td>5</td><td>11</td><td>14</td><td>16</td>
    <td>5</td><td>11</td><td>14</td><td>16</td>
  </tr>
  <tr>
    <td>GP</td><td>Sucka Luva</td><td>Harmony Machine</td><td>DDR GP</td><td>128</td><td>-</td>
    <td>1</td><td>4</td><td>7</td><td>11</td><td>-</td>
    <td>5</td><td>8</td><td>11</td><td>-</td>
  </tr>
""",
)

OFFICIAL_REGRESSION_HTML = """
<!doctype html>
<html>
<body>
<table class="m_list">
  <tr><th>タイトル</th><th>アーティスト</th><th>フリープレー</th><th>グランプリプレー</th></tr>
  <tr><td>7 Colors</td><td>kors k feat.吉河順央</td><td></td><td>〇</td></tr>
  <tr><td>Sucka Luva</td><td>Harmony Machine</td><td>〇</td><td>〇</td></tr>
  <tr>
    <td>Din Don Dan (にじさんじダンス部 ver.)</td>
    <td>レイン・パターソン &amp; 山神カルタ &amp; 東堂コハク</td>
    <td></td><td>〇</td>
  </tr>
  <tr>
    <td>打打打打打打打打打打 (にじさんじダンス部 ver.)</td>
    <td>長尾景 &amp; 倉持めると &amp; セラフ・ダズルガーデン</td>
    <td></td><td>〇</td>
  </tr>
</table>
</body>
</html>
"""


def test_parse_ddrworld_music_page_extracts_sp_and_dp_levels() -> None:
    source = builder.ddrworld_source_from_html(
        DDRWORLD_REGRESSION_HTML,
        source_url=builder.DDRWORLD_MUSIC_SOURCE_URL,
        fetched_at="2026-08-31T00:00:00+00:00",
    )

    sucka = next(song for song in source.songs if song.title == "Sucka Luva")
    assert {
        (chart.play_style, chart.difficulty, chart.level)
        for chart in sucka.charts
    } == {
        ("SINGLE", "BEGINNER", 2),
        ("SINGLE", "BASIC", 5),
        ("SINGLE", "DIFFICULT", 8),
        ("SINGLE", "EXPERT", 11),
        ("DOUBLE", "BASIC", 5),
        ("DOUBLE", "DIFFICULT", 9),
        ("DOUBLE", "EXPERT", 11),
    }
    assert source.chart_count == sum(len(song.charts) for song in source.songs)
    assert len(source.snapshot.content_hash) == 64


def test_ddrworld_priority_adds_gp_charts_without_promoting_world_only_song(
    tmp_path: Path,
) -> None:
    build = builder.parse_master_html(
        REGRESSION_WIKI_HTML,
        source_url="https://example.test/wiki",
        official_html=OFFICIAL_REGRESSION_HTML,
        official_source_url="https://example.test/official",
        ddrworld_html=DDRWORLD_REGRESSION_HTML,
        ddrworld_source_url=builder.DDRWORLD_MUSIC_SOURCE_URL,
        fetched_at="2026-08-31T00:00:00+00:00",
    )

    def chart_levels(title: str) -> set[tuple[str, str, int]]:
        song = next(song for song in build.songs if song.title == title)
        return {
            (chart.play_style, chart.difficulty, chart.level)
            for chart in build.charts
            if chart.song_id == song.song_id
        }

    assert chart_levels("Sucka Luva") == {
        ("SINGLE", "BEGINNER", 2),
        ("SINGLE", "BASIC", 5),
        ("SINGLE", "DIFFICULT", 8),
        ("SINGLE", "EXPERT", 11),
        ("DOUBLE", "BASIC", 5),
        ("DOUBLE", "DIFFICULT", 9),
        ("DOUBLE", "EXPERT", 11),
    }
    for title in (
        "Din Don Dan (にじさんじダンス部 ver.)",
        "打打打打打打打打打打 (にじさんじダンス部 ver.)",
    ):
        levels = chart_levels(title)
        assert len(levels) == 9
        assert sum(difficulty == "CHALLENGE" for _, difficulty, _ in levels) == 2
        assert all(level == 16 for _, difficulty, level in levels if difficulty == "CHALLENGE")
        assert all(
            "DDR WORLD official chart;" in chart.notes
            for chart in build.charts
            if chart.song_id == next(song for song in build.songs if song.title == title).song_id
        )

    seven_colors = next(song for song in build.songs if song.title == "7 Colors")
    seven_colors_challenge = next(
        chart
        for chart in build.charts
        if chart.song_id == seven_colors.song_id
        and chart.play_style == "SINGLE"
        and chart.difficulty == "CHALLENGE"
    )
    assert seven_colors_challenge.level == 16
    assert "DDR WORLD official chart;" not in seven_colors_challenge.notes
    assert not any(song.title == "WORLD ONLY" for song in build.songs)

    counts = build.ddrworld_merge_report["counts"]
    assert counts["official_only"] == 14
    assert counts["official_override"] == 11
    assert counts["wiki_only"] == 9
    assert counts["supplement_only"] == 0
    assert counts["world_only_outside_gp"] == 7
    assert counts["unmatchable_gp_candidate"] == 0
    assert counts["ambiguous_gp_candidate"] == 0
    assert sum(counts[status] for status in builder.DDRWORLD_MERGE_STATUSES) == len(
        build.ddrworld_merge_report["rows"]
    )
    charts_by_id = {chart.chart_id: chart for chart in build.charts}
    assert all(
        charts_by_id[row["chart_id"]].level == row["official_level"]
        for row in build.ddrworld_merge_report["rows"]
        if row["status"] in {"official_only", "official_override"}
    )
    assert counts["level_changed"] == 4
    assert counts["level_unchanged"] == 7

    output_path = tmp_path / "ddrgp-master.sqlite"
    builder.write_master_database(output_path, build, master_version="fixture-world-v1")
    summary = master_inspect.inspect_master_database(output_path)
    assert summary["snapshot_count"] == 3
    assert summary["ddrworld_source_hash"] == build.ddrworld_snapshot.snapshot.content_hash
    assert summary["ddrworld_merge_counts"] == counts
    assert summary["chart_id_duplicate_count"] == 0
    assert summary["chart_identity_duplicate_count"] == 0
    assert summary["referential_integrity_error_count"] == 0


def test_ddrworld_snapshot_loader_validates_collector_contract(tmp_path: Path) -> None:
    page_url = (
        "https://p.eagate.573.jp/game/ddr/ddrworld/music/index.html?"
        "offset=0&filter=7&filtertype=0&playmode=2"
    )
    page_path = tmp_path / "pages" / "page-00.html"
    page_path.parent.mkdir()
    page_path.write_bytes(DDRWORLD_REGRESSION_HTML.encode("utf-8"))
    page_hash = hashlib.sha256(page_path.read_bytes()).hexdigest()
    page_songs = builder.parse_ddrworld_music_page(
        DDRWORLD_REGRESSION_HTML,
        page_offset=0,
        page_url=page_url,
    )
    songs_path = tmp_path / "songs.jsonl"
    songs_path.write_text(
        "".join(
            json.dumps(
                {
                    "source_page": song.source_page,
                    "page_position": song.page_position,
                    "title": song.title,
                    "artist": song.artist,
                },
                ensure_ascii=False,
            )
            + "\n"
            for song in page_songs
        ),
        encoding="utf-8",
        newline="\n",
    )
    manifest = {
        "schema_version": "ddrworld-music-snapshot-manifest-v1",
        "status": "complete",
        "snapshot_id": "fixture-snapshot",
        "collector_version": "ddrworld-music-snapshot-v1",
        "source": {
            "origin": builder.DDRWORLD_SOURCE_ORIGIN,
            "path": builder.DDRWORLD_SOURCE_PATH,
            "filter": 7,
            "filter_type": 0,
            "play_mode": 2,
            "offsets": [0],
        },
        "started_at": "2026-08-31T00:00:00Z",
        "completed_at": "2026-08-31T00:00:02Z",
        "pages": [
            {
                "offset": 0,
                "source_url": page_url,
                "fetched_at": "2026-08-31T00:00:01Z",
                "status_code": 200,
                "content_type": "text/html",
                "byte_size": page_path.stat().st_size,
                "sha256": page_hash,
                "local_path": "pages/page-00.html",
                "error": None,
            }
        ],
        "pagination": {
            "strategy": "empty_page",
            "max_page_count": 100,
            "terminal_offset": 1,
            "terminal_validation": "normal_empty_page",
            "terminal_page": {"offset": 1, "validation": "normal_empty_page"},
        },
        "images": [],
        "failures": [],
    }
    summary = {
        "schema_version": "ddrworld-music-snapshot-summary-v1",
        "status": "complete",
        "snapshot_id": "fixture-snapshot",
        "request_count": 2,
        "page_request_count": 1,
        "terminal_offset": 1,
        "image_request_count": 0,
        "song_count": len(page_songs),
        "unique_jacket_url_count": 0,
        "stored_jacket_count": 0,
        "failure_count": 0,
        "duplicate_image_hash_count": 0,
        "duplicate_image_hashes": [],
    }
    (tmp_path / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    (tmp_path / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    loaded = builder.load_ddrworld_snapshot(tmp_path)
    assert loaded.snapshot_id == "fixture-snapshot"
    assert loaded.page_count == 1
    assert loaded.chart_count == sum(len(song.charts) for song in loaded.songs)
    assert loaded.snapshot.source_url == builder.DDRWORLD_MUSIC_SOURCE_URL


def test_ddrworld_ambiguous_song_is_reported_without_chart_merge() -> None:
    songs = tuple(
        builder.MasterSong(
            song_id=builder.stable_id("song", "DUPLICATE TITLE", artist),
            title="DUPLICATE TITLE",
            artist=artist,
            version="fixture",
            source_version="fixture",
            bpm="100",
            category="fixture",
            movie_stage="",
            availability="",
            notes="",
            grand_prix_play_available=True,
        )
        for artist in ("Artist A", "Artist B")
    )
    world_html = (
        '<table class="table-ui"><tbody>'
        + _ddrworld_row(
            "DUPLICATE TITLE",
            "Other Artist",
            (1, None, None, None, None),
            (1, None, None, None),
        )
        + "</tbody></table>"
    )
    source = builder.ddrworld_source_from_html(world_html)

    charts, report = builder.merge_ddrworld_chart_data(songs, (), source)

    assert charts == ()
    assert report["counts"]["ambiguous_gp_candidate"] == 2
    assert report["counts"]["ambiguous_gp_candidate_song_count"] == 1
    assert {row["reason"] for row in report["rows"]} == {"ambiguous_title"}


def test_ddrworld_non_gp_chart_is_excluded_without_level_override() -> None:
    song = builder.MasterSong(
        song_id=builder.stable_id("song", "NON GP", "Artist"),
        title="NON GP",
        artist="Artist",
        version="fixture",
        source_version="fixture",
        bpm="100",
        category="fixture",
        movie_stage="",
        availability="",
        notes="",
        grand_prix_play_available=False,
    )
    chart = builder.MasterChart(
        chart_id=builder.stable_id("chart", song.song_id, "SINGLE", "BEGINNER"),
        song_id=song.song_id,
        play_style="SINGLE",
        difficulty="BEGINNER",
        level=5,
        raw_level="5",
        shock_arrow=False,
        is_removed=False,
        is_limited=False,
        notes="wiki baseline",
    )
    source = builder.ddrworld_source_from_html(
        '<table class="table-ui"><tbody>'
        + _ddrworld_row(
            "NON GP",
            "Artist",
            (6, None, None, None, None),
            (None, None, None, None),
        )
        + "</tbody></table>"
    )

    charts, report = builder.merge_ddrworld_chart_data((song,), (chart,), source)

    assert charts == (chart,)
    assert report["counts"]["excluded_non_gp"] == 1
    row = report["rows"][0]
    assert row["status"] == "excluded_non_gp"
    assert row["baseline_level"] == 5
    assert row["official_level"] == 6


def test_ddrworld_confirmed_challenge_without_official_chart_is_supplement_only() -> None:
    song = builder.MasterSong(
        song_id=builder.stable_id("song", "SUPPLEMENT SONG", "Artist"),
        title="SUPPLEMENT SONG",
        artist="Artist",
        version="fixture",
        source_version="fixture",
        bpm="100",
        category="fixture",
        movie_stage="",
        availability="",
        notes="",
        grand_prix_play_available=True,
    )
    challenge = builder.MasterChart(
        chart_id=builder.stable_id("chart", song.song_id, "SINGLE", "CHALLENGE"),
        song_id=song.song_id,
        play_style="SINGLE",
        difficulty="CHALLENGE",
        level=16,
        raw_level="16",
        shock_arrow=False,
        is_removed=False,
        is_limited=False,
        notes=f"{builder.CONFIRMED_CHALLENGE_NOTE_MARKER} fixture",
    )
    source = builder.ddrworld_source_from_html(
        '<table class="table-ui"><tbody>'
        + _ddrworld_row(
            "SUPPLEMENT SONG",
            "Artist",
            (2, None, None, None, None),
            (None, None, None, None),
        )
        + "</tbody></table>"
    )

    charts, report = builder.merge_ddrworld_chart_data(
        (song,),
        (challenge,),
        source,
    )

    assert len(charts) == 2
    assert report["counts"]["official_only"] == 1
    assert report["counts"]["supplement_only"] == 1
    supplement = next(
        row for row in report["rows"] if row["status"] == "supplement_only"
    )
    assert supplement["chart_id"] == challenge.chart_id
    assert supplement["baseline_level"] == 16


def test_ddrworld_gp_candidate_without_master_match_is_blocking() -> None:
    source = builder.ddrworld_source_from_html(
        '<table class="table-ui"><tbody>'
        + _ddrworld_row(
            "GP CANDIDATE",
            "Official Artist",
            (3, None, None, None, None),
            (4, None, None, None),
        )
        + "</tbody></table>"
    )
    availability = (
        builder.OfficialSongAvailability(
            title="GP CANDIDATE",
            artist="Official Artist",
            free_play_available=False,
            grand_prix_play_available=True,
        ),
    )

    charts, report = builder.merge_ddrworld_chart_data(
        (),
        (),
        source,
        official_availability_entries=availability,
    )

    assert charts == ()
    assert report["counts"]["unmatchable_gp_candidate"] == 2
    assert report["counts"]["unmatchable_gp_candidate_song_count"] == 1
    assert {row["reason"] for row in report["rows"]} == {
        "gp_candidate_not_found_in_master"
    }


def test_inspect_rejects_blocking_ddrworld_gp_candidate(tmp_path: Path) -> None:
    base = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/wiki",
        fetched_at="2026-08-31T00:00:00+00:00",
    )
    source = builder.ddrworld_source_from_html(
        '<table class="table-ui"><tbody>'
        + _ddrworld_row(
            "GP CANDIDATE",
            "Official Artist",
            (3, None, None, None, None),
            (None, None, None, None),
        )
        + "</tbody></table>",
        fetched_at="2026-08-31T00:00:01+00:00",
    )
    charts, report = builder.merge_ddrworld_chart_data(
        base.songs,
        base.charts,
        source,
        official_availability_entries=(
            builder.OfficialSongAvailability(
                title="GP CANDIDATE",
                artist="Official Artist",
                free_play_available=False,
                grand_prix_play_available=True,
            ),
        ),
    )
    build = replace(
        base,
        charts=charts,
        ddrworld_snapshot=source,
        ddrworld_merge_report=report,
    )
    output_path = tmp_path / "blocking.sqlite"
    builder.write_master_database(output_path, build, master_version="blocking-v1")

    try:
        master_inspect.inspect_master_database(output_path)
    except ValueError as exc:
        assert "blocking GP candidates" in str(exc)
        assert "unmatchable_gp_candidate=1" in str(exc)
    else:
        raise AssertionError("blocking DDR WORLD GP candidate should fail inspection")


def test_inspect_rejects_ambiguous_ddrworld_gp_candidate(tmp_path: Path) -> None:
    base = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/wiki",
        fetched_at="2026-08-31T00:00:00+00:00",
    )
    duplicate_songs = tuple(
        builder.MasterSong(
            song_id=builder.stable_id("song", "AMBIGUOUS GP", artist),
            title="AMBIGUOUS GP",
            artist=artist,
            version="fixture",
            source_version="fixture",
            bpm="100",
            category="fixture",
            movie_stage="",
            availability="",
            notes="",
            grand_prix_play_available=True,
        )
        for artist in ("Artist A", "Artist B")
    )
    songs = base.songs + duplicate_songs
    source = builder.ddrworld_source_from_html(
        '<table class="table-ui"><tbody>'
        + _ddrworld_row(
            "AMBIGUOUS GP",
            "Other Artist",
            (3, None, None, None, None),
            (None, None, None, None),
        )
        + "</tbody></table>",
        fetched_at="2026-08-31T00:00:01+00:00",
    )
    charts, report = builder.merge_ddrworld_chart_data(
        songs,
        base.charts,
        source,
    )
    build = replace(
        base,
        songs=songs,
        charts=charts,
        ddrworld_snapshot=source,
        ddrworld_merge_report=report,
    )
    output_path = tmp_path / "ambiguous.sqlite"
    builder.write_master_database(output_path, build, master_version="ambiguous-v1")

    try:
        master_inspect.inspect_master_database(output_path)
    except ValueError as exc:
        assert "blocking GP candidates" in str(exc)
        assert "ambiguous_gp_candidate=1" in str(exc)
    else:
        raise AssertionError("ambiguous DDR WORLD GP candidate should fail inspection")


def confirmed_challenge_fixture_build() -> builder.MasterBuild:
    songs = tuple(
        builder.MasterSong(
            song_id=builder.stable_id("song", title, f"Artist for {title}"),
            title=title,
            artist=f"Artist for {title}",
            version="fixture",
            source_version="fixture",
            bpm="100",
            category="fixture",
            movie_stage="",
            availability="GP pack",
            notes="",
            grand_prix_play_available=True,
        )
        for title in EXPECTED_CONFIRMED_CHALLENGE_LEVELS
    )
    control_song = builder.MasterSong(
        song_id=builder.stable_id("song", "CONTROL SONG", "Control Artist"),
        title="CONTROL SONG",
        artist="Control Artist",
        version="fixture",
        source_version="fixture",
        bpm="120",
        category="fixture",
        movie_stage="",
        availability="",
        notes="",
    )
    control_chart = builder.MasterChart(
        chart_id=builder.stable_id(
            "chart", control_song.song_id, "SINGLE", "CHALLENGE"
        ),
        song_id=control_song.song_id,
        play_style="SINGLE",
        difficulty="CHALLENGE",
        level=12,
        raw_level="12",
        shock_arrow=False,
        is_removed=False,
        is_limited=False,
        notes="control",
    )
    all_songs = songs + (control_song,)
    charts, supplements = builder.apply_confirmed_challenge_supplements(
        all_songs,
        (control_chart,),
    )
    return builder.MasterBuild(
        songs=all_songs,
        charts=charts,
        snapshot=builder.SourceSnapshot(
            source_url="https://example.test/source",
            fetched_at="2026-08-09T00:00:00+00:00",
            content_hash="a" * 64,
            parser_version=builder.PARSER_VERSION,
            html_content="fixture",
        ),
        confirmed_challenge_supplements=supplements,
    )


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


def test_parse_master_html_uses_official_empty_artist_and_preserves_remix_title() -> None:
    build = builder.parse_master_html(
        NEW_SONG_FIXTURE_HTML,
        source_url="https://example.test/source",
        new_song_html=NEW_SONG_FIXTURE_HTML,
        new_song_source_url="https://example.test/new-songs",
        official_html=OFFICIAL_CANONICAL_FIXTURE_HTML,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )

    remix = next(song for song in build.songs if song.title.startswith("Party Lights"))
    licensed = next(song for song in build.songs if song.title == "LICENSED EMPTY ARTIST")

    assert remix.title == "Party Lights (Tommie Sunshine's Brooklyn Fire Remix)"
    assert remix.artist == "Tommie Sunshine"
    assert licensed.artist == ""
    assert licensed.grand_prix_play_available
    assert any(alias.alias_title.endswith("Remix)") for alias in build.song_aliases)
    assert any(alias.alias_artist == "Copyright Artist" for alias in build.song_aliases)


def test_parse_master_html_keeps_all_official_gp_rows_and_normalizes_ellipsis(
    tmp_path: Path,
) -> None:
    wiki_html = FIXTURE_HTML.replace(
        "<td>MAKE IT BETTER</td><td>mitsu-O!</td>",
        "<td>Leaving･･･</td><td>seiya-murai meets “eimy”</td>",
    )
    build = builder.parse_master_html(
        wiki_html,
        source_url="https://example.test/source",
        official_html=OFFICIAL_GP_ONLY_FIXTURE_HTML,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )

    leaving = next(song for song in build.songs if song.title == "Leaving…")
    assert leaving.artist == "seiya-murai meets “eimy”"
    assert leaving.grand_prix_play_available
    assert leaving.official_availability_match == "title_artist"
    assert any(alias.alias_title == "Leaving･･･" for alias in build.song_aliases)

    official_only_titles = {
        "Din Don Dan (にじさんじダンス部 ver.)",
        "打打打打打打打打打打 (にじさんじダンス部 ver.)",
        "創聖のアクエリオン",
    }
    official_only = [song for song in build.songs if song.title in official_only_titles]
    assert {song.title for song in official_only} == official_only_titles
    assert all(song.official_availability_match == "official_only" for song in official_only)
    assert all(song.grand_prix_play_available for song in official_only)
    assert next(song for song in official_only if song.title == "創聖のアクエリオン").artist == ""
    official_only_by_title = {song.title: song for song in official_only}
    assert all(
        chart.song_id != official_only_by_title["創聖のアクエリオン"].song_id
        for chart in build.charts
    )
    for title in (
        "Din Don Dan (にじさんじダンス部 ver.)",
        "打打打打打打打打打打 (にじさんじダンス部 ver.)",
    ):
        charts = [
            chart
            for chart in build.charts
            if chart.song_id == official_only_by_title[title].song_id
        ]
        assert {(chart.play_style, chart.difficulty, chart.level) for chart in charts} == {
            ("SINGLE", "CHALLENGE", 16),
            ("DOUBLE", "CHALLENGE", 16),
        }

    output_path = tmp_path / "ddrgp-master.sqlite"
    builder.write_master_database(output_path, build, master_version="fixture-v1")
    with sqlite3.connect(output_path) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
    assert metadata["grand_prix_play_available_song_count"] == "4"
    assert metadata["official_availability_matched_song_count"] == "4"


def test_parse_master_html_merges_new_song_levels_and_records_snapshot() -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        new_song_html=NEW_SONG_FIXTURE_HTML,
        new_song_source_url="https://example.test/new-songs",
        fetched_at="2026-07-04T00:00:00+00:00",
    )

    new_song = next(song for song in build.songs if song.title == "NEW ONLY")
    charts = [chart for chart in build.charts if chart.song_id == new_song.song_id]
    assert len(charts) == 5
    assert {chart.level for chart in charts} == {5, 9, 13}
    assert build.new_song_snapshot is not None
    assert build.new_song_snapshot.source_url == "https://example.test/new-songs"


def test_confirmed_challenge_supplement_generates_expected_68_charts(
    tmp_path: Path,
) -> None:
    build = confirmed_challenge_fixture_build()
    output_path = tmp_path / "ddrgp-master.sqlite"

    assert len(build.confirmed_challenge_supplements) == 68
    assert len({row.chart_id for row in build.confirmed_challenge_supplements}) == 68
    builder.write_master_database(output_path, build)

    with sqlite3.connect(output_path) as connection:
        rows = connection.execute(
            """
            SELECT s.title, c.play_style, c.level, c.chart_id, c.song_id, c.notes
            FROM charts c
            JOIN songs s ON s.song_id = c.song_id
            WHERE c.notes LIKE '%confirmed CHALLENGE supplement;%'
            ORDER BY s.title, c.play_style
            """
        ).fetchall()
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        control_rows = connection.execute(
            """
            SELECT c.play_style, c.difficulty, c.level, c.raw_level, c.notes
            FROM charts c
            JOIN songs s ON s.song_id = c.song_id
            WHERE s.title = 'CONTROL SONG'
            """
        ).fetchall()

    actual_levels: dict[str, dict[str, int]] = {}
    for title, play_style, level, chart_id, song_id, notes in rows:
        actual_levels.setdefault(title, {})[play_style] = level
        assert chart_id == builder.stable_id(
            "chart", song_id, play_style, "CHALLENGE"
        )
        assert "source_url=" in notes
        assert "acquired_on=" in notes
    assert actual_levels == {
        title: {"SINGLE": levels[0], "DOUBLE": levels[1]}
        for title, levels in EXPECTED_CONFIRMED_CHALLENGE_LEVELS.items()
    }
    assert control_rows == [("SINGLE", "CHALLENGE", 12, "12", "control")]

    manifest = json.loads(metadata["confirmed_challenge_supplement_json"])
    assert metadata["confirmed_challenge_chart_count"] == "68"
    assert len(manifest) == 68
    assert sum(row["acquired_on"] == "2026-07-25" for row in manifest) == 50
    assert sum(row["acquired_on"] == "2026-08-09" for row in manifest) == 18
    assert metadata["confirmed_challenge_supplement_hash"] == (
        builder.confirmed_challenge_supplements_hash(
            build.confirmed_challenge_supplements
        )
    )

    summary = master_inspect.inspect_master_database(output_path)
    assert summary["confirmed_challenge_chart_count"] == 68
    assert summary["confirmed_challenge_supplement_hash"] == metadata[
        "confirmed_challenge_supplement_hash"
    ]


def test_confirmed_challenge_supplement_resolves_representative_song_chart_ids() -> None:
    build = confirmed_challenge_fixture_build()
    charts_by_song_and_style = {
        (supplement.title, supplement.play_style): supplement
        for supplement in build.confirmed_challenge_supplements
    }

    for title, expected_level in (("Ace out", 14), ("和風インザ洋風", 17)):
        song = next(song for song in build.songs if song.title == title)
        resolved = [
            charts_by_song_and_style[(title, play_style)]
            for play_style in ("SINGLE", "DOUBLE")
        ]
        assert {chart.level for chart in resolved} == {expected_level}
        assert all(chart.song_id == song.song_id for chart in resolved)
        assert all(
            chart.chart_id
            == builder.stable_id(
                "chart", song.song_id, chart.play_style, "CHALLENGE"
            )
            for chart in resolved
        )

    wiki_titles = {
        "7 Colors",
        "Harmonia",
        "In The Breeze",
        "Superior MAXXX",
        "Touch My Body",
        "True Blue",
        "クリムゾンゲイト",
        "パ→ピ→プ→Yeah!",
        "和風インザ洋風",
    }
    wiki_rows = [
        row
        for row in build.confirmed_challenge_supplements
        if row.title in wiki_titles
    ]
    assert len(wiki_rows) == 18
    assert all(row.source_url == builder.BEMANIWIKI_PACK_SOURCE_URL for row in wiki_rows)


def test_confirmed_challenge_supplement_rejects_level_conflict() -> None:
    song = builder.MasterSong(
        song_id=builder.stable_id("song", "Ace out", "Artist"),
        title="Ace out",
        artist="Artist",
        version="fixture",
        source_version="fixture",
        bpm="100",
        category="fixture",
        movie_stage="",
        availability="",
        notes="",
    )
    conflicting_chart = builder.MasterChart(
        chart_id=builder.stable_id("chart", song.song_id, "SINGLE", "CHALLENGE"),
        song_id=song.song_id,
        play_style="SINGLE",
        difficulty="CHALLENGE",
        level=15,
        raw_level="15",
        shock_arrow=False,
        is_removed=False,
        is_limited=False,
        notes="",
    )

    try:
        builder.apply_confirmed_challenge_supplements((song,), (conflicting_chart,))
    except ValueError as exc:
        assert "conflicts with source chart" in str(exc)
    else:
        raise AssertionError("conflicting confirmed CHALLENGE level should be rejected")


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


def test_write_master_database_records_new_song_snapshot_and_inspects_it(
    tmp_path: Path,
) -> None:
    build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        new_song_html=NEW_SONG_FIXTURE_HTML,
        new_song_source_url="https://example.test/new-songs",
        official_html=OFFICIAL_FIXTURE_HTML,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    output_path = tmp_path / "ddrgp-master.sqlite"
    builder.write_master_database(output_path, build, master_version="fixture-v2")

    with sqlite3.connect(output_path) as connection:
        metadata = dict(connection.execute("SELECT key, value FROM master_metadata"))
        assert metadata["new_song_source_url"] == "https://example.test/new-songs"
        assert metadata["new_song_source_hash"] == build.new_song_snapshot.content_hash
        assert connection.execute("SELECT COUNT(*) FROM source_snapshots").fetchone()[0] == 3

    summary = master_inspect.inspect_master_database(output_path)
    assert summary["snapshot_count"] == 3
    assert summary["new_song_source_url"] == "https://example.test/new-songs"
    assert summary["new_song_source_hash"] == build.new_song_snapshot.content_hash


def test_auto_master_version_changes_when_only_official_snapshot_changes(
    tmp_path: Path,
) -> None:
    changed_official_html = OFFICIAL_FIXTURE_HTML.replace(
        "<td>MAKE IT BETTER</td><td>mitsu-O!</td><td>〇　※１</td><td>〇</td>",
        "<td>MAKE IT BETTER</td><td>mitsu-O!</td><td>〇　※１</td><td></td>",
    )
    original_build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        new_song_html=NEW_SONG_FIXTURE_HTML,
        new_song_source_url="https://example.test/new-songs",
        official_html=OFFICIAL_FIXTURE_HTML,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    changed_build = builder.parse_master_html(
        FIXTURE_HTML,
        source_url="https://example.test/source",
        new_song_html=NEW_SONG_FIXTURE_HTML,
        new_song_source_url="https://example.test/new-songs",
        official_html=changed_official_html,
        official_source_url="https://example.test/official",
        fetched_at="2026-07-04T00:00:00+00:00",
    )
    original_path = tmp_path / "original.sqlite"
    changed_path = tmp_path / "changed.sqlite"

    builder.write_master_database(original_path, original_build)
    builder.write_master_database(changed_path, changed_build)

    with sqlite3.connect(original_path) as original_connection:
        original_metadata = dict(
            original_connection.execute("SELECT key, value FROM master_metadata")
        )
    with sqlite3.connect(changed_path) as changed_connection:
        changed_metadata = dict(
            changed_connection.execute("SELECT key, value FROM master_metadata")
        )

    assert original_metadata["official_source_hash"] != changed_metadata["official_source_hash"]
    assert original_metadata["master_version"] != changed_metadata["master_version"]


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
