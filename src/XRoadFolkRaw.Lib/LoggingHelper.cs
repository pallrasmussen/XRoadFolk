
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace XRoadFolkRaw
{
    public static class LoggingHelper
    {
        public static IDisposable BeginCorrelationScope(ILogger logger, string? correlationId = null)
        {
            var id = !string.IsNullOrWhiteSpace(correlationId) ? correlationId :
                     (Activity.Current?.Id ?? Guid.NewGuid().ToString("N"));
            return logger.BeginScope(new Dictionary<string, object> { ["correlationId"] = id });
        }

        public static string Mask(string? value, int visible = 4)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (visible <= 0) return new string('*', value.Length);
            var vis = Math.Min(visible, value.Length);
            return new string('*', value.Length - vis) + value.Substring(value.Length - vis, vis);
        }
    }
}
