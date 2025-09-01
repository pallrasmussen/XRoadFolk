namespace XRoadFolkWeb.Infrastructure
{
    public sealed class HttpLogOptions
    {
        /// <summary>
        /// Maximum number of log entries kept in memory
        /// </summary>
        public int Capacity { get; set; } = 1000;

        /// <summary>
        /// Optional write rate limiting (entries/second). 0 disables the limiter.
        /// </summary>
        public int MaxWritesPerSecond { get; set; } = 200;

        /// <summary>
        /// When the limiter is active and rate is exceeded, still allow >= Warning
        /// </summary>
        public bool AlwaysAllowWarningsAndErrors { get; set; } = true;

        /// <summary>
        /// Persistence to rolling file (optional)
        /// </summary>
        public bool PersistToFile { get; set; }
        public string? FilePath { get; set; }
        public long MaxFileBytes { get; set; } = 5_000_000; // 5 MB
        public int MaxRolls { get; set; } = 3; // keep N rolled files

        /// <summary>
        /// Bounded channel queue size for file-backed writer (back-pressure)
        /// </summary>
        public int MaxQueue { get; set; } = 5000;

        /// <summary>
        /// Flush interval (milliseconds) for batching writes to file
        /// </summary>
        public int FlushIntervalMs { get; set; } = 250;
    }
}
