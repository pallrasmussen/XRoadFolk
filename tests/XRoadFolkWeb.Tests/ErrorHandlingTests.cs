using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class ErrorHandlingTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ErrorHandlingTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.Configure(app =>
                {
                    app.Use(next => new RequestDelegate(async ctx =>
                    {
                        if (string.Equals(ctx.Request.Path, "/throw", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("boom");
                        }
                        await next(ctx);
                    }));
                });
            });
        }

        [Fact]
        public async Task ErrorHandler_Returns_Html_When_Accept_TextHtml()
        {
            using HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            using var req = new HttpRequestMessage(HttpMethod.Get, "/throw");
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            var resp = await client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            resp.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
            string html = await resp.Content.ReadAsStringAsync();
            html.Should().Contain("Trace Id:");
        }

        [Fact]
        public async Task ErrorHandler_Returns_ProblemJson_When_Accept_Json()
        {
            using HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            using var req = new HttpRequestMessage(HttpMethod.Get, "/throw");
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var resp = await client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            root.GetProperty("status").GetInt32().Should().Be(500);
            root.TryGetProperty("title", out _).Should().BeTrue();
            root.TryGetProperty("traceId", out _).Should().BeTrue();
        }
    }
}
