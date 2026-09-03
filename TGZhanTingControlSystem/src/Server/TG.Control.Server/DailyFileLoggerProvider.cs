using Microsoft.Extensions.Logging;

namespace TG.Control.Server;

/// <summary>Small production file sink so Windows Service diagnostics remain available without a third-party wrapper.</summary>
public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string directory;
    private StreamWriter? writer;
    private DateOnly writerDate;

    public DailyFileLoggerProvider(string configuredDirectory)
    {
        directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredDirectory), AppContext.BaseDirectory);
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new DailyFileLogger(this, categoryName);

    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        lock (gate)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (writer is null || writerDate != today)
            {
                writer?.Dispose();
                writerDate = today;
                var path = Path.Combine(directory, $"server-{today:yyyyMMdd}.log");
                writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = true
                };
            }

            writer.WriteLine($"{DateTimeOffset.Now:O} [{level}] {category} {eventId.Id}: {message}");
            if (exception is not null) writer.WriteLine(exception);
        }
    }

    public void Dispose()
    {
        lock (gate) { writer?.Dispose(); writer = null; }
    }

    private sealed class DailyFileLogger(DailyFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            owner.Write(logLevel, category, eventId, formatter(state, exception), exception);
        }
    }
}
