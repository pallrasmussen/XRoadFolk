using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace XRoadFolkRaw.Lib
{
    public static class LoggingHelper
    {
        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }

        public static IDisposable BeginCorrelationScope(ILogger logger, string? correlationId = null)
        {
            ArgumentNullException.ThrowIfNull(logger);

            string id = !string.IsNullOrWhiteSpace(correlationId)
                ? correlationId!
                : (Activity.Current?.Id ?? Guid.NewGuid().ToString("N"));

            try
            {
                // Use lightweight key/value state for better compatibility with logging providers
                var state = new[] { new KeyValuePair<string, object?>("correlationId", id) } as IEnumerable<KeyValuePair<string, object?>>;
                return logger.BeginScope(state) ?? NoopDisposable.Instance;
            }
            catch
            {
                // Never throw from logging helpers
                return NoopDisposable.Instance;
            }
        }

        public static string Mask(string? value, int visible = 4)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (visible <= 0)
            {
                return new string('*', value.Length);
            }

            int vis = Math.Min(visible, value.Length);
            return string.Concat(new string('*', value.Length - vis), value.AsSpan(value.Length - vis, vis));
        }
    }
}
