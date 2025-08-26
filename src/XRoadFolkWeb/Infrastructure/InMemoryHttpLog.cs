using System.Collections.Concurrent;
using System.Text;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib.Logging;

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
        public required string Kind { get; init; } // http | soap | app
        public string? Message { get; init; }
        public string? Exception { get; init; }
    }

    public sealed class InMemoryHttpLogStore : IHttpLogStore
    {
        private readonly ConcurrentQueue<LogEntry> _queue = new();
        private readonly int _capacity;
        private readonly ILogStream? _stream; // optional realtime broadcaster

        // Rate limiting (simple token bucket)
        private readonly int _maxWritesPerSecond;
        private readonly bool _alwaysAllowWarnError;
        private double _tokens;
        private DateTime _lastRefillUtc;
        private readonly object _rateLock = new();

        // Optional file persistence
        private readonly bool _persistToFile;
        private readonly string? _filePath;
        private readonly long _maxFileBytes;
        private readonly int _maxRolls;
        private readonly object _fileLock = new();

        public InMemoryHttpLogStore(int capacity = 500, ILogStream? stream = null)
            : this(Options.Create(new HttpLogOptions { Capacity = capacity }), stream) { }

        public InMemoryHttpLogStore(IOptions<HttpLogOptions> options, ILogStream? stream = null)
        {
            HttpLogOptions cfg = options?.Value ?? new HttpLogOptions();
            _capacity = Math.Max(50, cfg.Capacity);
            _stream = stream;

            _maxWritesPerSecond = Math.Max(0, cfg.MaxWritesPerSecond);
            _alwaysAllowWarnError = cfg.AlwaysAllowWarningsAndErrors;
            _tokens = _maxWritesPerSecond; _lastRefillUtc = DateTime.UtcNow;

            _persistToFile = cfg.PersistToFile && !string.IsNullOrWhiteSpace(cfg.FilePath);
            _filePath = cfg.FilePath;
            _maxFileBytes = Math.Max(50_000, cfg.MaxFileBytes);
            _maxRolls = Math.Max(1, cfg.MaxRolls);
        }

        public int Capacity => _capacity;
        public int Count => _queue.Count;

        public void Add(LogEntry entry)
        {
            if (!AllowWrite(entry.Level)) return; // drop if rate-limited and not important

            _queue.Enqueue(entry);
            while (_queue.Count > _capacity && _queue.TryDequeue(out _)) { }
            try { _stream?.Publish(entry); } catch { }

            if (_persistToFile)
            {
                try { AppendToFile(entry); } catch { }
            }
        }

        private bool AllowWrite(LogLevel level)
        {
            if (_maxWritesPerSecond <= 0) return true; // limiter disabled
            if (_alwaysAllowWarnError && (level >= LogLevel.Warning)) return true;

            lock (_rateLock)
            {
                DateTime now = DateTime.UtcNow;
                double elapsed = (now - _lastRefillUtc).TotalSeconds;
                if (elapsed > 0)
                {
                    _tokens = Math.Min(_maxWritesPerSecond, _tokens + elapsed * _maxWritesPerSecond);
                    _lastRefillUtc = now;
                }
                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    return true;
                }
                return false;
            }
        }

        private void AppendToFile(LogEntry e)
        {
            if (string.IsNullOrWhiteSpace(_filePath)) return;
            string line = FormatLine(e);
            lock (_fileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath!)!);
                RollIfNeeded();
                File.AppendAllText(_filePath!, line, Encoding.UTF8);
            }
        }

        private void RollIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_filePath)) return;
            try
            {
                FileInfo fi = new FileInfo(_filePath!);
                if (fi.Exists && fi.Length > _maxFileBytes)
                {
                    // roll: file.log -> file.log.1/.2...
                    for (int i = _maxRolls; i >= 1; i--)
                    {
                        string from = i == 1 ? _filePath! : _filePath! + "." + (i - 1);
                        string to = _filePath! + "." + i;
                        if (File.Exists(to)) File.Delete(to);
                        if (File.Exists(from)) File.Move(from, to);
                    }
                }
            }
            catch { }
        }

        private static string FormatLine(LogEntry e)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{e.Timestamp:O}\t{e.Level}\t{e.Kind}\t{e.Category}\t{e.EventId}\t{e.Message}\t{e.Exception}{Environment.NewLine}");
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
                    (msg != null && msg.StartsWith("HTTP", StringComparison.OrdinalIgnoreCase)))
                {
                    return "http";
                }
                return "app";
            }

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
                    Kind = ComputeKind(_category, eventId, msg),
                    Message = msg,
                    Exception = exception?.ToString()
                });
            }
        }
    }
}
