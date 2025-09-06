using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace XRoadFolkWeb.Infrastructure
{
    public sealed class HttpLogsWritableHealthCheck : IHealthCheck
    {
        private readonly HttpLogOptions _opts;

        public HttpLogsWritableHealthCheck(IOptions<HttpLogOptions> opts)
        {
            _opts = opts.Value;
        }

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
