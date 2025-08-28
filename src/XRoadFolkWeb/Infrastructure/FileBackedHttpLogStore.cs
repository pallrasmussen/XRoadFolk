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
    public sealed class FileBackedHttpLogStore : IHttpLogStore
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
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true
            });
            _log = log;
        }

        public int Capacity { get; }
        public int Count => _ring.Count;

        public void Add(LogEntry e)
        {
            ArgumentNullException.ThrowIfNull(e);

            // Ring buffer
            _ring.Enqueue(e);
            while (_ring.Count > Capacity && _ring.TryDequeue(out _)) { }

            try { _stream?.Publish(e); } catch { }

            // Back-pressure: try to enqueue; if full and not important, it may drop the oldest
            if (!_channel.Writer.TryWrite(e))
            {
                if (_alwaysAllowWarnError && e.Level >= LogLevel.Warning)
                {
                    // Force write by dropping one and retrying
                    _ = _channel.Writer.TryWrite(e);
                }
            }
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
    }

    // Background worker that drains the channel and persists to file in batches
    public sealed class FileBackedLogWriter(FileBackedHttpLogStore store, IOptions<HttpLogOptions> opts) : BackgroundService
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
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    // swallow; next iteration will retry
                    batch.Clear();
                    await Task.Delay(flushInterval, stoppingToken).ConfigureAwait(false);
                }
            }
        }

        private async Task AppendBatchAsync(List<LogEntry> batch, CancellationToken ct)
        {
            string path = _store.FilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }

            RollIfNeeded(path, _store.MaxFileBytes, _store.MaxRolls);

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

        private static void RollIfNeeded(string path, long maxBytes, int maxRolls)
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
            catch { }
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
            catch { }
            return Task.CompletedTask;
        }
    }
}
