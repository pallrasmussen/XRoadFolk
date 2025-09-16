using System.Diagnostics;
using System.Globalization;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Helpers to read localization settings from configuration and build supported cultures.
    /// Provides safe fallbacks and logs invalid culture names.
    /// </summary>
    internal static partial class AppLocalization
    {
        /// <summary>
        /// Config section name.
        /// </summary>
        public const string SectionName = "Localization";

        /// <summary>
        /// Safe defaults if config is missing or incomplete.
        /// </summary>
        private static readonly string[] _fallbackCultureNames = ["fo-FO", "da-DK", "en-US"];

        /// <summary>
        /// Reads localization configuration and returns the default culture and the list of supported cultures.
        /// Falls back to a small set if configuration is missing or invalid.
        /// </summary>
        /// <param name="configuration">Application configuration root.</param>
        /// <param name="logger">Optional logger for warnings about invalid cultures.</param>
        public static (string DefaultCulture, IReadOnlyList<CultureInfo> Cultures) FromConfiguration(IConfiguration configuration, ILogger? logger = null)
        {
            IConfigurationSection section = configuration.GetSection(SectionName);

            // Read supported cultures; handle null or empty by falling back
            string[]? configured = section.GetSection("SupportedCultures").Get<string[]>();
            string[] names = (configured is null || configured.Length == 0)
                ? _fallbackCultureNames
                : [.. configured
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()),];

            if (names.Length == 0)
            {
                names = _fallbackCultureNames;
            }

            // Deduplicate in a case-insensitive, order-preserving manner
            List<string> deduped = new(names.Length);
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names)
            {
                if (seen.Add(n))
                {
                    deduped.Add(n);
                }
            }
            names = [.. deduped];

            string? defaultNameConfigured = section.GetValue<string>("DefaultCulture");
            string defaultName = (!string.IsNullOrWhiteSpace(defaultNameConfigured) &&
                                  names.Any(n => string.Equals(n, defaultNameConfigured, StringComparison.OrdinalIgnoreCase)))
                                 ? defaultNameConfigured!
                                 : names[0];

            // Build CultureInfo list defensively; report invalid entries
            List<CultureInfo> cultures = [];
            foreach (string n in names)
            {
                try { cultures.Add(CultureInfo.GetCultureInfo(n)); }
                catch (Exception ex)
                {
                    if (logger is not null) { LogInvalidCulture(logger, ex, n); }
                    else { Trace.TraceWarning("Invalid culture configured: {0}. Error: {1}", n, ex.Message); }
                }
            }

            if (cultures.Count == 0)
            {
                // As a last resort, use fallbacks
                if (logger is not null) { LogFallback(logger, string.Join(", ", _fallbackCultureNames)); }
                else { Trace.TraceWarning("No valid cultures configured. Falling back to defaults: {0}", string.Join(", ", _fallbackCultureNames)); }
                cultures = [.. _fallbackCultureNames.Select(CultureInfo.GetCultureInfo)];
                defaultName = _fallbackCultureNames[0];
            }

            return (defaultName, cultures);
        }

        [LoggerMessage(EventId = 6201, Level = LogLevel.Warning, Message = "Invalid culture configured: {Culture}")]
        private static partial void LogInvalidCulture(ILogger logger, Exception ex, string Culture);

        [LoggerMessage(EventId = 6202, Level = LogLevel.Warning, Message = "No valid cultures configured. Falling back to defaults: {Fallback}")]
        private static partial void LogFallback(ILogger logger, string Fallback);
    }
}
