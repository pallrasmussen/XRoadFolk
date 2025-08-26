using Microsoft.Extensions.Logging;
using System.Text;

namespace XRoadFolkWeb.Infrastructure.Logging;

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly InMemoryLogStore _store;

    public InMemoryLoggerProvider(InMemoryLogStore store) => _store = store;

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(_store, categoryName);

    public void Dispose() { }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly InMemoryLogStore _store;
        private readonly string _category;

        public InMemoryLogger(InMemoryLogStore store, string category)
        {
            _store = store; _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true; // capture all; filtering is done by Logging.AddFilter

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var ts = DateTimeOffset.Now;
            var message = formatter(state, exception);
            _store.Add(ts, logLevel, _category, eventId, message, exception?.ToString());
        }
    }
}
