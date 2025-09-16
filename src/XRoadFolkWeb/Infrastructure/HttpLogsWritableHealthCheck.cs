using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Health check that verifies the configured HttpLogs file is writable (if persistence is enabled).
    /// </summary>
    public sealed class HttpLogsWritableHealthCheck : IHealthCheck
    {
        private readonly HttpLogOptions _opts;

        public HttpLogsWritableHealthCheck(IOptions<HttpLogOptions> opts)
        {
            ArgumentNullException.ThrowIfNull(opts);
            _opts = opts.Value;
        }

        /// <summary>
        /// Checks the health of the HttpLogs file configuration.
        /// </summary>
        /// <param name="context">The health check context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous health check operation. The task result contains the health check result.</returns>
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (!_opts.PersistToFile || string.IsNullOrWhiteSpace(_opts.FilePath))
            {
                return HealthCheckResult.Healthy("HttpLogs persistence disabled");
            }

            try
            {
                string path = _opts.FilePath!;
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }
#pragma warning disable MA0004 // await using cannot use ConfigureAwait
                await using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
#pragma warning restore MA0004
                    await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                return HealthCheckResult.Healthy($"Writable: {path}");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Cannot write HttpLogs file", ex);
            }
        }
    }
}
