using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Extensions for reading boolean values from configuration with flexible parsing and defaults.
    /// </summary>
    public static class ConfigurationBoolExtensions
    {
        /// <summary>
        /// Tries to parse a boolean from configuration supporting common forms: true/false, 1/0, yes/no, on/off.
        /// Returns true on success with the parsed value in 'value'. Returns false if the key is absent or value is invalid.
        /// </summary>
        public static bool TryGetBool(this IConfiguration cfg, string key, out bool value)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            value = default;

            string? raw = cfg[key];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string v = raw.Trim();
            if (bool.TryParse(v, out bool b))
            {
                value = b;
                return true;
            }

            if (string.Equals(v, "1", StringComparison.Ordinal)) { value = true; return true; }
            if (string.Equals(v, "0", StringComparison.Ordinal)) { value = false; return true; }
            if (string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
            if (string.Equals(v, "no", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
            if (string.Equals(v, "on", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
            if (string.Equals(v, "off", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }

            return false;
        }

        /// <summary>
        /// Gets a boolean from configuration using TryGetBool semantics; returns 'default' when missing.
        /// Logs a warning when the value is present but invalid.
        /// </summary>
        public static bool GetBoolOrDefault(this IConfiguration cfg, string key, bool @default, ILogger? logger = null)
        {
            if (cfg.TryGetBool(key, out bool value))
            {
                return value;
            }

            string? raw = cfg[key];
            if (!string.IsNullOrWhiteSpace(raw))
            {
                logger?.LogWarning("Invalid boolean value for '{Key}': '{Value}'. Using default: {Default}.", key, raw, @default);
            }
            return @default;
        }
    }
}
