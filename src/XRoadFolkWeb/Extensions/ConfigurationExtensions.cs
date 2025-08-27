using System.Reflection;
//using Microsoft.Extensions.Configuration;

namespace XRoadFolkWeb.Extensions
{
    public static class ConfigurationExtensions
    {
        // Load default X-Road settings from the XRoadFolkRaw.Lib embedded resource or adjacent file
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
                }
            }
            else
            {
                string? libDir = Path.GetDirectoryName(libAsm.Location);
                string? jsonPath = libDir is null ? null : Path.Combine(libDir, "Resources", "appsettings.xroad.json");
                if (jsonPath is not null && File.Exists(jsonPath))
                {
                    _ = configuration.AddJsonFile(jsonPath, optional: true, reloadOnChange: false);
                }
            }
        }
    }
}
