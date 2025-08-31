
namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppAntiforgery(this IServiceCollection services)
        {
            _ = services.AddAntiforgery(opts =>
            {
                opts.Cookie.Name = "__Host.AntiForgery";
                opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                opts.Cookie.SameSite = SameSiteMode.Strict; // tighten CSRF surface
                opts.HeaderName = "RequestVerificationToken";
            });
            return services;
        }
    }
}
