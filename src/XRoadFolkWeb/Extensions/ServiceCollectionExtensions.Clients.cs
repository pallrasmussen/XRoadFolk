using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    // Registers FolkRawClient factory using the named HttpClient "XRoadFolk"
    public static IServiceCollection AddFolkRawClientFactory(this IServiceCollection services)
    {
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
        return services;
    }
}
