using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace LoggerService;

/// <summary>
/// Minimal daily rolling file logger (logs/tuniplan-yyyyMMdd.log), written by a background task.
/// Replace with Serilog/NLog if you need more (sinks, JSON, retention…).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly Task _writer;

    public FileLoggerProvider(string directory, LogLevel minLevel)
    {
        _directory = directory;
        _minLevel = minLevel;
        Directory.CreateDirectory(directory);
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    internal bool IsEnabled(LogLevel level) => level >= _minLevel && level != LogLevel.None;

    internal void Enqueue(string line) => _channel.Writer.TryWrite(line);

    private async Task WriteLoopAsync()
    {
        await foreach (var line in _channel.Reader.ReadAllAsync())
        {
            try
            {
                var path = Path.Combine(_directory, $"tuniplan-{DateTime.UtcNow:yyyyMMdd}.log");
                await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { /* never crash the app because of logging */ }
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        try { _writer.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
    }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTime.UtcNow:O} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            provider.Enqueue(line);
        }
    }
}
