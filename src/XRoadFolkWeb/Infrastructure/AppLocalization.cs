using System.Globalization;
//using Microsoft.Extensions.Configuration;

namespace XRoadFolkWeb.Infrastructure
{
    internal static class AppLocalization
    {
        // Config section name
        public const string SectionName = "Localization";

        // Safe defaults if config is missing or incomplete
        private static readonly string[] FallbackCultureNames = ["fo-FO", "da-DK", "en-US"];

        public static (string DefaultCulture, IReadOnlyList<CultureInfo> Cultures) FromConfiguration(IConfiguration configuration)
        {
            IConfigurationSection section = configuration.GetSection(SectionName);
            string[] names = section.GetSection("SupportedCultures").Get<string[]>() ?? FallbackCultureNames;
            string defaultName = section.GetValue<string>("DefaultCulture");

            // Ensure default is present and valid
            if (string.IsNullOrWhiteSpace(defaultName) ||
                !names.Any(n => string.Equals(n, defaultName, StringComparison.OrdinalIgnoreCase)))
            {
                defaultName = names[0];
            }

            List<CultureInfo> cultures = [.. names.Select(n => new CultureInfo(n))];
            return (defaultName, cultures);
        }
    }
}