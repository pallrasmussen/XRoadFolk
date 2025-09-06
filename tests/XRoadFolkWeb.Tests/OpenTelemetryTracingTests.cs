using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class OpenTelemetryTracingTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public OpenTelemetryTracingTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(_ => { });
        }

        [Fact]
        public async Task Index_Returns_200_And_TraceId_Header()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var resp = await client.GetAsync("/");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            resp.Headers.Contains("traceparent").Should().BeTrue();
        }
    }
}
