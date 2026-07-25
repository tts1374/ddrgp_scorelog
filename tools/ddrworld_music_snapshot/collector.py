from __future__ import annotations

import hashlib
import json
import math
import os
import re
import shutil
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
DEFAULT_SNAPSHOT_ROOT = Path("data/ddrworld_music_snapshot")
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


class SnapshotCancelled(SnapshotError):
    """Raised when a collection is cancelled at a request boundary."""


@dataclass(frozen=True)
class SnapshotProgress:
    phase: str
    completed: int
    total: int


def find_repository_root(start_directory: Path | None = None) -> Path:
    start = (start_directory or Path(__file__)).resolve()
    if start.is_file():
        start = start.parent
    for directory in (start, *start.parents):
        git_path = directory / ".git"
        if git_path.is_dir() or git_path.is_file():
            return directory
    raise SnapshotError(f"repository root cannot be resolved from: {start}")


def resolve_repository_path(path: Path, repository_root: Path | None = None) -> Path:
    root = (repository_root or find_repository_root()).resolve()
    candidate = path if path.is_absolute() else root / path
    # Normalize `..` without following a fixed output symlink. The collector
    # must reject that link before it can publish or remove anything.
    return Path(os.path.abspath(candidate))


@dataclass(frozen=True)
class SnapshotConfig:
    snapshot_id: str
    output_root: Path = DEFAULT_SNAPSHOT_ROOT
    page_count: int = DEFAULT_PAGE_COUNT
    delay_seconds: float = DEFAULT_DELAY_SECONDS
    connect_timeout_seconds: float = DEFAULT_CONNECT_TIMEOUT_SECONDS
    read_timeout_seconds: float = DEFAULT_READ_TIMEOUT_SECONDS
    filter_value: int = DEFAULT_FILTER
    filter_type: int = DEFAULT_FILTER_TYPE
    play_mode: int = DEFAULT_PLAY_MODE
    fixed_output: bool = False
    incomplete_root: Path | None = None
    repository_root: Path | None = None
    cancel_file: Path | None = None

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
        if not all(
            math.isfinite(value)
            for value in (
                self.delay_seconds,
                self.connect_timeout_seconds,
                self.read_timeout_seconds,
            )
        ):
            raise SnapshotError("HTTP delay and timeout values must be finite")
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
        cancel_check: Callable[[], bool] | None = None,
    ) -> None:
        self.delay_seconds = delay_seconds
        self.timeout = timeout
        self.session = session or requests.Session()
        self.sleep = sleep
        self.now = now
        self.cancel_check = cancel_check
        self._last_request_finished_at: float | None = None
        adapter = HTTPAdapter(max_retries=0, pool_connections=1, pool_maxsize=1)
        self.session.mount("https://", adapter)
        self.session.headers.update({"User-Agent": USER_AGENT, "Accept-Encoding": "gzip, deflate"})

    def get(self, url: str, *, accept: str) -> FetchResult:
        if self._last_request_finished_at is not None:
            elapsed = time.monotonic() - self._last_request_finished_at
            if elapsed < self.delay_seconds:
                self.sleep(self.delay_seconds - elapsed)
        if self.cancel_check is not None and self.cancel_check():
            raise SnapshotCancelled("snapshot collection cancelled")
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
        cancel_check: Callable[[], bool] | None = None,
    ) -> None:
        config.validate()
        self.config = config
        self.cancel_check = cancel_check or self._cancel_file_exists
        self.fetcher = fetcher or SerialFetcher(
            delay_seconds=config.delay_seconds,
            timeout=(config.connect_timeout_seconds, config.read_timeout_seconds),
            cancel_check=self.cancel_check,
        )
        self.now = now

    def collect(
        self,
        *,
        progress: Callable[[SnapshotProgress], None] | None = None,
    ) -> Path:
        final_dir, incomplete_dir = self._resolve_output_paths()
        if self.config.fixed_output:
            self._prepare_fixed_output(final_dir, incomplete_dir)
        elif final_dir.exists() or incomplete_dir.exists():
            raise SnapshotError(
                "snapshot output already exists; refusing to overwrite: "
                f"{final_dir} or {incomplete_dir}"
            )
        final_dir.parent.mkdir(parents=True, exist_ok=True)
        incomplete_dir.parent.mkdir(parents=True, exist_ok=True)
        incomplete_dir.mkdir()

        started_at = self.now().isoformat().replace("+00:00", "Z")
        page_records: list[dict[str, Any]] = []
        image_records: list[dict[str, Any]] = []
        songs: list[SongEntry] = []
        failures: list[dict[str, Any]] = []
        request_count = 0

        try:
            self._emit_progress(progress, "pages", 0, self.config.page_count)
            for offset in range(self.config.page_count):
                self._check_cancelled()
                url = build_page_url(self.config, offset)
                result = self.fetcher.get(url, accept="text/html,application/xhtml+xml")
                request_count += 1
                self._check_cancelled()
                record = self._page_record(result, offset)
                page_records.append(record)
                if record["error"] is not None:
                    failures.append(
                        {"resource": "page", "offset": offset, "error": record["error"]}
                    )
                else:
                    assert result.content is not None
                    atomic_write_bytes(
                        incomplete_dir / f"pages/page-{offset:02d}.html", result.content
                    )
                    try:
                        songs.extend(parse_page(result.content, page_offset=offset, page_url=url))
                    except SnapshotError as exc:
                        failures.append(
                            {"resource": "page", "offset": offset, "error": str(exc)}
                        )
                self._emit_progress(progress, "pages", offset + 1, self.config.page_count)

            jacket_urls = list(dict.fromkeys(song.jacket_source_url for song in songs))
            songs_by_jacket_url = defaultdict(int)
            for song in songs:
                songs_by_jacket_url[song.jacket_source_url] += 1
            jacket_total = len(songs)
            jacket_completed = 0
            self._emit_progress(progress, "jackets", 0, jacket_total)
            for jacket_url in jacket_urls:
                self._check_cancelled()
                result = self.fetcher.get(jacket_url, accept="image/*")
                request_count += 1
                self._check_cancelled()
                record = self._image_record(result)
                image_records.append(record)
                if record["error"] is not None:
                    failures.append(
                        {"resource": "image", "url": jacket_url, "error": record["error"]}
                    )
                else:
                    assert result.content is not None
                    atomic_write_bytes(incomplete_dir / record["local_path"], result.content)
                jacket_completed += songs_by_jacket_url[jacket_url]
                self._emit_progress(progress, "jackets", jacket_completed, jacket_total)

            song_records = self._song_records(songs, image_records)
            atomic_write_jsonl(incomplete_dir / "songs.jsonl", song_records)

            duplicate_hashes = self._duplicate_hashes(image_records)
            status = "complete" if not failures else "incomplete"
            completed_at = self.now().isoformat().replace("+00:00", "Z")
            manifest = self._manifest(
                status=status,
                started_at=started_at,
                completed_at=completed_at,
                page_records=page_records,
                image_records=image_records,
                failures=failures,
            )
            summary = self._summary(
                status=status,
                request_count=request_count,
                page_records=page_records,
                image_records=image_records,
                songs=songs,
                duplicate_hashes=duplicate_hashes,
                snapshot_id=self.config.snapshot_id,
                failures=failures,
            )
            atomic_write_json(incomplete_dir / "manifest.json", manifest)
            atomic_write_json(incomplete_dir / "summary.json", summary)
            if failures:
                raise SnapshotError(
                    f"snapshot is incomplete ({len(failures)} failures); "
                    f"diagnostics retained at {incomplete_dir}"
                )
            self._check_cancelled()
            self._validate_complete_snapshot(incomplete_dir)
            if self.config.fixed_output:
                self._publish_fixed_snapshot(final_dir, incomplete_dir)
            else:
                try:
                    incomplete_dir.rename(final_dir)
                except OSError as exc:
                    raise SnapshotError(
                        f"failed to publish snapshot without overwriting {final_dir}: {exc}"
                    ) from exc
            return final_dir
        except SnapshotCancelled:
            self._write_cancelled_diagnostics(
                incomplete_dir,
                started_at=started_at,
                page_records=page_records,
                image_records=image_records,
                songs=songs,
                request_count=request_count,
            )
            raise

    def _resolve_output_paths(self) -> tuple[Path, Path]:
        output_root = resolve_repository_path(
            self.config.output_root,
            self.config.repository_root,
        )
        if self.config.fixed_output:
            incomplete_root = self.config.incomplete_root or Path(
                output_root.parent / f"{output_root.name}.incomplete"
            )
            incomplete_dir = resolve_repository_path(
                incomplete_root,
                self.config.repository_root,
            )
            if output_root == incomplete_dir:
                raise SnapshotError("fixed snapshot and incomplete paths must differ")
            return output_root, incomplete_dir
        return (
            output_root / self.config.snapshot_id,
            output_root / f"{self.config.snapshot_id}.incomplete",
        )

    def _prepare_fixed_output(self, final_dir: Path, incomplete_dir: Path) -> None:
        if final_dir.is_symlink() or incomplete_dir.is_symlink():
            raise SnapshotError("fixed snapshot paths must not be symbolic links")
        if final_dir.exists() and not self._is_complete_snapshot(final_dir):
            raise SnapshotError(
                "fixed snapshot root is not a complete snapshot; refusing to overwrite: "
                f"{final_dir}"
            )
        if incomplete_dir.exists():
            if incomplete_dir.is_dir():
                shutil.rmtree(incomplete_dir)
            else:
                incomplete_dir.unlink()

    def _publish_fixed_snapshot(self, final_dir: Path, incomplete_dir: Path) -> None:
        backup_dir = final_dir.with_name(f".{final_dir.name}.previous-{uuid.uuid4().hex}")
        had_previous = final_dir.exists()
        if had_previous:
            final_dir.rename(backup_dir)
        try:
            incomplete_dir.rename(final_dir)
        except OSError as exc:
            if had_previous and backup_dir.exists():
                backup_dir.rename(final_dir)
            raise SnapshotError(
                f"failed to publish fixed snapshot without exposing partial data: {exc}"
            ) from exc
        if backup_dir.exists():
            shutil.rmtree(backup_dir)

    def _check_cancelled(self) -> None:
        if self.cancel_check():
            raise SnapshotCancelled("snapshot collection cancelled")

    def _cancel_file_exists(self) -> bool:
        return self.config.cancel_file is not None and self.config.cancel_file.exists()

    @staticmethod
    def _emit_progress(
        progress: Callable[[SnapshotProgress], None] | None,
        phase: str,
        completed: int,
        total: int,
    ) -> None:
        if progress is not None:
            progress(SnapshotProgress(phase, completed, total))

    @staticmethod
    def _song_records(
        songs: list[SongEntry],
        image_records: list[dict[str, Any]],
    ) -> list[dict[str, Any]]:
        images_by_url = {record["source_url"]: record for record in image_records}
        return [
            {
                **asdict(song),
                "jacket_local_path": (
                    image.get("local_path") if image is not None else None
                ),
                "jacket_content_type": (
                    image.get("content_type") if image is not None else None
                ),
                "jacket_byte_size": (
                    image.get("byte_size") if image is not None else None
                ),
                "jacket_sha256": image.get("sha256") if image is not None else None,
                "jacket_error": (
                    image.get("error") if image is not None else "image was not requested"
                ),
            }
            for song in songs
            for image in [images_by_url.get(song.jacket_source_url)]
        ]

    def _manifest(
        self,
        *,
        status: str,
        started_at: str,
        completed_at: str,
        page_records: list[dict[str, Any]],
        image_records: list[dict[str, Any]],
        failures: list[dict[str, Any]],
    ) -> dict[str, Any]:
        return {
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

    @staticmethod
    def _summary(
        *,
        status: str,
        request_count: int,
        page_records: list[dict[str, Any]],
        image_records: list[dict[str, Any]],
        songs: list[SongEntry],
        duplicate_hashes: list[dict[str, Any]],
        snapshot_id: str,
        failures: list[dict[str, Any]],
    ) -> dict[str, Any]:
        stored_paths = {
            record["local_path"]
            for record in image_records
            if record["error"] is None and record["local_path"]
        }
        return {
            "schema_version": "ddrworld-music-snapshot-summary-v1",
            "status": status,
            "snapshot_id": snapshot_id,
            "request_count": request_count,
            "page_request_count": len(page_records),
            "image_request_count": len(image_records),
            "song_count": len(songs),
            "unique_jacket_url_count": len(image_records),
            "stored_jacket_count": len(stored_paths),
            "failure_count": len(failures),
            "duplicate_image_hash_count": len(duplicate_hashes),
            "duplicate_image_hashes": duplicate_hashes,
        }

    def _write_cancelled_diagnostics(
        self,
        incomplete_dir: Path,
        *,
        started_at: str,
        page_records: list[dict[str, Any]],
        image_records: list[dict[str, Any]],
        songs: list[SongEntry],
        request_count: int,
    ) -> None:
        if not incomplete_dir.is_dir():
            return
        cancelled_at = self.now().isoformat().replace("+00:00", "Z")
        try:
            for name in ("manifest.json", "summary.json"):
                (incomplete_dir / name).unlink(missing_ok=True)
            atomic_write_json(
                incomplete_dir / "manifest.json",
                self._manifest(
                    status="cancelled",
                    started_at=started_at,
                    completed_at=cancelled_at,
                    page_records=page_records,
                    image_records=image_records,
                    failures=[{"resource": "collection", "error": "cancelled"}],
                ),
            )
            duplicate_hashes = self._duplicate_hashes(image_records)
            atomic_write_json(
                incomplete_dir / "summary.json",
                {
                    **self._summary(
                        status="cancelled",
                        request_count=request_count,
                        page_records=page_records,
                        image_records=image_records,
                        songs=songs,
                        duplicate_hashes=duplicate_hashes,
                        snapshot_id=self.config.snapshot_id,
                        failures=[{"resource": "collection", "error": "cancelled"}],
                    ),
                    "failure_count": 1,
                },
            )
        except (OSError, SnapshotError):
            # Cancellation must never turn the preserved final snapshot into a failure.
            return

    @classmethod
    def _is_complete_snapshot(cls, path: Path) -> bool:
        try:
            cls._validate_complete_snapshot(path)
        except (OSError, SnapshotError, json.JSONDecodeError, TypeError, ValueError):
            return False
        return True

    @staticmethod
    def _validate_complete_snapshot(path: Path) -> None:
        required_files = ("manifest.json", "pages", "songs.jsonl", "jackets", "summary.json")
        if not path.is_dir():
            raise SnapshotError(f"snapshot directory does not exist: {path}")
        for name in required_files:
            child = path / name
            if name in {"pages", "jackets"}:
                if not child.is_dir():
                    raise SnapshotError(f"snapshot required directory is missing: {child}")
            elif not child.is_file():
                raise SnapshotError(f"snapshot required file is missing: {child}")
        manifest = json.loads((path / "manifest.json").read_text(encoding="utf-8"))
        summary = json.loads((path / "summary.json").read_text(encoding="utf-8"))
        if manifest.get("status") != "complete" or summary.get("status") != "complete":
            raise SnapshotError("snapshot status is not complete")
        if not isinstance(manifest.get("snapshot_id"), str) or not manifest["snapshot_id"]:
            raise SnapshotError("snapshot id is invalid")
        if summary.get("snapshot_id") != manifest["snapshot_id"]:
            raise SnapshotError("snapshot ids do not match")
        if not isinstance(manifest.get("failures"), list) or manifest["failures"]:
            raise SnapshotError("complete snapshot contains failures")
        if not isinstance(summary.get("song_count"), int) or summary["song_count"] < 0:
            raise SnapshotError("snapshot song count is invalid")
        songs = [
            json.loads(line)
            for line in (path / "songs.jsonl").read_text(encoding="utf-8").splitlines()
            if line
        ]
        if len(songs) != summary["song_count"]:
            raise SnapshotError("snapshot song count does not match songs.jsonl")
        if not isinstance(summary.get("page_request_count"), int) or summary[
            "page_request_count"
        ] < 0:
            raise SnapshotError("snapshot page count is invalid")
        page_files = [child for child in (path / "pages").iterdir() if child.is_file()]
        if len(page_files) != summary["page_request_count"]:
            raise SnapshotError("snapshot page count does not match page files")
        image_records = manifest.get("images")
        if not isinstance(image_records, list):
            raise SnapshotError("snapshot image manifest is invalid")
        if not isinstance(summary.get("image_request_count"), int) or summary[
            "image_request_count"
        ] < 0:
            raise SnapshotError("snapshot image count is invalid")
        if len(image_records) != summary["image_request_count"]:
            raise SnapshotError("snapshot image count does not match manifest")
        if (
            not isinstance(summary.get("stored_jacket_count"), int)
            or summary["stored_jacket_count"] < 0
        ):
            raise SnapshotError("snapshot stored image count is invalid")
        stored_files = [child for child in (path / "jackets").iterdir() if child.is_file()]
        if len(stored_files) != summary["stored_jacket_count"]:
            raise SnapshotError("snapshot stored image count does not match jacket files")

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
