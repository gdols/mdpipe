# Third-party notices & attribution

## MarkItDown

MdPipe is a wrapper around **[Microsoft MarkItDown](https://github.com/microsoft/markitdown)**,
which performs the actual document-to-Markdown conversion.

- **License:** MIT License, © Microsoft Corporation
- **How MdPipe uses it:** MarkItDown is **not redistributed** with MdPipe. It is
  downloaded from [PyPI](https://pypi.org/project/markitdown/) at runtime, into an
  isolated Python virtual environment on the user's machine, when `mdpipe setup` is run.
- MdPipe contains **no source code copied from MarkItDown**.

## Bundled with the executable

The portable `MdPipe.exe` is a self-contained .NET build, so it carries these
components inside it. All of them are under the MIT License, © .NET Foundation
and Contributors / Microsoft Corporation:

- The .NET runtime and WPF (<https://github.com/dotnet/runtime>, <https://github.com/dotnet/wpf>)
- Microsoft.Extensions.Hosting, Http and Logging (<https://github.com/dotnet/runtime>)
- System.CommandLine (<https://github.com/dotnet/command-line-api>)

## Downloaded at runtime, not redistributed

- **Python**: when no suitable Python is installed, MdPipe downloads the official
  embeddable package from <https://www.python.org>, under the
  [PSF License](https://docs.python.org/3/license.html).
- **pip**, from <https://bootstrap.pypa.io>, under the MIT License.
- **MarkItDown and its dependencies**, from PyPI, each under its own license.

## Trademark & affiliation disclaimer

MdPipe is an **independent, unofficial** project. It is **not affiliated with,
endorsed by, or sponsored by Microsoft Corporation**.

"MarkItDown" and "Microsoft" are trademarks of Microsoft Corporation. They are used
here **descriptively** (nominative use) only to identify the underlying tool that
MdPipe integrates with. No Microsoft logos or brand assets are used.

Microsoft's trademark guidelines apply to any use of its marks:
<https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks>
