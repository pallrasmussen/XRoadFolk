using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Infrastructure
{
    public interface IHttpLogStore
    {
        void Add(LogEntry entry);
        void Clear();
        IReadOnlyList<LogEntry> GetAll();
        int Capacity { get; }
        int Count { get; }
    }

    public sealed class LogEntry
    {
        public required DateTimeOffset Timestamp { get; init; }
        public required LogLevel Level { get; init; }
        public required string Category { get; init; }
        public required int EventId { get; init; }
        public string? Message { get; init; }
        public string? Exception { get; init; }
    }

    public sealed class InMemoryHttpLogStore : IHttpLogStore
    {
        private readonly ConcurrentQueue<LogEntry> _queue = new();
        private readonly int _capacity;

        public InMemoryHttpLogStore(int capacity = 500)
        {
            _capacity = Math.Max(50, capacity);
        }

        public int Capacity => _capacity;
        public int Count => _queue.Count;

        public void Add(LogEntry entry)
        {
            _queue.Enqueue(entry);
            while (_queue.Count > _capacity && _queue.TryDequeue(out _)) { }
        }

        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
        }

        public IReadOnlyList<LogEntry> GetAll()
        {
            return _queue.ToArray();
        }
    }

    public sealed class InMemoryHttpLogLoggerProvider : ILoggerProvider
    {
        private readonly IHttpLogStore _store;

        public InMemoryHttpLogLoggerProvider(IHttpLogStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new SinkLogger(categoryName, _store);
        }

        public void Dispose() { }

        private sealed class SinkLogger : ILogger
        {
            private readonly string _category;
            private readonly IHttpLogStore _store;

            public SinkLogger(string category, IHttpLogStore store)
            {
                _category = category;
                _store = store;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                string msg = formatter(state, exception);
                _store.Add(new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Level = logLevel,
                    Category = _category,
                    EventId = eventId.Id,
                    Message = msg,
                    Exception = exception?.ToString()
                });
            }
        }
    }
}
