from __future__ import annotations

import os
import tempfile
import zipfile
from collections.abc import Iterable
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any
from xml.sax.saxutils import escape, quoteattr

XLSX_MIMETYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"
ZIP_TIMESTAMP = (1980, 1, 1, 0, 0, 0)
EMU_PER_CM = 360_000


def _utf8(value: str) -> bytes:
    return value.encode("utf-8")  # noqa: UP012


@dataclass(frozen=True)
class EmbeddedImage:
    """An image stored inside an XLSX package and rendered over one cell."""

    name: str
    data: bytes
    width_cm: float
    height_cm: float
    media_type: str = "image/png"


def _zip_info(name: str) -> zipfile.ZipInfo:
    info = zipfile.ZipInfo(name, ZIP_TIMESTAMP)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.create_system = 0
    info.external_attr = 0
    return info


def _validate_image(image: EmbeddedImage) -> None:
    parts = PurePosixPath(image.name).parts
    if (
        len(parts) < 2
        or parts[0] != "Pictures"
        or any(part in {"", ".", ".."} for part in parts)
        or "\\" in image.name
        or not image.data
        or image.media_type != "image/png"
        or not image.name.lower().endswith(".png")
        or image.width_cm <= 0
        or image.height_cm <= 0
    ):
        raise ValueError("embedded XLSX image has invalid package metadata")


def _embedded_images(
    sheets: list[tuple[str, list[str], list[list[Any]]]],
) -> tuple[EmbeddedImage, ...]:
    images: dict[str, EmbeddedImage] = {}
    for _name, headers, rows in sheets:
        values = [*headers, *[cell for row in rows for cell in row]]
        for value in values:
            if not isinstance(value, EmbeddedImage):
                continue
            _validate_image(value)
            previous = images.get(value.name)
            if previous is not None and previous != value:
                raise ValueError(f"XLSX contains conflicting embedded images: {value.name}")
            images[value.name] = value
    return tuple(images[name] for name in sorted(images))


def _column_name(index: int) -> str:
    result = ""
    while index >= 0:
        index, remainder = divmod(index, 26)
        result = chr(ord("A") + remainder) + result
        index -= 1
    return result


def _cell_reference(row_index: int, column_index: int) -> str:
    return f"{_column_name(column_index)}{row_index + 1}"


def _image_anchors(
    headers: list[str], rows: list[list[Any]]
) -> list[tuple[EmbeddedImage, int, int]]:
    return [
        (value, column_index, row_index)
        for row_index, row in enumerate([headers, *rows])
        for column_index, value in enumerate(row)
        if isinstance(value, EmbeddedImage)
    ]


def _cell(value: Any, *, reference: str, style: int = 0) -> str:
    style_attr = f' s="{style}"' if style else ""
    if isinstance(value, EmbeddedImage):
        return f'<c r="{reference}" t="inlineStr"{style_attr}><is><t/></is></c>'
    if value is None:
        return f'<c r="{reference}"{style_attr}/>'
    if isinstance(value, bool):
        raw = "1" if value else "0"
        return f'<c r="{reference}" t="b"{style_attr}><v>{raw}</v></c>'
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        raw = str(value)
        return f'<c r="{reference}" t="n"{style_attr}><v>{escape(raw)}</v></c>'
    raw = str(value)
    space_attr = ' xml:space="preserve"' if raw[:1].isspace() or raw[-1:].isspace() else ""
    return (
        f'<c r="{reference}" t="inlineStr"{style_attr}>'
        f"<is><t{space_attr}>{escape(raw)}</t></is></c>"
    )


def _sheet_columns(headers: list[str]) -> str:
    widths = [28.0, 66.0, 66.0, 16.0, 20.0, 32.0]
    columns = []
    for index in range(len(headers)):
        width = widths[index] if index < len(widths) else 20.0
        columns.append(
            f'<col min="{index + 1}" max="{index + 1}" width="{width:g}" '
            'bestFit="1" customWidth="1"/>'
        )
    return f"<cols>{''.join(columns)}</cols>" if columns else ""


def _sheet_xml(
    headers: list[str],
    rows: list[list[Any]],
    *,
    drawing_relationship: bool,
) -> tuple[bytes, list[tuple[EmbeddedImage, int, int]]]:
    all_rows = [headers, *rows]
    image_anchors: list[tuple[EmbeddedImage, int, int]] = []
    rendered_rows = []
    for row_index, row in enumerate(all_rows):
        rendered_cells = []
        for column_index, value in enumerate(row):
            if row_index == 0:
                style = 1
            elif headers[column_index] in {"status", "truth_song_id", "notes"}:
                style = 2
            elif headers[column_index] == "observation_id":
                style = 3
            else:
                style = 0
            rendered_cells.append(
                _cell(
                    value,
                    reference=_cell_reference(row_index, column_index),
                    style=style,
                )
            )
            if isinstance(value, EmbeddedImage):
                image_anchors.append((value, column_index, row_index))
        custom_height = ' ht="32" customHeight="1"' if row_index > 0 and any(
            isinstance(value, EmbeddedImage) for value in row
        ) else ""
        rendered_rows.append(
            f'<row r="{row_index + 1}"{custom_height}>{"".join(rendered_cells)}</row>'
        )
    max_row = max(1, len(all_rows))
    max_column = max(1, len(headers))
    dimension = f"A1:{_cell_reference(max_row - 1, max_column - 1)}"
    drawing = '<drawing r:id="rId1"/>' if drawing_relationship else ""
    status_column = next(
        (index for index, header in enumerate(headers) if header == "status"),
        None,
    )
    data_validation = ""
    if rows and status_column is not None:
        status_range = (
            f"{_cell_reference(1, status_column)}:"
            f"{_cell_reference(len(rows), status_column)}"
        )
        data_validation = (
            '<dataValidations count="1">'
            f'<dataValidation type="list" allowBlank="0" '
            f'showErrorMessage="1" showInputMessage="1" sqref={quoteattr(status_range)}>'
            '<formula1>"unreviewed,confirmed,rejected,hold"</formula1>'
            "</dataValidation></dataValidations>"
        )
    xml = f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
 <dimension ref="{dimension}"/>
 <sheetViews><sheetView workbookViewId="0"/></sheetViews>
 <sheetFormatPr defaultRowHeight="15"/>
 {_sheet_columns(headers)}
 <sheetData>{"".join(rendered_rows)}</sheetData>
 {data_validation}
 <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
 {drawing}
</worksheet>
'''
    return _utf8(xml), image_anchors


def _drawing_xml(anchors: list[tuple[EmbeddedImage, int, int]]) -> bytes:
    rendered = []
    for index, (image, column_index, row_index) in enumerate(anchors, start=1):
        _validate_image(image)
        relationship_id = f"rId{index}"
        width = round(image.width_cm * EMU_PER_CM)
        height = round(image.height_cm * EMU_PER_CM)
        name = image.name.replace("/", "-")
        rendered.append(
            f'''<xdr:oneCellAnchor>
 <xdr:from><xdr:col>{column_index}</xdr:col><xdr:colOff>0</xdr:colOff>
  <xdr:row>{row_index}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
 <xdr:ext cx="{width}" cy="{height}"/>
 <xdr:pic>
  <xdr:nvPicPr><xdr:cNvPr id="{index}" name={quoteattr(name)}/><xdr:cNvPicPr/></xdr:nvPicPr>
   <xdr:blipFill>
    <a:blip r:embed="{relationship_id}"/><a:stretch><a:fillRect/></a:stretch>
   </xdr:blipFill>
  <xdr:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr>
 </xdr:pic>
 <xdr:clientData/>
</xdr:oneCellAnchor>'''
        )
    xml = f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<xdr:wsDr
 xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
 xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
{"".join(rendered)}
</xdr:wsDr>
'''
    return _utf8(xml)


def _styles_xml() -> bytes:
    return b'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
 <numFmts count="0"/>
 <fonts count="2">
  <font><sz val="11"/><name val="Calibri"/></font>
  <font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font>
 </fonts>
 <fills count="4">
  <fill><patternFill patternType="none"/></fill>
  <fill><patternFill patternType="gray125"/></fill>
   <fill><patternFill patternType="solid"><fgColor rgb="FF1F4E78"/>
    <bgColor indexed="64"/></patternFill></fill>
   <fill><patternFill patternType="solid"><fgColor rgb="FFFFF2CC"/>
    <bgColor indexed="64"/></patternFill></fill>
 </fills>
 <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
 <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
 <cellXfs count="4">
  <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
  <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/>
  <xf numFmtId="0" fontId="0" fillId="3" borderId="0" xfId="0" applyFill="1"/>
  <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1">
   <alignment shrinkToFit="1" vertical="center"/>
  </xf>
 </cellXfs>
 <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
 <dxfs count="0"/>
  <tableStyles count="0" defaultTableStyle="TableStyleMedium2"
   defaultPivotStyle="PivotStyleMedium9"/>
</styleSheet>
'''


def _workbook_xml(sheet_names: list[str]) -> bytes:
    sheets = "".join(
        f'<sheet name={quoteattr(name)} sheetId="{index}" r:id="rId{index}"/>'
        for index, name in enumerate(sheet_names, start=1)
    )
    return _utf8(f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
 <sheets>{sheets}</sheets>
 <calcPr calcId="0"/>
</workbook>
''')


def _content_types_xml(sheet_count: int, drawing_indices: Iterable[int], has_images: bool) -> bytes:
    overrides = [
        '<Override PartName="/docProps/core.xml" '
        'ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>',
        '<Override PartName="/docProps/app.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>',
        '<Override PartName="/xl/workbook.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>',
        '<Override PartName="/xl/styles.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>',
    ]
    overrides.extend(
        f'<Override PartName="/xl/worksheets/sheet{index}.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
        for index in range(1, sheet_count + 1)
    )
    overrides.extend(
        f'<Override PartName="/xl/drawings/drawing{index}.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>'
        for index in drawing_indices
    )
    png_default = (
        '<Default Extension="png" ContentType="image/png"/>' if has_images else ""
    )
    return _utf8(f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
 <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
 <Default Extension="xml" ContentType="application/xml"/>
 {png_default}
 {"".join(overrides)}
</Types>
''')


def _root_relationships_xml() -> bytes:
    return b'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
 <Relationship Id="rId1"
  Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
  Target="xl/workbook.xml"/>
 <Relationship Id="rId2"
  Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"
  Target="docProps/core.xml"/>
 <Relationship Id="rId3"
  Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties"
  Target="docProps/app.xml"/>
</Relationships>
'''


def _workbook_relationships_xml(sheet_count: int) -> bytes:
    sheets = "".join(
        f'<Relationship Id="rId{index}" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
        f'Target="worksheets/sheet{index}.xml"/>'
        for index in range(1, sheet_count + 1)
    )
    return _utf8(f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
 {sheets}
 <Relationship Id="rId{sheet_count + 1}"
  Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"
  Target="styles.xml"/>
</Relationships>
''')


def _worksheet_relationships_xml(drawing_index: int) -> bytes:
    return _utf8(f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
 <Relationship Id="rId1"
  Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"
  Target="../drawings/drawing{drawing_index}.xml"/>
</Relationships>
''')


def _drawing_relationships_xml(anchors: list[tuple[EmbeddedImage, int, int]]) -> bytes:
    relationships = "".join(
        f'<Relationship Id="rId{index}" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" '
        f'Target="../media/{PurePosixPath(image.name).name}"/>'
        for index, (image, _column_index, _row_index) in enumerate(anchors, start=1)
    )
    return _utf8(f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
 {relationships}
</Relationships>
''')


def _core_properties_xml() -> bytes:
    return b'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties
 xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
 xmlns:dc="http://purl.org/dc/elements/1.1/"
 xmlns:dcterms="http://purl.org/dc/terms/"
 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
 <dc:creator>ddrgp_scorelog</dc:creator>
</cp:coreProperties>
'''


def _app_properties_xml(sheet_names: list[str]) -> bytes:
    titles = "".join(f"<vt:lpstr>{escape(name)}</vt:lpstr>" for name in sheet_names)
    return _utf8(f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
 xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
 <Application>ddrgp_scorelog</Application>
 <HeadingPairs><vt:vector size="2" baseType="variant">
  <vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant>
  <vt:variant><vt:i4>{len(sheet_names)}</vt:i4></vt:variant>
 </vt:vector></HeadingPairs>
 <TitlesOfParts>
  <vt:vector size="{len(sheet_names)}" baseType="lpstr">{titles}</vt:vector>
 </TitlesOfParts>
</Properties>
''')


def write_xlsx(
    path: Path,
    sheets: list[tuple[str, list[str], list[list[Any]]]],
) -> None:
    """Write a deterministic XLSX workbook, replacing an existing file atomically."""
    path = path.resolve()
    if path.suffix.lower() != ".xlsx":
        raise ValueError("manual review export must use the .xlsx extension")
    path.parent.mkdir(parents=True, exist_ok=True)
    images = _embedded_images(sheets)
    entries: dict[str, bytes] = {}
    drawing_indices: list[int] = []
    sheet_names = [name for name, _headers, _rows in sheets]
    for index, (_name, headers, rows) in enumerate(sheets, start=1):
        anchors = _image_anchors(headers, rows)
        worksheet, anchors = _sheet_xml(
            headers,
            rows,
            drawing_relationship=bool(anchors),
        )
        entries[f"xl/worksheets/sheet{index}.xml"] = worksheet
        if anchors:
            drawing_indices.append(index)
            entries[f"xl/worksheets/_rels/sheet{index}.xml.rels"] = (
                _worksheet_relationships_xml(index)
            )
            entries[f"xl/drawings/drawing{index}.xml"] = _drawing_xml(anchors)
            entries[f"xl/drawings/_rels/drawing{index}.xml.rels"] = (
                _drawing_relationships_xml(anchors)
            )
    entries["[Content_Types].xml"] = _content_types_xml(
        len(sheets), drawing_indices, bool(images)
    )
    entries["_rels/.rels"] = _root_relationships_xml()
    entries["xl/workbook.xml"] = _workbook_xml(sheet_names)
    entries["xl/_rels/workbook.xml.rels"] = _workbook_relationships_xml(len(sheets))
    entries["xl/styles.xml"] = _styles_xml()
    entries["docProps/core.xml"] = _core_properties_xml()
    entries["docProps/app.xml"] = _app_properties_xml(sheet_names)
    for image in images:
        entries[f"xl/media/{PurePosixPath(image.name).name}"] = image.data

    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".tmp", dir=path.parent
    )
    os.close(descriptor)
    temporary = Path(temporary_name)
    try:
        with zipfile.ZipFile(temporary, "w") as archive:
            for name in sorted(entries):
                archive.writestr(_zip_info(name), entries[name])
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
