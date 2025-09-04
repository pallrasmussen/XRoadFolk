namespace XRoadFolkWeb.Shared
{
    public static class ProgramStatics
    {
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
            // WebAssembly text formats (do not include application/wasm as it's already compressed)
            // Fonts (woff2 is already compressed; avoid double compression)
            "font/ttf",
            "font/otf"
        ];
    }
}
