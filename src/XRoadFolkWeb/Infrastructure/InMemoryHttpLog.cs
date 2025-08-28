using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
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

    public sealed class InMemoryHttpLog(IConfiguration cfg) : IHttpLogStore
    {
        private readonly ConcurrentQueue<LogEntry> _queue = new();
        private readonly long _maxFileBytes = Math.Max(1024 * 1024, cfg.GetValue("HttpLog:MaxFileBytes", 1024 * 1024 * 5));
        private readonly int _maxRolls = Math.Max(1, cfg.GetValue("HttpLog:MaxRolls", 5));
        private readonly string? _filePath = cfg.GetValue<string>("HttpLog:FilePath");
        private readonly object _fileLock = new();

        public void Add(LogEntry e)
        {
            ArgumentNullException.ThrowIfNull(e);

            _queue.Enqueue(e);

            if (string.IsNullOrWhiteSpace(_filePath))
            {
                return;
            }

            string line = FormatLine(e);
            lock (_fileLock)
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(_filePath!)!);
                RollIfNeeded();
                File.AppendAllText(_filePath!, line, Encoding.UTF8);
            }
        }

        private void RollIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_filePath))
            {
                return;
            }

            try
            {
                FileInfo fi = new(_filePath!);
                if (fi.Exists && fi.Length > _maxFileBytes)
                {
                    // roll: file.log -> file.log.1/.2...
                    for (int i = _maxRolls; i >= 1; i--)
                    {
                        string from = i == 1 ? _filePath! : _filePath! + "." + (i - 1);
                        string to = _filePath! + "." + i;
                        if (File.Exists(to))
                        {
                            File.Delete(to);
                        }

                        if (File.Exists(from))
                        {
                            File.Move(from, to);
                        }
                    }
                }
            }
            catch (IOException)
            {
                // ignore file IO issues when rolling; logging continues in memory
            }
            catch (UnauthorizedAccessException)
            {
                // ignore permission issues; logging continues in memory
            }
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
            return [.. _queue];
        }
    }

    public sealed class InMemoryHttpLogLoggerProvider(IHttpLogStore store) : ILoggerProvider
    {
        private readonly IHttpLogStore _store = store ?? throw new ArgumentNullException(nameof(store));

        public ILogger CreateLogger(string categoryName)
        {
            return new SinkLogger(categoryName, _store);
        }

        public void Dispose() { }

        private sealed class SinkLogger(string category, IHttpLogStore store) : ILogger
        {
            private readonly string _category = category;
            private readonly IHttpLogStore _store = store;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            private static string ComputeKind(string category, EventId eventId, string? msg)
            {
                return eventId.Id == SafeSoapLogger.SoapRequestEvent.Id ||
                    eventId.Id == SafeSoapLogger.SoapResponseEvent.Id ||
                    eventId.Id == SafeSoapLogger.SoapGeneralEvent.Id
                    ? "soap"
                    : category.Contains("HttpClient", StringComparison.OrdinalIgnoreCase) ||
                    category.Contains("System.Net.Http", StringComparison.OrdinalIgnoreCase) ||
                    (msg != null && msg.StartsWith("HTTP", StringComparison.OrdinalIgnoreCase))
                    ? "http"
                    : "app";
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string msg = formatter(state, exception);
                string kind = ComputeKind(_category, eventId, msg);
                _store.Add(new LogEntry
                {
                    Timestamp = DateTimeOffset.Now,
                    Level = logLevel,
                    Category = _category,
                    EventId = eventId.Id,
                    Kind = kind,
                    Message = msg,
                    Exception = exception?.Message
                });
            }
        }
    }
}
