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

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Decompose into focused registrations
        services
            .AddAppLogging()
            .AddAppLocalization(configuration)
            .AddAppAntiforgery()
            .AddAppOptions(configuration)
            .AddXRoadHttpClient()
            .AddInMemoryHttpLogging();

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

        // Custom model binder for Controllers
        services.AddControllers(options =>
        {
            options.ModelBinderProviders.Insert(0, new XRoadFolkWeb.Validation.TrimDigitsModelBinderProvider());
        });

        return services;
    }
}
