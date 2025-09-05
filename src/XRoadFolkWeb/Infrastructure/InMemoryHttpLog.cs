using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib.Logging;
using System.Threading.Channels;
using System.Diagnostics.Metrics;

namespace XRoadFolkWeb.Infrastructure
{
    internal static class InMemoryLogMetrics
    {
        public static readonly Meter Meter = new("XRoadFolkWeb");
        public static readonly Counter<long> LogDrops = Meter.CreateCounter<long>("logs.dropped", unit: "count", description: "Number of log entries dropped due to backpressure");
        public static readonly Counter<long> LogDropsByReason = Meter.CreateCounter<long>("logs.dropped.reason", unit: "count", description: "Log drops by reason (tags: reason, store)");
        public static readonly Counter<long> LogDropsByLevel = Meter.CreateCounter<long>("logs.dropped.level", unit: "count", description: "Log drops by level (tags: level, store)");

        private static readonly ConcurrentDictionary<Guid, Func<int>> Providers = new();
        public static readonly ObservableGauge<int> QueueLength = Meter.CreateObservableGauge<int>(
            "logs.queue.length",
            ObserveQueueLengths,
            unit: "items",
            description: "Approximate in-memory log queue size per instance");

        private static IEnumerable<Measurement<int>> ObserveQueueLengths()
        {
            foreach (KeyValuePair<Guid, Func<int>> kv in Providers)
            {
                int size = 0;
                try { size = kv.Value(); }
                catch { }
                yield return new Measurement<int>(size,
                    new KeyValuePair<string, object?>("store", "memory"),
                    new KeyValuePair<string, object?>("instance", kv.Key.ToString()));
            }
        }

        public static Guid RegisterProvider(Func<int> provider)
        {
            Guid id = Guid.NewGuid();
            Providers[id] = provider ?? (() => 0);
            return id;
        }

        public static void UnregisterProvider(Guid id)
        {
            _ = Providers.TryRemove(id, out _);
        }
    }

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

    public sealed class InMemoryHttpLog : IHttpLogStore, IAsyncDisposable, IDisposable
    {
        private readonly ConcurrentQueue<LogEntry> _queue = new();
        private int _size; // approximate size to avoid O(n) Count

        private readonly int _capacity;
        private readonly long _maxFileBytes;
        private readonly int _maxRolls;
        private readonly string? _filePath;
        private readonly HttpLogRateLimiter _rateLimiter;
        private readonly ILogger<InMemoryHttpLog>? _logger;

        // Bounded ingestion channel to enforce backpressure at ingress
        private readonly Channel<LogEntry> _ingestChannel;
        private readonly Task _ingestTask;
        private readonly CancellationTokenSource _ingestCts = new();

        // Async file writer (enabled only if _filePath is provided)
        private readonly Channel<string>? _fileChannel;
        private readonly Task? _fileWriterTask;
        private readonly CancellationTokenSource? _writerCts;
        private volatile bool _disposed;
        private readonly Guid _metricsHandle;

        public InMemoryHttpLog(IOptions<HttpLogOptions> opts, ILogger<InMemoryHttpLog>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(opts);
            HttpLogOptions cfg = opts.Value;

            _capacity = Math.Max(50, cfg.Capacity);
            _maxFileBytes = Math.Max(1024L * 1024, cfg.MaxFileBytes);
            _maxRolls = Math.Max(1, cfg.MaxRolls);
            _filePath = cfg.FilePath;
            _rateLimiter = new HttpLogRateLimiter(cfg.MaxWritesPerSecond, cfg.AlwaysAllowWarningsAndErrors);
            _logger = logger;

            // Ingestion channel uses MaxQueue to cap memory usage and enforce backpressure
            _ingestChannel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(capacity: Math.Max(1000, cfg.MaxQueue))
            {
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
            _ingestTask = Task.Run(() => IngestLoopAsync(_ingestChannel.Reader, _ingestCts.Token));

            // Register per-instance queue length provider
            _metricsHandle = InMemoryLogMetrics.RegisterProvider(() => Volatile.Read(ref _size));

            if (!string.IsNullOrWhiteSpace(_filePath))
            {
                // Ensure directory exists once during initialization
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Bounded channel to provide back-pressure for file persistence
                _fileChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity: Math.Max(1000, cfg.MaxQueue))
                {
                    AllowSynchronousContinuations = true,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                });
                _writerCts = new CancellationTokenSource();
                _fileWriterTask = Task.Run(() => FileWriterLoopAsync(_fileChannel.Reader, _filePath!, _maxFileBytes, _maxRolls, _logger, _writerCts.Token));
            }
        }

        public void Add(LogEntry e)
        {
            if (_disposed)
            {
                return;
            }
            ArgumentNullException.ThrowIfNull(e);

            if (_rateLimiter.ShouldDrop(e.Level))
            {
                InMemoryLogMetrics.LogDrops.Add(1);
                InMemoryLogMetrics.LogDropsByReason.Add(1, new KeyValuePair<string, object?>("reason", "rate"), new KeyValuePair<string, object?>("store", "memory"));
                InMemoryLogMetrics.LogDropsByLevel.Add(1, new KeyValuePair<string, object?>("level", e.Level.ToString()), new KeyValuePair<string, object?>("store", "memory"));
                return; // throttled
            }

            if (!_ingestChannel.Writer.TryWrite(e))
            {
                // Backpressure drop at ingress
                InMemoryLogMetrics.LogDrops.Add(1);
                InMemoryLogMetrics.LogDropsByReason.Add(1, new KeyValuePair<string, object?>("reason", "backpressure"), new KeyValuePair<string, object?>("store", "memory"));
                InMemoryLogMetrics.LogDropsByLevel.Add(1, new KeyValuePair<string, object?>("level", e.Level.ToString()), new KeyValuePair<string, object?>("store", "memory"));
            }
        }

        private async Task IngestLoopAsync(ChannelReader<LogEntry> reader, CancellationToken ct)
        {
            try
            {
                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out LogEntry? item))
                    {
                        // Ring buffer enqueue
                        _queue.Enqueue(item);
                        _ = Interlocked.Increment(ref _size);

                        // Trim until capacity is met (robust under contention)
                        while (Volatile.Read(ref _size) > _capacity)
                        {
                            if (_queue.TryDequeue(out LogEntry? removed))
                            {
                                _ = Interlocked.Decrement(ref _size);
                                // Capacity eviction metrics
                                InMemoryLogMetrics.LogDrops.Add(1);
                                InMemoryLogMetrics.LogDropsByReason.Add(1, new KeyValuePair<string, object?>("reason", "capacity"), new KeyValuePair<string, object?>("store", "memory"));
                                InMemoryLogMetrics.LogDropsByLevel.Add(1, new KeyValuePair<string, object?>("level", removed.Level.ToString()), new KeyValuePair<string, object?>("store", "memory"));
                            }
                            else
                            {
                                // Queue observed empty; stop trimming to avoid underflow on _size
                                break;
                            }
                        }

                        // Optional persistence to file via bounded channel
                        if (_fileChannel is not null)
                        {
                            string line = LogLineFormatter.FormatLine(item) + Environment.NewLine;
                            if (!_fileChannel.Writer.TryWrite(line))
                            {
                                InMemoryLogMetrics.LogDropsByReason.Add(1, new KeyValuePair<string, object?>("reason", "backpressure"), new KeyValuePair<string, object?>("store", "memory"));
                                InMemoryLogMetrics.LogDropsByLevel.Add(1, new KeyValuePair<string, object?>("level", item.Level.ToString()), new KeyValuePair<string, object?>("store", "memory"));
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "InMemoryHttpLog ingest loop error");
            }
        }

        private static async Task FileWriterLoopAsync(ChannelReader<string> reader, string path, long maxBytes, int maxRolls, ILogger? logger, CancellationToken ct)
        {
            List<string> batch = new(capacity: 512);
            const int flushIntervalMs = 200;

            while (true)
            {
                try
                {
                    // Wait for data
                    if (!await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        // Channel completed; exit
                        break;
                    }

                    // Read up to a batch
                    batch.Clear();
                    while (reader.TryRead(out string? line))
                    {
                        batch.Add(line);
                        if (batch.Count >= 1024)
                        {
                            break;
                        }
                    }

                    if (batch.Count == 0)
                    {
                        await Task.Delay(flushIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    // Roll if needed and append batch asynchronously (UTF-8 without BOM)
                    LogFileRolling.RollIfNeeded(path, maxBytes, maxRolls, logger);
                    using FileStream fs = new(path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
                    using StreamWriter sw = new(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    foreach (string l in batch)
                    {
                        await sw.WriteAsync(l.AsMemory(), ct).ConfigureAwait(false);
                    }
                    await sw.FlushAsync(ct).ConfigureAwait(false);
                    await fs.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Report errors using provided logger then continue
                    logger?.LogError(ex, "InMemoryHttpLog file writer error");
                    await Task.Delay(flushIntervalMs, ct).ConfigureAwait(false);
                }
            }
        }

        public void Clear()
        {
            while (_queue.TryDequeue(out _))
            {
            }
            Volatile.Write(ref _size, 0);
        }

        public IReadOnlyList<LogEntry> GetAll()
        {
            return [.. _queue];
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            CancelIngestion();
            CompleteIngestionChannel();
            await CancelFileWriterAsync().ConfigureAwait(false);
            CompleteFileChannelWriter();
            await WaitForBackgroundTasksAsync().ConfigureAwait(false);
            InMemoryLogMetrics.UnregisterProvider(_metricsHandle);
            DisposeTokens();
        }

        private void CancelIngestion()
        {
            try
            {
                _ingestCts.Cancel();
            }
            catch
            {
            }
        }

        private void CompleteIngestionChannel()
        {
            try
            {
                _ingestChannel.Writer.TryComplete();
            }
            catch
            {
            }
        }

        private async Task CancelFileWriterAsync()
        {
            try
            {
                if (_writerCts is not null)
                {
                    await _writerCts.CancelAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        private void CompleteFileChannelWriter()
        {
            if (_fileChannel is not null)
            {
                try
                {
                    _fileChannel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "InMemoryHttpLog: Error completing file channel writer during dispose");
                }
            }
        }

        private async Task WaitForBackgroundTasksAsync()
        {
            try
            {
                await _ingestTask.ConfigureAwait(false);
            }
            catch
            {
            }

            if (_fileWriterTask is not null)
            {
                try
                {
                    await _fileWriterTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "InMemoryHttpLog: Error waiting for file writer task during dispose");
                }
            }
        }

        private void DisposeTokens()
        {
            _writerCts?.Dispose();
            _ingestCts.Dispose();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
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
            Volatile.Write(ref _scopes, scopeProvider);
        }

        private sealed class SinkLogger(string category, IHttpLogStore store, InMemoryHttpLogLoggerProvider owner) : ILogger
        {
            private readonly string _category = category;
            private readonly IHttpLogStore _store = store;
            private readonly InMemoryHttpLogLoggerProvider _owner = owner;

            private sealed class NoopScope : IDisposable { public static readonly NoopScope Instance = new(); public void Dispose() { } }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                IExternalScopeProvider? provider = Volatile.Read(ref _owner._scopes);
                return provider?.Push(state) ?? NoopScope.Instance;
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

            private const int MaxScopeChars = 2048; // ~2KB cap
            private const int MaxScopes = 16;       // depth bound
            private const int MaxKvpPerScope = 16;  // per-scope kv limit

            private static string? RenderScopes(IExternalScopeProvider? provider)
            {
                if (provider is null)
                {
                    return null;
                }

                StringBuilder sb = new(capacity: 256);
                bool truncated = false;
                int scopeCount = 0;

                provider.ForEachScope<object?>((scope, _) =>
                {
                    if (scopeCount >= MaxScopes || truncated)
                    {
                        return;
                    }

                    AppendScopeSeparator(sb, ref scopeCount);
                    AppendScopeContent(sb, scope, ref truncated);
                    TruncateIfNeeded(sb, ref truncated);
                    scopeCount++;
                }, state: null);

                if (sb.Length == 0)
                {
                    return null;
                }
                return sb.ToString();
            }

            private static void AppendScopeSeparator(StringBuilder sb, ref int scopeCount)
            {
                if (scopeCount > 0)
                {
                    _ = sb.Append(" => ");
                }
            }

            private static void AppendScopeContent(StringBuilder sb, object? scope, ref bool truncated)
            {
                switch (scope)
                {
                    case IEnumerable<KeyValuePair<string, object?>> kvs:
                        AppendKvpScope(sb, kvs, ref truncated);
                        break;
                    default:
                        _ = sb.Append(scope?.ToString());
                        break;
                }
            }

            private static void AppendKvpScope(StringBuilder sb, IEnumerable<KeyValuePair<string, object?>> kvs, ref bool truncated)
            {
                bool firstKv = true;
                int kvCount = 0;
                _ = sb.Append('{');
                foreach (KeyValuePair<string, object?> kv in kvs)
                {
                    if (kvCount >= MaxKvpPerScope)
                    {
                        _ = sb.Append(", ...");
                        break;
                    }
                    if (!firstKv)
                    {
                        _ = sb.Append(", ");
                    }
                    _ = sb.Append(kv.Key).Append('=').Append(kv.Value);
                    firstKv = false;
                    kvCount++;
                    if (sb.Length > MaxScopeChars)
                    {
                        TruncateBuilder(sb, ref truncated);
                        break;
                    }
                }
                _ = sb.Append('}');
            }

            private static void TruncateIfNeeded(StringBuilder sb, ref bool truncated)
            {
                if (sb.Length > MaxScopeChars)
                {
                    TruncateBuilder(sb, ref truncated);
                }
            }

            private static void TruncateBuilder(StringBuilder sb, ref bool truncated)
            {
                sb.Length = Math.Max(0, MaxScopeChars - 3);
                _ = sb.Append("...");
                truncated = true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string msg = formatter(state, exception);
                string kind = ComputeKind(_category, eventId, msg);
                IExternalScopeProvider? provider = Volatile.Read(ref _owner._scopes);
                string? scopeInfo = RenderScopes(provider);
                if (!string.IsNullOrEmpty(scopeInfo))
                {
                    msg = $"{msg} | scopes: {scopeInfo}";
                }

                // Guardrail: cap message length to avoid excessive allocations/IO
                const int MaxMessageChars = 8 * 1024; // 8KB
                if (msg.Length > MaxMessageChars)
                {
                    int trimmed = msg.Length - MaxMessageChars;
                    msg = msg[..MaxMessageChars] + $"... (+{trimmed} chars)";
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
