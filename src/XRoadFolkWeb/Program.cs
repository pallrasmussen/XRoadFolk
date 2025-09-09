using XRoadFolkWeb.Extensions;
using XRoadFolkWeb.Infrastructure;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Suppress Kestrel "Server" header
builder.WebHost.ConfigureKestrel(opts => opts.AddServerHeader = false);

// Load default X-Road settings from library (robust resource lookup + file fallback)
builder.Configuration.AddXRoadDefaultSettings();

// Allow overrides from Web appsettings/UserSecrets/ENV
builder.Configuration.AddEnvironmentVariables();

// Detect IIS / IIS Express hosting (environment variables set by ANCM)
bool runningInIis = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH"));

if (!runningInIis)
{
    // Self-hosted Kestrel -> use Negotiate handler
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}
else
{
    // IIS (Express) integrated pipeline already performs Windows auth; just register IISDefaults
    builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme);
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireAssertion(ctx =>
        ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase)) == true));

    // Composite user-access policy where Admin implicitly satisfies normal user access.
    options.AddPolicy("UserAccess", p => p.RequireAssertion(ctx =>
        ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase) || c.Value.Equals(AppRoles.User, StringComparison.OrdinalIgnoreCase))) == true));

    // Default for bare [Authorize] attributes.
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => ctx.User?.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase) || c.Value.Equals(AppRoles.User, StringComparison.OrdinalIgnoreCase))) == true)
        .Build();
});

// Services
builder.Services.AddApplicationServices(builder.Configuration);

// Custom role store & claims transformation + seeding
builder.Services.AddAppRoleInfrastructure(builder.Configuration);

WebApplication app = builder.Build();

// NOTE: Authentication/Authorization middleware is added inside ConfigureRequestPipeline after UseRouting

// Pipeline
app.ConfigureRequestPipeline();

await app.RunAsync().ConfigureAwait(false);

public partial class Program { }
