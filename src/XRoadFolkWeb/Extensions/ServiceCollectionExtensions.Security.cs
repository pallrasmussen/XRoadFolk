namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppAntiforgery(this IServiceCollection services)
        {
            _ = services.AddAntiforgery(opts =>
            {
                bool isDev = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

                // In non-development, use __Host- cookie name and force Secure
                if (!isDev)
                {
                    opts.Cookie.Name = "__Host-AntiForgery";
                    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                }
                else
                {
                    // In dev/test, avoid __Host- prefix so cookie works over HTTP with TestServer
                    opts.Cookie.Name = "AntiForgery";
                    opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                }

                opts.Cookie.Path = "/";
                opts.Cookie.SameSite = SameSiteMode.Lax; // safe and allows top-level navigations
                // Keep HttpOnly default (true) to prevent JS access
                opts.HeaderName = "RequestVerificationToken";
            });
            return services;
        }
    }
}
