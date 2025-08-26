using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkRaw;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options; // added for GetPersonRequestOptions & validator

// Use the Generic Host only (remove mixed top-level + class blocks)

// Host builder
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Load embedded X-Road defaults from the library
Assembly libAsm = typeof(XRoadSettings).Assembly;
string? resName = libAsm
    .GetManifestResourceNames()
    .FirstOrDefault(n => n.EndsWith(".Resources.appsettings.xroad.json", StringComparison.OrdinalIgnoreCase));
if (resName is not null)
{
    using Stream? s = libAsm.GetManifestResourceStream(resName);
    if (s is not null)
    {
        builder.Configuration.AddJsonStream(s);
    }
}

// Keep appsettings.json (added by default) and environment variables as overrides
builder.Configuration.AddEnvironmentVariables();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

// Localization
builder.Services.AddLocalization();

// Bind and expose XRoad settings via DI
builder.Services.Configure<XRoadSettings>(builder.Configuration.GetSection("XRoad"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<XRoadSettings>>().Value);

// HttpClient with certificate (warn if missing)
builder.Services.AddHttpClient("XRoadFolk", (sp, c) =>
{
    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
    c.BaseAddress = new Uri(xr.BaseUrl, UriKind.Absolute);
    c.Timeout = TimeSpan.FromSeconds(xr.Http.TimeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    SocketsHttpHandler handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 20,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
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

    IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();
    bool bypass = sp.GetRequiredService<IConfiguration>().GetValue("Http:BypassServerCertificateValidation", true);
    if (env.IsDevelopment() && bypass)
    {
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
    }

    return handler;
});

// FolkRawClient + PeopleService + ConsoleUi
builder.Services.AddScoped(sp =>
{
    IHttpClientFactory httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    HttpClient http = httpFactory.CreateClient("XRoadFolk");
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

// Register validator for GetPerson requests
builder.Services.AddSingleton<IValidateOptions<GetPersonRequestOptions>, GetPersonRequestOptionsValidator>();

builder.Services.AddScoped(sp =>
{
    FolkRawClient client = sp.GetRequiredService<FolkRawClient>();
    IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
    XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
    ILogger<PeopleService> log = sp.GetRequiredService<ILogger<PeopleService>>();
    IStringLocalizer<PeopleService> loc = sp.GetRequiredService<IStringLocalizer<PeopleService>>();
    IValidateOptions<GetPersonRequestOptions> validator = sp.GetRequiredService<IValidateOptions<GetPersonRequestOptions>>();
    return new PeopleService(client, cfg, xr, log, loc, validator);
});

builder.Services.AddScoped<ConsoleUi>();

// Build and run
IHost app = builder.Build();
await app.Services.GetRequiredService<ConsoleUi>().RunAsync();

// LoggerMessage helpers (types must come after top-level statements)
internal static partial class Program
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "[culture] Resource 'BannerSeparator' not found for {Culture}")]
    public static partial void LogMissingBannerSeparator(ILogger logger, string culture);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "[cert] Using {Subject} thumbprint {Thumbprint}")]
    public static partial void LogCertificateDetails(ILogger logger, string subject, string thumbprint);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "[culture] Using {Culture}")]
    public static partial void LogCultureSelection(ILogger logger, string culture);
}
