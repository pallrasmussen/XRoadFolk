using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using XRoadFolkRaw.Lib.Options;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace XRoadFolkRaw.Lib.Extensions
{
    internal sealed class XRoadPollyHandler : DelegatingHandler
    {
        private readonly HttpRetryOptions _opts;
        private readonly ILogger _log;

        public XRoadPollyHandler(IOptions<HttpRetryOptions> opts, ILoggerFactory lf)
        {
            _opts = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
            _log = lf?.CreateLogger("XRoad.Http") ?? throw new ArgumentNullException(nameof(lf));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }

            string op = request.Headers.TryGetValues("X-XRoad-Operation", out var values) ? (values.FirstOrDefault() ?? string.Empty) : string.Empty;
            var eff = ResolveEffective(op);

            int attempts = Math.Clamp(eff.Attempts, 0, 10);
            int timeoutMs = Math.Max(1000, eff.TimeoutMs);
            int baseDelayMs = Math.Max(0, eff.BaseDelayMs);
            int jitterMs = Math.Max(0, eff.JitterMs);
            int breakFailures = Math.Clamp(eff.BreakFailures, 1, 100);
            int breakDurationMs = Math.Max(1000, eff.BreakDurationMs);

            var jitterer = new Random();
            IAsyncPolicy<HttpResponseMessage> retry = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                .OrResult(r => (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(attempts,
                    i => TimeSpan.FromMilliseconds((baseDelayMs * (1 << (i - 1))) + jitterer.Next(0, jitterMs)),
                    (outcome, delay, i, _) => _log.LogWarning("XRoad HTTP retry {Attempt} after {Delay}ms for {Operation} (TimeoutMs={TimeoutMs}, Attempts={Attempts})", i, delay.TotalMilliseconds, op, timeoutMs, attempts));

            IAsyncPolicy<HttpResponseMessage> breaker = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                .OrResult(r => (int)r.StatusCode >= 500)
                .CircuitBreakerAsync(breakFailures, TimeSpan.FromMilliseconds(breakDurationMs),
                    (outcome, ts) => _log.LogWarning(outcome.Exception, "XRoad HTTP circuit opened for {Operation} during {Duration}ms", op, ts.TotalMilliseconds),
                    () => _log.LogInformation("XRoad HTTP circuit reset for {Operation}", op));

            IAsyncPolicy<HttpResponseMessage> timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMilliseconds(timeoutMs), TimeoutStrategy.Pessimistic);

            IAsyncPolicy<HttpResponseMessage> pipeline = Policy.WrapAsync(timeout, breaker, retry);

            return pipeline.ExecuteAsync(ct => base.SendAsync(request, ct), cancellationToken);
        }

        private HttpRetryOptions ResolveEffective(string? op)
        {
            if (string.IsNullOrWhiteSpace(op))
            {
                return _opts;
            }
            if (_opts.Operations is null)
            {
                return _opts;
            }
            if (!_opts.Operations.TryGetValue(op!, out var ov) && !_opts.Operations.TryGetValue("Default", out ov))
            {
                return _opts;
            }

            return new HttpRetryOptions
            {
                Attempts = ov.Attempts ?? _opts.Attempts,
                BaseDelayMs = ov.BaseDelayMs ?? _opts.BaseDelayMs,
                JitterMs = ov.JitterMs ?? _opts.JitterMs,
                TimeoutMs = ov.TimeoutMs ?? _opts.TimeoutMs,
                BreakFailures = ov.BreakFailures ?? _opts.BreakFailures,
                BreakDurationMs = ov.BreakDurationMs ?? _opts.BreakDurationMs,
                Operations = _opts.Operations,
            };
        }
    }
}
