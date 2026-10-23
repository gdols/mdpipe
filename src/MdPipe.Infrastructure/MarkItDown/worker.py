"""MdPipe conversion worker.

Importing MarkItDown costs about two seconds, and converting a small document costs about a
tenth of that. Paying the import once per batch instead of once per file is the whole point of
this script: MdPipe starts it, writes one file path per line to stdin, and reads one JSON object
per line from stdout.

    {"path": "C:\\docs\\report.pdf", "ok": true,  "markdown": "..."}
    {"path": "C:\\docs\\broken.xlsx", "ok": false, "error": "File is not a zip file"}

JSON because converted Markdown can contain anything, newlines and quotes included.

Run with --formats it does a different job: it reports what the installed MarkItDown can read, so
MdPipe never has to keep a hand-written copy of somebody else's capabilities.

Every conversion is wrapped, so a document that blows up is reported and the next one still runs.
Only a hard crash ends the loop, and MdPipe restarts the worker when it sees the pipe close.
"""

import json
import sys

# MarkItDown transcribes audio with speech_recognition's recognize_google, which uploads it to
# Google. Everything else MdPipe does stays on the computer, so transcription is switched off.
NO_TRANSCRIPTION = (
    "MdPipe doesn't transcribe audio: MarkItDown would upload it to Google to do it, "
    "and MdPipe keeps your files on this computer."
)

# Converters whose output MdPipe can't stand behind, so folder scans and the formats list leave
# their extensions out. Audio, for the reason above. Images, because without OCR, exiftool or an
# LLM, MarkItDown gets nothing out of them. A file named explicitly is still tried.
LEFT_OUT = {"AudioConverter", "ImageConverter"}


class AudioNotTranscribed(Exception):
    pass


def switch_off_transcription():
    try:
        from markitdown.converters import _audio_converter
    except ImportError:
        return

    def refuse(*args, **kwargs):
        raise AudioNotTranscribed(NO_TRANSCRIPTION)

    _audio_converter.transcribe_audio = refuse


def emit(payload):
    """Write one result object and push it out immediately, so MdPipe can show progress."""
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def one_line(exc):
    """Boil an exception down to a single useful line.

    MarkItDown wraps converter failures in a multi-line summary whose last line holds the actual
    cause, and a batch listing is unreadable if every failure spills over several lines.
    """
    text = str(exc).strip()
    if not text:
        return type(exc).__name__

    cleaned = [ln.strip().lstrip("- ").strip() for ln in text.splitlines()]
    cleaned = [ln for ln in cleaned if ln]
    return cleaned[-1] if cleaned else type(exc).__name__


def describe_formats():
    """Report what this MarkItDown build can read.

    Held together with tape on purpose. There is no public way to list the converters, hence the
    private _converters, and the extension constants are not named consistently (most modules use
    ACCEPTED_FILE_EXTENSIONS, the spreadsheet ones use ACCEPTED_XLSX_FILE_EXTENSIONS), hence the
    match on the suffix. Either can break in any release, which is why an empty answer is an error
    rather than a catalogue with nothing in it: the caller cannot tell a broken engine from an
    honest zero, but it can act on an error and keep what it already knew.
    """
    from markitdown import MarkItDown

    try:
        from importlib.metadata import version as pkg_version
        engine = pkg_version("markitdown")
    except Exception:  # noqa: BLE001 - the version is nice to have, not essential
        engine = "unknown"

    try:
        registrations = MarkItDown()._converters
    except AttributeError as exc:
        emit({"error": f"This MarkItDown does not expose its converters the way MdPipe reads them: {exc}"})
        return 1

    converters, every = [], set()
    for registration in registrations:
        converter = registration.converter
        if type(converter).__name__ in LEFT_OUT:
            continue
        module = sys.modules.get(type(converter).__module__)
        found = set()

        for name in dir(module):
            if not name.endswith("FILE_EXTENSIONS"):
                continue
            value = getattr(module, name)
            if isinstance(value, (list, tuple, set)):
                found.update(v for v in value if isinstance(v, str) and v.startswith("."))

        if found:
            converters.append({"name": type(converter).__name__, "extensions": sorted(found)})
            every.update(found)

    if not every:
        emit({"error": (
            f"MarkItDown {engine} reported {len(registrations)} converters and not one readable "
            "extension, so the way MdPipe reads them has probably stopped working.")})
        return 1

    emit({"engineVersion": engine, "extensions": sorted(every), "converters": converters})
    return 0


def main():
    # MdPipe sets PYTHONIOENCODING, but being explicit keeps accented text intact if the worker is
    # ever run by hand. errors="replace" is the seat belt: a decode error in the loop below would be
    # raised outside every try in this file and kill the interpreter mid-batch.
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    sys.stdin.reconfigure(encoding="utf-8", errors="replace")

    try:
        from markitdown import MarkItDown
    except Exception as exc:  # noqa: BLE001 - anything here means the environment is unusable
        emit({"path": "", "ok": False, "error": f"MarkItDown could not be loaded: {exc}"})
        return 1

    if "--formats" in sys.argv:
        return describe_formats()

    switch_off_transcription()
    converter = MarkItDown()

    for line in sys.stdin:
        path = line.strip()
        if not path:
            continue

        try:
            result = converter.convert(path)
            emit({"path": path, "ok": True, "markdown": result.text_content})
        except Exception as exc:  # noqa: BLE001 - one bad document must not end the batch
            # MarkItDown wraps what a converter raised in its own exception, so look at the text.
            error = NO_TRANSCRIPTION if "AudioNotTranscribed" in str(exc) else one_line(exc)
            emit({"path": path, "ok": False, "error": error})

    return 0


if __name__ == "__main__":
    sys.exit(main())
