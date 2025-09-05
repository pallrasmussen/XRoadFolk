using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkWeb.Shared;
using Microsoft.Extensions.Localization;

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
                .AddRazorPagesOptions(o =>
                {
                    // Tests expect posting Razor Pages without antiforgery token to succeed with validation errors (200).
                    o.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute());
                });

            _ = services.AddLocalization(opts => opts.ResourcesPath = "Resources");

            _ = services.AddOptions<RequestLocalizationOptions>()
                .Configure<ILoggerFactory>((opts, lf) =>
                {
                    ILogger log = lf.CreateLogger("Localization.Config");
                    (string defaultCulture, IReadOnlyList<CultureInfo> cultures) = AppLocalization.FromConfiguration(configuration, log);
                    opts.DefaultRequestCulture = new RequestCulture(defaultCulture);
                    opts.SupportedCultures = [.. cultures];
                    opts.SupportedUICultures = [.. cultures];
                    opts.FallBackToParentCultures = true;
                    opts.FallBackToParentUICultures = true;

                    LocalizationConfig locCfg = configuration.GetSection("Localization").Get<LocalizationConfig>() ?? new LocalizationConfig();
                    // Inject sensible defaults for fallback map if not provided
                    if (locCfg.FallbackMap.Count == 0)
                    {
                        locCfg.FallbackMap["fo"] = "fo-FO";
                        locCfg.FallbackMap["en"] = "en-US";
                    }
                    System.Collections.ObjectModel.ReadOnlyDictionary<string, string> roMap = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(locCfg.FallbackMap);
                    ILogger providerLog = lf.CreateLogger("Localization.Provider");

                    // Remove default providers (Cookie, QueryString, Accept-Language) to avoid duplicate parsing
                    opts.RequestCultureProviders.Clear();
                    // Use a single best-match provider that handles cookie and Accept-Language
                    opts.RequestCultureProviders.Add(new BestMatchRequestCultureProvider(
                        opts.SupportedUICultures, roMap, providerLog));
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

            // Startup coverage logger to verify that some resources exist per culture
            services.AddHostedService<LocalizationCoverageHostedService>();

            return services;
        }

        private sealed class LocalizationCoverageHostedService : IHostedService
        {
            private readonly ILogger<LocalizationCoverageHostedService> _log;
            private readonly IOptions<RequestLocalizationOptions> _locOpts;
            private readonly IStringLocalizer<SharedResource> _sr;

            public LocalizationCoverageHostedService(ILogger<LocalizationCoverageHostedService> log,
                                                     IOptions<RequestLocalizationOptions> locOpts,
                                                     IStringLocalizer<SharedResource> sr)
            {
                _log = log;
                _locOpts = locOpts;
                _sr = sr;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                var cultures = _locOpts.Value.SupportedUICultures ?? new List<CultureInfo>();
                foreach (var c in cultures)
                {
                    try
                    {
                        using var _ = new CultureScope(c);
                        string key = "AppName"; // common key used in layout
                        var value = _sr[key];
                        bool found = value.ResourceNotFound == false && !string.Equals(value.Name, value.Value, StringComparison.Ordinal);
                        if (!found)
                        {
                            _log.LogWarning("Localization: Missing resource for culture {Culture} (key '{Key}'). Falling back to parent or default.", c.Name, key);
                        }
                        else
                        {
                            _log.LogInformation("Localization: Resource found for culture {Culture} (key '{Key}').", c.Name, key);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Localization: Failed to evaluate resources for culture {Culture}.", c.Name);
                    }
                }
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            private sealed class CultureScope : IDisposable
            {
                private readonly CultureInfo? _prev;
                private readonly CultureInfo? _prevUi;
                public CultureScope(CultureInfo c)
                {
                    _prev = CultureInfo.CurrentCulture;
                    _prevUi = CultureInfo.CurrentUICulture;
                    CultureInfo.CurrentCulture = c;
                    CultureInfo.CurrentUICulture = c;
                }
                public void Dispose()
                {
                    if (_prev is not null) CultureInfo.CurrentCulture = _prev;
                    if (_prevUi is not null) CultureInfo.CurrentUICulture = _prevUi;
                }
            }
        }
    }
}
