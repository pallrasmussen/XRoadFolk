using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Tries: exact match -> explicit map -> parent cultures -> same language in supported list.
    /// </summary>
    public sealed class BestMatchRequestCultureProvider(IEnumerable<CultureInfo> supportedCultures,
                                           IReadOnlyDictionary<string, string>? map = null) : RequestCultureProvider
    {
        private readonly HashSet<string> _supported = new(supportedCultures.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyDictionary<string, string> _map = map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            // 1) cookie takes precedence
            if (httpContext.Request.Cookies.TryGetValue(CookieRequestCultureProvider.DefaultCookieName, out string? cookieVal)
                && !string.IsNullOrWhiteSpace(cookieVal))
            {
                ProviderCultureResult? parsed = CookieRequestCultureProvider.ParseCookieValue(cookieVal);
                if (parsed is not null)
                {
                    string? fromUi = (parsed.UICultures is { Count: > 0 }) ? parsed.UICultures[0].Value : null;
                    string? fromCult = (parsed.Cultures is { Count: > 0 }) ? parsed.Cultures[0].Value : null;
                    string? candidateFromCookie = fromUi ?? fromCult;
                    if (!string.IsNullOrWhiteSpace(candidateFromCookie))
                    {
                        string? match = ResolveCandidate(candidateFromCookie!);
                        if (match is not null)
                        {
                            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(match, match));
                        }
                    }
                }
            }

            // 2) Accept-Language header with quality weights (q). q=0 means "not acceptable" and must be ignored (RFC 9110).
            string accept = httpContext.Request.Headers.AcceptLanguage.ToString();
            if (!string.IsNullOrWhiteSpace(accept))
            {
                List<(string Tag, double Q, int Index)> items = [];
                int idx = 0;
                foreach (string part in accept.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string seg = part.Trim();
                    if (seg.Length == 0) { idx++; continue; }

                    string tag = seg;
                    double q = 1.0; // default
                    int sc = seg.IndexOf(';', StringComparison.Ordinal);
                    if (sc >= 0)
                    {
                        tag = seg[..sc].Trim();
                        string paramStr = seg[(sc + 1)..];
                        foreach (string p in paramStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            int eq = p.IndexOf('=', StringComparison.Ordinal);
                            if (eq > 0)
                            {
                                string key = p[..eq].Trim();
                                string val = p[(eq + 1)..].Trim();
                                if (key.Equals("q", StringComparison.OrdinalIgnoreCase)
                                    && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double qv))
                                {
                                    q = Math.Clamp(qv, 0d, 1d);
                                    break;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(tag) && !string.Equals(tag, "*", StringComparison.Ordinal))
                    {
                        if (q <= 0d)
                        {
                            // q=0 means "not acceptable" per RFC 9110; skip
                            idx++;
                            continue;
                        }
                        items.Add((tag, q, idx));
                    }
                    idx++;
                }

                foreach ((string Tag, double Q, int Index) it in items.OrderByDescending(i => i.Q).ThenBy(i => i.Index))
                {
                    string? match = ResolveCandidate(it.Tag);
                    if (match is not null)
                    {
                        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(match, match));
                    }
                }
            }

            return Task.FromResult<ProviderCultureResult?>(null);
        }

        private string? ResolveCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            // Exact
            if (_supported.Contains(candidate))
            {
                return candidate;
            }

            // Mapping (e.g. "en" -> "en-US", "fo" -> "fo-FO")
            if (_map.TryGetValue(candidate, out string? mapped) && _supported.Contains(mapped))
            {
                return mapped;
            }

            // Parent chain (e.g. "da-DK" -> "da")
            try
            {
                CultureInfo ci = CultureInfo.GetCultureInfo(candidate);
                while (!string.IsNullOrEmpty(ci.Name))
                {
                    ci = ci.Parent;
                    if (string.IsNullOrEmpty(ci.Name))
                    {
                        break;
                    }

                    if (_supported.Contains(ci.Name))
                    {
                        return ci.Name;
                    }
                }
            }
            catch { /* ignore invalid culture names */ }

            // Same language (e.g. "en-GB" -> match any supported like "en-US")
            string lang = candidate.Split('-')[0];
            return _supported.FirstOrDefault(c =>
                c.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c, lang, StringComparison.OrdinalIgnoreCase));
        }
    }
}