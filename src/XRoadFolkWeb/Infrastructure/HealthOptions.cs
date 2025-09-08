namespace XRoadFolkWeb.Infrastructure
{
    public sealed class HealthOptions
    {
        /// <summary>
        /// Optional delay (seconds) before the readiness endpoint reports healthy. Use to avoid premature restarts during JIT/warmup.
        /// Key: Health:ReadinessDelaySeconds
        /// </summary>
#pragma warning disable CA1805 // (analyzer false positive on auto-property w/out initializer)
        public int ReadinessDelaySeconds { get; set; }
#pragma warning restore CA1805
    }
}
