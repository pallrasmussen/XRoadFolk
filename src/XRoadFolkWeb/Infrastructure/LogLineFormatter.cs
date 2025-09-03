using System.Globalization;

namespace XRoadFolkWeb.Infrastructure
{
    internal static class LogLineFormatter
    {
        public static string Sanitize(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
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

        public static string FormatLine(XRoadFolkWeb.Infrastructure.LogEntry e)
        {
            string msg = Sanitize(e.Message);
            string ex = Sanitize(e.Exception);
            return string.Create(CultureInfo.InvariantCulture, $"{e.Timestamp:O}\t{e.Level}\t{e.Kind}\t{e.Category}\t{e.EventId}\t{msg}\t{ex}");
        }
    }
}
