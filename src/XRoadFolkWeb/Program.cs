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
