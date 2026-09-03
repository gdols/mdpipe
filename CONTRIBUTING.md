# Contributing

Thanks for stopping by! MdPipe is a small personal project, but contributions are very welcome.

## Found a bug?

Open an [issue](https://github.com/gdols/MdPipe/issues) and tell me what happened. The most useful things
you can include: what you did, what you expected, what you got instead, and, if it's about a conversion,
what kind of file it was. If the app showed an error window, paste its text.

## Want to add or change something?

For small fixes, just send a pull request. For anything bigger, please open an issue first so we can talk
it over before you spend time on it.

A few notes to make it smooth:

- The solution needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). `dotnet test` should stay green.
- A few tests drive a real worker process, so they need Python on PATH. Any version does: the scripts
  they run import nothing, and the point is that the pipe and the encoding are the real ones rather
  than a stand-in. Without it those tests fail with a message saying so.
- The desktop app (WPF) and the CLI share the same engine (`MdPipe.Core` + `MdPipe.Infrastructure`).
  New logic belongs in the engine, not the front-ends.
- MdPipe never touches the system Python or anything outside its own folder. Please keep it that way.
- Keep UI text friendly and plain; it's aimed at people who don't know what a terminal is.
- Nothing that touches the disk should run on the interface thread. Walking a folder there used to
  freeze the window for seconds at a time, so anything that might be slow goes through `Task.Run`
  with a `CancellationToken` and something to show for the wait.

### Adding or changing text in the app

The interface follows the machine's language, so a label lives in **three** files and all three
have to agree:

| | |
|---|---|
| `src/MdPipe.Wpf/Resources/Strings.resx` | English, the neutral fallback |
| `src/MdPipe.Wpf/Resources/Strings.es.resx` | Spanish |
| `src/MdPipe.Wpf/Resources/Strings.cs` | the accessor XAML and the view model use |

`Strings.cs` is written by hand and committed on purpose: WPF compiles XAML through a temporary
project that cannot see sources generated during the build, so generating it breaks the build
in a confusing way. Miss an entry and nothing fails, the window just shows the key name where
the text should be, which is why `StringsTests` checks all three against each other. XAML reads
them as `{res:Str TheKey}`.

### The sample documents

`tests/fixtures/` holds a small PDF, DOCX and XLSX that the weekly MarkItDown check converts
with an old version and a new one to see whether the output moved. They are generated rather
than collected, so if you ever need to rebuild them:

```bash
python tests/fixtures/make_fixtures.py
```

## Not sure?

Open an issue and ask.
