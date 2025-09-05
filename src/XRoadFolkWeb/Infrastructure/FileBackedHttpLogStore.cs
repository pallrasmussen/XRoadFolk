using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using System.Diagnostics.Metrics;

namespace XRoadFolkWeb.Infrastructure
{
    internal static class LogStoreMetrics
    {
        public static readonly Meter Meter = new("XRoadFolkWeb");
        public static readonly Counter<long> LogDrops = Meter.CreateCounter<long>("logs.dropped", unit: "count", description: "Number of log entries dropped due to backpressure");
        public static readonly Counter<long> LogDropsByReason = Meter.CreateCounter<long>("logs.dropped.reason", unit: "count", description: "Log drops by reason (tags: reason, store)");
        public static readonly Counter<long> LogDropsByLevel = Meter.CreateCounter<long>("logs.dropped.level", unit: "count", description: "Log drops by level (tags: level, store)");
        // Initialize with a non-null snapshot provider to satisfy nullable flow
        private static Func<IEnumerable<Measurement<int>>> _ringSizeSnapshot = () => new[] { new Measurement<int>(0, new KeyValuePair<string, object?>("store", "file")) };
        public static readonly ObservableGauge<int> QueueLength = Meter.CreateObservableGauge<int>("logs.queue.length", () => _ringSizeSnapshot(), unit: "items", description: "Approximate in-memory ring size");
        public static void SetRingSizeProvider(Func<int> provider) => _ringSizeSnapshot = () => new[] { new Measurement<int>(provider(), new KeyValuePair<string, object?>("store", "file")) };
    }

    /// <summary>
    /// File-backed store with bounded channel and background writer.
    /// - Maintains an in-memory ring buffer (Capacity)
    /// - Persists entries to rolling log files
    /// - Uses a bounded channel to provide back-pressure; warnings/errors bypass drops
    /// </summary>
    public sealed partial class FileBackedHttpLogStore : IHttpLogStore
    {
        private readonly ConcurrentQueue<LogEntry> _ring = new();
        private int _ringSize; // approximate size to avoid O(n) ConcurrentQueue.Count
        private readonly ILogFeed? _stream;
        private readonly int _maxQueue;
        private readonly HttpLogRateLimiter _rateLimiter;

        private readonly Channel<LogEntry> _channel;
        private readonly ILogger<FileBackedHttpLogStore> _log;

        public FileBackedHttpLogStore(IOptions<HttpLogOptions> options, ILogFeed? stream, ILogger<FileBackedHttpLogStore> log)
        {
            ArgumentNullException.ThrowIfNull(options);
            HttpLogOptions cfg = options.Value;
            Capacity = Math.Max(50, cfg.Capacity);
            _maxQueue = Math.Max(100, cfg.MaxQueue);
            _stream = stream;
            FilePath = cfg.FilePath ?? "logs/http-logs.log";
            MaxFileBytes = Math.Max(50_000, cfg.MaxFileBytes);
            MaxRolls = Math.Max(1, cfg.MaxRolls);
            _rateLimiter = new HttpLogRateLimiter(cfg.MaxWritesPerSecond, cfg.AlwaysAllowWarningsAndErrors);
            _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(_maxQueue)
            {
                // Use Wait so writes fail (TryWrite=false) when full; we will explicitly manage eviction below
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
            });
            _log = log;
            LogStoreMetrics.SetRingSizeProvider(() => Volatile.Read(ref _ringSize));
        }

        internal ILogger Logger => _log;

        public int Capacity { get; }

        public void Add(LogEntry e)
        {
            ArgumentNullException.ThrowIfNull(e);

            if (_rateLimiter.ShouldDrop(e.Level))
            {
                return;
            }

            EnqueueRing(e);
            PublishToStream(e);
            if (!TryWriteToChannel(e) && !TryBestEffortWarnError(e))
            {
                // Log the enqueue drop for operational visibility and record metrics
                LogEnqueueDrop(_log, e.Level, e.Category, e.EventId);
                RecordDrop(e, reason: "backpressure");
            }
        }

        private void EnqueueRing(LogEntry e)
        {
            _ring.Enqueue(e);
            _ = Interlocked.Increment(ref _ringSize);
            while (Volatile.Read(ref _ringSize) > Capacity)
            {
                if (_ring.TryDequeue(out _))
                {
                    _ = Interlocked.Decrement(ref _ringSize);
                }
                else
                {
                    break;
                }
            }
        }

        private void PublishToStream(LogEntry e)
        {
            try
            {
                _stream?.Publish(e);
            }
            catch (Exception ex)
            {
                LogStreamPublishError(_log, ex);
            }
        }

        private bool TryWriteToChannel(LogEntry e)
        {
            return _channel.Writer.TryWrite(e);
        }

        private bool TryBestEffortWarnError(LogEntry e)
        {
            if (!_rateLimiter.AlwaysAllowWarnError || e.Level < LogLevel.Warning)
            {
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                _ = _channel.Reader.TryRead(out _);
                if (_channel.Writer.TryWrite(e))
                {
                    return true;
                }
            }
            _ = Thread.Yield();
            return _channel.Writer.TryWrite(e);
        }

        private static void RecordDrop(LogEntry e, string reason)
        {
            LogStoreMetrics.LogDrops.Add(1);
            LogStoreMetrics.LogDropsByReason.Add(1, new KeyValuePair<string, object?>("reason", reason), new KeyValuePair<string, object?>("store", "file"));
            LogStoreMetrics.LogDropsByLevel.Add(1, new KeyValuePair<string, object?>("level", e.Level.ToString()), new KeyValuePair<string, object?>("store", "file"));
        }

        public void Clear()
        {
            while (_ring.TryDequeue(out _)) { }
            Volatile.Write(ref _ringSize, 0);
            // Note: file is not truncated here; persistence clearing is handled by the hosted writer on shutdown.
        }

        public IReadOnlyList<LogEntry> GetAll()
        {
            return [.. _ring];
        }

        internal ChannelReader<LogEntry> GetReader()
        {
            return _channel.Reader;
        }

        internal string FilePath { get; }
        internal long MaxFileBytes { get; }
        internal int MaxRolls { get; }

        [LoggerMessage(EventId = 6001, Level = LogLevel.Error, Message = "Error publishing log entry to stream")]
        private static partial void LogStreamPublishError(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 6003, Level = LogLevel.Warning, Message = "HTTP log enqueue failed; dropping entry (level={Level}, category={Category}, eventId={EventId})")]
        private static partial void LogEnqueueDrop(ILogger logger, LogLevel level, string category, int eventId);
    }

    /// <summary>
    /// Background worker that drains the channel and persists to file in batches.
    /// Semantics: at-least-once persistence.
    /// - If a failure occurs mid-batch, some lines may already be written when an exception is thrown.
    ///   The whole batch is then retried from memory, which can cause duplicate lines (at-least-once).
    /// - On write failure, lines are buffered in an in-memory backlog and retried periodically.
    ///   If the backlog exceeds its maximum size, oldest lines are dropped and drop metrics are recorded.
    /// - On shutdown, attempts to flush the channel tail and any backlog best-effort before exit.
    /// </summary>
    public sealed partial class FileBackedLogWriter(FileBackedHttpLogStore store, IOptions<HttpLogOptions> opts) : BackgroundService
    {
        private readonly FileBackedHttpLogStore _store = store;
        private readonly IOptions<HttpLogOptions> _opts = opts;
        private readonly List<LogEntry> _backlog = new(capacity: 2048);
        private int MaxBacklog => Math.Max(1000, _opts.Value.MaxQueue);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            EnsureDirectoryExists(_store.FilePath);

            ChannelReader<LogEntry> reader = _store.GetReader();
            int flushInterval = Math.Max(50, _opts.Value.FlushIntervalMs);
            List<LogEntry> batch = new(capacity: 512);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReadBatchAsync(reader, batch, stoppingToken).ConfigureAwait(false);
                    if (await HandleBacklogFirstAsync(batch, flushInterval, stoppingToken).ConfigureAwait(false))
                    {
                        continue;
                    }
                    if (await HandleCurrentBatchAsync(batch, flushInterval, stoppingToken).ConfigureAwait(false))
                    {
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    await SafeFlushTailAsync(reader, batch).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex)
                {
                    LogWriteBatchError(_store.Logger, ex);
                    batch.Clear();
                    await Task.Delay(flushInterval, stoppingToken).ConfigureAwait(false);
                }
            }

            await SafeFlushTailAsync(reader, new List<LogEntry>(capacity: 512)).ConfigureAwait(false);
        }

        private async Task<bool> HandleBacklogFirstAsync(List<LogEntry> batch, int flushInterval, CancellationToken ct)
        {
            if (_backlog.Count > 0 && batch.Count == 0)
            {
                if (await TryAppendAsync(_backlog, ct).ConfigureAwait(false))
                {
                    _backlog.Clear();
                }
                else
                {
                    await Task.Delay(flushInterval, ct).ConfigureAwait(false);
                }
                return true;
            }
            return false;
        }

        private async Task<bool> HandleCurrentBatchAsync(List<LogEntry> batch, int flushInterval, CancellationToken ct)
        {
            if (batch.Count == 0)
            {
                await Task.Delay(flushInterval, ct).ConfigureAwait(false);
                return true;
            }

            if (_backlog.Count > 0)
            {
                _backlog.AddRange(batch);
                batch.Clear();
                if (await TryAppendAsync(_backlog, ct).ConfigureAwait(false))
                {
                    _backlog.Clear();
                }
                else
                {
                    TrimBacklogIfNeeded();
                    await Task.Delay(flushInterval, ct).ConfigureAwait(false);
                }
                return true;
            }

            if (!await TryAppendAsync(batch, ct).ConfigureAwait(false))
            {
                _backlog.AddRange(batch);
                TrimBacklogIfNeeded();
                await Task.Delay(flushInterval, ct).ConfigureAwait(false);
            }
            batch.Clear();
            return false;
        }

        private static void EnsureDirectoryExists(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }
        }

        private static async Task ReadBatchAsync(ChannelReader<LogEntry> reader, List<LogEntry> batch, CancellationToken ct)
        {
            if (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out LogEntry? item))
                {
                    batch.Add(item);
                    if (batch.Count >= 1024)
                    {
                        break;
                    }
                }
            }
        }

        private async Task SafeFlushTailAsync(ChannelReader<LogEntry> reader, List<LogEntry> tail)
        {
            try
            {
                while (reader.TryRead(out LogEntry? item))
                {
                    tail.Add(item);
                    if (tail.Count >= 1024)
                    {
                        if (!await TryAppendAsync(tail, CancellationToken.None).ConfigureAwait(false))
                        {
                            // Could not flush; move to backlog and break
                            _backlog.AddRange(tail);
                            tail.Clear();
                            break;
                        }
                        tail.Clear();
                    }
                }
                if (tail.Count > 0)
                {
                    if (!await TryAppendAsync(tail, CancellationToken.None).ConfigureAwait(false))
                    {
                        _backlog.AddRange(tail);
                        tail.Clear();
                    }
                    else
                    {
                        tail.Clear();
                    }
                }

                if (_backlog.Count > 0)
                {
                    _ = await TryAppendAsync(_backlog, CancellationToken.None).ConfigureAwait(false);
                    _backlog.Clear();
                }
            }
            catch (Exception ex)
            {
                LogWriteBatchError(_store.Logger, ex);
            }
        }

        private async Task<bool> TryAppendAsync(List<LogEntry> entries, CancellationToken ct)
        {
            try
            {
                await AppendBatchAsync(entries, ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                LogWriteBatchError(_store.Logger, ex);
                return false;
            }
        }

        private void TrimBacklogIfNeeded()
        {
            int max = MaxBacklog;
            if (_backlog.Count <= max)
            {
                return;
            }
            int toDrop = _backlog.Count - max;
            for (int i = 0; i < toDrop; i++)
            {
                LogEntry e = _backlog[i];
                LogStoreMetrics.LogDrops.Add(1);
                LogStoreMetrics.LogDropsByReason.Add(1, new KeyValuePair<string, object?>("reason", "fs_backlog_full"), new KeyValuePair<string, object?>("store", "file"));
                LogStoreMetrics.LogDropsByLevel.Add(1, new KeyValuePair<string, object?>("level", e.Level.ToString()), new KeyValuePair<string, object?>("store", "file"));
            }
            _backlog.RemoveRange(0, toDrop);
        }

        /// <summary>
        /// Append a batch of entries to the current log file and flushes stream and file.
        /// Note: at-least-once semantics. If an exception happens after some lines have been written,
        /// the caller will retry the entire batch later, resulting in possible duplicates.
        /// </summary>
        private async Task AppendBatchAsync(List<LogEntry> batch, CancellationToken ct)
        {
            string path = _store.FilePath;

            LogFileRolling.RollIfNeeded(path, _store.MaxFileBytes, _store.MaxRolls, _store.Logger);

            using FileStream fs = new(path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
            using StreamWriter sw = new(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            foreach (LogEntry e in batch)
            {
                string line = LogLineFormatter.FormatLine(e);
                await sw.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            }
            await sw.FlushAsync(ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }

        [LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Error writing HTTP log batch to file")]
        private static partial void LogWriteBatchError(ILogger logger, Exception ex);
    }
}
