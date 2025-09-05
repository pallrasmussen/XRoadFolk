using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Infrastructure;
using System.ComponentModel.DataAnnotations;

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

        // Http options with environment-aware validation
        services.AddOptions<HttpOptions>()
            .Bind(configuration.GetSection("Http"));
        services.AddSingleton<IValidateOptions<HttpOptions>, XRoadFolkWeb.Validation.HttpOptionsValidator>();

        // Retry.Http strongly-typed options with validation
        services.AddOptions<HttpRetryOptions>()
            .Bind(configuration.GetSection("Retry:Http"))
            .ValidateDataAnnotations()
            .Validate(o => o.TimeoutMs >= 1000, "Retry:Http: TimeoutMs must be >= 1000")
            .Validate(o => o.Attempts >= 0 && o.Attempts <= 10, "Retry:Http: Attempts must be between 0 and 10");

        // Safe SOAP sanitization hook
        bool maskTokens = configuration.GetValue<bool>("Logging:MaskTokens", true);
        SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

        // GetPerson options & validator
        services.AddOptions<GetPersonRequestOptions>()
            .Bind(configuration.GetSection("Operations:GetPerson:Request"));

        services.AddSingleton<IValidateOptions<GetPersonRequestOptions>, GetPersonRequestOptionsValidator>();

        // Token cache options with validation (defer validation until accessed)
        services.AddOptions<TokenCacheOptions>()
            .Bind(configuration.GetSection("TokenCache"))
            .Validate(o => o.RefreshSkewSeconds >= 0, "TokenCache: RefreshSkewSeconds must be >= 0.")
            .Validate(o => o.DefaultTtlSeconds >= 1, "TokenCache: DefaultTtlSeconds must be >= 1.");

        return services;
    }
}
