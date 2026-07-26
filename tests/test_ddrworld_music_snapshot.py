from __future__ import annotations

import json
from datetime import UTC, datetime
from pathlib import Path

import pytest

import tools.ddrworld_music_snapshot.collector as collector_module
from tools.ddrworld_music_snapshot.cli import build_parser, config_from_args, main
from tools.ddrworld_music_snapshot.collector import (
    FetchResult,
    SnapshotCancelled,
    SnapshotCollector,
    SnapshotConfig,
    SnapshotError,
    SnapshotProgress,
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
EMPTY_PAGE = b"<!doctype html><html><body><table id=\"data_tbl\"></table></body></html>"


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
        "stored_jacket_count": 1,
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


def test_fixed_output_reports_phases_and_publishes_required_root_files(tmp_path: Path) -> None:
    fetcher = FakeFetcher(
        [
            response(PAGE, "text/html"),
            response(EMPTY_PAGE, "text/html"),
            response(PNG, "image/png"),
            response(PNG, "image/png"),
        ]
    )
    fixed_root = tmp_path / "data" / "ddrworld_music_snapshot"
    incomplete_root = tmp_path / "data" / "ddrworld_music_snapshot.incomplete"
    progress: list[SnapshotProgress] = []

    output = SnapshotCollector(
        SnapshotConfig(
            snapshot_id="internal-run-id",
            output_root=Path("data/ddrworld_music_snapshot"),
            incomplete_root=Path("data/ddrworld_music_snapshot.incomplete"),
            fixed_output=True,
            repository_root=tmp_path,
            page_count=1,
        ),
        fetcher=fetcher,
        now=lambda: NOW,
    ).collect(progress=progress.append)

    assert output == fixed_root
    assert not incomplete_root.exists()
    assert all((output / name).exists() for name in [
        "manifest.json",
        "pages",
        "songs.jsonl",
        "jackets",
        "summary.json",
    ])
    assert progress[:2] == [
        SnapshotProgress("pages", 0, 1),
        SnapshotProgress("pages", 1, 1),
    ]
    assert progress[2] == SnapshotProgress("jackets", 0, 2)
    assert progress[-1] == SnapshotProgress("jackets", 2, 2)
    summary = json.loads((output / "summary.json").read_text(encoding="utf-8"))
    assert summary["snapshot_id"] == "internal-run-id"
    assert summary["stored_jacket_count"] == 1


def test_fixed_output_keeps_previous_snapshot_on_failure_and_discards_stale_incomplete(
    tmp_path: Path,
) -> None:
    fixed_root = tmp_path / "data" / "ddrworld_music_snapshot"
    incomplete_root = tmp_path / "data" / "ddrworld_music_snapshot.incomplete"
    success_config = SnapshotConfig(
        snapshot_id="first",
        output_root=fixed_root,
        incomplete_root=incomplete_root,
        fixed_output=True,
        page_count=1,
    )
    SnapshotCollector(
        success_config,
        fetcher=FakeFetcher(
            [
                response(PAGE, "text/html"),
                response(EMPTY_PAGE, "text/html"),
                response(PNG, "image/png"),
                response(PNG, "image/png"),
            ]
        ),
        now=lambda: NOW,
    ).collect()
    previous_summary = (fixed_root / "summary.json").read_bytes()
    incomplete_root.mkdir(parents=True)
    (incomplete_root / "stale.txt").write_text("discard me", encoding="utf-8")

    with pytest.raises(SnapshotError, match="snapshot is incomplete"):
        SnapshotCollector(
            SnapshotConfig(
                snapshot_id="second",
                output_root=fixed_root,
                incomplete_root=incomplete_root,
                fixed_output=True,
                page_count=1,
            ),
            fetcher=FakeFetcher(
                [
                    response(PAGE, "text/html"),
                    response(EMPTY_PAGE, "text/html"),
                    response(b"unavailable", "text/plain", error="HTTP 503"),
                    response(b"unavailable", "text/plain", error="HTTP 503"),
                ]
            ),
            now=lambda: NOW,
        ).collect()

    assert (fixed_root / "summary.json").read_bytes() == previous_summary
    assert incomplete_root.is_dir()
    assert not (incomplete_root / "stale.txt").exists()
    assert json.loads((incomplete_root / "manifest.json").read_text(encoding="utf-8"))[
        "status"
    ] == "incomplete"


def test_fixed_output_cancellation_keeps_previous_snapshot_and_stops_before_next_request(
    tmp_path: Path,
) -> None:
    fixed_root = tmp_path / "data" / "ddrworld_music_snapshot"
    incomplete_root = tmp_path / "data" / "ddrworld_music_snapshot.incomplete"
    SnapshotCollector(
        SnapshotConfig(
            snapshot_id="first",
            output_root=fixed_root,
            incomplete_root=incomplete_root,
            fixed_output=True,
            page_count=1,
        ),
        fetcher=FakeFetcher(
            [
                response(PAGE, "text/html"),
                response(EMPTY_PAGE, "text/html"),
                response(PNG, "image/png"),
                response(PNG, "image/png"),
            ]
        ),
        now=lambda: NOW,
    ).collect()
    previous_summary = (fixed_root / "summary.json").read_bytes()
    fetcher = FakeFetcher([response(PAGE, "text/html")])

    with pytest.raises(SnapshotCancelled):
        SnapshotCollector(
            SnapshotConfig(
                snapshot_id="cancelled",
                output_root=fixed_root,
                incomplete_root=incomplete_root,
                fixed_output=True,
                page_count=1,
            ),
            fetcher=fetcher,
            now=lambda: NOW,
            cancel_check=lambda: len(fetcher.urls) >= 1,
        ).collect()

    assert len(fetcher.urls) == 1
    assert (fixed_root / "summary.json").read_bytes() == previous_summary
    assert json.loads((incomplete_root / "manifest.json").read_text(encoding="utf-8"))[
        "status"
    ] == "cancelled"


@pytest.mark.parametrize(
    "incomplete_relative_path",
    [
        Path("data"),
        Path("data/ddrworld_music_snapshot/staging"),
    ],
)
def test_fixed_output_rejects_overlapping_paths_before_cleanup(
    tmp_path: Path,
    incomplete_relative_path: Path,
) -> None:
    data_root = tmp_path / "data"
    data_root.mkdir()
    sentinel = data_root / "must-survive.txt"
    sentinel.write_text("keep", encoding="utf-8")
    fetcher = FakeFetcher([])

    with pytest.raises(SnapshotError, match="separate, non-overlapping"):
        SnapshotCollector(
            SnapshotConfig(
                snapshot_id="overlap",
                output_root=Path("data/ddrworld_music_snapshot"),
                incomplete_root=incomplete_relative_path,
                fixed_output=True,
                repository_root=tmp_path,
                page_count=1,
            ),
            fetcher=fetcher,
        ).collect()

    assert fetcher.urls == []
    assert sentinel.read_text(encoding="utf-8") == "keep"


def test_fixed_output_accepts_legacy_record_count_for_shared_hash_path(tmp_path: Path) -> None:
    fixed_root = tmp_path / "data" / "ddrworld_music_snapshot"
    incomplete_root = tmp_path / "data" / "ddrworld_music_snapshot.incomplete"
    SnapshotCollector(
        SnapshotConfig(
            snapshot_id="legacy-source",
            output_root=fixed_root,
            incomplete_root=incomplete_root,
            fixed_output=True,
            page_count=1,
        ),
        fetcher=FakeFetcher(
            [
                response(PAGE, "text/html"),
                response(EMPTY_PAGE, "text/html"),
                response(PNG, "image/png"),
                response(PNG, "image/png"),
            ]
        ),
        now=lambda: NOW,
    ).collect()
    summary_path = fixed_root / "summary.json"
    summary = json.loads(summary_path.read_text(encoding="utf-8"))
    summary["stored_jacket_count"] = summary["image_request_count"]
    summary_path.write_text(json.dumps(summary), encoding="utf-8")

    assert SnapshotCollector._is_complete_snapshot(fixed_root)


def test_fixed_output_rejects_an_additional_page_and_preserves_previous_snapshot(
    tmp_path: Path,
) -> None:
    fixed_root = tmp_path / "data" / "ddrworld_music_snapshot"
    incomplete_root = tmp_path / "data" / "ddrworld_music_snapshot.incomplete"
    config = SnapshotConfig(
        snapshot_id="first",
        output_root=fixed_root,
        incomplete_root=incomplete_root,
        fixed_output=True,
        page_count=1,
    )
    SnapshotCollector(
        config,
        fetcher=FakeFetcher(
            [
                response(PAGE, "text/html"),
                response(EMPTY_PAGE, "text/html"),
                response(PNG, "image/png"),
                response(PNG, "image/png"),
            ]
        ),
        now=lambda: NOW,
    ).collect()
    previous_summary = (fixed_root / "summary.json").read_bytes()

    with pytest.raises(SnapshotError, match="snapshot is incomplete"):
        SnapshotCollector(
            SnapshotConfig(
                snapshot_id="expanded",
                output_root=fixed_root,
                incomplete_root=incomplete_root,
                fixed_output=True,
                page_count=1,
            ),
            fetcher=FakeFetcher([response(PAGE, "text/html"), response(PAGE, "text/html")]),
            now=lambda: NOW,
        ).collect()

    assert (fixed_root / "summary.json").read_bytes() == previous_summary
    failures = json.loads(
        (incomplete_root / "manifest.json").read_text(encoding="utf-8")
    )["failures"]
    assert failures[0]["resource"] == "pagination"


def test_fixed_output_rejects_shorter_catalog_and_keeps_incomplete_diagnostic(
    tmp_path: Path,
) -> None:
    fixed_root = tmp_path / "data" / "ddrworld_music_snapshot"
    incomplete_root = tmp_path / "data" / "ddrworld_music_snapshot.incomplete"
    with pytest.raises(SnapshotError, match="snapshot is incomplete"):
        SnapshotCollector(
            SnapshotConfig(
                snapshot_id="shorter",
                output_root=fixed_root,
                incomplete_root=incomplete_root,
                fixed_output=True,
                page_count=1,
            ),
            fetcher=FakeFetcher([response(EMPTY_PAGE, "text/html")]),
            now=lambda: NOW,
        ).collect()

    failures = json.loads(
        (incomplete_root / "manifest.json").read_text(encoding="utf-8")
    )["failures"]
    assert failures[0]["resource"] == "page"


def test_fixed_output_rejects_pagination_probe_failure(tmp_path: Path) -> None:
    fixed_root = tmp_path / "data" / "ddrworld_music_snapshot"
    incomplete_root = tmp_path / "data" / "ddrworld_music_snapshot.incomplete"

    with pytest.raises(SnapshotError, match="snapshot is incomplete"):
        SnapshotCollector(
            SnapshotConfig(
                snapshot_id="pagination-error",
                output_root=fixed_root,
                incomplete_root=incomplete_root,
                fixed_output=True,
                page_count=1,
            ),
            fetcher=FakeFetcher(
                [
                    response(PAGE, "text/html"),
                    response(b"service unavailable", "text/plain", error="HTTP 503"),
                ]
            ),
            now=lambda: NOW,
        ).collect()

    failures = json.loads(
        (incomplete_root / "manifest.json").read_text(encoding="utf-8")
    )["failures"]
    assert failures[0]["resource"] == "pagination"


def test_fixed_publish_success_survives_backup_cleanup_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    final_root = tmp_path / "data" / "ddrworld_music_snapshot"
    incomplete_root = tmp_path / "data" / "ddrworld_music_snapshot.incomplete"
    final_root.mkdir(parents=True)
    (final_root / "old.txt").write_text("old", encoding="utf-8")
    incomplete_root.mkdir(parents=True)
    (incomplete_root / "new.txt").write_text("new", encoding="utf-8")
    collector = SnapshotCollector(
        SnapshotConfig(
            snapshot_id="publish",
            output_root=final_root,
            incomplete_root=incomplete_root,
            fixed_output=True,
        ),
        fetcher=FakeFetcher([]),
    )
    original_rmtree = collector_module.shutil.rmtree

    def fail_previous_backup(path: Path, *args: object, **kwargs: object) -> None:
        if path.name.startswith(".ddrworld_music_snapshot.previous-"):
            raise OSError("previous snapshot is locked")
        original_rmtree(path, *args, **kwargs)

    monkeypatch.setattr(collector_module.shutil, "rmtree", fail_previous_backup)
    collector._publish_fixed_snapshot(final_root, incomplete_root)

    assert (final_root / "new.txt").read_text(encoding="utf-8") == "new"
    assert list(final_root.parent.glob(".ddrworld_music_snapshot.previous-*"))


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


def test_fixed_fetch_does_not_require_a_user_snapshot_id() -> None:
    args = build_parser().parse_args(["fetch", "--fixed-output"])

    config = config_from_args(args)

    assert config.fixed_output
    assert config.snapshot_id.endswith("Z")
    config.validate()


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


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("filter_value", 6),
        ("filter_type", 1),
        ("play_mode", 1),
    ],
)
def test_source_query_cannot_be_changed(field: str, value: int) -> None:
    config_values = {field: value}
    fetcher = FakeFetcher([])

    with pytest.raises(SnapshotError, match="source query is fixed"):
        SnapshotCollector(
            SnapshotConfig(snapshot_id="wrong-query", **config_values), fetcher=fetcher
        )

    assert fetcher.urls == []


def test_delay_cannot_be_reduced_below_safe_default() -> None:
    with pytest.raises(SnapshotError, match="at least 2 seconds"):
        SnapshotConfig(snapshot_id="too-fast", delay_seconds=1).validate()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        (field, value)
        for field in (
            "delay_seconds",
            "connect_timeout_seconds",
            "read_timeout_seconds",
        )
        for value in (float("nan"), float("inf"), float("-inf"))
    ],
)
def test_http_timing_values_must_be_finite(field: str, value: float) -> None:
    config_values = {field: value}
    fetcher = FakeFetcher([])

    with pytest.raises(SnapshotError, match="must be finite"):
        SnapshotCollector(
            SnapshotConfig(snapshot_id="non-finite", **config_values), fetcher=fetcher
        )

    assert fetcher.urls == []


def test_fetch_rejects_nan_delay_before_network(
    capsys: pytest.CaptureFixture[str],
) -> None:
    exit_code = main(
        [
            "fetch",
            "--allow-network",
            "--snapshot-id",
            "nan-delay",
            "--delay-seconds",
            "nan",
        ]
    )

    assert exit_code == 2
    assert "must be finite" in capsys.readouterr().err
