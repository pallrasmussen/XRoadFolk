using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using XRoadFolkWeb.Shared;
using XRoadFolkRaw.Lib.Extensions;

namespace XRoadFolkWeb.TagHelpers
{
    /// <summary>
    /// Renders a top-of-page bootstrap alert about the client certificate state (missing/expired/expiring soon).
    /// Text is localized via <see cref="IStringLocalizer{SharedResource}"/>.
    /// </summary>
    [HtmlTargetElement("certificate-banner", TagStructure = TagStructure.NormalOrSelfClosing)]
    public sealed class CertificateBannerTagHelper : TagHelper
    {
        private readonly IStringLocalizer<SharedResource> _loc;
        private readonly ICertificateExpiryInfo _info;

        public CertificateBannerTagHelper(IStringLocalizer<SharedResource> loc, ICertificateExpiryInfo info)
        {
            _loc = loc;
            _info = info;
        }

        /// <summary>
        /// Emits a bootstrap alert when the certificate is expired, expiring soon, or missing.
        /// Suppresses output when the certificate is healthy.
        /// </summary>
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

            if (!(_info.IsExpired || _info.IsExpiringSoon || !_info.HasCertificate))
            {
                output.SuppressOutput();
                return;
            }

            string cls = _info.IsExpired ? "alert-danger" : (_info.IsExpiringSoon ? "alert-warning" : "alert-secondary");
            string msg;
            if (!_info.HasCertificate)
            {
                msg = _loc["ClientCertificateMissing"].ResourceNotFound ? "Client certificate is not configured." : _loc["ClientCertificateMissing"].Value;
            }
            else if (_info.IsExpired)
            {
                msg = (_loc["ClientCertificateExpired"].ResourceNotFound ? "Client certificate has expired." : _loc["ClientCertificateExpired"].Value) + (_info.Subject is null ? string.Empty : $" ({_info.Subject})");
            }
            else
            {
                string daysText = _info.DaysRemaining?.ToString("F1", CultureInfo.InvariantCulture) ?? "?";
                msg = (_loc["ClientCertificateExpiringSoon"].ResourceNotFound ? "Client certificate will expire soon" : _loc["ClientCertificateExpiringSoon"].Value) + $" ({daysText} days remaining)" + (_info.Subject is null ? string.Empty : $" ({_info.Subject})");
            }

            output.TagName = "div";
            output.Attributes.SetAttribute("class", $"alert {cls} rounded-0 mb-0 py-2 small text-center");
            output.Attributes.SetAttribute("role", "alert");
            output.Content.SetHtmlContent($"<i class='bi bi-exclamation-triangle me-1' aria-hidden='true'></i>{System.Net.WebUtility.HtmlEncode(msg)}");
        }
    }
}
