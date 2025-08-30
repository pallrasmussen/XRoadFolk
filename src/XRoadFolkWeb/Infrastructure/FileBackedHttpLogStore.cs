using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace XRoadFolkWeb.Infrastructure
{
    // File-backed store with bounded channel and background writer.
    // - Maintains an in-memory ring buffer (Capacity)
    // - Persists entries to rolling log files
    // - Uses a bounded channel to provide back-pressure; warnings/errors bypass drops
    public sealed partial class FileBackedHttpLogStore : IHttpLogStore
    {
        private readonly ConcurrentQueue<LogEntry> _ring = new();
        private readonly ILogFeed? _stream;
        private readonly int _maxQueue;
        private readonly bool _alwaysAllowWarnError;

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
            _alwaysAllowWarnError = cfg.AlwaysAllowWarningsAndErrors;
            _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(_maxQueue)
            {
                // Use Wait so writes fail (TryWrite=false) when full; we will explicitly manage eviction below
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true
            });
            _log = log;
        }

        internal ILogger Logger => _log;

        public int Capacity { get; }
        public int Count => _ring.Count;

        public void Add(LogEntry e)
        {
            ArgumentNullException.ThrowIfNull(e);

            // Ring buffer
            _ring.Enqueue(e);
            while (_ring.Count > Capacity && _ring.TryDequeue(out _)) { }

            try { _stream?.Publish(e); }
            catch (Exception ex)
            {
                LogStreamPublishError(_log, ex);
            }

            // Back-pressure handling:
            // - For low-severity, best-effort enqueue; if full, drop silently.
            // - For Warning+ (if enabled), ensure space by evicting oldest items and write.
            if (_channel.Writer.TryWrite(e))
            {
                return;
            }

            if (_alwaysAllowWarnError && e.Level >= LogLevel.Warning)
            {
                // Create room by evicting oldest queued entries (if any) and retry a few times
                for (int i = 0; i < 4; i++)
                {
                    _ = _channel.Reader.TryRead(out _); // discard one if available
                    if (_channel.Writer.TryWrite(e))
                    {
                        return;
                    }
                }
                // Last attempt: block very briefly by yielding to let reader drain
                Thread.Yield();
                _ = _channel.Writer.TryWrite(e); // best effort final try
            }
            // else: low severity and full -> drop
        }

        public void Clear()
        {
            while (_ring.TryDequeue(out _)) { }
            // Note: file is not truncated here; call FileBackedLogWriter.ClearAsync for persistence if needed
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
    }

    // Background worker that drains the channel and persists to file in batches
    public sealed partial class FileBackedLogWriter(FileBackedHttpLogStore store, IOptions<HttpLogOptions> opts) : BackgroundService
    {
        private readonly FileBackedHttpLogStore _store = store;
        private readonly IOptions<HttpLogOptions> _opts = opts;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ChannelReader<LogEntry> reader = _store.GetReader();
            int flushInterval = Math.Max(50, _opts.Value.FlushIntervalMs);
            List<LogEntry> batch = new(capacity: 512);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for data or time
                    if (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
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

                    if (batch.Count == 0)
                    {
                        await Task.Delay(flushInterval, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    await AppendBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                    batch.Clear();
                }
                catch (OperationCanceledException)
                {
                    // Flush any buffered entries before exiting
                    try
                    {
                        if (batch.Count > 0)
                        {
                            await AppendBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
                            batch.Clear();
                        }
                    }
                    catch { }
                    break;
                }
                catch (Exception ex)
                {
                    // Log and retry later
                    LogWriteBatchError(_store.Logger, ex);
                    batch.Clear();
                    await Task.Delay(flushInterval, stoppingToken).ConfigureAwait(false);
                }
            }

            // Final drain on shutdown: write any remaining items still in the channel
            try
            {
                List<LogEntry> tail = new(capacity: 512);
                while (reader.TryRead(out LogEntry? item))
                {
                    tail.Add(item);
                    if (tail.Count >= 1024)
                    {
                        await AppendBatchAsync(tail, CancellationToken.None).ConfigureAwait(false);
                        tail.Clear();
                    }
                }
                if (tail.Count > 0)
                {
                    await AppendBatchAsync(tail, CancellationToken.None).ConfigureAwait(false);
                    tail.Clear();
                }
            }
            catch { }
        }

        private async Task AppendBatchAsync(List<LogEntry> batch, CancellationToken ct)
        {
            string path = _store.FilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }

            RollIfNeeded(path, _store.MaxFileBytes, _store.MaxRolls, _store.Logger);

            using FileStream fs = new(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using StreamWriter sw = new(fs, Encoding.UTF8);
            foreach (LogEntry e in batch)
            {
                string line = $"{e.Timestamp:O}\t{e.Level}\t{e.Kind}\t{e.Category}\t{e.EventId}\t{e.Message}\t{e.Exception}";
                await sw.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            }
            await sw.FlushAsync(ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }

        private static void RollIfNeeded(string path, long maxBytes, int maxRolls, ILogger log)
        {
            try
            {
                FileInfo fi = new(path);
                if (fi.Exists && fi.Length > maxBytes)
                {
                    for (int i = maxRolls; i >= 1; i--)
                    {
                        string from = i == 1 ? path : path + "." + (i - 1);
                        string to = path + "." + i;
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
            catch (Exception ex)
            {
                LogRollError(log, ex, path);
            }
        }

        // Optional helper to clear persisted logs
        public Task ClearAsync()
        {
            try
            {
                string path = _store.FilePath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                for (int i = 1; i <= _store.MaxRolls; i++)
                {
                    string roll = path + "." + i;
                    if (File.Exists(roll))
                    {
                        File.Delete(roll);
                    }
                }
            }
            catch (Exception ex)
            {
                LogClearError(_store.Logger, ex);
            }
            return Task.CompletedTask;
        }

        [LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Error writing HTTP log batch to file")]
        private static partial void LogWriteBatchError(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 6003, Level = LogLevel.Error, Message = "Error rolling HTTP log files for path '{Path}'")]
        private static partial void LogRollError(ILogger logger, Exception ex, string Path);

        [LoggerMessage(EventId = 6004, Level = LogLevel.Error, Message = "Error clearing persisted HTTP logs")]
        private static partial void LogClearError(ILogger logger, Exception ex);
    }
}
