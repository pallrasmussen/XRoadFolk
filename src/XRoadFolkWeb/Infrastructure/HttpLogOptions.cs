using System.ComponentModel.DataAnnotations;

namespace XRoadFolkWeb.Infrastructure
{
    public sealed class HttpLogOptions
    {
        /// <summary>
        /// Maximum number of log entries kept in memory
        /// </summary>
        [Range(50, int.MaxValue)]
        public int Capacity { get; set; } = 1000;

        /// <summary>
        /// Optional write rate limiting (entries/second). 0 disables the limiter.
        /// </summary>
        [Range(0, int.MaxValue)]
        public int MaxWritesPerSecond { get; set; } = 200;

        /// <summary>
        /// When the limiter is active and rate is exceeded, still allow >= Warning
        /// </summary>
        public bool AlwaysAllowWarningsAndErrors { get; set; } = true;

        /// <summary>
        /// Persistence to rolling file (optional)
        /// </summary>
        public bool PersistToFile { get; set; }

        [MinLength(1)]
        public string? FilePath { get; set; }

        [Range(50_000, long.MaxValue)]
        public long MaxFileBytes { get; set; } = 5_000_000; // 5 MB

        [Range(1, int.MaxValue)]
        public int MaxRolls { get; set; } = 3; // keep N rolled files

        /// <summary>
        /// Bounded channel queue size for file-backed writer (back-pressure)
        /// </summary>
        [Range(100, int.MaxValue)]
        public int MaxQueue { get; set; } = 5000;

        /// <summary>
        /// Flush interval (milliseconds) for batching writes to file
        /// </summary>
        [Range(50, int.MaxValue)]
        public int FlushIntervalMs { get; set; } = 250;
    }
}
