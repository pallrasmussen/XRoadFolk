namespace XRoadFolkRaw.Lib.Options
{
    public sealed class TokenCacheOptions
    {
        /// <summary>
        /// Prefix added to the cache key used for token storage
        /// </summary>
        public string KeyPrefix { get; set; } = "folk-token|";

        /// <summary>
        /// Seconds of skew to refresh the token before provider expiry
        /// </summary>
        public int RefreshSkewSeconds { get; set; } = 60;

        /// <summary>
        /// Default TTL (seconds) to use if the provider does not specify an expiry
        /// </summary>
        public int DefaultTtlSeconds { get; set; } = 300;
    }
}
