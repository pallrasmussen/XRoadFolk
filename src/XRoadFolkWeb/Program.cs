using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc; // added
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkWeb.Infrastructure; // added

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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

// Register ConfigurationLoader
builder.Services.AddSingleton<ConfigurationLoader>();

// Register a factory for XRoadSettings that resolves dependencies from DI at runtime:
IServiceCollection serviceCollection = builder.Services.AddSingleton(sp =>
{
    ConfigurationLoader loader = sp.GetRequiredService<ConfigurationLoader>();
    ILogger preLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("XRoadFolkWeb");
    IStringLocalizer<ConfigurationLoader> cfgLoc = sp.GetRequiredService<IStringLocalizer<ConfigurationLoader>>();
    (IConfigurationRoot configRoot, XRoadSettings xr) = loader.Load(preLogger, cfgLoc);

    // Make the loaded configuration available to the app
    _ = builder.Configuration.AddConfiguration(configRoot);

    return xr;
});

// Safe SOAP sanitization hook, same behavior as console app
bool maskTokens = builder.Configuration.GetValue("Logging:MaskTokens", true);
SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

// Centralized supported cultures from configuration (with safe fallbacks)
builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    var (defaultCulture, cultures) = AppLocalization.FromConfiguration(builder.Configuration);
    opts.DefaultRequestCulture = new RequestCulture(defaultCulture);
    opts.SupportedCultures = [.. cultures];
    opts.SupportedUICultures = [.. cultures];
});

// Register IHttpClientFactory + handler with client certificate (resolve settings at runtime from DI)
builder.Services.AddHttpClient("XRoadFolk", (sp, c) =>
{
    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
    c.BaseAddress = new Uri(xr.BaseUrl, UriKind.Absolute);
    c.Timeout = TimeSpan.FromSeconds(xr.Http.TimeoutSeconds);
    c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    SocketsHttpHandler handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 20,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
    System.Security.Cryptography.X509Certificates.X509Certificate2? cert = CertLoader.LoadFromConfig(xr.Certificate);
    if (cert is not null)
    {
        handler.SslOptions.ClientCertificates ??= [];
        _ = handler.SslOptions.ClientCertificates.Add(cert);
    }

    IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
    bool bypass = cfg.GetValue("Http:BypassServerCertificateValidation", true);
    if (bypass)
    {
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
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

// PeopleService (resolve XRoadSettings from DI, not a captured local)
builder.Services.AddScoped(sp =>
{
    FolkRawClient client = sp.GetRequiredService<FolkRawClient>();
    IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
    ILogger<PeopleService> logger = sp.GetRequiredService<ILogger<PeopleService>>();
    IStringLocalizer<PeopleService> loc = sp.GetRequiredService<IStringLocalizer<PeopleService>>();
    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
    return new PeopleService(client, cfg, xr, logger, loc);
});

// Response compression
builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.MimeTypes = new[] { "text/plain", "text/xml", "application/xml", "application/soap+xml" };
});

builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new XRoadFolkWeb.Validation.TrimDigitsModelBinderProvider());
});

WebApplication app = builder.Build();
app.UseResponseCompression();

// Redirect to HTTPS in dev so secure cookies work
app.UseHttpsRedirection();

// Localization middleware
RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(locOpts);

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

// Culture defaults for threads (optional)
CultureInfo culture = locOpts.DefaultRequestCulture.Culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

app.Run();

namespace XRoadFolkWeb
{
    // Marker class for shared localization resources (layout, nav, etc.)
    public sealed class SharedResource { }
}

// add at the very end of the file (for WebApplicationFactory)
namespace XRoadFolkWeb
{
    public partial class Program { }
}
