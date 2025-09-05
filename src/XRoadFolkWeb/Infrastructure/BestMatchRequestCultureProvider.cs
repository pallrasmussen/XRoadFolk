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

        // Defensive bounds against pathological headers
        private const int MaxAcceptLanguageTotalChars = 2048;
        private const int MaxItems = 20;
        private const int MaxTagLength = 64; // practical upper bound for BCP47 tags

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
            StringValues acceptValues = httpContext.Request.Headers.AcceptLanguage;
            if (StringValues.IsNullOrEmpty(acceptValues))
            {
                return null;
            }

            List<Candidate> items = CollectAcceptLanguageCandidates(acceptValues);
            return SelectBestCandidate(items);
        }

        private List<Candidate> CollectAcceptLanguageCandidates(StringValues acceptValues)
        {
            List<Candidate> items = [];
            int globalIndex = 0;
            int processedChars = 0;

            for (int vi = 0; vi < acceptValues.Count && items.Count < MaxItems && processedChars <= MaxAcceptLanguageTotalChars; vi++)
            {
                string src = acceptValues[vi]!;
                processedChars += src.Length;
                ReadOnlySpan<char> span = src.AsSpan();
                int i = 0;
                while (i < span.Length && items.Count < MaxItems)
                {
                    ReadSegment(span, i, out ReadOnlySpan<char> seg, out int newIndex, out int start);
                    i = newIndex;

                    seg = Trim(seg);
                    if (seg.Length == 0)
                    {
                        globalIndex++;
                        continue;
                    }

                    ExtractTagAndQuality(seg, out ReadOnlySpan<char> tag, out double q);

                    tag = Trim(tag);
                    if (ShouldSkip(tag))
                    {
                        globalIndex++;
                        continue;
                    }

                    if (q > 0d)
                    {
                        int tagOffsetInSeg = seg.IndexOf(tag);
                        int tagStartInSrc = start + (tagOffsetInSeg < 0 ? 0 : tagOffsetInSeg);
                        items.Add(new Candidate(src, tagStartInSrc, tag.Length, q, globalIndex));
                    }
                    globalIndex++;
                }
            }
            return items;
        }

        private static void ReadSegment(ReadOnlySpan<char> span, int i, out ReadOnlySpan<char> seg, out int newIndex, out int start)
        {
            start = i;
            int comma = span.Slice(i).IndexOf(',');
            if (comma < 0)
            {
                i = span.Length; // last segment
            }
            else
            {
                i += comma + 1; // move past comma
            }
            seg = span.Slice(start, (comma < 0 ? span.Length : start + comma) - start);
            newIndex = i;
        }

        private static void ExtractTagAndQuality(ReadOnlySpan<char> seg, out ReadOnlySpan<char> tag, out double q)
        {
            q = 1.0; // default
            tag = seg;
            int sc = seg.IndexOf(';');
            if (sc >= 0)
            {
                tag = seg.Slice(0, sc);
                ReadOnlySpan<char> paramStr = seg.Slice(sc + 1);
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
                    if (part.Length == 0)
                    {
                        continue;
                    }
                    int eq = part.IndexOf('=');
                    if (eq > 0)
                    {
                        ReadOnlySpan<char> key = Trim(part.Slice(0, eq));
                        ReadOnlySpan<char> val = Trim(part.Slice(eq + 1));
                        if (key.Equals("q".AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            if (!TryParseQuality(val, out double qv))
                            {
                                q = 1.0; // treat invalid q as default weight
                            }
                            else
                            {
                                q = qv;
                            }
                        }
                    }
                }
            }
        }

        private static bool ShouldSkip(ReadOnlySpan<char> tag)
        {
            return tag.Length == 0 || tag.Length > MaxTagLength || tag.Equals("*".AsSpan(), StringComparison.Ordinal) || !IsValidTag(tag);
        }

        private ProviderCultureResult? SelectBestCandidate(List<Candidate> items)
        {
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
                    return new ProviderCultureResult(match, match);
                }
            }
            return null;
        }

        private static bool TryParseQuality(ReadOnlySpan<char> val, out double q)
        {
            // Strict qvalue parser per RFC: 0 or 1 or 0.xxx or 1.0 (up to 3 decimals)
            q = 1.0;
            if (val.Length == 0)
            {
                return false;
            }

            // Normalize by trimming quotes if any (robustness)
            if (val.Length >= 2 && ((val[0] == '"' && val[^1] == '"') || (val[0] == '\'' && val[^1] == '\'')))
            {
                val = val.Slice(1, val.Length - 2);
            }

            // Quick paths
            if (val.Length == 1 && (val[0] == '0' || val[0] == '1'))
            {
                q = val[0] - '0';
                return true;
            }

            int dot = val.IndexOf('.');
            if (dot < 0)
            {
                return false; // integers other than 0 or 1 are invalid
            }
            ReadOnlySpan<char> intPart = val.Slice(0, dot);
            ReadOnlySpan<char> frac = val.Slice(dot + 1);
            if (frac.Length == 0 || frac.Length > 3)
            {
                return false;
            }
            if (!(intPart.Length == 1 && (intPart[0] == '0' || intPart[0] == '1')))
            {
                return false;
            }
            for (int i = 0; i < frac.Length; i++)
            {
                if (frac[i] < '0' || frac[i] > '9')
                {
                    return false;
                }
            }
            // 1.x must be exactly 1.0, 1.00, or 1.000
            if (intPart[0] == '1')
            {
                for (int i = 0; i < frac.Length; i++)
                {
                    if (frac[i] != '0')
                    {
                        return false;
                    }
                }
            }
            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double qv))
            {
                q = Math.Clamp(qv, 0d, 1d);
                return true;
            }
            return false;
        }

        private static bool IsValidTag(ReadOnlySpan<char> tag)
        {
            // Basic BCP47 sanity: letters/digits/hyphen only; no leading/trailing hyphen; no consecutive hyphens
            if (tag.Length == 0)
            {
                return false;
            }
            if (tag[0] == '-' || tag[^1] == '-')
            {
                return false;
            }
            bool prevHyphen = false;
            for (int i = 0; i < tag.Length; i++)
            {
                char c = tag[i];
                bool isAlnum = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (c == '-')
                {
                    if (prevHyphen)
                    {
                        return false;
                    }
                    prevHyphen = true;
                }
                else if (!isAlnum)
                {
                    return false;
                }
                else
                {
                    prevHyphen = false;
                }
            }
            return true;
        }

        private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
        {
            int i = 0;
            int j = s.Length - 1;
            while (i <= j && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
            while (j >= i && char.IsWhiteSpace(s[j]))
            {
                j--;
            }
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