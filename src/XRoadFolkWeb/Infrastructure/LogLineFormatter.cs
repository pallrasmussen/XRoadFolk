using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XRoadFolkWeb.Infrastructure
{
    internal static class LogLineFormatter
    {
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        public static string Sanitize(string? s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            ReadOnlySpan<char> span = s.AsSpan();
            if (span.IndexOfAny('\r', '\n') < 0)
            {
                return s;
            }

            return string.Create(span.Length, s, static (dst, state) =>
            {
                ReadOnlySpan<char> src = state.AsSpan();
                for (int i = 0; i < src.Length; i++)
                {
                    char c = src[i];
                    dst[i] = (c is '\r' or '\n') ? ' ' : c;
                }
            });
        }

        public static string FormatLine(LogEntry e)
        {
            // Sanitize PII and secrets prior to formatting
            bool mask = _maskTokens;
            string msg = PiiSanitizer.Sanitize(e.Message, mask);
            string ex = PiiSanitizer.Sanitize(e.Exception, mask);
            msg = Sanitize(msg);
            ex = Sanitize(ex);

            var obj = new
            {
                timestamp = e.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                level = e.Level.ToString(),
                kind = e.Kind,
                category = e.Category,
                eventId = e.EventId,
                message = msg,
                exception = string.IsNullOrWhiteSpace(ex) ? null : ex,
                traceId = e.TraceId,
                spanId = e.SpanId,
                user = e.User,
                sessionId = e.SessionId,
                correlationId = e.CorrelationId,
            };
            return JsonSerializer.Serialize(obj, JsonOpts);
        }

        // cached flag set via Configure call at startup
        private static volatile bool _maskTokens = true;
        public static void Configure(bool maskTokens)
        {
            _maskTokens = maskTokens;
        }
    }
}
