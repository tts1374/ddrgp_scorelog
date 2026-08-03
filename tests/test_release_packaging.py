from __future__ import annotations

import shutil
import sqlite3
import subprocess
from pathlib import Path

import pytest
from test_jacket_reference_catalog import write_master

from tools.vision_poc import jacket_reference_catalog as catalog


def test_release_package_input_validation_rejects_catalog_for_another_master(
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
    script = (
        Path(__file__).resolve().parents[1]
        / "app"
        / "packaging"
        / "Build-Release.ps1"
    )

    valid = _validate_inputs(pwsh, script, master_db, catalog_db)
    assert valid.returncode == 0, valid.stderr
    assert "master_version=master-v1" in valid.stdout

    with sqlite3.connect(catalog_db) as connection, connection:
        connection.execute(
            "UPDATE catalog_metadata SET value = 'older-master' "
            "WHERE key = 'master_version'"
        )

    mismatch = _validate_inputs(pwsh, script, master_db, catalog_db)
    assert mismatch.returncode != 0
    assert "does not match the master DB" in mismatch.stderr


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
            "-CatalogDatabase",
            str(catalog_db),
            "-ValidateInputsOnly",
        ],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
