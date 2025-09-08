namespace XRoadFolkWeb.Shared
{
    public static class ProgramStatics
    {
        // Compressible MIME types; do NOT include already-compressed formats or streaming types (e.g., text/event-stream)
        internal static readonly string[] ResponseCompressionMimeTypes =
        [
            // Textual content
            "text/plain",
            "text/html",
            "text/css",
            "text/javascript",
            "application/javascript",
            "application/xhtml+xml",
            // JSON/ProblemDetails
            "application/json",
            "application/problem+json",
            // XML/SOAP
            "text/xml",
            "application/xml",
            "application/soap+xml",
            // Vector images
            "image/svg+xml",
            // Fonts (avoid woff2)
            "font/ttf",
            "font/otf",
            // NOTE: text/event-stream is intentionally excluded
        ];
    }
}
