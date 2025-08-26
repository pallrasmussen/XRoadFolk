using System.Collections.Concurrent;
using System.Threading.Channels;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Infrastructure
{
    public interface ILogStream
    {
        (ChannelReader<LogEntry> Reader, Guid SubscriptionId) Subscribe();
        void Unsubscribe(Guid id);
        void Publish(LogEntry entry);
    }

    public sealed class LogStreamBroadcaster : ILogStream
    {
        private readonly ConcurrentDictionary<Guid, Channel<LogEntry>> _subscribers = new();

        public (ChannelReader<LogEntry> Reader, Guid SubscriptionId) Subscribe()
        {
            var id = Guid.NewGuid();
            var channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = true
            });
            _subscribers[id] = channel;
            return (channel.Reader, id);
        }

        public void Unsubscribe(Guid id)
        {
            if (_subscribers.TryRemove(id, out var ch))
            {
                try { ch.Writer.TryComplete(); } catch { }
            }
        }

        public void Publish(LogEntry entry)
        {
            foreach (var kv in _subscribers)
            {
                try { _ = kv.Value.Writer.TryWrite(entry); } catch { }
            }
        }
    }
}
