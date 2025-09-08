using System;
using System.Linq;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkRaw.Lib.Logging;

namespace XRoadFolkWeb.Tests
{
    public class ComputeKindTests
    {
        private sealed class DummyStore : IHttpLogStore
        {
            public readonly System.Collections.Generic.List<LogEntry> Entries = new();
            public void Add(LogEntry e) => Entries.Add(e);
            public void Clear() => Entries.Clear();
            public System.Collections.Generic.IReadOnlyList<LogEntry> GetAll() => Entries;
        }

        private sealed class DummyFeed : ILogFeed
        {
            public (System.Threading.Channels.ChannelReader<LogEntry> Reader, Guid SubscriptionId) Subscribe() => throw new NotImplementedException();
            public void Unsubscribe(Guid id) => throw new NotImplementedException();
            public void Publish(LogEntry entry) { }
        }

        private ILogger CreateLogger(DummyStore store, out InMemoryHttpLogLoggerProvider provider)
        {
            provider = new InMemoryHttpLogLoggerProvider(store, new DummyFeed());
            return provider.CreateLogger("TestCategory");
        }

        [Fact]
        public void Soap_EventIds_Classify_As_Soap()
        {
            var store = new DummyStore();
            var logger = CreateLogger(store, out var provider);
            using (provider as IDisposable)
            {
                logger.LogDebug(SafeSoapLogger.SoapGeneralEvent, "SOAP test");
            }
            store.Entries.Should().ContainSingle(e => e.Kind == "soap");
        }

        [Fact]
        public void Http_Category_Classifies_As_Http()
        {
            var store = new DummyStore();
            var provider = new InMemoryHttpLogLoggerProvider(store, new DummyFeed());
            using (provider as IDisposable)
            {
                var logger = provider.CreateLogger("System.Net.Http.HttpClient.SomeClient");
                logger.LogInformation("HTTP GET https://example");
            }
            store.Entries.Should().ContainSingle(e => e.Kind == "http");
        }

        [Fact]
        public void Message_Starting_With_Http_Classifies_As_Http()
        {
            var store = new DummyStore();
            var logger = CreateLogger(store, out var provider);
            using (provider as IDisposable)
            {
                logger.LogInformation("HTTP 200 OK");
            }
            store.Entries.Should().ContainSingle(e => e.Kind == "http");
        }

        [Fact]
        public void Default_Falls_Back_To_App()
        {
            var store = new DummyStore();
            var logger = CreateLogger(store, out var provider);
            using (provider as IDisposable)
            {
                logger.LogInformation("Something else");
            }
            store.Entries.Should().ContainSingle(e => e.Kind == "app");
        }
    }
}
