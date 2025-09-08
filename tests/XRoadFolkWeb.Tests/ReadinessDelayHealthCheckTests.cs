using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class ReadinessDelayHealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ReadinessDelayHealthCheckTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    // Configure a small readiness delay (5s) for the test
                    cfg.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["Health:ReadinessDelaySeconds"] = "5"
                    });
                });
            });
        }

        [Fact]
        public async Task Readiness_Returns_Unhealthy_Before_Delay_And_Healthy_After()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var early = await client.GetAsync("/health/ready");
            // Expect 503 (unhealthy) before 5 seconds have elapsed
            early.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            await Task.Delay(5200); // wait slightly over 5 seconds

            var later = await client.GetAsync("/health/ready");
            later.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Liveness_Is_Not_Delayed()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var live = await client.GetAsync("/health/live");
            live.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
