<p align="center">
  <img src="assets/banner.png" alt="MdPipe, convert your documents to Markdown" width="820">
</p>

# MdPipe

<p align="center">
  <a href="https://github.com/gdols/MdPipe/releases/latest"><img src="https://img.shields.io/github/v/release/gdols/MdPipe?color=2563EB&label=latest" alt="Latest release"></a>
  <a href="https://github.com/gdols/MdPipe/releases"><img src="https://img.shields.io/github/downloads/gdols/MdPipe/total?color=16A34A&label=downloads" alt="Downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT license"></a>
  <img src="https://img.shields.io/badge/Windows%2010%2F11-64--bit-0078D4?logo=windows&logoColor=white" alt="Windows 10/11">
</p>

MdPipe converts PDF, Word, Excel, PowerPoint, HTML and images to Markdown on
Windows. It is available as a portable desktop application and as a .NET CLI.
Conversion runs through Microsoft's
[MarkItDown](https://github.com/microsoft/markitdown).

## Download

<p align="center">
  <a href="https://github.com/gdols/MdPipe/releases/latest/download/MdPipe.exe">
    <img src="https://img.shields.io/badge/%E2%AC%87%20Download%20MdPipe.exe-free%20%C2%B7%20~63%20MB%20%C2%B7%20Windows-2563EB?style=for-the-badge&logo=windows&logoColor=white" alt="Download MdPipe.exe for Windows">
  </a>
  <br>
  <sub><b>One file. No installer. No .NET or Python required.</b></sub>
</p>

It is a portable, self-contained Windows 10/11 x64 executable. The first launch
downloads a private Python environment and MarkItDown into `%APPDATA%\mdpipe`;
later conversions work offline. Because the executable is not code-signed yet,
Windows SmartScreen may ask you to confirm that you want to run it.

<p align="center">
  <img src="assets/screenshots/app-main.png" alt="MdPipe desktop app" width="720">
</p>

I built MdPipe so I could give someone a document converter without first asking
them to install Python or work from a terminal.

## What it does

- Converts several files in one batch using drag and drop.
- Saves the Markdown beside the original or in a chosen folder.
- Runs locally without uploading documents or collecting telemetry.
- Includes a CLI for scripts and automation.
- Keeps MarkItDown on a version tested with MdPipe.

## CLI

Install the .NET global tool:

```bash
dotnet tool install --global MdPipe

mdpipe convert report.pdf
mdpipe convert report.pdf -o report.md
mdpipe status
```

## How it works

The WPF application and CLI share the same conversion and setup code. MdPipe
creates its environment under AppData and does not modify the system Python or
`PATH`.

A small [compatibility manifest](docs/version-control.md) records the MarkItDown
versions tested with MdPipe. Setup also passes the Windows proxy to pip and keeps
its output visible when a firewall, proxy or SSL inspection blocks the download.

There is a longer write-up with code samples on
[gdols.dev](https://gdols.dev/blog/como-hice-mdpipe/).

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```bash
git clone https://github.com/gdols/MdPipe.git
cd MdPipe

dotnet run --project src/MdPipe.Wpf
dotnet test
```

To build the portable executable:

```bash
dotnet publish src/MdPipe.Wpf -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Limitations

- Official releases currently target Windows x64 only.
- The first launch needs internet access to download Python and MarkItDown.
- The executable is not code-signed, so new releases can trigger SmartScreen.
- Conversion quality depends on the source format and MarkItDown.

## License

MdPipe is available under the MIT license. It is an independent project and is
not affiliated with Microsoft. MarkItDown is fetched from PyPI at runtime and
is not redistributed; see [NOTICE.md](NOTICE.md).

Bug reports and small pull requests are welcome. See
[CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md).

