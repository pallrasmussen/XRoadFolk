using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Decorator that sanitizes PII/secrets before forwarding to the inner log store.
    /// Ensures consistent masking for in-memory list, streaming, and file sinks.
    /// </summary>
    public sealed class MaskingHttpLogStore : IHttpLogStore
    {
        private readonly IHttpLogStore _inner;
        private readonly IHostEnvironment _env;
        private readonly IOptions<LoggingOptions> _opts;

        public MaskingHttpLogStore(IHttpLogStore inner, IOptions<LoggingOptions> opts, IHostEnvironment env)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public void Add(LogEntry e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            bool mask = !_env.IsDevelopment() || _opts.Value.MaskTokens; // always mask outside Development

            var sanitized = new LogEntry
            {
                Timestamp = e.Timestamp,
                Level = e.Level,
                Kind = e.Kind,
                Category = e.Category,
                EventId = e.EventId,
                Message = PiiSanitizer.Sanitize(e.Message, mask),
                Exception = PiiSanitizer.Sanitize(e.Exception, mask),
            };
            _inner.Add(sanitized);
        }

        public void Clear() => _inner.Clear();

        public IReadOnlyList<LogEntry> GetAll() => _inner.GetAll();
    }
}
