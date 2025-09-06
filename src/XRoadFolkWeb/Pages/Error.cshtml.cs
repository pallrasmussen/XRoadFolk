using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace XRoadFolkWeb.Pages
{
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
        public new int? StatusCode { get; set; }

        public string Title { get; private set; } = "An error occurred";
        public string? Message { get; private set; }

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
