using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class CultureFallbackTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public CultureFallbackTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["Localization:DefaultCulture"] = "en-US",
                        ["Localization:SupportedCultures:0"] = "en-US",
                        ["Localization:SupportedCultures:1"] = "fo-FO",
                        ["Localization:SupportedCultures:2"] = "da-DK",
                        ["Localization:FallbackMap:en"] = "en-US",
                        ["Localization:FallbackMap:fo"] = "fo-FO"
                    };
                    cfg.AddInMemoryCollection(dict);
                });
            });
        }

        [Theory]
        [InlineData("en", "en-US")]
        [InlineData("fo", "fo-FO")]
        [InlineData("en-GB", "en-US")] // parent/ same-language fallback
        public async Task Neutral_Or_Parent_Culture_Falls_Back(string accept, string expected)
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            using var req = new HttpRequestMessage(HttpMethod.Get, "/");
            req.Headers.Add("Accept-Language", accept);
            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            // Response contains localized app name; ensure culture cookie set to expected
            resp.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
            cookies!.Any(c => c.Contains($"c={expected}", StringComparison.OrdinalIgnoreCase) || c.Contains(expected, StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        }
    }
}
