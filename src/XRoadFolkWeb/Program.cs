using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkWeb.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Load default X-Road settings from library (robust resource lookup + file fallback)
builder.Configuration.AddXRoadDefaultSettings();

// Allow overrides from Web appsettings/UserSecrets/ENV
builder.Configuration.AddEnvironmentVariables();

// Services
builder.Services.AddApplicationServices(builder.Configuration);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
// Re-add our custom provider after ClearProviders
builder.Services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));

WebApplication app = builder.Build();

// Pipeline
app.ConfigureRequestPipeline();

// Localization middleware (already applied in ConfigureRequestPipeline; harmless if duplicated)
RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(locOpts);

app.Run();
