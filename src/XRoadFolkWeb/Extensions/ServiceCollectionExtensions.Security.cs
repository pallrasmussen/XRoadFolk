using Microsoft.AspNetCore.Mvc;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppAntiforgery(this IServiceCollection services)
    {
        services.AddAntiforgery(opts =>
        {
            opts.Cookie.Name = "__Host.AntiForgery";
            opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            opts.Cookie.SameSite = SameSiteMode.Lax;
            opts.HeaderName = "RequestVerificationToken";
        });
        return services;
    }
}
