namespace XRoadFolkWeb.Infrastructure;

public sealed class HttpLogOptions
{
    // Maximum number of log entries kept in memory
    public int Capacity { get; set; } = 1000;

    // Optional write rate limiting (entries/second). 0 disables the limiter.
    public int MaxWritesPerSecond { get; set; } = 200;

    // When the limiter is active and rate is exceeded, still allow >= Warning
    public bool AlwaysAllowWarningsAndErrors { get; set; } = true;

    // Persistence to rolling file (optional)
    public bool PersistToFile { get; set; } = false;
    public string? FilePath { get; set; }
    public long MaxFileBytes { get; set; } = 5_000_000; // 5 MB
    public int MaxRolls { get; set; } = 3; // keep N rolled files
}
