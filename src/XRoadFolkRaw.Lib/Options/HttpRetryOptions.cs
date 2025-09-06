using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace XRoadFolkRaw.Lib.Options
{
    public sealed class HttpRetryOptions
    {
        [Range(0, 10)]
        public int Attempts { get; set; } = 3;

        [Range(0, int.MaxValue)]
        public int BaseDelayMs { get; set; } = 200;

        [Range(0, int.MaxValue)]
        public int JitterMs { get; set; } = 250;

        [Range(1000, int.MaxValue)]
        public int TimeoutMs { get; set; } = 30000;

        [Range(1, 100)]
        public int BreakFailures { get; set; } = 5;

        [Range(1000, int.MaxValue)]
        public int BreakDurationMs { get; set; } = 30000;

        // Operation-specific overrides keyed by operation name (e.g., "GetPerson", "Default")
        public IDictionary<string, HttpRetryOverride> Operations { get; set; } = new Dictionary<string, HttpRetryOverride>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class HttpRetryOverride
    {
        public int? Attempts { get; set; }
        public int? BaseDelayMs { get; set; }
        public int? JitterMs { get; set; }
        public int? TimeoutMs { get; set; }
        public int? BreakFailures { get; set; }
        public int? BreakDurationMs { get; set; }
    }
}
