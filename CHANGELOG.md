# Changelog

What changed in each release, in plain terms. The one-line summary that MdPipe itself shows
when a newer version exists comes from `app.notes` in
[the manifest](manifest/markitdown-compat.json), so it is worth writing that line here first
and copying it across.

## 0.7.1

- **Fixed: a first run that failed on machines with Python 3.14.** MdPipe accepted any system Python
  from 3.10 upwards and built its environment on it, but MarkItDown's own dependencies do not go
  that high yet, so the install died partway through with a wall of pip output. The bundled Python
  that would have worked was sitting there unused. MdPipe now ignores a system Python it cannot
  install on, and uses its own. Anyone already stuck this way is repaired on the next launch,
  without having to do anything.
- If the install fails anyway on a system Python, MdPipe now retries with the one it brings, which
  covers the next version clash rather than only this one.
- The message when that happens says what actually went wrong. It used to suggest checking the
  proxy, the firewall and the antivirus, which was the wrong place to look entirely.

## 0.7.0

Nothing here changes what MdPipe does. It is the round after shipping a release that would not
open, and it is all about that not happening again.

- **A release that cannot open can no longer be published.** The release now starts the executable
  it just built and waits for its window, before anything is attached to it.
- **The conversion engine is tested.** It had six tests and all six covered a helper that trims a
  string; what a worker dying halfway through a batch does was taken on trust. Twelve tests now
  cover it, driven by a worker that misbehaves on request.
- **The environment manager is tested**, for the first time. It is the file every failure reported
  from other people's machines has come out of.
- **A broken format discovery no longer passes for an empty one.** MarkItDown offers no supported
  way to list what it reads, so MdPipe reaches for an internal detail that any release could
  change. That failure used to be silent and replace a good list with an empty one; now it is
  reported and the list already recorded is kept.
- The window tests catch a binding that does not resolve, not just one that throws.

## 0.6.1

- **Fixes 0.6.0, which would not open.** The window failed to build and the app showed an error
  box instead of starting. One binding in the update bar pointed a two-way binding at a read-only
  property, which WPF refuses at the moment the window is laid out. Nothing else was wrong with
  0.6.0, but nothing else mattered either.
- The windows are now built and laid out in the tests, so a release that cannot open cannot get
  out again.

## 0.6.0

- **MdPipe can update itself, if you say yes.** The notice now offers to do the work: it fetches
  the new version, checks it against the published checksum, replaces itself and restarts. It
  asks first, every time, and where it cannot write to its own folder it opens the release page
  instead of failing halfway.
- **The engine no longer changes on its own.** Each release of MdPipe now installs the exact
  MarkItDown version it was tested with, taken from the executable rather than fetched from the
  repository. A new engine arrives when a new MdPipe does. Setting it up also stopped depending
  on GitHub being reachable.

## 0.5.0

- **Dropping a folder no longer freezes the window.** The scan used to walk the whole tree on
  the interface thread: `Documents` took 4.6 seconds on the machine this was measured on, a
  folder of projects 12.5, and for all of it the window said "Not responding". It now runs in
  the background, shows how many files it has found and can be stopped, keeping whatever it
  had already collected.
- **Files dropped during the first run are no longer thrown away.** The first launch spends
  minutes downloading Python, and anything dropped in the meantime silently vanished.
- **A slow network can no longer break an install that works.** A proxy that accepted the
  connection and then said nothing left MdPipe showing "Setup failed" on machines where the
  engine was installed and perfectly usable, because a timeout is reported differently from a
  failed request and the offline fallback never got its turn.
- **Opening the app is about two and a half times faster.** It was starting three Python
  interpreters on every launch to learn nothing new. Measured end to end: 1199 ms to 442 ms.
- **MdPipe now says when a newer MdPipe exists.** The portable executable had no way of
  finding out, so people stayed on old versions carrying bugs that had already been fixed.
  Nothing downloads or replaces itself; there is a dismissible bar with a link, and a line in
  `mdpipe status`.
- A weekly job now watches PyPI for new MarkItDown releases, converts sample documents with
  the new version and the validated one, and opens an issue with the differences.

## 0.4.0

- Batches are three to eight times faster: one engine process now serves a whole batch instead
  of paying two seconds of start-up per file. Five files went from 13.8 s to 4, twenty from
  about 55 s to 6.5.
- A "What can it convert?" button, listing the formats read from the engine installed on that
  computer rather than from a list written by hand. The hand-written one had already drifted.
- "Try every file" mode, letting the engine decide by content for files with a wrong or missing
  extension.
- The interface follows the Windows display language: Spanish on a Spanish machine, English
  everywhere else.
- Files dropped onto `MdPipe.exe`, or sent to it with "Open with", are picked up. They used to
  do nothing at all.
- A per-file timeout, so one pathological document can no longer hang a conversion forever.

## 0.3.0

- The CLI takes several inputs at once: files, folders and patterns, with `--recursive`.
- A failed file no longer stops the rest, and the exit code tells a script whether everything
  worked.
- Fixed: converting a folder tree into one output folder silently overwrote same-named files,
  so `2025\report.pdf` and `2026\report.pdf` both became `report.md`. This affected the desktop
  app too.

## 0.2.0

- Real progress during the first-run download, and a Cancel button for conversions.
- Whole folders can be dropped on the window, subfolders included.
- The output folder is remembered between sessions.
- Fixed: the system proxy was detected but never passed to the downloader.
- Fixed: reinstalling failed quietly on read-only files.

## 0.1.2

- Fixed: on a machine with a very old Python, MdPipe built on top of it and setup failed with a
  confusing "no matching distribution found". A system Python older than 3.10 is now ignored in
  favour of the bundled one.

## 0.1.1

- Fixed: first-run setup failed behind a corporate proxy, because pip does not pick up the
  Windows proxy on its own.
- Setup failures now say what actually went wrong instead of blaming the connection.

## 0.1.0

First release. Portable Windows executable and a CLI, converting documents to Markdown through
Microsoft MarkItDown, with no installer and no .NET or Python needed.
