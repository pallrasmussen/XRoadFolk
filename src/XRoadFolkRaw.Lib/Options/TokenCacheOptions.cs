namespace XRoadFolkRaw.Lib.Options
{
    public sealed class TokenCacheOptions
    {
        // Prefix added to the cache key used for token storage
        public string KeyPrefix { get; set; } = "folk-token|";

        // Seconds of skew to refresh the token before provider expiry
        public int RefreshSkewSeconds { get; set; } = 60;

        // Default TTL (seconds) to use if the provider does not specify an expiry
        public int DefaultTtlSeconds { get; set; } = 300;
    }
}
