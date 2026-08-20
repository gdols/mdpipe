namespace MdPipe.Core.Models;

/// <summary>
/// Where a converted document should be written, and whether the name had to be changed to keep it
/// from landing on top of something else in the same run.
/// </summary>
public sealed record OutputPath(string FullPath, bool Renamed);
