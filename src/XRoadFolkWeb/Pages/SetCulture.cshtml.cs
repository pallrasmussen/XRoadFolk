using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace XRoadFolkWeb.Pages;

public class SetCultureModel : PageModel
{
    public IActionResult OnPost([FromForm] string culture, [FromForm] string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(culture))
            culture = CultureInfo.CurrentUICulture.Name;

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });

        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
            returnUrl = Url.Content("~/");

        return LocalRedirect(returnUrl);
    }
}