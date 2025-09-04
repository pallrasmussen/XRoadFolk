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

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (!_opts.PersistToFile || string.IsNullOrWhiteSpace(_opts.FilePath))
            {
                return Task.FromResult(HealthCheckResult.Healthy("HttpLogs persistence disabled"));
            }

            try
            {
                string path = _opts.FilePath!;
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                fs.Flush();
                return Task.FromResult(HealthCheckResult.Healthy($"Writable: {path}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Cannot write HttpLogs file", ex));
            }
        }
    }
}
