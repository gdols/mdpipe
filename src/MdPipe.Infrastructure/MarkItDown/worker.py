"""MdPipe conversion worker.

Importing MarkItDown costs about two seconds, and converting a small document costs about a
tenth of that. Paying the import once per batch instead of once per file is the whole point of
this script: MdPipe starts it, writes one file path per line to stdin, and reads one JSON object
per line from stdout.

    {"path": "C:\\docs\\report.pdf", "ok": true,  "markdown": "..."}
    {"path": "C:\\docs\\broken.xlsx", "ok": false, "error": "File is not a zip file"}

JSON because converted Markdown can contain anything at all, including newlines and quotes, and
escaping that correctly is a solved problem not worth re-solving with a delimiter of our own.

Every conversion is wrapped, so a document that blows up is reported and the next one still runs.
Only a hard crash of the interpreter itself ends the loop, and MdPipe restarts the worker when it
sees the pipe close.
"""

import json
import sys


def emit(payload):
    """Write one result object and push it out immediately, so MdPipe can show progress."""
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def one_line(exc):
    """Boil an exception down to a single useful line.

    MarkItDown wraps converter failures in a multi-line summary whose last line holds the actual
    cause. A batch listing becomes unreadable if every failure spills over several lines, so we
    keep the part that says what actually went wrong.
    """
    text = str(exc).strip()
    if not text:
        return type(exc).__name__

    cleaned = [ln.strip().lstrip("- ").strip() for ln in text.splitlines()]
    cleaned = [ln for ln in cleaned if ln]
    return cleaned[-1] if cleaned else type(exc).__name__


def main():
    # MdPipe sets PYTHONIOENCODING, but being explicit costs nothing and keeps accented text intact
    # even if the worker is ever run by hand.
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    sys.stdin.reconfigure(encoding="utf-8")

    try:
        from markitdown import MarkItDown
    except Exception as exc:  # noqa: BLE001 - anything here means the environment is unusable
        emit({"path": "", "ok": False, "error": f"MarkItDown could not be loaded: {exc}"})
        return 1

    converter = MarkItDown()

    for line in sys.stdin:
        path = line.strip()
        if not path:
            continue

        try:
            result = converter.convert(path)
            emit({"path": path, "ok": True, "markdown": result.text_content})
        except Exception as exc:  # noqa: BLE001 - one bad document must not end the batch
            emit({"path": path, "ok": False, "error": one_line(exc)})

    return 0


if __name__ == "__main__":
    sys.exit(main())
