using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class CspHeaderTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public CspHeaderTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(_ => { });
        }

        [Fact]
        public async Task HomePage_Emits_Csp_Header_With_Nonce()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var resp = await client.GetAsync("/");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            resp.Headers.Contains("Content-Security-Policy").Should().BeTrue();

            var csp = string.Join(" ", resp.Headers.GetValues("Content-Security-Policy"));
            csp.Should().Contain("script-src");
            csp.Should().Contain("style-src");
            csp.Should().Contain("'self'");
            csp.Should().Contain("nonce-");
        }

        [Fact]
        public async Task LogsPage_Inline_Script_Has_Nonce_Attribute()
        {
            using var client = _factory.CreateClient();
            var resp = await client.GetAsync("/Logs/App");
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync();

            // The _LogsViewer partial sets a nonce variable and uses it on a script tag
            html.Should().Contain("<script nonce=");
        }
    }
}
