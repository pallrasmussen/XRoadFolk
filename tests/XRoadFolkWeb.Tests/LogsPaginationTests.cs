using System;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
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
    public class LogsPaginationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public LogsPaginationTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["Features:Logs:Enabled"] = "true"
                    };
                    cfg.AddInMemoryCollection(dict);
                });
            });
        }

        [Fact]
        public async Task Pagination_Works_As_Expected()
        {
            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IHttpLogStore>();
            store.Clear();

            // Insert 5 logs
            for (int i = 0; i < 5; i++)
            {
                store.Add(new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow.AddSeconds(i * -1),
                    Level = LogLevel.Information,
                    Kind = "app",
                    Category = "Test",
                    EventId = i + 1,
                    Message = "M" + i
                });
            }

            using var client = _factory.CreateClient();
            var page1 = await client.GetFromJsonAsync<JsonElement>("/logs?page=1&pageSize=2");
            var page2 = await client.GetFromJsonAsync<JsonElement>("/logs?page=2&pageSize=2");
            var page3 = await client.GetFromJsonAsync<JsonElement>("/logs?page=3&pageSize=2");

            int total = page1.GetProperty("total").GetInt32();
            total.Should().Be(5);
            page1.GetProperty("items").GetArrayLength().Should().Be(2);
            page2.GetProperty("items").GetArrayLength().Should().Be(2);
            page3.GetProperty("items").GetArrayLength().Should().Be(1);
            page1.GetProperty("totalPages").GetInt32().Should().Be(3);
        }
    }
}
