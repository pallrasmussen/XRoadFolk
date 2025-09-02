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

        public (ChannelReader<LogEntry> Reader, Guid SubscriptionId) Subscribe()
        {
            Guid id = Guid.NewGuid();
            Channel<LogEntry> channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
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
                    // For unbounded channels, TryWrite only returns false if the channel is completed/faulted
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
