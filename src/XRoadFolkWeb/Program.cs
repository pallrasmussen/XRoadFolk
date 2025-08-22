using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc; // added
using Microsoft.AspNetCore.Antiforgery; // NEW: for RequireAntiforgery()
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

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

// Razor Pages + view localization (needed for IViewLocalizer)
builder.Services
    .AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

// Build a tiny pre-provider to load X-Road settings using existing loader
using var pre = new ServiceCollection()
    .AddLogging(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Information).AddFilter("Microsoft", LogLevel.Warning))
    .AddLocalization(opts => opts.ResourcesPath = "Resources")
    .BuildServiceProvider();

var preLogger = pre.GetRequiredService<ILoggerFactory>().CreateLogger("XRoadFolkWeb");
var cfgLoc = pre.GetRequiredService<IStringLocalizer<ConfigurationLoader>>();
var loader = new ConfigurationLoader();
var (configRoot, xr) = loader.Load(preLogger, cfgLoc);

// Make the loaded configuration the app configuration for consistency
builder.Configuration.AddConfiguration(configRoot);

// Safe SOAP sanitization hook, same behavior as console app
bool maskTokens = builder.Configuration.GetValue("Logging:MaskTokens", true);
SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

// Localization options (force fo-FO as default and ensure it's supported)
builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    string[] supportedFromConfig = builder.Configuration.GetSection("Localization:SupportedCultures").Get<string[]>() ?? ["en-US"];
    var supported = supportedFromConfig.ToList();
    if (!supported.Contains("fo-FO", StringComparer.OrdinalIgnoreCase)) supported.Add("fo-FO");

    var cultures = supported.Select(CultureInfo.GetCultureInfo).ToList();
    opts.SupportedCultures = cultures;
    opts.SupportedUICultures = cultures;
    opts.DefaultRequestCulture = new RequestCulture("fo-FO");

    // Explicit provider order: cookie -> query -> Accept-Language
    opts.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new CookieRequestCultureProvider(),
        new QueryStringRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});

// Register IHttpClientFactory + handler with client certificate
builder.Services.AddHttpClient("XRoadFolk", c =>
{
    c.BaseAddress = new Uri(xr.BaseUrl, UriKind.Absolute);
    c.Timeout = TimeSpan.FromSeconds(xr.Http.TimeoutSeconds);
    c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 20,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    var cert = CertLoader.LoadFromConfig(xr.Certificate);
    if (cert is not null)
    {
        handler.SslOptions.ClientCertificates ??= new System.Security.Cryptography.X509Certificates.X509CertificateCollection();
        handler.SslOptions.ClientCertificates.Add(cert);
    }

    bool bypass = builder.Configuration.GetValue("Http:BypassServerCertificateValidation", true);
    if (bypass)
    {
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    }
    return handler;
});

// FolkRawClient via factory (reuses your HttpClient-aware constructor)
builder.Services.AddScoped<FolkRawClient>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("XRoadFolk");
    var logger = sp.GetRequiredService<ILogger<FolkRawClient>>();
    var cfg = sp.GetRequiredService<IConfiguration>();
    return new FolkRawClient(
        httpClient: http,
        logger: logger,
        verbose: cfg.GetValue("Logging:Verbose", false),
        maskTokens: cfg.GetValue("Logging:MaskTokens", true),
        retryAttempts: cfg.GetValue("Retry:Http:Attempts", 3),
        retryBaseDelayMs: cfg.GetValue("Retry:Http:BaseDelayMs", 200),
        retryJitterMs: cfg.GetValue("Retry:Http:JitterMs", 250));
});

// PeopleService (reuses existing implementation)
builder.Services.AddScoped(sp =>
{
    var client = sp.GetRequiredService<FolkRawClient>();
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<PeopleService>>();
    var loc = sp.GetRequiredService<IStringLocalizer<PeopleService>>();
    return new PeopleService(client, cfg, xr, logger, loc);
});

var app = builder.Build();

// Redirect to HTTPS in dev so secure cookies work
app.UseHttpsRedirection();

// Localization middleware
var locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
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

    var cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture));
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
var culture = locOpts.DefaultRequestCulture.Culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

app.Run();

namespace XRoadFolkWeb
{
    // Marker class for shared localization resources (layout, nav, etc.)
    public sealed class SharedResource { }
}
