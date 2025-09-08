using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.AspNetCore.OutputCaching;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// MVC/Razor Pages specific customizations
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddMvcCustomizations(this IServiceCollection services)
    {
        // Keep controllers registration without per-call binder insertion
        _ = services.AddControllers();

        // Global MVC filters and model binders
        _ = services.Configure<MvcOptions>(options =>
        {
            if (!options.ModelBinderProviders.OfType<Validation.TrimDigitsModelBinderProvider>().Any())
            {
                options.ModelBinderProviders.Insert(0, new Validation.TrimDigitsModelBinderProvider());
            }

            // Require antiforgery tokens by default in all environments
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

            // Default response cache policy for pages (safe public cache)
            options.Filters.Add(new ResponseCacheAttribute
            {
                Duration = 60,
                Location = ResponseCacheLocation.Any,
                VaryByHeader = "Accept-Language"
            });
        });

        // Razor Pages conventions to override caching for Logs folder (no-store)
        _ = services.AddRazorPages(options =>
        {
            options.Conventions.AddFolderApplicationModelConvention("/Logs", m => m.Filters.Add(new ResponseCacheAttribute
            {
                NoStore = true,
                Location = ResponseCacheLocation.None
            }));
        });

        // ResponseCaching and (optional) OutputCache services
        _ = services.AddResponseCaching();
        _ = services.AddOutputCache(o =>
        {
            o.AddPolicy("PublicPages", b =>
            {
                b.Expire(TimeSpan.FromSeconds(60));
                b.SetVaryByRouteValue("page");
                b.SetVaryByHeader("Accept-Language");
                b.SetVaryByHeader("Accept-Encoding");
                b.SetVaryByQuery("v");
            });
            o.AddPolicy("NoCache", b => b.NoCache());
        });

        return services;
    }
}
