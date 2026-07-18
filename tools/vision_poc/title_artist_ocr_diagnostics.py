from __future__ import annotations

import argparse
import csv
import io
import json
import math
import os
import shutil
import subprocess
import tempfile
from collections import Counter
from collections.abc import Callable, Iterable
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFilter, ImageOps

from tools.vision_poc import jacket_catalog_review_projection as projection
from tools.vision_poc import jacket_reference_catalog as catalog
from tools.vision_poc import master_match
from tools.vision_poc import title_artist_evaluation as evaluation
from tools.vision_poc import unresolved_candidate_evaluation as unresolved

REPORT_SCHEMA_VERSION = "m5c-title-artist-ocr-diagnostics-report-v1"
RECEIPT_SCHEMA_VERSION = "m5c-title-artist-ocr-diagnostics-receipt-v1"
FAILURE_REASON_LANGUAGE_UNAVAILABLE = "tesseract_language_unavailable_v1"
DEFAULT_REPRESENTATIVE_LIMIT = 12


@dataclass(frozen=True)
class OcrProfile:
    profile_id: str
    field: str
    language: str
    psm: int
    scale: int
    sharpen: bool


TITLE_PROFILES = tuple(
    OcrProfile(
        f"title-{language.replace('+', '-')}-psm{psm}-scale4-sharpen",
        "title",
        language,
        psm,
        4,
        True,
    )
    for language in ("eng", "jpn+eng")
    for psm in (6, 7)
)
ARTIST_PROFILES = tuple(
    OcrProfile(
        f"artist-{language.replace('+', '-')}-psm7-scale{scale}-"
        f"{'sharpen' if sharpen else 'no-sharpen'}",
        "artist",
        language,
        7,
        scale,
        sharpen,
    )
    for language in ("eng", "jpn+eng")
    for scale in (5, 10, 15)
    for sharpen in (True, False)
)
PROFILES = TITLE_PROFILES + ARTIST_PROFILES
BASELINE_TITLE_PROFILE = "title-eng-psm7-scale4-sharpen"
BASELINE_ARTIST_PROFILE = "artist-eng-psm7-scale5-sharpen"

CSV_FIELDS = (
    "observation_id",
    "configuration_id",
    "title_profile_id",
    "artist_profile_id",
    "title_raw",
    "title_confidence",
    "title_status",
    "title_failure_reason",
    "artist_raw",
    "artist_confidence",
    "artist_status",
    "artist_failure_reason",
    "candidate_status",
    "candidate_reason",
    "candidate_song_ids",
)

Extractor = Callable[[Image.Image, OcrProfile, str | None, set[str]], evaluation.FieldExtraction]
ProjectionBuilder = Callable[..., dict[str, Any]]


def _profile_language_parts(language: str) -> set[str]:
    return {part for part in language.split("+") if part}


def inspect_tesseract() -> tuple[str | None, set[str], str]:
    executable = shutil.which("tesseract")
    if executable is None:
        return None, set(), "tesseract_not_found"
    try:
        completed = subprocess.run(
            [executable, "--list-langs"],
            capture_output=True,
            check=False,
            timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired):
        return executable, set(), "tesseract_language_probe_failed"
    if completed.returncode != 0:
        return executable, set(), "tesseract_language_probe_failed"
    try:
        lines = completed.stdout.decode("utf-8", errors="strict").splitlines()
    except UnicodeDecodeError:
        return executable, set(), "tesseract_language_probe_invalid"
    languages = {line.strip() for line in lines[1:] if line.strip()}
    return executable, languages, ""


def profile_availability(
    profile: OcrProfile, executable: str | None, languages: set[str], probe_failure: str
) -> tuple[bool, str]:
    if executable is None:
        return False, "tesseract_not_found"
    if probe_failure:
        return False, probe_failure
    missing = sorted(_profile_language_parts(profile.language) - languages)
    if missing:
        return False, f"{FAILURE_REASON_LANGUAGE_UNAVAILABLE}:{'+'.join(missing)}"
    return True, ""


def probe_tsv_contract(executable: str, language: str) -> str:
    payload = io.BytesIO()
    Image.new("L", (32, 32), 255).save(payload, format="PNG")
    try:
        completed = subprocess.run(
            [
                executable,
                "stdin",
                "stdout",
                "-l",
                language,
                "--psm",
                "7",
                "--dpi",
                "300",
                "tsv",
            ],
            input=payload.getvalue(),
            capture_output=True,
            check=False,
            timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired):
        return "tesseract_execution_failed"
    if completed.returncode != 0:
        return "tesseract_nonzero_exit"
    try:
        evaluation._parse_tesseract_tsv(
            completed.stdout.decode("utf-8", errors="strict")
        )
    except (UnicodeDecodeError, ValueError):
        return "tesseract_output_invalid"
    return ""


def _prepare(image: Image.Image, profile: OcrProfile) -> Image.Image:
    roi = image.crop(evaluation._scaled_roi(image, profile.field)).convert("RGB")
    gray = ImageOps.autocontrast(roi.convert("L")).resize(
        (roi.width * profile.scale, roi.height * profile.scale),
        Image.Resampling.LANCZOS,
    )
    if profile.sharpen:
        gray = gray.filter(ImageFilter.SHARPEN)
    return ImageOps.expand(gray, border=24, fill=255)


def extract_profile(
    image: Image.Image,
    profile: OcrProfile,
    executable: str | None,
    languages: set[str],
) -> evaluation.FieldExtraction:
    available, failure = profile_availability(profile, executable, languages, "")
    if not available:
        status = "engine_unavailable" if failure == "tesseract_not_found" else "ocr_unavailable"
        return evaluation.FieldExtraction("", "", None, status, failure)
    payload = io.BytesIO()
    _prepare(image, profile).save(payload, format="PNG")
    try:
        completed = subprocess.run(
            [
                str(executable),
                "stdin",
                "stdout",
                "-l",
                profile.language,
                "--psm",
                str(profile.psm),
                "--dpi",
                "300",
                "tsv",
            ],
            input=payload.getvalue(),
            capture_output=True,
            check=False,
            timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired):
        return evaluation.FieldExtraction(
            "", "", None, "ocr_failed", "tesseract_execution_failed"
        )
    if completed.returncode != 0:
        return evaluation.FieldExtraction("", "", None, "ocr_failed", "tesseract_nonzero_exit")
    try:
        raw, confidence = evaluation._parse_tesseract_tsv(
            completed.stdout.decode("utf-8", errors="strict")
        )
    except (UnicodeDecodeError, ValueError):
        return evaluation.FieldExtraction("", "", None, "ocr_failed", "tesseract_output_invalid")
    normalized = master_match.normalize_song_title(raw)
    if not normalized:
        return evaluation.FieldExtraction(raw, "", confidence, "empty", "empty_ocr")
    if confidence is None:
        return evaluation.FieldExtraction(
            raw, normalized, None, "low_confidence", "confidence_unavailable"
        )
    if confidence < evaluation.MINIMUM_FIELD_CONFIDENCE:
        return evaluation.FieldExtraction(
            raw, normalized, confidence, "low_confidence", "below_confidence_gate"
        )
    return evaluation.FieldExtraction(raw, normalized, confidence, "ok", "")


def _validation_extractor(
    _image: Image.Image, _field: str, _method: str
) -> evaluation.FieldExtraction:
    return evaluation.FieldExtraction("", "", None, "empty", "diagnostic_validation_only")


def _configuration_pairs() -> list[tuple[str, str, str]]:
    pairs: list[tuple[str, str, str]] = []
    for profile in TITLE_PROFILES:
        pairs.append((profile.profile_id, profile.profile_id, BASELINE_ARTIST_PROFILE))
    for profile in ARTIST_PROFILES:
        pair = (profile.profile_id, BASELINE_TITLE_PROFILE, profile.profile_id)
        if pair[1:] != (BASELINE_TITLE_PROFILE, BASELINE_ARTIST_PROFILE):
            pairs.append(pair)
    return pairs


def _candidate(
    master: catalog.MasterIdentity,
    title: evaluation.FieldExtraction,
    artist: evaluation.FieldExtraction,
) -> tuple[str, str, list[str]]:
    if title.status != "ok" or artist.status != "ok":
        reason = title.failure_reason or artist.failure_reason or "field_evaluation_failed"
        return "extraction_failed", reason, []
    resolved = catalog.resolve_observation(master, title.raw, artist.raw)
    return resolved.status, resolved.reason, list(resolved.candidate_song_ids)


def evaluate_images(
    images: Iterable[tuple[str, Image.Image]],
    *,
    master: catalog.MasterIdentity,
    executable: str | None,
    languages: set[str],
    probe_failure: str = "",
    extractor: Extractor = extract_profile,
) -> list[dict[str, Any]]:
    configurations = _configuration_pairs()
    rows: list[dict[str, Any]] = []
    for observation_id, image in images:
        extracted = {}
        for profile in PROFILES:
            available, failure = profile_availability(
                profile, executable, languages, probe_failure
            )
            extracted[profile.profile_id] = (
                extractor(image, profile, executable, languages)
                if available
                else evaluation.FieldExtraction("", "", None, "ocr_unavailable", failure)
            )
        for configuration_id, title_profile_id, artist_profile_id in configurations:
            title = extracted[title_profile_id]
            artist = extracted[artist_profile_id]
            candidate_status, candidate_reason, candidate_song_ids = _candidate(
                master, title, artist
            )
            rows.append(
                {
                    "observation_id": observation_id,
                    "configuration_id": configuration_id,
                    "title_profile_id": title_profile_id,
                    "artist_profile_id": artist_profile_id,
                    "title": asdict(title),
                    "artist": asdict(artist),
                    "candidate_status": candidate_status,
                    "candidate_reason": candidate_reason,
                    "candidate_song_ids": candidate_song_ids,
                }
            )
    return sorted(rows, key=lambda row: (row["configuration_id"], row["observation_id"]))


def _percentile(values: list[float], fraction: float) -> float | None:
    if not values:
        return None
    position = (len(values) - 1) * fraction
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return values[lower]
    return values[lower] + (values[upper] - values[lower]) * (position - lower)


def _confidence_distribution(values: Iterable[float | None]) -> dict[str, float | int | None]:
    present = sorted(value for value in values if value is not None)
    return {
        "count": len(present),
        "minimum": None if not present else present[0],
        "p25": _percentile(present, 0.25),
        "median": _percentile(present, 0.5),
        "p75": _percentile(present, 0.75),
        "maximum": None if not present else present[-1],
    }


def summarize(
    rows: Iterable[dict[str, Any]],
    *,
    total_references: int,
    eligible_observations: int,
    skipped_reasons: Counter[str],
    executable: str | None,
    languages: set[str],
    probe_failure: str,
) -> dict[str, Any]:
    rows = list(rows)
    profiles = []
    for profile in PROFILES:
        available, failure = profile_availability(profile, executable, languages, probe_failure)
        unique_field_rows = {
            row["observation_id"]: row[profile.field]
            for row in rows
            if row[f"{profile.field}_profile_id"] == profile.profile_id
        }
        field_rows = list(unique_field_rows.values())
        profiles.append(
            {
                **asdict(profile),
                "available": available,
                "unavailable_reason": failure,
                "observation_count": len(field_rows),
                "status_counts": dict(sorted(Counter(row["status"] for row in field_rows).items())),
                "failure_reason_counts": dict(
                    sorted(
                        Counter(
                            row["failure_reason"] for row in field_rows if row["failure_reason"]
                        ).items()
                    )
                ),
                "nonempty_raw_count": sum(bool(row["raw"].strip()) for row in field_rows),
                "confidence": _confidence_distribution(row["confidence"] for row in field_rows),
            }
        )
    configurations = []
    for configuration_id, title_profile_id, artist_profile_id in _configuration_pairs():
        selected = [row for row in rows if row["configuration_id"] == configuration_id]
        configurations.append(
            {
                "configuration_id": configuration_id,
                "title_profile_id": title_profile_id,
                "artist_profile_id": artist_profile_id,
                "observation_count": len(selected),
                "field_status_combinations": dict(
                    sorted(
                        Counter(
                            f"{row['title']['status']}|{row['artist']['status']}"
                            for row in selected
                        ).items()
                    )
                ),
                "candidate_status_counts": dict(
                    sorted(Counter(row["candidate_status"] for row in selected).items())
                ),
                "candidate_reason_counts": dict(
                    sorted(Counter(row["candidate_reason"] for row in selected).items())
                ),
            }
        )
    return {
        "report_schema_version": REPORT_SCHEMA_VERSION,
        "environment": {
            "tesseract_available": executable is not None,
            "installed_languages": sorted(languages),
            "language_probe_failure": probe_failure,
        },
        "total_catalog_references": total_references,
        "eligible_observations": eligible_observations,
        "skipped_observation_reasons": dict(sorted(skipped_reasons.items())),
        "confidence_gate": evaluation.MINIMUM_FIELD_CONFIDENCE,
        "profiles": profiles,
        "configurations": configurations,
    }


def render_csv(rows: Iterable[dict[str, Any]]) -> str:
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=CSV_FIELDS, lineterminator="\n")
    writer.writeheader()
    for row in rows:
        writer.writerow(
            {
                "observation_id": row["observation_id"],
                "configuration_id": row["configuration_id"],
                "title_profile_id": row["title_profile_id"],
                "artist_profile_id": row["artist_profile_id"],
                "title_raw": row["title"]["raw"],
                "title_confidence": ""
                if row["title"]["confidence"] is None
                else f"{row['title']['confidence']:.6f}",
                "title_status": row["title"]["status"],
                "title_failure_reason": row["title"]["failure_reason"],
                "artist_raw": row["artist"]["raw"],
                "artist_confidence": ""
                if row["artist"]["confidence"] is None
                else f"{row['artist']['confidence']:.6f}",
                "artist_status": row["artist"]["status"],
                "artist_failure_reason": row["artist"]["failure_reason"],
                "candidate_status": row["candidate_status"],
                "candidate_reason": row["candidate_reason"],
                "candidate_song_ids": "|".join(row["candidate_song_ids"]),
            }
        )
    return output.getvalue()


def render_markdown(summary: dict[str, Any]) -> str:
    environment = summary["environment"]
    lines = [
        "# M5c title / artist OCR diagnostics",
        "",
        f"- catalog references: `{summary['total_catalog_references']}`",
        f"- eligible observations: `{summary['eligible_observations']}`",
        f"- installed Tesseract languages: `{' / '.join(environment['installed_languages'])}`",
        f"- language probe failure: `{environment['language_probe_failure'] or 'none'}`",
        "",
        "| profile | available | field | language | psm | scale | sharpen | statuses |",
        "| --- | --- | --- | --- | ---: | ---: | --- | --- |",
    ]
    for profile in summary["profiles"]:
        statuses = json.dumps(profile["status_counts"], sort_keys=True)
        available = "yes" if profile["available"] else profile["unavailable_reason"]
        lines.append(
            f"| `{profile['profile_id']}` | `{available}` | `{profile['field']}` | "
            f"`{profile['language']}` | {profile['psm']} | {profile['scale']} | "
            f"`{profile['sharpen']}` | `{statuses}` |"
        )
    lines.extend(
        [
            "",
            "Candidate results are read-only review aids, not truth or automatic confirmation.",
            "Missing OCR languages are not replaced by another language.",
            "Source images, crops, checkpoints, catalog rows, and master rows are not modified.",
            "",
        ]
    )
    return "\n".join(lines)


def _representative_rows(rows: list[dict[str, Any]], limit: int) -> list[dict[str, Any]]:
    baseline = [row for row in rows if row["configuration_id"] == BASELINE_TITLE_PROFILE]
    selected: list[dict[str, Any]] = []
    seen: set[tuple[str, str, str]] = set()
    for row in baseline:
        key = (row["title"]["status"], row["artist"]["status"], row["candidate_reason"])
        if key not in seen:
            seen.add(key)
            selected.append(row)
        if len(selected) >= limit:
            break
    return selected


def render_contact_sheet(
    representatives: list[dict[str, Any]], image_paths: dict[str, Path]
) -> Image.Image:
    if not representatives:
        sheet = Image.new("RGB", (1200, 100), "white")
        ImageDraw.Draw(sheet).text((20, 20), "No eligible representatives", fill="black")
        return sheet
    width, row_height = 1200, 210
    sheet = Image.new("RGB", (width, row_height * len(representatives)), "white")
    draw = ImageDraw.Draw(sheet)
    for index, row in enumerate(representatives):
        top = index * row_height
        with Image.open(image_paths[row["observation_id"]]) as opened:
            image = opened.convert("RGB")
        source = image.copy()
        source.thumbnail((320, 180), Image.Resampling.LANCZOS)
        title = image.crop(evaluation._scaled_roi(image, "title"))
        title.thumbnail((420, 80), Image.Resampling.LANCZOS)
        artist = image.crop(evaluation._scaled_roi(image, "artist"))
        artist.thumbnail((420, 80), Image.Resampling.LANCZOS)
        sheet.paste(source, (10, top + 20))
        sheet.paste(title, (350, top + 20))
        sheet.paste(artist, (350, top + 110))
        draw.text(
            (790, top + 20),
            "\n".join(
                (
                    row["observation_id"][:16],
                    f"title: {row['title']['status']} {row['title']['raw'][:42]}",
                    f"artist: {row['artist']['status']} {row['artist']['raw'][:42]}",
                    f"candidate: {row['candidate_reason']}",
                )
            ),
            fill="black",
        )
    return sheet


def write_reports(
    output_dir: Path,
    rows: list[dict[str, Any]],
    summary: dict[str, Any],
    *,
    image_paths: dict[str, Path],
    representative_limit: int,
) -> None:
    output_dir = output_dir.resolve()
    output_dir.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".ocr-diagnostics-", dir=output_dir.parent))
    try:
        (temporary / "ocr_diagnostics.csv").write_text(
            render_csv(rows), encoding="utf-8", newline="\n"
        )
        (temporary / "ocr_diagnostics.json").write_text(
            json.dumps({**summary, "rows": rows}, ensure_ascii=False, indent=2, sort_keys=True)
            + "\n",
            encoding="utf-8",
            newline="\n",
        )
        (temporary / "ocr_diagnostics.md").write_text(
            render_markdown(summary), encoding="utf-8", newline="\n"
        )
        sheet = render_contact_sheet(
            _representative_rows(rows, representative_limit), image_paths
        )
        sheet.save(temporary / "representative_contact_sheet.png", format="PNG")
        output_dir.mkdir(parents=True, exist_ok=True)
        for path in sorted(temporary.iterdir()):
            os.replace(path, output_dir / path.name)
    finally:
        shutil.rmtree(temporary, ignore_errors=True)


def _artifact_snapshots(
    review_rows: Iterable[dict[str, Any]],
) -> tuple[list[tuple[str, Path]], dict[Path, tuple[int, int, str]], Counter[str]]:
    images: list[tuple[str, Path]] = []
    fingerprints: dict[Path, tuple[int, int, str]] = {}
    skipped: Counter[str] = Counter()
    for row in review_rows:
        item = row["candidate_evaluation"]
        if row["stored_status"] != "unresolved":
            skipped[f"persisted_status_{row['stored_status']}"] += 1
            continue
        preview = item["jacket_preview_path"]
        if not preview:
            skipped[item["reason"]] += 1
            continue
        crop_path = Path(preview).resolve()
        manifest_path = crop_path.parent / "observation.json"
        source_path = crop_path.parent / "source.png"
        checkpoint_path = crop_path.parents[2] / "checkpoint.json"
        paths = (manifest_path, source_path, crop_path, checkpoint_path)
        try:
            for path in paths:
                fingerprints[path] = unresolved._file_fingerprint(path)
        except OSError:
            skipped["artifact_missing_after_validation"] += 1
            continue
        images.append((item["observation_id"], source_path))
    return images, fingerprints, skipped


def run_diagnostics(
    *,
    master_db: Path,
    catalog_db: Path,
    artifact_root: Path,
    output_dir: Path,
    representative_limit: int = DEFAULT_REPRESENTATIVE_LIMIT,
    extractor: Extractor = extract_profile,
    projection_builder: ProjectionBuilder = projection.build_review_projection,
) -> dict[str, Any]:
    artifact_root = evaluation._require_under_data(artifact_root, "artifact root")
    output_dir = evaluation._require_under_data(output_dir, "diagnostic output")
    if representative_limit < 0:
        raise ValueError("representative limit must be zero or greater")
    db_before = {
        "master": evaluation._sha256_file(master_db),
        "catalog": evaluation._sha256_file(catalog_db),
    }
    projected = projection_builder(
        catalog_db,
        master_db,
        artifact_root=artifact_root,
        extractor=_validation_extractor,
    )
    image_entries, fingerprints, skipped = _artifact_snapshots(projected["review_references"])
    executable, languages, probe_failure = inspect_tesseract()
    if executable is not None and not probe_failure:
        available_languages = {
            profile.language
            for profile in PROFILES
            if profile_availability(profile, executable, languages, probe_failure)[0]
        }
        for language in sorted(available_languages):
            tsv_failure = probe_tsv_contract(executable, language)
            if tsv_failure:
                raise ValueError(
                    f"tesseract profile preflight failed for {language}: {tsv_failure}"
                )
    master = catalog.load_master_identity(master_db)
    image_paths: dict[str, Path] = {}
    for observation_id, path in image_entries:
        image_paths[observation_id] = path

    def loaded_images() -> Iterable[tuple[str, Image.Image]]:
        for observation_id, path in image_entries:
            with Image.open(path) as opened:
                image = opened.convert("RGB")
            yield observation_id, image

    rows = evaluate_images(
        loaded_images(),
        master=master,
        executable=executable,
        languages=languages,
        probe_failure=probe_failure,
        extractor=extractor,
    )
    if any(unresolved._file_fingerprint(path) != before for path, before in fingerprints.items()):
        raise ValueError("artifact/checkpoint changed during OCR diagnostics")
    db_after = {
        "master": evaluation._sha256_file(master_db),
        "catalog": evaluation._sha256_file(catalog_db),
    }
    if db_before != db_after:
        raise ValueError("master/catalog changed during OCR diagnostics")
    summary = summarize(
        rows,
        total_references=len(projected["review_references"]),
        eligible_observations=len(image_entries),
        skipped_reasons=skipped,
        executable=executable,
        languages=languages,
        probe_failure=probe_failure,
    )
    write_reports(
        output_dir,
        rows,
        summary,
        image_paths=image_paths,
        representative_limit=representative_limit,
    )
    return {
        "receipt_schema_version": RECEIPT_SCHEMA_VERSION,
        "report_directory": str(output_dir.resolve()),
        "eligible_observations": len(image_entries),
        "installed_languages": sorted(languages),
        "unavailable_profiles": [
            profile["profile_id"] for profile in summary["profiles"] if not profile["available"]
        ],
    }


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Compare read-only title/artist OCR diagnostic profiles"
    )
    parser.add_argument("--master-db", required=True, type=Path)
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--artifact-root", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument(
        "--representative-limit", type=int, default=DEFAULT_REPRESENTATIVE_LIMIT
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        receipt = run_diagnostics(
            master_db=args.master_db,
            catalog_db=args.catalog,
            artifact_root=args.artifact_root,
            output_dir=args.output_dir,
            representative_limit=args.representative_limit,
        )
    except (OSError, ValueError) as exc:
        raise SystemExit(str(exc)) from exc
    print(json.dumps(receipt, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
