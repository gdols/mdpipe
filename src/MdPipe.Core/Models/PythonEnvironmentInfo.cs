namespace MdPipe.Core.Models;

public sealed class PythonEnvironmentInfo
{
    public bool IsReady { get; init; }
    public string? PythonExecutable { get; init; }
    public string? InstalledMarkItDownVersion { get; init; }
    public string? MissingReason { get; init; }
}
