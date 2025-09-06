using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests
{
    public class PermissionsPolicyTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public PermissionsPolicyTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory.WithWebHostBuilder(_ => { });
        }

        [Fact]
        public async Task HomePage_Emits_Valid_Permissions_Policy_Header()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var resp = await client.GetAsync("/");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            resp.Headers.Contains("Permissions-Policy").Should().BeTrue();

            var value = string.Join(" ", resp.Headers.GetValues("Permissions-Policy"));
            value.Should().NotContain("() ,");
            value.Should().NotContain(", ()");
            value.Should().NotContain("(), ()");
            value.Should().MatchRegex(@"^(?:[a-z-]+=\(.*?\))(?:,\s*[a-z-]+=\(.*?\))*$");
        }
    }
}
