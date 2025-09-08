using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Reports Unhealthy until the configured readiness delay has elapsed since process start.
    /// Tag with "ready"; liveness probes should not use this.
    /// </summary>
    public sealed class ReadinessDelayedHealthCheck : IHealthCheck
    {
        private static readonly DateTimeOffset ProcessStartUtc = DateTimeOffset.UtcNow;
        private readonly HealthOptions _opts;

        public ReadinessDelayedHealthCheck(IOptions<HealthOptions> opts)
        {
            ArgumentNullException.ThrowIfNull(opts);
            _opts = opts.Value;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            int delay = Math.Max(0, _opts.ReadinessDelaySeconds);
            if (delay == 0)
            {
                return Task.FromResult(HealthCheckResult.Healthy("No readiness delay configured"));
            }
            double elapsed = (DateTimeOffset.UtcNow - ProcessStartUtc).TotalSeconds;
            if (elapsed < delay)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"Readiness delay not elapsed ({elapsed:F1}s < {delay}s)"));
            }
            return Task.FromResult(HealthCheckResult.Healthy("Ready (delay elapsed)"));
        }
    }
}
