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
- The desktop app (WPF) and the CLI share the same engine (`MdPipe.Core` + `MdPipe.Infrastructure`).
  New logic belongs in the engine, not the front-ends.
- MdPipe never touches the system Python or anything outside its own folder. Please keep it that way.
- Keep UI text friendly and plain; it's aimed at people who don't know what a terminal is.

## Not sure?

Open an issue and ask.
