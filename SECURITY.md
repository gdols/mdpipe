# Security

If you find a security issue in MdPipe, please **don't open a public issue**. Email me instead at
**guille@gdols.dev** and I'll get back to you as soon as I can, usually within a few days.

Tell me what you found, how to reproduce it, and what you think the impact is. If it's real, I'll fix it
and credit you in the release notes (unless you'd rather stay anonymous).

A note on scope: MdPipe downloads two things at runtime from official sources, the embeddable Python from
python.org and MarkItDown from PyPI, and everything it installs lives in its own folder under
`%APPDATA%\mdpipe`. It never modifies the system Python, the PATH, or anything outside that folder.
