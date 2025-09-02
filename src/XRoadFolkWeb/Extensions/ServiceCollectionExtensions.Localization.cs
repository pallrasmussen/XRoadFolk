using Microsoft.AspNetCore.Localization;
using System.Globalization;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkWeb.Shared;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppLocalization(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            _ = services
                .AddRazorPages()
                .AddViewLocalization()
                .AddDataAnnotationsLocalization(opts =>
                {
                    opts.DataAnnotationLocalizerProvider = (_, factory) =>
                        factory.Create(typeof(SharedResource));
                })
                .AddMvcOptions(options => options.ModelBinderProviders.Insert(0, new Validation.TrimDigitsModelBinderProvider()));

            _ = services.AddLocalization(opts => opts.ResourcesPath = "Resources");

            _ = services.Configure((RequestLocalizationOptions opts) =>
            {
                (string defaultCulture, IReadOnlyList<CultureInfo> cultures) = AppLocalization.FromConfiguration(configuration);
                opts.DefaultRequestCulture = new RequestCulture(defaultCulture);
                opts.SupportedCultures = [.. cultures];
                opts.SupportedUICultures = [.. cultures];
                opts.FallBackToParentCultures = true;
                opts.FallBackToParentUICultures = true;

                LocalizationConfig locCfg = configuration.GetSection("Localization").Get<LocalizationConfig>() ?? new LocalizationConfig();
                System.Collections.ObjectModel.ReadOnlyDictionary<string, string> roMap = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(locCfg.FallbackMap);
                opts.RequestCultureProviders.Insert(0, new BestMatchRequestCultureProvider(
                    opts.SupportedUICultures, roMap));
            });

            // Configure and validate app Localization section
            _ = services.AddOptions<LocalizationConfig>()
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
}
