using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Antiforgery;
using System.Text.Encodings.Web;
using XRoadFolkWeb.Shared;

namespace XRoadFolkWeb.TagHelpers
{
    [HtmlTargetElement("theme-culture-switcher", TagStructure = TagStructure.NormalOrSelfClosing)]
    public sealed class ThemeCultureSwitcherTagHelper : TagHelper
    {
        private readonly IStringLocalizer<SharedResource> _loc;
        private readonly IHttpContextAccessor _http;
        private readonly IConfiguration _cfg;
        private readonly IOptions<RequestLocalizationOptions> _locOpts;
        private readonly IAntiforgery _af;

        public ThemeCultureSwitcherTagHelper(IStringLocalizer<SharedResource> loc,
            IHttpContextAccessor http,
            IConfiguration cfg,
            IOptions<RequestLocalizationOptions> locOpts,
            IAntiforgery af)
        {
            _loc = loc;
            _http = http;
            _cfg = cfg;
            _locOpts = locOpts;
            _af = af;
        }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            HttpContext? ctx = _http.HttpContext;
            var supported = _locOpts.Value.SupportedUICultures ?? new List<CultureInfo> { CultureInfo.GetCultureInfo("en-US") };
            string[] themeOptions = new [] { "flatly", "cerulean", "sandstone", "yeti" };

            string cookieTheme = ctx?.Request?.Cookies["site-theme"] ?? string.Empty;
            string qsTheme = (ctx?.Request?.Query["theme"].ToString() ?? string.Empty).Trim();
            string chosenTheme = themeOptions.FirstOrDefault(t => string.Equals(t, qsTheme, StringComparison.OrdinalIgnoreCase))
                              ?? themeOptions.FirstOrDefault(t => string.Equals(t, cookieTheme, StringComparison.OrdinalIgnoreCase))
                              ?? "flatly";

            string currentPathAndQuery = "/";
            if (ctx?.Request is { } reqPath)
            {
                try { currentPathAndQuery = (reqPath.Path.HasValue ? reqPath.Path.Value : "/") + reqPath.QueryString.ToUriComponent(); } catch { }
            }

            // Generate antiforgery hidden field
            string antiField = string.Empty;
            if (ctx is not null)
            {
                try
                {
                    var tokens = _af.GetAndStoreTokens(ctx);
                    string name = HtmlEncoder.Default.Encode(tokens.FormFieldName);
                    string value = HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty);
                    antiField = $"<input type='hidden' name='{name}' value='{value}' />";
                }
                catch { }
            }

            string BuildThemeHref(string t)
            {
                if (ctx?.Request is null)
                {
                    return "/?theme=" + Uri.EscapeDataString(t ?? "flatly");
                }
                var req = ctx.Request;
                var sb = new StringBuilder();
                sb.Append(req.Path.HasValue ? req.Path.Value : "/");
                bool first = true;
                foreach (var kv in req.Query)
                {
                    if (string.Equals(kv.Key, "theme", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    foreach (var v in kv.Value)
                    {
                        if (v is null)
                        {
                            continue;
                        }
                        sb.Append(first ? '?' : '&'); first = false;
                        sb.Append(Uri.EscapeDataString(kv.Key ?? string.Empty)).Append('=').Append(Uri.EscapeDataString(v));
                    }
                }
                sb.Append(first ? '?' : '&');
                sb.Append("theme=").Append(Uri.EscapeDataString(t ?? "flatly"));
                return sb.ToString();
            }

            string BuildReturnUrlSansTheme()
            {
                if (ctx?.Request is null)
                {
                    return "/";
                }
                var req = ctx.Request;
                var sb = new StringBuilder();
                sb.Append(req.Path.HasValue ? req.Path.Value : "/");
                bool first = true;
                foreach (var kv in req.Query)
                {
                    if (string.Equals(kv.Key, "theme", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    foreach (var v in kv.Value)
                    {
                        if (v is null)
                        {
                            continue;
                        }
                        sb.Append(first ? '?' : '&'); first = false;
                        sb.Append(Uri.EscapeDataString(kv.Key ?? string.Empty)).Append('=').Append(Uri.EscapeDataString(v));
                    }
                }
                return sb.ToString();
            }

            string BuildSetThemeHref(string t)
            {
                string ret = "/";
                try { ret = BuildReturnUrlSansTheme(); } catch { }
                return "/set-theme?theme=" + Uri.EscapeDataString(t ?? "flatly") + "&returnUrl=" + Uri.EscapeDataString(ret);
            }

            var culturesToShow = supported.GroupBy(ci => ci.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            string langText = _loc["Language"].ResourceNotFound ? "Language" : _loc["Language"].Value;
            string applyText = _loc["Apply"].ResourceNotFound ? "Apply" : _loc["Apply"].Value;

            var sbOut = new StringBuilder();
            sbOut.Append("<div class='d-flex align-items-center gap-2'>");
            sbOut.Append("<div class='dropdown'>");
            sbOut.Append("<button class='btn btn-sm btn-outline-light dropdown-toggle' type='button' id='themeMenu' data-bs-toggle='dropdown' aria-expanded='false'>").Append(_loc["Theme"].Value).Append("</button>");
            sbOut.Append("<ul class='dropdown-menu dropdown-menu-end' aria-labelledby='themeMenu'>");
            foreach (var t in themeOptions)
            {
                bool active = string.Equals(t, chosenTheme, StringComparison.OrdinalIgnoreCase);
                sbOut.Append("<li><a class='dropdown-item ").Append(active ? "active" : string.Empty).Append("' href='").Append(BuildSetThemeHref(t)).Append("'>").Append(t).Append("</a></li>");
            }
            sbOut.Append("<li><hr class='dropdown-divider' /></li>");
            sbOut.Append("<li class='dropdown-header'>").Append(_loc["PreviewNoSave"].Value).Append("</li>");
            foreach (var t in themeOptions)
            {
                sbOut.Append("<li><a class='dropdown-item' href='").Append(BuildThemeHref(t)).Append("'>").Append(t).Append(' ').Append(_loc["Preview"].Value).Append("</a></li>");
            }
            sbOut.Append("</ul></div>");
            sbOut.Append("<form class='d-flex align-items-center' method='post' action='/set-culture'>");
            sbOut.Append(antiField);
            sbOut.Append("<input type='hidden' name='returnUrl' value='").Append(System.Net.WebUtility.HtmlEncode(currentPathAndQuery)).Append("' />");
            sbOut.Append("<label id='culture-select-label' for='culture-select' class='visually-hidden'>").Append(langText).Append("</label>");
            sbOut.Append("<select id='culture-select' class='form-select form-select-sm me-2' name='culture' aria-labelledby='culture-select-label' aria-label='").Append(langText).Append("'>");
            foreach (var c in culturesToShow)
            {
                var loc = _loc[$"Culture_{c!.Name}"]; string label = loc.ResourceNotFound ? c.NativeName : loc.Value;
                bool selected = string.Equals(c.Name, CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase);
                sbOut.Append("<option value='").Append(c.Name).Append("'" );
                if (selected)
                {
                    sbOut.Append(" selected");
                }
                sbOut.Append(">" + System.Net.WebUtility.HtmlEncode($"{label} ({c.Name})") + "</option>");
            }
            sbOut.Append("</select><noscript><button type='submit' class='btn btn-sm btn-outline-light ms-2'>").Append(applyText).Append("</button></noscript></form>");
            sbOut.Append("</div>");

            output.TagName = null;
            output.Content.SetHtmlContent(sbOut.ToString());
        }
    }
}
