using System.Globalization;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Helper to perform size-based log file rolling.
    /// Renames <c>path</c> to <c>path.1</c>, shifting existing suffixes up to <paramref name="maxRolls"/>.
    /// </summary>
    internal static class LogFileRolling
    {
        /// <summary>
        /// Rolls the file at <paramref name="path"/> when its size exceeds <paramref name="maxBytes"/>.
        /// </summary>
        /// <param name="path">Full path to the active log file.</param>
        /// <param name="maxBytes">Maximum allowed size in bytes before rolling.</param>
        /// <param name="maxRolls">How many rolled files to keep.</param>
        /// <param name="log">Optional logger for error reporting.</param>
        public static void RollIfNeeded(string path, long maxBytes, int maxRolls, ILogger? log = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                FileInfo fi = new(path);
                if (fi.Exists && fi.Length > maxBytes)
                {
                    for (int i = maxRolls; i >= 1; i--)
                    {
                        string from = i == 1 ? path : path + "." + (i - 1).ToString(CultureInfo.InvariantCulture);
                        string to = path + "." + i.ToString(CultureInfo.InvariantCulture);
                        if (File.Exists(to))
                        {
                            File.Delete(to);
                        }

                        if (File.Exists(from))
                        {
                            File.Move(from, to);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "Error rolling HTTP log files for path '{Path}'", path);
                // Swallow exceptions to avoid impacting request processing
            }
        }
    }
}
