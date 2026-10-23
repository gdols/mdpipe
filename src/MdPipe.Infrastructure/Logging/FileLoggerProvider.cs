using System.Text;
using Microsoft.Extensions.Logging;

namespace MdPipe.Infrastructure.Logging;

/// <summary>
/// Writes the log to a small text file, so a failure on someone else's machine leaves something
/// they can attach to an issue. It never throws: losing a log line is not worth a crash.
/// </summary>
/// <remarks>
/// Kept short on purpose. When the file passes <see cref="MaxBytes"/> at startup it becomes
/// <c>.old.log</c>, replacing the previous one, so there are never more than two.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const long MaxBytes = 512 * 1024;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpipe", "mdpipe.log");

    private readonly Lock _gate = new();
    private readonly LogLevel _minimum;

    public FileLoggerProvider(string? path = null, LogLevel minimum = LogLevel.Information)
    {
        FilePath = path ?? DefaultPath;
        _minimum = minimum;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public string FilePath { get; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortName(categoryName));

    public void Dispose() { }

    private void Write(string line)
    {
        try
        {
            lock (_gate) File.AppendAllText(FilePath, line, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>"MdPipe.Infrastructure.Python.PythonEnvironmentManager" is long for every line.</summary>
    private static string ShortName(string category) =>
        category[(category.LastIndexOf('.') + 1)..];

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= owner._minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append(' ').Append(Level(logLevel))
                .Append(' ').Append(category).Append(": ")
                .Append(formatter(state, exception))
                .AppendLine();
            if (exception is not null) line.AppendLine(exception.ToString());

            owner.Write(line.ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "FATAL"
        };
    }
}
