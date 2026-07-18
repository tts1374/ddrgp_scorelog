from __future__ import annotations

import os
import tempfile
import zipfile
from collections.abc import Iterable
from pathlib import Path
from typing import Any
from xml.sax.saxutils import escape, quoteattr

ODS_MIMETYPE = "application/vnd.oasis.opendocument.spreadsheet"
ZIP_TIMESTAMP = (1980, 1, 1, 0, 0, 0)


def _zip_info(name: str, *, stored: bool = False) -> zipfile.ZipInfo:
    info = zipfile.ZipInfo(name, ZIP_TIMESTAMP)
    info.compress_type = zipfile.ZIP_STORED if stored else zipfile.ZIP_DEFLATED
    info.create_system = 0
    info.external_attr = 0
    return info


def _cell(value: Any, *, style: str = "") -> str:
    style_attr = f" table:style-name={quoteattr(style)}" if style else ""
    if value is None:
        return f"<table:table-cell{style_attr}/>"
    if isinstance(value, bool):
        raw = "true" if value else "false"
        return (
            f"<table:table-cell office:value-type=\"boolean\" office:boolean-value={quoteattr(raw)}"
            f"{style_attr}><text:p>{raw}</text:p></table:table-cell>"
        )
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        raw = str(value)
        return (
            f"<table:table-cell office:value-type=\"float\" office:value={quoteattr(raw)}"
            f"{style_attr}><text:p>{escape(raw)}</text:p></table:table-cell>"
        )
    raw = str(value)
    return (
        f"<table:table-cell office:value-type=\"string\"{style_attr}>"
        f"<text:p>{escape(raw)}</text:p></table:table-cell>"
    )


def _sheet(name: str, headers: list[str], rows: Iterable[list[Any]]) -> str:
    header = "".join(_cell(value, style="header") for value in headers)
    body = []
    editable = {"truth_song_id", "notes"}
    for row in rows:
        cells = []
        for index, value in enumerate(row):
            style = "input" if headers[index] in editable else ""
            cells.append(_cell(value, style=style))
        body.append(f"<table:table-row>{''.join(cells)}</table:table-row>")
    return (
        f"<table:table table:name={quoteattr(name)}>"
        f"<table:table-row>{header}</table:table-row>"
        f"{''.join(body)}"
        "</table:table>"
    )


def _content_xml(sheets: list[tuple[str, list[str], list[list[Any]]]]) -> bytes:
    rendered = "".join(_sheet(name, headers, rows) for name, headers, rows in sheets)
    xml = f"""<?xml version="1.0" encoding="UTF-8"?>
<office:document-content
 xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
 xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
 xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
 xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
 xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"
 office:version="1.3">
 <office:automatic-styles>
  <style:style style:name="header" style:family="table-cell">
   <style:table-cell-properties fo:background-color="#1F4E78"/>
   <style:text-properties fo:color="#FFFFFF" fo:font-weight="bold"/>
  </style:style>
  <style:style style:name="input" style:family="table-cell">
   <style:table-cell-properties fo:background-color="#FFF2CC"/>
  </style:style>
 </office:automatic-styles>
 <office:body><office:spreadsheet>{rendered}</office:spreadsheet></office:body>
</office:document-content>
"""
    return xml.encode("utf-8")


def _manifest_xml() -> bytes:
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<manifest:manifest
 xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"
 manifest:version="1.3">
 <manifest:file-entry manifest:full-path="/" manifest:media-type="{ODS_MIMETYPE}"/>
 <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
 <manifest:file-entry manifest:full-path="styles.xml" manifest:media-type="text/xml"/>
 <manifest:file-entry manifest:full-path="meta.xml" manifest:media-type="text/xml"/>
</manifest:manifest>
""".encode()


def _styles_xml() -> bytes:
    return b"""<?xml version="1.0" encoding="UTF-8"?>
<office:document-styles
 xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
 office:version="1.3">
 <office:styles/>
</office:document-styles>
"""


def _meta_xml() -> bytes:
    return b"""<?xml version="1.0" encoding="UTF-8"?>
<office:document-meta
 xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
 office:version="1.3">
 <office:meta/>
</office:document-meta>
"""


def write_ods(
    path: Path,
    sheets: list[tuple[str, list[str], list[list[Any]]]],
) -> None:
    path = path.resolve()
    if path.suffix.lower() != ".ods":
        raise ValueError("manual review export must use the .ods extension")
    if path.exists():
        raise ValueError(f"manual review export already exists: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".tmp", dir=path.parent
    )
    os.close(descriptor)
    temporary = Path(temporary_name)
    try:
        with zipfile.ZipFile(temporary, "w") as archive:
            archive.writestr(_zip_info("mimetype", stored=True), ODS_MIMETYPE.encode("ascii"))
            archive.writestr(_zip_info("content.xml"), _content_xml(sheets))
            archive.writestr(_zip_info("styles.xml"), _styles_xml())
            archive.writestr(_zip_info("meta.xml"), _meta_xml())
            archive.writestr(_zip_info("META-INF/manifest.xml"), _manifest_xml())
        if path.exists():
            raise ValueError(f"manual review export already exists: {path}")
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
