using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkRaw.Lib.Logging;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // XRoad settings
        services.Configure<XRoadSettings>(configuration.GetSection("XRoad"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<XRoadSettings>>().Value);

        // Safe SOAP sanitization hook
        bool maskTokens = configuration.GetValue("Logging:MaskTokens", true);
        SafeSoapLogger.GlobalSanitizer = s => SoapSanitizer.Scrub(s, maskTokens);

        // GetPerson options & validator
        services.AddOptions<GetPersonRequestOptions>()
            .Bind(configuration.GetSection("Operations:GetPerson:Request"));

        services.AddSingleton<IValidateOptions<GetPersonRequestOptions>, GetPersonRequestOptionsValidator>();

        // Token cache options
        services.Configure<TokenCacheOptions>(configuration.GetSection("TokenCache"));

        return services;
    }
}
