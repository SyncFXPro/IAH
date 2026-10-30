using System.IO;
using Microsoft.Extensions.Logging;

namespace Glance.App.Services;

public sealed class GlanceFileLoggerProvider : ILoggerProvider
{
    private readonly Func<bool> _enabled;
    private readonly string _path;
    private readonly object _gate = new();

    public GlanceFileLoggerProvider(Func<bool> enabled, string path)
    {
        _enabled = enabled;
        _path = path;
    }

    public ILogger CreateLogger(string categoryName) => new GlanceFileLogger(categoryName, _path, _enabled, _gate);

    public void Dispose()
    {
    }

    private sealed class GlanceFileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _path;
        private readonly Func<bool> _enabled;
        private readonly object _gate;

        public GlanceFileLogger(string category, string path, Func<bool> enabled, object gate)
        {
            _category = category;
            _path = path;
            _enabled = enabled;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && _enabled();

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {logLevel} [{_category}] {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_gate)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
    }
}
