using System.Diagnostics;
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
using XRoadFolkWeb.Infrastructure;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Infrastructure.Logging;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// In-memory log store + provider registration (bounded)
int logCapacity = builder.Configuration.GetValue("Logging:View:Capacity", 500);
var logStore = new InMemoryLogStore(logCapacity);
builder.Services.AddSingleton(logStore);

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
    string? libDir = Path.GetDirectoryName(libAsm.Location);
    string? jsonPath = libDir is null ? null : Path.Combine(libDir, "Resources", "appsettings.xroad.json");
    if (jsonPath is not null && File.Exists(jsonPath))
    {
        _ = builder.Configuration.AddJsonFile(jsonPath, optional: true, reloadOnChange: false);
    }
}

// Allow overrides from env
builder.Configuration.AddEnvironmentVariables();

// Logging: console + in-memory provider; filter Microsoft noise
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddProvider(new InMemoryLoggerProvider(logStore));

// MVC + localization
builder.Services
    .AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(opts =>
    {
        opts.DataAnnotationLocalizerProvider = (type, factory) => factory.Create(typeof(XRoadFolkWeb.SharedResource));
    });

builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");

// Anti-forgery
builder.Services.AddAntiforgery(opts =>
{
    opts.Cookie.Name = "__Host.AntiForgery";
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.Cookie.SameSite = SameSiteMode.Lax;
    opts.HeaderName = "RequestVerificationToken";
});

// X-Road settings + sanitizer
builder.Services.Configure<XRoadSettings>(builder.Configuration.GetSection("XRoad"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<XRoadSettings>>().Value);
bool maskTokens = builder.Configuration.GetValue("Logging:MaskTokens", true);
SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

// Supported cultures
builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    (string defaultCulture, IReadOnlyList<CultureInfo> cultures) = AppLocalization.FromConfiguration(builder.Configuration);
    opts.DefaultRequestCulture = new RequestCulture(defaultCulture);
    opts.SupportedCultures = [.. cultures];
    opts.SupportedUICultures = [.. cultures];
});

// Register outgoing HTTP logging handler (verbose controlled by Logging:Verbose)
builder.Services.AddTransient(sp => new OutgoingHttpLoggingHandler(
    sp.GetRequiredService<ILogger<OutgoingHttpLoggingHandler>>(),
    sp.GetRequiredService<IConfiguration>().GetValue("Logging:Verbose", false)));

// HTTP client + certificate + logging handler
builder.Services.AddHttpClient("XRoadFolk", (sp, c) =>
{
    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
    c.BaseAddress = new Uri(xr.BaseUrl, UriKind.Absolute);
    c.Timeout = TimeSpan.FromSeconds(xr.Http.TimeoutSeconds);
})
.AddHttpMessageHandler<OutgoingHttpLoggingHandler>()
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
    try
    {
        X509Certificate2 cert = CertLoader.LoadFromConfig(xr.Certificate);
        handler.SslOptions.ClientCertificates ??= new X509CertificateCollection();
        handler.SslOptions.ClientCertificates.Add(cert);
    }
    catch (Exception ex)
    {
        ILogger log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("XRoadCert");
        log.LogWarning(ex, "Client certificate not configured. Proceeding without certificate.");
    }

    IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
    IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();
    bool bypass = cfg.GetValue("Http:BypassServerCertificateValidation", true);
    if (env.IsDevelopment() && bypass)
    {
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
    }

    return handler;
});

// Options validator for PeopleService
builder.Services.AddSingleton<IValidateOptions<GetPersonRequestOptions>, GetPersonRequestOptionsValidator>();

// FolkRawClient via factory (register in DI)
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

// PeopleService
builder.Services.AddScoped(sp =>
{
    FolkRawClient client = sp.GetRequiredService<FolkRawClient>();
    IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
    ILogger<PeopleService> logger = sp.GetRequiredService<ILogger<PeopleService>>();
    IStringLocalizer<PeopleService> loc = sp.GetRequiredService<IStringLocalizer<PeopleService>>();
    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
    IValidateOptions<GetPersonRequestOptions> validator = sp.GetRequiredService<IValidateOptions<GetPersonRequestOptions>>();
    return new PeopleService(client, cfg, xr, logger, loc, validator);
});

// Response compression
builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.MimeTypes = XRoadFolkWeb.Program.ResponseCompressionMimeTypes;
});

// Model binder for SSN
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

// Exceptions + HSTS
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// HTTPS
app.UseHttpsRedirection();

// Log Content/Web root
app.Logger.LogInformation("ContentRoot: {ContentRoot}", app.Environment.ContentRootPath);
app.Logger.LogInformation("WebRoot: {WebRoot}", app.Environment.WebRootPath);

// Security headers + CSP
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    h["X-XSS-Protection"] = "0";
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; font-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; upgrade-insecure-requests";
    await next();
});

// Localization
RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(locOpts);

// Static files
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=604800,immutable");
    }
});

bool verbose = builder.Configuration.GetValue("Logging:Verbose", false);

// Request logging (correlation id + timings + sizes/headers when verbose)
app.Use(async (ctx, next) =>
{
    string cid = ctx.TraceIdentifier;
    if (!ctx.Request.Headers.ContainsKey("X-Correlation-Id"))
        ctx.Request.Headers["X-Correlation-Id"] = cid;
    ctx.Response.Headers["X-Correlation-Id"] = ctx.Request.Headers["X-Correlation-Id"];

    long reqBytes = 0;
    if (verbose)
    {
        if (ctx.Request.ContentLength.HasValue)
        {
            reqBytes = ctx.Request.ContentLength.Value;
        }
        else if (string.Equals(ctx.Request.Method, "POST", StringComparison.OrdinalIgnoreCase)
              || string.Equals(ctx.Request.Method, "PUT", StringComparison.OrdinalIgnoreCase)
              || string.Equals(ctx.Request.Method, "PATCH", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Request.EnableBuffering();
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            reqBytes = ms.Length;
            ctx.Request.Body.Position = 0;
        }
    }

    var originalBody = ctx.Response.Body;
    CountingStream? counter = null;
    if (verbose)
    {
        counter = new CountingStream(originalBody);
        ctx.Response.Body = counter;
    }

    var ua = ctx.Request.Headers.UserAgent.ToString();
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
    var method = ctx.Request.Method;
    var path = ctx.Request.Path + ctx.Request.QueryString;

    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();

    long resBytes = 0;
    if (verbose && counter is not null)
    {
        await ctx.Response.Body.FlushAsync();
        ctx.Response.Body = originalBody; // restore
        resBytes = counter.BytesWritten;
    }

    var status = ctx.Response.StatusCode;

    if (!verbose)
    {
        app.Logger.LogInformation("HTTP {Method} {Path} => {Status} in {Elapsed} ms (ip {IP}) UA='{UA}'", method, path, status, sw.ElapsedMilliseconds, ip, ua);
    }
    else
    {
        static string Sanitize(string key, string value)
            => (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
             || key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) ? "***" : value;

        string Trunc(string s) => s.Length > 200 ? s[..200] + "…" : s;
        string reqHeaders = string.Join("; ", ctx.Request.Headers.Select(h => $"{h.Key}={Trunc(Sanitize(h.Key, h.Value.ToString()))}"));
        string resHeaders = string.Join("; ", ctx.Response.Headers.Select(h => $"{h.Key}={Trunc(Sanitize(h.Key, h.Value.ToString()))}"));

        app.Logger.LogInformation("HTTP {Method} {Path} => {Status} in {Elapsed} ms | req={ReqBytes}B res={ResBytes}B | reqHdrs: {ReqHeaders} | resHdrs: {ResHeaders}",
            method, path, status, sw.ElapsedMilliseconds, reqBytes, resBytes, reqHeaders, resHeaders);
    }
});

// Routing
app.UseRouting();

// Anti-forgery
app.UseAntiforgery();

// Culture switch
app.MapPost("/set-culture", async ([FromForm] string culture, [FromForm] string? returnUrl, HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
{
    await af.ValidateRequestAsync(ctx);

    bool supported = locOpts.SupportedUICultures != null &&
        locOpts.SupportedUICultures.Any(c => string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase));
    if (!supported)
    {
        app.Logger.LogWarning("[Culture] Attempt to set unsupported culture '{Culture}'", culture);
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
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

    app.Logger.LogInformation("[Culture] UI culture set to '{Culture}'", culture);
    return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
});

app.MapRazorPages();

// Default culture for threads
CultureInfo culture = locOpts.DefaultRequestCulture.Culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

app.Run();

namespace XRoadFolkWeb
{
    public sealed class SharedResource { }
}

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

// small counting stream used for response size measurement when verbose
file sealed class CountingStream(Stream inner) : Stream
{
    public long BytesWritten { get; private set; }
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush()
    {
        // route sync flush to async to avoid Kestrel's disallow-sync-IO exception
        var t = inner.FlushAsync();
        if (!t.IsCompletedSuccessfully)
        {
            t.GetAwaiter().GetResult();
        }
    }
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); BytesWritten += count; }
#if NET8_0_OR_GREATER
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    { await inner.WriteAsync(buffer, cancellationToken); BytesWritten += buffer.Length; }
#endif
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    { await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken); BytesWritten += count; }
}
