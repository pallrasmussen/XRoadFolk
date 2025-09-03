namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppAntiforgery(this IServiceCollection services)
        {
            _ = services.AddAntiforgery(opts =>
            {
                // __Host- cookies must be: name starting with __Host-, Secure, Path=/, and no Domain attribute
                opts.Cookie.Name = "__Host-AntiForgery";
                opts.Cookie.Path = "/";
                opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                opts.Cookie.SameSite = SameSiteMode.Strict; // tighten CSRF surface
                // Keep HttpOnly default (true) to prevent JS access
                opts.HeaderName = "RequestVerificationToken";
            });
            return services;
        }
    }
}
