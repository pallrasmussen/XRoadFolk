using XRoadFolkWeb.Extensions;
using XRoadFolkWeb.Infrastructure;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// Create and configure the WebApplication host
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Basic server header suppression + safe header limits
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.AddServerHeader = false; // remove "Server" header
    if (opts.Limits is not null)
    {
        // Tighten request header limits to mitigate header abuse
        opts.Limits.MaxRequestHeadersTotalSize = 64 * 1024;
    }
});

// (Optional) keep HTTP/1.1 in Development to avoid IIS Express / Negotiate quirks
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.ConfigureKestrel(o => o.ConfigureEndpointDefaults(lo => lo.Protocols = HttpProtocols.Http1));
}

// App settings: default X-Road settings + environment variables
builder.Configuration.AddXRoadDefaultSettings();
builder.Configuration.AddEnvironmentVariables();

// Authentication: Windows SSO only via Negotiate
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = NegotiateDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = NegotiateDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = NegotiateDefaults.AuthenticationScheme;
}).AddNegotiate();

// IIS integration: rely on Integrated Windows Authentication
builder.Services.Configure<IISServerOptions>(o =>
{
    o.AutomaticAuthentication = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireAssertion(ctx =>
        ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase)) == true));

    options.AddPolicy("UserAccess", p => p.RequireAssertion(ctx =>
        ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase) || c.Value.Equals(AppRoles.User, StringComparison.OrdinalIgnoreCase))) == true));

    // Default: authenticated users with Admin or User role
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase) || c.Value.Equals(AppRoles.User, StringComparison.OrdinalIgnoreCase))) == true)
        .Build();
});

// Core application services and logging, health, HTTP logs, OpenTelemetry, etc.
builder.Services.AddApplicationServices(builder.Configuration);

// App role infrastructure (store, admin pages, audit, claims enrichment, health) 
builder.Services.AddAppRoleInfrastructure(builder.Configuration);

// Build and configure the HTTP request pipeline
WebApplication app = builder.Build();
app.ConfigureRequestPipeline();
await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Marker partial class for WebApplicationFactory-based integration tests.
/// </summary>
public partial class Program { }
