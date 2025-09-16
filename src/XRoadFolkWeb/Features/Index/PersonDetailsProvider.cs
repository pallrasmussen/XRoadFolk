using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Features.People;
using Microsoft.Extensions.Caching.Memory;

namespace XRoadFolkWeb.Features.Index
{
    /// <summary>
    /// Provides person details by calling GetPerson and caching the flattened response per PublicId.
    /// Also filters noisy keys and enforces an include allow-list derived from configuration.
    /// </summary>
    public sealed class PersonDetailsProvider(PeopleService service, PeopleResponseParser parser, IConfiguration config, IMemoryCache cache)
    {
        private readonly PeopleService _service = service;
        private readonly PeopleResponseParser _parser = parser;
        private readonly IConfiguration _config = config;
        private readonly IMemoryCache _cache = cache;

        private sealed class CachedPerson
        {
            public required string Xml { get; init; }
            public required string Pretty { get; init; }
            public required IReadOnlyList<(string Key, string Value)> Pairs { get; init; }
        }

        private static string CacheKey(string publicId) => $"pd|{publicId}";

        private int GetTtlSeconds()
        {
            try
            {
                int ttl = _config.GetValue<int>("Caching:PersonDetailsSeconds", 30);
                return Math.Max(5, ttl);
            }
            catch { return 30; }
        }

        /// <summary>
        /// Cache of allowed include keys for this scoped instance (request)
        /// </summary>
        private IReadOnlyList<string>? _allowedIncludeKeysCache;
        private IReadOnlyList<string> GetAllowedIncludeKeysFromConfig()
        {
            if (_allowedIncludeKeysCache is { Count: > 0 })
            {
                return _allowedIncludeKeysCache;
            }

            // Base keys (config-backed cache) + ensure core sections
            var baseKeys = IncludeConfigHelper.GetEnabledIncludeKeys(_config);
            HashSet<string> tmp = new(baseKeys, StringComparer.OrdinalIgnoreCase);
            _ = tmp.Add("Person");
            _ = tmp.Add("Names");
            _allowedIncludeKeysCache = [.. tmp];
            return _allowedIncludeKeysCache;
        }

        /// <summary>
        /// Gets person details for a given public id; returns filtered key/value pairs and raw/pretty XML.
        /// </summary>
        public async Task<(IReadOnlyList<(string Key, string Value)> Details, string Pretty, string Raw, string SelectedNameSuffix)> GetAsync(
            string publicId,
            Microsoft.Extensions.Localization.IStringLocalizer loc,
            IReadOnlyCollection<string>? allowedIncludeKeys = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(loc);

            // Fetch from cache or materialize
            CachedPerson? cached = await _cache.GetOrCreateAsync(CacheKey(publicId), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(GetTtlSeconds());
                string xmlFresh = await _service.GetPersonAsync(publicId, ct).ConfigureAwait(false);
                var pairsFresh = _parser.FlattenResponse(xmlFresh);
                string prettyFresh = _parser.PrettyFormatXml(xmlFresh);
                return new CachedPerson { Xml = xmlFresh, Pretty = prettyFresh, Pairs = pairsFresh };
            }).ConfigureAwait(false);

            if (cached is null)
            {
                string xmlFresh = await _service.GetPersonAsync(publicId, ct).ConfigureAwait(false);
                var pairsFresh = _parser.FlattenResponse(xmlFresh);
                string prettyFresh = _parser.PrettyFormatXml(xmlFresh);
                cached = new CachedPerson { Xml = xmlFresh, Pretty = prettyFresh, Pairs = pairsFresh };
                _cache.Set(CacheKey(publicId), cached, TimeSpan.FromSeconds(GetTtlSeconds()));
            }

            IReadOnlyList<(string Key, string Value)> pairs = cached.Pairs;

            List<(string Key, string Value)> filtered = [.. ApplyPrimaryFilter(pairs)];

            // Build an allow-list that includes all observed top-level segments from the response,
            // ensuring we don't hide any groups returned by GetPerson.
            HashSet<string> observedTopSegments = new(StringComparer.OrdinalIgnoreCase);
            foreach (var p in filtered)
            {
                if (string.IsNullOrWhiteSpace(p.Key))
                {
                    continue;
                }
                int dot = p.Key.IndexOf('.', StringComparison.Ordinal);
                string top = dot >= 0 ? p.Key[..dot] : p.Key;
                int bracket = top.IndexOf('[', StringComparison.Ordinal);
                if (bracket >= 0)
                {
                    top = top[..bracket];
                }
                if (!string.IsNullOrWhiteSpace(top))
                {
                    observedTopSegments.Add(top);
                }
            }

            // Include allow-list filter (use provided keys if present, else from cached configuration),
            // then union with observed top-level segments to permit everything present.
            IReadOnlyCollection<string> allowedBase = (allowedIncludeKeys?.Count > 0)
                ? allowedIncludeKeys
                : GetAllowedIncludeKeysFromConfig();

            HashSet<string> allowUnion = new(allowedBase, StringComparer.OrdinalIgnoreCase);
            foreach (var seg in observedTopSegments)
            {
                allowUnion.Add(seg);
            }

            filtered = [.. ApplyAllowedFilter(filtered, allowUnion)];

            string selectedNameSuffix = ComputeSelectedNameSuffix(filtered, loc);

            return (filtered.AsReadOnly(), cached.Pretty, cached.Xml, selectedNameSuffix);
        }

        private static IEnumerable<(string Key, string Value)> ApplyPrimaryFilter(IEnumerable<(string Key, string Value)> pairs)
        {
            foreach (var p in pairs)
            {
                if (string.IsNullOrEmpty(p.Key))
                {
                    yield return p;
                    continue;
                }

                string k = p.Key;
                bool isHeaderOrBody = k.StartsWith("requestheader", StringComparison.OrdinalIgnoreCase)
                    || k.StartsWith("requestbody", StringComparison.OrdinalIgnoreCase)
                    || k.Contains(".requestheader", StringComparison.OrdinalIgnoreCase)
                    || k.Contains(".requestbody", StringComparison.OrdinalIgnoreCase);

                if (isHeaderOrBody)
                {
                    continue;
                }

                string key = p.Key;
                int lastDot = key.LastIndexOf('.');
                string sub = lastDot >= 0 ? key[(lastDot + 1)..] : key;
                int bpos = sub.IndexOf('[', StringComparison.Ordinal);
                if (bpos >= 0)
                {
                    sub = sub[..bpos];
                }

                // Case-insensitive checks without allocating lowercase copies
                if (sub.Equals("id", StringComparison.OrdinalIgnoreCase)
                    || sub.Equals("fixed", StringComparison.OrdinalIgnoreCase)
                    || sub.Equals("authoritycode", StringComparison.OrdinalIgnoreCase)
                    || sub.Equals("personaddressid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return p;
            }
        }

        private static IEnumerable<(string Key, string Value)> ApplyAllowedFilter(IEnumerable<(string Key, string Value)> items, IReadOnlyCollection<string> allowedBase)
        {
            // Enforce core allow-list on caller-provided keys as well
            HashSet<string> allowed = new(allowedBase, StringComparer.OrdinalIgnoreCase)
            {
                "Person",
                "Names",
            };

            static bool Matches(string seg, string allowedKey)
            {
                return seg.Equals(allowedKey, StringComparison.OrdinalIgnoreCase)
                    || seg.StartsWith(allowedKey, StringComparison.OrdinalIgnoreCase)
                    || allowedKey.StartsWith(seg, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var p in items)
            {
                if (string.IsNullOrWhiteSpace(p.Key))
                {
                    continue;
                }

                foreach (string seg in Segments(p.Key))
                {
                    foreach (string a in allowed)
                    {
                        if (Matches(seg, a))
                        {
                            yield return p;
                            goto NextItem;
                        }
                    }
                }

            NextItem:;
            }
        }

        private static string ComputeSelectedNameSuffix(IReadOnlyList<(string Key, string Value)> filtered, Microsoft.Extensions.Localization.IStringLocalizer loc)
        {
            // Extract first and last names in a single pass to avoid duplicate scans
            string? first = null;
            string? last = null;
            foreach (var p in filtered)
            {
                if (first is null && p.Key.EndsWith(".FirstName", StringComparison.OrdinalIgnoreCase))
                {
                    first = p.Value;
                    if (last is not null)
                    {
                        break;
                    }
                }
                else if (last is null && p.Key.EndsWith(".LastName", StringComparison.OrdinalIgnoreCase))
                {
                    last = p.Value;
                    if (first is not null)
                    {
                        break;
                    }
                }
            }

            return (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                ? loc["SelectedNameSuffixFormat", string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)))]
                : string.Empty;
        }

        // Existing span-free iterator version (safe for yield)
        private static IEnumerable<string> Segments(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                yield break;
            }

            int start = 0;
            int n = key.Length;
            while (start < n)
            {
                int dot = key.IndexOf('.', start);
                int endExclusive = dot >= 0 ? dot : n;

                // Trim whitespace on both sides of the segment
                int i = start;
                int j = endExclusive - 1;
                while (i <= j && char.IsWhiteSpace(key[i]))
                {
                    i++;
                }
                while (j >= i && char.IsWhiteSpace(key[j]))
                {
                    j--;
                }

                if (i <= j)
                {
                    int bracket = key.IndexOf('[', i, j - i + 1);
                    int segEnd = (bracket >= 0 && bracket <= j) ? bracket : (j + 1);
                    if (segEnd > i)
                    {
                        yield return key.Substring(i, segEnd - i);
                    }
                }

                if (dot < 0)
                {
                    break;
                }
                start = dot + 1;
            }
        }
    }
}
