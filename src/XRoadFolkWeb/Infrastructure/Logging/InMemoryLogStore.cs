using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Infrastructure.Logging;

public sealed class InMemoryLogStore
{
    public sealed record LogEntry(long Id, DateTimeOffset Timestamp, LogLevel Level, string Category, EventId EventId, string Message, string? Exception);

    private readonly ConcurrentQueue<LogEntry> _queue = new();
    private readonly int _capacity;
    private long _lastId = 0;

    public InMemoryLogStore(int capacity = 500)
    {
        _capacity = Math.Max(50, capacity);
    }

    public long LastId => Interlocked.Read(ref _lastId);

    public void Add(DateTimeOffset ts, LogLevel level, string category, EventId eventId, string message, string? exception)
    {
        long id = Interlocked.Increment(ref _lastId);
        _queue.Enqueue(new LogEntry(id, ts, level, category, eventId, message, exception));
        while (_queue.Count > _capacity && _queue.TryDequeue(out _)) { }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        return _queue.ToArray();
    }

    public IReadOnlyList<LogEntry> GetSince(long lastId)
    {
        if (lastId <= 0) return Snapshot();
        return _queue.Where(e => e.Id > lastId).ToArray();
    }

    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}
