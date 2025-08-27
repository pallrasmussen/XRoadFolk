using XRoadFolkWeb.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Load default X-Road settings from library (robust resource lookup + file fallback)
builder.Configuration.AddXRoadDefaultSettings();

// Allow overrides from Web appsettings/UserSecrets/ENV
builder.Configuration.AddEnvironmentVariables();

// Services
builder.Services.AddApplicationServices(builder.Configuration);

WebApplication app = builder.Build();

// Pipeline
app.ConfigureRequestPipeline();

app.Run();
