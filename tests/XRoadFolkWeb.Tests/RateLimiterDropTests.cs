using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Tests
{
    public class RateLimiterDropTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public RateLimiterDropTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["Features:Logs:Enabled"] = "true",
                        ["HttpLogs:MaxWritesPerSecond"] = "1" // very low to force drops
                    };
                    cfg.AddInMemoryCollection(dict);
                });
            });
        }

        [Fact]
        public async Task Low_Rate_Limits_Drop_Entries()
        {
            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IHttpLogStore>();
            store.Clear();

            // Fire a burst > 1 log quickly
            for (int i = 0; i < 10; i++)
            {
                store.Add(new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Level = LogLevel.Debug,
                    Kind = "app",
                    Category = "RateTest",
                    EventId = i,
                    Message = "Burst" + i
                });
            }
            await Task.Delay(300); // allow ingest loop

            var all = store.GetAll();
            all.Count.Should().BeLessThan(10); // some dropped
        }
    }
}
