namespace MdPipe.Core.Exceptions;

public class MdPipeException(string message, Exception? inner = null)
    : Exception(message, inner);

public class ConversionException(string message, Exception? inner = null)
    : MdPipeException(message, inner);

public class ManifestException(string message, Exception? inner = null)
    : MdPipeException(message, inner);

public class PythonEnvironmentException(string message, Exception? inner = null)
    : MdPipeException(message, inner);

/// <summary>No usable Python interpreter was found on the system.</summary>
public class PythonNotFoundException(string message, Exception? inner = null)
    : PythonEnvironmentException(message, inner);

public class VersionGateException(string message, string installedVersion, string stableVersion)
    : MdPipeException(message)
{
    public string InstalledVersion { get; } = installedVersion;
    public string StableVersion { get; } = stableVersion;
}
