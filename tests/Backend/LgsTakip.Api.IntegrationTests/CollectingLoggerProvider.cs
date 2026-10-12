using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LgsTakip.Api.IntegrationTests;

public sealed class CollectingLoggerProvider(
    ConcurrentQueue<string> messages) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new CollectingLogger(messages);
    }

    public void Dispose()
    {
    }

    private sealed class CollectingLogger(
        ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(formatter(state, exception));
        }
    }
}
