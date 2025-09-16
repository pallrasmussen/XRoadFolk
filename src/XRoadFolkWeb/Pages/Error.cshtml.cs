using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace XRoadFolkWeb.Pages
{
    /// <summary>
    /// Generic error page model that derives title and message from the current status code.
    /// </summary>
    public class ErrorModel : PageModel
    {
        /// <summary>Trace identifier to correlate the error.</summary>
        public string? RequestId { get; set; }
        /// <summary>True if a RequestId is available.</summary>
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
        /// <summary>Optional status code to render (falls back to current response status).</summary>
        public new int? StatusCode { get; set; }

        /// <summary>Error title and message shown on the page.</summary>
        public string Title { get; private set; } = "An error occurred";
        public string? Message { get; private set; }

        /// <summary>
        /// Selects title/message based on known status codes.
        /// </summary>
        public void OnGet()
        {
            int code = StatusCode ?? HttpContext?.Response?.StatusCode ?? 500;
            switch (code)
            {
                case 403:
                    Title = "Access denied";
                    Message = "You don't have permission to access this page.";
                    break;
                case 404:
                    Title = "Page not found";
                    Message = "The page you are looking for might have been removed or is temporarily unavailable.";
                    break;
                default:
                    Title = "An error occurred";
                    Message = null;
                    break;
            }
        }
    }
}
