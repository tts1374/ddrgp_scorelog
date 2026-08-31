from __future__ import annotations

import hashlib
import shutil
import sqlite3
import subprocess
from pathlib import Path

import pytest
from test_jacket_reference_catalog import write_master

from tools.vision_poc import jacket_reference_catalog as catalog


def test_release_package_defaults_to_collector_source_catalog() -> None:
    script = (
        Path(__file__).resolve().parents[1]
        / "app"
        / "packaging"
        / "Build-Release.ps1"
    )

    script_text = script.read_text(encoding="utf-8")

    assert "..\\..\\databases\\jacket-catalog.sqlite" in script_text
    assert "validate-bind-inputs" in script_text
    assert "bind-release-catalog" in script_text
    assert "--output-catalog $boundCatalogPath" in script_text
    assert "jacket-catalog-release.sqlite" not in script_text


def test_release_package_input_validation_accepts_unbound_catalog_without_writing(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.fail("pwsh is required for the Release packaging contract test")
    monkeypatch.chdir(tmp_path)
    databases = tmp_path / "databases"
    databases.mkdir()
    master_db = databases / "ddrgp-master.sqlite"
    catalog_db = databases / "jacket-catalog.sqlite"
    write_master(master_db)
    catalog.create_catalog(catalog_db, master_db)
    with sqlite3.connect(catalog_db) as connection, connection:
        connection.execute(
            "DELETE FROM catalog_metadata WHERE key = 'master_version'"
        )
    master_hash = hashlib.sha256(master_db.read_bytes()).hexdigest()
    catalog_hash = hashlib.sha256(catalog_db.read_bytes()).hexdigest()
    script = (
        Path(__file__).resolve().parents[1]
        / "app"
        / "packaging"
        / "Build-Release.ps1"
    )

    result = _validate_inputs(pwsh, script, master_db, catalog_db)

    assert result.returncode == 0, result.stderr
    assert "master_version=master-v1" in result.stdout
    assert hashlib.sha256(master_db.read_bytes()).hexdigest() == master_hash
    assert hashlib.sha256(catalog_db.read_bytes()).hexdigest() == catalog_hash


def test_release_package_input_validation_rejects_invalid_catalog(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.fail("pwsh is required for the Release packaging contract test")
    monkeypatch.chdir(tmp_path)
    databases = tmp_path / "databases"
    databases.mkdir()
    master_db = databases / "ddrgp-master.sqlite"
    catalog_db = databases / "jacket-catalog.sqlite"
    write_master(master_db)
    catalog.create_catalog(catalog_db, master_db)

    with sqlite3.connect(catalog_db) as connection, connection:
        connection.execute("DROP TABLE reference_candidates")
    script = (
        Path(__file__).resolve().parents[1]
        / "app"
        / "packaging"
        / "Build-Release.ps1"
    )

    result = _validate_inputs(pwsh, script, master_db, catalog_db)

    assert result.returncode != 0
    assert "Release reference DB bind input validation failed" in result.stderr


def test_release_package_catalog_database_parameter_remains_compatible() -> None:
    script = (
        Path(__file__).resolve().parents[1]
        / "app"
        / "packaging"
        / "Build-Release.ps1"
    )

    assert "[Alias('CatalogDatabase')]" in script.read_text(encoding="utf-8")


def test_release_catalog_binding_writes_only_versioned_build_output(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    databases = tmp_path / "databases"
    databases.mkdir()
    master_db = databases / "ddrgp-master.sqlite"
    source_catalog = databases / "jacket-catalog.sqlite"
    write_master(master_db)
    catalog.create_catalog(source_catalog, master_db)
    with sqlite3.connect(source_catalog) as connection, connection:
        connection.execute(
            "DELETE FROM catalog_metadata WHERE key = 'master_version'"
        )
    source_hash = hashlib.sha256(source_catalog.read_bytes()).hexdigest()
    output_catalog = (
        tmp_path
        / "data"
        / "release-build"
        / "0.1.0"
        / "publish"
        / "ReferenceData"
        / "jacket-catalog.sqlite"
    )
    output_catalog.parent.mkdir(parents=True)

    result = catalog.bind_catalog_to_master(
        source_catalog,
        output_catalog,
        master_db,
        release_output=True,
    )

    assert result["master_version"] == "master-v1"
    assert hashlib.sha256(source_catalog.read_bytes()).hexdigest() == source_hash
    assert catalog.validate_release_reference_pair(output_catalog, master_db)[
        "catalog_master_version"
    ] == "master-v1"


def test_release_catalog_binding_rejects_output_outside_release_build(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    databases = tmp_path / "databases"
    databases.mkdir()
    master_db = databases / "ddrgp-master.sqlite"
    source_catalog = databases / "jacket-catalog.sqlite"
    write_master(master_db)
    catalog.create_catalog(source_catalog, master_db)

    with pytest.raises(ValueError, match="must be under data/release-build"):
        catalog.bind_catalog_to_master(
            source_catalog,
            tmp_path / "outside.sqlite",
            master_db,
            release_output=True,
        )


def _validate_inputs(
    pwsh: str, script: Path, master_db: Path, catalog_db: Path
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            pwsh,
            "-NoProfile",
            "-File",
            str(script),
            "-Version",
            "0.1.0",
            "-MasterDatabase",
            str(master_db),
            "-CatalogSourceDatabase",
            str(catalog_db),
            "-ValidateInputsOnly",
        ],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
