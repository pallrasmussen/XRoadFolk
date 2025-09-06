using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class LogsEndpointsSecurityTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public LogsEndpointsSecurityTests(WebApplicationFactory<Program> factory)
        {
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
        public async Task Logs_Post_Endpoints_Require_Antiforgery()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            using var empty = new StringContent("");
            var clear = await client.PostAsync("/logs/clear", empty);
            clear.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var write = await client.PostAsJsonAsync("/logs/write", new { level = "Information", message = "test" });
            write.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Logs_Post_Endpoints_Succeed_With_Antiforgery()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // get antiforgery token from home page meta
            var home = await client.GetAsync("/");
            home.EnsureSuccessStatusCode();
            var html = await home.Content.ReadAsStringAsync();
            var token = ExtractMetaToken(html);
            token.Should().NotBeNullOrEmpty();

            using var req = new HttpRequestMessage(HttpMethod.Post, "/logs/clear");
            req.Headers.Add("RequestVerificationToken", token);
            req.Content = new StringContent("");
            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var req2 = new HttpRequestMessage(HttpMethod.Post, "/logs/write");
            req2.Headers.Add("RequestVerificationToken", token);
            req2.Content = JsonContent.Create(new { level = "Information", message = "ok" });
            var resp2 = await client.SendAsync(req2);
            resp2.EnsureSuccessStatusCode();
        }

        private static string? ExtractMetaToken(string html)
        {
            const string marker = "name=\"request-verification-token\"";
            int i = html.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
            if (i < 0)
            {
                return null;
            }
            int c = html.IndexOf("content=\"", i, System.StringComparison.OrdinalIgnoreCase);
            if (c < 0)
            {
                return null;
            }
            c += "content=\"".Length;
            int end = html.IndexOf('"', c);
            if (end <= c)
            {
                return null;
            }
            return html.Substring(c, end - c);
        }
    }
}
