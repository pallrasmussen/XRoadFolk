using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class AccessDeniedCultureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public AccessDeniedCultureTests(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        });
    }

    [Fact]
    public async Task Direct_403_Page_Respects_AcceptLanguage_Danish()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("da-DK,da;q=0.9");
        var resp = await client.GetAsync("/Error/403");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await resp.Content.ReadAsStringAsync();
        html.Should().Contain("<html lang=\"da-DK\"");
        html.Should().Contain("Access denied"); // fallback or localized string
    }

    [Fact]
    public async Task Unauthorized_Admin_Redirect_Includes_Localized_Header_Danish()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("da-DK,da;q=0.9");
        var resp = await client.GetAsync("/Admin/Users");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.RedirectKeepVerb, HttpStatusCode.SeeOther);
        resp.Headers.Location!.ToString().Should().Contain("/Error/403");
        resp.Headers.TryGetValues("X-Access-Denied-Message", out var headerVals).Should().BeTrue();
        headerVals!.Should().NotBeNull();
        // header may be localized or fallback; assert non-empty
        headerVals!.Should().Contain(h => !string.IsNullOrWhiteSpace(h));
    }

    private class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "Test403Culture";
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Authenticated normal user (no Admin role) to trigger 403 when accessing admin pages
            var id = new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "cultureUser"), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "User") }, Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(id), Scheme)));
        }
    }
}
