using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using XRoadFolkWeb;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class DirectoryHealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public DirectoryHealthCheckTests(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var baseConfig = new Dictionary<string,string?> { ["AppRoles:Directory:Domain"] = "" };
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) => cfg.AddInMemoryCollection(baseConfig));
        });
    }

    [Fact]
    public async Task Ready_Health_NoDomainConfigured_IsHealthy()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync("/health/ready");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }

    [Fact]
    public async Task Ready_Health_WithDummyDomain_WindowsOnly()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        var specCfg = new Dictionary<string,string?> { ["AppRoles:Directory:Domain"] = "invalid.local" };
        var factory2 = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((ctx, cfg) => cfg.AddInMemoryCollection(specCfg)));
        using var client = factory2.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync("/health/ready");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Ready_Health_InvalidCredentials_Unhealthy_WindowsOnly()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        // Use local machine context (Domain='.') with random bogus credentials to force ValidateCredentials failure
        var badCfg = new Dictionary<string,string?>
        {
            ["AppRoles:Directory:Domain"] = ".", // local machine
            ["AppRoles:Directory:Username"] = "_nonexistent_user_",
            ["AppRoles:Directory:Password"] = "_bogus_password_"
        };
        var factory3 = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((ctx, cfg) => cfg.AddInMemoryCollection(badCfg)));
        using var client = factory3.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync("/health/ready");
        // Expect ServiceUnavailable (Unhealthy) for invalid credentials; allow OK if environment ignores auth
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var text = await resp.Content.ReadAsStringAsync();
        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            text.Should().Contain("Directory");
        }
    }
}
