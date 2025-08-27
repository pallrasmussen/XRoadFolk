using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Shared;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppLocalization(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddRazorPages()
            .AddViewLocalization()
            .AddDataAnnotationsLocalization(opts =>
            {
                opts.DataAnnotationLocalizerProvider = (type, factory) =>
                    factory.Create(typeof(SharedResource));
            })
            .AddMvcOptions(options =>
            {
                options.ModelBinderProviders.Insert(0, new XRoadFolkWeb.Validation.TrimDigitsModelBinderProvider());
            });

        services.AddLocalization(opts => opts.ResourcesPath = "Resources");

        services.Configure<RequestLocalizationOptions>(opts =>
        {
            (string defaultCulture, IReadOnlyList<CultureInfo> cultures) = AppLocalization.FromConfiguration(configuration);
            opts.DefaultRequestCulture = new RequestCulture(defaultCulture);
            opts.SupportedCultures = [.. cultures];
            opts.SupportedUICultures = [.. cultures];
            opts.FallBackToParentCultures = true;
            opts.FallBackToParentUICultures = true;

            LocalizationConfig locCfg = configuration.GetSection("Localization").Get<LocalizationConfig>() ?? new LocalizationConfig();
            opts.RequestCultureProviders.Insert(0, new XRoadFolkWeb.Infrastructure.BestMatchRequestCultureProvider(
                opts.SupportedUICultures, locCfg.FallbackMap));
        });

        // Configure and validate app Localization section
        services.AddOptions<LocalizationConfig>()
            .Bind(configuration.GetSection("Localization"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultCulture), "Localization: DefaultCulture is required.")
            .Validate(o => o.SupportedCultures is { Count: > 0 }, "Localization: SupportedCultures must have at least one value.")
            .Validate(o =>
            {
                try { _ = CultureInfo.GetCultureInfo(o.DefaultCulture!); }
                catch { return false; }
                foreach (string c in o.SupportedCultures)
                {
                    try { _ = CultureInfo.GetCultureInfo(c); }
                    catch { return false; }
                }
                return true;
            }, "Localization: One or more culture names are invalid.")
            .Validate(o => o.SupportedCultures.Contains(o.DefaultCulture!, StringComparer.OrdinalIgnoreCase),
                "Localization: DefaultCulture must be included in SupportedCultures.")
            .ValidateOnStart();

        return services;
    }
}
