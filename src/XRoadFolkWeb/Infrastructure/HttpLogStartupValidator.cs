using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Validates HttpLogs persistence path at startup.
    /// Ensures directory exists and attempts a write so failures are detected early, especially in Production.
    /// </summary>
    public sealed class HttpLogStartupValidator : IHostedService
    {
        private readonly IHostEnvironment _env;
        private readonly HttpLogOptions _opts;
        private readonly ILogger<HttpLogStartupValidator> _log;

        public HttpLogStartupValidator(IHostEnvironment env, IOptions<HttpLogOptions> opts, ILogger<HttpLogStartupValidator> log)
        {
            _env = env;
            _opts = opts.Value;
            _log = log;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_opts.PersistToFile || string.IsNullOrWhiteSpace(_opts.FilePath))
            {
                return Task.CompletedTask;
            }

            try
            {
                string path = _opts.FilePath!;
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Try to open or create the file to validate write permissions
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                fs.Flush();

                if (!_env.IsDevelopment())
                {
                    _log.LogInformation("HttpLogs: Using file persistence at '{Path}'. Directory exists and is writable.", path);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "HttpLogs: Unable to create or write to '{Path}'. File persistence may not work.", _opts.FilePath);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
