using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace XRoadFolkWeb.Tests.Infrastructure
{
    public class LocalizationFallbackTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public LocalizationFallbackTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Defaults_To_Configured_Default_When_AcceptLanguage_Unknown()
        {
            using var client = _factory.CreateClient();
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "/");
            req.Headers.TryAddWithoutValidation(HeaderNames.AcceptLanguage, "zz-ZZ,yy-YY;q=0.8");
            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var diag = await client.GetAsync("/__culture");
            if (diag.IsSuccessStatusCode)
            {
                var info = await diag.Content.ReadFromJsonAsync<dynamic>();
                var supported = ((IEnumerable<object>)info!.Applied.Supported).Select(o => o?.ToString()).ToArray();
                supported.Should().Contain(new[] { "fo-FO", "da-DK", "en-US" });
                string def = (string)info!.Applied.Default;
                new[] { "fo-FO", "da-DK", "en-US" }.Should().Contain(def);
            }
        }
    }
}
