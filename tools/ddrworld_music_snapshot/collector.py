from __future__ import annotations

import hashlib
import json
import os
import re
import time
import uuid
from collections import defaultdict
from collections.abc import Callable, Iterator
from dataclasses import asdict, dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any
from urllib.parse import urlencode, urljoin, urlsplit

import requests
from bs4 import BeautifulSoup
from requests.adapters import HTTPAdapter

COLLECTOR_VERSION = "ddrworld-music-snapshot-v1"
SOURCE_ORIGIN = "https://p.eagate.573.jp"
SOURCE_PATH = "/game/ddr/ddrworld/music/index.html"
DEFAULT_FILTER = 7
DEFAULT_FILTER_TYPE = 0
DEFAULT_PLAY_MODE = 2
DEFAULT_PAGE_COUNT = 26
DEFAULT_DELAY_SECONDS = 2.0
DEFAULT_CONNECT_TIMEOUT_SECONDS = 10.0
DEFAULT_READ_TIMEOUT_SECONDS = 30.0
USER_AGENT = "ddrgp-scorelog-local-snapshot/1.0"
SNAPSHOT_ID_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,79}\Z")


class SnapshotError(RuntimeError):
    """Raised when a snapshot cannot be collected or published safely."""


@dataclass(frozen=True)
class SnapshotConfig:
    snapshot_id: str
    output_root: Path = Path("data/ddrworld_music_snapshot")
    page_count: int = DEFAULT_PAGE_COUNT
    delay_seconds: float = DEFAULT_DELAY_SECONDS
    connect_timeout_seconds: float = DEFAULT_CONNECT_TIMEOUT_SECONDS
    read_timeout_seconds: float = DEFAULT_READ_TIMEOUT_SECONDS
    filter_value: int = DEFAULT_FILTER
    filter_type: int = DEFAULT_FILTER_TYPE
    play_mode: int = DEFAULT_PLAY_MODE

    def validate(self) -> None:
        if not SNAPSHOT_ID_PATTERN.fullmatch(self.snapshot_id):
            raise SnapshotError(
                "snapshot ID must be 1-80 characters using letters, digits, dot, "
                "underscore, or hyphen"
            )
        if (
            self.filter_value != DEFAULT_FILTER
            or self.filter_type != DEFAULT_FILTER_TYPE
            or self.play_mode != DEFAULT_PLAY_MODE
        ):
            raise SnapshotError(
                "source query is fixed to filter=7, filtertype=0, and playmode=2"
            )
        if not 1 <= self.page_count <= DEFAULT_PAGE_COUNT:
            raise SnapshotError(f"page count must be between 1 and {DEFAULT_PAGE_COUNT}")
        if self.delay_seconds < DEFAULT_DELAY_SECONDS:
            raise SnapshotError(
                f"delay must be at least {DEFAULT_DELAY_SECONDS:g} seconds"
            )
        if self.connect_timeout_seconds <= 0 or self.read_timeout_seconds <= 0:
            raise SnapshotError("HTTP timeouts must be positive")


@dataclass(frozen=True)
class SongEntry:
    source_page: int
    page_position: int
    title: str
    artist: str
    jacket_source_url: str


@dataclass(frozen=True)
class FetchResult:
    url: str
    fetched_at: str
    status_code: int | None
    content_type: str | None
    content: bytes | None
    error: str | None


class SerialFetcher:
    """Single-threaded HTTP fetcher with a delay and no automatic retries."""

    def __init__(
        self,
        *,
        delay_seconds: float,
        timeout: tuple[float, float],
        session: requests.Session | None = None,
        sleep: Callable[[float], None] = time.sleep,
        now: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        self.delay_seconds = delay_seconds
        self.timeout = timeout
        self.session = session or requests.Session()
        self.sleep = sleep
        self.now = now
        self._last_request_finished_at: float | None = None
        adapter = HTTPAdapter(max_retries=0, pool_connections=1, pool_maxsize=1)
        self.session.mount("https://", adapter)
        self.session.headers.update({"User-Agent": USER_AGENT, "Accept-Encoding": "gzip, deflate"})

    def get(self, url: str, *, accept: str) -> FetchResult:
        if self._last_request_finished_at is not None:
            elapsed = time.monotonic() - self._last_request_finished_at
            if elapsed < self.delay_seconds:
                self.sleep(self.delay_seconds - elapsed)
        fetched_at = self.now().isoformat().replace("+00:00", "Z")
        try:
            response = self.session.get(
                url,
                headers={"Accept": accept},
                timeout=self.timeout,
                allow_redirects=False,
            )
            content_type = response.headers.get("Content-Type")
            content = response.content
            error = (
                None
                if 200 <= response.status_code < 300
                else f"HTTP {response.status_code}"
            )
            return FetchResult(
                url=url,
                fetched_at=fetched_at,
                status_code=response.status_code,
                content_type=content_type,
                content=content,
                error=error,
            )
        except requests.RequestException as exc:
            return FetchResult(
                url=url,
                fetched_at=fetched_at,
                status_code=None,
                content_type=None,
                content=None,
                error=f"{type(exc).__name__}: {exc}",
            )
        finally:
            self._last_request_finished_at = time.monotonic()


def build_page_url(config: SnapshotConfig, offset: int) -> str:
    query = urlencode(
        {
            "offset": offset,
            "filter": config.filter_value,
            "filtertype": config.filter_type,
            "playmode": config.play_mode,
        }
    )
    return f"{SOURCE_ORIGIN}{SOURCE_PATH}?{query}"


def parse_page(html: bytes, *, page_offset: int, page_url: str) -> list[SongEntry]:
    soup = BeautifulSoup(html, "html.parser")
    rows = soup.select("#data_tbl tr.data")
    if not rows:
        raise SnapshotError(f"page {page_offset} contains no music rows")

    songs: list[SongEntry] = []
    for position, row in enumerate(rows):
        title_cell = row.select_one("td.music_tit")
        artist_cell = row.select_one("td.artist_nam")
        jacket = row.select_one("td.jk img[src]")
        title = title_cell.get_text(" ", strip=True) if title_cell else ""
        artist = artist_cell.get_text(" ", strip=True) if artist_cell else ""
        jacket_src = jacket.get("src", "").strip() if jacket else ""
        missing = [
            name
            for name, value in (("title", title), ("artist", artist), ("jacket URL", jacket_src))
            if not value
        ]
        if missing:
            raise SnapshotError(
                f"page {page_offset} row {position} is missing {', '.join(missing)}"
            )
        jacket_url = urljoin(page_url, jacket_src)
        parsed_jacket_url = urlsplit(jacket_url)
        if (
            parsed_jacket_url.scheme != "https"
            or f"{parsed_jacket_url.scheme}://{parsed_jacket_url.netloc}" != SOURCE_ORIGIN
        ):
            raise SnapshotError(
                f"page {page_offset} row {position} has an off-origin jacket URL"
            )
        songs.append(
            SongEntry(
                source_page=page_offset,
                page_position=position,
                title=title,
                artist=artist,
                jacket_source_url=jacket_url,
            )
        )
    return songs


def detect_image_type(content: bytes) -> tuple[str, str] | None:
    if content.startswith(b"\x89PNG\r\n\x1a\n"):
        return "png", "image/png"
    if content.startswith(b"\xff\xd8\xff"):
        return "jpg", "image/jpeg"
    if content.startswith((b"GIF87a", b"GIF89a")):
        return "gif", "image/gif"
    if len(content) >= 12 and content[:4] == b"RIFF" and content[8:12] == b"WEBP":
        return "webp", "image/webp"
    return None


def media_type(value: str | None) -> str | None:
    return value.split(";", 1)[0].strip().lower() if value else None


def atomic_write_bytes(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if path.read_bytes() == content:
            return
        raise SnapshotError(f"refusing to overwrite existing file with different content: {path}")
    temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        temporary.rename(path)
    finally:
        temporary.unlink(missing_ok=True)


def atomic_write_json(path: Path, value: Any) -> None:
    content = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode()
    atomic_write_bytes(path, content)


def atomic_write_jsonl(path: Path, values: list[dict[str, Any]]) -> None:
    content = "".join(json.dumps(value, ensure_ascii=False) + "\n" for value in values).encode()
    atomic_write_bytes(path, content)


class SnapshotCollector:
    def __init__(
        self,
        config: SnapshotConfig,
        *,
        fetcher: SerialFetcher | None = None,
        now: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        config.validate()
        self.config = config
        self.fetcher = fetcher or SerialFetcher(
            delay_seconds=config.delay_seconds,
            timeout=(config.connect_timeout_seconds, config.read_timeout_seconds),
        )
        self.now = now

    def collect(self) -> Path:
        output_root = self.config.output_root.resolve()
        final_dir = output_root / self.config.snapshot_id
        incomplete_dir = output_root / f"{self.config.snapshot_id}.incomplete"
        if final_dir.exists() or incomplete_dir.exists():
            raise SnapshotError(
                "snapshot output already exists; refusing to overwrite: "
                f"{final_dir} or {incomplete_dir}"
            )
        output_root.mkdir(parents=True, exist_ok=True)
        incomplete_dir.mkdir()

        started_at = self.now().isoformat().replace("+00:00", "Z")
        page_records: list[dict[str, Any]] = []
        image_records: list[dict[str, Any]] = []
        songs: list[SongEntry] = []
        failures: list[dict[str, Any]] = []
        request_count = 0

        for offset in range(self.config.page_count):
            url = build_page_url(self.config, offset)
            result = self.fetcher.get(url, accept="text/html,application/xhtml+xml")
            request_count += 1
            record = self._page_record(result, offset)
            page_records.append(record)
            if record["error"] is not None:
                failures.append({"resource": "page", "offset": offset, "error": record["error"]})
                continue
            assert result.content is not None
            atomic_write_bytes(incomplete_dir / f"pages/page-{offset:02d}.html", result.content)
            try:
                songs.extend(parse_page(result.content, page_offset=offset, page_url=url))
            except SnapshotError as exc:
                failures.append({"resource": "page", "offset": offset, "error": str(exc)})

        for jacket_url in dict.fromkeys(song.jacket_source_url for song in songs):
            result = self.fetcher.get(jacket_url, accept="image/*")
            request_count += 1
            record = self._image_record(result)
            image_records.append(record)
            if record["error"] is not None:
                failures.append({"resource": "image", "url": jacket_url, "error": record["error"]})
                continue
            assert result.content is not None
            atomic_write_bytes(incomplete_dir / record["local_path"], result.content)

        images_by_url = {record["source_url"]: record for record in image_records}
        song_records = []
        for song in songs:
            image = images_by_url.get(song.jacket_source_url)
            song_records.append(
                {
                    **asdict(song),
                    "jacket_local_path": image.get("local_path") if image else None,
                    "jacket_content_type": image.get("content_type") if image else None,
                    "jacket_byte_size": image.get("byte_size") if image else None,
                    "jacket_sha256": image.get("sha256") if image else None,
                    "jacket_error": image.get("error") if image else "image was not requested",
                }
            )
        atomic_write_jsonl(incomplete_dir / "songs.jsonl", song_records)

        duplicate_hashes = self._duplicate_hashes(image_records)
        status = "complete" if not failures else "incomplete"
        completed_at = self.now().isoformat().replace("+00:00", "Z")
        manifest = {
            "schema_version": "ddrworld-music-snapshot-manifest-v1",
            "status": status,
            "snapshot_id": self.config.snapshot_id,
            "collector_version": COLLECTOR_VERSION,
            "source": {
                "origin": SOURCE_ORIGIN,
                "path": SOURCE_PATH,
                "filter": self.config.filter_value,
                "filter_type": self.config.filter_type,
                "play_mode": self.config.play_mode,
                "offsets": list(range(self.config.page_count)),
            },
            "request_policy": {
                "concurrency": 1,
                "delay_seconds": self.config.delay_seconds,
                "automatic_retries": 0,
                "connect_timeout_seconds": self.config.connect_timeout_seconds,
                "read_timeout_seconds": self.config.read_timeout_seconds,
                "user_agent": USER_AGENT,
            },
            "started_at": started_at,
            "completed_at": completed_at,
            "pages": page_records,
            "images": image_records,
            "failures": failures,
        }
        summary = {
            "schema_version": "ddrworld-music-snapshot-summary-v1",
            "status": status,
            "snapshot_id": self.config.snapshot_id,
            "request_count": request_count,
            "page_request_count": len(page_records),
            "image_request_count": len(image_records),
            "song_count": len(songs),
            "unique_jacket_url_count": len(image_records),
            "stored_jacket_count": sum(record["error"] is None for record in image_records),
            "failure_count": len(failures),
            "duplicate_image_hash_count": len(duplicate_hashes),
            "duplicate_image_hashes": duplicate_hashes,
        }
        atomic_write_json(incomplete_dir / "manifest.json", manifest)
        atomic_write_json(incomplete_dir / "summary.json", summary)
        if failures:
            raise SnapshotError(
                f"snapshot is incomplete ({len(failures)} failures); "
                f"diagnostics retained at {incomplete_dir}"
            )
        try:
            incomplete_dir.rename(final_dir)
        except OSError as exc:
            raise SnapshotError(
                f"failed to publish snapshot without overwriting {final_dir}: {exc}"
            ) from exc
        return final_dir

    @staticmethod
    def _page_record(result: FetchResult, offset: int) -> dict[str, Any]:
        content_type = media_type(result.content_type)
        error = result.error
        if error is None and content_type not in {"text/html", "application/xhtml+xml"}:
            error = f"unexpected page content type: {content_type or 'missing'}"
        if error is None and not result.content:
            error = "empty page response"
        return {
            "offset": offset,
            "source_url": result.url,
            "fetched_at": result.fetched_at,
            "status_code": result.status_code,
            "content_type": content_type,
            "byte_size": len(result.content) if result.content is not None else None,
            "sha256": hashlib.sha256(result.content).hexdigest() if result.content else None,
            "local_path": f"pages/page-{offset:02d}.html" if error is None else None,
            "error": error,
        }

    @staticmethod
    def _image_record(result: FetchResult) -> dict[str, Any]:
        content_type = media_type(result.content_type)
        error = result.error
        image_type = detect_image_type(result.content or b"") if error is None else None
        if error is None and image_type is None:
            error = "unrecognized image signature"
        if error is None and not content_type:
            error = "missing image content type"
        if error is None and content_type != image_type[1]:
            error = f"image content type/signature mismatch: {content_type} != {image_type[1]}"
        digest = hashlib.sha256(result.content).hexdigest() if result.content else None
        extension = image_type[0] if image_type else None
        return {
            "source_url": result.url,
            "fetched_at": result.fetched_at,
            "status_code": result.status_code,
            "content_type": content_type,
            "byte_size": len(result.content) if result.content is not None else None,
            "sha256": digest,
            "local_path": f"jackets/{digest}.{extension}" if error is None else None,
            "error": error,
        }

    @staticmethod
    def _duplicate_hashes(image_records: list[dict[str, Any]]) -> list[dict[str, Any]]:
        urls_by_hash: defaultdict[str, list[str]] = defaultdict(list)
        for record in image_records:
            if record["error"] is None:
                urls_by_hash[record["sha256"]].append(record["source_url"])
        return [
            {"sha256": digest, "source_urls": urls}
            for digest, urls in sorted(urls_by_hash.items())
            if len(urls) > 1
        ]


def iter_request_plan(config: SnapshotConfig, estimated_songs: int) -> Iterator[tuple[str, int]]:
    yield "page", config.page_count
    yield "jacket (maximum; one per estimated song)", estimated_songs
