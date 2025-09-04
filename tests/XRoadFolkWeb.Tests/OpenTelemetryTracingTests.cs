using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class OpenTelemetryTracingTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public OpenTelemetryTracingTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(_ => { });
        }

        [Fact]
        public async Task Tracing_Is_Registered_And_Basic_Request_Works()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var resp = await client.GetAsync("/");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<TracerProvider>();
            provider.Should().NotBeNull();
        }
    }
}
