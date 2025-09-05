namespace XRoadFolkWeb.Infrastructure
{
    public sealed class LoggingOptions
    {
        // When true, mask tokens/identifiers in application and HTTP log sinks
        public bool MaskTokens { get; set; } = true;
    }
}
