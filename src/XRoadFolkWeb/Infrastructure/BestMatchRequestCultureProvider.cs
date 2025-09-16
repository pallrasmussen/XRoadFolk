using Microsoft.AspNetCore.Localization;
using System.Globalization;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Determines the best matching culture from cookie or Accept-Language headers.
    /// Tries: exact match -> explicit map -> parent cultures -> same language in supported list.
    /// </summary>
    public sealed class BestMatchRequestCultureProvider(IEnumerable<CultureInfo> supportedCultures,
                                           IReadOnlyDictionary<string, string>? map = null,
                                           ILogger? logger = null) : RequestCultureProvider
    {
        private readonly HashSet<string> _supported = new(supportedCultures.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyDictionary<string, string> _map = map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger? _log = logger;

        /// <summary>
        /// Returns culture from cookie (if valid), otherwise selects the best Accept-Language match.
        /// </summary>
        public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            ProviderCultureResult? cookie = TryGetFromCookie(httpContext);
            if (cookie is not null)
            {
                return Task.FromResult<ProviderCultureResult?>(cookie);
            }

            ProviderCultureResult? fromAccept = TryGetFromAcceptLanguage(httpContext);
            return Task.FromResult(fromAccept);
        }

        private ProviderCultureResult? TryGetFromCookie(HttpContext httpContext)
        {
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
                            return new ProviderCultureResult(match, match);
                        }
                    }
                }
            }
            return null;
        }

        private ProviderCultureResult? TryGetFromAcceptLanguage(HttpContext httpContext)
        {
            // Prefer typed headers; if unavailable, fallback to raw
            var typed = httpContext.Request.GetTypedHeaders();
            var langs = typed.AcceptLanguage;
            if (langs is null || langs.Count == 0)
            {
                StringValues raw = httpContext.Request.Headers.AcceptLanguage;
                if (StringValues.IsNullOrEmpty(raw))
                {
                    return null;
                }
                // Minimal fallback: first raw tag (before ';' / ',')
                foreach (string? v in raw)
                {
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        continue;
                    }
                    foreach (string seg in v.Split(','))
                    {
                        string tag = seg.Split(';')[0].Trim();
                        if (string.IsNullOrWhiteSpace(tag))
                        {
                            continue;
                        }
                        string? match = ResolveCandidate(tag);
                        if (match is not null)
                        {
                            return new ProviderCultureResult(match, match);
                        }
                    }
                }
                return null;
            }

            var ordered = langs
                .Select((h, idx) => new { Tag = h.Value.Value, Q = h.Quality ?? 1.0, Index = idx })
                .OrderByDescending(x => x.Q)
                .ThenBy(x => x.Index);

            foreach (var x in ordered)
            {
                if (string.IsNullOrWhiteSpace(x.Tag))
                {
                    continue;
                }
                string? match = ResolveCandidate(x.Tag);
                if (match is not null)
                {
                    return new ProviderCultureResult(match, match);
                }
            }
            return null;
        }

        private string? ResolveCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            candidate = candidate.Trim();

            string? exactOrMapped = TryExactOrMapped(candidate);
            if (exactOrMapped is not null)
            {
                return exactOrMapped;
            }

            string? byParent = TryParentChain(candidate);
            if (byParent is not null)
            {
                return byParent;
            }

            return TrySameLanguage(candidate);
        }

        private string? TryExactOrMapped(string candidate)
        {
            if (_supported.Contains(candidate))
            {
                return candidate;
            }
            if (_map.TryGetValue(candidate, out string? mapped) && _supported.Contains(mapped))
            {
                return mapped;
            }
            return null;
        }

        private string? TryParentChain(string candidate)
        {
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
            catch (CultureNotFoundException ex)
            {
                _log?.LogWarning(ex, "Invalid culture candidate received: {Candidate}", candidate);
            }
            return null;
        }

        private string? TrySameLanguage(string candidate)
        {
            ReadOnlySpan<char> span = candidate.AsSpan();
            int langLen = 0;
            foreach (char ch in span)
            {
                if (char.IsLetter(ch))
                {
                    langLen++;
                }
                else
                {
                    break;
                }
            }
            if (langLen == 0)
            {
                return null; // malformed like "-GB" -> no language subtag
            }

            foreach (string c in _supported)
            {
                if (c.Length == langLen)
                {
                    if (string.Compare(c, 0, candidate, 0, langLen, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return c;
                    }
                }
                else if (c.Length > langLen && c[langLen] == '-')
                {
                    if (string.Compare(c, 0, candidate, 0, langLen, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return c;
                    }
                }
            }
            return null;
        }
    }
}
