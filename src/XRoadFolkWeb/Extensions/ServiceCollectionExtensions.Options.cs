using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Infrastructure;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // XRoad settings with validation (defer to use-time; do not ValidateOnStart to keep tests lightweight)
        services.AddOptions<XRoadSettings>()
            .Bind(configuration.GetSection("XRoad"));
        services.AddSingleton<IValidateOptions<XRoadSettings>, XRoadSettingsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<XRoadSettings>>().Value);

        // Features options (centralized feature toggles)
        services.AddOptions<FeaturesOptions>()
            .Bind(configuration.GetSection("Features"))
            .ValidateDataAnnotations();

        // Http options with environment-aware validation
        services.AddOptions<HttpOptions>()
            .Bind(configuration.GetSection("Http"));
        services.AddSingleton<IValidateOptions<HttpOptions>, XRoadFolkWeb.Validation.HttpOptionsValidator>();

        // HttpLogs options with DataAnnotations validation and fail-fast
        services.AddOptions<HttpLogOptions>()
            .Bind(configuration.GetSection("HttpLogs"))
            .ValidateDataAnnotations()
            .Validate(o => !o.PersistToFile || !string.IsNullOrWhiteSpace(o.FilePath), "HttpLogs:FilePath must be set when PersistToFile is true")
            .ValidateOnStart();

        // Retry.Http strongly-typed options with DataAnnotations and fail-fast
        services.AddOptions<HttpRetryOptions>()
            .Bind(configuration.GetSection("Retry:Http"))
            .ValidateDataAnnotations()
            .Validate(o => o.TimeoutMs >= 1000, "Retry:Http: TimeoutMs must be >= 1000")
            .Validate(o => o.Attempts >= 0 && o.Attempts <= 10, "Retry:Http: Attempts must be between 0 and 10")
            .ValidateOnStart();

        // Safe SOAP sanitization hook
        bool maskTokens = configuration.GetValue<bool>(key: "Logging:MaskTokens", defaultValue: true);
        SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

        // GetPerson options & validator (runtime validation for identifiers)
        services.AddOptions<GetPersonRequestOptions>()
            .Bind(configuration.GetSection("Operations:GetPerson:Request"));
        services.AddSingleton<IValidateOptions<GetPersonRequestOptions>, GetPersonRequestOptionsValidator>();

        // Bind and validate the Include block separately at startup
        services.AddOptions<GetPersonIncludeOptions>()
            .Bind(configuration.GetSection("Operations:GetPerson:Request:Include"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Token cache options with validation (defer validation until accessed)
        services.AddOptions<TokenCacheOptions>()
            .Bind(configuration.GetSection("TokenCache"))
            .Validate(o => o.RefreshSkewSeconds >= 0, "TokenCache: RefreshSkewSeconds must be >= 0.")
            .Validate(o => o.DefaultTtlSeconds >= 1, "TokenCache: DefaultTtlSeconds must be >= 1.");

        // Response viewer options: bind + validate + fail fast; then apply Production overrides
        AddResponseViewerOptions(services, configuration);

        return services;
    }

    private static void AddResponseViewerOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ResponseViewerOptions>()
            .Bind(configuration.GetSection("Features:ResponseViewer"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ResponseViewerOptions>, ResponseViewerOptionsValidator>();
        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var opts = sp.GetRequiredService<IOptions<ResponseViewerOptions>>().Value;
            if (env.IsProduction())
            {
                // Force off in production regardless of config
                opts.ShowRawXml = false;
                opts.ShowPrettyXml = false;
            }
            return opts;
        });
    }
}
