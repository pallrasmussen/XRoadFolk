using System.Globalization;
using System.Net;
//using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkWeb.Infrastructure;
using System.Security.Cryptography.X509Certificates; // add
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib.Options;
using Microsoft.AspNetCore.Mvc;
using XRoadFolkWeb.Infrastructure;
using System.Threading.Channels;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Load default X-Road settings from library (robust resource lookup + file fallback)
Assembly libAsm = typeof(XRoadSettings).Assembly;
string? resName = libAsm.GetManifestResourceNames()
    .FirstOrDefault(n => n.EndsWith(".Resources.appsettings.xroad.json", StringComparison.OrdinalIgnoreCase));

if (resName is not null)
{
    using Stream? s = libAsm.GetManifestResourceStream(resName);
    if (s is not null) { builder.Configuration.AddJsonStream(s); }
}
else
{
    // Fallback: try file next to the lib assembly (dev scenarios)
    string? libDir = Path.GetDirectoryName(libAsm.Location);
    string? jsonPath = libDir is null ? null : Path.Combine(libDir, "Resources", "appsettings.xroad.json");
    if (jsonPath is not null && File.Exists(jsonPath))
    {
        _ = builder.Configuration.AddJsonFile(jsonPath, optional: true, reloadOnChange: false);
    }
}

// Allow overrides from Web appsettings/UserSecrets/ENV
builder.Configuration.AddEnvironmentVariables();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

// Configure DataAnnotations to use a shared resource by default
builder.Services
    .AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(opts =>
    {
        opts.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(XRoadFolkWeb.SharedResource)); // base name: XRoadFolkWeb.Resources.SharedResource
    });

// Localization resources
builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");

// Anti-forgery
builder.Services.AddAntiforgery(opts =>
{
    opts.Cookie.Name = "__Host.AntiForgery";
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.Cookie.SameSite = SameSiteMode.Lax;
    opts.HeaderName = "RequestVerificationToken"; // for AJAX if needed
});

// Bind XRoad settings from configuration (defaults come from the lib; Web can override via appsettings.json/UserSecrets/EnvVars)
builder.Services.Configure<XRoadSettings>(builder.Configuration.GetSection("XRoad"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<XRoadSettings>>().Value);

// Safe SOAP sanitization hook, same behavior as console app
bool maskTokens = builder.Configuration.GetValue("Logging:MaskTokens", true);
SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

// Centralized supported cultures from configuration (with safe fallbacks)
builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    (string defaultCulture, IReadOnlyList<CultureInfo> cultures) = AppLocalization.FromConfiguration(builder.Configuration);
    opts.DefaultRequestCulture = new RequestCulture(defaultCulture);
    opts.SupportedCultures = [.. cultures];
    opts.SupportedUICultures = [.. cultures];

    // Explicitly enable parent fallback
    opts.FallBackToParentCultures = true;
    opts.FallBackToParentUICultures = true;

    // Optional mapping from appsettings: Localization:FallbackMap
    var locCfg = builder.Configuration.GetSection("Localization").Get<LocalizationConfig>() ?? new LocalizationConfig();
    // Insert our best-match provider before the built-ins (Cookie/Query/Accept-Language)
    opts.RequestCultureProviders.Insert(0, new XRoadFolkWeb.Infrastructure.BestMatchRequestCultureProvider(
        opts.SupportedUICultures, locCfg.FallbackMap));
});

// Register IHttpClientFactory + handler with client certificate (resolve settings at runtime from DI)
builder.Services.AddHttpClient("XRoadFolk", (sp, c) =>
{
    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
    c.BaseAddress = new Uri(xr.BaseUrl, UriKind.Absolute);
    c.Timeout = TimeSpan.FromSeconds(xr.Http.TimeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    SocketsHttpHandler handler = new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 20,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();

    // Try to attach client certificate; if not configured, log and continue (useful for dev)
    try
    {
        X509Certificate2 cert = CertLoader.LoadFromConfig(xr.Certificate);
        handler.SslOptions.ClientCertificates ??= new X509CertificateCollection();
        _ = handler.SslOptions.ClientCertificates.Add(cert);
    }
    catch (Exception ex)
    {
        ILogger log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("XRoadCert");
        log.LogWarning(ex, "Client certificate not configured. Proceeding without certificate.");
    }

    IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
    bool bypass = cfg.GetValue("Http:BypassServerCertificateValidation", true);
    IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();
    if (env.IsDevelopment() && bypass)
    {
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
    }

    return handler;
});

// FolkRawClient via factory
builder.Services.AddScoped(sp =>
{
    HttpClient http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("XRoadFolk");
    ILogger<FolkRawClient> logger = sp.GetRequiredService<ILogger<FolkRawClient>>();
    IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
    return new FolkRawClient(
        httpClient: http,
        logger: logger,
        verbose: cfg.GetValue("Logging:Verbose", false),
        maskTokens: cfg.GetValue("Logging:MaskTokens", true),
        retryAttempts: cfg.GetValue("Retry:Http:Attempts", 3),
        retryBaseDelayMs: cfg.GetValue("Retry:Http:BaseDelayMs", 200),
        retryJitterMs: cfg.GetValue("Retry:Http:JitterMs", 250));
});

// Bind GetPerson request options (do not ValidateOnStart; we validate per-request in PeopleService)
builder.Services
    .AddOptions<GetPersonRequestOptions>()
    .Bind(builder.Configuration.GetSection("Operations:GetPerson:Request"));

// Register validator
builder.Services.AddSingleton<IValidateOptions<GetPersonRequestOptions>, GetPersonRequestOptionsValidator>();

// PeopleService registration (inject the validator)
builder.Services.AddScoped(sp =>
{
    var client   = sp.GetRequiredService<FolkRawClient>();
    var cfg      = sp.GetRequiredService<IConfiguration>();
    var xr       = sp.GetRequiredService<XRoadSettings>();
    var logger   = sp.GetRequiredService<ILogger<PeopleService>>();
    var loc      = sp.GetRequiredService<IStringLocalizer<PeopleService>>();
    var validator= sp.GetRequiredService<IValidateOptions<GetPersonRequestOptions>>();
    return new PeopleService(client, cfg, xr, logger, loc, validator);
});

// Response compression
builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.MimeTypes = XRoadFolkWeb.Program.ResponseCompressionMimeTypes;
});

// Add custom SSN model binder provider globally (controllers + pages)
builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new XRoadFolkWeb.Validation.TrimDigitsModelBinderProvider());
});

builder.Services.AddRazorPages()
    .AddMvcOptions(options =>
    {
        options.ModelBinderProviders.Insert(0, new XRoadFolkWeb.Validation.TrimDigitsModelBinderProvider());
    });

// Bind + validate Localization config from appsettings
builder.Services
    .AddOptions<LocalizationConfig>()
    .Bind(builder.Configuration.GetSection("Localization"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultCulture), "Localization: DefaultCulture is required.")
    .Validate(o => o.SupportedCultures is { Count: > 0 }, "Localization: SupportedCultures must have at least one value.")
    .Validate(o =>
    {
        try { _ = CultureInfo.GetCultureInfo(o.DefaultCulture!); }
        catch { return false; }
        foreach (var c in o.SupportedCultures)
        {
            try { _ = CultureInfo.GetCultureInfo(c); }
            catch { return false; }
        }
        return true;
    }, "Localization: One or more culture names are invalid.")
    .Validate(o => o.SupportedCultures.Contains(o.DefaultCulture!, StringComparer.OrdinalIgnoreCase),
        "Localization: DefaultCulture must be included in SupportedCultures.")
    .ValidateOnStart();

// Register realtime broadcaster and in-memory log store + logger provider
builder.Services.AddSingleton<ILogStream, LogStreamBroadcaster>();
builder.Services.AddSingleton<IHttpLogStore>(sp => new InMemoryHttpLogStore(1000, sp.GetRequiredService<ILogStream>()));
builder.Services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));

WebApplication app = builder.Build();
app.UseResponseCompression();

// Redirect to HTTPS in dev so secure cookies work
app.UseHttpsRedirection();

// Localization middleware
RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(locOpts);

// After app.UseRequestLocalization(locOpts);
var locLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Localization");
var locCfg = app.Services.GetRequiredService<IOptions<LocalizationConfig>>().Value;
locLogger.LogInformation("Localization config: Default={Default}, Supported=[{Supported}]",
    locCfg.DefaultCulture, string.Join(", ", locCfg.SupportedCultures));

// Diagnostic endpoint to verify applied culture at runtime
app.MapGet("/__culture", (HttpContext ctx,
                          IOptions<RequestLocalizationOptions> locOpts,
                          IOptions<LocalizationConfig> cfg) =>
{
    var feature = ctx.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>();
    return Results.Json(new
    {
        FromConfig = new
        {
            cfg.Value.DefaultCulture,
            cfg.Value.SupportedCultures
        },
        Applied = new
        {
            Default = locOpts.Value.DefaultRequestCulture.Culture.Name,
            Supported = locOpts.Value.SupportedCultures.Select(c => c.Name).ToArray(),
            Current = feature?.RequestCulture.Culture.Name,
            CurrentUI = feature?.RequestCulture.UICulture.Name
        }
    });
});

// Static files + routing + pages
app.UseStaticFiles();
app.UseRouting();

// Anti-forgery middleware
app.UseAntiforgery();

// Culture switch endpoint with manual antiforgery validation
app.MapPost("/set-culture", async ([FromForm] string culture, [FromForm] string? returnUrl, HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
{
    await af.ValidateRequestAsync(ctx);

    // Ensure the requested culture is one of the supported UI cultures
    bool supported = locOpts.SupportedUICultures != null &&
        locOpts.SupportedUICultures.Any(c => string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase));
    if (!supported)
    {
        return Results.BadRequest();
    }

    string cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture));
    ctx.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        cookieValue,
        new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            Secure = ctx.Request.IsHttps, // allow in dev over HTTP
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

    return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
});

app.MapRazorPages();

// Logs endpoints (generic with kind=http|soap|app)
app.MapGet("/logs", ([FromQuery] string? kind, IHttpLogStore store) =>
{
    var items = store.GetAll();
    if (!string.IsNullOrWhiteSpace(kind))
    {
        items = items.Where(i => string.Equals(i.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    return Results.Json(new { ok = true, items });
});
app.MapPost("/logs/clear", (IHttpLogStore store) => { store.Clear(); return Results.Json(new { ok = true }); });
app.MapPost("/logs/write", ([FromBody] XRoadFolkWeb.LogWriteDto dto, IHttpLogStore store) =>
{
    if (dto is null) return Results.BadRequest();
    if (!Enum.TryParse<LogLevel>(dto.Level ?? "Information", true, out var lvl)) lvl = LogLevel.Information;
    store.Add(new XRoadFolkWeb.Infrastructure.LogEntry
    {
        Timestamp = DateTimeOffset.UtcNow,
        Level = lvl,
        Category = dto.Category ?? "Manual",
        EventId = dto.EventId ?? 0,
        Kind = "app",
        Message = dto.Message ?? string.Empty,
        Exception = null
    });
    return Results.Json(new { ok = true });
});

// Server-Sent Events: real-time log stream (accepts kind filter)
app.MapGet("/logs/stream", async (HttpContext ctx, [FromQuery] string? kind, ILogStream stream, CancellationToken ct) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.Headers.Add("X-Accel-Buffering", "no"); // for proxies like nginx
    ctx.Response.ContentType = "text/event-stream";

    var (reader, id) = stream.Subscribe();
    try
    {
        await foreach (var entry in reader.ReadAllAsync(ct))
        {
            if (!string.IsNullOrWhiteSpace(kind) && !string.Equals(entry.Kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string json = System.Text.Json.JsonSerializer.Serialize(entry);
            await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        stream.Unsubscribe(id);
    }
});

// Culture defaults for threads (optional)
CultureInfo culture = locOpts.DefaultRequestCulture.Culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

app.Run();

namespace XRoadFolkWeb
{
    // Marker class for shared localization resources (layout, nav, etc.)
    public sealed class SharedResource { }

    public record LogWriteDto(string? Message, string? Category, string? Level, int? EventId);
}

// add at the very end of the file (for WebApplicationFactory)
namespace XRoadFolkWeb
{
    public partial class Program
    {
        internal static readonly string[] ResponseCompressionMimeTypes =
        [
            "text/plain",
            "text/xml",
            "application/xml",
            "application/soap+xml"
        ];
    }
}
