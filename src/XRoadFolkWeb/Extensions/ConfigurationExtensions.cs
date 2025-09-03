using System.Reflection;

namespace XRoadFolkWeb.Extensions
{
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Load default X-Road settings from the XRoadFolkRaw.Lib embedded resource or adjacent file
        /// </summary>
        /// <param name="configuration"></param>
        public static void AddXRoadDefaultSettings(this ConfigurationManager configuration)
        {
            Assembly libAsm = typeof(XRoadFolkRaw.Lib.XRoadSettings).Assembly;
            string? resName = libAsm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(".Resources.appsettings.xroad.json", StringComparison.OrdinalIgnoreCase));

            if (resName is not null)
            {
                using Stream? s = libAsm.GetManifestResourceStream(resName);
                if (s is not null)
                {
                    _ = configuration.AddJsonStream(s);
                    return;
                }
            }

            // Fallback to file on disk. In single-file publish Assembly.Location may be empty; use AppContext.BaseDirectory.
            string? libDir = string.IsNullOrWhiteSpace(libAsm.Location)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(libAsm.Location);

            if (!string.IsNullOrWhiteSpace(libDir))
            {
                string jsonPath = Path.Combine(libDir, "Resources", "appsettings.xroad.json");
                if (File.Exists(jsonPath))
                {
                    _ = configuration.AddJsonFile(jsonPath, optional: true, reloadOnChange: false);
                }
            }
        }
    }
}
