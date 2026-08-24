# MarkItDown Version Control in MdPipe

MdPipe wraps [Microsoft MarkItDown](https://github.com/microsoft/markitdown), a Python library.
Because Python libraries can introduce breaking changes between releases, MdPipe uses a
**compatibility manifest** to ensure it never automatically upgrades to an untested version.

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
    "latestVersion": "0.4.0",
    "releaseUrl": "https://github.com/gdols/MdPipe/releases/latest",
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

Nothing downloads or replaces anything. The executable is portable, it may be running from a
memory stick or a folder it cannot write to, and an unsigned binary that rewrites itself is
exactly the shape antivirus software looks for. The link opens the release page.

**Older builds are unaffected.** The serializer does not reject unknown fields, so every copy
already downloaded goes on reading the manifest as before. This was checked by running the
manifest parsers from the v0.1.0, v0.2.0 and v0.4.0 tags against the schema 2 file: all three
read it without complaint.

### 2. The version gate

Before any conversion, `VersionGateService` checks that the installed MarkItDown version
appears in `compatibleVersions`. If not, the command fails with a clear message:

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

Only the repository owner (you) can advance the validated set. The workflow is:

1. Install and test a new MarkItDown version manually.
2. Update `manifest/markitdown-compat.json`: add the version to `compatibleVersions`
   and optionally advance `stableVersion`.
3. Commit and push. Users get the update automatically within 24 hours.

This means users will **never** get an untested MarkItDown update via `pip install --upgrade`.

### 5. Releasing a new MdPipe

The version lives in exactly one place, `Directory.Build.props`, which both the desktop app
and the CLI inherit. Releasing means:

1. Bump `<Version>` in `Directory.Build.props`.
2. Set `app.latestVersion` in the manifest to the same number, with a `releaseUrl` pointing at
   the tag and a `notes` line saying what changed.
3. Merge, then tag `vX.Y.Z`.

The release workflow refuses to build unless the tag, `<Version>` and `app.latestVersion` all
agree, so it is not possible to publish a release that forgets to tell anyone about itself.

### 6. Why not just pin the version in a requirements.txt?

A `requirements.txt` pins for **your** environment. The manifest pins for **all users**,
with an explicit validation record, version history in git, and a human-readable audit trail.
It also lets you allow a range of compatible versions rather than a single exact pin.

