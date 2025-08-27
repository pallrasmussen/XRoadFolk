namespace XRoadFolkWeb.Infrastructure
{
    public sealed class LocalizationConfig
    {
        public string? DefaultCulture { get; set; }
        public List<string> SupportedCultures { get; set; } = [];

        // Optional: map unsupported or neutral cultures to supported specific ones (e.g., "en" -> "en-US")
        public Dictionary<string, string> FallbackMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}