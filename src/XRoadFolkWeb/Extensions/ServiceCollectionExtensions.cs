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
                .AddSessionServices();
        }

        private static IServiceCollection AddSessionServices(this IServiceCollection services)
        {
            _ = services.AddDistributedMemoryCache();
            _ = services.AddSession(options =>
            {
                options.Cookie.Name = ".XRoadFolk.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict; // tighten CSRF surface
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // HTTPS-only
                options.Cookie.IsEssential = true; // required for auth/csrf-related flows
                options.IdleTimeout = TimeSpan.FromMinutes(30);
            });
            return services;
        }
    }
}
