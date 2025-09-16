namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// HTTP client/server options.
    /// </summary>
    public sealed class HttpOptions
    {
        /// <summary>
        /// When true, bypasses server certificate validation for outgoing HTTPS calls.
        /// Must be false outside Development.
        /// </summary>
        public bool BypassServerCertificateValidation { get; set; }
    }
}
