from __future__ import annotations

import hashlib
import io
import json
import re
import sqlite3
from urllib.error import URLError

import pytest
from test_master_builder import FIXTURE_HTML

from master import builder, web_input
from master.ac_history import VERSION_CATEGORIES
from master.d1_export import normalize_title_search


@pytest.fixture
def release(tmp_path, monkeypatch):
    database = tmp_path / "fixture.sqlite"
    build = builder.parse_master_html(
        FIXTURE_HTML, source_url="https://example.test/source", fetched_at="2026-10-04"
    )
    builder.write_master_database(database, build, master_version="fixture-v1")
    # Model a released master preceding AC history, including GP availability.
    with sqlite3.connect(database) as connection:
        connection.execute("DROP TABLE song_ac_history")
        connection.execute("DELETE FROM master_metadata WHERE key LIKE 'ac_history%'")
        connection.execute("UPDATE songs SET grand_prix_play_available=1")
        connection.execute(
            "INSERT INTO song_aliases "
            "(alias_id, song_id, alias_title, alias_artist, alias_type, source) "
            "SELECT 'alias_fixed', song_id, 'Fixed alias', artist, 'wiki_source', 'fixture' "
            "FROM songs LIMIT 1"
        )
        connection.execute("UPDATE master_metadata SET value='1' WHERE key='song_alias_count'")
    pin = json.loads(web_input.DEFAULT_PIN.read_text(encoding="utf-8"))
    pin.update(content_version="1.2.3", master_content_version="fixture-v1",
               catalog_master_content_version="fixture-v1")
    assets = {"ddrgp-master.sqlite": database.read_bytes()}
    requested = []

    def fetch(url, **kwargs):
        assert kwargs == {"timeout": 60}
        assert url.startswith(pin["release_base_url"] + "/")
        name = url.rsplit("/", 1)[1]
        requested.append(name)
        return io.BytesIO(assets[name])

    monkeypatch.setattr(web_input, "urlopen", fetch)

    def save_pin():
        pin["master_sha256"] = hashlib.sha256(assets["ddrgp-master.sqlite"]).hexdigest()
        assets["reference-set.json"] = json.dumps(
            {key: pin[key] for key in web_input.MANIFEST_FIELDS}
        ).encode()
        pin["manifest_sha256"] = hashlib.sha256(assets["reference-set.json"]).hexdigest()
        path = tmp_path / "pin.json"
        path.write_text(json.dumps(pin), encoding="utf-8")
        return path

    return database, assets, pin, requested, save_pin


def test_fixed_legacy_master_exports_offline_with_identity_aliases_and_categories(
    release, tmp_path, monkeypatch
):
    database, _, _, requested, save_pin = release
    monkeypatch.setattr(builder, "fetch_source_html", lambda *a, **k: pytest.fail("external fetch"))
    before = database.read_bytes()
    output = web_input.prepare_web_master(save_pin(), tmp_path / "output")
    assert requested == ["reference-set.json", "ddrgp-master.sqlite"]
    assert database.read_bytes() == before
    with sqlite3.connect(database) as source, sqlite3.connect(":memory:") as target:
        for migration in sorted((web_input.ROOT / "web/identity-api/migrations").glob("*.sql")):
            target.executescript(migration.read_text(encoding="utf-8"))
        target.executescript(output.read_text(encoding="utf-8"))
        assert target.execute(
            "SELECT chart_id, song_id FROM charts ORDER BY chart_id"
        ).fetchall() == (
            source.execute("SELECT chart_id, song_id FROM charts ORDER BY chart_id").fetchall()
        )
        assert target.execute("SELECT song_id FROM songs ORDER BY song_id").fetchall() == (
            source.execute("SELECT song_id FROM songs ORDER BY song_id").fetchall()
        )
        assert dict(target.execute("SELECT key,value FROM web_master_metadata")) == {
            "master_version": "fixture-v1"
        }
        expected = [
            (sid, normalize_title_search(title), VERSION_CATEGORIES.get(version))
            for sid, title, version in source.execute(
                "SELECT song_id,title,version FROM songs ORDER BY song_id"
            )
        ]
        assert target.execute(
            "SELECT song_id,title_search_key,flare_category FROM songs ORDER BY song_id"
        ).fetchall() == expected
        assert {row[2] for row in expected} == {"CLASSIC", "GOLD"}
        assert target.execute(
            "SELECT search_key FROM song_title_search_aliases WHERE search_key='fixed alias'"
        ).fetchone() == ("fixed alias",)


@pytest.mark.parametrize(
    "failure", ["download", "checksum", "manifest", "invalid_db", "version", "export"]
)
def test_preparation_failure_stops_workflow_before_remote_operations(
    failure, release, tmp_path, monkeypatch
):
    database, assets, pin, _, save_pin = release
    if failure == "invalid_db":
        assets["ddrgp-master.sqlite"] = b"not a database"
    if failure == "version":
        pin["master_content_version"] = pin["catalog_master_content_version"] = "other-version"
    pin_path = save_pin()
    if failure == "checksum":
        assets["ddrgp-master.sqlite"] += b"tampered"
    if failure == "manifest":
        doc = json.loads(assets["reference-set.json"])
        doc["content_version"] = "9.9.9"
        assets["reference-set.json"] = json.dumps(doc).encode()
        pin["manifest_sha256"] = hashlib.sha256(assets["reference-set.json"]).hexdigest()
        pin_path.write_text(json.dumps(pin), encoding="utf-8")
    if failure == "download":
        def unavailable(*args, **kwargs):
            raise URLError("unavailable")
        monkeypatch.setattr(web_input, "urlopen", unavailable)
    if failure == "export":
        monkeypatch.setattr(web_input, "export_shared_master_sql", lambda _: "INVALID SQL;")

    # Exercise the workflow command order with failed preparation and mocked remote commands.
    workflow = (web_input.ROOT / ".github/workflows/deploy-web.yml").read_text(encoding="utf-8")
    commands = re.findall(r"^\s+(?:run: )?((?:uv run python|npm run migrate:remote|"
                          r"npx wrangler d1 execute|npm run deploy).*)$", workflow, re.MULTILINE)
    remote_calls = []
    with pytest.raises((URLError, ValueError, sqlite3.DatabaseError)):
        for command in commands:
            if command.startswith("uv run python"):
                web_input.main(["--pin", str(pin_path), "--output-dir", str(tmp_path / "failed")])
            else:
                remote_calls.append(command)
    assert remote_calls == []
    assert not (tmp_path / "failed/ddrgp-web-master.sql").exists()
    assert database.exists()


@pytest.mark.parametrize("field,value", [
    ("release_base_url", "https://github.com/tts1374/ddrgp_scorelog/releases/download/latest"),
    ("master_schema_version", 2),
    ("catalog_master_content_version", "different"),
    ("master_sha256", "invalid"),
])
def test_invalid_pin_is_rejected_before_download(field, value, release, tmp_path):
    _, _, pin, requested, save_pin = release
    pin_path = save_pin()
    pin[field] = value
    pin_path.write_text(json.dumps(pin), encoding="utf-8")
    with pytest.raises(ValueError):
        web_input.prepare_web_master(pin_path, tmp_path / "invalid-pin")
    assert requested == []
    assert not (tmp_path / "invalid-pin").exists()


def test_existing_output_directory_is_preserved(release, tmp_path):
    _, _, _, requested, save_pin = release
    output = tmp_path / "existing"
    output.mkdir()
    sentinel = output / "ddrgp-web-master.sql"
    sentinel.write_text("existing material", encoding="utf-8")
    with pytest.raises(FileExistsError):
        web_input.prepare_web_master(save_pin(), output)
    assert requested == []
    assert sentinel.read_text(encoding="utf-8") == "existing material"
