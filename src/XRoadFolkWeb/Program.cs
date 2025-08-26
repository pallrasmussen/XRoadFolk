using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Infrastructure;
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
