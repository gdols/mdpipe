# MarkItDown Version Control in MdPipe

MdPipe wraps [Microsoft MarkItDown](https://github.com/microsoft/markitdown), a Python library.
Python libraries introduce breaking changes between releases, so MdPipe never installs one that
has not been tried.

**A release of MdPipe is a pairing.** One application and one MarkItDown, tested together. Which
engine a copy of MdPipe installs is decided by the build it came from, and nothing else changes
it. If a new MarkItDown brings something worth having, that reaches people the way everything
else does: in a new release of MdPipe, once it has been looked at.

That is why there are two manifests doing two different jobs.

| | Answers | Comes from |
|---|---|---|
| Build manifest | which engine to install | the copy of `markitdown-compat.json` compiled into that build |
| Remote manifest | whether a newer MdPipe exists | the same file, fetched from this repository |

They are the same file in the repository, embedded at build time. The difference is *when* each
copy was read: the build's copy was frozen when the release was cut, the remote one is whatever
master says today.

The practical consequence, and the reason it is worth the extra indirection: **editing this
repository cannot change what is installed on somebody's machine.** It can only tell them that a
newer MdPipe is available. Setting up the engine also stops depending on GitHub being reachable,
since its instructions travel inside the executable.

## How it works

### 1. The manifest

A JSON file is hosted in this repository at `manifest/markitdown-compat.json`.
MdPipe fetches it from the GitHub raw URL at startup:

```
https://raw.githubusercontent.com/gdols/MdPipe/master/manifest/markitdown-compat.json
```

The manifest looks like this:

```json
{
  "schemaVersion": 2,
  "stableVersion": "0.1.7",
  "minimumVersion": "0.1.5",
  "compatibleVersions": ["0.1.5", "0.1.6", "0.1.7"],
  "updatedAt": "2026-08-05",
  "notes": "Validated on real PDF, DOCX and XLSX files",
  "app": {
    "latestVersion": "0.5.0",
    "releaseUrl": "https://github.com/gdols/MdPipe/releases/tag/v0.5.0",
    "downloadUrl": "https://github.com/gdols/MdPipe/releases/download/v0.5.0/MdPipe.exe",
    "criticalBelow": "",
    "notes": ""
  }
}
```

| Field | Purpose |
|---|---|
| `stableVersion` | The version installed when running `mdpipe setup` |
| `compatibleVersions` | The exact set of versions considered safe to run |
| `minimumVersion` | Informational; lowest validated version |
| `app` | What the newest MdPipe is, so an old copy can say so |

#### The `app` block

The portable executable had no way of finding out that a newer one existed. Most of the
downloads so far are on releases old enough to carry bugs that have since been fixed, and
nothing ever told those people a fix had shipped.

It rides in this manifest instead of asking the GitHub releases API because the manifest is
already fetched on every launch, already cached for a day and already has a baseline
underneath it, so the notice costs no extra request and behaves sensibly offline. The API
would also bring its limit of sixty requests an hour per address, which an office behind a
single NAT reaches on its own.

`criticalBelow` is for releases with a known problem: a copy older than that gets blunter
wording rather than a polite "an update is available". Leave it empty when no release is
that bad. `notes` is the one sentence the notice shows, so write it for the person reading
it, not as a commit list.

`downloadUrl` is the executable at its **versioned** address, never a "latest" one, which would
start pointing somewhere else the moment the next release went out and leave the published
checksum describing a different file.

#### Updating in place

If the person says yes, MdPipe replaces itself. It asks first, every time, and it checks its
work:

1. It fetches `downloadUrl` + `.sha256`, published beside the executable by the release
   workflow. **No checksum means no update.** Running something nobody verified is worse than
   sending the user to the browser, which at least brings SmartScreen along.
2. It downloads the executable and hashes it. A mismatch stops everything, with the running copy
   untouched.
3. Only then does anything move: the running executable is renamed to `MdPipe.exe.old`, the new
   one takes its place, and MdPipe restarts into it. If that second step fails, the old one goes
   back, because the one genuinely unrecoverable outcome is leaving no executable at all.
4. Windows will not let a running executable be deleted, only renamed, so `MdPipe.exe.old` is
   swept up on the next launch.

Where it cannot write to its own folder, which is a portable executable's normal condition on
read-only media or under Program Files, it does not offer any of this. It checks before
downloading anything and offers the release page instead.

The checksum protects against a truncated or corrupted download. It is not a signature, and it
does not pretend to be: it comes from the same place as the executable. Signing would need a
certificate.

**Older builds are unaffected.** The serializer does not reject unknown fields, so every copy
already downloaded goes on reading the manifest as before. This was checked by running the
manifest parsers from the v0.1.0, v0.2.0 and v0.4.0 tags against the schema 2 file: all three
read it without complaint.

### 2. The version gate

`SetupOrchestrator` compares what is installed against the version the build pins, and replaces
it when they differ, in either direction. Comparison is by version rather than by text, so an
engine recorded as `0.1.7` and reporting itself as `0.1.7.0` counts as a match. That detail is
load bearing: a target that could never equal what gets installed would reinstall several
hundred megabytes on every launch, forever.

An engine whose version cannot be parsed at all gets replaced, since something nobody can
identify is not what the release was tried with.

The CLI additionally refuses to convert with a version outside `compatibleVersions`:

```
Version gate blocked: MarkItDown 0.2.0 is not in the validated set.
Safe version: 0.1.1. Run 'mdpipe setup' to update.
```

### 3. Offline caching and the embedded baseline

The manifest is resolved through a small chain:

```
FallbackManifestProvider
  ├─ primary:  CachedManifestProvider → GitHubManifestProvider   (remote, 24h disk cache)
  └─ fallback: EmbeddedManifestProvider                          (baked-in baseline)
```

1. **Remote (cached):** the GitHub manifest is fetched and cached to disk for 24 hours.
2. **Embedded baseline:** a copy of `manifest/markitdown-compat.json` is **embedded into the
   build** as the offline fallback. If GitHub is unreachable (no internet, the repo isn't
   published yet, or a transient outage), MdPipe falls back to this baked-in manifest so it
   can still prepare a **known-good** MarkItDown version.

This means MdPipe works **out of the box on first run even without the remote manifest**.
The remote manifest's only job is to *advance* the validated set over time; it is never
required just to get started. The embedded file and the repo file are the same source. The
csproj embeds the repository manifest at build time, so there is a single source of truth.

### 4. Updating the manifest

Only the repository owner can advance the validated set, and users will **never** get an
untested MarkItDown through `pip install --upgrade`.

Noticing that there is something to validate used to be a matter of remembering to look, which
is how the format list drifted once already. `.github/workflows/markitdown-watch.yml` now does
it every Monday:

1. It compares PyPI against `compatibleVersions`.
2. If there is a version MdPipe has not validated, it installs that version and the current one
   side by side, converts everything in `tests/fixtures/` with both, compares the format lists
   the two report, and opens an issue with the differences.
3. It opens **one** issue per version. A second run while that issue is open says so and stops,
   rather than filing a duplicate every week.

It never edits the manifest. Accepting a version is still a decision:

1. Read the issue. A clean diff means the output did not change for those documents.
2. Add the version to `compatibleVersions` in `manifest/markitdown-compat.json`, and move
   `stableVersion` up.
3. Commit and push. That changes what the **next build** installs. Nobody's machine changes
   until they install an MdPipe carrying it, which is the whole point of the arrangement.

To reject one, say why in the issue and close it. That way the refusal is on the record too.

You can see the report without waiting for Monday: run the workflow by hand with
`pretend_current` set to an older version, or locally with

```bash
python .github/scripts/check_markitdown.py --pretend-current 0.1.6
```

The sample documents it converts are built by `tests/fixtures/make_fixtures.py`, from the
standard library alone, so they are neither unexplained binaries nor dependent on what happens
to be installed.

### 5. Releasing a new MdPipe

The version lives in exactly one place, `Directory.Build.props`, which both the desktop app
and the CLI inherit. Releasing means:

1. Bump `<Version>` in `Directory.Build.props`.
2. Set `app.latestVersion` in the manifest to the same number, with `releaseUrl` pointing at the
   tag, `downloadUrl` at that tag's `MdPipe.exe`, and a `notes` line saying what changed. Write
   that line for the person who will read it in the app, and put the same thing at the top of
   `CHANGELOG.md`. Getting `downloadUrl` wrong is not dangerous: the checksum will not match, the
   update will refuse, and the user gets sent to the browser.
3. Merge, tag `vX.Y.Z`, push the tag.
4. The workflow builds the executable and attaches it to a **draft** release. Write the notes
   and publish it.

**Do not merge step 2 ahead of the tag.** `app.latestVersion` is what every installed copy
reads within a day, so landing it before the release exists points all of them at a page that
is not there yet. Bump and tag close together.

The release workflow refuses to build unless the tag, `<Version>` and `app.latestVersion` all
agree, so it is not possible to publish a release that forgets to tell anyone about itself.

### 6. Why not just pin the version in a requirements.txt?

A `requirements.txt` pins for **your** environment. The manifest pins for **all users**,
with an explicit validation record, version history in git, and a human-readable audit trail.
It also lets you allow a range of compatible versions rather than a single exact pin.

