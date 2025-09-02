namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddAppLogging()
                .AddAppLocalization(configuration)
                .AddAppAntiforgery()
                .AddAppOptions(configuration)
                .AddXRoadHttpClient()
                .AddFolkRawClientFactory()
                .AddPeopleServices()
                .AddMvcCustomizations()
                .AddHttpLogging(configuration)
                .AddResponseCompressionDefaults()
                .AddSessionServices(configuration);
        }

        private static IServiceCollection AddSessionServices(this IServiceCollection services, IConfiguration configuration)
        {
            _ = services.AddDistributedMemoryCache();

            IConfiguration section = configuration.GetSection("Session");

            string cookieName = section.GetValue<string>("Cookie:Name") ?? ".XRoadFolk.Session";
            bool cookieHttpOnly = section.GetValue<bool?>("Cookie:HttpOnly") ?? true;
            string? sameSiteStr = section.GetValue<string>("Cookie:SameSite");
            string? securePolicyStr = section.GetValue<string>("Cookie:SecurePolicy");
            bool cookieIsEssential = section.GetValue<bool?>("Cookie:IsEssential") ?? true;
            int idleMinutes = section.GetValue<int?>("IdleTimeoutMinutes") ?? 30;

            SameSiteMode sameSite = SameSiteMode.Strict;
            if (!string.IsNullOrWhiteSpace(sameSiteStr) && Enum.TryParse(sameSiteStr, ignoreCase: true, out SameSiteMode parsedSameSite))
            {
                sameSite = parsedSameSite;
            }

            CookieSecurePolicy securePolicy = CookieSecurePolicy.Always;
            if (!string.IsNullOrWhiteSpace(securePolicyStr) && Enum.TryParse(securePolicyStr, ignoreCase: true, out CookieSecurePolicy parsedSecure))
            {
                securePolicy = parsedSecure;
            }

            _ = services.AddSession(options =>
            {
                options.Cookie.Name = cookieName;
                options.Cookie.HttpOnly = cookieHttpOnly;
                options.Cookie.SameSite = sameSite;
                options.Cookie.SecurePolicy = securePolicy;
                options.Cookie.IsEssential = cookieIsEssential;
                options.IdleTimeout = TimeSpan.FromMinutes(Math.Max(1, idleMinutes));
            });
            return services;
        }
    }
}
