using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib.Logging;

namespace XRoadFolkWeb.Infrastructure
{
    public sealed record LogEntry
    {
        public DateTimeOffset Timestamp { get; init; }
        public LogLevel Level { get; init; }
        public string Kind { get; init; } = string.Empty; // http|soap|app
        public string Category { get; init; } = string.Empty;
        public int EventId { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Exception { get; init; }
    }

    public interface IHttpLogStore
    {
        void Add(LogEntry e);
        void Clear();
        IReadOnlyList<LogEntry> GetAll();
    }

    public sealed class InMemoryHttpLog : IHttpLogStore
    {
        private readonly ConcurrentQueue<LogEntry> _queue = new();
        private int _size; // approximate size to avoid O(n) Count

        private readonly int _capacity;
        private readonly long _maxFileBytes;
        private readonly int _maxRolls;
        private readonly string? _filePath;
        private readonly object _fileLock = new();
        private readonly HttpLogRateLimiter _rateLimiter;

        public InMemoryHttpLog(IOptions<HttpLogOptions> opts)
        {
            ArgumentNullException.ThrowIfNull(opts);
            HttpLogOptions cfg = opts.Value;

            _capacity = Math.Max(50, cfg.Capacity);
            _maxFileBytes = Math.Max(1024L * 1024, cfg.MaxFileBytes);
            _maxRolls = Math.Max(1, cfg.MaxRolls);
            _filePath = cfg.FilePath;
            _rateLimiter = new HttpLogRateLimiter(cfg.MaxWritesPerSecond, cfg.AlwaysAllowWarningsAndErrors);
        }

        public void Add(LogEntry e)
        {
            ArgumentNullException.ThrowIfNull(e);

            if (_rateLimiter.ShouldDrop(e.Level))
            {
                return; // throttled
            }

            _queue.Enqueue(e);
            _ = Interlocked.Increment(ref _size);

            // Trim until capacity is met (robust under contention)
            while (Volatile.Read(ref _size) > _capacity)
            {
                if (_queue.TryDequeue(out _))
                {
                    _ = Interlocked.Decrement(ref _size);
                }
                else
                {
                    // Queue observed empty; stop trimming to avoid underflow on _size
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(_filePath))
            {
                return;
            }

            string line = FormatLine(e);
            lock (_fileLock)
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(_filePath!)!);
                LogFileRolling.RollIfNeeded(_filePath!, _maxFileBytes, _maxRolls, log: null);
                File.AppendAllText(_filePath!, line, Encoding.UTF8);
            }
        }

        private static string FormatLine(LogEntry e)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{e.Timestamp:O}\t{e.Level}\t{e.Kind}\t{e.Category}\t{e.EventId}\t{e.Message}\t{e.Exception}{Environment.NewLine}");
        }

        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
            Volatile.Write(ref _size, 0);
        }

        public IReadOnlyList<LogEntry> GetAll()
        {
            return [.. _queue];
        }
    }

    public sealed class InMemoryHttpLogLoggerProvider(IHttpLogStore store) : ILoggerProvider, ISupportExternalScope
    {
        private readonly IHttpLogStore _store = store ?? throw new ArgumentNullException(nameof(store));
        private IExternalScopeProvider? _scopes;

        public ILogger CreateLogger(string categoryName)
        {
            return new SinkLogger(categoryName, _store, this);
        }

        public void Dispose() { }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopes = scopeProvider;
        }

        private sealed class SinkLogger(string category, IHttpLogStore store, InMemoryHttpLogLoggerProvider owner) : ILogger
        {
            private readonly string _category = category;
            private readonly IHttpLogStore _store = store;
            private readonly InMemoryHttpLogLoggerProvider _owner = owner;

            private sealed class NoopScope : IDisposable { public static readonly NoopScope Instance = new(); public void Dispose() { } }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return _owner._scopes?.Push(state) ?? NoopScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            private static string ComputeKind(string category, EventId eventId, string? msg)
            {
                if (eventId.Id == SafeSoapLogger.SoapRequestEvent.Id ||
                    eventId.Id == SafeSoapLogger.SoapResponseEvent.Id ||
                    eventId.Id == SafeSoapLogger.SoapGeneralEvent.Id)
                {
                    return "soap";
                }

                if (category.Contains("HttpClient", StringComparison.OrdinalIgnoreCase) ||
                                    category.Contains("System.Net.Http", StringComparison.OrdinalIgnoreCase) ||
                                    (msg?.StartsWith("HTTP", StringComparison.OrdinalIgnoreCase) == true))
                {
                    return "http";
                }

                return "app";
            }

            private static string? RenderScopes(IExternalScopeProvider? provider)
            {
                if (provider is null)
                {
                    return null;
                }

                StringBuilder sb = new();
                bool first = true;
                provider.ForEachScope<object?>((scope, _) =>
                {
                    if (!first)
                    {
                        _ = sb.Append(" => ");
                    }

                    switch (scope)
                    {
                        case IEnumerable<KeyValuePair<string, object?>> kvs:
                            bool firstKv = true;
                            _ = sb.Append('{');
                            foreach (KeyValuePair<string, object?> kv in kvs)
                            {
                                if (!firstKv)
                                {
                                    _ = sb.Append(", ");
                                }

                                _ = sb.Append(kv.Key).Append('=').Append(kv.Value);
                                firstKv = false;
                            }
                            _ = sb.Append('}');
                            break;
                        default:
                            _ = sb.Append(scope?.ToString());
                            break;
                    }
                    first = false;
                }, state: null);
                return sb.Length == 0 ? null : sb.ToString();
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string msg = formatter(state, exception);
                string kind = ComputeKind(_category, eventId, msg);
                string? scopeInfo = RenderScopes(_owner._scopes);
                if (!string.IsNullOrEmpty(scopeInfo))
                {
                    msg = $"{msg} | scopes: {scopeInfo}";
                }

                _store.Add(new LogEntry
                {
                    Timestamp = DateTimeOffset.Now,
                    Level = logLevel,
                    Category = _category,
                    EventId = eventId.Id,
                    Kind = kind,
                    Message = msg,
                    Exception = exception?.ToString(),
                });
            }
        }
    }
}
