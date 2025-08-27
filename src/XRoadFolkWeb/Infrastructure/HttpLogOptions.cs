namespace XRoadFolkWeb.Infrastructure
{
    public sealed class HttpLogOptions
    {
        // Maximum number of log entries kept in memory
        public int Capacity { get; set; } = 1000;

        // Optional write rate limiting (entries/second). 0 disables the limiter.
        public int MaxWritesPerSecond { get; set; } = 200;

        // When the limiter is active and rate is exceeded, still allow >= Warning
        public bool AlwaysAllowWarningsAndErrors { get; set; } = true;

        // Persistence to rolling file (optional)
        public bool PersistToFile { get; set; }
        public string? FilePath { get; set; }
        public long MaxFileBytes { get; set; } = 5_000_000; // 5 MB
        public int MaxRolls { get; set; } = 3; // keep N rolled files

        // Bounded channel queue size for file-backed writer (back-pressure)
        public int MaxQueue { get; set; } = 5000;

        // Flush interval (milliseconds) for batching writes to file
        public int FlushIntervalMs { get; set; } = 250;
    }
}
