using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class ImplicitAdminTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ImplicitAdminTests(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string,string?>
                {
                    ["AppRoles:ImplicitWindowsAdminEnabled"] = "true"
                };
                cfg.AddInMemoryCollection(dict);
            });
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(TestAdminGroupsHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAdminGroupsHandler>(TestAdminGroupsHandler.Scheme, _ => { });
            });
        });
    }

    [Fact]
    public async Task User_With_AdminGroupSid_Gets_Admin_Role()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync("/Admin/Users");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private class TestAdminGroupsHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "TestAdminGroups";
        public TestAdminGroupsHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, "test@domain.local"),
                new(ClaimTypes.GroupSid, "S-1-5-32-544")
            };
            var id = new ClaimsIdentity(claims, Scheme);
            var principal = new ClaimsPrincipal(id);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
