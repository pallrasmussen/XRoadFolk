using System.Globalization;

namespace XRoadFolkWeb.Infrastructure
{
    internal static class AppLocalization
    {
        // Centralized, immutable culture configuration
        public static readonly string[] CultureNames = ["fo-FO", "da-DK", "en-US"];
        public static string DefaultCulture => CultureNames[0];

        private static IReadOnlyList<CultureInfo>? _cultures;
        public static IReadOnlyList<CultureInfo> Cultures =>
            _cultures ??= CultureNames.Select(c => new CultureInfo(c)).ToList().AsReadOnly();
    }
}