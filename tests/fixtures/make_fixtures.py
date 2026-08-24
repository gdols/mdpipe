"""Builds the sample documents the MarkItDown watch workflow converts.

The files next to this script are committed, so the weekly check has something stable to
convert with an old MarkItDown and a new one. They are generated rather than collected so
nobody has to wonder where a binary in a public repository came from, and they are built from
the standard library alone so regenerating them does not depend on whatever happens to be
installed.

    python tests/fixtures/make_fixtures.py

Every one of them holds the same handful of things a conversion can get wrong: a heading, a
paragraph, an accented word, and a two by two table. Small on purpose, since the point is to
notice that the Markdown changed, not to exercise the whole format.
"""

import zipfile
from pathlib import Path

HERE = Path(__file__).parent

TITLE = "MdPipe fixture"
PARAGRAPH = "Un párrafo con acentos, para ver si la codificación sobrevive."
TABLE = [["Format", "Reads"], ["PDF", "yes"]]


def write_pdf(path: Path) -> None:
    """A minimal PDF: one page, a few lines of text in the base Helvetica font."""
    lines = [TITLE, PARAGRAPH, " | ".join(TABLE[0]), " | ".join(TABLE[1])]

    # WinAnsi is what the base fonts use, and it covers the accented characters above.
    def escape(text: str) -> bytes:
        return text.encode("cp1252").replace(b"\\", b"\\\\").replace(b"(", b"\\(").replace(b")", b"\\)")

    content = b"BT\n/F1 12 Tf\n72 720 Td\n14 TL\n"
    for line in lines:
        content += b"(" + escape(line) + b") Tj\nT*\n"
    content += b"ET"

    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
        b"/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        b"<< /Length " + str(len(content)).encode() + b" >>\nstream\n" + content + b"\nendstream",
    ]

    out = bytearray(b"%PDF-1.4\n")
    offsets = []
    for number, body in enumerate(objects, start=1):
        offsets.append(len(out))
        out += f"{number} 0 obj\n".encode() + body + b"\nendobj\n"

    start = len(out)
    out += f"xref\n0 {len(objects) + 1}\n".encode() + b"0000000000 65535 f \n"
    for offset in offsets:
        out += f"{offset:010d} 00000 n \n".encode()
    out += (
        f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{start}\n".encode()
        + b"%%EOF\n"
    )

    path.write_bytes(bytes(out))


def _zip(path: Path, entries: dict[str, str]) -> None:
    """Office formats are zipped XML, so building one by hand needs no library at all.

    Every entry gets the same fixed timestamp. A zip normally stores the moment each file went
    in, which would make these come out different on every run and show up as a diff in a
    committed file that nobody actually changed. 1980-01-01 is the earliest a zip can express.
    """
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, text in entries.items():
            entry = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, text)


CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  {overrides}
</Types>"""

ROOT_RELS = """<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="{type}" Target="{target}"/>
</Relationships>"""


def write_docx(path: Path) -> None:
    w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"

    def para(text: str, style: str | None = None) -> str:
        properties = f'<w:pPr><w:pStyle w:val="{style}"/></w:pPr>' if style else ""
        return f"<w:p>{properties}<w:r><w:t xml:space='preserve'>{text}</w:t></w:r></w:p>"

    rows = "".join(
        "<w:tr>" + "".join(f"<w:tc><w:tcPr/>{para(cell)}</w:tc>" for cell in row) + "</w:tr>"
        for row in TABLE
    )

    document = (
        f"<?xml version='1.0' encoding='UTF-8'?>"
        f"<w:document xmlns:w='{w}'><w:body>"
        f"{para(TITLE, 'Heading1')}{para(PARAGRAPH)}"
        f"<w:tbl><w:tblPr/>{rows}</w:tbl>"
        f"</w:body></w:document>"
    )

    _zip(path, {
        "[Content_Types].xml": CONTENT_TYPES.format(overrides=
            '<Override PartName="/word/document.xml" ContentType="application/vnd.'
            'openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'),
        "_rels/.rels": ROOT_RELS.format(
            type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
            target="word/document.xml"),
        "word/document.xml": document,
    })


def write_xlsx(path: Path) -> None:
    ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
    rows = TABLE + [[TITLE, PARAGRAPH]]

    def column(index: int) -> str:
        return chr(ord("A") + index)

    body = "".join(
        f"<row r='{r}'>" + "".join(
            f"<c r='{column(c)}{r}' t='inlineStr'><is><t xml:space='preserve'>{value}</t></is></c>"
            for c, value in enumerate(row)
        ) + "</row>"
        for r, row in enumerate(rows, start=1)
    )

    _zip(path, {
        "[Content_Types].xml": CONTENT_TYPES.format(overrides=
            '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.'
            'openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
            '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.'
            'openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'),
        "_rels/.rels": ROOT_RELS.format(
            type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
            target="xl/workbook.xml"),
        "xl/_rels/workbook.xml.rels": ROOT_RELS.format(
            type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
            target="worksheets/sheet1.xml"),
        "xl/workbook.xml":
            f"<?xml version='1.0' encoding='UTF-8'?><workbook xmlns='{ns}' "
            f"xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'>"
            f"<sheets><sheet name='Fixture' sheetId='1' r:id='rId1'/></sheets></workbook>",
        "xl/worksheets/sheet1.xml":
            f"<?xml version='1.0' encoding='UTF-8'?><worksheet xmlns='{ns}'>"
            f"<sheetData>{body}</sheetData></worksheet>",
    })


def main() -> None:
    for name, build in [
        ("sample.pdf", write_pdf),
        ("sample.docx", write_docx),
        ("sample.xlsx", write_xlsx),
    ]:
        path = HERE / name
        build(path)
        print(f"{name:14} {path.stat().st_size:6} bytes")


if __name__ == "__main__":
    main()
