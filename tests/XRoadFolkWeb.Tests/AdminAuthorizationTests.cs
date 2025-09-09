using System;
using System.Net;
using System.Security.Claims;
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

public class AdminAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public AdminAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(TestAdminAuthHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>(TestAdminAuthHandler.Scheme, _ => { });
                services.AddAuthentication(TestUserAuthHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestUserAuthHandler>(TestUserAuthHandler.Scheme, _ => { });
            });
        });
    }

    [Fact]
    public async Task NonAdmin_User_Cannot_Access_Admin_Pages()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Auth-Role", "User");
        var resp = await client.GetAsync("/Admin/Users");
        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location!.ToString().Should().Contain("/Error/403");
    }

    [Fact]
    public async Task Admin_User_Can_Access_Admin_Pages()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Auth-Role", "Admin");
        var resp = await client.GetAsync("/Admin/Users");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await client.GetAsync("/Admin/RoleAudit");
        audit.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private class TestAdminAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "TestAdmin";
        public TestAdminAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.Name, "admin@test"), new Claim(ClaimTypes.Role, "Admin") };
            var id = new ClaimsIdentity(claims, Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), Scheme)));
        }
    }

    private class TestUserAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "TestUser";
        public TestUserAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.Name, "user@test"), new Claim(ClaimTypes.Role, "User") };
            var id = new ClaimsIdentity(claims, Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), Scheme)));
        }
    }
}
