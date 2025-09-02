using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Infrastructure
{
    public interface ILogFeed
    {
        (ChannelReader<LogEntry> Reader, Guid SubscriptionId) Subscribe();
        void Unsubscribe(Guid id);
        void Publish(LogEntry entry);
    }

    public sealed class LogStreamBroadcaster(ILogger<LogStreamBroadcaster> logger) : ILogFeed
    {
        private readonly ILogger<LogStreamBroadcaster> _log = logger;
        private readonly ConcurrentDictionary<Guid, Channel<LogEntry>> _subscribers = new();
        private const int SubscriberQueueCapacity = 1024; // bounded queue per subscriber to avoid unbounded memory

        public (ChannelReader<LogEntry> Reader, Guid SubscriptionId) Subscribe()
        {
            Guid id = Guid.NewGuid();
            Channel<LogEntry> channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(SubscriberQueueCapacity)
            {
                SingleReader = true,               // one reader per subscription
                SingleWriter = false,              // may publish from multiple threads
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest, // back-pressure: drop oldest messages for slow clients
            });
            _subscribers[id] = channel;
            return (channel.Reader, id);
        }

        public void Unsubscribe(Guid id)
        {
            if (_subscribers.TryRemove(id, out Channel<LogEntry>? ch))
            {
                try { _ = ch.Writer.TryComplete(); }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "LogStreamBroadcaster: TryComplete failed for subscription {Id}", id);
                }
            }
        }

        public void Publish(LogEntry entry)
        {
            foreach (KeyValuePair<Guid, Channel<LogEntry>> kv in _subscribers)
            {
                Channel<LogEntry> ch = kv.Value;
                try
                {
                    // With bounded channel and DropOldest, TryWrite only fails if channel is completed/faulted
                    if (!ch.Writer.TryWrite(entry) || ch.Reader.Completion.IsCompleted)
                    {
                        if (_subscribers.TryRemove(kv.Key, out Channel<LogEntry>? dead))
                        {
                            try { _ = dead.Writer.TryComplete(); }
                            catch (Exception ex)
                            {
                                _log.LogDebug(ex, "LogStreamBroadcaster: TryComplete failed for removed subscription {Id}", kv.Key);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "LogStreamBroadcaster: Publish failed for subscription {Id}. Removing subscriber.", kv.Key);
                    if (_subscribers.TryRemove(kv.Key, out Channel<LogEntry>? dead))
                    {
                        try { _ = dead.Writer.TryComplete(); }
                        catch (Exception ex2)
                        {
                            _log.LogDebug(ex2, "LogStreamBroadcaster: TryComplete after failure failed for {Id}", kv.Key);
                        }
                    }
                }
            }
        }
    }
}
