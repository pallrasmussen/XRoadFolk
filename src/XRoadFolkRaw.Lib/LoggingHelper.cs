
//using System;
//using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace XRoadFolkRaw.Lib
{
    public static class LoggingHelper
    {
        public static IDisposable BeginCorrelationScope(ILogger logger, string? correlationId = null)
        {
            ArgumentNullException.ThrowIfNull(logger);

            string id = !string.IsNullOrWhiteSpace(correlationId) ? correlationId :
                     (Activity.Current?.Id ?? Guid.NewGuid().ToString("N"));
            return logger.BeginScope(new Dictionary<string, object> { ["correlationId"] = id })
                   ?? throw new InvalidOperationException("BeginScope returned null.");
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
