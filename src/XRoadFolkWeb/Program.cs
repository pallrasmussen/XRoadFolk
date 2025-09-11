using XRoadFolkWeb.Extensions;
using XRoadFolkWeb.Infrastructure;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Basic server header suppression + safe header limits
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.AddServerHeader = false;
    if (opts.Limits is not null)
    {
        opts.Limits.MaxRequestHeadersTotalSize = 64 * 1024;
    }
});

// (Optional) keep HTTP/1.1 in Development to avoid IIS Express / Negotiate quirks
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.ConfigureKestrel(o => o.ConfigureEndpointDefaults(lo => lo.Protocols = HttpProtocols.Http1));
}

builder.Configuration.AddXRoadDefaultSettings();
builder.Configuration.AddEnvironmentVariables();

// PURE WINDOWS SSO: Only Negotiate. No cookies, no policy schemes, no remember-me.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = NegotiateDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = NegotiateDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = NegotiateDefaults.AuthenticationScheme;
}).AddNegotiate();

builder.Services.Configure<IISServerOptions>(o =>
{
    o.AutomaticAuthentication = true; // rely on IIS integrated Windows auth
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireAssertion(ctx =>
        ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase)) == true));

    options.AddPolicy("UserAccess", p => p.RequireAssertion(ctx =>
        ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase) || c.Value.Equals(AppRoles.User, StringComparison.OrdinalIgnoreCase))) == true));

    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase) || c.Value.Equals(AppRoles.User, StringComparison.OrdinalIgnoreCase))) == true)
        .Build();
});

builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddAppRoleInfrastructure(builder.Configuration);

WebApplication app = builder.Build();
app.ConfigureRequestPipeline();
await app.RunAsync().ConfigureAwait(false);

public partial class Program { }
