<p align="center">
  <img src="assets/banner.png" alt="MdPipe — Convert your documents to Markdown" width="820">
</p>

# MdPipe

MdPipe turns your documents into clean Markdown — PDF, Word, Excel, PowerPoint, HTML, images and more.
There's a friendly desktop app for everyone, and a CLI for scripting.

It's powered by Microsoft's [MarkItDown](https://github.com/microsoft/markitdown), but you never have to
touch any of that: MdPipe sets up everything it needs on its own.

> MdPipe is an independent, unofficial project — **not affiliated with Microsoft**. MarkItDown is
> downloaded from PyPI at runtime, not bundled. See [NOTICE.md](NOTICE.md).

## Download

<p align="center">
  <a href="https://github.com/gdols/MdPipe/releases/latest/download/MdPipe.exe">
    <img src="https://img.shields.io/badge/Download%20MdPipe-for%20Windows-2563EB?style=for-the-badge&logo=windows&logoColor=white" alt="Download MdPipe for Windows">
  </a>
</p>

One file. No installer, no .NET, no Python — really.

1. **Get it.** Click the blue button above — that downloads `MdPipe.exe`. (Or grab it by hand from the
   [latest release](https://github.com/gdols/MdPipe/releases/latest).)
2. **Open it.** Double-click `MdPipe.exe`. The first time, Windows might show a blue
   *"Windows protected your PC"* box — that's normal for a brand-new app that isn't code-signed yet.
   Just click **More info**, then **Run anyway**.
3. **Give it a minute (only once).** On the very first launch MdPipe quietly sets up its own little Python
   in the background. After that it opens instantly, works offline, and never touches any Python you
   already have.

That's the whole install. Windows 10 and 11 (64-bit).

## Using the desktop app

<p align="center">
  <img src="assets/screenshots/app-main.png" alt="MdPipe desktop app" width="720">
</p>

1. **Open MdPipe.** Double-click `MdPipe.exe`. The very first run takes a minute while it sets itself
   up; after that it opens instantly. When the dot turns green and says *Ready*, you're good to go.
2. **Add your files.** Drag one or more documents onto the window, or click the box to browse. PDF, Word,
   Excel, PowerPoint, HTML, images and more are supported.
3. **Pick where to save (optional).** By default each Markdown file lands right next to the original.
   Click *Change…* to send them all to one folder instead.
4. **Convert.** Press *Convert to Markdown*. Each file shows its progress and turns green when it's done.
5. **Open the results.** Click *Open folder* to jump straight to your new `.md` files.

A few good-to-knows:

- Your original files are never modified or deleted — MdPipe only ever writes new `.md` files.
- *Clear list* just empties the list on screen.
- If anything ever looks off, a *Reinstall* button appears to rebuild the engine from scratch.
- *About* (bottom of the window) has the version and contact details.

## CLI

Prefer the terminal? Install it as a .NET global tool:

```bash
dotnet tool install --global MdPipe

mdpipe convert report.pdf            # print Markdown to the console
mdpipe convert report.pdf -o out.md  # ...or save it to a file
mdpipe status                        # check the environment
```

## Keeping conversions stable

MdPipe doesn't blindly grab the newest MarkItDown. It checks a small compatibility manifest
([`manifest/markitdown-compat.json`](manifest/markitdown-compat.json)) and only installs versions that
have been validated — so an upstream change can't quietly break your conversions.
Details in [docs/version-control.md](docs/version-control.md).

## Building from source

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (only for building — not for running
the released app).

```bash
git clone https://github.com/gdols/MdPipe.git
cd MdPipe

dotnet run --project src/MdPipe.Wpf   # run the desktop app
dotnet test                           # run the tests
```

Make your own portable single-file exe:

```bash
dotnet publish src/MdPipe.Wpf -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## License

MIT — see [LICENSE](LICENSE). MdPipe integrates with Microsoft MarkItDown (MIT), fetched from PyPI at
runtime and not redistributed here. See [NOTICE.md](NOTICE.md).
