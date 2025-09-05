using Microsoft.AspNetCore.Mvc;
using System.Linq;

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

        // Consolidated: register TrimDigitsModelBinderProvider once globally for all MVC endpoints
        _ = services.Configure<MvcOptions>(options =>
        {
            if (!options.ModelBinderProviders.OfType<Validation.TrimDigitsModelBinderProvider>().Any())
            {
                options.ModelBinderProviders.Insert(0, new Validation.TrimDigitsModelBinderProvider());
            }

            // Require antiforgery tokens by default in all environments
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        });

        return services;
    }
}
