using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace XRoadFolkWeb.Infrastructure;

// Tries: exact match -> explicit map -> parent cultures -> same language in supported list.
public sealed class BestMatchRequestCultureProvider : RequestCultureProvider
{
    private readonly HashSet<string> _supported;
    private readonly IReadOnlyDictionary<string, string> _map;

    public BestMatchRequestCultureProvider(IEnumerable<CultureInfo> supportedCultures,
                                           IReadOnlyDictionary<string, string>? map = null)
    {
        _supported = new HashSet<string>(supportedCultures.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        _map = map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        string? candidate = null;

        // 1) cookie
        if (httpContext.Request.Cookies.TryGetValue(CookieRequestCultureProvider.DefaultCookieName, out string? cookieVal)
            && !string.IsNullOrWhiteSpace(cookieVal))
        {
            ProviderCultureResult? parsed = CookieRequestCultureProvider.ParseCookieValue(cookieVal);
            if (parsed is not null)
            {
                // ProviderCultureResult exposes StringSegment lists; use .Value to get string
                string? fromUi = (parsed.UICultures is { Count: > 0 }) ? parsed.UICultures[0].Value : null;
                string? fromCult = (parsed.Cultures is { Count: > 0 }) ? parsed.Cultures[0].Value : null;
                candidate = fromUi ?? fromCult;
            }
        }

        // 2) Accept-Language
        if (string.IsNullOrWhiteSpace(candidate))
        {
            string accept = httpContext.Request.Headers.AcceptLanguage.ToString();
            if (!string.IsNullOrWhiteSpace(accept))
            {
                candidate = accept.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(s => s.Split(';')[0].Trim())
                                  .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            }
        }

        if (string.IsNullOrWhiteSpace(candidate))
            return Task.FromResult<ProviderCultureResult?>(null);

        // Exact
        if (_supported.Contains(candidate))
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(candidate, candidate));

        // Mapping (e.g. "en" -> "en-US", "fo" -> "fo-FO")
        if (_map.TryGetValue(candidate, out string? mapped) && _supported.Contains(mapped))
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(mapped, mapped));

        // Parent chain (e.g. "da-DK" -> "da")
        try
        {
            CultureInfo ci = CultureInfo.GetCultureInfo(candidate);
            while (!string.IsNullOrEmpty(ci.Name))
            {
                ci = ci.Parent;
                if (string.IsNullOrEmpty(ci.Name)) break;
                if (_supported.Contains(ci.Name))
                    return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(ci.Name, ci.Name));
            }
        }
        catch { /* ignore invalid culture names */ }

        // Same language (e.g. "en-GB" -> match any supported like "en-US")
        string lang = candidate.Split('-')[0];
        string? match = _supported.FirstOrDefault(c =>
            c.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c, lang, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(match))
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(match, match));

        return Task.FromResult<ProviderCultureResult?>(null);
    }
}