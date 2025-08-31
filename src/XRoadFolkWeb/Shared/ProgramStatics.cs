namespace XRoadFolkWeb.Shared
{
    public partial class Program
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
            "image/svg+xml"
        ];
    }
}
