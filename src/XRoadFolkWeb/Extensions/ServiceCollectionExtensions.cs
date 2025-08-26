using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });

        // Localization and Razor Pages
        services
            .AddRazorPages()
            .AddViewLocalization()
            .AddDataAnnotationsLocalization(opts =>
            {
                opts.DataAnnotationLocalizerProvider = (type, factory) =>
                    factory.Create(typeof(XRoadFolkWeb.SharedResource));
            });

        services.AddLocalization(opts => opts.ResourcesPath = "Resources");

        // Anti-forgery
        services.AddAntiforgery(opts =>
        {
            opts.Cookie.Name = "__Host.AntiForgery";
            opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            opts.Cookie.SameSite = SameSiteMode.Lax;
            opts.HeaderName = "RequestVerificationToken";
        });

        // Bind XRoad settings
        services.Configure<XRoadSettings>(configuration.GetSection("XRoad"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<XRoadSettings>>().Value);

        // Safe SOAP sanitization hook
        bool maskTokens = configuration.GetValue("Logging:MaskTokens", true);
        SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

        // Localization options with best-match provider
        services.Configure<RequestLocalizationOptions>(opts =>
        {
            (string defaultCulture, IReadOnlyList<CultureInfo> cultures) = AppLocalization.FromConfiguration(configuration);
            opts.DefaultRequestCulture = new RequestCulture(defaultCulture);
            opts.SupportedCultures = [.. cultures];
            opts.SupportedUICultures = [.. cultures];
            opts.FallBackToParentCultures = true;
            opts.FallBackToParentUICultures = true;

            var locCfg = configuration.GetSection("Localization").Get<LocalizationConfig>() ?? new LocalizationConfig();
            opts.RequestCultureProviders.Insert(0, new XRoadFolkWeb.Infrastructure.BestMatchRequestCultureProvider(
                opts.SupportedUICultures, locCfg.FallbackMap));
        });

        // HttpClient with client certificate
        services.AddHttpClient("XRoadFolk", (sp, c) =>
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

        // FolkRawClient factory
        services.AddScoped(sp =>
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

        // GetPerson options & validator
        services.AddOptions<GetPersonRequestOptions>()
            .Bind(configuration.GetSection("Operations:GetPerson:Request"));

        services.AddSingleton<IValidateOptions<GetPersonRequestOptions>, GetPersonRequestOptionsValidator>();

        // PeopleService
        services.AddScoped(sp =>
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
        services.AddResponseCompression(opts =>
        {
            opts.EnableForHttps = true;
            opts.MimeTypes = XRoadFolkWeb.Program.ResponseCompressionMimeTypes;
        });

        // Custom model binder
        services.AddControllers(options =>
        {
            options.ModelBinderProviders.Insert(0, new XRoadFolkWeb.Validation.TrimDigitsModelBinderProvider());
        });

        services.AddRazorPages()
            .AddMvcOptions(options =>
            {
                options.ModelBinderProviders.Insert(0, new XRoadFolkWeb.Validation.TrimDigitsModelBinderProvider());
            });

        // Localization config binding + validation
        services.AddOptions<LocalizationConfig>()
            .Bind(configuration.GetSection("Localization"))
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

        // Realtime log broadcaster and store
        services.AddSingleton<ILogStream, LogStreamBroadcaster>();
        services.AddSingleton<IHttpLogStore>(sp => new InMemoryHttpLogStore(1000, sp.GetRequiredService<ILogStream>()));
        services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));

        return services;
    }
}
