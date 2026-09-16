namespace MdPipe.Core.Models;

/// <param name="Critical">The running version is old enough to have a known problem.</param>
public sealed record AppUpdate(
    string Version,
    string ReleaseUrl,
    string Notes,
    bool Critical,
    string DownloadUrl = "");
