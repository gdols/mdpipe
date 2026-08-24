# The winget package

`winget install Gdols.MdPipe` gets people MdPipe, and more to the point `winget upgrade` keeps
it current for them without anyone having to notice a new release exists. It costs nothing,
does not require the binary to be signed (the manifest carries a SHA256 instead), and it is a
second way of being found: `winget search markdown` starts returning MdPipe.

The three files here are the **initial submission**. After that, winget's own copy in
[microsoft/winget-pkgs][pkgs] is the source of truth, and the release workflow bumps it.

## One-time setup

Two steps, both of which have to be done by the account that owns the package. Until then the
release workflow skips the winget step entirely, so nothing breaks by leaving this undone.

**1. Submit the package.** With [wingetcreate][wc] installed:

```
wingetcreate submit --token <a GitHub PAT with public_repo> packaging/winget
```

That opens a pull request against [microsoft/winget-pkgs][pkgs]. The first one is reviewed by
a person, and an unsigned portable executable can attract extra scrutiny, so expect it to take
a few days and possibly a question or two. If it stalls, nothing else depends on it: the update
notice built into the app already covers the people who need to hear about a new version.

**2. Add the token as a repository secret** named `WINGET_TOKEN`, so releases from then on
update the package by themselves. A classic PAT with `public_repo` is enough.

**3. Once the package is live**, add it to the download section of the main README:

````markdown
If you would rather have updates handled for you, install it with winget instead
and `winget upgrade` will keep it current:

```bash
winget install Gdols.MdPipe
```
````

Deliberately not there yet. A README that tells people to run a command which answers "no
package found" is worse than one that says nothing about winget at all.

## Keeping the files here current

They are only needed for the first submission, so they are not part of the release checklist. If
they ever need refreshing:

```
winget validate --manifest packaging/winget
```

`InstallerSha256` is the SHA256 of the exe attached to that release:

```
(Get-FileHash MdPipe.exe -Algorithm SHA256).Hash
```

## Notes on the manifest

- `InstallerType: portable`, because MdPipe is one self-contained executable and has no
  installer. winget registers it and handles upgrades from there.
- `InstallerUrl` points at the **versioned** asset, not `/releases/latest/`. A moving URL would
  make the recorded hash wrong the moment a new release went out.
- The command is `mdpipe-desktop` rather than `mdpipe`, so it cannot shadow the `mdpipe` CLI
  for anyone who has installed that as a .NET global tool.

[pkgs]: https://github.com/microsoft/winget-pkgs
[wc]: https://github.com/microsoft/winget-create
