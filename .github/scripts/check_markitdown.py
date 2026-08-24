"""Checks whether MarkItDown has released a version MdPipe has not validated yet.

Nothing watched PyPI before, so the compatibility manifest stayed current only because
somebody remembered to look. The list of readable formats had already drifted once for exactly
that reason.

This does not decide anything. It installs the new version alongside the validated one,
converts the sample documents with both, and reports what changed. Advancing stableVersion is
still a person's call; the point is that the call gets made instead of forgotten.

Run by .github/workflows/markitdown-watch.yml, or by hand:

    python .github/scripts/check_markitdown.py            # look at the real manifest
    python .github/scripts/check_markitdown.py --pretend-current 0.1.6
"""

from __future__ import annotations

import argparse
import difflib
import json
import os
import subprocess
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "manifest" / "markitdown-compat.json"
FIXTURES = ROOT / "tests" / "fixtures"
WORKER = ROOT / "src" / "MdPipe.Infrastructure" / "MarkItDown" / "worker.py"
PYPI = "https://pypi.org/pypi/markitdown/json"


def latest_on_pypi() -> str:
    """The newest stable release. PyPI's info.version already leaves pre-releases out."""
    with urllib.request.urlopen(PYPI, timeout=30) as response:
        return json.load(response)["info"]["version"]


def run(*command: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")


def install(version: str) -> Path:
    """Puts one MarkItDown version in a virtual environment of its own and returns its python."""
    env = ROOT / ".watch" / version
    subprocess.run([sys.executable, "-m", "venv", str(env)], check=True)

    python = env / ("Scripts" if os.name == "nt" else "bin") / ("python.exe" if os.name == "nt" else "python")
    subprocess.run(
        [str(python), "-m", "pip", "install", "--quiet", "--upgrade", "pip"], check=True)
    subprocess.run(
        [str(python), "-m", "pip", "install", "--quiet", f"markitdown[all]=={version}"], check=True)
    return python


CONVERT = """
import sys
from markitdown import MarkItDown
try:
    print(MarkItDown().convert(sys.argv[1]).markdown)
except Exception as exc:
    print(f"CONVERSION FAILED: {type(exc).__name__}: {exc}")
"""


def convert(python: Path, document: Path) -> str:
    result = run(str(python), "-c", CONVERT, str(document))
    return result.stdout.strip() if result.returncode == 0 else f"PROCESS FAILED: {result.stderr.strip()}"


def formats(python: Path) -> set[str]:
    """Reuses the discovery mode MdPipe itself ships, so this measures what the app would see."""
    result = run(str(python), str(WORKER), "--formats")
    for line in result.stdout.splitlines():
        if line.strip().startswith("{"):
            return set(json.loads(line).get("extensions", []))
    return set()


def report(current: str, new: str, python_old: Path, python_new: Path) -> tuple[str, bool]:
    """Builds the issue body, and says whether anything actually differs."""
    lines = [
        f"MarkItDown **{new}** is on PyPI. MdPipe validates up to **{current}**.",
        "",
        "Everything below was produced by installing both versions and running them against "
        "`tests/fixtures/`. It is evidence, not a decision.",
        "",
        "## Formats",
        "",
    ]

    old_formats, new_formats = formats(python_old), formats(python_new)
    gained, lost = sorted(new_formats - old_formats), sorted(old_formats - new_formats)

    if gained or lost:
        if gained:
            lines.append(f"- Gained: {', '.join('`' + e + '`' for e in gained)}")
        if lost:
            lines.append(f"- **Lost**: {', '.join('`' + e + '`' for e in lost)}")
    else:
        lines.append(f"No change. Both read the same {len(new_formats)} extensions.")

    lines += ["", "## Conversion output", ""]

    changed = bool(gained or lost)
    for document in sorted(FIXTURES.glob("sample.*")):
        before = convert(python_old, document)
        after = convert(python_new, document)

        if before == after:
            lines.append(f"- `{document.name}`: identical")
            continue

        changed = True
        diff = "\n".join(difflib.unified_diff(
            before.splitlines(), after.splitlines(),
            fromfile=f"{document.name} ({current})", tofile=f"{document.name} ({new})", lineterm=""))
        lines += [
            f"- `{document.name}`: **differs**",
            "",
            "<details><summary>diff</summary>",
            "",
            "```diff",
            diff[:6000],
            "```",
            "",
            "</details>",
            "",
        ]

    lines += [
        "",
        "## To accept it",
        "",
        "Add the version to `compatibleVersions` in `manifest/markitdown-compat.json` and move "
        "`stableVersion` up. Every installed copy picks it up within a day.",
        "",
        "To reject it, say why here and close this. The refusal is then on the record, which is "
        "the part that was missing before.",
    ]

    return "\n".join(lines), changed


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pretend-current", help="test this script by claiming an older validated set")
    args = parser.parse_args()

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    validated = set(manifest["compatibleVersions"])
    current = manifest["stableVersion"]

    if args.pretend_current:
        current = args.pretend_current
        validated = {v for v in validated if v <= current}

    new = latest_on_pypi()
    print(f"PyPI: {new}   validated: {sorted(validated)}")

    output = Path(os.environ["GITHUB_OUTPUT"]) if "GITHUB_OUTPUT" in os.environ else None

    if new in validated:
        print("Nothing to do.")
        if output:
            output.write_text("found=false\n", encoding="utf-8")
        return 0

    body, changed = report(current, new, install(current), install(new))
    (ROOT / "watch-report.md").write_text(body, encoding="utf-8")

    print(body)
    if output:
        output.write_text(f"found=true\nversion={new}\nchanged={str(changed).lower()}\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
