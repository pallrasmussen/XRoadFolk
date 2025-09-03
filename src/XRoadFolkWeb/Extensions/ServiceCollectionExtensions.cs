using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddAppLogging(configuration)
                .AddAppLocalization(configuration)
                .AddAppAntiforgery()
                .AddAppOptions(configuration)
                .AddXRoadHttpClient()
                .AddFolkRawClientFactory()
                .AddPeopleServices()
                .AddMvcCustomizations()
                .AddResponseCompressionDefaults()
                .AddSessionServices(configuration);
        }

        private static IServiceCollection AddSessionServices(this IServiceCollection services, IConfiguration configuration)
        {
            _ = services.AddDistributedMemoryCache();

            _ = services.AddOptions<SessionOptions>()
                .Configure<IConfiguration, ILoggerFactory>((options, cfg, lf) =>
                {
                    ILogger log = lf.CreateLogger("SessionConfig");

                    IConfiguration section = cfg.GetSection("Session");

                    string cookieName = section.GetValue<string>("Cookie:Name") ?? ".XRoadFolk.Session";

                    // Use shared configuration helpers for booleans
                    bool cookieHttpOnly = section.GetBoolOrDefault("Cookie:HttpOnly", true, log);
                    bool cookieIsEssential = section.GetBoolOrDefault("Cookie:IsEssential", true, log);

                    // Enums (with validation)
                    string? sameSiteStr = section.GetValue<string>("Cookie:SameSite");
                    string? securePolicyStr = section.GetValue<string>("Cookie:SecurePolicy");

                    // Idle timeout minutes (validate integer)
                    int idleMinutes = 30;
                    string? idleStr = section["IdleTimeoutMinutes"];
                    if (!string.IsNullOrWhiteSpace(idleStr))
                    {
                        if (!int.TryParse(idleStr, out idleMinutes))
                        {
                            log.LogWarning("Session: Invalid IdleTimeoutMinutes='{Value}'. Falling back to {Fallback} minutes.", idleStr, 30);
                            idleMinutes = 30;
                        }
                    }

                    SameSiteMode sameSite = SameSiteMode.Strict;
                    if (!string.IsNullOrWhiteSpace(sameSiteStr))
                    {
                        if (!Enum.TryParse(sameSiteStr, ignoreCase: true, out SameSiteMode parsedSameSite))
                        {
                            log.LogWarning("Session: Invalid Cookie:SameSite='{Value}'. Falling back to {Fallback}.", sameSiteStr, sameSite);
                        }
                        else
                        {
                            sameSite = parsedSameSite;
                        }
                    }

                    CookieSecurePolicy securePolicy = CookieSecurePolicy.Always;
                    if (!string.IsNullOrWhiteSpace(securePolicyStr))
                    {
                        if (!Enum.TryParse(securePolicyStr, ignoreCase: true, out CookieSecurePolicy parsedSecure))
                        {
                            log.LogWarning("Session: Invalid Cookie:SecurePolicy='{Value}'. Falling back to {Fallback}.", securePolicyStr, securePolicy);
                        }
                        else
                        {
                            securePolicy = parsedSecure;
                        }
                    }

                    int origIdle = idleMinutes;
                    if (idleMinutes < 1)
                    {
                        idleMinutes = 1;
                        log.LogWarning("Session: IdleTimeoutMinutes {Original} is too small. Clamped to {Clamped} minute.", origIdle, idleMinutes);
                    }

                    options.Cookie.Name = cookieName;
                    options.Cookie.HttpOnly = cookieHttpOnly;
                    options.Cookie.IsEssential = cookieIsEssential;
                    options.Cookie.SecurePolicy = securePolicy;
                    options.Cookie.SameSite = sameSite;
                    options.IdleTimeout = TimeSpan.FromMinutes(idleMinutes);
                });

            _ = services.AddSession();
            return services;
        }
    }
}
