namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Application-level logging options that tweak masking and sinks.
    /// </summary>
    public sealed class LoggingOptions
    {
        /// <summary>
        /// When true, mask tokens/identifiers in application and HTTP log sinks.
        /// </summary>
        public bool MaskTokens { get; set; } = true;
    }
}
