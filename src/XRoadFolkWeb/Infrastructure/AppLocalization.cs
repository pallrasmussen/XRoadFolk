using System.Globalization;

namespace XRoadFolkWeb.Infrastructure
{
    internal static class AppLocalization
    {
        /// <summary>
        /// Config section name
        /// </summary>
        public const string SectionName = "Localization";

        /// <summary>
        /// Safe defaults if config is missing or incomplete
        /// </summary>
        private static readonly string[] FallbackCultureNames = ["fo-FO", "da-DK", "en-US"];

        public static (string DefaultCulture, IReadOnlyList<CultureInfo> Cultures) FromConfiguration(IConfiguration configuration)
        {
            IConfigurationSection section = configuration.GetSection(SectionName);

            // Read supported cultures; handle null or empty by falling back
            string[]? configured = section.GetSection("SupportedCultures").Get<string[]>();
            string[] names = (configured is null || configured.Length == 0)
                ? FallbackCultureNames
                : configured.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            if (names.Length == 0)
            {
                names = FallbackCultureNames;
            }

            string? defaultNameConfigured = section.GetValue<string>("DefaultCulture");
            string defaultName = (!string.IsNullOrWhiteSpace(defaultNameConfigured) &&
                                  names.Any(n => string.Equals(n, defaultNameConfigured, StringComparison.OrdinalIgnoreCase)))
                                 ? defaultNameConfigured!
                                 : names[0];

            // Build CultureInfo list defensively; skip invalid entries
            List<CultureInfo> cultures = new();
            foreach (string n in names)
            {
                try { cultures.Add(CultureInfo.GetCultureInfo(n)); }
                catch { /* skip invalid */ }
            }

            if (cultures.Count == 0)
            {
                // As a last resort, use fallbacks
                cultures = [.. FallbackCultureNames.Select(CultureInfo.GetCultureInfo)];
                defaultName = FallbackCultureNames[0];
            }

            return (defaultName, cultures);
        }
    }
}