using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace XRoadFolkRaw.Lib.Extensions
{
    internal sealed class XRoadPollyHandler : DelegatingHandler
    {
        private readonly IConfiguration _cfg;
        private readonly ILogger _log;

        public XRoadPollyHandler(IConfiguration cfg, ILoggerFactory lf)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _log = lf?.CreateLogger("XRoad.Http") ?? throw new ArgumentNullException(nameof(lf));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string op = request.Headers.TryGetValues("X-XRoad-Operation", out var values) ? (values.FirstOrDefault() ?? string.Empty) : string.Empty;
            string key = string.IsNullOrWhiteSpace(op) ? "Default" : op;

            int timeoutMs = _cfg.GetValue<int>($"Retry:Http:{key}:TimeoutMs", _cfg.GetValue<int>("Retry:Http:TimeoutMs", 30000));
            int attempts = _cfg.GetValue<int>($"Retry:Http:{key}:Attempts", _cfg.GetValue<int>("Retry:Http:Attempts", 3));
            int baseDelayMs = _cfg.GetValue<int>($"Retry:Http:{key}:BaseDelayMs", _cfg.GetValue<int>("Retry:Http:BaseDelayMs", 200));
            int jitterMs = _cfg.GetValue<int>($"Retry:Http:{key}:JitterMs", _cfg.GetValue<int>("Retry:Http:JitterMs", 250));
            int breakFailures = _cfg.GetValue<int>($"Retry:Http:{key}:BreakFailures", _cfg.GetValue<int>("Retry:Http:BreakFailures", 5));
            int breakDurationMs = _cfg.GetValue<int>($"Retry:Http:{key}:BreakDurationMs", _cfg.GetValue<int>("Retry:Http:BreakDurationMs", 30000));

            attempts = Math.Clamp(attempts, 0, 10);
            timeoutMs = Math.Max(1000, timeoutMs);
            baseDelayMs = Math.Max(0, baseDelayMs);
            jitterMs = Math.Max(0, jitterMs);
            breakFailures = Math.Clamp(breakFailures, 1, 100);
            breakDurationMs = Math.Max(1000, breakDurationMs);

            var jitterer = new Random();
            IAsyncPolicy<HttpResponseMessage> retry = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .OrResult(r => (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(attempts,
                    i => TimeSpan.FromMilliseconds((baseDelayMs * (1 << (i - 1))) + jitterer.Next(0, jitterMs)),
                    (outcome, delay, i, _) => _log.LogWarning("XRoad HTTP retry {Attempt} after {Delay}ms for {Operation}", i, delay.TotalMilliseconds, key));

            IAsyncPolicy<HttpResponseMessage> breaker = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .OrResult(r => (int)r.StatusCode >= 500)
                .CircuitBreakerAsync(breakFailures, TimeSpan.FromMilliseconds(breakDurationMs),
                    (outcome, ts) => _log.LogWarning(outcome.Exception, "XRoad HTTP circuit opened for {Operation} during {Duration}ms", key, ts.TotalMilliseconds),
                    () => _log.LogInformation("XRoad HTTP circuit reset for {Operation}", key));

            IAsyncPolicy<HttpResponseMessage> timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMilliseconds(timeoutMs));

            IAsyncPolicy<HttpResponseMessage> pipeline = Policy.WrapAsync(timeout, breaker, retry);

            return pipeline.ExecuteAsync(ct => base.SendAsync(request, ct), cancellationToken);
        }
    }
}
