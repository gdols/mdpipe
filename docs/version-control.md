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
  "schemaVersion": 1,
  "stableVersion": "0.1.1",
  "minimumVersion": "0.1.0",
  "compatibleVersions": ["0.1.0", "0.1.1"],
  "updatedAt": "2026-06-30",
  "notes": "Validated against MdPipe 1.0.0"
}
```

| Field | Purpose |
|---|---|
| `stableVersion` | The version installed when running `mdpipe setup` |
| `compatibleVersions` | The exact set of versions considered safe to run |
| `minimumVersion` | Informational; lowest validated version |

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

### 5. Why not just pin the version in a requirements.txt?

A `requirements.txt` pins for **your** environment. The manifest pins for **all users**,
with an explicit validation record, version history in git, and a human-readable audit trail.
It also lets you allow a range of compatible versions rather than a single exact pin.

