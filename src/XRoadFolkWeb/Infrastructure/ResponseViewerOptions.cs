namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Options controlling the response viewer UI on the Index page (raw/pretty XML tabs).
    /// </summary>
    public sealed class ResponseViewerOptions
    {
        /// <summary>Show the raw XML tab.</summary>
        public bool ShowRawXml { get; set; } = true;
        /// <summary>Show the pretty-printed XML tab.</summary>
        public bool ShowPrettyXml { get; set; } = true;
    }
}
