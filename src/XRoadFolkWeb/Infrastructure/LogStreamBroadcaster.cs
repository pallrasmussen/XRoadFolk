using System.Collections.Concurrent;
using System.Threading.Channels;

namespace XRoadFolkWeb.Infrastructure
{
    public interface ILogFeed
    {
        (ChannelReader<LogEntry> Reader, Guid SubscriptionId) Subscribe();
        void Unsubscribe(Guid id);
        void Publish(LogEntry entry);
    }

    public sealed class LogStreamBroadcaster : ILogFeed
    {
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
                try { _ = ch.Writer.TryComplete(); } catch { }
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
                            try { _ = dead.Writer.TryComplete(); } catch { }
                        }
                    }
                }
                catch
                {
                    if (_subscribers.TryRemove(kv.Key, out Channel<LogEntry>? dead))
                    {
                        try { _ = dead.Writer.TryComplete(); } catch { }
                    }
                }
            }
        }
    }
}
