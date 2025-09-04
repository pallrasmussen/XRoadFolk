using System.Diagnostics.Metrics;

namespace XRoadFolkRaw.Lib.Metrics;

internal static class XRoadRawMetrics
{
    public static readonly Meter Meter = new("XRoadFolkRaw");

    public static readonly Counter<long> HttpRetries = Meter.CreateCounter<long>(
        name: "xroad.http.retries",
        unit: "count",
        description: "Number of HTTP retries performed by FolkRawClient");

    public static readonly Histogram<double> HttpDurationMs = Meter.CreateHistogram<double>(
        name: "xroad.http.duration",
        unit: "ms",
        description: "HTTP call duration in milliseconds for SOAP operations");
}
