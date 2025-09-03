using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // XRoad settings
        services.Configure<XRoadSettings>(configuration.GetSection("XRoad"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<XRoadSettings>>().Value);

        // Safe SOAP sanitization hook
        bool maskTokens = configuration.GetValue<bool>("Logging:MaskTokens", true);
        SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

        // GetPerson options & validator
        services.AddOptions<GetPersonRequestOptions>()
            .Bind(configuration.GetSection("Operations:GetPerson:Request"));

        services.AddSingleton<IValidateOptions<GetPersonRequestOptions>, GetPersonRequestOptionsValidator>();

        // Token cache options with validation
        services.AddOptions<TokenCacheOptions>()
            .Bind(configuration.GetSection("TokenCache"))
            .Validate(o => o.RefreshSkewSeconds >= 0, "TokenCache: RefreshSkewSeconds must be >= 0.")
            .Validate(o => o.DefaultTtlSeconds >= 1, "TokenCache: DefaultTtlSeconds must be >= 1.")
            .ValidateOnStart();

        return services;
    }
}
