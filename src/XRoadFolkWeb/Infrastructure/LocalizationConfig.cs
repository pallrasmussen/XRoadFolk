using System.ComponentModel.DataAnnotations;

namespace XRoadFolkWeb.Infrastructure
{
    public sealed class LocalizationConfig
    {
        [Required]
        public string? DefaultCulture { get; set; }

        [MinLength(1)]
        public IList<string> SupportedCultures { get; set; } = [];

        /// <summary>
        /// Optional: map unsupported or neutral cultures to supported specific ones (e.g., "en" -> "en-US")
        /// </summary>
        public IDictionary<string, string> FallbackMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}