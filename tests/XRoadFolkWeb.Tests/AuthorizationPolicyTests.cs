using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class AuthorizationPolicyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public AuthorizationPolicyTests(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.Scheme)
                    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        });
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("User", HttpStatusCode.OK)]
    [InlineData("Guest", HttpStatusCode.Forbidden)]
    public async Task DefaultPolicy_Allows_Admin_Or_User(string roleHeader, HttpStatusCode expected)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Roles", roleHeader);
        var resp = await client.GetAsync("/");
        resp.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("User", HttpStatusCode.Forbidden)]
    public async Task AdminOnly_Requires_Admin(string roleHeader, HttpStatusCode expected)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Roles", roleHeader);
        var resp = await client.GetAsync("/Admin/Users");
        resp.StatusCode.Should().Be(expected);
    }
}
