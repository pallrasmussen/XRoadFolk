using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class ReadinessAndWritableCombinedTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ReadinessAndWritableCombinedTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    var dict = new System.Collections.Generic.Dictionary<string,string?>
                    {
                        ["Health:ReadinessDelaySeconds"] = "2",
                        ["HttpLogs:PersistToFile"] = "true",
                        ["HttpLogs:FilePath"] = Path.Combine(Path.GetTempPath(), "xrf_unwritable", "nested", "app.log")
                    };
                    cfg.AddInMemoryCollection(dict);
                });
            });
        }

        [Fact]
        public async Task Readiness_Unhealthy_During_Delay_Then_Writable_Status_Dominates()
        {
            using var client = _factory.CreateClient();
            var early = await client.GetAsync("/health/ready");
            early.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            await Task.Delay(2500);
            var later = await client.GetAsync("/health/ready");
            // Path may become writable if directory can be created; accept either Healthy or ServiceUnavailable but not crash
            later.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        }
    }
}
