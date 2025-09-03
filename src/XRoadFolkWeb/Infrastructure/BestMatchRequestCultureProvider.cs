using Microsoft.AspNetCore.Localization;
using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Tries: exact match -> explicit map -> parent cultures -> same language in supported list.
    /// </summary>
    public sealed class BestMatchRequestCultureProvider(IEnumerable<CultureInfo> supportedCultures,
                                           IReadOnlyDictionary<string, string>? map = null,
                                           ILogger? logger = null) : RequestCultureProvider
    {
        private readonly HashSet<string> _supported = new(supportedCultures.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyDictionary<string, string> _map = map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger? _log = logger;

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
            StringValues acceptValues = httpContext.Request.Headers.AcceptLanguage;
            if (!StringValues.IsNullOrEmpty(acceptValues))
            {
                // Lightweight candidate list storing slices into the header strings
                List<Candidate> items = [];
                int globalIndex = 0;

                for (int vi = 0; vi < acceptValues.Count; vi++)
                {
                    string src = acceptValues[vi]!;
                    ReadOnlySpan<char> span = src.AsSpan();
                    int i = 0;
                    while (i < span.Length)
                    {
                        int start = i;
                        int comma = span.Slice(i).IndexOf(',');
                        if (comma < 0)
                        {
                            i = span.Length; // last segment
                        }
                        else
                        {
                            i += comma + 1; // move past comma
                        }

                        // Current segment: [start, end)
                        ReadOnlySpan<char> seg = span.Slice(start, (comma < 0 ? span.Length : start + comma) - start);
                        // Trim WS
                        seg = Trim(seg);
                        if (seg.Length == 0) { globalIndex++; continue; }

                        double q = 1.0; // default
                        ReadOnlySpan<char> tag = seg;
                        int sc = seg.IndexOf(';');
                        if (sc >= 0)
                        {
                            tag = seg.Slice(0, sc);
                            ReadOnlySpan<char> paramStr = seg.Slice(sc + 1);
                            // parse params separated by ';'
                            int pj = 0;
                            while (pj < paramStr.Length)
                            {
                                int pstart = pj;
                                int psep = paramStr.Slice(pj).IndexOf(';');
                                if (psep < 0)
                                {
                                    pj = paramStr.Length;
                                }
                                else
                                {
                                    pj += psep + 1;
                                }
                                ReadOnlySpan<char> part = Trim(paramStr.Slice(pstart, (psep < 0 ? paramStr.Length : pstart + psep) - pstart));
                                if (part.Length == 0) continue;
                                int eq = part.IndexOf('=');
                                if (eq > 0)
                                {
                                    ReadOnlySpan<char> key = Trim(part.Slice(0, eq));
                                    ReadOnlySpan<char> val = Trim(part.Slice(eq + 1));
                                    if (key.Equals("q".AsSpan(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double qv))
                                        {
                                            q = Math.Clamp(qv, 0d, 1d);
                                        }
                                    }
                                }
                            }
                        }

                        tag = Trim(tag);
                        if (tag.Length > 0 && !tag.Equals("*".AsSpan(), StringComparison.Ordinal))
                        {
                            if (q > 0d)
                            {
                                // Compute absolute start index of tag within the source string
                                int segOffset = start; // start index of seg in src
                                int tagOffsetInSeg = seg.IndexOf(tag);
                                int tagStartInSrc = segOffset + (tagOffsetInSeg < 0 ? 0 : tagOffsetInSeg);
                                items.Add(new Candidate(src, tagStartInSrc, tag.Length, q, globalIndex));
                            }
                        }
                        globalIndex++;
                    }
                }

                // Process candidates in priority order: q desc, index asc (stable by manual selection)
                while (items.Count > 0)
                {
                    int best = 0;
                    for (int k = 1; k < items.Count; k++)
                    {
                        if (items[k].Q > items[best].Q || (items[k].Q == items[best].Q && items[k].Index < items[best].Index))
                        {
                            best = k;
                        }
                    }

                    Candidate cand = items[best];
                    items.RemoveAt(best);

                    string tag = cand.Source.Substring(cand.Start, cand.Length);
                    string? match = ResolveCandidate(tag);
                    if (match is not null)
                    {
                        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(match, match));
                    }
                }
            }

            return Task.FromResult<ProviderCultureResult?>(null);
        }

        private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
        {
            int i = 0;
            int j = s.Length - 1;
            while (i <= j && char.IsWhiteSpace(s[i])) i++;
            while (j >= i && char.IsWhiteSpace(s[j])) j--;
            return s.Slice(i, j - i + 1);
        }

        private readonly record struct Candidate(string Source, int Start, int Length, double Q, int Index);

        private string? ResolveCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            candidate = candidate.Trim();

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
            catch (CultureNotFoundException ex)
            {
                _log?.LogWarning(ex, "Invalid culture candidate received: {Candidate}", candidate);
            }

            // Same language (e.g. "en-GB" -> match any supported like "en-US"). Avoid string.Split allocations.
            ReadOnlySpan<char> span = candidate.AsSpan();
            int langLen = 0;
            foreach (char ch in span)
            {
                if (char.IsLetter(ch)) { langLen++; }
                else { break; }
            }
            if (langLen == 0)
            {
                return null; // malformed like "-GB" -> no language subtag
            }

            // Validate language subtag contains only letters (already ensured by scan)
            // Find a supported culture with the same language
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