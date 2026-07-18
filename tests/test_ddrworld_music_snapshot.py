from __future__ import annotations

import json
from datetime import UTC, datetime
from pathlib import Path

import pytest

from tools.ddrworld_music_snapshot.cli import main
from tools.ddrworld_music_snapshot.collector import (
    FetchResult,
    SnapshotCollector,
    SnapshotConfig,
    SnapshotError,
    build_page_url,
    detect_image_type,
    parse_page,
)

NOW = datetime(2026, 7, 18, 3, 4, 5, tzinfo=UTC)
PNG = b"\x89PNG\r\n\x1a\nsynthetic-png-data"
PAGE = """<!doctype html><html><body><table id="data_tbl">
<tr class="data">
  <td class="jk"><img src="/game/ddr/ddrworld/images/binary_jk.html?img=one&amp;kind=2"></td>
  <td class="music_tit">Song One</td><td class="artist_nam">Artist One</td>
</tr>
<tr class="data">
  <td class="jk"><img src="/game/ddr/ddrworld/images/binary_jk.html?img=two&amp;kind=2"></td>
  <td class="music_tit">曲　二</td><td class="artist_nam">作者　二</td>
</tr>
</table></body></html>""".encode()


class FakeFetcher:
    def __init__(self, responses: list[FetchResult]) -> None:
        self.responses = iter(responses)
        self.urls: list[str] = []

    def get(self, url: str, *, accept: str) -> FetchResult:
        del accept
        self.urls.append(url)
        response = next(self.responses)
        return FetchResult(
            url=url,
            fetched_at=response.fetched_at,
            status_code=response.status_code,
            content_type=response.content_type,
            content=response.content,
            error=response.error,
        )


def response(content: bytes, content_type: str, *, error: str | None = None) -> FetchResult:
    return FetchResult(
        url="unused",
        fetched_at="2026-07-18T03:04:05Z",
        status_code=200 if error is None else 503,
        content_type=content_type,
        content=content,
        error=error,
    )


def test_parse_page_extracts_official_fields_and_absolute_jacket_urls() -> None:
    page_url = build_page_url(SnapshotConfig(snapshot_id="test"), 0)

    songs = parse_page(PAGE, page_offset=0, page_url=page_url)

    assert [(song.title, song.artist) for song in songs] == [
        ("Song One", "Artist One"),
        ("曲　二", "作者　二"),
    ]
    assert songs[0].jacket_source_url == (
        "https://p.eagate.573.jp/game/ddr/ddrworld/images/"
        "binary_jk.html?img=one&kind=2"
    )
    assert songs[1].source_page == 0
    assert songs[1].page_position == 1


@pytest.mark.parametrize(
    "row",
    [
        b'<td class="artist_nam">Artist</td><td class="jk"><img src="/j.png"></td>',
        b'<td class="music_tit">Title</td><td class="jk"><img src="/j.png"></td>',
        b'<td class="music_tit">Title</td><td class="artist_nam">Artist</td>',
    ],
)
def test_parse_page_rejects_missing_required_fields(row: bytes) -> None:
    html = b'<table id="data_tbl"><tr class="data">' + row + b"</tr></table>"

    with pytest.raises(SnapshotError, match="is missing"):
        parse_page(html, page_offset=3, page_url="https://example.test/page")


def test_parse_page_rejects_off_origin_jacket_url() -> None:
    html = b"""<table id="data_tbl"><tr class="data">
    <td class="music_tit">Title</td><td class="artist_nam">Artist</td>
    <td class="jk"><img src="https://example.test/jacket.png"></td>
    </tr></table>"""

    with pytest.raises(SnapshotError, match="off-origin jacket URL"):
        parse_page(
            html,
            page_offset=0,
            page_url="https://p.eagate.573.jp/game/ddr/ddrworld/music/index.html",
        )


def test_detect_image_type_uses_signature() -> None:
    assert detect_image_type(PNG) == ("png", "image/png")
    assert detect_image_type(b"not-an-image") is None


def test_collect_publishes_complete_snapshot_atomically(tmp_path: Path) -> None:
    fetcher = FakeFetcher(
        [
            response(PAGE, "text/html; charset=UTF-8"),
            response(PNG, "image/png"),
            response(PNG, "image/png"),
        ]
    )
    config = SnapshotConfig(snapshot_id="snapshot-1", output_root=tmp_path, page_count=1)

    output = SnapshotCollector(config, fetcher=fetcher, now=lambda: NOW).collect()

    assert output == tmp_path / "snapshot-1"
    assert not (tmp_path / "snapshot-1.incomplete").exists()
    assert (output / "pages/page-00.html").read_bytes() == PAGE
    jackets = list((output / "jackets").iterdir())
    assert len(jackets) == 1
    assert jackets[0].read_bytes() == PNG
    songs = [json.loads(line) for line in (output / "songs.jsonl").read_text().splitlines()]
    assert len(songs) == 2
    assert songs[0]["jacket_sha256"] == songs[1]["jacket_sha256"]
    summary = json.loads((output / "summary.json").read_text())
    assert summary == {
        "schema_version": "ddrworld-music-snapshot-summary-v1",
        "status": "complete",
        "snapshot_id": "snapshot-1",
        "request_count": 3,
        "page_request_count": 1,
        "image_request_count": 2,
        "song_count": 2,
        "unique_jacket_url_count": 2,
        "stored_jacket_count": 2,
        "failure_count": 0,
        "duplicate_image_hash_count": 1,
        "duplicate_image_hashes": [
            {
                "sha256": songs[0]["jacket_sha256"],
                "source_urls": [fetcher.urls[1], fetcher.urls[2]],
            }
        ],
    }


def test_collect_retains_failed_run_only_as_incomplete(tmp_path: Path) -> None:
    fetcher = FakeFetcher(
        [
            response(PAGE, "text/html"),
            response(b"service unavailable", "text/plain", error="HTTP 503"),
            response(PNG, "image/png"),
        ]
    )
    config = SnapshotConfig(snapshot_id="failed", output_root=tmp_path, page_count=1)

    with pytest.raises(SnapshotError, match="snapshot is incomplete"):
        SnapshotCollector(config, fetcher=fetcher, now=lambda: NOW).collect()

    assert not (tmp_path / "failed").exists()
    incomplete = tmp_path / "failed.incomplete"
    assert incomplete.is_dir()
    manifest = json.loads((incomplete / "manifest.json").read_text())
    assert manifest["status"] == "incomplete"
    assert manifest["failures"][0]["resource"] == "image"


def test_collect_refuses_existing_final_or_incomplete_output_before_fetch(tmp_path: Path) -> None:
    (tmp_path / "existing").mkdir()
    fetcher = FakeFetcher([])

    with pytest.raises(SnapshotError, match="refusing to overwrite"):
        SnapshotCollector(
            SnapshotConfig(snapshot_id="existing", output_root=tmp_path), fetcher=fetcher
        ).collect()

    assert fetcher.urls == []


def test_image_content_type_must_match_signature(tmp_path: Path) -> None:
    fetcher = FakeFetcher(
        [response(PAGE, "text/html"), response(PNG, "image/jpeg"), response(PNG, "image/png")]
    )

    with pytest.raises(SnapshotError, match="snapshot is incomplete"):
        SnapshotCollector(
            SnapshotConfig(snapshot_id="mismatch", output_root=tmp_path, page_count=1),
            fetcher=fetcher,
            now=lambda: NOW,
        ).collect()

    manifest = json.loads((tmp_path / "mismatch.incomplete/manifest.json").read_text())
    assert "content type/signature mismatch" in manifest["failures"][0]["error"]


def test_fetch_requires_explicit_network_opt_in(capsys: pytest.CaptureFixture[str]) -> None:
    exit_code = main(["fetch", "--snapshot-id", "no-network"])

    assert exit_code == 2
    assert "--allow-network" in capsys.readouterr().err


def test_plan_is_network_free_and_reports_upper_bound(capsys: pytest.CaptureFixture[str]) -> None:
    exit_code = main(
        ["plan", "--page-count", "26", "--estimated-songs", "1300", "--delay-seconds", "2"]
    )

    assert exit_code == 0
    output = capsys.readouterr().out
    assert "maximum requests: 1326" in output
    assert "minimum inter-request wait: 2650.0 seconds" in output
    assert "existing outputs: never overwritten" in output


def test_page_count_cannot_exceed_known_catalog_extent() -> None:
    with pytest.raises(SnapshotError, match="between 1 and 26"):
        SnapshotConfig(snapshot_id="too-many", page_count=27).validate()


def test_delay_cannot_be_reduced_below_safe_default() -> None:
    with pytest.raises(SnapshotError, match="at least 2 seconds"):
        SnapshotConfig(snapshot_id="too-fast", delay_seconds=1).validate()
