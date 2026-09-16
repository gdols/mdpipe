namespace MdPipe.Core.Models;

/// <param name="Renamed">The name had to change to avoid landing on another file in the same run.</param>
public sealed record OutputPath(string FullPath, bool Renamed);
