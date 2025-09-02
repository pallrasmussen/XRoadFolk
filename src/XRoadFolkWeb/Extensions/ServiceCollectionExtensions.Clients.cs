using XRoadFolkRaw.Lib;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers FolkRawClient factory using the named HttpClient "XRoadFolk"
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddFolkRawClientFactory(this IServiceCollection services)
    {
        _ = services.AddSingleton(sp =>
        {
            HttpClient http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("XRoadFolk");
            ILogger<FolkRawClient> logger = sp.GetRequiredService<ILogger<FolkRawClient>>();
            IConfiguration cfg = sp.GetRequiredService<IConfiguration>();

            int attempts = cfg.GetValue<int>("Retry:Http:Attempts", 3);
            int baseDelayMs = cfg.GetValue<int>("Retry:Http:BaseDelayMs", 200);
            int jitterMs = cfg.GetValue<int>("Retry:Http:JitterMs", 250);

            const int MaxAttempts = 10; // avoid overflow in backoff calculation
            int origAttempts = attempts, origBase = baseDelayMs, origJitter = jitterMs;
            attempts = Math.Clamp(attempts, 0, MaxAttempts);
            baseDelayMs = Math.Max(0, baseDelayMs);
            jitterMs = Math.Max(0, jitterMs);

            if (attempts != origAttempts || baseDelayMs != origBase || jitterMs != origJitter)
            {
                logger.LogWarning("Invalid retry settings clamped: Attempts {OrigAttempts}-> {Attempts}, BaseDelayMs {OrigBase}-> {Base}, JitterMs {OrigJitter}-> {Jitter}",
                    origAttempts, attempts, origBase, baseDelayMs, origJitter, jitterMs);
            }

            return new FolkRawClient(
                httpClient: http,
                logger: logger,
                verbose: cfg.GetValue<bool>("Logging:Verbose", defaultValue: false),
                retryAttempts: attempts,
                retryBaseDelayMs: baseDelayMs,
                retryJitterMs: jitterMs);
        });
        return services;
    }
}
